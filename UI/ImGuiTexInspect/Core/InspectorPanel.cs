using System;
using System.Numerics;
using Hexa.NET.ImGui;
using OGNES.UI.ImGuiTexInspect.Core;
using OGNES.UI.ImGuiTexInspect.Utilities;
using OGNES.UI.ImGuiTexInspect.Annotations;

namespace OGNES.UI.ImGuiTexInspect
{
    /// <summary>
    /// Main API for ImGuiTexInspect texture inspector panels
    /// </summary>
    public static class InspectorPanel
    {
        private static Context? _globalContext;
        private const int BorderWidth = 1;

        public readonly struct HoverInfo
        {
            public HoverInfo(Inspector inspector, Vector2 uv, Vector2 texel, Vector4 color, bool hasColor = true)
            {
                Inspector = inspector;
                UV = uv;
                Texel = texel;
                Color = color;
                HasColor = hasColor;
            }

            public Inspector Inspector { get; }
            public Vector2 UV { get; }
            public Vector2 Texel { get; }
            public Vector4 Color { get; }
            public bool HasColor { get; }
        }

        /// <summary>
        /// Initialize ImGuiTexInspect (optional but recommended)
        /// </summary>
        public static void Init()
        {
            // Reserved for future initialization needs
        }

        /// <summary>
        /// Shutdown ImGuiTexInspect and release resources
        /// </summary>
        public static void Shutdown()
        {
            _globalContext?.Dispose();
            _globalContext = null;
        }

        /// <summary>
        /// Create a new context
        /// </summary>
        public static Context CreateContext()
        {
            var context = new Context();
            SetCurrentContext(context);
            return context;
        }

        /// <summary>
        /// Destroy a context
        /// </summary>
        public static void DestroyContext(Context? context = null)
        {
            context ??= _globalContext;
            if (context == _globalContext)
            {
                _globalContext = null;
            }
            context?.Dispose();
        }

        /// <summary>
        /// Set the current global context
        /// </summary>
        public static void SetCurrentContext(Context context)
        {
            _globalContext = context;
        }

        /// <summary>
        /// Get the current global context (creates one if it doesn't exist)
        /// </summary>
        public static Context GetCurrentContext()
        {
            if (_globalContext == null)
            {
                _globalContext = new Context();
            }
            return _globalContext;
        }

        /// <summary>
        /// Set flags to apply to the next inspector panel created
        /// </summary>
        public static void SetNextPanelFlags(InspectorFlags setFlags, InspectorFlags clearFlags = InspectorFlags.None)
        {
            var ctx = GetCurrentContext();
            ctx.NextPanelOptions.ToSet = MathUtils.SetFlag(ctx.NextPanelOptions.ToSet, setFlags);
            ctx.NextPanelOptions.ToClear = MathUtils.SetFlag(ctx.NextPanelOptions.ToClear, clearFlags);
        }

        /// <summary>
        /// Set the alpha mode to apply to the next inspector panel.
        /// </summary>
        public static void SetNextPanelAlphaMode(InspectorAlphaMode alphaMode)
        {
            var ctx = GetCurrentContext();
            ctx.NextPanelOptions.AlphaMode = alphaMode;
        }

        /// <summary>
        /// Set the grid cell size (in texture texels) to apply to the next inspector panel.
        /// </summary>
        public static void SetNextPanelGridCellSize(Vector2 gridCellSizeTexels)
        {
            var ctx = GetCurrentContext();
            ctx.NextPanelOptions.GridCellSizeTexels = gridCellSizeTexels;
        }

        /// <summary>
        /// Set the minimum zoom scale required for the grid to show on the next inspector panel.
        /// </summary>
        public static void SetNextPanelMinimumGridScale(float minimumGridScale)
        {
            var ctx = GetCurrentContext();
            ctx.NextPanelOptions.MinimumGridScale = minimumGridScale;
        }

        /// <summary>
        /// Begin an inspector panel. Returns true if the panel is visible and should be rendered.
        /// Must be paired with EndInspectorPanel().
        /// </summary>
        public static unsafe bool BeginInspectorPanel(
            string title,
            nint texture,
            Vector2 textureSize,
            InspectorFlags flags = InspectorFlags.None,
            Vector2? size = null)
        {
            return BeginInspectorPanel(title, texture, textureSize, flags, size, null);
        }

