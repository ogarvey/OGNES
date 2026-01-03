using Hexa.NET.GLFW;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;

namespace OGNES
{
	public unsafe class GLFWContext : IGLContext
	{
		private readonly Hexa.NET.GLFW.GLFWwindowPtr _window;

		public GLFWContext(Hexa.NET.GLFW.GLFWwindowPtr window)
		{
			_window = window;
		}

		public nint Handle => (nint)_window.Handle;

		public void MakeCurrent()
		{
			GLFW.MakeContextCurrent(_window);
		}

		public void SwapBuffers()
		{
			GLFW.SwapBuffers(_window);
		}

		public void SwapInterval(int interval)
		{
			GLFW.SwapInterval(interval);
		}

		public nint GetProcAddress(string procName)
		{
			return (nint)GLFW.GetProcAddress(procName);
		}

		public bool TryGetProcAddress(string procName, out nint procAddress)
		{
			procAddress = GetProcAddress(procName);
			return procAddress != 0;
		}

		public bool IsExtensionSupported(string extensionName)
		{
			return GLFW.ExtensionSupported(extensionName) != 0;
		}

		public bool IsCurrent => GLFW.GetCurrentContext() == _window;

		public void Dispose()
		{
		}
	}
}
