using System;
using System.Collections.Generic;
using System.Numerics;

namespace OGNES.UI.ImGuiTexInspect.Core
{
    /// <summary>
    /// Input mapping configuration for inspector interaction
    /// </summary>
    public class InputMap
    {
        /// <summary>Mouse button that enables panning when held</summary>
        public Hexa.NET.ImGui.ImGuiMouseButton PanButton { get; set; } = Hexa.NET.ImGui.ImGuiMouseButton.Left;
    }

    /// <summary>
    /// Settings to apply to the next panel created
    /// </summary>
    public class NextPanelSettings
    {
        public InspectorFlags ToSet { get; set; }
        public InspectorFlags ToClear { get; set; }

        public InspectorAlphaMode? AlphaMode { get; set; }

        public Vector2? GridCellSizeTexels { get; set; }
        public float? MinimumGridScale { get; set; }
    }

    /// <summary>
    /// Global context for ImGuiTexInspect - manages all inspector instances and settings
    /// </summary>
    public class Context : IDisposable
    {
        /// <summary>Input configuration</summary>
        public InputMap Input { get; set; } = new InputMap();

        /// <summary>Registry of all inspector instances</summary>
        public Dictionary<uint, Inspector> Inspectors { get; set; } = new Dictionary<uint, Inspector>();

        /// <summary>Currently active inspector being rendered</summary>
        public Inspector? CurrentInspector { get; set; }

        /// <summary>Settings to apply to next panel created</summary>
        public NextPanelSettings NextPanelOptions { get; set; } = new NextPanelSettings();

        /// <summary>How fast mouse wheel affects zoom (values > 1.0)</summary>
        public float ZoomRate { get; set; } = 1.3f;

        /// <summary>Default height of panel in pixels</summary>
        public float DefaultPanelHeight { get; set; } = 600;

        /// <summary>Default initial panel width in pixels (only applies when window first appears)</summary>
        public float DefaultInitialPanelWidth { get; set; } = 600;

        /// <summary>Maximum number of texels to annotate for performance</summary>
        public int MaxAnnotations { get; set; } = 1000;

        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose all inspectors
                foreach (var inspector in Inspectors.Values)
                {
                    inspector?.Dispose();
                }
                Inspectors.Clear();

                _disposed = true;
            }
        }

        ~Context()
        {
            Dispose();
        }
    }
}
