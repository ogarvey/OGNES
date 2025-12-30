using System;

namespace OGNES.Components.Mappers
{
    public class Mapper3 : Mapper
    {
        public override string Name => "CNROM";

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
                int bankCount = ChrBanks == 0 ? 1 : ChrBanks;
                mappedAddress = (uint)((_chrBank % bankCount) * 8192 + address);
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
                    // CHR RAM is always 8KB for CNROM
                    mappedAddress = (uint)address;
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }
    }
}
