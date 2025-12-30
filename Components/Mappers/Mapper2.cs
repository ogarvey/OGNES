using System;

namespace OGNES.Components.Mappers
{
    public class Mapper2 : Mapper
    {
        public override string Name => "UxROM";

        private byte _prgBank = 0;

        public Mapper2(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                if (address < 0xC000)
                {
                    mappedAddress = (uint)((_prgBank % PrgBanks) * 16384 + (address & 0x3FFF));
                }
                else
                {
                    mappedAddress = (uint)((PrgBanks - 1) * 16384 + (address & 0x3FFF));
                }
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                _prgBank = data;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                mappedAddress = address;
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
                    mappedAddress = address;
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }
    }
}
