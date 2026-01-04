using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

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
        public string Group { get; set; } = "Default";
        
        public List<string> GameGenieCodes { get; set; } = new();
        
        [JsonIgnore]
        public List<GameGenieCode> DecodedCodes { get; set; } = new();
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

        // Game Genie Decoding
        private static readonly int[] _ggCharValues = new int[256];

        static CheatManager()
        {
            // Initialize GG char map
            for (int i = 0; i < 256; i++) _ggCharValues[i] = -1;
            string chars = "APZLGITYEOXUKSVN";
            for (int i = 0; i < chars.Length; i++)
            {
                _ggCharValues[chars[i]] = i;
            }
        }

        public CheatManager(Memory memory)
        {
            _memory = memory;
        }

        public void SetMemory(Memory memory)
        {
            _memory = memory;
            NewScan();
            _activeCheats.Clear();
        }

        public void Update()
        {
            if (_memory == null) return;
            
            _memory.GameGenieCodes.Clear();

            foreach (var cheat in _activeCheats)
            {
                if (cheat.Active)
                {
                    if (cheat.GameGenieCodes.Count > 0)
                    {
                        // Ensure decoded codes are available
                        if (cheat.DecodedCodes.Count == 0)
                        {
                            foreach (var code in cheat.GameGenieCodes)
                            {
                                if (TryDecodeGameGenie(code, out int addr, out int val, out int? cmp))
                                {
                                    cheat.DecodedCodes.Add(new GameGenieCode
                                    {
                                        Address = (ushort)addr,
                                        Value = (byte)val,
                                        CompareValue = cmp.HasValue ? (byte)cmp.Value : null,
                                        Enabled = true
                                    });
                                }
                            }
                        }

                        foreach (var decoded in cheat.DecodedCodes)
                        {
                            _memory.GameGenieCodes.Add(decoded);
                        }
                    }
                    else
                    {
                        ApplyCheat(cheat);
                    }
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

        public int ReadValue(int address, CheatDataType dataType)
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

        public void AddCheat(int address, CheatDataType dataType, string? description = null, string group = "Default")
        {
            if (_activeCheats.Any(c => c.Address == address && c.GameGenieCodes.Count == 0))
                return;

            int val = ReadValue(address, dataType);
            _activeCheats.Add(new Cheat
            {
                Address = address,
                DataType = dataType,
                Value = val,
                Active = true,
                Description = description ?? $"Cheat {address:X4}",
                Group = group
            });
        }

        public bool AddGameGenieCheat(string codesInput, string? description = null, string group = "Default")
        {
            var codes = codesInput.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (codes.Length == 0) return false;

            var cheat = new Cheat
            {
                Description = description ?? $"GG {codes[0]}",
                Group = group,
                DataType = CheatDataType.Byte,
                Active = true
            };

            bool anyValid = false;
            foreach (var code in codes)
            {
                if (TryDecodeGameGenie(code, out int address, out int value, out int? compare))
                {
                    cheat.GameGenieCodes.Add(code);
                    cheat.DecodedCodes.Add(new GameGenieCode
                    {
                        Address = (ushort)address,
                        Value = (byte)value,
                        CompareValue = compare.HasValue ? (byte)compare.Value : null,
                        Enabled = true
                    });
                    anyValid = true;
                }
            }

            if (anyValid)
            {
                _activeCheats.Add(cheat);
                return true;
            }
            return false;
        }

        public static bool TryDecodeGameGenie(string code, out int address, out int value, out int? compare)
        {
            address = 0;
            value = 0;
            compare = null;
            
            code = code.Trim().ToUpper().Replace("-", "").Replace(" ", "").Replace("0", "O");
            if (code.Length != 6 && code.Length != 8) return false;

            int[] n = new int[code.Length];
            for (int i = 0; i < code.Length; i++)
            {
                int val = _ggCharValues[code[i]];
                if (val == -1) return false;
                n[i] = val;
            }

            // Helper to get bit from nibble
            // bitIndex: 0-3
            int GetBit(int nibble, int bitIndex) => (nibble >> bitIndex) & 1;

            // 6-char:
            // Char 1: 3 2 1 0 -> Val 1 6 7 8 (7 2 1 0)
            // Char 2: 3 2 1 0 -> Val H 2 3 4 (Addr 7, Val 6 5 4)
            // Char 3: 3 2 1 0 -> Val - I J K (Addr 6 5 4)
            // Char 4: 3 2 1 0 -> Addr L A B C (3 14 13 12)
            // Char 5: 3 2 1 0 -> Addr D M N O (11 2 1 0)
            // Char 6: 3 2 1 0 -> Addr 5 E F G (Val 3, Addr 10 9 8)

            // Value bits: 76543210
            // Address bits: 14..0

            if (code.Length == 6)
            {
                value = (GetBit(n[0], 3) << 7) |
                        (GetBit(n[1], 2) << 6) |
                        (GetBit(n[1], 1) << 5) |
                        (GetBit(n[1], 0) << 4) |
                        (GetBit(n[5], 3) << 3) |
                        (GetBit(n[0], 2) << 2) |
                        (GetBit(n[0], 1) << 1) |
                        (GetBit(n[0], 0) << 0);

                address = 0x8000 |
                          (GetBit(n[3], 2) << 14) |
                          (GetBit(n[3], 1) << 13) |
                          (GetBit(n[3], 0) << 12) |
                          (GetBit(n[4], 3) << 11) |
                          (GetBit(n[5], 2) << 10) |
                          (GetBit(n[5], 1) << 9) |
                          (GetBit(n[5], 0) << 8) |
                          (GetBit(n[1], 3) << 7) |
                          (GetBit(n[2], 2) << 6) |
                          (GetBit(n[2], 1) << 5) |
                          (GetBit(n[2], 0) << 4) |
                          (GetBit(n[3], 3) << 3) |
                          (GetBit(n[4], 2) << 2) |
                          (GetBit(n[4], 1) << 1) |
                          (GetBit(n[4], 0) << 0);
            }
            else // 8 chars
            {
                // Char 1, 2, 3 same as 6-char for Value/Addr parts?
                // Wait, mapping is different for 8-char.
                
                // Char 1: 3 2 1 0 -> Val 1 6 7 8 (7 2 1 0)
                // Char 2: 3 2 1 0 -> Val H 2 3 4 (Addr 7, Val 6 5 4)
                // Char 3: 3 2 1 0 -> Val - I J K (Addr 6 5 4)
                // Char 4: 3 2 1 0 -> Addr L A B C (3 14 13 12)
                // Char 5: 3 2 1 0 -> Addr D M N O (11 2 1 0)
                // Char 6: 3 2 1 0 -> Val % E F G (Comp 3, Addr 10 9 8)
                // Char 7: 3 2 1 0 -> Val ! ^ & * (Comp 7 2 1 0)
                // Char 8: 3 2 1 0 -> Val 5 @ # $ (Val 3, Comp 6 5 4)

                value = (GetBit(n[0], 3) << 7) |
                        (GetBit(n[1], 2) << 6) |
                        (GetBit(n[1], 1) << 5) |
                        (GetBit(n[1], 0) << 4) |
                        (GetBit(n[7], 3) << 3) | // Val 3 is in Char 8 bit 3
                        (GetBit(n[0], 2) << 2) |
                        (GetBit(n[0], 1) << 1) |
                        (GetBit(n[0], 0) << 0);

                address = 0x8000 |
                          (GetBit(n[3], 2) << 14) |
                          (GetBit(n[3], 1) << 13) |
                          (GetBit(n[3], 0) << 12) |
                          (GetBit(n[4], 3) << 11) |
                          (GetBit(n[5], 2) << 10) |
                          (GetBit(n[5], 1) << 9) |
                          (GetBit(n[5], 0) << 8) |
                          (GetBit(n[1], 3) << 7) |
                          (GetBit(n[2], 2) << 6) |
                          (GetBit(n[2], 1) << 5) |
                          (GetBit(n[2], 0) << 4) |
                          (GetBit(n[3], 3) << 3) |
                          (GetBit(n[4], 2) << 2) |
                          (GetBit(n[4], 1) << 1) |
                          (GetBit(n[4], 0) << 0);

                compare = (GetBit(n[6], 3) << 7) |
                          (GetBit(n[7], 2) << 6) |
                          (GetBit(n[7], 1) << 5) |
                          (GetBit(n[7], 0) << 4) |
                          (GetBit(n[5], 3) << 3) |
                          (GetBit(n[6], 2) << 2) |
                          (GetBit(n[6], 1) << 1) |
                          (GetBit(n[6], 0) << 0);
            }

            return true;
        }

        public void SaveCheats(string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_activeCheats, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save cheats: {ex.Message}");
            }
        }

        public void LoadCheats(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                // Check if it's a JSON file or a Text file
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".txt")
                {
                    LoadGameGenieFile(filePath);
                }
                else
                {
                    string json = File.ReadAllText(filePath);
                    var cheats = JsonSerializer.Deserialize<List<Cheat>>(json);
                    if (cheats != null)
                    {
                        _activeCheats = cheats;
                        // Re-decode GG codes
                        foreach (var cheat in _activeCheats)
                        {
                            if (cheat.GameGenieCodes != null)
                            {
                                foreach (var code in cheat.GameGenieCodes)
                                {
                                    if (TryDecodeGameGenie(code, out int addr, out int val, out int? cmp))
                                    {
                                        cheat.DecodedCodes.Add(new GameGenieCode
                                        {
                                            Address = (ushort)addr,
                                            Value = (byte)val,
                                            CompareValue = cmp.HasValue ? (byte)cmp.Value : null,
                                            Enabled = true
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load cheats: {ex.Message}");
            }
        }

        private void LoadGameGenieFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            string group = Path.GetFileNameWithoutExtension(filePath);
            
            // Dictionary to group multi-part cheats by description
            var groupedCheats = new Dictionary<string, Cheat>();
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Format: CODE Description
                var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string code = parts[0];
                    string desc = parts.Length > 1 ? parts[1] : "No Description";
                    
                    // Check for (X of Y) pattern
                    var match = Regex.Match(desc, @"\s*\(\d+\s+of\s+\d+\)$");
                    string baseDesc = desc;
                    if (match.Success)
                    {
                        baseDesc = desc.Substring(0, match.Index).Trim();
                    }

                    if (groupedCheats.TryGetValue(baseDesc, out var existingCheat))
                    {
                        if (!existingCheat.GameGenieCodes.Contains(code))
                        {
                            existingCheat.GameGenieCodes.Add(code);
                            if (TryDecodeGameGenie(code, out int addr, out int val, out int? cmp))
                            {
                                existingCheat.DecodedCodes.Add(new GameGenieCode 
                                { 
                                    Address = (ushort)addr, 
                                    Value = (byte)val, 
                                    CompareValue = cmp.HasValue ? (byte)cmp.Value : null,
                                    Enabled = true 
                                });
                            }
                        }
                    }
                    else
                    {
                        var cheat = new Cheat
                        {
                            Description = baseDesc,
                            Group = group,
                            DataType = CheatDataType.Byte
                        };
                        
                        cheat.GameGenieCodes.Add(code);
                        if (TryDecodeGameGenie(code, out int addr, out int val, out int? cmp))
                        {
                            cheat.DecodedCodes.Add(new GameGenieCode 
                            { 
                                Address = (ushort)addr, 
                                Value = (byte)val, 
                                CompareValue = cmp.HasValue ? (byte)cmp.Value : null,
                                Enabled = true 
                            });
                        }
                        
                        groupedCheats[baseDesc] = cheat;
                        _activeCheats.Add(cheat);
                    }
                }
            }
        }

        public void RemoveCheat(Cheat cheat)
        {
            _activeCheats.Remove(cheat);
        }
    }
}
