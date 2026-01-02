using System;
using System.Collections.Generic;
using System.Linq;

namespace OGNES.Components
{
    public enum ScanType
    {
        ExactValue,
        UnknownInitialValue,
        ValueChanged,
        ValueUnchanged,
        ValueIncreased,
        ValueDecreased
    }

    public enum CheatDataType
    {
        Byte,
        Int16
    }

    public enum MemoryRegion
    {
        InternalRam, // 0x0000 - 0x07FF
        Wram,        // 0x6000 - 0x7FFF
        All          // 0x0000 - 0xFFFF
    }

    public class Cheat
    {
        public bool Active { get; set; }
        public string Description { get; set; } = "No Description";
        public int Address { get; set; }
        public int Value { get; set; }
        public CheatDataType DataType { get; set; }
    }

    public class ScanResult
    {
        public int Address { get; set; }
        public int CurrentValue { get; set; }
        public int PreviousValue { get; set; }
    }

    public class CheatManager
    {
        private Memory _memory;
        private List<ScanResult> _scanResults = new();
        private List<Cheat> _activeCheats = new();
        private byte[] _previousMemorySnapshot = Array.Empty<byte>();
        
        // Scan State
        public bool IsFirstScan { get; private set; } = true;
        public int ResultCount => _scanResults.Count;
        public IEnumerable<ScanResult> Results => _scanResults;
        public List<Cheat> Cheats => _activeCheats;

        public CheatManager(Memory memory)
        {
            _memory = memory;
        }

        public void SetMemory(Memory memory)
        {
            _memory = memory;
            NewScan();
            _activeCheats.Clear(); // Clear cheats on new game? Probably yes.
        }

        public void Update()
        {
            if (_memory == null) return;
            foreach (var cheat in _activeCheats)
            {
                if (cheat.Active)
                {
                    ApplyCheat(cheat);
                }
            }
        }

        public void WriteMemory(int address, int value, CheatDataType dataType)
        {
            if (_memory == null) return;

            if (dataType == CheatDataType.Byte)
            {
                _memory.Write((ushort)address, (byte)value);
            }
            else if (dataType == CheatDataType.Int16)
            {
                ushort val = (ushort)value;
                _memory.Write((ushort)address, (byte)(val & 0xFF));
                _memory.Write((ushort)(address + 1), (byte)((val >> 8) & 0xFF));
            }
        }

        private void ApplyCheat(Cheat cheat)
        {
            WriteMemory(cheat.Address, cheat.Value, cheat.DataType);
        }

        public void NewScan()
        {
            _scanResults.Clear();
            IsFirstScan = true;
            _previousMemorySnapshot = Array.Empty<byte>();
        }

        public void Scan(MemoryRegion region, ScanType scanType, CheatDataType dataType, string valueStr)
        {
            if (_memory == null) return;

            int value = 0;
            bool valueProvided = int.TryParse(valueStr, out value);

            // If exact value scan, we need a value
            if (scanType == ScanType.ExactValue && !valueProvided)
                return;

            // Determine range
            int startAddr = 0;
            int endAddr = 0xFFFF;

            switch (region)
            {
                case MemoryRegion.InternalRam:
                    startAddr = 0x0000;
                    endAddr = 0x07FF;
                    break;
                case MemoryRegion.Wram:
                    startAddr = 0x6000;
                    endAddr = 0x7FFF;
                    break;
                case MemoryRegion.All:
                    startAddr = 0x0000;
                    endAddr = 0xFFFF;
                    break;
            }

            // Take a snapshot of current memory for "Previous Value" comparisons
            // We only need to snapshot the region we are scanning, but for simplicity let's just read as we go
            // Actually, for "Value Changed" etc, we need the previous snapshot from the *last scan*.
            // If this is the First Scan, "Value Changed" doesn't make sense unless we compare against 0 or something, 
            // but usually First Scan is Exact Value or Unknown Initial Value.

            if (IsFirstScan)
            {
                PerformFirstScan(startAddr, endAddr, scanType, dataType, value);
                IsFirstScan = false;
            }
            else
            {
                PerformNextScan(scanType, dataType, value);
            }
            
            // Update snapshot for next time
            // We can store the values in the ScanResult itself
        }

