using System;

namespace GBOG.ImGuiTexInspect.Core
{
    /// <summary>
    /// Describes a buffer containing texture data read from GPU
    /// </summary>
    public unsafe class BufferDesc
    {
        /// <summary>Pointer to float data (mutually exclusive with Data_UInt8)</summary>
        public float* DataFloat = null;
        
        /// <summary>Pointer to byte data (mutually exclusive with DataFloat)</summary>
        public byte* DataUInt8 = null;
        
        /// <summary>Size of buffer in bytes</summary>
        public int BufferByteSize = 0;
        
        /// <summary>Stride between texels (measured in data type units, not bytes)</summary>
        public int Stride = 0;
        
        /// <summary>Stride between lines (measured in data type units, not bytes)</summary>
        public int LineStride = 0;
        
        /// <summary>X coordinate of the first texel in the buffer (in texture space)</summary>
        public int StartX = 0;
        
        /// <summary>Y coordinate of the first texel in the buffer (in texture space)</summary>
        public int StartY = 0;
        
        /// <summary>Width of the region covered by this buffer (in texels)</summary>
        public int Width = 0;
        
        /// <summary>Height of the region covered by this buffer (in texels)</summary>
        public int Height = 0;
        
        /// <summary>Number of color channels in the data (e.g., 4 for RGBA)</summary>
        public byte ChannelCount = 0;
        
        /// <summary>Offset to red channel from start of texel data</summary>
        public byte Red = 0;
        
        /// <summary>Offset to green channel from start of texel data</summary>
        public byte Green = 0;
        
        /// <summary>Offset to blue channel from start of texel data</summary>
        public byte Blue = 0;
        
        /// <summary>Offset to alpha channel from start of texel data</summary>
        public byte Alpha = 0;

        /// <summary>
        /// Check if this descriptor is using float data
        /// </summary>
        public bool IsFloat => DataFloat != null;

        /// <summary>
        /// Check if this descriptor is using byte data
        /// </summary>
        public bool IsByte => DataUInt8 != null;
    }
}
