using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public class Mapper10 : Mapper
    {
        public override string Name => "MMC4";

        private byte _prgBank = 0;
        private byte _chrBank0A = 0;
        private byte _chrBank0B = 0;
        private byte _chrBank1A = 0;
        private byte _chrBank1B = 0;
        private bool _latch0 = false;
        private bool _latch1 = false;

        public Mapper10(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
        }

        public override void SaveState(BinaryWriter writer)
        {
            base.SaveState(writer);
            writer.Write(_prgBank);
            writer.Write(_chrBank0A);
            writer.Write(_chrBank0B);
            writer.Write(_chrBank1A);
            writer.Write(_chrBank1B);
            writer.Write(_latch0);
            writer.Write(_latch1);
        }

        public override void LoadState(BinaryReader reader)
        {
            base.LoadState(reader);
            _prgBank = reader.ReadByte();
            _chrBank0A = reader.ReadByte();
            _chrBank0B = reader.ReadByte();
            _chrBank1A = reader.ReadByte();
            _chrBank1B = reader.ReadByte();
            _latch0 = reader.ReadBoolean();
            _latch1 = reader.ReadBoolean();
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
            mappedAddress = 0;
            if (address >= 0xA000 && address <= 0xAFFF) _prgBank = (byte)(data & 0x0F);
            else if (address >= 0xB000 && address <= 0xBFFF) _chrBank0A = (byte)(data & 0x1F);
            else if (address >= 0xC000 && address <= 0xCFFF) _chrBank0B = (byte)(data & 0x1F);
            else if (address >= 0xD000 && address <= 0xDFFF) _chrBank1A = (byte)(data & 0x1F);
            else if (address >= 0xE000 && address <= 0xEFFF) _chrBank1B = (byte)(data & 0x1F);
            else if (address >= 0xF000 && address <= 0xFFFF) MirrorMode = (data & 0x01) != 0 ? Cartridge.Mirror.Horizontal : Cartridge.Mirror.Vertical;
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                int chrBankCount = ChrBanks * 2;
                if (address < 0x1000)
                {
                    mappedAddress = (uint)(((_latch0 ? _chrBank0B : _chrBank0A) % chrBankCount) * 4096 + (address & 0x0FFF));
                }
                else
                {
                    mappedAddress = (uint)(((_latch1 ? _chrBank1B : _chrBank1A) % chrBankCount) * 4096 + (address & 0x0FFF));
                }
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapWrite(ushort address, out uint mappedAddress)
        {
            mappedAddress = 0;
            return false;
        }

        public override void NotifyPpuAddress(ushort address)
        {
            if (address >= 0x0FD0 && address <= 0x0FDF) _latch0 = false;
            else if (address >= 0x0FE0 && address <= 0x0FEF) _latch0 = true;
            else if (address >= 0x1FD0 && address <= 0x1FDF) _latch1 = false;
            else if (address >= 0x1FE0 && address <= 0x1FEF) _latch1 = true;
        }
    }
}