        private void PerformFirstScan(int startAddr, int endAddr, ScanType scanType, CheatDataType dataType, int targetValue)
        {
            _scanResults.Clear();
            int step = dataType == CheatDataType.Int16 ? 1 : 1; // Usually we scan every byte alignment even for 16-bit

            for (int addr = startAddr; addr <= endAddr; addr += step)
            {
                // Safety check for 16-bit reading past end
                if (dataType == CheatDataType.Int16 && addr > 0xFFFF - 1)
                    continue;

                // Skip RAM Mirrors (0x0800 - 0x1FFF)
                if (addr >= 0x0800 && addr <= 0x1FFF) continue;
                
                // Skip PPU Register Mirrors (0x2008 - 0x3FFF)
                if (addr >= 0x2008 && addr <= 0x3FFF) continue;

                int currentValue = ReadValue(addr, dataType);

                bool match = false;
                switch (scanType)
                {
                    case ScanType.ExactValue:
                        match = currentValue == targetValue;
                        break;
                    case ScanType.UnknownInitialValue:
                        match = true;
                        break;
                    // Other types usually don't apply to first scan, or imply comparison with 0?
                    // Cheat Engine allows "Value > X" etc, but let's stick to simple ones.
                    // If user selects "Value Changed" on first scan, it's invalid or we treat as Unknown.
                    default: 
                        match = true; 
                        break;
                }

                if (match)
                {
                    _scanResults.Add(new ScanResult
                    {
                        Address = addr,
                        CurrentValue = currentValue,
                        PreviousValue = currentValue // For first scan, prev = current
                    });
                }
            }
        }

        private void PerformNextScan(ScanType scanType, CheatDataType dataType, int targetValue)
        {
            // We iterate backwards so we can remove items efficiently
            for (int i = _scanResults.Count - 1; i >= 0; i--)
            {
                var result = _scanResults[i];
                int currentValue = ReadValue(result.Address, dataType);
                int previousValue = result.PreviousValue; // Value from last scan

                bool match = false;
                switch (scanType)
                {
                    case ScanType.ExactValue:
                        match = currentValue == targetValue;
                        break;
                    case ScanType.ValueChanged:
                        match = currentValue != previousValue;
                        break;
                    case ScanType.ValueUnchanged:
                        match = currentValue == previousValue;
                        break;
                    case ScanType.ValueIncreased:
                        match = currentValue > previousValue;
                        break;
                    case ScanType.ValueDecreased:
                        match = currentValue < previousValue;
                        break;
                    case ScanType.UnknownInitialValue:
                        match = true; // Rescan?
                        break;
                }

                if (match)
                {
                    // Update the result
                    result.PreviousValue = currentValue; // Update prev to current for next time
                    result.CurrentValue = currentValue;
                }
                else
                {
                    _scanResults.RemoveAt(i);
                }
            }
        }

        private int ReadValue(int address, CheatDataType dataType)
        {
            if (_memory == null) return 0;

            if (dataType == CheatDataType.Byte)
            {
                return _memory.Peek((ushort)address);
            }
            else
            {
                byte low = _memory.Peek((ushort)address);
                byte high = 0;
                if (address < 0xFFFF)
                    high = _memory.Peek((ushort)(address + 1));
                
                return (high << 8) | low;
            }
        }

        public void AddCheat(int address, CheatDataType dataType)
        {
            if (_activeCheats.Any(c => c.Address == address))
                return;

            int val = ReadValue(address, dataType);
            _activeCheats.Add(new Cheat
            {
                Address = address,
                DataType = dataType,
                Value = val,
                Active = false,
                Description = $"Cheat {address:X4}"
            });
        }

        public void RemoveCheat(Cheat cheat)
        {
            _activeCheats.Remove(cheat);
        }
    }
}