        /// <summary>
        /// Begin an inspector panel with an optional callback to append additional tooltip content.
        /// </summary>
        public static unsafe bool BeginInspectorPanel(
            string title,
            nint texture,
            Vector2 textureSize,
            InspectorFlags flags,
            Vector2? size,
            Action<HoverInfo>? extraTooltip)
        {
            var ctx = GetCurrentContext();
            var id = ImGui.GetID(title);
            var io = ImGui.GetIO();

            var nextAlphaMode = ctx.NextPanelOptions.AlphaMode;
            var nextGridCellSize = ctx.NextPanelOptions.GridCellSizeTexels;
            var nextMinimumGridScale = ctx.NextPanelOptions.MinimumGridScale;

            // Get or create inspector
            bool justCreated = !ctx.Inspectors.ContainsKey(id);
            if (justCreated)
            {
                ctx.Inspectors[id] = new Inspector { ID = id };
            }
            
            var inspector = ctx.Inspectors[id];
            ctx.CurrentInspector = inspector;
            justCreated |= !inspector.Initialized;

            // Cache basic properties
            inspector.ID = id;
            inspector.Texture = texture;
            inspector.TextureSize = textureSize;
            inspector.Initialized = true;

            // Handle flags
            var newlySetFlags = ctx.NextPanelOptions.ToSet;
            if (justCreated)
            {
                newlySetFlags = MathUtils.SetFlag(newlySetFlags, flags);
                inspector.MaxAnnotatedTexels = (ulong)ctx.MaxAnnotations;
            }
            inspector.Flags = MathUtils.SetFlag(inspector.Flags, newlySetFlags);
            inspector.Flags = MathUtils.ClearFlag(inspector.Flags, ctx.NextPanelOptions.ToClear);
            newlySetFlags = MathUtils.ClearFlag(newlySetFlags, ctx.NextPanelOptions.ToClear);

            // Apply alpha mode (if requested) before we update shader options/draw.
            if (nextAlphaMode.HasValue)
            {
                CurrentInspector_SetAlphaMode(nextAlphaMode.Value);
            }

            if (nextGridCellSize.HasValue)
            {
                inspector.ActiveShaderOptions.GridCellSizeTexels = nextGridCellSize.Value;
            }

            if (nextMinimumGridScale.HasValue)
            {
                inspector.MinimumGridSize = nextMinimumGridScale.Value;
            }

            ctx.NextPanelOptions = new NextPanelSettings();

            // Calculate panel size
            var contentRegionAvail = ImGui.GetContentRegionAvail();
            Vector2 panelSize;

            if (justCreated)
            {
                panelSize = new Vector2(
                    size?.X ?? (size?.X == 0 ? MathF.Max(ctx.DefaultInitialPanelWidth, contentRegionAvail.X) : contentRegionAvail.X),
                    size?.Y ?? (size?.Y == 0 ? ctx.DefaultPanelHeight : contentRegionAvail.Y)
                );
            }
            else
            {
                panelSize = new Vector2(
                    size?.X ?? (size?.X == 0 ? contentRegionAvail.X : contentRegionAvail.X),
                    size?.Y ?? (size?.Y == 0 ? ctx.DefaultPanelHeight : contentRegionAvail.Y)
                );
            }

            inspector.PanelSize = panelSize;
            var availablePanelSize = panelSize - new Vector2(BorderWidth, BorderWidth) * 2;

            // Possibly update scale based on flags
            if (MathUtils.HasFlag(newlySetFlags, InspectorFlags.FillVertical))
            {
                float newScale = availablePanelSize.Y / textureSize.Y;
                SetScale(inspector, newScale);
                SetPanPos(inspector, new Vector2(0.5f, 0.5f));
            }
            else if (MathUtils.HasFlag(newlySetFlags, InspectorFlags.FillHorizontal))
            {
                float newScale = availablePanelSize.X / textureSize.X;
                SetScale(inspector, newScale);
                SetPanPos(inspector, new Vector2(0.5f, 0.5f));
            }

            RoundPanPos(inspector);

            var textureSizePixels = inspector.Scale * textureSize;
            var viewSizeUV = availablePanelSize / textureSizePixels;
            var uv0 = inspector.PanPos - viewSizeUV * 0.5f;
            var uv1 = inspector.PanPos + viewSizeUV * 0.5f;

            var drawImageOffset = new Vector2(BorderWidth, BorderWidth);
            var viewSize = availablePanelSize;

            // Handle ShowWrap flag
            if (!MathUtils.HasFlag(inspector.Flags, InspectorFlags.ShowWrap))
            {
                if (textureSizePixels.X < availablePanelSize.X)
                {
                    float diff = availablePanelSize.X - textureSizePixels.X;
                    drawImageOffset.X += diff * 0.5f;
                    viewSize.X = textureSizePixels.X;
                    uv0.X = 0;
                    uv1.X = 1;
                    viewSizeUV.X = 1;
                }
                if (textureSizePixels.Y < availablePanelSize.Y)
                {
                    float diff = availablePanelSize.Y - textureSizePixels.Y;
                    drawImageOffset.Y += diff * 0.5f;
                    viewSize.Y = textureSizePixels.Y;
                    uv0.Y = 0;
                    uv1.Y = 1;
                    viewSizeUV.Y = 1;
                }
            }

            // Handle flip flags
            if (MathUtils.HasFlag(flags, InspectorFlags.FlipX))
            {
                MathUtils.Swap(ref uv0.X, ref uv1.X);
                viewSizeUV.X *= -1;
            }

            if (MathUtils.HasFlag(flags, InspectorFlags.FlipY))
            {
                MathUtils.Swap(ref uv0.Y, ref uv1.Y);
                viewSizeUV.Y *= -1;
            }

            inspector.ViewSize = viewSize;
            inspector.ViewSizeUV = viewSizeUV;

            // Begin child window
            var childFlags = ImGuiChildFlags.None;
            var windowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove;
            
            if (!ImGui.BeginChild(title, panelSize, childFlags, windowFlags))
            {
                ImGui.EndChild();
                
                // Reset texture data flag (unless NoAutoReadTexture is set)
                if (!MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoAutoReadTexture))
                {
                    inspector.HaveCurrentTexelData = false;
                }
                
                return false;
            }

