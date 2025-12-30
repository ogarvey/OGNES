using System;
using Hexa.NET.OpenGL;

namespace OGNES.UI.ImGuiTexInspect.Backend.OpenGL
{
    /// <summary>
    /// Manages compilation and linking of the custom texture inspector shader
    /// </summary>
    public static unsafe class ShaderManager
    {
        private static GL? _gl;
        private static uint _shaderProgram;
        private static uint _vertexShader;
        private static uint _fragmentShader;
        private static string _glslVersion = "#version 130\n";

        // Uniform locations
        private static int _locTexture;
        private static int _locProjMtx;
        private static int _locTextureSize;
        private static int _locColorTransform;
        private static int _locColorOffset;
        private static int _locBackgroundColor;
        private static int _locPremultiplyAlpha;
        private static int _locDisableFinalAlpha;
        private static int _locForceNearestSampling;
        private static int _locCheckeredBackground;
        private static int _locGrid;
        private static int _locGridWidth;
        private static int _locGridCellSize;

        // Attribute locations
        private static uint _locPosition;
        private static uint _locUV;

        public static bool IsInitialized => _shaderProgram != 0;

        /// <summary>
        /// Initialize the shader manager with a GL context
        /// </summary>
        public static bool Initialize(GL gl, string? glslVersion = null)
        {
            _gl = gl;
            
            if (glslVersion != null)
            {
                _glslVersion = glslVersion + "\n";
            }

            return BuildShader();
        }

        /// <summary>
        /// Clean up shader resources
        /// </summary>
        public static void Shutdown()
        {
            if (_gl == null) return;

            if (_vertexShader != 0)
            {
                _gl.DeleteShader(_vertexShader);
                _vertexShader = 0;
            }

            if (_fragmentShader != 0)
            {
                _gl.DeleteShader(_fragmentShader);
                _fragmentShader = 0;
            }

            if (_shaderProgram != 0)
            {
                _gl.DeleteProgram(_shaderProgram);
                _shaderProgram = 0;
            }

            _gl = null;
        }

        /// <summary>
        /// Get the shader program ID
        /// </summary>
        public static uint GetShaderProgram() => _shaderProgram;

        /// <summary>
        /// Get uniform locations structure for quick access
        /// </summary>
        public static ShaderUniforms GetUniforms()
        {
            return new ShaderUniforms
            {
                Texture = _locTexture,
                ProjMtx = _locProjMtx,
                TextureSize = _locTextureSize,
                ColorTransform = _locColorTransform,
                ColorOffset = _locColorOffset,
                BackgroundColor = _locBackgroundColor,
                PremultiplyAlpha = _locPremultiplyAlpha,
                DisableFinalAlpha = _locDisableFinalAlpha,
                ForceNearestSampling = _locForceNearestSampling,
                CheckeredBackground = _locCheckeredBackground,
                Grid = _locGrid,
                GridWidth = _locGridWidth,
                GridCellSize = _locGridCellSize,
                Position = _locPosition,
                UV = _locUV
            };
        }

        private static bool BuildShader()
        {
            if (_gl == null)
            {
                Console.WriteLine("ERROR: GL context not set in ShaderManager");
                return false;
            }

            // Determine GLSL version from string
            int glslVersionNum = 130;
            if (_glslVersion.Contains("version "))
            {
                var versionStr = _glslVersion.Split(' ')[1].TrimEnd('\n');
                if (int.TryParse(versionStr, out int parsedVersion))
                {
                    glslVersionNum = parsedVersion;
                }
            }

            // Select appropriate shader source based on version
            string vertexSource;
            string fragmentSource;

            if (glslVersionNum < 130)
            {
                vertexSource = VertexShader_GLSL_120;
                fragmentSource = FragmentShader_GLSL_120;
            }
            else if (glslVersionNum >= 410)
            {
                vertexSource = VertexShader_GLSL_410;
                fragmentSource = FragmentShader_GLSL_410;
            }
            else if (glslVersionNum == 300)
            {
                vertexSource = VertexShader_GLSL_300_ES;
                fragmentSource = FragmentShader_GLSL_300_ES;
            }
            else
            {
                vertexSource = VertexShader_GLSL_130;
                fragmentSource = FragmentShader_GLSL_130;
            }

            // Compile vertex shader
            _vertexShader = _gl.CreateShader(GLShaderType.VertexShader);
            var vertexSourceWithVersion = _glslVersion + vertexSource;
            _gl.ShaderSource(_vertexShader, vertexSourceWithVersion);
            _gl.CompileShader(_vertexShader);
            if (!CheckShader(_vertexShader, "vertex shader"))
                return false;

            // Compile fragment shader
            _fragmentShader = _gl.CreateShader(GLShaderType.FragmentShader);
            var fragmentSourceWithVersion = _glslVersion + fragmentSource;
            _gl.ShaderSource(_fragmentShader, fragmentSourceWithVersion);
            _gl.CompileShader(_fragmentShader);
            if (!CheckShader(_fragmentShader, "fragment shader"))
                return false;

            // Link program
            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, _vertexShader);
            _gl.AttachShader(_shaderProgram, _fragmentShader);
            _gl.LinkProgram(_shaderProgram);
            if (!CheckProgram(_shaderProgram, "shader program"))
                return false;

