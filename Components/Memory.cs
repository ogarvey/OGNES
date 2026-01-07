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
        public Ppu? Ppu { get; set; }
        public Apu? Apu { get; set; }
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
            Ppu?.SaveState(writer);
            Apu?.SaveState(writer);
            Joypad1.SaveState(writer);
            Joypad2.SaveState(writer);
        }

        public void LoadState(BinaryReader reader)
        {
            _ram = reader.ReadBytes(2048);
            TotalCycles = reader.ReadInt64();
            _lastBusValue = reader.ReadByte();
            Cartridge?.LoadState(reader);
            Ppu?.LoadState(reader);
            Apu?.LoadState(reader);
            Joypad1.LoadState(reader);
            Joypad2.LoadState(reader);
        }

        public void Tick()
        {
            TotalCycles++;
            // PPU ticks 3 times for every CPU cycle
            Ppu?.Tick();
            Ppu?.Tick();
            Ppu?.Tick();
            // APU ticks once per CPU cycle
            Apu?.Tick(this);
        }

        public byte Read(ushort address)
        {
            byte data = _lastBusValue;
            bool updateBus = true;

            // RAM (0x0000 - 0x07FF) mirrored up to 0x1FFF
            if (address < 0x2000)
            {
                data = _ram[address % 2048];
            }
            // PPU Registers (0x2000 - 0x2007) mirrored up to 0x3FFF
            else if (address < 0x4000)
            {
                data = Ppu?.CpuRead(address) ?? _lastBusValue;
            }
            // APU and I/O Registers (0x4000 - 0x4017)
            else if (address < 0x4018)
            {
                if (address == 0x4015)
                {
                    byte apuStatus = Apu?.ReadStatus() ?? 0;
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
                    for (int i = 0; i < GameGenieCodes.Count; i++)
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
                return _ram[address % 2048];
            }
            else if (address < 0x4000)
            {
                return Ppu?.CpuRead(address) ?? 0; // Peek might need a non-destructive read if side effects exist
            }
            else if (address < 0x4018)
            {
                if (address == 0x4015)
                {
                    return Apu?.PeekStatus() ?? 0;
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
                _ram[address % 2048] = data;
            }
            else if (address < 0x4000)
            {
                Ppu?.CpuWrite(address, data);
            }
            else if (address < 0x4018)
            {
                if (address == 0x4014)
                {
                    // OAM DMA
                    ushort baseAddr = (ushort)(data << 8);
                    
                    // DMA takes 513 cycles (or 514 if on an odd cycle)
                    // We stall the CPU by ticking the PPU and APU for these cycles.
                    for (int i = 0; i < 513; i++)
                    {
                        Tick();
                    }

                    for (int i = 0; i < 256; i++)
                    {
                        Ppu?.WriteOam((byte)i, Read((ushort)(baseAddr + i)));
                    }
                }
                else if (address == 0x4016)
                {
                    Joypad1.Write(data);
                    Joypad2.Write(data);
                }
                else
                {
                    Apu?.Write(address, data);
                }
            }
            else if (address == 0x4017)
            {
                Apu?.Write(address, data);
            }
            else
            {
                Cartridge?.CpuWrite(address, data);
            }
        }
    }
}
