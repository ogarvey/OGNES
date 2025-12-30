using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public class Mapper4 : Mapper
    {
        public override string Name => "MMC3";

        private byte _targetRegister = 0;
        private bool _prgBankMode = false;
        private bool _chrInversion = false;
        private byte[] _registers = new byte[8];
        private bool _irqEnabled = false;
        private byte _irqLatch = 0;
        private byte _irqCounter = 0;
        private bool _irqReload = false;
        private bool _irqActive = false;

        private bool _prgRamEnable = true;
        private bool _prgRamWriteProtect = false;

        private uint[] _prgBankOffsets = new uint[4];
        private uint[] _chrBankOffsets = new uint[8];

        public override bool IrqActive => _irqActive;

        public Mapper4(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode)
        {
            UpdateOffsets();
        }

        public override void SaveState(BinaryWriter writer)
        {
            base.SaveState(writer);
            writer.Write(_targetRegister);
            writer.Write(_prgBankMode);
            writer.Write(_chrInversion);
            writer.Write(_registers);
            writer.Write(_irqEnabled);
            writer.Write(_irqLatch);
            writer.Write(_irqCounter);
            writer.Write(_irqReload);
            writer.Write(_irqActive);
            writer.Write(_prgRamEnable);
            writer.Write(_prgRamWriteProtect);
        }

        public override void LoadState(BinaryReader reader)
        {
            base.LoadState(reader);
            _targetRegister = reader.ReadByte();
            _prgBankMode = reader.ReadBoolean();
            _chrInversion = reader.ReadBoolean();
            _registers = reader.ReadBytes(8);
            _irqEnabled = reader.ReadBoolean();
            _irqLatch = reader.ReadByte();
            _irqCounter = reader.ReadByte();
            _irqReload = reader.ReadBoolean();
            _irqActive = reader.ReadBoolean();
            _prgRamEnable = reader.ReadBoolean();
            _prgRamWriteProtect = reader.ReadBoolean();
            UpdateOffsets();
        }

        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                if (!_prgRamEnable)
                {
                    mappedAddress = 0;
                    return false;
                }
                mappedAddress = (uint)(address & 0x1FFF);
                return true;
            }

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
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                if (!_prgRamEnable || _prgRamWriteProtect)
                {
                    return false;
                }
                mappedAddress = (uint)(address & 0x1FFF);
                return true;
            }

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
                return false;
            }
            else if (address >= 0xA000 && address <= 0xBFFF)
            {
                if ((address & 0x0001) == 0)
                {
                    MirrorMode = (data & 0x01) != 0 ? Cartridge.Mirror.Horizontal : Cartridge.Mirror.Vertical;
                }
                else
                {
                    _prgRamEnable = (data & 0x80) != 0;
                    _prgRamWriteProtect = (data & 0x40) != 0;
                }
                return false;
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
                return false;
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
                return false;
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

        private int _lastCycle = 0;

        public override void NotifyPpuAddress(ushort address, int cycle)
        {
            ushort a12 = (ushort)(address & 0x1000);
            
            if (a12 != 0)
            {
                int diff = cycle - _lastCycle;
                // Handle frame wrap-around or long delay
                if (diff > 12 || diff < -100) 
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
                _lastCycle = cycle;
            }
        }

        public override void IrqClear()
        {
            _irqActive = false;
        }

        private void UpdateOffsets()
        {
            int prgBankCount = PrgBanks * 2;
            int chrBankCount = ChrBanks == 0 ? 8 : ChrBanks * 8;

            if (!_chrInversion)
            {
                _chrBankOffsets[0] = (uint)(((_registers[0] & 0xFE) % chrBankCount) * 1024);
                _chrBankOffsets[1] = (uint)(((_registers[0] | 0x01) % chrBankCount) * 1024);
                _chrBankOffsets[2] = (uint)(((_registers[1] & 0xFE) % chrBankCount) * 1024);
                _chrBankOffsets[3] = (uint)(((_registers[1] | 0x01) % chrBankCount) * 1024);
                _chrBankOffsets[4] = (uint)((_registers[2] % chrBankCount) * 1024);
                _chrBankOffsets[5] = (uint)((_registers[3] % chrBankCount) * 1024);
                _chrBankOffsets[6] = (uint)((_registers[4] % chrBankCount) * 1024);
                _chrBankOffsets[7] = (uint)((_registers[5] % chrBankCount) * 1024);
            }
            else
            {
                _chrBankOffsets[4] = (uint)(((_registers[0] & 0xFE) % chrBankCount) * 1024);
                _chrBankOffsets[5] = (uint)(((_registers[0] | 0x01) % chrBankCount) * 1024);
                _chrBankOffsets[6] = (uint)(((_registers[1] & 0xFE) % chrBankCount) * 1024);
                _chrBankOffsets[7] = (uint)(((_registers[1] | 0x01) % chrBankCount) * 1024);
                _chrBankOffsets[0] = (uint)((_registers[2] % chrBankCount) * 1024);
                _chrBankOffsets[1] = (uint)((_registers[3] % chrBankCount) * 1024);
                _chrBankOffsets[2] = (uint)((_registers[4] % chrBankCount) * 1024);
                _chrBankOffsets[3] = (uint)((_registers[5] % chrBankCount) * 1024);
            }

            if (!_prgBankMode)
            {
                _prgBankOffsets[0] = (uint)((_registers[6] % prgBankCount) * 8192);
                _prgBankOffsets[1] = (uint)((_registers[7] % prgBankCount) * 8192);
                _prgBankOffsets[2] = (uint)((prgBankCount - 2) * 8192);
                _prgBankOffsets[3] = (uint)((prgBankCount - 1) * 8192);
            }
            else
            {
                _prgBankOffsets[2] = (uint)((_registers[6] % prgBankCount) * 8192);
                _prgBankOffsets[1] = (uint)((_registers[7] % prgBankCount) * 8192);
                _prgBankOffsets[0] = (uint)((prgBankCount - 2) * 8192);
                _prgBankOffsets[3] = (uint)((prgBankCount - 1) * 8192);
            }
        }
    }
}