            // Store positions
            inspector.PanelTopLeftPixel = ImGui.GetCursorScreenPos();
            ImGui.SetCursorPos(ImGui.GetCursorPos() + drawImageOffset);
            inspector.ViewTopLeftPixel = ImGui.GetCursorScreenPos();

            // Update shader options
            UpdateShaderOptions(inspector);
            inspector.CachedShaderOptions = inspector.ActiveShaderOptions.Clone();

            // Add callback to use custom shader for texture rendering
            Backend.OpenGL.RenderState.AddDrawCallback(inspector);

            // Draw the image (will be rendered with custom shader via callback)
            ImGui.Image(new ImTextureRef(null, (uint)texture), viewSize, uv0, uv1);
            
            // Reset to ImGui's default shader after drawing the image
            Backend.OpenGL.RenderState.AddResetCallback();

            // Setup transformation matrices
            inspector.TexelsToPixels = MathUtils.GetTexelsToPixels(
                inspector.ViewTopLeftPixel, viewSize, uv0, viewSizeUV, textureSize);
            inspector.PixelsToTexels = inspector.TexelsToPixels.Inverse();

            // Get mouse position for interaction
            var mousePos = ImGui.GetMousePos();
            var mousePosTexel = inspector.PixelsToTexels * mousePos;

            // Texel coordinate behavior should match what the image actually shows:
            // - If ShowWrap is enabled, wrap texels.
            // - Otherwise, clamp to texture bounds (prevents edge/padding reporting as wrapped texels).
            if (MathUtils.HasFlag(inspector.Flags, InspectorFlags.ShowWrap))
            {
                mousePosTexel.X = MathUtils.Modulus(mousePosTexel.X, textureSize.X);
                mousePosTexel.Y = MathUtils.Modulus(mousePosTexel.Y, textureSize.Y);
            }
            else
            {
                // Clamp slightly below max so (int)cast stays in-range.
                float maxX = MathF.Max(0f, textureSize.X - 0.001f);
                float maxY = MathF.Max(0f, textureSize.Y - 0.001f);
                mousePosTexel.X = MathF.Min(maxX, MathF.Max(0f, mousePosTexel.X));
                mousePosTexel.Y = MathF.Min(maxY, MathF.Max(0f, mousePosTexel.Y));
            }

            var mouseUV = mousePosTexel / textureSize;
            
            bool hovered = ImGui.IsWindowHovered();

            // Show tooltip when hovering
            if (ImGui.IsItemHovered() && !MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoTooltip))
            {
                // Try to get texture data for tooltip
                Vector2 texelTL = MathUtils.Floor(inspector.PixelsToTexels * inspector.ViewTopLeftPixel);
                Vector2 texelBR = MathUtils.Floor(inspector.PixelsToTexels * (inspector.ViewTopLeftPixel + inspector.ViewSize));

                if (texelTL.X > texelBR.X) MathUtils.Swap(ref texelTL.X, ref texelBR.X);
                if (texelTL.Y > texelBR.Y) MathUtils.Swap(ref texelTL.Y, ref texelBR.Y);

                texelBR += Vector2.One;
                texelTL = MathUtils.Clamp(texelTL, Vector2.Zero, inspector.TextureSize);
                texelBR = MathUtils.Clamp(texelBR, Vector2.Zero, inspector.TextureSize);

                Vector2 texelViewSize = texelBR - texelTL;

                // Get texture data if we don't have it yet
                if (!inspector.HaveCurrentTexelData && texelViewSize.X > 0 && texelViewSize.Y > 0)
                {
                    bool success = Backend.OpenGL.TextureReader.GetTextureData(
                        inspector,
                        inspector.Texture,
                        (int)texelTL.X, (int)texelTL.Y,
                        (int)texelViewSize.X, (int)texelViewSize.Y);

                    if (success)
                    {
                        inspector.HaveCurrentTexelData = true;
                    }
                }

                // Always show tooltip even if texture readback fails.
                // Readback is required only for showing the RGBA swatch/values.
                string tooltipText = $"UV: ({mouseUV.X:F5}, {mouseUV.Y:F5})\n" +
                                   $"Texel: ({(int)mousePosTexel.X}, {(int)mousePosTexel.Y})";

                Vector4 color = default;
                bool hasColor = false;
                if (inspector.HaveCurrentTexelData)
                {
                    color = BufferUtils.GetTexel(inspector.Buffer, (int)mousePosTexel.X, (int)mousePosTexel.Y);
                    hasColor = true;
                }

                ImGui.BeginTooltip();

                if (hasColor)
                {
                    ImGui.ColorButton("##preview", color, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoPicker, new Vector2(40, 40));
                    ImGui.SameLine();
                    ImGui.BeginGroup();
                    ImGui.Text(tooltipText);
                    ImGui.Separator();
                    ImGui.Text($"#{(byte)(color.X * 255):X2}{(byte)(color.Y * 255):X2}{(byte)(color.Z * 255):X2}{(byte)(color.W * 255):X2}");
                    ImGui.Text($"R:{(byte)(color.X * 255)}, G:{(byte)(color.Y * 255)}, B:{(byte)(color.Z * 255)}, A:{(byte)(color.W * 255)}");
                    ImGui.Text($"({color.X:F3}, {color.Y:F3}, {color.Z:F3}, {color.W:F3})");
                    ImGui.EndGroup();
                }
                else
                {
                    ImGui.Text(tooltipText);
                    ImGui.Separator();
                    ImGui.TextDisabled("Texture readback unavailable");
                }

                if (extraTooltip != null)
                {
                    ImGui.Separator();
                    extraTooltip(new HoverInfo(inspector, mouseUV, mousePosTexel, color, hasColor));
                }

                ImGui.EndTooltip();
            }

