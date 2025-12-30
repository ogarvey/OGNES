using System;
using System.Numerics;

namespace OGNES.UI.ImGuiTexInspect.Core
{
    /// <summary>
    /// State for a single texture inspector instance
    /// </summary>
    public unsafe class Inspector : IDisposable
    {
        // Identity
        public uint ID { get; set; }
        public bool Initialized { get; set; }

        // Texture
        public nint Texture { get; set; }
        public Vector2 TextureSize { get; set; }
        public float PixelAspectRatio { get; set; } = 1.0f;

        // View State
        public bool IsDragging { get; set; }
        public Vector2 PanPos { get; set; } = new Vector2(0.5f, 0.5f);
        public Vector2 Scale { get; set; } = Vector2.One;

        public Vector2 PanelTopLeftPixel { get; set; }
        public Vector2 PanelSize { get; set; }

        public Vector2 ViewTopLeftPixel { get; set; }
        public Vector2 ViewSize { get; set; }
        public Vector2 ViewSizeUV { get; set; }

        // Conversion transforms
        public Transform2D TexelsToPixels { get; set; }
        public Transform2D PixelsToTexels { get; set; }

        // Cached pixel data
        public bool HaveCurrentTexelData { get; set; }
        public BufferDesc Buffer { get; set; } = new BufferDesc();

        // Data buffer management (managed separately from BufferDesc)
        public byte* DataBuffer { get; set; }
        public int DataBufferSize { get; set; }

        // Configuration
        public InspectorFlags Flags { get; set; }

        // Background mode
        public InspectorAlphaMode AlphaMode { get; set; } = InspectorAlphaMode.ImGui;
        public Vector4 CustomBackgroundColor { get; set; } = new Vector4(0, 0, 0, 1);

        // Scaling limits
        public Vector2 ScaleMin { get; set; } = new Vector2(0.02f, 0.02f);
        public Vector2 ScaleMax { get; set; } = new Vector2(500, 500);

        // Grid
        public float MinimumGridSize { get; set; } = 4.0f;

        // Annotations
        public ulong MaxAnnotatedTexels { get; set; }

        // Shader configuration
        public ShaderOptions ActiveShaderOptions { get; set; } = new ShaderOptions();
        public ShaderOptions CachedShaderOptions { get; set; } = new ShaderOptions();

        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                // Free data buffer if allocated
                if (DataBuffer != null)
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal((nint)DataBuffer);
                    DataBuffer = null;
                    DataBufferSize = 0;
                }

                _disposed = true;
            }
        }

        ~Inspector()
        {
            Dispose();
        }
    }
}
