namespace OGNES.Components
{
    public class Ppu
    {
        // Registers
        private byte _ppuCtrl;   // $2000
        private byte _ppuMask;   // $2001
        private byte _ppuStatus; // $2002
        private byte _oamAddr;   // $2003
        private byte _ppuDataBuffer; // Internal buffer for $2007 reads
        private byte _staleBusContents; // Last value written to a PPU register

        // Internal registers for VRAM addressing
        private ushort _v; // Current VRAM address (15 bits)
        private ushort _t; // Temporary VRAM address (15 bits)
        private byte _x;   // Fine X scroll (3 bits)
        private byte _w;   // Write latch (1 bit)

        // Background rendering state
        private byte _bgNextTileId;
        private byte _bgNextTileAttr;
        private byte _bgNextTileLsb;
        private byte _bgNextTileMsb;

        private ushort _bgShiftPatternLo;
        private ushort _bgShiftPatternHi;
        private ushort _bgShiftAttribLo;
        private ushort _bgShiftAttribHi;

        // Sprite rendering state
        private byte[] _secondaryOam = new byte[32]; // 8 sprites max per scanline
        private int _spriteCount;
        private byte[] _spriteShiftLo = new byte[8];
        private byte[] _spriteShiftHi = new byte[8];
        private byte[] _spriteAttrib = new byte[8];
        private byte[] _spriteX = new byte[8];
        private bool _sprite0OnScanline;
        private bool _oddFrame;

        // Memory
        private byte[] _vram = new byte[2048]; // 2KB of internal VRAM (Name Tables)
        private byte[] _paletteRam = new byte[32];
        private byte[] _oam = new byte[256];

        public byte[] Vram => _vram;
        public byte[] PaletteRam => _paletteRam;
        public byte[] Oam => _oam;

        public byte PeekRegister(ushort address)
        {
            switch (address & 0x0007)
            {
                case 0x0000: return _ppuCtrl;
                case 0x0001: return _ppuMask;
                case 0x0002: return _ppuStatus;
                case 0x0003: return _oamAddr;
                case 0x0004: return _oam[_oamAddr];
                case 0x0007: return _ppuDataBuffer;
                default: return 0;
            }
        }

        public byte[] FrameBuffer { get; } = new byte[256 * 240 * 4];
        public bool FrameReady { get; set; }

        private static readonly uint[] NesPalette = {
            0x666666FF, 0x002A88FF, 0x1412A7FF, 0x3B00A4FF, 0x5C007EFF, 0x6E0040FF, 0x670600FF, 0x561D00FF, 0x333500FF, 0x0B4800FF, 0x005200FF, 0x004F08FF, 0x00404DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xADADADFF, 0x155FD9FF, 0x4240FFFF, 0x7527FEFF, 0xA01ACCFF, 0xB71E7BFF, 0xB53120FF, 0x994E00FF, 0x6B6D00FF, 0x388700FF, 0x0C9300FF, 0x008F32FF, 0x007C8DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0x64B0FFFF, 0x9290FFFF, 0xC676FFFF, 0xF36AFFFF, 0xFE6ECCFF, 0xFE8170FF, 0xEA9E22FF, 0xBCBE00FF, 0x88D800FF, 0x5CE430FF, 0x45E082FF, 0x48CDDEFF, 0x4F4F4FFF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0xC0DFFFFF, 0xD1D8FFFF, 0xE8CDFFFF, 0xFBCCFFFF, 0xFECDF5FF, 0xFED5D7FF, 0xFEE2B5FF, 0xEDEB9EFF, 0xD6F296FF, 0xC2F6AFFF, 0xB7F4CCFF, 0xB8ECF0FF, 0xBDBDBDFF, 0x000000FF, 0x000000FF
        };

        public Cartridge? Cartridge { get; set; }

        public int Scanline { get; private set; } = 0;
        public int Cycle { get; private set; } = 0;

        public bool NmiOccurred { get; set; }
        public bool TriggerNmi { get; set; }
        public bool NmiOutput => (_ppuCtrl & 0x80) != 0;
        public bool RenderingEnabled => (_ppuMask & 0x18) != 0;

