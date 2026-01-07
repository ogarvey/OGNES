using System;
using System.IO;

namespace OGNES.Components
{
    public enum PpuVariant
    {
        Standard_2C02,
        RGB_2C03,
        RGB_2C05,
        Scrambled_2C04_0001,
        Scrambled_2C04_0002,
        Scrambled_2C04_0003,
        Scrambled_2C04_0004,
        PAL_2C07
    }

    public enum GammaCorrection
    {
        None = 0,           // Treat input as sRGB (Direct map)
        Standard = 1,       // Treat input as Linear (Apply sRGB encoding)
        P22 = 2,            // Treat input as Signal, apply 2.2 Gamma -> Linear -> sRGB
        MeasuredCrt = 3,    // Treat input as Signal, apply 2.5 Gamma (CRT) -> Linear -> sRGB
        Smpte240M = 4       // Treat input as Signal, apply SMPTE 240M -> Linear -> sRGB
    }

    public class Ppu
    {
        // Registers
        private byte _ppuCtrl;   // $2000
        private byte _ppuMask;   // $2001
        private byte _ppuStatus; // $2002
        private byte _oamAddr;   // $2003
        private byte _ppuDataBuffer; // Internal buffer for $2007 reads
        private byte _ppuGenLatch; // Last value written to a PPU register (PPUGenLatch)

        // Decay register state
        private long _totalCycles;
        private long[] _decayTimers = new long[8];
        private const long DecayDuration = 3221591; // ~600ms in PPU cycles

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

        // Delayed V update
        private ushort _pendingV;
        private int _vUpdateTimer;
        private int _nmiDelay;

