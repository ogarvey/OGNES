using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using OGNES.Components;
using System;

namespace OGNES
{
	public unsafe class Program
	{
		private Hexa.NET.GLFW.GLFWwindowPtr _window;
		private GL _gl = null!;
		private ImGuiContextPtr _guiContext;
		private const float UiScale = 1.25f;
		private const int NesScreenWidth = 256;
		private const int NesScreenHeight = 240;
		public static void Main(string[] args)
		{
			var memory = new Memory();
			var cpu = new Cpu(memory);

			// Simple program:
			// LDA #$10
			// STA $0000
			// NOP
			// JMP $0600
			
			memory.Write(0x0600, 0xA9); // LDA #
			memory.Write(0x0601, 0x10); // $10
			memory.Write(0x0602, 0x85); // STA zp
			memory.Write(0x0603, 0x00); // $00
			memory.Write(0x0604, 0xEA); // NOP
			memory.Write(0x0605, 0x4C); // JMP abs
			memory.Write(0x0606, 0x00); // $00
			memory.Write(0x0607, 0x06); // $06

			// Set reset vector to 0x0600
			memory.Write(0xFFFC, 0x00);
			memory.Write(0xFFFD, 0x06);

			cpu.Reset();
			Console.WriteLine($"Initial PC: {cpu.PC:X4}, Cycles: {cpu.TotalCycles}");

			for (int i = 0; i < 5; i++)
			{
				cpu.Step();
				Console.WriteLine($"Step {i}: PC={cpu.PC:X4}, A={cpu.A:X2}, Cycles={cpu.TotalCycles}");
			}
		}
	}
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
			procAddress = (nint)GLFW.GetProcAddress(procName);
			return procAddress != 0;
		}

		public bool IsExtensionSupported(string extensionName)
		{
			return GLFW.ExtensionSupported(extensionName) != 0;
		}

		public bool IsCurrent => GLFW.GetCurrentContext() == _window;
		public void Dispose() { }
	}

}