        public void Tick()
        {
            if (Scanline >= -1 && Scanline < 240)
            {
                if (Scanline == -1 && Cycle == 1)
                {
                    _ppuStatus &= 0x1F; // Clear VBlank, Sprite 0 hit, Sprite overflow
                }

                if (RenderingEnabled)
                {
                    if (Cycle >= 1 && Cycle <= 256)
                    {
                        if (Scanline >= 0)
                        {
                            RenderPixel();
                        }
                        UpdateShifts();
                        ProcessFetch(Cycle % 8);
                    }
                    else if (Cycle >= 257 && Cycle <= 320)
                    {
                        if (Cycle == 257 && Scanline >= -1 && Scanline < 239)
                        {
                            EvaluateSprites(Scanline + 1);
                        }
                        if (Cycle == 320 && Scanline >= -1 && Scanline < 239)
                        {
                            FetchSprites(Scanline + 1);
                        }
                    }
                    else if (Cycle >= 321 && Cycle <= 336)
                    {
                        UpdateShifts();
                        ProcessFetch(Cycle % 8);
                    }
                }

                // Scroll increments and transfers
                if (RenderingEnabled)
                {
                    if (Cycle == 256)
                    {
                        IncrementScrollY();
                    }
                    if (Cycle == 257)
                    {
                        TransferAddressX();
                    }
                    if (Scanline == -1 && Cycle >= 280 && Cycle <= 304)
                    {
                        TransferAddressY();
                    }
                    if ((Cycle >= 1 && Cycle <= 256) || (Cycle >= 321 && Cycle <= 336))
                    {
                        if (Cycle % 8 == 0)
                        {
                            IncrementScrollX();
                        }
                    }
                }
            }

            Cycle++;
            if (Cycle >= 341)
            {
                Cycle = 0;
                Scanline++;

                if (Scanline == 241)
                {
                    _ppuStatus |= 0x80;
                    if (NmiOutput)
                    {
                        TriggerNmi = true;
                    }
                    FrameReady = true;
                }
                else if (Scanline >= 261)
                {
                    Scanline = -1;
                    _oddFrame = !_oddFrame;
                    if (_oddFrame && RenderingEnabled)
                    {
                        Cycle = 1;
                    }
                }
            }
        }

        private void ProcessFetch(int step)
        {
            switch (step)
            {
                case 1: // NT
                    _bgNextTileId = PpuRead((ushort)(0x2000 | (_v & 0x0FFF)));
                    break;
                case 3: // AT
                    ushort atAddr = (ushort)(0x23C0 | (_v & 0x0C00) | ((_v >> 4) & 0x38) | ((_v >> 2) & 0x07));
                    byte at = PpuRead(atAddr);
                    // Shift AT to get the 2 bits for the current 16x16 quadrant
                    int shift = ((_v >> 4) & 0x04) | (_v & 0x02);
                    _bgNextTileAttr = (byte)((at >> shift) & 0x03);
                    break;
                case 5: // Low PT
                    _bgNextTileLsb = PpuRead((ushort)(((_ppuCtrl & 0x10) << 8) | (_bgNextTileId << 4) | ((_v >> 12) & 0x07)));
                    break;
                case 7: // High PT
                    _bgNextTileMsb = PpuRead((ushort)(((_ppuCtrl & 0x10) << 8) | (_bgNextTileId << 4) | ((_v >> 12) & 0x07) | 8));
                    break;
                case 0: // Load shift registers
                    LoadShifts();
                    break;
            }
        }

        private void LoadShifts()
        {
            _bgShiftPatternLo = (ushort)((_bgShiftPatternLo & 0xFF00) | _bgNextTileLsb);
            _bgShiftPatternHi = (ushort)((_bgShiftPatternHi & 0xFF00) | _bgNextTileMsb);
            _bgShiftAttribLo = (ushort)((_bgShiftAttribLo & 0xFF00) | ((_bgNextTileAttr & 0x01) != 0 ? 0xFF : 0x00));
            _bgShiftAttribHi = (ushort)((_bgShiftAttribHi & 0xFF00) | ((_bgNextTileAttr & 0x02) != 0 ? 0xFF : 0x00));
        }

        private void UpdateShifts()
        {
            if ((_ppuMask & 0x08) != 0)
            {
                _bgShiftPatternLo <<= 1;
                _bgShiftPatternHi <<= 1;
                _bgShiftAttribLo <<= 1;
                _bgShiftAttribHi <<= 1;
            }
        }

