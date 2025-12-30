using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Widgets.Dialogs;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using OGNES.Components;
using OGNES.UI;
using OGNES.UI.General;
using OGNES.UI.ImGuiTexInspect;
using NAudio.Wave;
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
		public int TargetFps { get; set; } = 60;
		public Dictionary<string, int> KeyMappings { get; set; } = new()
		{
			{ "A", (int)GlfwKey.Z },
			{ "B", (int)GlfwKey.X },
			{ "Select", (int)GlfwKey.RightShift },
			{ "Start", (int)GlfwKey.Enter },
			{ "Up", (int)GlfwKey.Up },
			{ "Down", (int)GlfwKey.Down },
			{ "Left", (int)GlfwKey.Left },
			{ "Right", (int)GlfwKey.Right }
		};
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
		private Apu _apu = null!;
		private Cartridge? _cartridge;

		private AudioOutput? _audioOutput;
		private const int AudioSampleRate = 44100;
		private const double CpuFrequency = 1789773.0;
		private const double CyclesPerSample = CpuFrequency / AudioSampleRate;

		private uint _textureId;
		private List<string> _logBuffer = new();
		private bool _logEnabled = false;
		private bool _isRunning = false;
		private bool _isPaused = false;
		private bool _stepFrame = false;
		private bool _pPressed = false;
		private bool _fPressed = false;
		private string _romPath = "";
		private string? _errorMessage;
		private string _testOutput = "";
		private byte _testStatus = 0x80;
		private bool _testActive = false;
		private FileOpenDialog _fileOpenDialog = null!;
		private NesWindow _nesWindow = new();
		private CpuLogWindow _cpuLogWindow = new();
		private TestStatusWindow _testStatusWindow = new();
		private SettingsWindow _settingsWindow = new();
		private PpuDebugWindow _ppuDebugWindow = new();
		private bool _settingsOpen = false;
		private AppSettings _settings = new();
		private const string SettingsFile = "settings.json";

		private uint _pt0TextureId;
		private uint _pt1TextureId;
		private uint _ntTextureId;
		private uint _spriteAtlasTextureId;
		private uint _spritePreviewTextureId;
		private uint _spriteLayerTextureId;

		private System.Diagnostics.Stopwatch _stopwatch = new();
		private double _lastTime;
		private double _accumulator;

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
			var apu = new Apu();
			var memory = new Memory { Ppu = ppu, Apu = apu };
			apu.Memory = memory;
			var cartridge = new Cartridge(romPath);
			memory.Cartridge = cartridge;
			ppu.Cartridge = cartridge;
			var cpu = new Cpu(memory);
			
			cpu.Reset();
			ppu.Reset();

			// Initialize Audio
			if (_audioOutput != null)
			{
				_audioOutput.Dispose();
			}
			_audioOutput = new AudioOutput(AudioSampleRate);
			apu.SetSink(_audioOutput);

			// Only assign to fields if initialization succeeded
			_ppu = ppu;
			_apu = apu;
			_memory = memory;
			_cartridge = cartridge;
			_cpu = cpu;
			_romPath = romPath;
			_testOutput = "";
			_testStatus = 0x80;
			_testActive = false;
		}

		private uint CreateTexture()
		{
			uint tex;
			_gl.GenTextures(1, &tex);
			_gl.BindTexture(GLTextureTarget.Texture2D, tex);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapS, (int)GLTextureWrapMode.ClampToEdge);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapT, (int)GLTextureWrapMode.ClampToEdge);
			_gl.BindTexture(GLTextureTarget.Texture2D, 0);
			return tex;
		}

		private void UpdateTexture()
		{
			if (_ppu == null) return;
			_gl.BindTexture(GLTextureTarget.Texture2D, _textureId);
			fixed (byte* ptr = _ppu.FrameBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, NesScreenWidth, NesScreenHeight, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}
			_gl.BindTexture(GLTextureTarget.Texture2D, 0);
		}

		private void UpdateDebugTextures()
		{
			if (_ppu == null || !_ppuDebugWindow.Visible) return;

			_ppuDebugWindow.UpdateBuffers(_ppu);

			// Update PT0
			_gl.BindTexture(GLTextureTarget.Texture2D, _pt0TextureId);
			fixed (byte* ptr = _ppuDebugWindow.PatternTable0Buffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update PT1
			_gl.BindTexture(GLTextureTarget.Texture2D, _pt1TextureId);
			fixed (byte* ptr = _ppuDebugWindow.PatternTable1Buffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update NT
			_gl.BindTexture(GLTextureTarget.Texture2D, _ntTextureId);
			fixed (byte* ptr = _ppuDebugWindow.NameTableBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 512, 480, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Atlas
			_gl.BindTexture(GLTextureTarget.Texture2D, _spriteAtlasTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpriteAtlasBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Preview
			_gl.BindTexture(GLTextureTarget.Texture2D, _spritePreviewTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpritePreviewBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 64, 64, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Layer
			_gl.BindTexture(GLTextureTarget.Texture2D, _spriteLayerTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpriteLayerBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, 256, 240, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			_gl.BindTexture(GLTextureTarget.Texture2D, 0);
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
			// io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
			io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
			io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

			ImGui.StyleColorsDark();
			
			ImGuiImplGLFW.SetCurrentContext(_guiContext);
			var imguiWindowPtr = Unsafe.BitCast<Hexa.NET.GLFW.GLFWwindowPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWwindowPtr>(_window);
			ImGuiImplGLFW.InitForOpenGL(imguiWindowPtr, true);
			
			ImGuiImplOpenGL3.SetCurrentContext(_guiContext);
			ImGuiImplOpenGL3.Init("#version 330");

			// Initialize ImGuiTexInspect render-state/shader integration.
			// Without this, inspectors still render (via ImGui.Image), but grid/alpha/background features won't work.
			if (!OGNES.UI.ImGuiTexInspect.Backend.OpenGL.RenderState.Initialize(_gl, "#version 330"))
			{
				Console.WriteLine("WARNING: Failed to init ImGuiTexInspect RenderState (grid/alpha modes may not work).");
			}

			_textureId = CreateTexture();
			_pt0TextureId = CreateTexture();
			_pt1TextureId = CreateTexture();
			_ntTextureId = CreateTexture();
			_spriteAtlasTextureId = CreateTexture();
			_spritePreviewTextureId = CreateTexture();
			_spriteLayerTextureId = CreateTexture();

			_fileOpenDialog = new FileOpenDialog();
			if (!string.IsNullOrEmpty(_settings.LastRomDirectory))
			{
				_fileOpenDialog.CurrentFolder = _settings.LastRomDirectory;
			}

			_stopwatch.Start();
			_lastTime = 0;
			_accumulator = 0;

			while (GLFW.WindowShouldClose(_window) == 0)
			{
				double currentTime = _stopwatch.Elapsed.TotalSeconds;
				double deltaTime = currentTime - _lastTime;
				_lastTime = currentTime;

				// Cap deltaTime to avoid spiral of death if we fall behind
				if (deltaTime > 0.1) deltaTime = 0.1;

				_accumulator += deltaTime;
				double targetFrameTime = 1.0 / _settings.TargetFps;

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
					ProcessInput();
				}

				_settingsWindow.Update(_settings, _window);

				if ((_isRunning && !_isPaused || _stepFrame) && _cpu != null && _ppu != null)
				{
					if (_stepFrame)
					{
						_stepFrame = false;
						RunFrame();
					}
					else
					{
						while (_accumulator >= targetFrameTime)
						{
							RunFrame();
							_accumulator -= targetFrameTime;
						}
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

			if (_audioOutput != null)
			{
				_audioOutput.Dispose();
			}

			OGNES.UI.ImGuiTexInspect.Backend.OpenGL.RenderState.Shutdown();
			InspectorPanel.Shutdown();
			ImGuiImplOpenGL3.Shutdown();
			ImGuiImplGLFW.Shutdown();
			ImGui.DestroyContext();
			GLFW.DestroyWindow(_window);
			GLFW.Terminate();
		}

		private void RunFrame()
		{
			if (_cpu == null || _ppu == null) return;

			_ppu.FrameReady = false;
			int safetyCounter = 0;
			while (!_ppu.FrameReady && safetyCounter < 100000)
			{
				if (_logEnabled)
				{
					_logBuffer.Add(_cpu.GetStateLog());
					if (_logBuffer.Count > 1000) _logBuffer.RemoveAt(0);
				}
				_cpu.Step();
				safetyCounter++;
			}

			if (_ppu.FrameReady)
			{
				UpdateTexture();
				UpdateDebugTextures();
			}
		}

		private void ProcessInput()
		{
			if (ImGui.GetIO().WantCaptureKeyboard) return;

			if (_memory == null) return;

			// Emulator controls
			bool pDown = GLFW.GetKey(_window, (int)GlfwKey.P) == 1;
			if (pDown && !_pPressed)
			{
				_isPaused = !_isPaused;
			}
			_pPressed = pDown;

			bool fDown = GLFW.GetKey(_window, (int)GlfwKey.F) == 1;
			if (fDown && !_fPressed)
			{
				_stepFrame = true;
			}
			_fPressed = fDown;

			foreach (var mapping in _settings.KeyMappings)
			{
				bool pressed = GLFW.GetKey(_window, mapping.Value) == 1;
				Joypad.Button button = mapping.Key switch
				{
					"A" => Joypad.Button.A,
					"B" => Joypad.Button.B,
					"Select" => Joypad.Button.Select,
					"Start" => Joypad.Button.Start,
					"Up" => Joypad.Button.Up,
					"Down" => Joypad.Button.Down,
					"Left" => Joypad.Button.Left,
					"Right" => Joypad.Button.Right,
					_ => (Joypad.Button)(-1)
				};
				if ((int)button != -1)
				{
					_memory.Joypad1.SetButtonState(button, pressed);
				}
			}
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
					if (ImGui.MenuItem("Settings"))
					{
						_settingsOpen = true;
					}
					ImGui.EndMenu();
				}
				if (ImGui.BeginMenu("Debug"))
				{
					if (ImGui.MenuItem("PPU Viewer", "", _ppuDebugWindow.Visible))
					{
						_ppuDebugWindow.Visible = !_ppuDebugWindow.Visible;
					}
					ImGui.EndMenu();
				}

				if (_isPaused)
				{
					ImGui.TextColored(new Vector4(1, 1, 0, 1), " [PAUSED]");
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

			_nesWindow.Draw(_ppu, _textureId);
			_cpuLogWindow.Draw(_cpu, _ppu, _logBuffer, ref _isRunning, ref _logEnabled);
			_testStatusWindow.Draw(_cpu, _testActive, _testStatus, _testOutput);
			_settingsWindow.Draw(ref _settingsOpen, _settings);
			_ppuDebugWindow.Draw(_ppu, _pt0TextureId, _pt1TextureId, _ntTextureId, _spriteAtlasTextureId, _spritePreviewTextureId, _spriteLayerTextureId);
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

						// Reset timing to prevent "catch-up" speed up
						_lastTime = _stopwatch.Elapsed.TotalSeconds;
						_accumulator = 0;
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
