using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.OpenGL;
using OGNES.UI.ImGuiTexInspect.Core;

namespace OGNES.UI.ImGuiTexInspect.Backend.OpenGL
{
    /// <summary>
    /// Manages OpenGL render state and shader integration for texture inspector
    /// </summary>
    public static unsafe class RenderState
    {
        private static GL? _gl;
        private static bool _initialized;

        // Store inspector reference for callback
        private static Inspector? _currentInspector;
        
        // Store ImGui's shader program to restore later
        private static uint _imguiShaderProgram = 0;

        /// <summary>
        /// Managed callback function for custom shader setup
        /// </summary>
        private static void DrawCallbackManaged(ImDrawList* parentList, ImDrawCmd* cmd)
        {
            if (_gl == null || !_initialized || _currentInspector == null)
                return;

            // Save ImGui's current shader program before switching to ours
            int currentProgram;
            _gl.GetIntegerv(GLGetPName.CurrentProgram, &currentProgram);
            _imguiShaderProgram = (uint)currentProgram;

            SetupCustomShader(_currentInspector);
        }

        /// <summary>
        /// Reset callback to restore ImGui's rendering backend state
        /// </summary>
        private static void ResetCallbackManaged(ImDrawList* parentList, ImDrawCmd* cmd)
        {
            if (_gl == null || !_initialized)
                return;

            // Restore ImGui's shader program
            if (_imguiShaderProgram != 0)
            {
                _gl.UseProgram(_imguiShaderProgram);
            }
        }

        // Keep delegate references to prevent GC
        private static readonly ImDrawCallback _drawCallbackDelegate = DrawCallbackManaged;
        private static readonly ImDrawCallback _resetCallbackDelegate = ResetCallbackManaged;

        /// <summary>
        /// Initialize the render state manager
        /// </summary>
        public static bool Initialize(GL gl, string? glslVersion = null)
        {
            _gl = gl;
            
            if (!ShaderManager.Initialize(gl, glslVersion))
            {
                Console.WriteLine("ERROR: Failed to initialize ShaderManager");
                return false;
            }

            if (!TextureReader.Initialize(gl))
            {
                Console.WriteLine("ERROR: Failed to initialize TextureReader");
                return false;
            }

            _initialized = true;
            Console.WriteLine("ImGuiTexInspect RenderState initialized");
            return true;
        }

        /// <summary>
        /// Shutdown and cleanup
        /// </summary>
        public static void Shutdown()
        {
            ShaderManager.Shutdown();
            TextureReader.Shutdown();
            _gl = null;
            _initialized = false;
        }

        /// <summary>
        /// Add draw callback to render with custom shader
        /// </summary>
        public static void AddDrawCallback(Inspector inspector)
        {
            if (!_initialized)
            {
                Console.WriteLine("WARNING: RenderState not initialized, skipping draw callback");
                return;
            }

            _currentInspector = inspector;
            
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddCallback(_drawCallbackDelegate, null);
        }

        /// <summary>
        /// Add reset callback to restore ImGui's default shader after the image
        /// </summary>
        public static void AddResetCallback()
        {
            if (!_initialized)
            {
                return;
            }

            var drawList = ImGui.GetWindowDrawList();
            // Use our reset callback delegate to restore state
            drawList.AddCallback(_resetCallbackDelegate, null);
        }

        /// <summary>
        /// Setup our custom shader and uniforms
        /// </summary>
        private static void SetupCustomShader(Inspector inspector)
        {
            if (_gl == null) return;

            var shaderProgram = ShaderManager.GetShaderProgram();
            if (shaderProgram == 0)
            {
                Console.WriteLine("WARNING: Shader program not available");
                return;
            }

            var drawData = ImGui.GetDrawData();
            int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
            int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);

            if (fbWidth <= 0 || fbHeight <= 0)
                return;

            // Setup render state similar to ImGui's default
            SetupRenderState(drawData, fbWidth, fbHeight);

            // Get uniform locations
            var uniforms = ShaderManager.GetUniforms();
            var shaderOptions = inspector.CachedShaderOptions;