        private void RenderPixel()
        {
            byte bgPalette = 0;
            byte bgPixel = 0;

            if ((_ppuMask & 0x08) != 0)
            {
                ushort bit = (ushort)(0x8000 >> _x);
                byte p0 = (byte)((_bgShiftPatternLo & bit) != 0 ? 1 : 0);
                byte p1 = (byte)((_bgShiftPatternHi & bit) != 0 ? 1 : 0);
                bgPixel = (byte)((p1 << 1) | p0);

                byte a0 = (byte)((_bgShiftAttribLo & bit) != 0 ? 1 : 0);
                byte a1 = (byte)((_bgShiftAttribHi & bit) != 0 ? 1 : 0);
                bgPalette = (byte)((a1 << 1) | a0);
            }

            byte fgPalette = 0;
            byte fgPixel = 0;
            bool fgPriority = false;
            bool isSprite0 = false;

            if ((_ppuMask & 0x10) != 0)
            {
                for (int i = 0; i < _spriteCount; i++)
                {
                    int offset = (Cycle - 1) - _spriteX[i];
                    if (offset >= 0 && offset < 8)
                    {
                        byte p0 = (byte)((_spriteShiftLo[i] & (0x80 >> offset)) != 0 ? 1 : 0);
                        byte p1 = (byte)((_spriteShiftHi[i] & (0x80 >> offset)) != 0 ? 1 : 0);
                        fgPixel = (byte)((p1 << 1) | p0);

                        if (fgPixel != 0)
                        {
                            fgPalette = (byte)(_spriteAttrib[i] & 0x03);
                            fgPriority = (_spriteAttrib[i] & 0x20) == 0;
                            isSprite0 = _sprite0OnScanline && i == 0;
                            break;
                        }
                    }
                }
            }

            byte pixel = 0;
            byte palette = 0;

            if (bgPixel == 0 && fgPixel == 0)
            {
                pixel = 0;
                palette = 0;
            }
            else if (bgPixel == 0 && fgPixel != 0)
            {
                pixel = fgPixel;
                palette = (byte)(fgPalette + 4);
            }
            else if (bgPixel != 0 && fgPixel == 0)
            {
                pixel = bgPixel;
                palette = bgPalette;
            }
            else
            {
                if (fgPriority)
                {
                    pixel = fgPixel;
                    palette = (byte)(fgPalette + 4);
                }
                else
                {
                    pixel = bgPixel;
                    palette = bgPalette;
                }

                if (isSprite0 && Cycle - 1 < 255)
                {
                    // Sprite 0 hit does not occur if clipping is enabled and we are in the leftmost 8 pixels
                    bool bgClip = (_ppuMask & 0x02) == 0 && (Cycle - 1) < 8;
                    bool fgClip = (_ppuMask & 0x04) == 0 && (Cycle - 1) < 8;

                    if (!bgClip && !fgClip)
                    {
                        _ppuStatus |= 0x40;
                    }
                }
            }

            byte colorIndex = PpuRead((ushort)(0x3F00 | (pixel == 0 ? 0 : (palette << 2) | pixel)));
            uint color = NesPalette[colorIndex & 0x3F];
            int pixelIndex = (Scanline * 256 + (Cycle - 1)) * 4;
            FrameBuffer[pixelIndex] = (byte)((color >> 24) & 0xFF);
            FrameBuffer[pixelIndex + 1] = (byte)((color >> 16) & 0xFF);
            FrameBuffer[pixelIndex + 2] = (byte)((color >> 8) & 0xFF);
            FrameBuffer[pixelIndex + 3] = (byte)(color & 0xFF);
        }

        private void EvaluateSprites(int scanline)
        {
            int spriteHeight = (_ppuCtrl & 0x20) != 0 ? 16 : 8;
            _spriteCount = 0;
            _sprite0OnScanline = false;

            for (int i = 0; i < 64; i++)
            {
                int y = _oam[i * 4];
                int row = scanline - (y + 1);

                if (row >= 0 && row < spriteHeight)
                {
                    if (_spriteCount < 8)
                    {
                        if (i == 0) _sprite0OnScanline = true;

                        _secondaryOam[_spriteCount * 4 + 0] = _oam[i * 4 + 0];
                        _secondaryOam[_spriteCount * 4 + 1] = _oam[i * 4 + 1];
                        _secondaryOam[_spriteCount * 4 + 2] = _oam[i * 4 + 2];
                        _secondaryOam[_spriteCount * 4 + 3] = _oam[i * 4 + 3];
                        _spriteCount++;
                    }
                    else
                    {
                        _ppuStatus |= 0x20; // Sprite Overflow
                        break;
                    }
                }
            }
        }