            // Get uniform locations
            _locTexture = _gl.GetUniformLocation(_shaderProgram, "Texture");
            _locProjMtx = _gl.GetUniformLocation(_shaderProgram, "ProjMtx");
            _locTextureSize = _gl.GetUniformLocation(_shaderProgram, "TextureSize");
            _locColorTransform = _gl.GetUniformLocation(_shaderProgram, "ColorTransform");
            _locColorOffset = _gl.GetUniformLocation(_shaderProgram, "ColorOffset");
            _locBackgroundColor = _gl.GetUniformLocation(_shaderProgram, "BackgroundColor");
            _locPremultiplyAlpha = _gl.GetUniformLocation(_shaderProgram, "PremultiplyAlpha");
            _locDisableFinalAlpha = _gl.GetUniformLocation(_shaderProgram, "DisableFinalAlpha");
            _locCheckeredBackground = _gl.GetUniformLocation(_shaderProgram, "CheckeredBackground");
            _locForceNearestSampling = _gl.GetUniformLocation(_shaderProgram, "ForceNearestSampling");
            _locGrid = _gl.GetUniformLocation(_shaderProgram, "Grid");
            _locGridWidth = _gl.GetUniformLocation(_shaderProgram, "GridWidth");
            _locGridCellSize = _gl.GetUniformLocation(_shaderProgram, "GridCellSize");

            // Get attribute locations
            _locPosition = (uint)_gl.GetAttribLocation(_shaderProgram, "Position");
            _locUV = (uint)_gl.GetAttribLocation(_shaderProgram, "UV");

            Console.WriteLine($"ImGuiTexInspect shader compiled successfully (GLSL {glslVersionNum})");
            return true;
        }

        private static bool CheckShader(uint handle, string desc)
        {
            if (_gl == null) return false;

            int status;
            _gl.GetShaderiv(handle, GLShaderParameterName.CompileStatus, &status);
            
            int logLength;
            _gl.GetShaderiv(handle, GLShaderParameterName.InfoLogLength, &logLength);

            if (status == 0)
            {
                Console.WriteLine($"ERROR: Failed to compile {desc}");
            }

            if (logLength > 1)
            {
                var log = _gl.GetShaderInfoLog(handle);
                Console.WriteLine($"Shader log for {desc}:\n{log}");
            }

            return status != 0;
        }

        private static bool CheckProgram(uint handle, string desc)
        {
            if (_gl == null) return false;

            int status;
            _gl.GetProgramiv(handle, GLProgramPropertyARB.LinkStatus, &status);
            
            int logLength;
            _gl.GetProgramiv(handle, GLProgramPropertyARB.InfoLogLength, &logLength);

            if (status == 0)
            {
                Console.WriteLine($"ERROR: Failed to link {desc}");
            }

            if (logLength > 1)
            {
                var log = _gl.GetProgramInfoLog(handle);
                Console.WriteLine($"Program log for {desc}:\n{log}");
            }

            return status != 0;
        }

        #region Vertex Shaders

        private const string VertexShader_GLSL_120 = @"
uniform mat4 ProjMtx;
attribute vec2 Position;
attribute vec2 UV;
varying vec2 Frag_UV;
void main()
{
    Frag_UV = UV;
    gl_Position = ProjMtx * vec4(Position.xy,0,1);
}
";

        private const string VertexShader_GLSL_130 = @"
uniform mat4 ProjMtx;
in vec2 Position;
in vec2 UV;
out vec2 Frag_UV;
void main()
{
    Frag_UV = UV;
    gl_Position = ProjMtx * vec4(Position.xy,0,1);
}
";

