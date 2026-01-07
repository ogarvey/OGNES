using System;
using System.Runtime.CompilerServices;

namespace OGNES.Components
{
    public class NtscFilter
    {
        private const int LSN_PM_NTSC_RENDER_WIDTH = 256;
        private const int LSN_PM_NTSC_DOTS_X = 341; // Standard NTSC dot width? Or scanline cycle count? 341.
        private const int LSN_SRGB_RES = 1024;

        private float _hueSetting = 0.0f;
        private float _gammaSetting = 2.2f; //1.0f / 0.45f;
        private float _brightnessSetting = 1.0f;
        private float _saturationSetting = 1.0f;
        private float _blackSetting = 0.312f;
        private float _whiteSetting = 1.100f;

        private uint _filterKernelSize = 12;

        private float[] _filter = new float[128];
        private float[] _filterY = new float[128];
        private float[] _phaseCosTable = new float[12];
        private float[] _phaseSinTable = new float[12];

        private float[] _normalizedLevels = new float[16];

        private byte[] _gammaTable = new byte[LSN_SRGB_RES];
        private byte[] _gammaTableG = new byte[LSN_SRGB_RES];

        // Buffers
        private float[][]? _signalBuffer; // [height][width * 8 + padding]
        private float[]? _yBuffer;
        private float[]? _iBuffer;
        private float[]? _qBuffer;
        private float[]? _blendBuffer; // For phosphor decay
        public byte[]? OutputBuffer => OutputBufferInternal;

        private int _width = 256;
        private int _height = 240;
        private int _widthScale = 8;
        private int _signalWidth;
        public int ScaledWidth => _width; // Return standard width now, as we downsample to it

        private static readonly float[] NtscLevels = new float[] {
            0.228f, 0.312f, 0.552f, 0.880f, // Signal low.
            0.616f, 0.840f, 1.100f, 1.100f, // Signal high.
            0.192f, 0.256f, 0.448f, 0.712f, // Signal low, attenuated.
            0.500f, 0.676f, 0.896f, 0.896f  // Signal high, attenuated.
        };

        public NtscFilter()
        {
            _hueSetting = 8.0f * (float)Math.PI / 180.0f;
            // Default bright/sat
            // _brightnessSetting = ...
            // _saturationSetting = ...

            SetSize(_width, _height);
            InitializeTables();
        }

        private void InitializeTables()
        {
            GenNormalizedSignals();
            GenPhaseTables(_hueSetting);
            GenFilterKernel(_filterKernelSize);
            SetGamma(_gammaSetting);
        }

        public void SetSize(int width, int height)
        {
            if (_width == width && _height == height && _signalBuffer != null) return;

            _width = width;
            _height = height;
            _signalWidth = _width * _widthScale;

            int rowSize = LSN_PM_NTSC_RENDER_WIDTH * 8 + (int)_filterKernelSize + 16;
            
            _signalBuffer = new float[_height][];
            for (int i = 0; i < _height; i++)
            {
                _signalBuffer[i] = new float[rowSize];
            }

            int outputSize = _width * _height;
            _yBuffer = new float[outputSize];
            _iBuffer = new float[outputSize];
            _qBuffer = new float[outputSize];
            _blendBuffer = new float[outputSize * 3]; // R, G, B floats
            OutputBufferInternal = new byte[outputSize * 4]; // BGRA bytes
        }

        private byte[]? OutputBufferInternal { get; set; }

        public void SetOutputBuffer(byte[] buffer)
        {
            OutputBufferInternal = buffer;
        }

        public void FilterFrame(ushort[] inputPixels, ulong renderStartCycle)
        {
            if (_signalBuffer == null || _yBuffer == null || _iBuffer == null || _qBuffer == null || OutputBufferInternal == null) return;
            // Single threaded scanline loop
            for (int y = 0; y < _height; y++)
            {
                RenderScanline(inputPixels, y, renderStartCycle);
            }
        }

        private void RenderScanline(ushort[] inputPixels, int y, ulong renderStartCycle)
        {
            // PPU dots per scanline is 341. PPU renders 256 pixels.
            ulong cycle = renderStartCycle + (ulong)(y * LSN_PM_NTSC_DOTS_X); 
            RenderScanlineRange(inputPixels, y, (ushort)((cycle * 8) % 12));
        }

