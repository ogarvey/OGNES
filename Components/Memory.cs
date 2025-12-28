using System;
using System.Collections.Generic;
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

        public long TotalCycles { get; private set; }

        public void Tick()
        {
            TotalCycles++;
            // PPU ticks 3 times for every CPU cycle
            Ppu?.Tick();
            Ppu?.Tick();
            Ppu?.Tick();
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
                // TODO: Implement PPU register reads
                return 0;
            }
            // APU and I/O Registers (0x4000 - 0x4017)
            else if (address < 0x4018)
            {
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
                return 0;
            }
            else if (address < 0x4018)
            {
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
                // TODO: Implement PPU register writes
            }
            else if (address < 0x4018)
            {
                // TODO: Implement APU/IO register writes
            }
            else
            {
                Cartridge?.CpuWrite(address, data);
            }
        }
    }
}
