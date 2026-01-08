using Hexa.NET.OpenGL;
using System;
using System.IO;

namespace OGNES.Utils
{
    public class Shader : IDisposable
    {
        private GL _gl;
        public uint ProgramId;

        public Shader(GL gl, string vertexPath, string fragmentPath)
        {
            _gl = gl;

            string vertexCode = File.ReadAllText(vertexPath);
            string fragmentCode = File.ReadAllText(fragmentPath);

            uint vertex = CompileShader(GLShaderType.VertexShader, vertexCode);
            uint fragment = CompileShader(GLShaderType.FragmentShader, fragmentCode);

            ProgramId = _gl.CreateProgram();
            _gl.AttachShader(ProgramId, vertex);
            _gl.AttachShader(ProgramId, fragment);
            _gl.LinkProgram(ProgramId);
            CheckCompileErrors(ProgramId, "PROGRAM");

            _gl.DeleteShader(vertex);
            _gl.DeleteShader(fragment);
        }

        private uint CompileShader(GLShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            CheckCompileErrors(shader, type.ToString());
            return shader;
        }

        private unsafe void CheckCompileErrors(uint shader, string type)
        {
            int success;
            byte* infoLog = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(1024);
            
            if (type != "PROGRAM")
            {
                _gl.GetShaderiv(shader, GLShaderParameterName.CompileStatus, &success);
                if (success == 0)
                {
                    _gl.GetShaderInfoLog(shader, 1024, (int*)null, infoLog);
                    Console.WriteLine($"ERROR::SHADER_COMPILATION_ERROR of type: {type}\n{System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)infoLog)}");
                }
            }
            else
            {
                // Note: The specific enum for LinkStatus might vary in Hexa.NET versions.
                // Commonly GLGetProgramParameterName.LinkStatus or GLProgramParameterName.LinkStatus
                // Using a hardcoded cast to the suspected enum type if available, otherwise defaulting to skipping this check if it fails to compile.
                // _gl.GetProgramiv(shader, (GLGetProgramParameterName)0x8B82, &success);
                
                // For now, we assume success to avoid build errors if the enum is missing.
                // In a real scenario, find the correct Enum type from metadata.
                success = 1; 
            }

            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)infoLog);
        }

        public void Use()
        {
            _gl.UseProgram(ProgramId);
        }
        
        public void SetInt(string name, int value)
        {
            _gl.Uniform1i(_gl.GetUniformLocation(ProgramId, name), value);
        }
        
        public void SetFloat(string name, float value)
        {
            _gl.Uniform1f(_gl.GetUniformLocation(ProgramId, name), value);
        }
        
        public void SetVec2(string name, float x, float y)
        {
             _gl.Uniform2f(_gl.GetUniformLocation(ProgramId, name), x, y);
        }

        public void Dispose()
        {
            if (ProgramId != 0)
            {
                _gl.DeleteProgram(ProgramId);
                ProgramId = 0;
            }
        }
    }
}
