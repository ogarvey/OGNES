using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using OGNES.Utils;

namespace OGNES.Components
{
    public class NtscFilter
    {
        private const int LSN_PM_NTSC_RENDER_WIDTH = 256;
        private const int LSN_PM_NTSC_DOTS_X = 341;
        private const int LSN_SRGB_RES = 1024;

        private float _hueSetting = 0.0f;
        private GammaCorrection _gammaCorrection = GammaCorrection.Standard;
        private float _brightnessSetting = 1.0f;
        private float _saturationSetting = 1.0f;
        private float _blackSetting = 0.312f;
        private float _whiteSetting = 1.100f;
        
        // Proper CRT settings
        public double CrtLw { get; set; } = 1.0;
        public double CrtDb { get; set; } = 0.0181;

        private uint _filterKernelSize = 12;

        private float[] _filter = new float[128];
        private float[] _filterY = new float[128];
        private float[] _phaseCosTable = new float[24];
        private float[] _phaseSinTable = new float[24];

        private float[] _normalizedLevels = new float[16];

        private byte[] _gammaTable = new byte[LSN_SRGB_RES];
        private byte[] _gammaTableG = new byte[LSN_SRGB_RES];
        
        private float[]? _signalLut;
        private byte[]? _paletteLut;
        
        public void SetPaletteLut(byte[]? lut)
        {
            _paletteLut = lut;
            GenSignalLut();
        }

        // Buffers
        private float[][]? _signalBuffer;
        private float[]? _yBuffer;
        private float[]? _iBuffer;
        private float[]? _qBuffer;
        private float[]? _blendBuffer; 
        public byte[]? OutputBuffer => OutputBufferInternal;

        private int _width = 256;
        private int _height = 240;
        private int _widthScale = 8;
        private int _signalWidth;
        public int ScaledWidth => _width; 

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
            SetGamma(_gammaCorrection);
            GenSignalLut();
        }