        private void RenderScanlineRange(ushort[] inputPixels, int y, ushort initialPhase)
        {
            float[][] signalBuffer = _signalBuffer!;
            float[] yBuffer = _yBuffer!;
            float[] iBuffer = _iBuffer!;
            float[] qBuffer = _qBuffer!;
            byte[] outputBuffer = OutputBufferInternal!;

            int signalRowSize = signalBuffer[y].Length;
            // Center the signal in the buffer to allow kernel overhang
            int signalOffset = (int)(_filterKernelSize / 2) + ((int)_filterKernelSize & 1);
            
            float[] signalRow = signalBuffer[y];
            
            // Generate Signals
            // inputPixels is linear full frame.
            int inputOffset = y * LSN_PM_NTSC_RENDER_WIDTH;
            
            int currentSignalIdx = signalOffset;
            for (int x = 0; x < LSN_PM_NTSC_RENDER_WIDTH; x++)
            {
                ushort pixel = inputPixels[inputOffset + x];
                PixelToNtscSignals(signalRow, currentSignalIdx, pixel, (ushort)(initialPhase + x * 8));
                currentSignalIdx += 8;
            }

            // Convolution to YIQ
            int dstOffset = y * _width;
            
            // Loop over output pixels
            for (int i = 0; i < _width; i++)
            {
                // Map output index to input signal index
                // Input has 8 samples per pixel. scaledWidth is width * 8.
                // We sample at 256 evenly spaced locations.
                
                // Align center of pixel i in signal buffer.
                int center = (i * _widthScale) + signalOffset + (_widthScale / 2); // +4 for center
                int start = center - (int)(_filterKernelSize / 2);
                int end = center + (int)Math.Ceiling(_filterKernelSize / 2.0f);
                
                float yVal = 0, iVal = 0, qVal = 0;
                
                int k = 0;
                for (int j = start; j < end; j++)
                {
                    float sig = signalRow[j];
                    float level = sig * _filter[k];
                    yVal += sig * _filterY[k];
                    
                    int phase = (initialPhase + (12 * 4) + (j - signalOffset)) % 12;
                    
                    iVal += _phaseCosTable[phase] * level;
                    qVal += _phaseSinTable[phase] * level;
                    k++;
                }
                
                // Brightness
                yBuffer[dstOffset + i] = yVal * _brightnessSetting;
                iBuffer[dstOffset + i] = iVal; // * saturation implicitly via tables
                qBuffer[dstOffset + i] = qVal;
                
            }
            
            ConvertYiqToBgra(y, yBuffer, iBuffer, qBuffer, outputBuffer);
        }

