using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OGNES.Components
{
    public struct GameGenieCode
    {
        public ushort Address;
        public byte Value;
        public byte? CompareValue;
        public bool Enabled;
    }

    public class Memory
    {
        public delegate void MemoryAccessEventHandler(ushort address, byte data, bool isWrite);
        public MemoryAccessEventHandler? OnAccess;

        // The NES has 2KB of internal RAM
        private byte[] _ram = new byte[2048];
        private byte _lastBusValue = 0;
        
        public List<GameGenieCode> GameGenieCodes = new();

        public Cartridge? Cartridge { get; set; }
        public Ppu? Ppu
        {
            get => _ppu;
            set => _ppu = value;
        }
        private Ppu? _ppu;

        public Apu? Apu
        {
            get => _apu;
            set => _apu = value;
        }
        private Apu? _apu;

        public Cpu? Cpu { get; set; }
        public Joypad Joypad1 { get; } = new();
        public Joypad Joypad2 { get; } = new();

        public long TotalCycles { get; private set; }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_ram);
            writer.Write(TotalCycles);
            writer.Write(_lastBusValue);
            Cartridge?.SaveState(writer);
            _ppu?.SaveState(writer);
            _apu?.SaveState(writer);
            Joypad1.SaveState(writer);
            Joypad2.SaveState(writer);
        }

        public void LoadState(BinaryReader reader)
        {
            _ram = reader.ReadBytes(2048);
            TotalCycles = reader.ReadInt64();
            _lastBusValue = reader.ReadByte();
            Cartridge?.LoadState(reader);
            _ppu?.LoadState(reader);
            _apu?.LoadState(reader);
            Joypad1.LoadState(reader);
            Joypad2.LoadState(reader);
        }

        public void Tick()
        {
            TotalCycles++;
            // PPU ticks 3 times for every CPU cycle
            if (_ppu != null)
            {
                _ppu.Tick();
                _ppu.Tick();
                _ppu.Tick();
            }
            // APU ticks once per CPU cycle
            _apu?.Tick(this);
        }

        public byte Read(ushort address)
        {
            byte data = _lastBusValue;
            bool updateBus = true;

            // RAM (0x0000 - 0x07FF) mirrored up to 0x1FFF
            if (address < 0x2000)
            {
                data = _ram[address & 0x7FF];
            }
            // PPU Registers (0x2000 - 0x2007) mirrored up to 0x3FFF
            else if (address < 0x4000)
            {
                data = _ppu?.CpuRead(address) ?? _lastBusValue;
            }
            // APU and I/O Registers (0x4000 - 0x4017)
            else if (address < 0x4018)
            {
                if (address == 0x4015)
                {
                    byte apuStatus = _apu?.ReadStatus() ?? 0;
                    // Merge with open bus (bit 5 is open bus)
                    data = (byte)(apuStatus | (_lastBusValue & 0x20));
                    updateBus = false; // Reading $4015 does not update the open bus value
                }
                else if (address == 0x4016)
                {
                    data = Joypad1.Read(_lastBusValue, TotalCycles);
                }
                else if (address == 0x4017)
                {
                    data = Joypad2.Read(_lastBusValue, TotalCycles);
                }
            }
            // Cartridge Space (0x4020 - 0xFFFF)
            else
            {
                if (Cartridge != null && Cartridge.CpuRead(address, out byte cartData))
                {
                    data = cartData;

                    // Apply Game Genie
                    int count = GameGenieCodes.Count;
                    if (count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var code = GameGenieCodes[i];
                            if (code.Enabled && code.Address == address)
                            {
                                if (code.CompareValue == null || code.CompareValue == cartData)
                                {
                                    data = code.Value;
                                }
                            }
                        }
                    }
                }
            }

            if (updateBus)
            {
                _lastBusValue = data;
            }

            OnAccess?.Invoke(address, data, false);

            return data;
        }

        public byte Peek(ushort address)
        {
            if (address < 0x2000)
            {
                return _ram[address & 0x7FF];
            }
            else if (address < 0x4000)
            {
                return _ppu?.CpuRead(address) ?? 0; // Peek might need a non-destructive read if side effects exist
            }
            else if (address < 0x4018)
            {
                if (address == 0x4015)
                {
                    return _apu?.PeekStatus() ?? 0;
                }
                return 0;
            }
            else
            {
                if (Cartridge != null)
                {
                    return Cartridge.Peek(address);
                }
                return 0;
            }
        }

        public void Write(ushort address, byte data)
        {
            _lastBusValue = data;

            OnAccess?.Invoke(address, data, true);

            if (address < 0x2000)
            {
                _ram[address & 0x7FF] = data;
            }
            else if (address < 0x4000)
            {
                // Allow Mapper to snoop PPU writes (e.g. MMC5)
                Cartridge?.CpuWrite(address, data);
                _ppu?.CpuWrite(address, data);
            }
            else if (address < 0x4018)
            {
                if (address == 0x4014)
                {
                    // OAM DMA
                    ushort baseAddr = (ushort)(data << 8);
                    
                    // DMA takes 513 cycles (or 514 if on an odd cycle)
                    // We stall the CPU by ticking the PPU and APU for these cycles.
                    // Loop unrolling or batching could be done here if supported
                    for (int i = 0; i < 513; i++)
                    {
                        Tick();
                    }

                    for (int i = 0; i < 256; i++)
                    {
                        _ppu?.WriteOam((byte)i, Read((ushort)(baseAddr + i)));
                    }
                }
                else if (address == 0x4016)
                {
                    Joypad1.Write(data);
                    Joypad2.Write(data);
                }
                else
                {
                    _apu?.Write(address, data);
                }
            }
            else if (address == 0x4017)
            {
                _apu?.Write(address, data);
            }
            else
            {
                Cartridge?.CpuWrite(address, data);
            }
        }
    }
}