        // Memory
        private byte[] _vram = new byte[2048]; // 2KB of internal VRAM (Name Tables)
        private byte[] _paletteRam = new byte[32];
        private byte[] _oam = new byte[256];

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_ppuCtrl);
            writer.Write(_ppuMask);
            writer.Write(_ppuStatus);
            writer.Write(_oamAddr);
            writer.Write(_ppuDataBuffer);
            writer.Write(_ppuGenLatch);
            writer.Write(_v);
            writer.Write(_t);
            writer.Write(_x);
            writer.Write(_w);
            writer.Write(_bgNextTileId);
            writer.Write(_bgNextTileAttr);
            writer.Write(_bgNextTileLsb);
            writer.Write(_bgNextTileMsb);
            writer.Write(_bgShiftPatternLo);
            writer.Write(_bgShiftPatternHi);
            writer.Write(_bgShiftAttribLo);
            writer.Write(_bgShiftAttribHi);
            writer.Write(_secondaryOam);
            writer.Write(_spriteCount);
            writer.Write(_spriteShiftLo);
            writer.Write(_spriteShiftHi);
            writer.Write(_spriteAttrib);
            writer.Write(_spriteX);
            writer.Write(_sprite0OnScanline);
            writer.Write(_oddFrame);
            writer.Write(_pendingV);
            writer.Write(_vUpdateTimer);
            writer.Write(_vram);
            writer.Write(_paletteRam);
            writer.Write(_oam);
            writer.Write(Scanline);
            writer.Write(Cycle);
            writer.Write(NmiOccurred);
            writer.Write(TriggerNmi);
            writer.Write(_totalCycles);
            for (int i = 0; i < 8; i++) writer.Write(_decayTimers[i]);
        }

        public void LoadState(BinaryReader reader)
        {
            _ppuCtrl = reader.ReadByte();
            _ppuMask = reader.ReadByte();
            _ppuStatus = reader.ReadByte();
            _oamAddr = reader.ReadByte();
            _ppuDataBuffer = reader.ReadByte();
            _ppuGenLatch = reader.ReadByte();
            _v = reader.ReadUInt16();
            _t = reader.ReadUInt16();
            _x = reader.ReadByte();
            _w = reader.ReadByte();
            _bgNextTileId = reader.ReadByte();
            _bgNextTileAttr = reader.ReadByte();
            _bgNextTileLsb = reader.ReadByte();
            _bgNextTileMsb = reader.ReadByte();
            _bgShiftPatternLo = reader.ReadUInt16();
            _bgShiftPatternHi = reader.ReadUInt16();
            _bgShiftAttribLo = reader.ReadUInt16();
            _bgShiftAttribHi = reader.ReadUInt16();
            _secondaryOam = reader.ReadBytes(32);
            _spriteCount = reader.ReadInt32();
            _spriteShiftLo = reader.ReadBytes(8);
            _spriteShiftHi = reader.ReadBytes(8);
            _spriteAttrib = reader.ReadBytes(8);
            _spriteX = reader.ReadBytes(8);
            _sprite0OnScanline = reader.ReadBoolean();
            _oddFrame = reader.ReadBoolean();
            _pendingV = reader.ReadUInt16();
            _vUpdateTimer = reader.ReadInt32();
            _vram = reader.ReadBytes(2048);
            _paletteRam = reader.ReadBytes(32);
            _oam = reader.ReadBytes(256);
            Scanline = reader.ReadInt32();
            Cycle = reader.ReadInt32();
            NmiOccurred = reader.ReadBoolean();
            TriggerNmi = reader.ReadBoolean();
            try {
                _totalCycles = reader.ReadInt64();
                for (int i = 0; i < 8; i++) _decayTimers[i] = reader.ReadInt64();
            } catch (EndOfStreamException) {
                // Handle old save states
                _totalCycles = 0;
                for (int i = 0; i < 8; i++) _decayTimers[i] = 0;
            }
        }

        public byte[] Vram => _vram;
        public byte[] PaletteRam => _paletteRam;
        public byte[] Oam => _oam;

        // Debugging helpers
        public byte[] SpritePatternTableHistory { get; } = new byte[240]; // 0 or 1
        public byte[][] NametablePatternTableMap { get; } = new byte[4][]; // [NT][Row] -> 0, 1, or 255
        public Cartridge.Mirror[] MirroringHistory { get; } = new Cartridge.Mirror[240];
        public int[][] ChrBankHistory { get; } = new int[8][]; // [Chunk][Scanline] -> Offset

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

        public byte[] FrameBuffer => _enableNtsc ? _ntscFrameBuffer : _standardFrameBuffer;
        public ushort[] IndexBuffer { get; } = new ushort[256 * 240];

        // Ensure we always return 256 for width, as NTSC is now downsampled/decimated to 256
        public int FrameWidth => 256; 
        public int FrameHeight => 240;

        private NtscFilter _ntscFilter;
        private byte[] _standardFrameBuffer = new byte[256 * 240 * 4];
        private byte[] _ntscFrameBuffer;
        private bool _enableNtsc;

        public bool EnableNtsc
        {
            get => _enableNtsc;
            set
            {
                _enableNtsc = value;
                if (_enableNtsc && _ntscFrameBuffer == null)
                {
                    _ntscFilter.SetSize(256, 240);
                    _ntscFrameBuffer = new byte[_ntscFilter.ScaledWidth * 240 * 4];
                    _ntscFilter.SetOutputBuffer(_ntscFrameBuffer);
                }
            }
        }

        public void RegenerateFrameBuffer()
        {
            if (_enableNtsc)
            {
                 // ensure buffer is set
                 if (_ntscFrameBuffer == null) EnableNtsc = true; // triggers init
                 _ntscFilter.FilterFrame(IndexBuffer, 0); // 0 frame count for now
                 return;
            }

            if (_palettes == null) return;
            for (int i = 0; i < 256 * 240; i++)
            {
                ushort val = IndexBuffer[i];
                int colorIndex = val & 0x3F;
                int emphasis = (val >> 6) & 0x07;

                uint color = _palettes[emphasis][colorIndex];
                // Map to RGBA in memory: [R, G, B, A]
                // Compatible with GLPixelFormat.Rgba
                int pixelIndex = i * 4;
                _standardFrameBuffer[pixelIndex] = (byte)((color >> 24) & 0xFF);     // R
                _standardFrameBuffer[pixelIndex + 1] = (byte)((color >> 16) & 0xFF);  // G
                _standardFrameBuffer[pixelIndex + 2] = (byte)((color >> 8) & 0xFF); // B
                _standardFrameBuffer[pixelIndex + 3] = (byte)(color & 0xFF); // A
            }
        }
        public bool FrameReady { get; set; }

        private static readonly uint[] DefaultPalette = {
            0x666666FF, 0x002A88FF, 0x1412A7FF, 0x3B00A4FF, 0x5C007EFF, 0x6E0040FF, 0x670600FF, 0x561D00FF, 0x333500FF, 0x0B4800FF, 0x005200FF, 0x004F08FF, 0x00404DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xADADADFF, 0x155FD9FF, 0x4240FFFF, 0x7527FEFF, 0xA01ACCFF, 0xB71E7BFF, 0xB53120FF, 0x994E00FF, 0x6B6D00FF, 0x388700FF, 0x0C9300FF, 0x008F32FF, 0x007C8DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0x64B0FFFF, 0x9290FFFF, 0xC676FFFF, 0xF36AFFFF, 0xFE6ECCFF, 0xFE8170FF, 0xEA9E22FF, 0xBCBE00FF, 0x88D800FF, 0x5CE430FF, 0x45E082FF, 0x48CDDEFF, 0x4F4F4FFF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0xC0DFFFFF, 0xD1D8FFFF, 0xE8CDFFFF, 0xFBCCFFFF, 0xFECDF5FF, 0xFED5D7FF, 0xFEE2B5FF, 0xEDEB9EFF, 0xD6F296FF, 0xC2F6AFFF, 0xB7F4CCFF, 0xB8ECF0FF, 0xBDBDBDFF, 0x000000FF, 0x000000FF
        };

        private static readonly byte[] PaletteLUT_2C04_0001 = {
            0x35,0x23,0x16,0x22,0x1C,0x09,0x1D,0x15,0x20,0x00,0x27,0x05,0x04,0x28,0x08,0x20,
            0x21,0x3E,0x1F,0x29,0x3C,0x32,0x36,0x12,0x3F,0x2B,0x2E,0x1E,0x3D,0x2D,0x24,0x01,
            0x0E,0x31,0x33,0x2A,0x2C,0x0C,0x1B,0x14,0x2E,0x07,0x34,0x06,0x13,0x02,0x26,0x2E,
            0x2E,0x19,0x10,0x0A,0x39,0x03,0x37,0x17,0x0F,0x11,0x0B,0x0D,0x38,0x25,0x18,0x3A
        };

        private static readonly byte[] PaletteLUT_2C04_0002 = {
            0x2E,0x27,0x18,0x39,0x3A,0x25,0x1C,0x31,0x16,0x13,0x38,0x34,0x20,0x23,0x3C,0x0B,
            0x0F,0x21,0x06,0x3D,0x1B,0x29,0x1E,0x22,0x1D,0x24,0x0E,0x2B,0x32,0x08,0x2E,0x03,
            0x04,0x36,0x26,0x33,0x11,0x1F,0x10,0x02,0x14,0x3F,0x00,0x09,0x12,0x2E,0x28,0x20,
            0x3E,0x0D,0x2A,0x17,0x0C,0x01,0x15,0x19,0x2E,0x2C,0x07,0x37,0x35,0x05,0x0A,0x2D
        };

        private static readonly byte[] PaletteLUT_2C04_0003 = {
            0x14,0x25,0x3A,0x10,0x0B,0x20,0x31,0x09,0x01,0x2E,0x36,0x08,0x15,0x3D,0x3E,0x3C,
            0x22,0x1C,0x05,0x12,0x19,0x18,0x17,0x1B,0x00,0x03,0x2E,0x02,0x16,0x06,0x34,0x35,
            0x23,0x0F,0x0E,0x37,0x0D,0x27,0x26,0x20,0x29,0x04,0x21,0x24,0x11,0x2D,0x2E,0x1F,
            0x2C,0x1E,0x39,0x33,0x07,0x2A,0x28,0x1D,0x0A,0x2E,0x32,0x38,0x13,0x2B,0x3F,0x0C
        };

        private static readonly byte[] PaletteLUT_2C04_0004 = {
            0x18,0x03,0x1C,0x28,0x2E,0x35,0x01,0x17,0x10,0x1F,0x2A,0x0E,0x36,0x37,0x0B,0x39,
            0x25,0x1E,0x12,0x34,0x2E,0x1D,0x06,0x26,0x3E,0x1B,0x22,0x19,0x04,0x2E,0x3A,0x21,
            0x05,0x0A,0x07,0x02,0x13,0x14,0x00,0x15,0x0C,0x3D,0x11,0x0F,0x0D,0x38,0x2D,0x24,
            0x33,0x20,0x08,0x16,0x3F,0x2B,0x20,0x3C,0x2E,0x27,0x23,0x31,0x29,0x32,0x2C,0x09
        };

        public uint[] CurrentPalette { get; private set; } = (uint[])DefaultPalette.Clone();
        private uint[] _basePalette = (uint[])DefaultPalette.Clone();
        private uint[][] _palettes;
        private byte[]? _currentLut = null;
        
        private PpuVariant _variant = PpuVariant.Standard_2C02;
        private GammaCorrection _gammaMode = GammaCorrection.P22;

        public GammaCorrection GammaMode
        {
            get => _gammaMode;
            set
            {
                if (_gammaMode != value)
                {
                    _gammaMode = value;
                    RegenerateEmphasisPalettes();
                    UpdateCurrentPalette();
                }
            }
        }

        public PpuVariant Variant
        {
            get => _variant;
            set
            {
                if (_variant != value)
                {
                    _variant = value;
                    switch (_variant)
                    {
                        case PpuVariant.Scrambled_2C04_0001: SetLut(PaletteLUT_2C04_0001); break;
                        case PpuVariant.Scrambled_2C04_0002: SetLut(PaletteLUT_2C04_0002); break;
                        case PpuVariant.Scrambled_2C04_0003: SetLut(PaletteLUT_2C04_0003); break;
                        case PpuVariant.Scrambled_2C04_0004: SetLut(PaletteLUT_2C04_0004); break;
                        default: SetLut(null); break;
                    }
                    RegenerateEmphasisPalettes();
                    UpdateCurrentPalette();
                }
            }
        }

        public Ppu()
        {
            _ntscFilter = new NtscFilter();
            _ntscFilter.SetSize(256, 240);
            _ntscFrameBuffer = new byte[_ntscFilter.ScaledWidth * 240 * 4];
            _ntscFilter.SetOutputBuffer(_ntscFrameBuffer);

            _palettes = new uint[8][];
            for (int i = 0; i < 8; i++)
            {
                _palettes[i] = (uint[])DefaultPalette.Clone();
            }
            RegenerateEmphasisPalettes();

            for (int i = 0; i < 4; i++)
            {
                NametablePatternTableMap[i] = new byte[30];
                Array.Fill(NametablePatternTableMap[i], (byte)255);
            }
            for (int i = 0; i < 8; i++)
            {
                ChrBankHistory[i] = new int[240];
            }
        }

        public void LoadPalette(string filePath)
        {
            if (filePath == "2C04-0001") { Variant = PpuVariant.Scrambled_2C04_0001; return; }
            if (filePath == "2C04-0002") { Variant = PpuVariant.Scrambled_2C04_0002; return; }
            if (filePath == "2C04-0003") { Variant = PpuVariant.Scrambled_2C04_0003; return; }
            if (filePath == "2C04-0004") { Variant = PpuVariant.Scrambled_2C04_0004; return; }

            if (File.Exists(filePath))
            {
                _currentLut = null;
                // If the user loaded a custom file, default to 2C02 unless manually changed, 
                // but we should probably reset to a 'base' type if it was a specialized one.
                if (Variant.ToString().StartsWith("Scrambled")) Variant = PpuVariant.Standard_2C02;

                if (filePath.EndsWith(".fpal", StringComparison.OrdinalIgnoreCase))
                {
                    LoadFPal(filePath);
                    return;
                }

                byte[] palData = File.ReadAllBytes(filePath);
                if (palData.Length >= 192 * 8)
                {
                    for (int v = 0; v < 8; v++)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int offset = v * 192 + i * 3;
                            byte r = palData[offset];
                            byte g = palData[offset + 1];
                            byte b = palData[offset + 2];
                            _palettes[v][i] = (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
                        }
                    }
                    // Base is neutral (0)
                    Array.Copy(_palettes[0], _basePalette, 64);
                }
                else if (palData.Length >= 192)
                {
                    for (int i = 0; i < 64; i++)
                    {
                        byte r = palData[i * 3];
                        byte g = palData[i * 3 + 1];
                        byte b = palData[i * 3 + 2];
                        uint color = (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
                        _basePalette[i] = color;
                    }
                    RegenerateEmphasisPalettes();
                }
                UpdateCurrentPalette();
            }
        }

        private void LoadFPal(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
            using (var br = new BinaryReader(fs))
            {
                int count = (int)(fs.Length / 24); // 8 bytes * 3 double
                if (count < 64) return;

                // We assume 64 entries for the base palette if small, or 512 if full
                bool fullSet = count >= 512;

                int entriesToRead = fullSet ? 512 : 64;
                
                uint[] tempBuffer = new uint[entriesToRead];
                for (int i = 0; i < entriesToRead; i++)
                {
                    double r = br.ReadDouble();
                    double g = br.ReadDouble();
                    double b = br.ReadDouble();

                    // Apply the transformation requested
                    switch (GammaMode)
                    {
                        case GammaCorrection.None:
                            // Input is sRGB
                            break;
                        case GammaCorrection.Standard:
                            // Input is Linear, encode to sRGB
                            r = LinearTosRGB(r);
                            g = LinearTosRGB(g);
                            b = LinearTosRGB(b);
                            break;
                        case GammaCorrection.P22:
                            // Input is Signal (Gamma 2.2)
                            r = LinearTosRGB(Math.Pow(r, 2.2));
                            g = LinearTosRGB(Math.Pow(g, 2.2));
                            b = LinearTosRGB(Math.Pow(b, 2.2));
                            break;
                        case GammaCorrection.MeasuredCrt:
                            // Input is Signal (measured/approx Gamma 2.5)
                            r = LinearTosRGB(Math.Pow(r, 2.5));
                            g = LinearTosRGB(Math.Pow(g, 2.5));
                            b = LinearTosRGB(Math.Pow(b, 2.5));
                            break;
                        case GammaCorrection.Smpte240M:
                            // Input is Signal (SMPTE 240M)
                            r = LinearTosRGB(Smpte240MToLinear(r));
                            g = LinearTosRGB(Smpte240MToLinear(g));
                            b = LinearTosRGB(Smpte240MToLinear(b));
                            break;
                    }

                    byte rb = (byte)(Math.Clamp(r * 255.0, 0, 255));
                    byte gb = (byte)(Math.Clamp(g * 255.0, 0, 255));
                    byte bb = (byte)(Math.Clamp(b * 255.0, 0, 255));

                    tempBuffer[i] = (uint)((rb << 24) | (gb << 16) | (bb << 8) | 0xFF);
                }

                if (fullSet)
                {
                     for (int v = 0; v < 8; v++)
                     {
                         Array.Copy(tempBuffer, v * 64, _palettes[v], 0, 64);
                     }
                     Array.Copy(_palettes[0], _basePalette, 64);
                }
                else
                {
                    Array.Copy(tempBuffer, 0, _basePalette, 0, 64);
                    RegenerateEmphasisPalettes();
                }
            }
            UpdateCurrentPalette();
        }

        private static double Smpte240MToLinear(double v)
        {
            // EOTF: L = ((V + 0.1115) / 1.1115) ^ (1/0.45) if V >= 0.0913
            //       L = V / 4.0 if V < 0.0913
            // Note: 0.0913 is approx 4.0 * 0.0228
            if (v < 0.0913) return v / 4.0;
            return Math.Pow((v + 0.1115) / 1.1115, 2.22222222222);
        }

        private static double LinearTosRGB(double linear)
        {
            if (linear <= 0.0031308) return linear * 12.92;
            return 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        }

        private void RegenerateEmphasisPalettes()
        {
            // First copy base to all
            for (int v = 0; v < 8; v++)
            {
                 // If not generating, copy base
                 Array.Copy(_basePalette, _palettes[v], 64);
            }

            // If 2C03/2C05, emphasis is largely ignored or works differently, we stick to base?
            // Wiki: "2C03... emphasis bits are ignored."
            if (Variant == PpuVariant.RGB_2C03 || Variant == PpuVariant.RGB_2C05)
            {
                return;
            }

            // 2C04: Usually RGB, no emphasis?
            // "The 2C04... contains an internal palette ROM... emphasis bits are ignored" ?
            // Assuming 2C04 behaves like 2C03 for emphasis.
            if (Variant.ToString().StartsWith("Scrambled")) return;

            // Generate emphasis for 2C02 / 2C07
            // Attenuation factor
            const float atten = 0.746f;
            
            // 2C02: 7=B, 6=G, 5=R.
            // 2C07: 7=B, 6=R, 5=G. 
            
            for (int i = 0; i < 64; i++)
            {
                uint c = _basePalette[i];
                byte r = (byte)((c >> 24) & 0xFF);
                byte g = (byte)((c >> 16) & 0xFF);
                byte b = (byte)((c >> 8) & 0xFF);

                for (int v = 0; v < 8; v++)
                {
                    if (v == 0) continue; // Base

                    // Start with base components
                    float rf = r;
                    float gf = g;
                    float bf = b;

                    // Apply attenuation
                    bool bit5 = (v & 1) != 0;
                    bool bit6 = (v & 2) != 0;
                    bool bit7 = (v & 4) != 0;

                    // Determine which channels to attenuate
                    bool attenRed = false;
                    bool attenGreen = false;
                    bool attenBlue = false;

                    if (Variant == PpuVariant.PAL_2C07)
                    {
                        // 2C07: 7=B, 6=R, 5=G
                         if (bit6) { attenGreen = true; attenBlue = true; } // Red Emphasized -> Dim others ?
                         // No, emphasis bits "Emphasize" usually means "Don't Attenuate", while others are attenuated.
                         // But for NTSC, setting the bit *attenuates* the COMPLEMENTARY colors.
                         // NTSC 2C02:
                         // Bit 5 (R): Attenuates G, B.
                         // Bit 6 (G): Attenuates R, B.
                         // Bit 7 (B): Attenuates R, G.
                         
                         // Logic: if ANY bit is set that attenuates a channel, that channel gets dimmed.
                         // Bit 5 (R): G*=a, B*=a
                         // Bit 6 (G): R*=a, B*=a
                         // Bit 7 (B): R*=a, G*=a

                         if (bit5) { attenRed = true; attenBlue = true; } // Green bit on 2C07?
                         if (bit6) { attenGreen = true; attenBlue = true; } // Red bit on 2C07
                         if (bit7) { attenRed = true; attenGreen = true; } // Blue bit on 2C07
                    }
                    else // Standard 2C02
                    {
                         // Bit 5 (R): G, B
                         if (bit5) { attenGreen = true; attenBlue = true; }
                         // Bit 6 (G): R, B
                         if (bit6) { attenRed = true; attenBlue = true; }
                         // Bit 7 (B): R, G
                         if (bit7) { attenRed = true; attenGreen = true; }
                    }

                    if (attenRed) rf *= atten;
                    if (attenGreen) gf *= atten;
                    if (attenBlue) bf *= atten;

                    _palettes[v][i] = (uint)((((byte)rf) << 24) | (((byte)gf) << 16) | (((byte)bf) << 8) | 0xFF);
                }
            }
        }


        private void SetLut(byte[]? lut)
        {
            _currentLut = lut;
            UpdateCurrentPalette();
        }

        private void UpdateCurrentPalette()
        {
            if (_currentLut == null)
            {
                int emphasis = (_ppuMask >> 5) & 0x07;
                CurrentPalette = _palettes[emphasis];
            }
            else
            {
                for (int i = 0; i < 64; i++)
                {
                    CurrentPalette[i] = _basePalette[_currentLut[i] & 0x3F];
                }
            }
        }

        public void ResetPalette()
        {
            _basePalette = (uint[])DefaultPalette.Clone();
            for (int i = 0; i < 8; i++)
            {
                _palettes[i] = (uint[])DefaultPalette.Clone();
            }
            _currentLut = null;
            UpdateCurrentPalette();
        }

        public (uint[][] palettes, byte[]? lut) GetPaletteState()
        {
            uint[][] pals = new uint[8][];
            for (int i = 0; i < 8; i++) pals[i] = (uint[])_palettes[i].Clone();
            return (pals, _currentLut);
        }

        public void SetPaletteState(uint[][] palettes, byte[]? lut)
        {
            _palettes = palettes;
            _currentLut = lut;
            UpdateCurrentPalette();
        }

        public Cartridge? Cartridge { get; set; }
        public Joypad? Joypad { get; set; }

        public int Scanline { get; private set; } = 0;
        public int Cycle { get; private set; } = 0;

        public bool NmiOccurred { get; set; }
        public bool TriggerNmi { get; set; }
        public bool NmiOutput => (_ppuCtrl & 0x80) != 0;
        public bool RenderingEnabled => (_ppuMask & 0x18) != 0;

        public void Tick()
        {
            _totalCycles++;
            if (_vUpdateTimer > 0)
            {
                _vUpdateTimer--;
                if (_vUpdateTimer == 0)
                {
                    _v = _pendingV;
                    // Notify mapper of address change
                    int c = (Scanline + 1) * 341 + Cycle;
                    Cartridge?.NotifyPpuAddress(_v, c);
                }
            }

            if (_nmiDelay > 0)
            {
                _nmiDelay--;
                if (_nmiDelay == 0 && NmiOutput && (_ppuStatus & 0x80) != 0)
                {
                    TriggerNmi = true;
                }
            }

            if (Scanline >= -1 && Scanline < 240)
            {
                if (Scanline >= 0 && Cycle == 0)
                {
                    // Record Sprite PT
                    SpritePatternTableHistory[Scanline] = (byte)((_ppuCtrl & 0x08) != 0 ? 1 : 0);

                    if (Cartridge != null)
                    {
                        MirroringHistory[Scanline] = Cartridge.MirrorMode;
                        for (int i = 0; i < 8; i++)
                        {
                            ChrBankHistory[i][Scanline] = Cartridge.GetChrBankOffset((ushort)(i * 0x400));
                        }
                    }

                    // Record BG PT for the current Nametable Row
                    if (RenderingEnabled)
                    {
                        int nt = (_v >> 10) & 0x03;
                        int coarseY = (_v >> 5) & 0x1F;
                        if (coarseY < 30)
                        {
                            byte bgPt = (byte)((_ppuCtrl & 0x10) != 0 ? 1 : 0);
                            NametablePatternTableMap[nt][coarseY] = bgPt;
                        }
                    }
                }

                if (Scanline == -1 && Cycle == 3)
                {
                    _ppuStatus &= 0x1F; // Clear VBlank, Sprite 0 hit, Sprite overflow
                }

                bool rendering = RenderingEnabled;
                if (rendering)
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
                        if (Cycle == 257 && Scanline >= -1 && Scanline < 240)
                        {
                            EvaluateSprites(Scanline + 1);
                        }
                        
                        if (Scanline >= -1 && Scanline < 240)
                        {
                            ProcessSpriteFetch(Cycle, Scanline + 1);
                        }
                    }
                    else if (Cycle >= 321 && Cycle <= 336)
                    {
                        UpdateShifts();
                        ProcessFetch(Cycle % 8);
                    }
                }

                // Scroll increments and transfers
                if (rendering)
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
                    if ((Cycle >= 1 && Cycle < 256) || (Cycle >= 321 && Cycle <= 336))
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

                if (Scanline >= 261)
                {
                    Scanline = -1;
                    // Clear debug maps for the new frame
                    for (int i = 0; i < 4; i++) Array.Fill(NametablePatternTableMap[i], (byte)255);
                    
                    _oddFrame = !_oddFrame;
                    if (_oddFrame && RenderingEnabled)
                    {
                        // Skip cycle 0 on odd frames if rendering is enabled
                        Cycle = 1;
                    }
                }
            }

            if (Scanline == 241)
            {
                if (Cycle == 1)
                {
                    _ppuStatus |= 0x80;
                    FrameReady = true;

                    if (_enableNtsc)
                    {
                        if (_ntscFrameBuffer == null) EnableNtsc = true; // triggers init
                        _ntscFilter.FilterFrame(IndexBuffer, (ulong)_totalCycles); 
                    }
                }
                
                // Delay NMI trigger slightly to pass timing tests (approx 2 CPU cycles / 6 PPU cycles)
                if (Cycle == 6 && NmiOutput && (_ppuStatus & 0x80) != 0)
                {
                    TriggerNmi = true;
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

            if ((_ppuMask & 0x02) == 0 && (Cycle - 1) < 8)
            {
                bgPixel = 0;
                bgPalette = 0;
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

            if ((_ppuMask & 0x04) == 0 && (Cycle - 1) < 8)
            {
                fgPixel = 0;
                fgPalette = 0;
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
                        // Check if both pixels are non-transparent
                        if (bgPixel != 0 && fgPixel != 0)
                        {
                            if ((_ppuMask & 0x18) == 0x18) // Both BG and FG enabled
                            {
                                _ppuStatus |= 0x40;
                            }
                        }
                    }
                }
            }

            byte colorIndex = PeekVram((ushort)(0x3F00 | (pixel == 0 ? 0 : (palette << 2) | pixel)));
            if ((_ppuMask & 0x01) != 0) colorIndex &= 0x30; // Greyscale

            if (Joypad != null && Joypad.ZapperEnabled)
            {
                int x = Cycle - 1;
                int y = Scanline;
                if (x == Joypad.ZapperX && y == Joypad.ZapperY)
                {
                    bool isBright = (colorIndex & 0x30) >= 0x10;
                    if (colorIndex == 0x0F || colorIndex == 0x1D) isBright = false;

                    if (isBright)
                    {
                        Joypad.DetectLight(_totalCycles / 3);
                    }
                }
            }

            uint color = CurrentPalette[colorIndex & 0x3F];
            int pixelIndex = (Scanline * 256 + (Cycle - 1)) * 4;
            
            ushort emphasis = (ushort)(((_ppuMask >> 5) & 0x07) << 6);
            IndexBuffer[Scanline * 256 + (Cycle - 1)] = (ushort)((colorIndex & 0x3F) | emphasis);

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

        private void ProcessSpriteFetch(int cycle, int scanline)
        {
            int spriteIndex = (cycle - 257) / 8;
            int step = (cycle - 257) % 8;

            ushort addr;
            byte attrib = 0;

            if (spriteIndex < _spriteCount)
            {
                int spriteHeight = (_ppuCtrl & 0x20) != 0 ? 16 : 8;
                byte tileId = _secondaryOam[spriteIndex * 4 + 1];
                attrib = _secondaryOam[spriteIndex * 4 + 2];
                int y = _secondaryOam[spriteIndex * 4 + 0];
                int row = scanline - (y + 1);

                if ((attrib & 0x80) != 0) // Flip vertical
                {
                    row = (spriteHeight - 1) - row;
                }

                if (spriteHeight == 8)
                {
                    addr = (ushort)(((_ppuCtrl & 0x08) << 9) | (tileId << 4) | row);
                }
                else
                {
                    addr = (ushort)(((tileId & 0x01) << 12) | ((tileId & 0xFE) << 4) | (row & 0x07) | ((row & 0x08) << 1));
                }
            }
            else
            {
                // Dummy fetch for empty sprite slots
                // FCEUX uses tile index 0 for dummy fetches, which in 8x16 mode forces Pattern Table $0000.
                // This is critical for MMC3 IRQ timing (A12 toggles).
                // If we used PPUCTRL bit 3 (like for 8x8), we might stay in $1000 and miss the toggle.
                byte tileId = 0xFF;
                int spriteHeight = (_ppuCtrl & 0x20) != 0 ? 16 : 8;
                
                if (spriteHeight == 8)
                {
                    addr = (ushort)(((_ppuCtrl & 0x08) << 9) | (tileId << 4));
                }
                else
                {
                    // For 8x16, use the tile's LSB to select the bank.
                    // FCEUX uses tile 0 -> Bank $0000.
                    // We'll use tile $FF -> Bank $1000 (if we follow the bit 0 rule).
                    // This ensures empty slots keep A12 high, preventing double clocking of MMC3 IRQ.
                    
                    tileId = 0xFF; 
                    addr = (ushort)(((tileId & 0x01) << 12) | ((tileId & 0xFE) << 4));
                }
            }

            switch (step)
            {
                case 0: // Garbage NT fetch
                    PpuRead((ushort)(0x2000 | (_v & 0x0FFF)));
                    break;
                case 2: // Garbage AT fetch
                    PpuRead((ushort)(0x23C0 | (_v & 0x0C00) | ((_v >> 4) & 0x38) | ((_v >> 2) & 0x07)));
                    break;
                case 4: // PT Low
                    byte lsb = PpuRead(addr);
                    if (spriteIndex < _spriteCount)
                    {
                        if ((attrib & 0x40) != 0) lsb = FlipByte(lsb);
                        _spriteShiftLo[spriteIndex] = lsb;
                    }
                    break;
                case 6: // PT High
                    byte msb = PpuRead((ushort)(addr + 8));
                    if (spriteIndex < _spriteCount)
                    {
                        if ((attrib & 0x40) != 0) msb = FlipByte(msb);
                        _spriteShiftHi[spriteIndex] = msb;
                        _spriteAttrib[spriteIndex] = attrib;
                        _spriteX[spriteIndex] = _secondaryOam[spriteIndex * 4 + 3];
                    }
                    break;
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
            _ppuGenLatch = 0;
            _totalCycles = 0;
            for (int i = 0; i < 8; i++) _decayTimers[i] = 0;
            _v = 0;
            _t = 0;
            _x = 0;
            _w = 0;
            _oddFrame = false;
            _pendingV = 0;
            _vUpdateTimer = 0;
            _nmiDelay = 0;

            Array.Clear(SpritePatternTableHistory, 0, SpritePatternTableHistory.Length);
            Array.Clear(MirroringHistory, 0, MirroringHistory.Length);
            for (int i = 0; i < 4; i++) Array.Fill(NametablePatternTableMap[i], (byte)255);
            for (int i = 0; i < 8; i++) Array.Clear(ChrBankHistory[i], 0, ChrBankHistory[i].Length);
        }

        private void UpdateDecay()
        {
            for (int i = 0; i < 8; i++)
            {
                if ((_ppuGenLatch & (1 << i)) != 0)
                {
                    if (_totalCycles - _decayTimers[i] > DecayDuration)
                    {
                        _ppuGenLatch = (byte)(_ppuGenLatch & ~(1 << i));
                    }
                }
            }
        }

        private void RefreshDecay(byte value, byte mask)
        {
            for (int i = 0; i < 8; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    // Defined bit
                    int bit = (value >> i) & 1;
                    if (bit == 1)
                    {
                        _ppuGenLatch |= (byte)(1 << i);
                        _decayTimers[i] = _totalCycles;
                    }
                    else
                    {
                        _ppuGenLatch &= (byte)~(1 << i);
                    }
                }
            }
        }

        public byte CpuRead(ushort address)
        {
            UpdateDecay();
            byte mask = 0x00;
            byte val = 0;

            switch (address & 0x0007)
            {
                case 0x0000: // PPUCTRL (Write only)
                    break;
                case 0x0001: // PPUMASK (Write only)
                    break;
                case 0x0002: // PPUSTATUS
                    val = _ppuStatus;
                    
                    // Suppression logic: Reading $2002 near the time VBlank is set has special behavior.
                    // If read 1-2 cycles after set: Returns 0, but VBlank flag remains set (race condition where read misses the set).
                    // If read 3 cycles after set: Returns 0, VBlank flag is cleared, and NMI is suppressed.
                    // If read 4+ cycles after set: Returns 1, VBlank flag is cleared.
                    bool suppress = false;
                    bool preventClear = false;

                    if (Scanline == 241)
                    {
                        if (Cycle == 1 || Cycle == 2)
                        {
                            val &= 0x7F; // Read as 0
                            preventClear = true;
                        }
                        else if (Cycle == 3)
                        {
                            val &= 0x7F; // Read as 0
                            suppress = true;
                        }
                    }

                    if (suppress)
                    {
                        _ppuStatus &= 0x7F;
                        TriggerNmi = false;
                    }
                    else if (!preventClear)
                    {
                        _ppuStatus &= 0x7F; // Clear VBlank flag
                    }

                    mask = 0xE0; // Bits 7,6,5 defined
                    _w = 0; // Reset write latch
                    break;
                case 0x0003: // OAMADDR (Write only)
                    break;
                case 0x0004: // OAMDATA
                    val = _oam[_oamAddr];
                    if ((_oamAddr & 0x03) == 0x02)
                    {
                        val &= 0xE3; // Clear bits 2-4 for byte 2 (Attributes)
                    }
                    mask = 0xFF;
                    break;
                case 0x0005: // PPUSCROLL (Write only)
                    break;
                case 0x0006: // PPUADDR (Write only)
                    break;
                case 0x0007: // PPUDATA
                    if (_v >= 0x3F00)
                    {
                        // Palette reads are immediate, but still update the buffer with VRAM data "underneath"
                        val = PpuRead(_v);
                        _ppuDataBuffer = _vram[MapVramAddress(_v)];
                        mask = 0x3F; // Bits 5-0 defined
                    }
                    else
                    {
                        val = _ppuDataBuffer;
                        _ppuDataBuffer = PpuRead(_v);
                        mask = 0xFF;
                    }
                    
                    _v += (ushort)((_ppuCtrl & 0x04) != 0 ? 32 : 1);
                    _v &= 0x3FFF;
                    // Notify mapper of address change due to increment
                    {
                        int c = (Scanline + 1) * 341 + Cycle;
                        Cartridge?.NotifyPpuAddress(_v, c);
                    }
                    break;
            }
            
            RefreshDecay(val, mask);
            return _ppuGenLatch;
        }

        public void CpuWrite(ushort address, byte data)
        {
            RefreshDecay(data, 0xFF);
            switch (address & 0x0007)
            {
                case 0x0000: // PPUCTRL
                    bool nmiBefore = (_ppuCtrl & 0x80) != 0;
                    _ppuCtrl = data;
                    bool nmiAfter = (_ppuCtrl & 0x80) != 0;
                    if (!nmiBefore && nmiAfter && (_ppuStatus & 0x80) != 0)
                    {
                        // If NMI is enabled while VBlank is set, trigger NMI immediately
                        // But with a small delay to ensure it's taken after the next instruction
                        _nmiDelay = 3; 
                    }
                    else if (nmiBefore && !nmiAfter)
                    {
                        TriggerNmi = false;
                        _nmiDelay = 0;
                    }
                    _t = (ushort)((_t & 0xF3FF) | ((data & 0x03) << 10));
                    break;
                case 0x0001: // PPUMASK
                    _ppuMask = data;
                    UpdateCurrentPalette();
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
                        byte valueToWrite = data;
                        if ((_oamAddr & 0x03) == 0x02)
                        {
                            valueToWrite &= 0xE3; // Clear bits 2-4 for byte 2 (Attributes)
                        }
                        _oam[_oamAddr++] = valueToWrite;
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
                        _pendingV = _t;
                        _vUpdateTimer = 3;
                        _w = 0;
                    }
                    // Notify mapper of address change - REMOVED to avoid double notification with Tick
                    // The actual v update happens in Tick() after 3 cycles
                    break;
                case 0x0007: // PPUDATA
                    if (RenderingEnabled && (Scanline >= -1 && Scanline < 240))
                    {
                        // During rendering, writes to $2007 are ignored but still increment the address
                        // In a more accurate emulator, this would trigger a coarse X/fine Y increment.
                    }
                    else
                    {
                        PpuWrite(_v, data);
                    }
                    _v += (ushort)((_ppuCtrl & 0x04) != 0 ? 32 : 1);
                    _v &= 0x3FFF;
                    // Notify mapper of address change due to increment
                    {
                        int c = (Scanline + 1) * 341 + Cycle;
                        Cartridge?.NotifyPpuAddress(_v, c);
                    }
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
            return PeekVram(address);
        }

        public byte PeekVram(ushort address)
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
                case Cartridge.Mirror.OnescreenLo:
                    return offset;
                case Cartridge.Mirror.OnescreenHi:
                    return 1024 + offset;
                default:
                    return address % 2048;
            }
        }
    }
}