        private void FetchSprites(int scanline)
        {
            int spriteHeight = (_ppuCtrl & 0x20) != 0 ? 16 : 8;

            for (int i = 0; i < _spriteCount; i++)
            {
                byte tileId = _secondaryOam[i * 4 + 1];
                byte attrib = _secondaryOam[i * 4 + 2];
                int y = _secondaryOam[i * 4 + 0];
                int row = scanline - (y + 1);

                if ((attrib & 0x80) != 0) // Flip vertical
                {
                    row = (spriteHeight - 1) - row;
                }

                ushort addr;
                if (spriteHeight == 8)
                {
                    addr = (ushort)(((_ppuCtrl & 0x08) << 9) | (tileId << 4) | row);
                }
                else
                {
                    addr = (ushort)(((tileId & 0x01) << 12) | ((tileId & 0xFE) << 4) | (row & 0x07) | ((row & 0x08) << 1));
                }

                byte lsb = PpuRead(addr);
                byte msb = PpuRead((ushort)(addr + 8));

                if ((attrib & 0x40) != 0) // Flip horizontal
                {
                    lsb = FlipByte(lsb);
                    msb = FlipByte(msb);
                }

                _spriteShiftLo[i] = lsb;
                _spriteShiftHi[i] = msb;
                _spriteAttrib[i] = attrib;
                _spriteX[i] = _secondaryOam[i * 4 + 3];
            }
        }

        private byte FlipByte(byte b)
        {
            b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
            b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
            b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
            return b;
        }

        private void IncrementScrollX()
        {
            if ((_v & 0x001F) == 31)
            {
                _v &= 0xFFE0;
                _v ^= 0x0400;
            }
            else
            {
                _v++;
            }
        }

        private void IncrementScrollY()
        {
            if ((_v & 0x7000) != 0x7000)
            {
                _v += 0x1000;
            }
            else
            {
                _v &= 0x8FFF;
                int y = (_v & 0x03E0) >> 5;
                if (y == 29)
                {
                    y = 0;
                    _v ^= 0x0800;
                }
                else if (y == 31)
                {
                    y = 0;
                }
                else
                {
                    y++;
                }
                _v = (ushort)((_v & 0xFC1F) | (y << 5));
            }
        }

        private void TransferAddressX()
        {
            if (RenderingEnabled)
            {
                _v = (ushort)((_v & 0xFBE0) | (_t & 0x041F));
            }
        }

        private void TransferAddressY()
        {
            if (RenderingEnabled)
            {
                _v = (ushort)((_v & 0x841F) | (_t & 0x7BE0));
            }
        }

        public void Reset()
        {
            Scanline = 0;
            Cycle = 0;
            _ppuCtrl = 0;
            _ppuMask = 0;
            _ppuStatus = 0;
            _oamAddr = 0;
            _ppuDataBuffer = 0;
            _staleBusContents = 0;
            _v = 0;
            _t = 0;
            _x = 0;
            _w = 0;
            _oddFrame = false;
        }

        public byte CpuRead(ushort address)
        {
            byte data = _staleBusContents;
            switch (address & 0x0007)
            {
                case 0x0000: // PPUCTRL (Write only)
                    break;
                case 0x0001: // PPUMASK (Write only)
                    break;
                case 0x0002: // PPUSTATUS
                    data = (byte)((_ppuStatus & 0xE0) | (_staleBusContents & 0x1F));
                    _ppuStatus &= 0x7F; // Clear VBlank flag
                    _w = 0; // Reset write latch
                    break;
                case 0x0003: // OAMADDR (Write only)
                    break;
                case 0x0004: // OAMDATA
                    data = _oam[_oamAddr];
                    // Reading OAMDATA during rendering is not recommended but returns OAM data
                    break;
                case 0x0005: // PPUSCROLL (Write only)
                    break;
                case 0x0006: // PPUADDR (Write only)
                    break;
                case 0x0007: // PPUDATA
                    data = _ppuDataBuffer;
                    if (_v >= 0x3F00)
                    {
                        // Palette reads are immediate, but still update the buffer with VRAM data "underneath"
                        data = PpuRead(_v);
                        _ppuDataBuffer = _vram[MapVramAddress(_v)];
                    }
                    else
                    {
                        _ppuDataBuffer = PpuRead(_v);
                    }
                    _v += (ushort)((_ppuCtrl & 0x04) != 0 ? 32 : 1);
                    _v &= 0x3FFF;
                    break;
            }
            _staleBusContents = data;
            return data;
        }

