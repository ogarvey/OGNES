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

        // Memory
        private byte[] _vram = new byte[2048]; // 2KB of internal VRAM (Name Tables)
        private byte[] _paletteRam = new byte[32];
        private byte[] _oam = new byte[256];

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
        public bool NmiOutput => (_ppuCtrl & 0x80) != 0;

        public void Tick()
        {
            if (Scanline >= 0 && Scanline < 240 && Cycle >= 0 && Cycle < 256)
            {
                // For now, just render the background color
                byte paletteIndex = PpuRead(0x3F00);
                uint color = NesPalette[paletteIndex & 0x3F];
                int pixelIndex = (Scanline * 256 + Cycle) * 4;
                FrameBuffer[pixelIndex] = (byte)((color >> 24) & 0xFF);
                FrameBuffer[pixelIndex + 1] = (byte)((color >> 16) & 0xFF);
                FrameBuffer[pixelIndex + 2] = (byte)((color >> 8) & 0xFF);
                FrameBuffer[pixelIndex + 3] = (byte)(color & 0xFF);
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
                        NmiOccurred = true;
                    }
                    FrameReady = true;
                }
                else if (Scanline == 261)
                {
                    _ppuStatus &= 0x1F; // Clear VBlank, Sprite 0 hit, Sprite overflow
                    Scanline = 0;
                }
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
                    break;
                case 0x0005: // PPUSCROLL (Write only)
                    break;
                case 0x0006: // PPUADDR (Write only)
                    break;
                case 0x0007: // PPUDATA
                    data = _ppuDataBuffer;
                    _ppuDataBuffer = PpuRead(_v);
                    if (_v >= 0x3F00) data = _ppuDataBuffer; // Palette reads are immediate
                    _v += (ushort)((_ppuCtrl & 0x04) != 0 ? 32 : 1);
                    break;
            }
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
                        NmiOccurred = true;
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
                    _oam[_oamAddr++] = data;
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
