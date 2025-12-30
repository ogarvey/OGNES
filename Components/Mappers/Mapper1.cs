using System;

namespace OGNES.Components.Mappers
{
    public class Mapper1 : Mapper
    {
        private byte _shiftRegister = 0x10; // Bit 4 is set to detect 5th write
        private byte _controlReg = 0x0C; // Default: PRG mode 3
        private byte _chrBank0 = 0;
        private byte _chrBank1 = 0;
        private byte _prgBank = 0;

        // Calculated bank offsets
        private uint _prgBankOffsetLow;
        private uint _prgBankOffsetHigh;
        private uint _chrBankOffsetLow;
        private uint _chrBankOffsetHigh;

        public Mapper1(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
            UpdateOffsets();
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                if (address < 0xC000)
                {
                    mappedAddress = _prgBankOffsetLow + (uint)(address & 0x3FFF);
                }
                else
                {
                    mappedAddress = _prgBankOffsetHigh + (uint)(address & 0x3FFF);
                }
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            mappedAddress = 0;
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                if ((data & 0x80) != 0)
                {
                    _shiftRegister = 0x10;
                    _controlReg |= 0x0C;
                    UpdateOffsets();
                }
                else
                {
                    bool complete = (_shiftRegister & 0x01) != 0;
                    _shiftRegister >>= 1;
                    _shiftRegister |= (byte)((data & 0x01) << 4);

                    if (complete)
                    {
                        byte value = _shiftRegister;
                        if (address <= 0x9FFF) _controlReg = value;
                        else if (address <= 0xBFFF) _chrBank0 = value;
                        else if (address <= 0xDFFF) _chrBank1 = value;
                        else _prgBank = value;

                        _shiftRegister = 0x10;
                        UpdateOffsets();
                    }
                }
                return true;
            }
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                if (address < 0x1000)
                {
                    mappedAddress = _chrBankOffsetLow + (uint)(address & 0x0FFF);
                }
                else
                {
                    mappedAddress = _chrBankOffsetHigh + (uint)(address & 0x0FFF);
                }
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapWrite(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                if (ChrBanks == 0) // CHR RAM
                {
                    mappedAddress = address;
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }

        private void UpdateOffsets()
        {
            // Mirroring
            MirrorMode = (_controlReg & 0x03) switch
            {
                0 => Cartridge.Mirror.OnescreenLo,
                1 => Cartridge.Mirror.OnescreenHi,
                2 => Cartridge.Mirror.Vertical,
                3 => Cartridge.Mirror.Horizontal,
                _ => MirrorMode
            };

            // PRG Banks
            int prgMode = (_controlReg >> 2) & 0x03;
            if (prgMode == 0 || prgMode == 1)
            {
                // 32k mode
                _prgBankOffsetLow = (uint)((_prgBank & 0x0E) * 16384);
                _prgBankOffsetHigh = (uint)(((_prgBank & 0x0E) | 0x01) * 16384);
            }
            else if (prgMode == 2)
            {
                // Fix $8000 to first bank, switch $C000
                _prgBankOffsetLow = 0;
                _prgBankOffsetHigh = (uint)((_prgBank & 0x0F) * 16384);
            }
            else
            {
                // Fix $C000 to last bank, switch $8000
                _prgBankOffsetLow = (uint)((_prgBank & 0x0F) * 16384);
                _prgBankOffsetHigh = (uint)((PrgBanks - 1) * 16384);
            }

            // CHR Banks
            if ((_controlReg & 0x10) == 0)
            {
                // 8k mode
                _chrBankOffsetLow = (uint)((_chrBank0 & 0x1E) * 4096);
                _chrBankOffsetHigh = (uint)(((_chrBank0 & 0x1E) | 0x01) * 4096);
            }
            else
            {
                // 4k mode
                _chrBankOffsetLow = (uint)(_chrBank0 * 4096);
                _chrBankOffsetHigh = (uint)(_chrBank1 * 4096);
            }
        }
    }
}
