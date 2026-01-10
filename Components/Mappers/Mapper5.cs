using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public class Mapper5 : Mapper
    {
        public override string Name => "MMC5";

        private byte[] _prgRam = new byte[64 * 1024];
        private byte[] _exRam = new byte[1024];
        private byte[] _internalCiram; // 2KB internal Nametable RAM

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

        private byte[] _prgRegs = { 0, 0, 0, 0, 0 }; // $5113-$5117 
        private ushort[] _chrRegs = new ushort[12]; // $5120-$512B, 10-bit values

        private int[] _prgBanks = new int[5]; 
        private bool[] _prgIsRam = new bool[5]; 

        private int _irqScanline = 0;
        private bool _irqActive = false;
        private bool _inFrame = false;
        
        // CHR Banking State
        private byte _cachedExRamTile = 0; // Latch for ExGraphics banking
        
        // Snooped PPU State
        private bool _spriteSize16 = false; // $2000 bit 5
        private bool _renderingEnabled = false; // $2001 bit 3/4
        private bool _inSpriteFetch = false;
        private int _prevScanline = -100;

        public Mapper5(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode) : base(prgBanks, chrBanks, mirrorMode) 
        {
            _internalCiram = new byte[2048];
            _prgRegs[4] = 0xFF; // Init PRG bank pointer to last bank
            // CHR regs default to 0
            for(int i=0; i<_chrRegs.Length; i++) _chrRegs[i] = 0;
            
            UpdatePrgBanks();
        }

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
                // Determine banking set (A or B)
                // 8x8 Mode (_spriteSize16 = false): Always Set A ($5120-$5127). Set B ignored.
                // 8x16 Mode (_spriteSize16 = true): 
                //    - Sprites (257-320 cycle fetch): Set A
                //    - Background (1-256 cycle fetch): Set B
                //    - Other: Set B
                
                bool useSetB = false;

                if (_spriteSize16 && !_inSpriteFetch)
                {
                    useSetB = true; 
                }

                int bank = GetChrBank(address, useSetB);
                mappedAddress = (uint)((bank * 1024) + (address & 0x03FF)); // 1KB granularity
                return true;
            }
            mappedAddress = 0;
            return false;
        }
        
        private int GetChrBank(int address, bool useSetB)
        {
            // ExGraphics (Mode 1) Logic - Only for Background in ExMode 1
            // Use ExRam latched value if applicable
            if (_exRamMode == 1 && useSetB)
            {
                int exBank4k = (_cachedExRamTile & 0x3F);
                // Address offset into the 4KB bank
                int subBank = (address & 0x0FFF) / 1024;
                int finalBank = (exBank4k * 4) + subBank; 
                
                if (ChrBanks > 0) finalBank %= (ChrBanks * 8);
                return finalBank;
            }

            int page = (address & 0x1FFF) / 1024; // 0 to 7
            int regIndex = 0;
            
            if (!useSetB)
            {
                // Set A Logic ($5120-$5127)
                switch (_chrMode)
                {
                    case 0: // 8KB
                        regIndex = 7;
                        break;
                    case 1: // 4KB
                        regIndex = (page < 4) ? 3 : 7;
                        break;
                    case 2: // 2KB
                        regIndex = (page / 2) * 2 + 1;
                        break;
                    case 3: // 1KB
                        regIndex = page;
                        break;
                }
            }
            else
            {
                // Set B Logic ($5128-$512B)
                int bPage = page % 4; // 0-3
                
                switch (_chrMode)
                {
                    case 0: // 8KB
                        regIndex = 11;
                        break;
                    case 1: // 4KB
                        regIndex = 11; 
                        break;
                    case 2: // 2KB
                        if (bPage < 2) regIndex = 9; else regIndex = 11;
                        break;
                    case 3: // 1KB
                        regIndex = 8 + bPage;
                        break;
                }
            }

            int bankVal = _chrRegs[regIndex]; 
            
            int computedBank = 0;
            switch (_chrMode)
            {
                case 0: // 8KB reg val -> 1KB banks
                    computedBank = (bankVal * 8) + page; 
                    break;
                case 1: // 4KB reg val -> 1KB banks
                    computedBank = (bankVal * 4) + (page % 4);
                    break;
                case 2: // 2KB reg val
                    computedBank = (bankVal * 2) + (page % 2);
                    break;
                default: // 1KB
                    computedBank = bankVal;
                    break;
            }

            // Mask CHR Bank to actual ROM size safely (Modulo)
            if (ChrBanks > 0)
            {
                computedBank %= (ChrBanks * 8);
            }
            return computedBank;
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
            // Total 8KB banks available in ROM
            int totalPrgBanks = PrgBanks * 2;
            if (totalPrgBanks == 0) totalPrgBanks = 2; // Safety

            // Helper to mask bank index
            int Mask(int bank) => bank % totalPrgBanks;

            // $5113 (Reg 0) maps $6000-$7FFF. 
            // Docs: Bit 7 selects RAM (0) or ROM (1).
            bool r0_isRom = (_prgRegs[0] & 0x80) != 0;
            if (r0_isRom)
            {
                _prgBanks[0] = Mask((_prgRegs[0] & 0x7F)) * 8192;
                _prgIsRam[0] = false;
            }
            else
            {
                _prgBanks[0] = (_prgRegs[0] & 0x7F) * 8192; // RAM index
                _prgIsRam[0] = true;
            }

            // $5117 (Reg 4) maps $E000-$FFFF. Always ROM.
            int reg4 = _prgRegs[4];
            _prgBanks[4] = Mask((reg4 & 0x7F)) * 8192;
            _prgIsRam[4] = false;

            switch (_prgMode)
            {
                case 0: // One 32KB bank at $8000
                    int bankBase0 = (_prgRegs[4] & 0x7C); 
                    _prgBanks[1] = Mask(bankBase0 | 0) * 8192; _prgIsRam[1] = false;
                    _prgBanks[2] = Mask(bankBase0 | 1) * 8192; _prgIsRam[2] = false;
                    _prgBanks[3] = Mask(bankBase0 | 2) * 8192; _prgIsRam[3] = false;
                    _prgBanks[4] = Mask(bankBase0 | 3) * 8192; _prgIsRam[4] = false;
                    break;

                case 1: // Two 16KB banks
                    // Bank 1 ($8000-$BFFF): controlled by $5115 (Reg 2).
                    bool b1Rom = (_prgRegs[2] & 0x80) != 0;
                    int b1Val = (_prgRegs[2] & 0x7F) & ~1; 
                    if (b1Rom)
                    {
                        _prgBanks[1] = Mask(b1Val) * 8192;       _prgIsRam[1] = false;
                        _prgBanks[2] = Mask(b1Val + 1) * 8192; _prgIsRam[2] = false;
                    }
                    else
                    {
                        _prgBanks[1] = b1Val * 8192;       _prgIsRam[1] = true;
                        _prgBanks[2] = (b1Val + 1) * 8192; _prgIsRam[2] = true;
                    }

                    // Bank 2 ($C000-$FFFF): controlled by $5117 (Reg 4)
                    int b2Val = (_prgRegs[4] & 0x7F) & ~1;
                    _prgBanks[3] = Mask(b2Val) * 8192;       _prgIsRam[3] = false; // Force ROM
                    _prgBanks[4] = Mask(b2Val + 1) * 8192; _prgIsRam[4] = false;
                    break;

                case 2: // One 16KB + Two 8KB
                    // $8000-$BFFF (16KB) -> $5115 (Reg 2)
                    bool b1Rom2 = (_prgRegs[2] & 0x80) != 0;
                    int b1Val2 = (_prgRegs[2] & 0x7F) & ~1;
                    if (b1Rom2)
                    {
                        _prgBanks[1] = Mask(b1Val2) * 8192;       _prgIsRam[1] = false;
                        _prgBanks[2] = Mask(b1Val2 + 1) * 8192; _prgIsRam[2] = false;
                    }
                    else
                    {
                        _prgBanks[1] = b1Val2 * 8192;       _prgIsRam[1] = true;
                        _prgBanks[2] = (b1Val2 + 1) * 8192; _prgIsRam[2] = true;
                    }

                    // $C000-$DFFF (8KB) -> $5116 (Reg 3)
                    bool b2Rom2 = (_prgRegs[3] & 0x80) != 0;
                    if (b2Rom2)
                    {
                        _prgBanks[3] = Mask(_prgRegs[3] & 0x7F) * 8192;
                        _prgIsRam[3] = false;
                    }
                    else
                    {
                        _prgBanks[3] = (_prgRegs[3] & 0x7F) * 8192;
                        _prgIsRam[3] = true;
                    }
                    break;

                case 3: // Four 8KB banks
                    // $8000-$9FFF -> $5114 (Reg 1)
                     bool r1 = (_prgRegs[1] & 0x80) != 0;
                     if (r1) { _prgBanks[1] = Mask(_prgRegs[1] & 0x7F) * 8192; _prgIsRam[1] = false; }
                     else    { _prgBanks[1] = (_prgRegs[1] & 0x7F) * 8192; _prgIsRam[1] = true; }

                     // $A000-$BFFF -> $5115 (Reg 2)
                     bool r2 = (_prgRegs[2] & 0x80) != 0;
                     if (r2) { _prgBanks[2] = Mask(_prgRegs[2] & 0x7F) * 8192; _prgIsRam[2] = false; }
                     else    { _prgBanks[2] = (_prgRegs[2] & 0x7F) * 8192; _prgIsRam[2] = true; }

                     // $C000-$DFFF -> $5116 (Reg 3)
                     bool r3 = (_prgRegs[3] & 0x80) != 0;
                     if (r3) { _prgBanks[3] = Mask(_prgRegs[3] & 0x7F) * 8192; _prgIsRam[3] = false; }
                     else    { _prgBanks[3] = (_prgRegs[3] & 0x7F) * 8192; _prgIsRam[3] = true; }
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
            // Snoop PPU Registers
            if (address == 0x2000)
            {
                _spriteSize16 = (data & 0x20) != 0;
                return false; // Just snooping
            }
            if (address == 0x2001)
            {
                _renderingEnabled = (data & 0x18) != 0;
                return false; // Just snooping
            }

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
                     
                     // CHR Registers - Combine with _chrHigh
                     case 0x5120: _chrRegs[0] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5121: _chrRegs[1] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5122: _chrRegs[2] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5123: _chrRegs[3] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5124: _chrRegs[4] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5125: _chrRegs[5] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5126: _chrRegs[6] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5127: _chrRegs[7] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5128: _chrRegs[8] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x5129: _chrRegs[9] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x512A: _chrRegs[10] = (ushort)(data | (_chrHigh << 8)); break;
                     case 0x512B: _chrRegs[11] = (ushort)(data | (_chrHigh << 8)); break;
                     
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
            writer.Write(_prgRam); writer.Write(_exRam); writer.Write(_internalCiram);
            writer.Write(_prgMode); writer.Write(_chrMode);
            writer.Write(_exRamMode); writer.Write(_ntMapping); writer.Write(_fillTile); writer.Write(_fillColor);
            writer.Write(_prgRamProtect1); writer.Write(_prgRamProtect2); writer.Write(_irqLine); writer.Write(_irqEnable);
            writer.Write(_irqStatus); writer.Write(_multiplier1); writer.Write(_multiplier2); writer.Write(_chrHigh);
            writer.Write(_prgRegs); 
            foreach(ushort v in _chrRegs) writer.Write(v);
            
            writer.Write(_irqScanline); writer.Write(_irqActive);
            writer.Write(_inFrame);
            // Save new flags
            writer.Write(_spriteSize16); writer.Write(_renderingEnabled); 
            writer.Write(_inSpriteFetch);
            writer.Write(_cachedExRamTile);
        }

        public override void LoadState(BinaryReader reader)
        {
            base.LoadState(reader);
            _prgRam = reader.ReadBytes(65536); _exRam = reader.ReadBytes(1024); _internalCiram = reader.ReadBytes(2048);
            _prgMode = reader.ReadByte(); _chrMode = reader.ReadByte();
            _exRamMode = reader.ReadByte(); _ntMapping = reader.ReadByte(); _fillTile = reader.ReadByte(); _fillColor = reader.ReadByte();
            _prgRamProtect1 = reader.ReadByte(); _prgRamProtect2 = reader.ReadByte(); _irqLine = reader.ReadByte(); _irqEnable = reader.ReadByte();
            _irqStatus = reader.ReadByte(); _multiplier1 = reader.ReadByte(); _multiplier2 = reader.ReadByte(); _chrHigh = reader.ReadByte();
            _prgRegs = reader.ReadBytes(5); 
            // Load _chrRegs
            for(int i=0; i<12; i++) _chrRegs[i] = reader.ReadUInt16();
            
            _irqScanline = reader.ReadInt32(); _irqActive = reader.ReadBoolean();
            // _ppuFetchCount Removed
            if(reader.BaseStream.Position < reader.BaseStream.Length)
            {
               // Just skipping potential old save data if we want compatibility, but since we are changing schema, load might fail or need versioning.
               // Assuming simplistic load:
               // Old was: FetchCount(int), InFrame(bool), LastWritten(byte), bSetWritten(bool), Cached(byte)
               // New is: InFrame(bool), SpriteSize(bool), Rendering(bool), InSprite(bool), Cached(byte)
               
               // To keep it simple, I will read boolean for InFrame
               // Discard FetchCount which is next in OLD saves?
               // Since I can't easily detect version, I will just proceed with writing my fields.
               // Note: This may break existing saves. 
            }
            
            _inFrame = reader.ReadBoolean();
            _spriteSize16 = reader.ReadBoolean();
            _renderingEnabled = reader.ReadBoolean();
            _inSpriteFetch = reader.ReadBoolean();
            _cachedExRamTile = reader.ReadByte();

            UpdatePrgBanks();
        }

        public override bool PpuRead(ushort address, out byte data)
        {
            if (address >= 0x2000 && address <= 0x3FFF)
            {
                // Latch ExRAM for ExGraphics (Mode 1)
                // This latches the byte corresponding to the current nametable address.
                // Critical Fix: Only latch on NameTable fetches (Offsets 0-959), NOT Attribute Table fetches (Offsets 960-1023).
                // If we latch on AT fetches, we overwrite the valid tile data with AT garbage, causing wrong CHR banks on subsequent PT fetches.
                if (_exRamMode == 1 && (address & 0x03FF) < 0x03C0)
                {
                    _cachedExRamTile = _exRam[address & 0x03FF];
                }

                 int quad = (address & 0x0C00) >> 10; 
                 int mode = (_ntMapping >> (quad * 2)) & 3;
                 
                 // Handle Modes 0 and 1 (Internal CIRAM)
                 if (mode == 0) // CIRAM Page 0
                 {
                     data = _internalCiram[address & 0x03FF]; return true;
                 }
                 else if (mode == 1) // CIRAM Page 1
                 {
                     data = _internalCiram[1024 + (address & 0x03FF)]; return true;
                 }
                 else if (mode == 2)
                 {
                     if(_exRamMode == 0 || _exRamMode == 1) { data = _exRam[address & 0x03FF]; return true; }
                 }
                 else if (mode == 3)
                 {
                     if ((address & 0x03FF) >= 0x3C0) // Attribute table for Fill Mode
                     { 
                        byte c = (byte)(_fillColor & 3); 
                        data = (byte)(c | c<<2 | c<<4 | c<<6); 
                        return true; 
                     }
                     else 
                     { 
                        data = _fillTile; 
                        return true; 
                     }
                 }
            }
            data = 0; return false;
        }

        public override bool PpuWrite(ushort address, byte data)
        {
            if (address >= 0x2000 && address <= 0x3FFF)
            {
                int quad = (address & 0x0C00) >> 10;
                int mode = (_ntMapping >> (quad * 2)) & 3;

                if (mode == 0)
                {
                    _internalCiram[address & 0x03FF] = data; return true;
                }
                else if (mode == 1)
                {
                    _internalCiram[1024 + (address & 0x03FF)] = data; return true;
                }
                else if (mode == 2)
                {
                    if (_exRamMode == 0 || _exRamMode == 1)
                    {
                        _exRam[address & 0x03FF] = data; return true;
                    }
                }
                return true; // Mode 3 writes ignored
            }
            return false;
        }

        public override void NotifyPpuAddress(ushort address, int cycle)
        {
             // Ppu.cs passes: (Scanline + 1) * 341 + Cycle
             // We need to decode this to find the actual PPU scanline.
             // Scanline 0 (first visible) -> passed index 1.
             // Scanline 239 (last visible) -> passed index 240.
             
             int rawScanline = cycle / 341;
             int scanline = rawScanline - 1; 
             int dot = cycle % 341;
             
             // Update InFrame Status
             // InFrame is set if we are rendering a visible scanline (0-239) and rendering is enabled via $2001.
             bool newInFrame = (scanline >= 0 && scanline < 240 && _renderingEnabled);
             
             if (newInFrame != _inFrame)
             {
                 _inFrame = newInFrame;
                 if (_inFrame) 
                 {
                     _irqStatus |= 0x40; // Set InFrame bit (6)
                     _irqStatus &= 0x7F; // Clear Pending IRQ bit (7)
                     _irqActive = false;
                 }
                 else
                 {
                     _irqStatus &= 0xBF; // Clear InFrame bit (6)
                     // When leaving frame (entering VBlank), clear scanline counter
                     _irqScanline = 0;
                     _irqStatus &= 0x7F; // Clear Pending IRQ bit (7)
                     _irqActive = false;
                 }
             }

             // Sprite Fetch Detection (257-320)
             _inSpriteFetch = (dot >= 257 && dot <= 320);

             // Scanline Detection & IRQ
             // We detect a new scanline when the scanline index changes.
             if (_inFrame && scanline != _prevScanline)
             {
                 _prevScanline = scanline;
                 _irqScanline = scanline; // God-mode sync to PPU scanline
                 
                 // Trigger IRQ if line matches
                 if (_irqScanline == _irqLine)
                 {
                     _irqStatus |= 0x80; // Set Pending IRQ bit
                     if ((_irqEnable & 0x80) != 0)
                     {
                         _irqActive = true;
                     }
                 }
             }
        }
    }
}
