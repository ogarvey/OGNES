using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OGNES.Components
{
    public class Memory
    {
        // For now, we'll use a full 64KB address space to simplify testing.
        // In a real NES, this would be mapped to RAM, PPU, APU, and Cartridge.
        private byte[] _memory = new byte[65536];
        
        public long TotalCycles { get; private set; }

        public void Tick()
        {
            TotalCycles++;
        }

        public byte Read(ushort address)
        {
            return _memory[address];
        }

        public void Write(ushort address, byte data)
        {
            _memory[address] = data;
        }
    }
}
