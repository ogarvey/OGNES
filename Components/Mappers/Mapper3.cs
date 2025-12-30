using System;

namespace OGNES.Components.Mappers
{
    public class Mapper3 : Mapper
    {
        private byte _chrBank = 0;

        public Mapper3(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                mappedAddress = (uint)(address & (PrgBanks > 1 ? 0x7FFF : 0x3FFF));
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                _chrBank = data;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                mappedAddress = (uint)(_chrBank * 8192 + address);
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapWrite(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                if (ChrBanks == 0)
                {
                    mappedAddress = (uint)(_chrBank * 8192 + address);
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }
    }
}
