using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Widgets.Dialogs;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using OGNES.Components;
using OGNES.Input;
using OGNES.UI;
using OGNES.UI.General;
using OGNES.UI.ImGuiTexInspect;
using OGNES.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
		
		#region GPU Shader NTSC
		private Utils.Shader? _ntscShader;
		private uint _screenQuadVAO;
		private uint _screenQuadVBO;
		private uint _fbo;
		private uint _intermediateTexture;
		private bool _gpuResourcesInitialized = false;
		// If true, use GPU shader and disable CPU NTSC in Ppu.
		// If false, use CPU Ppu buffer directly (which might be CPU NTSC or Standard).
		// For this task, we assume we want to use GPU if initialized.
		private bool _useGpuNtsc = true; 
		#endregion

		private List<string> _logBuffer = new();
		private bool _logEnabled = false;
		private bool _isRunning = false;
		private bool _isPaused = false;
		private bool _lastEffectivelyRunning = false;
		private bool _stepFrame = false;
		private bool _pPressed = false;
		private bool _fPressed = false;
		private bool _plusPressed = false;
		private bool _minusPressed = false;
		private bool _f5Pressed = false;
		private bool _f8Pressed = false;
		private bool _rPressed = false;
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
		private bool _showLibraryWindow = false;
		private bool _showCpuLog = true;
		private AppSettings _settings = new();
		private const string SettingsFile = "settings.json";

		private uint _pt0TextureId;
		private uint _pt1TextureId;
		private uint _ntTextureId;
		private uint _spriteAtlasTextureId;
		private uint _spritePreviewTextureId;
		private uint _spriteLayerTextureId;

		// Upscale Resources
		private Utils.Shader? _upscaleShader;
		private uint _upscaledFbo;
		private uint _upscaledTexture;
		private int _currentUpscaleX = 1;
		private int _currentUpscaleY = 1;

		private System.Diagnostics.Stopwatch _stopwatch = new();
		private double _lastTime;
		private double _accumulator;
		private Library.LibraryManager _libraryManager = null!;
		private Library.LibraryWindow _libraryWindow = null!;
		private CheatManager _cheatManager = null!;
		private CheatWindow _cheatWindow = null!;
		private MemoryViewerWindow _memoryViewerWindow = null!;
		private JoypadMacroWindow _joypadMacroWindow = new();
		private InputManager _inputManager = null!;

		public static void Main(string[] args)
		{
			NesDatabase.Initialize();
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

				if (_cartridge != null && _cartridge.HasBattery && !string.IsNullOrEmpty(_romPath))
				{
					string savePath = Path.ChangeExtension(_romPath, ".sav");
					_cartridge.SaveBatteryRam(savePath);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
			}
		}

		private void InitEmulator(string romPath)
		{
			if (_cartridge != null && _cartridge.HasBattery && !string.IsNullOrEmpty(_romPath))
			{
				string savePath = Path.ChangeExtension(_romPath, ".sav");
				_cartridge.SaveBatteryRam(savePath);
			}

			var ppu = new Ppu();
			// Force CPU NTSC off if we plan to use GPU shader
			if (_useGpuNtsc)
			{
				ppu.EnableNtsc = false;
			}
			
			var apu = new Apu();

			var memory = new Memory { Ppu = ppu, Apu = apu };
			ppu.Joypad = memory.Joypad2;
			apu.Memory = memory;
			var cartridge = new Cartridge(romPath);
			if (cartridge.HasBattery)
			{
				string savePath = Path.ChangeExtension(romPath, ".sav");
				cartridge.LoadBatteryRam(savePath);
			}
			memory.Cartridge = cartridge;
			ppu.Cartridge = cartridge;
			var cpu = new Cpu(memory);
			memory.Cpu = cpu;
			
			cpu.Reset();
			ppu.Reset();

			ppu.CrtLw = _settings.CrtLw;
			ppu.CrtDb = _settings.CrtDb;

			// If using GPU NTSC, we want the PPU to output plain sRGB (Signal) values
			// so the shader can handle the CRT effects. We disable the CPU NTSC filter.
			// converting palettes to sRGB (GammaCorrection.Standard) ensures consistent signal input for the shader.
			if (_useGpuNtsc)
			{
				ppu.EnableNtsc = false;
				ppu.GammaMode = GammaCorrection.Standard;
			}
			else
			{
				ppu.EnableNtsc = true; // Fallback to CPU NTSC filter
				ppu.GammaMode = (GammaCorrection)_settings.PaletteGammaMode;
			}

			if (_settings.CurrentPalette != "Default")
			{
				ppu.LoadPalette(_settings.CurrentPalette);
			}


			// Initialize Audio
			if (_audioOutput != null)
			{
				_audioOutput.Dispose();
			}
			_audioOutput = new AudioOutput(AudioSampleRate);
			_audioOutput.Volume = _settings.AudioEnabled ? _settings.Volume : 0;
			_lastEffectivelyRunning = true;
			apu.SetSink(_audioOutput);

			// Only assign to fields if initialization succeeded
			_ppu = ppu;
			_apu = apu;
			_memory = memory;
			_cartridge = cartridge;
			_cpu = cpu;
			_romPath = romPath;
			
			_cheatManager.SetMemory(_memory);
			_memoryViewerWindow.SetMemory(_memory);
			_ppuDebugWindow.Reset();

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

		private void InitGpuResources()
		{
			if (_gpuResourcesInitialized) return;

			// Initialize NTSC Shader
			try 
			{
				string vertPath = Path.Combine("Shaders", "ntsc.vert");
				string fragPath = Path.Combine("Shaders", "ntsc.frag");
				if (File.Exists(vertPath) && File.Exists(fragPath))
				{
					_ntscShader = new Utils.Shader(_gl, vertPath, fragPath);
				}
				else
				{
					Console.WriteLine("Shader files not found. GPU NTSC disabled.");
					_useGpuNtsc = false;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load shaders: {ex.Message}");
				_useGpuNtsc = false;
			}

			// Screen Quad
			float[] quadVertices = {
				// positions   // texCoords
				-1.0f,  1.0f,  0.0f, 1.0f,
				-1.0f, -1.0f,  0.0f, 0.0f,
				 1.0f, -1.0f,  1.0f, 0.0f,

				-1.0f,  1.0f,  0.0f, 1.0f,
				 1.0f, -1.0f,  1.0f, 0.0f,
				 1.0f,  1.0f,  1.0f, 1.0f
			};

			fixed (uint* vao = &_screenQuadVAO)
			{
				_gl.GenVertexArrays(1, vao);
			}
			fixed (uint* vbo = &_screenQuadVBO)
			{
				_gl.GenBuffers(1, vbo);
			}
			_gl.BindVertexArray(_screenQuadVAO);
			_gl.BindBuffer(GLBufferTargetARB.ArrayBuffer, _screenQuadVBO);
			fixed (float* v = quadVertices)
			{
				_gl.BufferData(GLBufferTargetARB.ArrayBuffer, (nint)(quadVertices.Length * sizeof(float)), v, GLBufferUsageARB.StaticDraw);
			}

			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 2, GLVertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
			_gl.EnableVertexAttribArray(1);
			_gl.VertexAttribPointer(1, 2, GLVertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

			// Framebuffer logic
			// _textureId is already created in main Render loop or CreateTexture
			// We need an intermediate texture to hold the RAW NES output
			fixed (uint* tex = &_intermediateTexture)
			{
				_gl.GenTextures(1, tex);
			}
			_gl.BindTexture(GLTextureTarget.Texture2D, _intermediateTexture);
			_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba8, NesScreenWidth, NesScreenHeight, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, (void*)0);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);
			_gl.BindTexture(GLTextureTarget.Texture2D, 0);

			// FBO creation
			fixed (uint* fbo = &_fbo)
			{
				_gl.GenFramebuffers(1, fbo);
			}

			// Initialize Upscale Shader
			try
			{
				string vertPath = Path.Combine("Shaders", "passthrough.vert");
				string fragPath = Path.Combine("Shaders", "passthrough.frag");
				
				Console.WriteLine($"Loading upscale shader from {vertPath} and {fragPath}");
				
				if (File.Exists(vertPath) && File.Exists(fragPath))
				{
					_upscaleShader = new Utils.Shader(_gl, vertPath, fragPath);
					Console.WriteLine("Upscale shader loaded successfully.");
				}
				else
				{
					Console.WriteLine("Upscale shader files NOT FOUND.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load upscale shader: {ex.Message}");
			}
            
            // Upscale FBO
            fixed (uint* fbo = &_upscaledFbo)
            {
                _gl.GenFramebuffers(1, fbo);
            }

			
			// We defer binding texture to FBO until UpdateTexture because _textureId is created elsewhere potentially.
			
			_gpuResourcesInitialized = true;
		}

		private void UpdateTexture()
		{
			if (_ppu == null) return;
			
			if (!_gpuResourcesInitialized)
			{
				InitGpuResources();
			}

			// If using GPU NTSC, we render raw PPU output to _intermediateTexture, 
			// then draw quad to _fbo which is attached to _textureId.
			
			int width = _ppu.FrameWidth;
			int height = _ppu.FrameHeight;

			// 1. Upload raw PPU data
			uint targetTex = _useGpuNtsc ? _intermediateTexture : _textureId;
			
			_gl.BindTexture(GLTextureTarget.Texture2D, targetTex);
			
			// Ensure PBO is unbound so ptr is treated as client memory pointer
			// GL_PIXEL_UNPACK_BUFFER = 0x88EC
			_gl.BindBuffer((GLBufferTargetARB)0x88EC, 0); 
			
			// Reset unpack alignment to default or specific value if needed
			// Ensure strict packing for BGRA/RGBA upload
			_gl.PixelStorei(GLPixelStoreParameter.UnpackRowLength, 0);
			_gl.PixelStorei(GLPixelStoreParameter.UnpackImageHeight, 0);
			_gl.PixelStorei(GLPixelStoreParameter.UnpackAlignment, 1);
			_gl.PixelStorei(GLPixelStoreParameter.UnpackSkipPixels, 0);
			_gl.PixelStorei(GLPixelStoreParameter.UnpackSkipRows, 0);
			_gl.PixelStorei(GLPixelStoreParameter.UnpackSkipImages, 0);

			// Standard Nearest filtering for crisp pixels (even with NTSC we are now outputting 256 blended pixels)
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
			_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);
			
			fixed (byte* ptr = _ppu.FrameBuffer)
			{
				// Use Rgba8 for internal format to avoid implicit sRGB conversion if UI looks washed out
				// Use Rgba format for input, which matches the R,G,B,A order we are now using in Ppu.cs Standard Mode
				// (Since we are switching Standard Mode to R,G,B,A packing shortly)
				// For NTSC, we will also switch to R,G,B,A.
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba8, width, height, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
				
				// Check for errors
				var error = _gl.GetError();
				if (error != (GLEnum)GLErrorCode.NoError)
				{
				    Console.WriteLine($"GL Error in UpdateTexture: {error}");
				}
			}
			_gl.BindTexture(GLTextureTarget.Texture2D, 0);

			// 2. Perform GPU Shader Pass if enabled
			if (_useGpuNtsc && _ntscShader != null)
			{
				_ntscShader.Use();
				_ntscShader.SetInt("gammaMode", _settings.PaletteGammaMode);
				_ntscShader.SetFloat("crtLw", (float)_settings.CrtLw);
				_ntscShader.SetFloat("crtDb", (float)_settings.CrtDb);

				// Resize _textureId to match simple output or keep it standard size
				// For now, simple pass, same size.
				// Bind _textureId to FBO
				_gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, _fbo);
				_gl.FramebufferTexture2D(GLFramebufferTarget.Framebuffer, GLFramebufferAttachment.ColorAttachment0, GLTextureTarget.Texture2D, _textureId, 0);
				
				// Simple check
				var status = _gl.CheckFramebufferStatus(GLFramebufferTarget.Framebuffer);
				if (status != (GLEnum)GLFramebufferStatus.Complete)
				{
					// Should probably resize _textureId if it's not set yet or incompatible
					// Just force resize:
					_gl.BindTexture(GLTextureTarget.Texture2D, _textureId);
					_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba8, width, height, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, (void*)0);
					_gl.BindTexture(GLTextureTarget.Texture2D, 0);
					
					// Reattach
					_gl.FramebufferTexture2D(GLFramebufferTarget.Framebuffer, GLFramebufferAttachment.ColorAttachment0, GLTextureTarget.Texture2D, _textureId, 0);
				}

				_gl.Viewport(0, 0, width, height); // Render at NES resolution
				_gl.Clear(GLClearBufferMask.ColorBufferBit);

				_ntscShader.Use();
				_ntscShader.SetInt("screenTexture", 0);
				_ntscShader.SetVec2("resolution", (float)width, (float)height);
				
				_gl.ActiveTexture(GLTextureUnit.Texture0);
				_gl.BindTexture(GLTextureTarget.Texture2D, _intermediateTexture);
				
				_gl.BindVertexArray(_screenQuadVAO);
				_gl.DrawArrays(GLPrimitiveType.Triangles, 0, 6);
				
				_gl.BindVertexArray(0);
				_gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);

				// --- Upscale Pass ---
				if (_upscaleShader != null)
				{
					int scaleX = Math.Max(1, _settings.UpscaleFactorX);
					int scaleY = Math.Max(1, _settings.UpscaleFactorY);
					int targetWidth = width * scaleX;
					int targetHeight = height * scaleY;

					if (_upscaledTexture == 0 || _currentUpscaleX != scaleX || _currentUpscaleY != scaleY)
					{
						if (_upscaledTexture != 0) 
                        {
                            fixed (uint* t = &_upscaledTexture)
                            {
                                _gl.DeleteTextures(1, t);
                            }
                        }
						fixed (uint* tex = &_upscaledTexture) _gl.GenTextures(1, tex);
						_gl.BindTexture(GLTextureTarget.Texture2D, _upscaledTexture);
						_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba8, targetWidth, targetHeight, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, (void*)0);
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Linear);
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Linear);
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapS, (int)GLTextureWrapMode.ClampToEdge);
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.WrapT, (int)GLTextureWrapMode.ClampToEdge);
						
						_currentUpscaleX = scaleX;
						_currentUpscaleY = scaleY;
					}

					_gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, _upscaledFbo);
					_gl.FramebufferTexture2D(GLFramebufferTarget.Framebuffer, GLFramebufferAttachment.ColorAttachment0, GLTextureTarget.Texture2D, _upscaledTexture, 0);
					
					var statusUpscale = _gl.CheckFramebufferStatus(GLFramebufferTarget.Framebuffer);
					if (statusUpscale == (GLEnum)GLFramebufferStatus.Complete)
					{
						_gl.Viewport(0, 0, targetWidth, targetHeight);
						_gl.Clear(GLClearBufferMask.ColorBufferBit);
						
						_upscaleShader.Use();
						_upscaleShader.SetInt("screenTexture", 0);
						
						_gl.ActiveTexture(GLTextureUnit.Texture0);
						// Ensure we bind the ID from the NTSC pass, not the fallback one if it failed?
						// Note: _textureId is used for NTSC pass output in previous block.
						_gl.BindTexture(GLTextureTarget.Texture2D, _textureId); 
						
						// Ensure NTSC result is NEAREST for upscaling
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
						_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);

						_gl.BindVertexArray(_screenQuadVAO);
						_gl.DrawArrays(GLPrimitiveType.Triangles, 0, 6);
						
						_gl.BindVertexArray(0);
					}
					else 
					{
						// Console.WriteLine($"Upscale FBO Incomplete: {statusUpscale}");
					}
					_gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);
				}

				// Restore Viewport? The main loop resets viewport before rendering ImGui.
			}
		}

		private void UpdateDebugTextures()
		{
			if (_ppu == null || !_ppuDebugWindow.Visible) return;

			_ppuDebugWindow.UpdateBuffers(_ppu);

			// Update PT0
			_gl.BindTexture(GLTextureTarget.Texture2D, _pt0TextureId);
			fixed (byte* ptr = _ppuDebugWindow.PatternTable0Buffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update PT1
			_gl.BindTexture(GLTextureTarget.Texture2D, _pt1TextureId);
			fixed (byte* ptr = _ppuDebugWindow.PatternTable1Buffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update NT
			_gl.BindTexture(GLTextureTarget.Texture2D, _ntTextureId);
			fixed (byte* ptr = _ppuDebugWindow.NameTableBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 512, 480, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Atlas
			_gl.BindTexture(GLTextureTarget.Texture2D, _spriteAtlasTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpriteAtlasBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 128, 128, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Preview
			_gl.BindTexture(GLTextureTarget.Texture2D, _spritePreviewTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpritePreviewBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 64, 64, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			// Update Sprite Layer
			_gl.BindTexture(GLTextureTarget.Texture2D, _spriteLayerTextureId);
			fixed (byte* ptr = _ppuDebugWindow.SpriteLayerBuffer)
			{
				_gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Srgb8Alpha8, 256, 240, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, ptr);
			}

			_gl.BindTexture(GLTextureTarget.Texture2D, 0);
		}

		private void RunGui()
		{
			// Force disable sRGB for UI rendering to prevent double-gamma correction
			// This fixes the "washed out" grey look of the UI
			// if (_gl != null) _gl.Disable(GLEnableCap.FramebufferSrgb);

			LoadSettings();

			_showCpuLog = _settings.ShowCpuLog;
			_showLibraryWindow = _settings.ShowLibrary;
			_ppuDebugWindow.Visible = _settings.ShowPpuDebug;

			if (GLFW.Init() == 0) return;

			GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MAJOR, 3);
			GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MINOR, 3);
			GLFW.WindowHint(GLFW.GLFW_OPENGL_PROFILE, GLFW.GLFW_OPENGL_CORE_PROFILE);
			GLFW.WindowHint(GLFW.GLFW_SRGB_CAPABLE, GLFW.GLFW_TRUE);

			_window = GLFW.CreateWindow(1280, 720, "OGNES Emulator", null, null);
			if (_window.IsNull)
			{
				GLFW.Terminate();
				return;
			}

			GLFW.MakeContextCurrent(_window);
			_inputManager = new InputManager(_window, _settings);
			GLFW.SwapInterval(1);

			_gl = new GL(new GLFWContext(_window));
			//_gl.Enable((GLEnableCap)GLEnum.FramebufferSrgb);
			// Disable hardware sRGB conversion since our shader manually outputs sRGB.
			// Enabling this effectively applies gamma correction twice (once in shader, once in hardware).
			_gl.Disable((GLEnableCap)GLEnum.FramebufferSrgb);

			_guiContext = ImGui.CreateContext();
			ImGui.SetCurrentContext(_guiContext);
			var io = ImGui.GetIO();
			// io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
			io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
			io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
			io.ConfigDpiScaleFonts = true;
			io.ConfigDpiScaleViewports = true;
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

			_ppuDebugWindow.OnSettingsChanged = SaveSettings;

			_fileOpenDialog = new FileOpenDialog();
			if (!string.IsNullOrEmpty(_settings.LastRomDirectory))
			{
				_fileOpenDialog.CurrentFolder = _settings.LastRomDirectory;
			}

			_stopwatch.Start();
			_lastTime = 0;
			_accumulator = 0;

			_libraryManager = new Library.LibraryManager(_settings);
			_libraryWindow = new Library.LibraryWindow(_libraryManager, _gl);

			_cheatManager = new CheatManager(null!);
			_memoryViewerWindow = new MemoryViewerWindow(null!);
			_cheatWindow = new CheatWindow(_cheatManager, _memoryViewerWindow, _settings);
			_cheatWindow.OnSettingsChanged = SaveSettings;
			_cheatWindow.Visible = _settings.ShowCheats;
			_memoryViewerWindow.Visible = _settings.ShowMemoryViewer;

			while (GLFW.WindowShouldClose(_window) == 0)
			{
				double currentTime = _stopwatch.Elapsed.TotalSeconds;
				double deltaTime = currentTime - _lastTime;
				_lastTime = currentTime;

				// Cap deltaTime to avoid spiral of death if we fall behind
				if (deltaTime > 0.1) deltaTime = 0.1;

				if (_isRunning && !_isPaused)
				{
					_accumulator += deltaTime;
				}
				else
				{
					_accumulator = 0;
				}

				double targetFrameTime = 1.0 / _settings.TargetFps;

				GLFW.PollEvents();

				int w, h;
				GLFW.GetFramebufferSize(_window, &w, &h);
				if (w == 0 || h == 0)
				{
					GLFW.WaitEvents();
					continue;
				}

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

				bool effectivelyRunning = _isRunning && !_isPaused && _cpu != null;
				if (effectivelyRunning != _lastEffectivelyRunning)
				{
					if (effectivelyRunning) _audioOutput?.Resume();
					else _audioOutput?.Pause();
					_lastEffectivelyRunning = effectivelyRunning;
				}

				_settingsWindow.Update(_settings, _window);
				_cheatWindow.Draw();
				_memoryViewerWindow.Draw();

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

			if (_cartridge != null && _cartridge.HasBattery && !string.IsNullOrEmpty(_romPath))
			{
				string savePath = Path.ChangeExtension(_romPath, ".sav");
				_cartridge.SaveBatteryRam(savePath);
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

			_cheatManager.Update();
			_inputManager.AdvanceMacro();
			_inputManager.Update(_memory);

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

			// Volume controls
			bool ctrlDown = GLFW.GetKey(_window, (int)GlfwKey.LeftControl) == 1 || GLFW.GetKey(_window, (int)GlfwKey.RightControl) == 1;
			
			bool rDown = GLFW.GetKey(_window, (int)GlfwKey.R) == 1;
			if (ctrlDown && rDown && !_rPressed)
			{
				RestartEmulation();
			}
			_rPressed = rDown;

			bool plusDown = GLFW.GetKey(_window, (int)GlfwKey.Equal) == 1 || GLFW.GetKey(_window, (int)GlfwKey.KpAdd) == 1;
			bool minusDown = GLFW.GetKey(_window, (int)GlfwKey.Minus) == 1 || GLFW.GetKey(_window, (int)GlfwKey.KpSubtract) == 1;

			if (ctrlDown && plusDown && !_plusPressed)
			{
				_settings.Volume = Math.Min(1.0f, _settings.Volume + 0.1f);
				if (_audioOutput != null && _settings.AudioEnabled)
				{
					_audioOutput.Volume = _settings.Volume;
				}
				SaveSettings();
			}
			_plusPressed = plusDown;

			if (ctrlDown && minusDown && !_minusPressed)
			{
				_settings.Volume = Math.Max(0.0f, _settings.Volume - 0.1f);
				if (_audioOutput != null && _settings.AudioEnabled)
				{
					_audioOutput.Volume = _settings.Volume;
				}
				SaveSettings();
			}
			_minusPressed = minusDown;

			// State controls
			bool f5Down = GLFW.GetKey(_window, (int)GlfwKey.F5) == 1;
			if (f5Down && !_f5Pressed)
			{
				SaveState();
			}
			_f5Pressed = f5Down;

			bool f8Down = GLFW.GetKey(_window, (int)GlfwKey.F8) == 1;
			if (f8Down && !_f8Pressed)
			{
				LoadState();
			}
			_f8Pressed = f8Down;

			// Slot selection (0-9)
			for (int i = 0; i <= 9; i++)
			{
				bool numDown = GLFW.GetKey(_window, (int)GlfwKey.Key0 + i) == 1 || GLFW.GetKey(_window, (int)GlfwKey.Kp0 + i) == 1;
				if (numDown)
				{
					if (_settings.CurrentSaveSlot != i)
					{
						_settings.CurrentSaveSlot = i;
						SaveSettings();
					}
				}
			}

			_inputManager.Update(_memory);
		}

		private void RenderUI()
		{
			bool initialShowCpuLog = _showCpuLog;
			bool initialShowLibrary = _showLibraryWindow;
			bool initialShowPpuDebug = _ppuDebugWindow.Visible;

			if (ImGui.BeginMainMenuBar())
			{
				if (ImGui.BeginMenu("File"))
				{
					if (ImGui.MenuItem("Load ROM"))
					{
						_fileOpenDialog.Show(LoadRomCallback);
					}
					if (ImGui.MenuItem("Restart", "Ctrl+R"))
					{
						RestartEmulation();
					}
					if (ImGui.MenuItem("Library"))
					{
						_showLibraryWindow = true;
					}
					if (ImGui.MenuItem("Settings"))
					{
						_settingsOpen = true;
					}
					ImGui.EndMenu();
				}
				if (ImGui.BeginMenu("State"))
				{
					if (ImGui.MenuItem("Save State", "F5"))
					{
						SaveState();
					}
					if (ImGui.MenuItem("Load State", "F8"))
					{
						LoadState();
					}

					ImGui.Separator();
					if (ImGui.BeginMenu("Select Slot"))
					{
						for (int i = 0; i <= 9; i++)
						{
							bool selected = _settings.CurrentSaveSlot == i;
							if (ImGui.MenuItem($"Slot {i}", i.ToString(), selected))
							{
								_settings.CurrentSaveSlot = i;
								SaveSettings();
							}
						}
						ImGui.EndMenu();
					}
					ImGui.TextDisabled($"Current Slot: {_settings.CurrentSaveSlot}");

					ImGui.EndMenu();
				}
				if (ImGui.BeginMenu("Debug"))
				{
					if (ImGui.MenuItem("PPU Viewer", "", _ppuDebugWindow.Visible))
					{
						_ppuDebugWindow.Visible = !_ppuDebugWindow.Visible;
						if (_ppuDebugWindow.Visible) UpdateDebugTextures();
					}
					if (ImGui.MenuItem("CPU Log", "", _showCpuLog))
					{
						_showCpuLog = !_showCpuLog;
					}
					if (ImGui.MenuItem("Cheats", "", _cheatWindow.Visible))
					{
						_cheatWindow.Visible = !_cheatWindow.Visible;
					}
					if (ImGui.MenuItem("Memory Viewer", "", _memoryViewerWindow.Visible))
					{
						_memoryViewerWindow.Visible = !_memoryViewerWindow.Visible;
					}
					if (ImGui.MenuItem("Joypad Macro", "", _joypadMacroWindow.Visible))
					{
						_joypadMacroWindow.Visible = !_joypadMacroWindow.Visible;
					}
					ImGui.EndMenu();
				}

				if (ImGui.BeginMenu("Audio"))
				{
					bool enabled = _settings.AudioEnabled;
					if (ImGui.MenuItem("Enable Audio", "", ref enabled))
					{
						_settings.AudioEnabled = enabled;
						if (_audioOutput != null)
						{
							_audioOutput.Volume = _settings.AudioEnabled ? _settings.Volume : 0;
						}
						SaveSettings();
					}

					ImGui.Separator();

					if (ImGui.MenuItem("Volume Up", "Ctrl++"))
					{
						_settings.Volume = Math.Min(1.0f, _settings.Volume + 0.1f);
						if (_audioOutput != null && _settings.AudioEnabled)
						{
							_audioOutput.Volume = _settings.Volume;
						}
						SaveSettings();
					}

					if (ImGui.MenuItem("Volume Down", "Ctrl+-"))
					{
						_settings.Volume = Math.Max(0.0f, _settings.Volume - 0.1f);
						if (_audioOutput != null && _settings.AudioEnabled)
						{
							_audioOutput.Volume = _settings.Volume;
						}
						SaveSettings();
					}

					ImGui.TextDisabled($"Current Volume: {(int)(_settings.Volume * 100)}%");
					ImGui.EndMenu();
				}

				if (ImGui.BeginMenu("Video"))
				{
					if (_ppu != null)
					{
						bool ntsc = _ppu.EnableNtsc;
						if (ImGui.Checkbox("Enable NTSC Filter", ref ntsc))
						{
							_ppu.EnableNtsc = ntsc;
							_ppu.RegenerateFrameBuffer();
							UpdateTexture();
						}
						ImGui.Separator();
					}

					if (ImGui.BeginMenu("Palette"))
					{
						// Gamma options
						int gamma = _settings.PaletteGammaMode;
						ImGui.TextDisabled("Gamma Correction");
						if (ImGui.RadioButton("None (sRGB Input)", ref gamma, 0) ||
							ImGui.RadioButton("Standard (Linear Input)", ref gamma, 1) ||
							ImGui.RadioButton("Gamma 2.2 (Signal Input)", ref gamma, 2) ||
							ImGui.RadioButton("Measured CRT (Gamma 2.5)", ref gamma, 3) ||
							ImGui.RadioButton("SMPTE 240M", ref gamma, 4) ||
							ImGui.RadioButton("Proper CRT (Tunable)", ref gamma, 5))
						{
							_settings.PaletteGammaMode = gamma;
							if (_ppu != null)
							{
								// If using GPU NTSC, we avoid setting PPU Gamma because the shader will do it. 
								// But we still set it in settings.
								// Strategy: PPU Gamma = None (Signal) if GPU used.
								// PPU Gamma = SelectedMode if GPU not used.
								
                                // Update: For now, we update PPU logic just to be safe, but ideally we toggle this logic.
								if (_useGpuNtsc)
								{
									// Keep PPU in None mode so it passes through raw Linear values (from fpal) to the shader.
                                    // The shader will handles the Gamma correction.
									_ppu.GammaMode = GammaCorrection.None;
								}
								else
								{
									_ppu.GammaMode = (GammaCorrection)gamma;
								}

								// Reload current palette to apply change
								if (_settings.CurrentPalette != "Default")
								{
									_ppu.LoadPalette(_settings.CurrentPalette);
								}
								else
								{
									// Default palette is hardcoded sRGB, so maybe we don't apply gamma unless forced?
									// Currently default is used as-is.
								}
							}
						}

						if (gamma == 5)
						{
							ImGui.Indent();
							float lw = (float)_settings.CrtLw;
							float db = (float)_settings.CrtDb;
							bool changed = false;
							if (ImGui.SliderFloat("White Level", ref lw, 0.1f, 10.0f)) { _settings.CrtLw = lw; changed = true; }
							if (ImGui.SliderFloat("Black Lift", ref db, 0.0f, 1.0f)) { _settings.CrtDb = db; changed = true; }
							
							if (changed && _ppu != null)
							{
								_ppu.CrtLw = _settings.CrtLw;
								_ppu.CrtDb = _settings.CrtDb;
								if (_settings.CurrentPalette != "Default") { _ppu.LoadPalette(_settings.CurrentPalette); }
							}
							ImGui.Unindent();
						}
						ImGui.Separator();

						if (ImGui.MenuItem("Default", "", _settings.CurrentPalette == "Default"))
						{
							_settings.CurrentPalette = "Default";
							if (_ppu != null)
							{
								_ppu.ResetPalette();
								_ppu.RegenerateFrameBuffer();
								UpdateTexture();
								UpdateDebugTextures();
							}
							SaveSettings();
						}

						ImGui.Separator();
						ImGui.TextDisabled("Presets");
						string[] presets = { "2C04-0001", "2C04-0002", "2C04-0003", "2C04-0004" };
						foreach (var preset in presets)
						{
							if (ImGui.MenuItem(preset, "", _settings.CurrentPalette == preset))
							{
								_settings.CurrentPalette = preset;
								if (_ppu != null)
								{
									_ppu.LoadPalette(preset);
									_ppu.RegenerateFrameBuffer();
									UpdateTexture();
									UpdateDebugTextures();
								}
								SaveSettings();
							}
						}

						ImGui.Separator();
						ImGui.TextDisabled("Custom Files");
						if (Directory.Exists("PalFiles"))
						{
							foreach (var file in Directory.GetFiles("PalFiles", "*pal"))
							{
								string fileName = Path.GetFileName(file);
								bool isSelected = _settings.CurrentPalette == file;
								if (ImGui.MenuItem(fileName, "", isSelected))
								{
									_settings.CurrentPalette = file;
									if (_ppu != null)
									{
										_ppu.LoadPalette(file);
										_ppu.RegenerateFrameBuffer();
										UpdateTexture();
										UpdateDebugTextures();
									}
									SaveSettings();
								}
							}
						}
						ImGui.EndMenu();
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

			_nesWindow.Draw(_ppu, _upscaledTexture != 0 ? _upscaledTexture : _textureId);
			_cpuLogWindow.Draw(_cpu, _ppu, _logBuffer, ref _isRunning, ref _isPaused, ref _logEnabled, ref _showCpuLog);
			_testStatusWindow.Draw(_cpu, _testActive, _testStatus, _testOutput);
			
			float oldVolume = _settings.Volume;
			bool oldAudioEnabled = _settings.AudioEnabled;
			
			_settingsWindow.Draw(ref _settingsOpen, _settings);

			if (oldVolume != _settings.Volume || oldAudioEnabled != _settings.AudioEnabled)
			{
				if (_audioOutput != null)
				{
					_audioOutput.Volume = _settings.AudioEnabled ? _settings.Volume : 0;
				}
				SaveSettings();
			}

			_ppuDebugWindow.Draw(_ppu, _settings, _pt0TextureId, _pt1TextureId, _ntTextureId, _spriteAtlasTextureId, _spritePreviewTextureId, _spriteLayerTextureId);
			_libraryWindow.Render(ref _showLibraryWindow, LoadRom);
			_joypadMacroWindow.Draw(_inputManager);

			if (initialShowCpuLog != _showCpuLog)
			{
				_settings.ShowCpuLog = _showCpuLog;
				SaveSettings();
			}
			if (initialShowLibrary != _showLibraryWindow)
			{
				_settings.ShowLibrary = _showLibraryWindow;
				SaveSettings();
			}
			if (initialShowPpuDebug != _ppuDebugWindow.Visible)
			{
				_settings.ShowPpuDebug = _ppuDebugWindow.Visible;
				SaveSettings();
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
			if (result == DialogResult.Ok && _fileOpenDialog.SelectedFile != null)
			{
				LoadRom(_fileOpenDialog.SelectedFile);
			}
		}

		private void RestartEmulation()
		{
			if (!string.IsNullOrEmpty(_romPath) && File.Exists(_romPath))
			{
				LoadRom(_romPath);
			}
		}

		private void LoadRom(string path)
		{
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
					_isPaused = false;
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

		private void SaveState()
		{
			if (_cartridge == null) return;
			string statePath = _romPath + ".state" + _settings.CurrentSaveSlot;
			try
			{
				using var fs = new FileStream(statePath, FileMode.Create, FileAccess.Write);
				using var writer = new BinaryWriter(fs);
				_memory.SaveState(writer);
				_cpu.SaveState(writer);
				Console.WriteLine($"State saved to {statePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to save state: {ex.Message}");
			}
		}

		private void LoadState()
		{
			if (_cartridge == null) return;
			string statePath = _romPath + ".state" + _settings.CurrentSaveSlot;
			if (!File.Exists(statePath)) return;

			try
			{
				using var fs = new FileStream(statePath, FileMode.Open, FileAccess.Read);
				using var reader = new BinaryReader(fs);
				_memory.LoadState(reader);
				_cpu.LoadState(reader);
				Console.WriteLine($"State loaded from {statePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load state: {ex.Message}");
			}
		}
	}


}
