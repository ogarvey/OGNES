using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OGNES.Components.CPUs
{
  public class Ricoh2A03 : Cpu
  {
    // CPU: Ricoh 2A03 (NTSC) - Used in NES consoles in North America and Japan
    // Master Clock: 21.477272 (236.25/11.0) MHz
    // CPU Clock:   1.789773 (21.477272/12) MHz
    // Cycles per scanline: 113.66666 (341 * (4/12)) cycles
    // APU Frame Counter: 60Hz
    public Ricoh2A03(Memory bus) : base(bus)
    {
    }
  }
}