            // Dragging (panning)
            if (!inspector.IsDragging && hovered && ImGui.IsMouseClicked(ctx.Input.PanButton))
            {
                inspector.IsDragging = true;
            }
            else if (inspector.IsDragging && ImGui.IsMouseDown(ctx.Input.PanButton))
            {
                var mouseDelta = io.MouseDelta;
                var panDelta = mouseDelta / textureSizePixels;
                inspector.PanPos -= panDelta;
                RoundPanPos(inspector);
            }

            if (inspector.IsDragging && (!ImGui.IsMouseDown(ctx.Input.PanButton)))
            {
                inspector.IsDragging = false;
            }

            // Zooming
            if (hovered && io.MouseWheel != 0)
            {
                float zoomFactor = io.MouseWheel > 0 ? ctx.ZoomRate : 1.0f / ctx.ZoomRate;
                
                // Zoom toward mouse position
                var mouseUVBeforeZoom = inspector.PixelsToTexels * mousePos / textureSize;
                
                var newScale = inspector.Scale * zoomFactor;
                SetScale(inspector, newScale.Y);
                
                var mouseUVAfterZoom = inspector.PixelsToTexels * mousePos / textureSize;
                var uvDelta = mouseUVAfterZoom - mouseUVBeforeZoom;
                
                inspector.PanPos -= uvDelta;
                RoundPanPos(inspector);
            }