        private void ConvertYiqToBgra(int y, float[] yBuffer, float[] iBuffer, float[] qBuffer, byte[] outputBuffer)
        {
            int offset = y * _width;
            int rgbOffset = offset * 4;
            int blendOffset = offset * 3;

            for (int i = 0; i < _width; i++)
            {
                 float Y = yBuffer[offset + i];
                 float I = iBuffer[offset + i];
                 float Q = qBuffer[offset + i];

                 // YIQ to RGB
                 // R = Y + 0.956 I + 0.621 Q
                 // G = Y - 0.272 I - 0.647 Q
                 // B = Y - 1.106 I + 1.703 Q
                 // The C++ code uses:
                 // R = Y + (1.139883025203f * V)  (V is I?)
                 // G = Y + (-0.394642233974f * U) + (-0.580621847591f * V)
                 // B = Y + (2.032061872219f * U) (U is Q?)
                 // Let's match C++:
                 // mY = Y, mU = Q, mV = I (Wait, usually U=Q, V=I?)
                 // pfQ is passed as mU, pfI passed as mV
                 // So U = Q, V = I.
                 
                 float R = Y + 1.139883025203f * I;
                 float G = Y + -0.394642233974f * Q + -0.580621847591f * I;
                 float B = Y + 2.032061872219f * Q;

                 // Clamp and Output
                 
                 // Gamma
                 int rIdx = Clamp(R * (LSN_SRGB_RES - 1));
                 int gIdx = Clamp(G * (LSN_SRGB_RES - 1));
                 int bIdx = Clamp(B * (LSN_SRGB_RES - 1));
                 
                 // Output RGBA
                 outputBuffer[rgbOffset + 0] = _gammaTable[rIdx]; // Red
                 outputBuffer[rgbOffset + 1] = _gammaTableG[gIdx]; // Green
                 outputBuffer[rgbOffset + 2] = _gammaTable[bIdx]; // Blue
                 outputBuffer[rgbOffset + 3] = 255;
                 
                 rgbOffset += 4;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Clamp(float v)
        {
            if (v < 0) return 0;
            if (v >= LSN_SRGB_RES) return LSN_SRGB_RES - 1;
            return (int)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PixelToNtscSignals(float[] dst, int dstIdx, ushort pixel, ushort cycle)
        {
            // Generate 8 samples
            for (int i = 0; i < 8; i++)
            {
                dst[dstIdx + i] = IndexToNtscSignal(pixel, (ushort)(cycle + i));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float IndexToNtscSignal(ushort pixel, ushort phase)
        {
            // Decode PPU 9-bit
            int color = (pixel & 0x0F);
            int level = (color >= 0xE) ? 1 : ((pixel >> 4) & 3); // 0..3
            int emphasis = (pixel >> 6); // 0..7

            // Color Phase check
            // ((COLOR) + _ui16Phase) % 12 < 6
            // Emph bits:
            // Red (0x1) -> Phase 0xC ? No. C++ says:
            // ((emph & 1) && InPhase(0xC)) || ((emph & 2) && InPhase(0x4)) || ((emph & 4) && InPhase(0x8))
            
            bool attenuated = false;
            if (color < 0xE)
            {
                if ((emphasis & 1) != 0 && IsInColorPhase(0xC, phase)) attenuated = true;
                else if ((emphasis & 2) != 0 && IsInColorPhase(0x4, phase)) attenuated = true;
                else if ((emphasis & 4) != 0 && IsInColorPhase(0x8, phase)) attenuated = true;
            }
            
            int attenOffset = attenuated ? 8 : 0;
            
            float low = _normalizedLevels[level + attenOffset];
            float high = _normalizedLevels[level + attenOffset + 4]; // The array is structured [Low 0..3], [High 0..3], [LowAtt 0..3], [HighAtt 0..3]
            
            if (color == 0) return high;
            if (color > 12) return low; // 13..15
            
            return IsInColorPhase(color, phase) ? high : low;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsInColorPhase(int color, int phase)
        {
            return ((color + phase) % 12) < 6;
        }

        private void GenNormalizedSignals()
        {
            for (int i = 0; i < 16; i++)
            {
                _normalizedLevels[i] = (NtscLevels[i] - _blackSetting) / (_whiteSetting - _blackSetting);
            }
        }

        private void GenPhaseTables(float hue)
        {
            for (int i = 0; i < 12; i++)
            {
                double sin, cos;
                // ::sincos( std::numbers::pi * ((I - 0.5) / 6.0 + 0.5) + _fHue, &dSin, &dCos );
                double angle = Math.PI * ((i - 0.5) / 6.0 + 0.5) + hue;
                sin = Math.Sin(angle);
                cos = Math.Cos(angle);
                
                double factor = 1.0; // LSN_FINAL_BRIGHT * sat * 2.0
                // Note: In C++, LSN_FINAL_BRIGHT includes Phosphor decay rate adjustment? 
                // #define LSN_FINAL_BRIGHT m_fBrightnessSetting
                // dCos *= (LSN_FINAL_BRIGHT) * m_fSaturationSetting * 2.0;
                
                factor = _brightnessSetting * _saturationSetting * 2.0;

                _phaseCosTable[i] = (float)(cos * factor);
                _phaseSinTable[i] = (float)(sin * factor);
            }
        }

        private void GenFilterKernel(uint width)
        {
             double sum = 0.0;
             for (int i = 0; i < width; i++)
             {
                 _filter[i] = BoxFilterFunc((float)(i / (width - 1.0f) * width - (width / 2.0f)), width / 2.0f);
                 sum += _filter[i];
             }
             // Normalize
             if (sum != 0)
             {
                 double norm = 1.0 / sum;
                 for(int i=0; i<width; i++) _filter[i] *= (float)norm;
             }
             
             // Y Filter (Luma)
             double sumY = 0.0;
             for (int i = 0; i < width; i++)
             {
                 _filterY[i] = BoxFilterFunc((float)(i / (width - 1.0f) * width - (width / 2.0f)), width / 2.0f);
                 sumY += _filterY[i];
             }
             if (sumY != 0)
             {
                 double norm = 1.0 / sumY;
                 for(int i=0; i<width; i++) _filterY[i] *= (float)norm;
             }
        }
        
        private float BoxFilterFunc(float t, float width)
        {
            // Basic box filter for testing? The C++ code defaults to BoxFilterFunc but allows others.
            // Let's use Gaussian or Blackman if possible, but Box is default in C++ header init.
            // CUtilities::BoxFilterFunc
             t = Math.Abs(t);
             return (t <= Math.Ceiling(width)) ? 1.0f : 0.0f;
        }

        private void SetGamma(float gamma)
        {
            for (int i = 0; i < LSN_SRGB_RES; i++)
            {
                // Simple Gamma for now
                double val = (double)i / (LSN_SRGB_RES - 1);
                val = Math.Pow(val, 1.0/gamma); // Linear to Gamma
                val *= 255.0;
                if (val < 0) val = 0;
                if (val > 255) val = 255;
                
                _gammaTable[i] = (byte)val;
                _gammaTableG[i] = (byte)val; // Assume same for Green
            }
        }
    }
}