        public void CpuWrite(ushort address, byte data)
        {
            _staleBusContents = data;
            switch (address & 0x0007)
            {
                case 0x0000: // PPUCTRL
                    bool nmiBefore = (_ppuCtrl & 0x80) != 0;
                    _ppuCtrl = data;
                    bool nmiAfter = (_ppuCtrl & 0x80) != 0;
                    if (!nmiBefore && nmiAfter && (_ppuStatus & 0x80) != 0)
                    {
                        // If NMI is enabled while VBlank is set, trigger NMI immediately
                        TriggerNmi = true;
                    }
                    else if (nmiBefore && !nmiAfter)
                    {
                        TriggerNmi = false;
                    }
                    _t = (ushort)((_t & 0xF3FF) | ((data & 0x03) << 10));
                    break;
                case 0x0001: // PPUMASK
                    _ppuMask = data;
                    break;
                case 0x0002: // PPUSTATUS (Read only)
                    break;
                case 0x0003: // OAMADDR
                    _oamAddr = data;
                    break;
                case 0x0004: // OAMDATA
                    if (RenderingEnabled && (Scanline >= -1 && Scanline < 240))
                    {
                        // Writes to OAMDATA during rendering are generally ignored or corrupt OAM
                        // Some sources say it increments OAMADDR, others say it doesn't.
                    }
                    else
                    {
                        _oam[_oamAddr++] = data;
                    }
                    break;
                case 0x0005: // PPUSCROLL
                    if (_w == 0)
                    {
                        _t = (ushort)((_t & 0xFFE0) | (data >> 3));
                        _x = (byte)(data & 0x07);
                        _w = 1;
                    }
                    else
                    {
                        _t = (ushort)((_t & 0x8C1F) | ((data & 0x07) << 12) | ((data & 0xF8) << 2));
                        _w = 0;
                    }
                    break;
                case 0x0006: // PPUADDR
                    if (_w == 0)
                    {
                        _t = (ushort)((_t & 0x00FF) | ((data & 0x3F) << 8));
                        _w = 1;
                    }
                    else
                    {
                        _t = (ushort)((_t & 0xFF00) | data);
                        _v = _t;
                        _w = 0;
                    }
                    break;
                case 0x0007: // PPUDATA
                    PpuWrite(_v, data);
                    _v += (ushort)((_ppuCtrl & 0x04) != 0 ? 32 : 1);
                    break;
            }
        }

        public void WriteOam(byte address, byte data)
        {
            _oam[address] = data;
        }

        public byte PpuRead(ushort address)
        {
            address &= 0x3FFF;
            int cycle = (Scanline + 1) * 341 + Cycle;
            Cartridge?.NotifyPpuAddress(address, cycle);
            if (address < 0x2000)
            {
                if (Cartridge != null && Cartridge.PpuRead(address, out byte data))
                {
                    return data;
                }
                return 0;
            }
            else if (address < 0x3F00)
            {
                return _vram[MapVramAddress(address)];
            }
            else
            {
                address &= 0x001F;
                if (address == 0x0010) address = 0x0000;
                if (address == 0x0014) address = 0x0004;
                if (address == 0x0018) address = 0x0008;
                if (address == 0x001C) address = 0x000C;
                return _paletteRam[address];
            }
        }

        public void PpuWrite(ushort address, byte data)
        {
            address &= 0x3FFF;
            int cycle = (Scanline + 1) * 341 + Cycle;
            Cartridge?.NotifyPpuAddress(address, cycle);
            if (address < 0x2000)
            {
                Cartridge?.PpuWrite(address, data);
            }
            else if (address < 0x3F00)
            {
                _vram[MapVramAddress(address)] = data;
            }
            else
            {
                address &= 0x001F;
                if (address == 0x0010) address = 0x0000;
                if (address == 0x0014) address = 0x0004;
                if (address == 0x0018) address = 0x0008;
                if (address == 0x001C) address = 0x000C;
                _paletteRam[address] = data;
            }
        }

        private int MapVramAddress(ushort address)
        {
            address = (ushort)((address - 0x2000) % 0x1000);
            int table = address / 0x0400;
            int offset = address % 0x0400;

            if (Cartridge == null) return address % 2048;

            switch (Cartridge.MirrorMode)
            {
                case Cartridge.Mirror.Horizontal:
                    return (table < 2 ? 0 : 1024) + offset;
                case Cartridge.Mirror.Vertical:
                    return (table % 2 == 0 ? 0 : 1024) + offset;
                default:
                    return address % 2048;
            }
        }
    }
}
