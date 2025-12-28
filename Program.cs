using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Widgets.Dialogs;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using OGNES.Components;
using OGNES.UI.General;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OGNES
{
	public class AppSettings
	{
		public string? LastRomDirectory { get; set; }
	}

	public unsafe class Program
	{
		private Hexa.NET.GLFW.GLFWwindowPtr _window;
		private GL _gl = null!;
		private ImGuiContextPtr _guiContext;
		private const float UiScale = 1.25f;
		private const int NesScreenWidth = 256;
		private const int NesScreenHeight = 240;

		private Memory _memory = null!;
		private Cpu _cpu = null!;
		private Ppu _ppu = null!;
		private Cartridge? _cartridge;

		private List<string> _logBuffer = new();
		private bool _isRunning = false;
		private string _romPath = "";
		private string? _errorMessage;
		private string _testOutput = "";
		private byte _testStatus = 0x80;
		private bool _testActive = false;
		private FileOpenDialog _fileOpenDialog = null!;
		private AppSettings _settings = new();
		private const string SettingsFile = "settings.json";

		public static void Main(string[] args)
		{
			var program = new Program();
			if (args.Length > 0)
			{
				program.RunHeadless(args);
			}
			else
			{
				program.RunGui();
			}
		}

		private void LoadSettings()
		{
			if (File.Exists(SettingsFile))
			{
				try
				{
					string json = File.ReadAllText(SettingsFile);
					_settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
				}
				catch
				{
					_settings = new AppSettings();
				}
			}
		}

		private void SaveSettings()
		{
			try
			{
				string json = JsonSerializer.Serialize(_settings);
				File.WriteAllText(SettingsFile, json);
			}
			catch
			{
				// ignore
			}
		}

		private void RunHeadless(string[] args)
		{
			string romPath = args[0];
			int steps = args.Length > 1 ? int.Parse(args[1]) : 1000;
			string outputPath = args.Length > 2 ? args[2] : "log.txt";

			if (!File.Exists(romPath))
			{
				Console.WriteLine($"ROM not found: {romPath}");
				return;
			}

			try
			{
				InitEmulator(romPath);

				using var writer = new StreamWriter(outputPath);
				for (int i = 0; i < steps; i++)
				{
					writer.WriteLine(_cpu.GetStateLog());
					_cpu.Step();
					
					// Check for test completion in headless mode
					if (_memory.Peek(0x6001) == 0xDE && _memory.Peek(0x6002) == 0xB0 && _memory.Peek(0x6003) == 0x61)
					{
						byte status = _memory.Peek(0x6000);
						if (status < 0x80)
						{
							Console.WriteLine($"Test completed with status: 0x{status:X2}");
							break;
						}
					}
				}
				
				// Final test output check
				UpdateTestStatus();
				if (_testActive)
				{
					Console.WriteLine("Test Output:");
					Console.WriteLine(_testOutput);
				}

				Console.WriteLine($"Headless run complete. Log saved to {outputPath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
			}
		}

		private void InitEmulator(string romPath)
		{
			var ppu = new Ppu();
			var memory = new Memory { Ppu = ppu };
			var cartridge = new Cartridge(romPath);
			memory.Cartridge = cartridge;
			var cpu = new Cpu(memory);
			
			cpu.Reset();
			ppu.Reset();

			// Only assign to fields if initialization succeeded
			_ppu = ppu;
			_memory = memory;
			_cartridge = cartridge;
			_cpu = cpu;
			_romPath = romPath;
			_testOutput = "";
			_testStatus = 0x80;
			_testActive = false;
		}

		private void RunGui()
		{
			LoadSettings();

			if (GLFW.Init() == 0) return;

			GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MAJOR, 3);
			GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MINOR, 3);
			GLFW.WindowHint(GLFW.GLFW_OPENGL_PROFILE, GLFW.GLFW_OPENGL_CORE_PROFILE);

			_window = GLFW.CreateWindow(1280, 720, "OGNES Emulator", null, null);
			if (_window.IsNull)
			{
				GLFW.Terminate();
				return;
			}

			GLFW.MakeContextCurrent(_window);
			GLFW.SwapInterval(1);

			_gl = new GL(new GLFWContext(_window));
			_guiContext = ImGui.CreateContext();
			ImGui.SetCurrentContext(_guiContext);
			var io = ImGui.GetIO();
			io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
			io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
			io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

			ImGui.StyleColorsDark();
			
			ImGuiImplGLFW.SetCurrentContext(_guiContext);
			var imguiWindowPtr = Unsafe.BitCast<Hexa.NET.GLFW.GLFWwindowPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWwindowPtr>(_window);
			ImGuiImplGLFW.InitForOpenGL(imguiWindowPtr, true);
			
			ImGuiImplOpenGL3.SetCurrentContext(_guiContext);
			ImGuiImplOpenGL3.Init("#version 330");

			_fileOpenDialog = new FileOpenDialog();
			if (!string.IsNullOrEmpty(_settings.LastRomDirectory))
			{
				_fileOpenDialog.CurrentFolder = _settings.LastRomDirectory;
			}

			while (GLFW.WindowShouldClose(_window) == 0)
			{
				GLFW.PollEvents();

				ImGuiImplOpenGL3.NewFrame();
				ImGuiImplGLFW.NewFrame();
				ImGui.NewFrame();

				// Dockspace
				var dockspaceId = ImGui.GetID("MyDockSpace");
				ImGui.DockSpaceOverViewport(dockspaceId, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

				RenderUI();

				if (_cpu != null)
				{
					UpdateTestStatus();
				}

				if (_isRunning && _cpu != null)
				{
					// Run for one frame's worth of cycles or just a few steps for now
					for (int i = 0; i < 100; i++)
					{
						_logBuffer.Add(_cpu.GetStateLog());
						if (_logBuffer.Count > 1000) _logBuffer.RemoveAt(0);
						_cpu.Step();
					}
				}

				ImGui.Render();
				int display_w, display_h;
				GLFW.GetFramebufferSize(_window, &display_w, &display_h);
				_gl.Viewport(0, 0, display_w, display_h);
				_gl.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
				_gl.Clear(GLClearBufferMask.ColorBufferBit);
				ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

				if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
				{
					ImGui.UpdatePlatformWindows();
					ImGui.RenderPlatformWindowsDefault();
					GLFW.MakeContextCurrent(_window);
				}

				GLFW.SwapBuffers(_window);
			}

			SaveSettings();

			ImGuiImplOpenGL3.Shutdown();
			ImGuiImplGLFW.Shutdown();
			ImGui.DestroyContext();
			GLFW.DestroyWindow(_window);
			GLFW.Terminate();
		}

		private void RenderUI()
		{
			if (ImGui.BeginMainMenuBar())
			{
				if (ImGui.BeginMenu("File"))
				{
					if (ImGui.MenuItem("Load ROM"))
					{
						_fileOpenDialog.Show(LoadRomCallback);
					}
					ImGui.EndMenu();
				}
				ImGui.EndMainMenuBar();
			}

			_fileOpenDialog.Draw(ImGuiWindowFlags.None);

			if (ImGui.BeginPopupModal("Error", null, ImGuiWindowFlags.AlwaysAutoResize))
			{
				ImGui.Text(_errorMessage ?? "Unknown error");
				if (ImGui.Button("OK"))
				{
					_errorMessage = null;
					ImGui.CloseCurrentPopup();
				}
				ImGui.EndPopup();
			}

			ImGui.Begin("CPU Log");
			if (ImGui.Button(_isRunning ? "Pause" : "Resume"))
			{
				_isRunning = !_isRunning;
			}
			ImGui.SameLine();
			if (ImGui.Button("Step"))
			{
				if (_cpu != null)
				{
					_logBuffer.Add(_cpu.GetStateLog());
					if (_logBuffer.Count > 1000) _logBuffer.RemoveAt(0);
					_cpu.Step();
				}
			}

			ImGui.BeginChild("LogScroll");
			foreach (var line in _logBuffer)
			{
				ImGui.Text(line);
			}
			if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
				ImGui.SetScrollHereY(1.0f);
			ImGui.EndChild();
			ImGui.End();

			if (_testActive)
			{
				ImGui.Begin("Test Status");
				string statusText = _testStatus switch
				{
					0x80 => "Running...",
					0x81 => "Reset Required",
					var s when s < 0x80 => $"Completed (Result: 0x{s:X2})",
					_ => $"Unknown (0x{_testStatus:X2})"
				};
				ImGui.Text($"Status: {statusText}");
				if (_testStatus == 0x81)
				{
					if (ImGui.Button("Reset CPU"))
					{
						_cpu?.Reset();
					}
				}
				ImGui.Separator();
				ImGui.TextWrapped(_testOutput);
				ImGui.End();
			}
		}

		private void UpdateTestStatus()
		{
			// Check signature at $6001-$6003: $DE $B0 $61
			if (_memory.Peek(0x6001) == 0xDE && _memory.Peek(0x6002) == 0xB0 && _memory.Peek(0x6003) == 0x61)
			{
				_testActive = true;
				_testStatus = _memory.Peek(0x6000);

				// Read text at $6004+
				var sb = new System.Text.StringBuilder();
				ushort addr = 0x6004;
				while (true)
				{
					byte b = _memory.Peek(addr++);
					if (b == 0 || addr > 0x7FFF) break;
					sb.Append((char)b);
				}
				_testOutput = sb.ToString();
			}
		}

		private void LoadRomCallback(object? sender, DialogResult result)
		{
			if (result == DialogResult.Ok)
			{
				var path = _fileOpenDialog.SelectedFile;
				if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
				{
					try
					{
						// Stop current emulation and clear state
						_isRunning = false;
						_logBuffer.Clear();

						InitEmulator(path);
						
						// Update settings with the new directory
						string? dir = Path.GetDirectoryName(path);
						if (dir != null)
						{
							_settings.LastRomDirectory = dir;
							SaveSettings();
							_fileOpenDialog.CurrentFolder = dir;
						}

						_isRunning = true;
						_errorMessage = null;
					}
					catch (Exception ex)
					{
						_errorMessage = $"Failed to load ROM: {ex.Message}";
						_isRunning = false;
						ImGui.OpenPopup("Error");
					}
				}
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