        private const string VertexShader_GLSL_300_ES = @"
precision mediump float;
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
uniform mat4 ProjMtx;
out vec2 Frag_UV;
void main()
{
    Frag_UV = UV;
    gl_Position = ProjMtx * vec4(Position.xy,0,1);
}
";

        private const string VertexShader_GLSL_410 = @"
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
uniform mat4 ProjMtx;
out vec2 Frag_UV;
void main()
{
    Frag_UV = UV;
    gl_Position = ProjMtx * vec4(Position.xy,0,1);
}
";

        #endregion

        #region Fragment Shaders

        private const string FragmentShader_GLSL_120 = @"
#ifdef GL_ES
    precision mediump float;
#endif
uniform sampler2D Texture;
uniform vec2 TextureSize;
uniform mat4 ColorTransform;
uniform vec4 ColorOffset;
uniform vec3 BackgroundColor;
uniform float PremultiplyAlpha;
uniform float DisableFinalAlpha;
uniform bool ForceNearestSampling;
uniform bool CheckeredBackground;
uniform vec4 Grid;
uniform vec2 GridWidth;
uniform vec2 GridCellSize;
varying vec2 Frag_UV;
void main()
{
    vec2 uv;
    vec2 texel = Frag_UV * TextureSize;
    if (ForceNearestSampling)
        uv = (floor(texel) + vec2(0.5,0.5)) / TextureSize;
    else
        uv = Frag_UV;
    vec2 cellSize = max(GridCellSize, vec2(1.0, 1.0));
    vec2 texelEdge = step(mod(texel, cellSize), GridWidth);
    float isGrid = max(texelEdge.x, texelEdge.y);
    vec4 ct = ColorTransform * texture2D(Texture, uv) + ColorOffset;
    ct.rgb = ct.rgb * mix(1.0, ct.a, PremultiplyAlpha);
    
    vec3 bg = BackgroundColor;
    if (CheckeredBackground)
    {
        vec2 pos = gl_FragCoord.xy;
        float checker = mod(floor(pos.x / 10.0) + floor(pos.y / 10.0), 2.0);
        bg = mix(vec3(0.4, 0.4, 0.4), vec3(0.6, 0.6, 0.6), checker);
    }
    
    ct.rgb += bg * (1.0-ct.a);
    ct.a = mix(ct.a, 1.0, DisableFinalAlpha);
    ct = mix(ct, vec4(Grid.rgb,1), Grid.a * isGrid);
    gl_FragColor = ct;
}
";

        private const string FragmentShader_GLSL_130 = @"
uniform sampler2D Texture;
uniform vec2 TextureSize;
uniform mat4 ColorTransform;
uniform vec4 ColorOffset;
uniform vec3 BackgroundColor;
uniform float PremultiplyAlpha;
uniform float DisableFinalAlpha;
uniform bool ForceNearestSampling;
uniform bool CheckeredBackground;
uniform vec4 Grid;
uniform vec2 GridWidth;
uniform vec2 GridCellSize;
in vec2 Frag_UV;
out vec4 Out_Color;
void main()
{
    vec2 uv;
    vec2 texel = Frag_UV * TextureSize;
    if (ForceNearestSampling)
        uv = (floor(texel) + vec2(0.5,0.5)) / TextureSize;
    else
        uv = Frag_UV;
    vec2 cellSize = max(GridCellSize, vec2(1.0, 1.0));
    vec2 texelEdge = step(mod(texel, cellSize), GridWidth);
    float isGrid = max(texelEdge.x, texelEdge.y);
    vec4 ct = ColorTransform * texture(Texture, uv) + ColorOffset;
    ct.rgb = ct.rgb * mix(1.0, ct.a, PremultiplyAlpha);
    
    vec3 bg = BackgroundColor;
    if (CheckeredBackground)
    {
        vec2 pos = gl_FragCoord.xy;
        float checker = mod(floor(pos.x / 10.0) + floor(pos.y / 10.0), 2.0);
        bg = mix(vec3(0.4, 0.4, 0.4), vec3(0.6, 0.6, 0.6), checker);
    }
    
    ct.rgb += bg * (1.0-ct.a);
    ct.a = mix(ct.a, 1.0, DisableFinalAlpha);
    ct = mix(ct, vec4(Grid.rgb,1), Grid.a * isGrid);
    Out_Color = ct;
}
";

