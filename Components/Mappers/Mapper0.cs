using System;

namespace OGNES.Components.Mappers
{
    public class Mapper0 : Mapper
    {
        public override string Name => "NROM";

        public Mapper0(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                mappedAddress = (uint)(address & 0x1FFF);
                return true;
            }
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                // If PRG ROM is 16KB, it's mirrored at $C000
                mappedAddress = (uint)(address & (PrgBanks > 1 ? 0x7FFF : 0x3FFF));
                return true;
            }

            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                mappedAddress = (uint)(address & 0x1FFF);
                return true;
            }
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                mappedAddress = (uint)(address & (PrgBanks > 1 ? 0x7FFF : 0x3FFF));
                return true;
            }

            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x0000 && address <= 0x1FFF)
            {
                mappedAddress = address;
                return true;
            }

            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapWrite(ushort address, out uint mappedAddress)
        {
            if (address >= 0x0000 && address <= 0x1FFF)
            {
                if (ChrBanks == 0) // Treat as RAM
                {
                    mappedAddress = address;
                    return true;
                }
            }

            mappedAddress = 0;
            return false;
        }
    }
}