            return true;
        }

        /// <summary>
        /// Draw annotations on the texture using a custom annotation drawer.
        /// Call this between BeginInspectorPanel and EndInspectorPanel.
        /// </summary>
        /// <typeparam name="T">Type implementing IAnnotation interface</typeparam>
        /// <param name="drawer">Annotation drawer instance</param>
        /// <param name="maxAnnotatedTexels">Maximum number of texels to annotate (0 = use default)</param>
        public static void DrawAnnotations<T>(T drawer, ulong maxAnnotatedTexels = 0) where T : IAnnotation
        {
            AnnotationDesc ad;
            if (!GetAnnotationDesc(out ad, maxAnnotatedTexels))
            {
                // No annotation data available - this is normal when texture data hasn't been read yet
                return;
            }

            var texelBottomRight = new Vector2(
                ad.TexelTopLeft.X + ad.TexelViewSize.X,
                ad.TexelTopLeft.Y + ad.TexelViewSize.Y);

            // Iterate over all visible texels
            int annotationCount = 0;
            for (int ty = (int)ad.TexelTopLeft.Y; ty < (int)texelBottomRight.Y; ++ty)
            {
                for (int tx = (int)ad.TexelTopLeft.X; tx < (int)texelBottomRight.X; ++tx)
                {
                    Vector4 color = BufferUtils.GetTexel(ad.Buffer, tx, ty);
                    Vector2 texelCenter = new Vector2(tx + 0.5f, ty + 0.5f);
                    drawer.DrawAnnotation(ad.DrawList, texelCenter, ad.TexelsToPixels, color);
                    annotationCount++;
                }
            }
        }

        /// <summary>
        /// Get annotation description for the current inspector.
        /// Returns false if texture data is not available.
        /// </summary>
        private static bool GetAnnotationDesc(out AnnotationDesc desc, ulong maxAnnotatedTexels)
        {
            desc = new AnnotationDesc();
            
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            
            if (inspector == null)
            {
                return false;
            }

            // Limit the number of texels we'll annotate
            ulong maxTexels = maxAnnotatedTexels > 0 ? maxAnnotatedTexels : inspector.MaxAnnotatedTexels;

            // Calculate visible texel region
            Vector2 texelTL = MathUtils.Floor(inspector.PixelsToTexels * inspector.ViewTopLeftPixel);
            Vector2 texelBR = MathUtils.Floor(inspector.PixelsToTexels * (inspector.ViewTopLeftPixel + inspector.ViewSize));

            if (texelTL.X > texelBR.X)
            {
                MathUtils.Swap(ref texelTL.X, ref texelBR.X);
            }
            if (texelTL.Y > texelBR.Y)
            {
                MathUtils.Swap(ref texelTL.Y, ref texelBR.Y);
            }

            // Add (1,1) because we want to draw partially visible texels on the edges
            texelBR += Vector2.One;

            texelTL = MathUtils.Clamp(texelTL, Vector2.Zero, inspector.TextureSize);
            texelBR = MathUtils.Clamp(texelBR, Vector2.Zero, inspector.TextureSize);

            Vector2 texelViewSize = texelBR - texelTL;

            // Check if we need texture data
            if (!inspector.HaveCurrentTexelData)
            {
                // Request texture data from backend
                //Console.WriteLine("Requesting texture data from GPU...");
                bool success = Backend.OpenGL.TextureReader.GetTextureData(
                    inspector,
                    inspector.Texture,
                    (int)texelTL.X, (int)texelTL.Y,
                    (int)texelViewSize.X, (int)texelViewSize.Y);

                if (success)
                {
                    inspector.HaveCurrentTexelData = true;
                    //Console.WriteLine($"Successfully read texture data: {inspector.Buffer.Width}x{inspector.Buffer.Height}");
                }
                else
                {
                    // Failed to read texture data
                    Console.WriteLine("ERROR: Failed to read texture data");
                    return false;
                }
            }

            // Check if we have too many texels to annotate
            if (maxTexels > 0)
            {
                ulong texelCount = (ulong)(texelViewSize.X * texelViewSize.Y);
                if (texelCount > maxTexels)
                {
                    // Too many texels - skip annotation
                    return false;
                }
            }

            // Fill in annotation descriptor
            desc.DrawList = ImGui.GetWindowDrawList();
            desc.TexelViewSize = texelViewSize;
            desc.TexelTopLeft = texelTL;
            desc.Buffer = inspector.Buffer;
            desc.TexelsToPixels = inspector.TexelsToPixels;

            return true;
        }

        /// <summary>
        /// End an inspector panel. Must be called after BeginInspectorPanel().
        /// </summary>
        public static void EndInspectorPanel()
        {
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            if (inspector == null)
            {
                Console.WriteLine("ERROR: EndInspectorPanel called without BeginInspectorPanel");
                return;
            }

            uint innerBorderColor = 0xFFFFFFFF;
            uint outerBorderColor = 0xFF888888;

            var drawList = ImGui.GetWindowDrawList();

            // Draw outer border around whole inspector panel
            drawList.AddRect(
                inspector.PanelTopLeftPixel,
                inspector.PanelTopLeftPixel + inspector.PanelSize,
                outerBorderColor);

            // Draw inner border around texture
            drawList.AddRect(
                inspector.ViewTopLeftPixel - Vector2.One,
                inspector.ViewTopLeftPixel + inspector.ViewSize + Vector2.One,
                innerBorderColor);

            ImGui.EndChild();

            // Reset texture data flag (unless NoAutoReadTexture is set)
            if (!MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoAutoReadTexture))
            {
                inspector.HaveCurrentTexelData = false;
            }
        }

        /// <summary>
        /// Release cached data for an inspector (frees memory)
        /// </summary>
        public static void ReleaseInspectorData(uint inspectorId)
        {
            var ctx = GetCurrentContext();
            if (ctx.Inspectors.TryGetValue(inspectorId, out var inspector))
            {
                inspector.Dispose();
                ctx.Inspectors.Remove(inspectorId);
            }
        }

        #region Current Inspector Manipulators

        /// <summary>
        /// Get the ID of the current inspector
        /// </summary>
        public static uint CurrentInspector_GetID()
        {
            return GetCurrentContext().CurrentInspector?.ID ?? 0;
        }

        /// <summary>
        /// Set the color transformation matrix for the current inspector
        /// </summary>
        public static void CurrentInspector_SetColorMatrix(float[] matrix, float[] colorOffset)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector == null) return;

            Array.Copy(matrix, inspector.ActiveShaderOptions.ColorTransform, Math.Min(16, matrix.Length));
            Array.Copy(colorOffset, inspector.ActiveShaderOptions.ColorOffset, Math.Min(4, colorOffset.Length));
        }

        /// <summary>
        /// Reset color transformation matrix to identity
        /// </summary>
        public static void CurrentInspector_ResetColorMatrix()
        {
            var inspector = GetCurrentContext().CurrentInspector;
            inspector?.ActiveShaderOptions.ResetColorTransform();
        }

        /// <summary>
        /// Set the alpha blending mode for the current inspector
        /// </summary>
        public static void CurrentInspector_SetAlphaMode(InspectorAlphaMode mode)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector == null) return;

            inspector.AlphaMode = mode;
            var shaderOptions = inspector.ActiveShaderOptions;
            
            // Reset checkered background flag
            shaderOptions.CheckeredBackground = false;

            switch (mode)
            {
                case InspectorAlphaMode.Black:
                    shaderOptions.BackgroundColor = new Vector4(0, 0, 0, 1);
                    shaderOptions.DisableFinalAlpha = 1;
                    shaderOptions.PremultiplyAlpha = 1;
                    break;
                case InspectorAlphaMode.White:
                    shaderOptions.BackgroundColor = new Vector4(1, 1, 1, 1);
                    shaderOptions.DisableFinalAlpha = 1;
                    shaderOptions.PremultiplyAlpha = 1;
                    break;
                case InspectorAlphaMode.Checkered:
                    shaderOptions.CheckeredBackground = true;
                    shaderOptions.DisableFinalAlpha = 1;
                    shaderOptions.PremultiplyAlpha = 1;
                    break;
                case InspectorAlphaMode.ImGui:
                    shaderOptions.BackgroundColor = Vector4.Zero;
                    shaderOptions.DisableFinalAlpha = 0;
                    shaderOptions.PremultiplyAlpha = 0;
                    break;
                case InspectorAlphaMode.CustomColor:
                    shaderOptions.BackgroundColor = inspector.CustomBackgroundColor;
                    shaderOptions.DisableFinalAlpha = 1;
                    shaderOptions.PremultiplyAlpha = 1;
                    break;
            }
        }

        /// <summary>
        /// Set flags on the current inspector
        /// </summary>
        public static void CurrentInspector_SetFlags(InspectorFlags toSet, InspectorFlags toClear = InspectorFlags.None)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector == null) return;

            inspector.Flags = MathUtils.SetFlag(inspector.Flags, toSet);
            inspector.Flags = MathUtils.ClearFlag(inspector.Flags, toClear);
        }

        /// <summary>
        /// Set grid color for the current inspector
        /// </summary>
        public static void CurrentInspector_SetGridColor(uint color)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector == null) return;

            float alpha = inspector.ActiveShaderOptions.GridColor.W;
            var gridColor = ImGuiUtils.ColorToVec4(color);
            gridColor.W = alpha;
            inspector.ActiveShaderOptions.GridColor = gridColor;
        }

        /// <summary>
        /// Set maximum number of annotations for the current inspector
        /// </summary>
        public static void CurrentInspector_SetMaxAnnotations(int maxAnnotations)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector != null)
            {
                inspector.MaxAnnotatedTexels = (ulong)maxAnnotations;
            }
        }

        /// <summary>
        /// Invalidate cached texture data (force re-read next frame)
        /// </summary>
        public static void CurrentInspector_InvalidateTextureCache()
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector != null)
            {
                inspector.HaveCurrentTexelData = false;
            }
        }

        /// <summary>
        /// Set custom background color for CustomColor alpha mode
        /// </summary>
        public static void CurrentInspector_SetCustomBackgroundColor(Vector4 color)
        {
            var inspector = GetCurrentContext().CurrentInspector;
            if (inspector == null) return;

            inspector.CustomBackgroundColor = color;
            if (inspector.AlphaMode == InspectorAlphaMode.CustomColor)
            {
                inspector.ActiveShaderOptions.BackgroundColor = color;
            }
        }

        /// <summary>
        /// Set custom background color (uint variant)
        /// </summary>
        public static void CurrentInspector_SetCustomBackgroundColor(uint color)
        {
            CurrentInspector_SetCustomBackgroundColor(ImGuiUtils.ColorToVec4(color));
        }

        /// <summary>
        /// Set the zoom rate for all inspectors
        /// </summary>
        public static void SetZoomRate(float rate)
        {
            GetCurrentContext().ZoomRate = rate;
        }

        /// <summary>
        /// Convenience function to draw a line using texel coordinates.
        /// Useful for custom annotations.
        /// </summary>
        /// <param name="drawList">ImGui draw list</param>
        /// <param name="fromTexel">Start point in texel coordinates</param>
        /// <param name="toTexel">End point in texel coordinates</param>
        /// <param name="texelsToPixels">Transform from texel to pixel coordinates</param>
        /// <param name="color">Line color (ABGR format)</param>
        /// <param name="thickness">Line thickness in pixels</param>
        public static void DrawAnnotationLine(
            ImDrawListPtr drawList, 
            Vector2 fromTexel, 
            Vector2 toTexel, 
            Transform2D texelsToPixels, 
            uint color, 
            float thickness = 1.0f)
        {
            Vector2 lineFrom = texelsToPixels * fromTexel;
            Vector2 lineTo = texelsToPixels * toTexel;
            drawList.AddLine(lineFrom, lineTo, color, thickness);
        }

        /// <summary>
        /// Draw a grid enable/disable checkbox and grid color picker
        /// </summary>
        public static void DrawGridEditor()
        {
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            if (inspector == null) return;

            ImGui.BeginGroup();
            bool gridEnabled = !MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoGrid);
            if (ImGui.Checkbox("Grid", ref gridEnabled))
            {
                if (gridEnabled)
                {
                    CurrentInspector_SetFlags(InspectorFlags.None, InspectorFlags.NoGrid);
                }
                else
                {
                    CurrentInspector_SetFlags(InspectorFlags.NoGrid, InspectorFlags.None);
                }
            }

            if (gridEnabled)
            {
                ImGui.SameLine();
                var gridColor = inspector.ActiveShaderOptions.GridColor;
                unsafe
                {
                    if (ImGui.ColorEdit3("Grid Color"u8, (float*)&gridColor, ImGuiColorEditFlags.NoInputs))
                    {
                        gridColor.W = inspector.ActiveShaderOptions.GridColor.W; // Preserve alpha
                        inspector.ActiveShaderOptions.GridColor = gridColor;
                    }
                }
            }

            ImGui.EndGroup();
        }

        /// <summary>
        /// Draw color channel selector (R, G, B, Grey)
        /// </summary>
        public static void DrawColorChannelSelector()
        {
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            if (inspector == null) return;

            var shaderOptions = inspector.ActiveShaderOptions;
            var storage = ImGui.GetStateStorage();
            uint greyScaleID = ImGui.GetID("greyScale");

            bool greyScale = storage.GetBool(greyScaleID);
            bool red = shaderOptions.ColorTransform[0] > 0;
            bool green = shaderOptions.ColorTransform[5] > 0;
            bool blue = shaderOptions.ColorTransform[10] > 0;
            bool changed = false;

            // In greyscale mode, disable RGB checkboxes
            if (greyScale)
            {
                ImGui.BeginDisabled();
            }

            ImGui.BeginGroup();
            if (ImGui.Checkbox("Red", ref red)) changed = true;
            if (ImGui.Checkbox("Green", ref green)) changed = true;
            if (ImGui.Checkbox("Blue", ref blue)) changed = true;
            ImGui.EndGroup();

            ImGui.SameLine();

            if (greyScale)
            {
                ImGui.EndDisabled();
            }

            if (changed)
            {
                // Update color transform matrix based on checkbox states
                shaderOptions.ResetColorTransform();
                shaderOptions.ColorTransform[0] = red ? 1.0f : 0.0f;
                shaderOptions.ColorTransform[5] = green ? 1.0f : 0.0f;
                shaderOptions.ColorTransform[10] = blue ? 1.0f : 0.0f;
            }

            ImGui.BeginGroup();
            if (ImGui.Checkbox("Grey", ref greyScale))
            {
                shaderOptions.ResetColorTransform();
                storage.SetBool(greyScaleID, greyScale);
                
                if (greyScale)
                {
                    // Set all RGB channels to average (greyscale)
                    for (int i = 0; i < 3; i++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            shaderOptions.ColorTransform[i * 4 + j] = 0.333f;
                        }
                    }
                }
            }
            ImGui.EndGroup();
        }

        /// <summary>
        /// Draw alpha mode selector combo box
        /// </summary>
        public static void DrawAlphaModeSelector()
        {
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            if (inspector == null) return;

            string[] alphaModes = { "ImGui Background", "Black", "White", "Checkered", "Custom Color" };

            ImGui.SetNextItemWidth(200);

            int currentMode = (int)inspector.AlphaMode;
            if (ImGui.Combo("Alpha Mode", ref currentMode, alphaModes, alphaModes.Length))
            {
                CurrentInspector_SetAlphaMode((InspectorAlphaMode)currentMode);
            }

            if (inspector.AlphaMode == InspectorAlphaMode.CustomColor)
            {
                var backgroundColor = inspector.CustomBackgroundColor;
                unsafe
                {
                    if (ImGui.ColorEdit3("Background Color"u8, (float*)&backgroundColor))
                    {
                        CurrentInspector_SetCustomBackgroundColor(backgroundColor);
                    }
                }
            }
        }

        /// <summary>
        /// Draw color matrix editor (4x4 matrix + offset vector)
        /// </summary>
        public static void DrawColorMatrixEditor()
        {
            var ctx = GetCurrentContext();
            var inspector = ctx.CurrentInspector;
            if (inspector == null) return;

            var shaderOptions = inspector.ActiveShaderOptions;
            float dragSpeed = 0.01f;

            string[] colorVectorNames = { "R", "G", "B", "A" };

            ImGui.BeginGroup();

            // Draw 4x4 matrix + offset
            for (int i = 0; i < 4; i++)
            {
                ImGui.PushID(i);
                ImGui.BeginGroup();
                
                // Draw row of matrix
                for (int j = 0; j < 4; j++)
                {
                    ImGui.PushID(j);
                    ImGui.SetNextItemWidth(50);
                    float value = shaderOptions.ColorTransform[j * 4 + i];
                    if (ImGui.DragFloat("##f", ref value, dragSpeed))
                    {
                        shaderOptions.ColorTransform[j * 4 + i] = value;
                    }
                    ImGui.PopID();
                    ImGui.SameLine();
                }
                
                // Draw offset for this row
                ImGui.SetNextItemWidth(50);
                float offset = shaderOptions.ColorOffset[i];
                if (ImGui.DragFloat("##offset", ref offset, dragSpeed))
                {
                    shaderOptions.ColorOffset[i] = offset;
                }
                
                ImGui.EndGroup();
                ImGui.PopID();
            }
            
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.TextUnformatted("*");
            ImGui.SameLine();

            // Right side: input vector labels
            ImGui.BeginGroup();
            for (int i = 0; i < 4; i++)
            {
                ImGui.Text(colorVectorNames[i]);
            }
            ImGui.EndGroup();
        }

        #endregion

        #region Internal Helpers

        private static void SetPanPos(Inspector inspector, Vector2 pos)
        {
            inspector.PanPos = pos;
            RoundPanPos(inspector);
        }

        private static void SetScale(Inspector inspector, float scaleY)
        {
            var scale = new Vector2(scaleY * inspector.PixelAspectRatio, scaleY);
            SetScale(inspector, scale);
        }

        private static void SetScale(Inspector inspector, Vector2 scale)
        {
            scale = MathUtils.Clamp(scale, inspector.ScaleMin, inspector.ScaleMax);

            inspector.ViewSizeUV *= inspector.Scale / scale;
            inspector.Scale = scale;

            // Force nearest sampling when zoomed in
            inspector.ActiveShaderOptions.ForceNearestSampling =
                (inspector.Scale.X > 1.0f || inspector.Scale.Y > 1.0f) &&
                !MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoForceFilterNearest);
            
            inspector.ActiveShaderOptions.GridWidth = new Vector2(1.0f / inspector.Scale.X, 1.0f / inspector.Scale.Y);
        }

        private static void RoundPanPos(Inspector inspector)
        {
            if (MathUtils.HasFlag(inspector.Flags, InspectorFlags.ShowWrap))
            {
                // Wrap pan position
                var panPos = inspector.PanPos;
                panPos.X = MathUtils.Modulus(panPos.X, 1.0f);
                panPos.Y = MathUtils.Modulus(panPos.Y, 1.0f);
                inspector.PanPos = panPos;
            }
            else
            {
                // Clamp pan position
                inspector.PanPos = MathUtils.Clamp(inspector.PanPos, Vector2.Zero, Vector2.One);
            }

            // Align to pixel boundaries when zoomed in
            var topLeftSubTexel = inspector.PanPos * inspector.Scale * inspector.TextureSize - inspector.ViewSize * 0.5f;

            if (inspector.Scale.X >= 1)
            {
                topLeftSubTexel.X = MathF.Round(topLeftSubTexel.X);
            }
            if (inspector.Scale.Y >= 1)
            {
                topLeftSubTexel.Y = MathF.Round(topLeftSubTexel.Y);
            }

            inspector.PanPos = (topLeftSubTexel + inspector.ViewSize * 0.5f) / (inspector.Scale * inspector.TextureSize);
        }

        private static void UpdateShaderOptions(Inspector inspector)
        {
            float minScale = MathF.Min(inspector.Scale.X, inspector.Scale.Y);

            if (!MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoGrid) && minScale >= inspector.MinimumGridSize)
            {
                // Show grid
                inspector.ActiveShaderOptions.GridColor = new Vector4(0, 0, 0, 0.5f);

                // GridWidth is expressed in texel-space (0..1 within a texel).
                // To keep grid lines ~1 pixel thick on screen, set width ~= 1/scale.
                float gwX = inspector.Scale.X > 0.0001f ? (1.0f / inspector.Scale.X) : 0.0f;
                float gwY = inspector.Scale.Y > 0.0001f ? (1.0f / inspector.Scale.Y) : 0.0f;

                // Clamp to avoid overly thick lines at low zoom.
                gwX = MathF.Min(gwX, 1.0f);
                gwY = MathF.Min(gwY, 1.0f);

                inspector.ActiveShaderOptions.GridWidth = new Vector2(gwX, gwY);
            }
            else
            {
                // Hide grid
                inspector.ActiveShaderOptions.GridColor = Vector4.Zero;
                inspector.ActiveShaderOptions.GridWidth = Vector2.Zero;
            }

            inspector.ActiveShaderOptions.ForceNearestSampling =
                (inspector.Scale.X > 1.0f || inspector.Scale.Y > 1.0f) &&
                !MathUtils.HasFlag(inspector.Flags, InspectorFlags.NoForceFilterNearest);
        }

        #endregion
    }
}