        private const string FragmentShader_GLSL_300_ES = @"
precision mediump float;
uniform sampler2D Texture;
uniform vec2 TextureSize;
uniform mat4 ColorTransform;
uniform vec4 ColorOffset;
uniform vec3 BackgroundColor;
uniform float PremultiplyAlpha;
uniform float DisableFinalAlpha;
uniform bool ForceNearestSampling;
uniform bool CheckeredBackground;
uniform vec4 Grid;
uniform vec2 GridWidth;
uniform vec2 GridCellSize;
in vec2 Frag_UV;
layout (location = 0) out vec4 Out_Color;
void main()
{
    vec2 uv;
    vec2 texel = Frag_UV * TextureSize;
    if (ForceNearestSampling)
        uv = (floor(texel) + vec2(0.5,0.5)) / TextureSize;
    else
        uv = Frag_UV;
    vec2 cellSize = max(GridCellSize, vec2(1.0, 1.0));
    vec2 texelEdge = step(mod(texel, cellSize), GridWidth);
    float isGrid = max(texelEdge.x, texelEdge.y);
    vec4 ct = ColorTransform * texture(Texture, uv) + ColorOffset;
    ct.rgb = ct.rgb * mix(1.0, ct.a, PremultiplyAlpha);
    
    vec3 bg = BackgroundColor;
    if (CheckeredBackground)
    {
        vec2 pos = gl_FragCoord.xy;
        float checker = mod(floor(pos.x / 10.0) + floor(pos.y / 10.0), 2.0);
        bg = mix(vec3(0.4, 0.4, 0.4), vec3(0.6, 0.6, 0.6), checker);
    }
    
    ct.rgb += bg * (1.0-ct.a);
    ct.a = mix(ct.a, 1.0, DisableFinalAlpha);
    ct = mix(ct, vec4(Grid.rgb,1), Grid.a * isGrid);
    Out_Color = ct;
}
";

        private const string FragmentShader_GLSL_410 = @"
uniform sampler2D Texture;
uniform vec2 TextureSize;
uniform mat4 ColorTransform;
uniform vec4 ColorOffset;
uniform vec3 BackgroundColor;
uniform float PremultiplyAlpha;
uniform float DisableFinalAlpha;
uniform bool ForceNearestSampling;
uniform bool CheckeredBackground;
uniform vec4 Grid;
uniform vec2 GridWidth;
uniform vec2 GridCellSize;
in vec2 Frag_UV;
layout (location = 0) out vec4 Out_Color;
void main()
{
    vec2 uv;
    vec2 texel = Frag_UV * TextureSize;
    if (ForceNearestSampling)
        uv = (floor(texel) + vec2(0.5,0.5)) / TextureSize;
    else
        uv = Frag_UV;
    vec2 cellSize = max(GridCellSize, vec2(1.0, 1.0));
    vec2 texelEdge = step(mod(texel, cellSize), GridWidth);
    float isGrid = max(texelEdge.x, texelEdge.y);
    vec4 ct = ColorTransform * texture(Texture, uv) + ColorOffset;
    ct.rgb = ct.rgb * mix(1.0, ct.a, PremultiplyAlpha);
    
    vec3 bg = BackgroundColor;
    if (CheckeredBackground)
    {
        vec2 pos = gl_FragCoord.xy;
        float checker = mod(floor(pos.x / 10.0) + floor(pos.y / 10.0), 2.0);
        bg = mix(vec3(0.4, 0.4, 0.4), vec3(0.6, 0.6, 0.6), checker);
    }
    
    ct.rgb += bg * (1.0-ct.a);
    ct.a = mix(ct.a, 1.0, DisableFinalAlpha);
    ct = mix(ct, vec4(Grid.rgb,1), Grid.a * isGrid);
    Out_Color = ct;
}
";

        #endregion
    }

    /// <summary>
    /// Shader uniform locations for quick access
    /// </summary>
    public struct ShaderUniforms
    {
        public int Texture;
        public int ProjMtx;
        public int TextureSize;
        public int ColorTransform;
        public int ColorOffset;
        public int BackgroundColor;
        public int PremultiplyAlpha;
        public int DisableFinalAlpha;
        public int ForceNearestSampling;
        public int CheckeredBackground;
        public int Grid;
        public int GridWidth;
        public int GridCellSize;
        public uint Position;
        public uint UV;
    }
}
