using Hexa.NET.GLFW;
using System.Collections.Generic;

namespace OGNES
{
	public class AppSettings
	{
		public string? LastRomDirectory { get; set; }
		public string? LastExportDirectory { get; set; }
		public int TargetFps { get; set; } = 60;
		public float Volume { get; set; } = 1.0f;
		public bool AudioEnabled { get; set; } = true;
		public string CurrentPalette { get; set; } = "Default";
		public int PaletteGammaMode { get; set; } = 0; // 0=None/Standard, 1=CRT (2.5), 2=Gamma 2.2, 3=SMPTE 240M
		public int CurrentSaveSlot { get; set; } = 0;
		public string? GameFolderPath { get; set; }
		public string? IgdbClientId { get; set; }
		public string? IgdbClientSecret { get; set; }
		public bool ShowCpuLog { get; set; } = true;
		public bool ShowPpuDebug { get; set; } = false;
		public bool ShowLibrary { get; set; } = false;
		public bool ShowCheats { get; set; } = false;
		public bool ShowMemoryViewer { get; set; } = false;
		public string? LastCheatDirectory { get; set; }

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
}