            // Set texture inspector specific uniforms
            fixed (float* colorTransform = shaderOptions.ColorTransform)
            fixed (float* colorOffset = shaderOptions.ColorOffset)
            {
                _gl.UniformMatrix4fv(uniforms.ColorTransform, 1, false, colorTransform);
                _gl.Uniform4fv(uniforms.ColorOffset, 1, colorOffset);
            }

            _gl.Uniform2f(uniforms.TextureSize, inspector.TextureSize.X, inspector.TextureSize.Y);
            _gl.Uniform3f(uniforms.BackgroundColor, 
                shaderOptions.BackgroundColor.X, 
                shaderOptions.BackgroundColor.Y, 
                shaderOptions.BackgroundColor.Z);
            _gl.Uniform1f(uniforms.PremultiplyAlpha, shaderOptions.PremultiplyAlpha);
            _gl.Uniform1f(uniforms.DisableFinalAlpha, shaderOptions.DisableFinalAlpha);
            _gl.Uniform1i(uniforms.ForceNearestSampling, shaderOptions.ForceNearestSampling ? 1 : 0);
            _gl.Uniform1i(uniforms.CheckeredBackground, shaderOptions.CheckeredBackground ? 1 : 0);
            _gl.Uniform2f(uniforms.GridWidth, shaderOptions.GridWidth.X, shaderOptions.GridWidth.Y);
            _gl.Uniform2f(uniforms.GridCellSize, shaderOptions.GridCellSizeTexels.X, shaderOptions.GridCellSizeTexels.Y);
            _gl.Uniform4f(uniforms.Grid, 
                shaderOptions.GridColor.X, 
                shaderOptions.GridColor.Y, 
                shaderOptions.GridColor.Z, 
                shaderOptions.GridColor.W);
        }

        /// <summary>
        /// Setup OpenGL render state (based on ImGui's implementation)
        /// </summary>
        private static void SetupRenderState(ImDrawDataPtr drawData, int fbWidth, int fbHeight)
        {
            if (_gl == null) return;

            var shaderProgram = ShaderManager.GetShaderProgram();
            var uniforms = ShaderManager.GetUniforms();

            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled
            _gl.Enable(GLEnableCap.Blend);
            _gl.BlendEquation(GLBlendEquationModeEXT.FuncAdd);
            _gl.BlendFuncSeparate(
                GLBlendingFactor.SrcAlpha, GLBlendingFactor.OneMinusSrcAlpha,
                GLBlendingFactor.One, GLBlendingFactor.OneMinusSrcAlpha);
            _gl.Disable(GLEnableCap.CullFace);
            _gl.Disable(GLEnableCap.DepthTest);
            _gl.Disable(GLEnableCap.StencilTest);
            _gl.Enable(GLEnableCap.ScissorTest);

            // Setup viewport and projection matrix
            _gl.Viewport(0, 0, fbWidth, fbHeight);
            
            float L = drawData.DisplayPos.X;
            float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
            float T = drawData.DisplayPos.Y;
            float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

            Span<float> orthoProjection = stackalloc float[16]
            {
                2.0f/(R-L),   0.0f,         0.0f,   0.0f,
                0.0f,         2.0f/(T-B),   0.0f,   0.0f,
                0.0f,         0.0f,        -1.0f,   0.0f,
                (R+L)/(L-R),  (T+B)/(B-T),  0.0f,   1.0f,
            };

            _gl.UseProgram(shaderProgram);
            _gl.Uniform1i(uniforms.Texture, 0);
            
            fixed (float* projPtr = orthoProjection)
            {
                _gl.UniformMatrix4fv(uniforms.ProjMtx, 1, false, projPtr);
            }

            // Bind vertex array attributes
            _gl.EnableVertexAttribArray(uniforms.Position);
            _gl.EnableVertexAttribArray(uniforms.UV);

            // Note: ImDrawVert structure has Position, UV, Color
            // Our shader doesn't use Color, but we need to set the pointers correctly
            int stride = sizeof(ImDrawVert);
            _gl.VertexAttribPointer(uniforms.Position, 2, GLVertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.VertexAttribPointer(uniforms.UV, 2, GLVertexAttribPointerType.Float, false, stride, (void*)8);
        }
    }
}