        private void GenSignalLut()
        {
            _signalLut = new float[512 * 12 * 8];
            int idx = 0;
            for (int pixel = 0; pixel < 512; pixel++)
            {
                for (int phase = 0; phase < 12; phase++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        ushort cycle = (ushort)(phase + i);
                        _signalLut[idx++] = IndexToNtscSignal((ushort)pixel, cycle);
                    }
                }
            }
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
            _blendBuffer = new float[outputSize * 3]; 
            OutputBufferInternal = new byte[outputSize * 4];
        }

        private byte[]? OutputBufferInternal { get; set; }

        public void SetOutputBuffer(byte[] buffer)
        {
            OutputBufferInternal = buffer;
        }

        public void FilterFrame(ushort[] inputPixels, ulong renderStartCycle)
        {
            if (_signalBuffer == null || _yBuffer == null || _iBuffer == null || _qBuffer == null || OutputBufferInternal == null || _signalLut == null) return;

            Parallel.For(0, _height, y =>
            {
                RenderScanline(inputPixels, y, renderStartCycle);
            });
        }

        private void RenderScanline(ushort[] inputPixels, int y, ulong renderStartCycle)
        {
            ulong cycle = renderStartCycle + (ulong)(y * LSN_PM_NTSC_DOTS_X); 
            RenderScanlineRange(inputPixels, y, (ushort)((cycle * 8) % 12));
        }

        private unsafe void RenderScanlineRange(ushort[] inputPixels, int y, ushort initialPhase)
        {
            fixed (float* signalBufferPtr = _signalBuffer![y])
            fixed (float* yBufferPtr = _yBuffer, iBufferPtr = _iBuffer, qBufferPtr = _qBuffer)
            fixed (byte* outputBufferPtr = OutputBufferInternal)
            fixed (ushort* inputPixelsPtr = inputPixels)
            fixed (float* filterPtr = _filter, filterYPtr = _filterY)
            fixed (float* cosTablePtr = _phaseCosTable, sinTablePtr = _phaseSinTable)
            fixed (float* lutPtr = _signalLut)
            fixed (byte* gammaTablePtr = _gammaTable, gammaTableGPtr = _gammaTableG)
            {
                int signalOffset = (int)(_filterKernelSize / 2) + ((int)_filterKernelSize & 1);
                int inputOffset = y * LSN_PM_NTSC_RENDER_WIDTH;
                int currentSignalIdx = signalOffset;
                
                ushort* rowInputPtr = inputPixelsPtr + inputOffset;
                float* rowSignalPtr = signalBufferPtr;

                for (int x = 0; x < LSN_PM_NTSC_RENDER_WIDTH; x++)
                {
                    int phase = (initialPhase + (x * 8)) % 12; 
                    
                    int pixelIdx = rowInputPtr[x] & 0x1FF;
                    int lutIdx = (pixelIdx * 96) + (phase * 8); 

                    float* src = lutPtr + lutIdx;
                    float* dst = rowSignalPtr + currentSignalIdx;
                    
                    if (Avx.IsSupported)
                    {
                        var v = Avx.LoadVector256(src);
                        Avx.Store(dst, v);
                    }
                    else
                    {
                        dst[0] = src[0];
                        dst[1] = src[1];
                        dst[2] = src[2];
                        dst[3] = src[3];
                        dst[4] = src[4];
                        dst[5] = src[5];
                        dst[6] = src[6];
                        dst[7] = src[7];
                    }

                    currentSignalIdx += 8;
                }

                int dstOffset = y * _width;
                float* rowYPtr = yBufferPtr + dstOffset;
                float* rowIPtr = iBufferPtr + dstOffset;
                float* rowQPtr = qBufferPtr + dstOffset;
                
                int width = _width;
                int widthScale = _widthScale;
                int kernelSize = (int)_filterKernelSize;
                int kernelHalf = kernelSize / 2;
                float brightness = _brightnessSetting;

                float* temp = stackalloc float[12]; 

                for (int i = 0; i < width; i++)
                {
                    int center = (i * widthScale) + signalOffset + (widthScale / 2);
                    int start = center - kernelHalf;
                    
                    float yVal = 0, iVal = 0, qVal = 0;
                    
                    int p = (initialPhase + (start - signalOffset));
                    while (p < 0) p += 12;
                    if (p >= 12) p %= 12;
                    
                    float* sigPtr = rowSignalPtr + start;
                    float* fPtr = filterPtr;
                    float* fYPtr = filterYPtr;
                    float* cPtr = cosTablePtr + p;
                    float* sPtr = sinTablePtr + p;

                    if (Avx.IsSupported)
                    {
                         var vSig = Avx.LoadVector256(sigPtr);
                         var vFilter = Avx.LoadVector256(fPtr);
                         var vFilterY = Avx.LoadVector256(fYPtr);
                         var vCos = Avx.LoadVector256(cPtr);
                         var vSin = Avx.LoadVector256(sPtr);

                         var vLevel = Avx.Multiply(vSig, vFilter);
                         var vYVal = Avx.Multiply(vSig, vFilterY);
                         var vIVal = Avx.Multiply(vCos, vLevel);
                         var vQVal = Avx.Multiply(vSin, vLevel);
                         
                         var vSig2 = Sse.LoadVector128(sigPtr + 8);
                         var vFilter2 = Sse.LoadVector128(fPtr + 8);
                         var vFilterY2 = Sse.LoadVector128(fYPtr + 8);
                         var vCos2 = Sse.LoadVector128(cPtr + 8);
                         var vSin2 = Sse.LoadVector128(sPtr + 8);
                         
                         var vLevel2 = Sse.Multiply(vSig2, vFilter2);
                         var vYVal2 = Sse.Multiply(vSig2, vFilterY2);
                         var vIVal2 = Sse.Multiply(vCos2, vLevel2);
                         var vQVal2 = Sse.Multiply(vSin2, vLevel2);
                         
                         Avx.Store(temp, vYVal);
                         Sse.Store(temp + 8, vYVal2);
                         for(int k=0; k<12; k++) yVal += temp[k];
                         
                         Avx.Store(temp, vIVal);
                         Sse.Store(temp + 8, vIVal2);
                         for(int k=0; k<12; k++) iVal += temp[k];
                         
                         Avx.Store(temp, vQVal);
                         Sse.Store(temp + 8, vQVal2);
                         for(int k=0; k<12; k++) qVal += temp[k];
                    }
                    else
                    {
                        for (int k = 0; k < kernelSize; k++)
                        {
                            float sig = sigPtr[k]; 
                            float level = sig * fPtr[k];
                            yVal += sig * fYPtr[k];
                            
                            iVal += cPtr[k] * level;
                            qVal += sPtr[k] * level;
                        }
                    }

                    rowYPtr[i] = yVal * brightness;
                    rowIPtr[i] = iVal;
                    rowQPtr[i] = qVal;
                }
            
                int rgbOffset = dstOffset * 4;
                float i_factor_r = 1.139883025203f;
                float q_factor_g = -0.394642233974f;
                float i_factor_g = -0.580621847591f;
                float q_factor_b = 2.032061872219f;
                float srgb_scale = (float)(LSN_SRGB_RES - 1);
                
                var vIFacR = Vector256.Create(i_factor_r);
                var vQFacG = Vector256.Create(q_factor_g);
                var vIFacG = Vector256.Create(i_factor_g);
                var vQFacB = Vector256.Create(q_factor_b);
                var vScale = Vector256.Create(srgb_scale);
                var vMin = Vector256.Create(0.0f);
                var vMax = Vector256.Create((float)(LSN_SRGB_RES - 1));

                int simdWidth = width & ~7;
                
                for (int i = 0; i < simdWidth; i += 8)
                {
                     if (Avx.IsSupported)
                     {
                         var vY = Avx.LoadVector256(rowYPtr + i);
                         var vI = Avx.LoadVector256(rowIPtr + i);
                         var vQ = Avx.LoadVector256(rowQPtr + i);
                         
                         var vR = Avx.Add(vY, Avx.Multiply(vIFacR, vI));
                         var vG = Avx.Add(vY, Avx.Add(Avx.Multiply(vQFacG, vQ), Avx.Multiply(vIFacG, vI)));
                         var vB = Avx.Add(vY, Avx.Multiply(vQFacB, vQ));
                         
                         vR = Avx.Multiply(vR, vScale);
                         vG = Avx.Multiply(vG, vScale);
                         vB = Avx.Multiply(vB, vScale);
                         
                         vR = Avx.Max(vMin, Avx.Min(vMax, vR));
                         vG = Avx.Max(vMin, Avx.Min(vMax, vG));
                         vB = Avx.Max(vMin, Avx.Min(vMax, vB));
                         
                         var vRInt = Avx.ConvertToVector256Int32(vR);
                         var vGInt = Avx.ConvertToVector256Int32(vG);
                         var vBInt = Avx.ConvertToVector256Int32(vB);
                         
                         int* rBase = (int*)&vRInt;
                         int* gBase = (int*)&vGInt;
                         int* bBase = (int*)&vBInt;
                         
                         for(int k=0; k<8; k++)
                         {
                             int rIdx = rBase[k];
                             int gIdx = gBase[k];
                             int bIdx = bBase[k];
                             
                             outputBufferPtr[rgbOffset + 0] = gammaTablePtr[rIdx]; 
                             outputBufferPtr[rgbOffset + 1] = gammaTableGPtr[gIdx]; 
                             outputBufferPtr[rgbOffset + 2] = gammaTablePtr[bIdx]; 
                             outputBufferPtr[rgbOffset + 3] = 255;
                             rgbOffset += 4;
                         }
                     }
                }

                for (int i = simdWidth; i < width; i++)
                {
                     float Y = rowYPtr[i];
                     float I = rowIPtr[i];
                     float Q = rowQPtr[i];

                     float R = Y + i_factor_r * I;
                     float G = Y + q_factor_g * Q + i_factor_g * I;
                     float B = Y + q_factor_b * Q;

                     int rIdx = (int)(R * srgb_scale);
                     if (rIdx < 0) rIdx = 0; else if (rIdx >= LSN_SRGB_RES) rIdx = LSN_SRGB_RES - 1;

                     int gIdx = (int)(G * srgb_scale);
                     if (gIdx < 0) gIdx = 0; else if (gIdx >= LSN_SRGB_RES) gIdx = LSN_SRGB_RES - 1;

                     int bIdx = (int)(B * srgb_scale);
                     if (bIdx < 0) bIdx = 0; else if (bIdx >= LSN_SRGB_RES) bIdx = LSN_SRGB_RES - 1;
                     
                     outputBufferPtr[rgbOffset + 0] = gammaTablePtr[rIdx]; 
                     outputBufferPtr[rgbOffset + 1] = gammaTableGPtr[gIdx]; 
                     outputBufferPtr[rgbOffset + 2] = gammaTablePtr[bIdx]; 
                     outputBufferPtr[rgbOffset + 3] = 255;
                     
                     rgbOffset += 4;
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float IndexToNtscSignal(ushort pixel, ushort phase)
        {
            if (_paletteLut != null)
            {
                pixel = (ushort)((pixel & 0xFFC0) | (_paletteLut[pixel & 0x3F] & 0x3F));
            }

            int color = (pixel & 0x0F);
            int level = (color >= 0xE) ? 1 : ((pixel >> 4) & 3);
            int emphasis = (pixel >> 6);

            
            bool attenuated = false;
            if (color < 0xE)
            {
                if ((emphasis & 1) != 0 && IsInColorPhase(0xC, phase)) attenuated = true;
                else if ((emphasis & 2) != 0 && IsInColorPhase(0x4, phase)) attenuated = true;
                else if ((emphasis & 4) != 0 && IsInColorPhase(0x8, phase)) attenuated = true;
            }
            
            int attenOffset = attenuated ? 8 : 0;
            
            float low = _normalizedLevels[level + attenOffset];
            float high = _normalizedLevels[level + attenOffset + 4];
            
            if (color == 0) return high;
            if (color > 12) return low;
            
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

                double angle = Math.PI * ((i - 0.5) / 6.0 + 0.5) + hue;
                sin = Math.Sin(angle);
                cos = Math.Cos(angle);
                
                double factor = 1.0;
                
                factor = _brightnessSetting * _saturationSetting * 2.0;

                _phaseCosTable[i] = (float)(cos * factor);
                _phaseSinTable[i] = (float)(sin * factor);

                // Mirror for SIMD
                _phaseCosTable[i + 12] = _phaseCosTable[i];
                _phaseSinTable[i + 12] = _phaseSinTable[i];
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
             
             if (sum != 0)
             {
                 double norm = 1.0 / sum;
                 for(int i=0; i<width; i++) _filter[i] *= (float)norm;
             }
             
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
             t = Math.Abs(t);
             return (t <= Math.Ceiling(width)) ? 1.0f : 0.0f;
        }

        public void SetGamma(GammaCorrection gamma)
        {
            _gammaCorrection = gamma;
            for (int i = 0; i < LSN_SRGB_RES; i++)
            {
                double voltage = (double)i / (LSN_SRGB_RES - 1);
                double linear = 0;

                switch (gamma)
                {
                    case GammaCorrection.None:
                        linear = voltage;
                        break;
                    case GammaCorrection.Standard:
                    case GammaCorrection.P22:
                    case GammaCorrection.Smpte240M:
                        linear = Math.Pow(voltage, 2.2);
                        break;
                    case GammaCorrection.MeasuredCrt:
                        linear = GammaUtils.CrtProper2ToLinear(voltage);
                        break;
                    case GammaCorrection.CrtProper:
                        linear = GammaUtils.CrtProperToLinear(voltage, CrtLw, CrtDb);
                        break;
                }

                double srgb = GammaUtils.LinearTosRGB_Precise(linear);
                
                double val = srgb * 255.0;
                if (val < 0) val = 0;
                if (val > 255) val = 255;
                
                _gammaTable[i] = (byte)val;
                _gammaTableG[i] = (byte)val;
            }
        }
    }
}
