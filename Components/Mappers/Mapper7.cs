using System;

namespace OGNES.Components.Mappers
{
    public class Mapper7 : Mapper
    {
        public override string Name => "AxROM";

        private byte _prgBank = 0;

        public Mapper7(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                int prgBankCount = PrgBanks / 2;
                mappedAddress = (uint)((_prgBank % prgBankCount) * 32768 + (address & 0x7FFF));
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                _prgBank = (byte)(data & 0x07);
                MirrorMode = (data & 0x10) != 0 ? Cartridge.Mirror.OnescreenHi : Cartridge.Mirror.OnescreenLo;
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
