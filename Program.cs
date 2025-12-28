using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using OGNES.Components;
using System;
using System.IO;

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
			// Create a dummy iNES file for testing
			string dummyRom = "test.nes";
			byte[] header = new byte[16];
			header[0] = (byte)'N';
			header[1] = (byte)'E';
			header[2] = (byte)'S';
			header[3] = 0x1A;
			header[4] = 1; // 16KB PRG
			header[5] = 1; // 8KB CHR
			header[6] = 0; // Mapper 0
			
			byte[] prg = new byte[16384];
			// Simple program in PRG:
			// LDA #$42
			// STA $0000
			// JMP $8000
			prg[0] = 0xA9; prg[1] = 0x42;
			prg[2] = 0x85; prg[3] = 0x00;
			prg[4] = 0x4C; prg[5] = 0x00; prg[6] = 0x80;

			// Reset vector at $FFFC (relative to $8000, it's at index 16380)
			prg[16380] = 0x00;
			prg[16381] = 0x80;

			byte[] chr = new byte[8192];

			using (var fs = new FileStream(dummyRom, FileMode.Create))
			{
				fs.Write(header);
				fs.Write(prg);
				fs.Write(chr);
			}

			var memory = new Memory();
			var cartridge = new Cartridge(dummyRom);
			memory.Cartridge = cartridge;
			var cpu = new Cpu(memory);

			cpu.Reset();
			Console.WriteLine($"Initial PC: {cpu.PC:X4}, Cycles: {cpu.TotalCycles}");

			for (int i = 0; i < 5; i++)
			{
				cpu.Step();
				Console.WriteLine($"Step {i}: PC={cpu.PC:X4}, A={cpu.A:X2}, Cycles={cpu.TotalCycles}, RAM[0]={memory.Read(0x0000):X2}");
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
