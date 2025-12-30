using System;

namespace OGNES.Components.Mappers
{
    public class Mapper4 : Mapper
    {
        private byte _targetRegister = 0;
        private bool _prgBankMode = false;
        private bool _chrInversion = false;
        private byte[] _registers = new byte[8];
        private bool _irqEnabled = false;
        private byte _irqLatch = 0;
        private byte _irqCounter = 0;
        private bool _irqReload = false;
        private bool _irqActive = false;

        private uint[] _prgBankOffsets = new uint[4];
        private uint[] _chrBankOffsets = new uint[8];

        public override bool IrqActive => _irqActive;

        public Mapper4(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
            UpdateOffsets();
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                int bank = (address - 0x8000) / 0x2000;
                mappedAddress = _prgBankOffsets[bank] + (uint)(address & 0x1FFF);
                return true;
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            mappedAddress = 0;
            if (address >= 0x8000 && address <= 0x9FFF)
            {
                if ((address & 0x0001) == 0)
                {
                    _targetRegister = (byte)(data & 0x07);
                    _prgBankMode = (data & 0x40) != 0;
                    _chrInversion = (data & 0x80) != 0;
                }
                else
                {
                    _registers[_targetRegister] = data;
                }
                UpdateOffsets();
                return true;
            }
            else if (address >= 0xA000 && address <= 0xBFFF)
            {
                if ((address & 0x0001) == 0)
                {
                    MirrorMode = (data & 0x01) != 0 ? Cartridge.Mirror.Horizontal : Cartridge.Mirror.Vertical;
                }
                return true;
            }
            else if (address >= 0xC000 && address <= 0xDFFF)
            {
                if ((address & 0x0001) == 0)
                {
                    _irqLatch = data;
                }
                else
                {
                    _irqReload = true;
                }
                return true;
            }
            else if (address >= 0xE000 && address <= 0xFFFF)
            {
                if ((address & 0x0001) == 0)
                {
                    _irqEnabled = false;
                    _irqActive = false;
                }
                else
                {
                    _irqEnabled = true;
                }
                return true;
            }
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                int bank = address / 0x0400;
                mappedAddress = _chrBankOffsets[bank] + (uint)(address & 0x03FF);
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
                    int bank = address / 0x0400;
                    mappedAddress = _chrBankOffsets[bank] + (uint)(address & 0x03FF);
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }

        private ushort _lastA12 = 0;

        public override void NotifyPpuAddress(ushort address)
        {
            ushort a12 = (ushort)(address & 0x1000);
            if (_lastA12 == 0 && a12 != 0)
            {
                if (_irqCounter == 0 || _irqReload)
                {
                    _irqCounter = _irqLatch;
                    _irqReload = false;
                }
                else
                {
                    _irqCounter--;
                }

                if (_irqCounter == 0 && _irqEnabled)
                {
                    _irqActive = true;
                }
            }
            _lastA12 = a12;
        }

        public override void IrqClear()
        {
            _irqActive = false;
        }

        private void UpdateOffsets()
        {
            if (!_chrInversion)
            {
                _chrBankOffsets[0] = (uint)((_registers[0] & 0xFE) * 1024);
                _chrBankOffsets[1] = (uint)((_registers[0] | 0x01) * 1024);
                _chrBankOffsets[2] = (uint)((_registers[1] & 0xFE) * 1024);
                _chrBankOffsets[3] = (uint)((_registers[1] | 0x01) * 1024);
                _chrBankOffsets[4] = (uint)(_registers[2] * 1024);
                _chrBankOffsets[5] = (uint)(_registers[3] * 1024);
                _chrBankOffsets[6] = (uint)(_registers[4] * 1024);
                _chrBankOffsets[7] = (uint)(_registers[5] * 1024);
            }
            else
            {
                _chrBankOffsets[4] = (uint)((_registers[0] & 0xFE) * 1024);
                _chrBankOffsets[5] = (uint)((_registers[0] | 0x01) * 1024);
                _chrBankOffsets[6] = (uint)((_registers[1] & 0xFE) * 1024);
                _chrBankOffsets[7] = (uint)((_registers[1] | 0x01) * 1024);
                _chrBankOffsets[0] = (uint)(_registers[2] * 1024);
                _chrBankOffsets[1] = (uint)(_registers[3] * 1024);
                _chrBankOffsets[2] = (uint)(_registers[4] * 1024);
                _chrBankOffsets[3] = (uint)(_registers[5] * 1024);
            }

            if (!_prgBankMode)
            {
                _prgBankOffsets[0] = (uint)(_registers[6] * 8192);
                _prgBankOffsets[1] = (uint)(_registers[7] * 8192);
                _prgBankOffsets[2] = (uint)((PrgBanks * 2 - 2) * 8192);
                _prgBankOffsets[3] = (uint)((PrgBanks * 2 - 1) * 8192);
            }
            else
            {
                _prgBankOffsets[2] = (uint)(_registers[6] * 8192);
                _prgBankOffsets[1] = (uint)(_registers[7] * 8192);
                _prgBankOffsets[0] = (uint)((PrgBanks * 2 - 2) * 8192);
                _prgBankOffsets[3] = (uint)((PrgBanks * 2 - 1) * 8192);
            }
        }
    }
}
