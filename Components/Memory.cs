using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OGNES.Components
{
    public class Memory
    {
        // The NES has 2KB of internal RAM
        private byte[] _ram = new byte[2048];
        
        public Cartridge? Cartridge { get; set; }
        public Ppu? Ppu { get; set; }
        public Apu? Apu { get; set; }
        public Joypad Joypad1 { get; } = new();
        public Joypad Joypad2 { get; } = new();

        public long TotalCycles { get; private set; }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_ram);
            writer.Write(TotalCycles);
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
            // RAM (0x0000 - 0x07FF) mirrored up to 0x1FFF
            if (address < 0x2000)
            {
                return _ram[address % 2048];
            }
            // PPU Registers (0x2000 - 0x2007) mirrored up to 0x3FFF
            else if (address < 0x4000)
            {
                return Ppu?.CpuRead(address) ?? 0;
            }
            // APU and I/O Registers (0x4000 - 0x4017)
            else if (address < 0x4018)
            {
                if (address == 0x4015)
                {
                    return Apu?.ReadStatus() ?? 0;
                }
                if (address == 0x4016)
                {
                    return Joypad1.Read();
                }
                if (address == 0x4017)
                {
                    return Joypad2.Read();
                }
                // TODO: Implement APU/IO register reads
                return 0;
            }
            // Cartridge Space (0x4020 - 0xFFFF)
            else
            {
                if (Cartridge != null && Cartridge.CpuRead(address, out byte data))
                {
                    return data;
                }
                return 0;
            }
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
                    return Apu?.ReadStatus() ?? 0;
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
                    for (int i = 0; i < 256; i++)
                    {
                        Ppu?.WriteOam((byte)i, Read((ushort)(baseAddr + i)));
                    }
                    // DMA takes 513 or 514 cycles. For now we just perform the transfer.
                    // In a more accurate emulator, we would stall the CPU.
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
