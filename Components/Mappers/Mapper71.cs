using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public class Mapper71 : Mapper
    {
        public override string Name => "Camerica";

        private byte _prgBank = 0;

        public Mapper71(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override void SaveState(BinaryWriter writer)
        {
            base.SaveState(writer);
            writer.Write(_prgBank);
        }

        public override void LoadState(BinaryReader reader)
        {
            base.LoadState(reader);
            _prgBank = reader.ReadByte();
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
            mappedAddress = 0;
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                mappedAddress = (uint)(address & 0x1FFF);
                return true;
            }
            
            if (address >= 0x8000 && address <= 0x9FFF)
            {
                // Mirroring (Fire Hawk)
                // $8000-9FFF:  [...M ....]
                // 0 = 1ScA, 1 = 1ScB
                if ((data & 0x10) == 0)
                {
                    MirrorMode = Cartridge.Mirror.OnescreenLo;
                }
                else
                {
                    MirrorMode = Cartridge.Mirror.OnescreenHi;
                }
                return false;
            }
            else if (address >= 0xC000 && address <= 0xFFFF)
            {
                // PRG Select
                _prgBank = data;
                return false;
            }
            
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
