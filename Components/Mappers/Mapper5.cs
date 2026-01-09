using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public class Mapper5 : Mapper
    {
        public override string Name => "MMC5";
        public Mapper5(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode) {}

        private byte[] _prgRam = new byte[64 * 1024];
        private byte[] _exRam = new byte[1024];

        // Registers
        private byte _prgMode = 3;
        private byte _chrMode = 0;
        private byte _exRamMode = 0;
        private byte _ntMapping = 0;
        private byte _fillTile = 0;
        private byte _fillColor = 0;
        
        private byte _prgRamProtect1 = 0;
        private byte _prgRamProtect2 = 0;

        private byte _irqLine = 0;
        private byte _irqEnable = 0;
        private byte _irqStatus = 0;

        private byte _multiplier1 = 0;
        private byte _multiplier2 = 0;
        private byte _chrHigh = 0;

        private byte[] _prgRegs = { 0, 0, 0, 0, 0 }; 
        private byte[] _chrRegs = new byte[12]; 

        private int[] _prgBanks = new int[5]; 
        private bool[] _prgIsRam = new bool[5]; 

        private int _irqScanline = 0;
        private bool _irqActive = false;
        private int _ppuFetchCount = 0;
        private bool _inFrame = false;

        public override bool IrqActive => _irqActive;


        public override bool CpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address >= 0x6000)
            {
                int slot = GetPrgSlot(address);
                if (slot >= 0 && !_prgIsRam[slot])
                {
                    mappedAddress = (uint)(_prgBanks[slot] + (address & 0x1FFF));
                    return true;
                }
            }
            mappedAddress = 0;
            return false;
        }

        public override bool CpuMapWrite(ushort address, out uint mappedAddress, byte data)
        {
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapRead(ushort address, out uint mappedAddress)
        {
            if (address <= 0x1FFF)
            {
                int page = address / 1024;
                int bank = 0;
                switch (_chrMode)
                {
                    case 0: bank = _chrRegs[7] + (_chrHigh << 8); mappedAddress = (uint)((bank * 8192) + (address & 0x1FFF)); return true;
                    case 1: 
                        bank = (page < 4) ? _chrRegs[3] + (_chrHigh << 8) : _chrRegs[7] + (_chrHigh << 8);
                        mappedAddress = (uint)((bank * 4096) + (address & 0x0FFF)); return true;
                    case 2:
                        bank = _chrRegs[(page / 2) * 2 + 1] + (_chrHigh << 8);
                        mappedAddress = (uint)((bank * 2048) + (address & 0x07FF)); return true;
                    case 3:
                        bank = _chrRegs[page] + (_chrHigh << 8);
                        mappedAddress = (uint)((bank * 1024) + (address & 0x03FF)); return true;
                }
            }
            mappedAddress = 0;
            return false;
        }

        public override bool PpuMapWrite(ushort address, out uint mappedAddress) { mappedAddress = 0; return false; }

        
        public override byte[]? GetBatteryRam()
        {
            return _prgRam;
        }

        public override void SetBatteryRam(byte[] data)
        {
            if (data != null && data.Length <= _prgRam.Length)
            {
                Array.Copy(data, _prgRam, data.Length);
            }
        }

        private void UpdatePrgBanks()
        {
            // Slot 0 ($6000) - Always RAM
            _prgBanks[0] = (_prgRegs[0] & 0x07) * 8192;
            _prgIsRam[0] = true;

            // Slot 4 ($E000) - Always ROM
            _prgBanks[4] = ((_prgRegs[4] & 0x7F) * 8192);
            _prgIsRam[4] = false;

            switch (_prgMode)
            {
                case 0:
                    int bankBase0 = (_prgRegs[4] & 0x7C); 
                    _prgBanks[1] = (bankBase0 | 0) * 8192; _prgIsRam[1] = false;
                    _prgBanks[2] = (bankBase0 | 1) * 8192; _prgIsRam[2] = false;
                    _prgBanks[3] = (bankBase0 | 2) * 8192; _prgIsRam[3] = false;
                    _prgBanks[4] = (bankBase0 | 3) * 8192; _prgIsRam[4] = false;
                    break;
                case 1:
                    bool b1Ram = (_prgRegs[2] & 0x80) == 0;
                    int b1Val = (_prgRegs[2] & (b1Ram ? 0x07 : 0x7F)) & ~1;
                    _prgBanks[1] = b1Val * 8192; _prgIsRam[1] = b1Ram;
                    _prgBanks[2] = (b1Val + 1) * 8192; _prgIsRam[2] = b1Ram;

                    int b2Val = (_prgRegs[4] & 0x7F) & ~1;
                    _prgBanks[3] = b2Val * 8192; _prgIsRam[3] = false;
                    _prgBanks[4] = (b2Val + 1) * 8192; _prgIsRam[4] = false;
                    break;
                case 2:
                    bool b1Ram2 = (_prgRegs[2] & 0x80) == 0;
                    int b1Val2 = (_prgRegs[2] & (b1Ram2 ? 0x07 : 0x7F)) & ~1;
                    _prgBanks[1] = b1Val2 * 8192; _prgIsRam[1] = b1Ram2;
                    _prgBanks[2] = (b1Val2 + 1) * 8192; _prgIsRam[2] = b1Ram2;

                    bool b2Ram2 = (_prgRegs[3] & 0x80) == 0;
                    _prgBanks[3] = (_prgRegs[3] & (b2Ram2 ? 0x07 : 0x7F)) * 8192;
                    _prgIsRam[3] = b2Ram2;
                    break;
                case 3:
                     bool r1 = (_prgRegs[1] & 0x80) == 0;
                     _prgBanks[1] = (_prgRegs[1] & (r1 ? 0x07 : 0x7F)) * 8192;
                     _prgIsRam[1] = r1;

                     bool r2 = (_prgRegs[2] & 0x80) == 0;
                     _prgBanks[2] = (_prgRegs[2] & (r2 ? 0x07 : 0x7F)) * 8192;
                     _prgIsRam[2] = r2;

                     bool r3 = (_prgRegs[3] & 0x80) == 0;
                     _prgBanks[3] = (_prgRegs[3] & (r3 ? 0x07 : 0x7F)) * 8192;
                     _prgIsRam[3] = r3;
                    break;
            }
        }
        
        private int GetPrgSlot(ushort address)
        {
            if (address >= 0x6000 && address <= 0x7FFF) return 0;
            if (address >= 0x8000 && address <= 0x9FFF) return 1;
            if (address >= 0xA000 && address <= 0xBFFF) return 2;
            if (address >= 0xC000 && address <= 0xDFFF) return 3;
            if (address >= 0xE000 && address <= 0xFFFF) return 4;
            return -1;
        }

        public override bool Read(ushort address, out byte data)
        {
            data = 0;
            if (address >= 0x5000 && address <= 0x5FFF)
            {
                switch (address)
                {
                    case 0x5204:
                         data = _irqStatus; _irqStatus &= 0x7F; _irqActive = false; return true;
                    case 0x5205:
                         data = (byte)((_multiplier1 * _multiplier2) & 0xFF); return true;
                    case 0x5206:
                         data = (byte)(((_multiplier1 * _multiplier2) >> 8) & 0xFF); return true;
                }
                if (address >= 0x5C00 && _exRamMode >= 2)
                {
                    data = _exRam[address - 0x5C00]; return true;
                }
                return false;
            }
            if (address >= 0x6000)
            {
                int slot = GetPrgSlot(address);
                if (slot >= 0 && _prgIsRam[slot])
                {
                    uint map = (uint)(_prgBanks[slot] + (address & 0x1FFF));
                    data = _prgRam[map & (_prgRam.Length - 1)];
                    return true;
                }
            }
            return false;
        }

        public override bool Write(ushort address, byte data)
        {
            if (address >= 0x5000 && address <= 0x5FFF)
            {
                switch(address)
                {
                     case 0x5100: _prgMode = (byte)(data & 3); UpdatePrgBanks(); break;
                     case 0x5101: _chrMode = (byte)(data & 3); break;
                     case 0x5102: _prgRamProtect1 = (byte)(data & 3); break;
                     case 0x5103: _prgRamProtect2 = (byte)(data & 3); break;
                     case 0x5104: _exRamMode = (byte)(data & 3); break;
                     case 0x5105: _ntMapping = data; break;
                     case 0x5106: _fillTile = data; break;
                     case 0x5107: _fillColor = (byte)(data & 3); break;
                     case 0x5113: _prgRegs[0] = data; UpdatePrgBanks(); break;
                     case 0x5114: _prgRegs[1] = data; UpdatePrgBanks(); break;
                     case 0x5115: _prgRegs[2] = data; UpdatePrgBanks(); break;
                     case 0x5116: _prgRegs[3] = data; UpdatePrgBanks(); break;
                     case 0x5117: _prgRegs[4] = data; UpdatePrgBanks(); break;
                     case 0x5120: _chrRegs[0] = data; break;
                     case 0x5121: _chrRegs[1] = data; break;
                     case 0x5122: _chrRegs[2] = data; break;
                     case 0x5123: _chrRegs[3] = data; break;
                     case 0x5124: _chrRegs[4] = data; break;
                     case 0x5125: _chrRegs[5] = data; break;
                     case 0x5126: _chrRegs[6] = data; break;
                     case 0x5127: _chrRegs[7] = data; break;
                     case 0x5128: _chrRegs[8] = data; break;
                     case 0x5129: _chrRegs[9] = data; break;
                     case 0x512A: _chrRegs[10] = data; break;
                     case 0x512B: _chrRegs[11] = data; break;
                     case 0x5130: _chrHigh = (byte)(data & 3); break;
                     case 0x5203: _irqLine = data; break;
                     case 0x5204: _irqEnable = data; break;
                     case 0x5205: _multiplier1 = data; break;
                     case 0x5206: _multiplier2 = data; break;
                }
                if (address >= 0x5C00 && _exRamMode != 3) _exRam[address - 0x5C00] = data;
                return true;
            }
            if (address >= 0x6000)
            {
                int slot = GetPrgSlot(address);
                if (slot >= 0 && _prgIsRam[slot] && _prgRamProtect1 == 2 && _prgRamProtect2 == 1)
                {
                    uint map = (uint)(_prgBanks[slot] + (address & 0x1FFF));
                    _prgRam[map & (_prgRam.Length - 1)] = data;
                    return true;
                }
            }
            return false;
        }

        public override void SaveState(BinaryWriter writer)
        {
            base.SaveState(writer);
            writer.Write(_prgRam); writer.Write(_exRam); writer.Write(_prgMode); writer.Write(_chrMode);
            writer.Write(_exRamMode); writer.Write(_ntMapping); writer.Write(_fillTile); writer.Write(_fillColor);
            writer.Write(_prgRamProtect1); writer.Write(_prgRamProtect2); writer.Write(_irqLine); writer.Write(_irqEnable);
            writer.Write(_irqStatus); writer.Write(_multiplier1); writer.Write(_multiplier2); writer.Write(_chrHigh);
            writer.Write(_prgRegs); writer.Write(_chrRegs); writer.Write(_irqScanline); writer.Write(_irqActive);
            writer.Write(_ppuFetchCount); writer.Write(_inFrame);
        }

        public override void LoadState(BinaryReader reader)
        {
            base.LoadState(reader);
            _prgRam = reader.ReadBytes(65536); _exRam = reader.ReadBytes(1024); _prgMode = reader.ReadByte(); _chrMode = reader.ReadByte();
            _exRamMode = reader.ReadByte(); _ntMapping = reader.ReadByte(); _fillTile = reader.ReadByte(); _fillColor = reader.ReadByte();
            _prgRamProtect1 = reader.ReadByte(); _prgRamProtect2 = reader.ReadByte(); _irqLine = reader.ReadByte(); _irqEnable = reader.ReadByte();
            _irqStatus = reader.ReadByte(); _multiplier1 = reader.ReadByte(); _multiplier2 = reader.ReadByte(); _chrHigh = reader.ReadByte();
            _prgRegs = reader.ReadBytes(5); _chrRegs = reader.ReadBytes(12); _irqScanline = reader.ReadInt32(); _irqActive = reader.ReadBoolean();
            _ppuFetchCount = reader.ReadInt32(); _inFrame = reader.ReadBoolean();
            UpdatePrgBanks();
        }

        public override bool PpuRead(ushort address, out byte data)
        {
            if (address >= 0x2000 && address <= 0x3FFF)
            {
                 int quad = (address & 0x0C00) >> 10; 
                 int mode = (_ntMapping >> (quad * 2)) & 3;
                 if (mode == 2)
                 {
                     if(_exRamMode == 0 || _exRamMode == 1) { data = _exRam[address & 0x03FF]; return true; }
                 }
                 else if (mode == 3)
                 {
                     if ((address & 0x03FF) >= 0x3C0) { byte c = (byte)(_fillColor & 3); data = (byte)(c | c<<2 | c<<4 | c<<6); return true; }
                     else { data = _fillTile; return true; }
                 }
            }
            data = 0; return false;
        }

        public override void NotifyPpuAddress(ushort address)
        {
            if (address >= 0x2000 && address <= 0x2FFF) 
            {
                if (!_inFrame) { _inFrame = true; _irqScanline = 0; _ppuFetchCount = 0; }
                _ppuFetchCount++;
                if (_ppuFetchCount >= 42)
                {
                    _ppuFetchCount = 0; _irqScanline++;
                    if (_irqScanline == _irqLine) { _irqStatus |= 0x80; if ((_irqEnable & 0x80) != 0) _irqActive = true; }
                }
            }
            else if (address >= 0x3F00) _inFrame = false;
        }



    }
}
