using System;
using Hexa.NET.OpenGL;
using OGNES.UI.ImGuiTexInspect.Core;

namespace OGNES.UI.ImGuiTexInspect.Backend.OpenGL
{
    /// <summary>
    /// Handles reading texture data from GPU to CPU for annotations and inspection
    /// </summary>
    public static unsafe class TextureReader
    {
        private static GL? _gl;
        private static uint _readbackFramebuffer;
        private static bool _initialized;

        /// <summary>
        /// Initialize the texture reader with an OpenGL context
        /// </summary>
        public static bool Initialize(GL gl)
        {
            _gl = gl;
            
            // Create framebuffer for texture readback
            uint fbo;
            _gl.GenFramebuffers(1, &fbo);
            _readbackFramebuffer = fbo;
            
            _initialized = true;
            return true;
        }

        /// <summary>
        /// Shutdown and cleanup resources
        /// </summary>
        public static void Shutdown()
        {
            if (_gl != null && _readbackFramebuffer != 0)
            {
                uint fbo = _readbackFramebuffer;
                _gl.DeleteFramebuffers(1, &fbo);
                _readbackFramebuffer = 0;
            }
            _initialized = false;
        }

        /// <summary>
        /// Read texture data from GPU to CPU buffer.
        /// Currently reads the entire texture (simplified implementation).
        /// </summary>
        /// <param name="inspector">Inspector instance to store data in</param>
        /// <param name="texture">OpenGL texture ID</param>
        /// <param name="x">X coordinate of region to read (not yet implemented)</param>
        /// <param name="y">Y coordinate of region to read (not yet implemented)</param>
        /// <param name="width">Width of region to read (not yet implemented)</param>
        /// <param name="height">Height of region to read (not yet implemented)</param>
        /// <returns>True if readback succeeded</returns>
        public static bool GetTextureData(
            Inspector inspector,
            nint texture,
            int x, int y,
            int width, int height)
        {
            if (!_initialized || _gl == null || _readbackFramebuffer == 0)
            {
                Console.WriteLine("ERROR: TextureReader not initialized");
                return false;
            }

            const int numChannels = 4; // RGBA
            int texWidth = (int)inspector.TextureSize.X;
            int texHeight = (int)inspector.TextureSize.Y;
            uint glTexture = (uint)texture;

            // Clear any existing GL errors
            _gl.GetError();

            // Calculate buffer size
            int bufferSize = texWidth * texHeight * numChannels;

            // Allocate buffer if needed
            if (inspector.DataBuffer == null || inspector.DataBufferSize < bufferSize)
            {
                if (inspector.DataBuffer != null)
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal((nint)inspector.DataBuffer);
                }

                // Allocate slightly more to avoid frequent reallocations
                int allocSize = bufferSize * 5 / 4;
                inspector.DataBuffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(allocSize);
                inspector.DataBufferSize = allocSize;
            }

            // Setup buffer descriptor
            var buffer = inspector.Buffer;
            buffer.DataUInt8 = inspector.DataBuffer;
            buffer.DataFloat = null;
            buffer.BufferByteSize = bufferSize;
            buffer.Red = 0;   // RGBA order: R first
            buffer.Green = 1; // G second
            buffer.Blue = 2;  // B third
            buffer.Alpha = 3; // A fourth
            buffer.ChannelCount = 4;
            buffer.LineStride = texWidth * numChannels;
            buffer.Stride = numChannels; // 4 bytes per texel (RGBA)
            buffer.StartX = 0;
            buffer.StartY = 0;
            buffer.Width = texWidth;
            buffer.Height = texHeight;

            // Save current framebuffer
            int currentFramebuffer;
            _gl.GetIntegerv(GLGetPName.DrawFramebufferBinding, &currentFramebuffer);

            // Bind our readback framebuffer and attach texture
            _gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, _readbackFramebuffer);
            _gl.FramebufferTexture2D(
                GLFramebufferTarget.Framebuffer,
                GLFramebufferAttachment.ColorAttachment0,
                GLTextureTarget.Texture2D,
                glTexture,
                0);

            // Read pixel data
            _gl.ReadPixels(
                0, 0,
                texWidth, texHeight,
                GLPixelFormat.Rgba,
                GLPixelType.UnsignedByte,
                inspector.DataBuffer);

            // Restore previous framebuffer
            _gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, (uint)currentFramebuffer);

            // Check for errors
            GLEnum error = _gl.GetError();
            if (error != (GLEnum)GLErrorCode.NoError)
            {
                Console.WriteLine($"ERROR: TextureReader.GetTextureData failed with GL error: {error}");
                return false;
            }

            return true;
        }
    }
}
