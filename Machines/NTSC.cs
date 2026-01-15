using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OGNES.Components;

namespace OGNES.Machines
{
    public class NTSC
    {
        private Cpu _cpu;
        private Ppu _ppu;

        public NTSC()
        {
            Memory memory = new Memory();
            _cpu = new Components.CPUs.Ricoh2A03(memory);
            _ppu = new Components.PPUs.Ricoh2C02();
            memory.Cpu = _cpu;
            memory.Ppu = _ppu;
        }
    }
}
