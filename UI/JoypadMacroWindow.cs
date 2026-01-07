using Hexa.NET.ImGui;
using OGNES.Components;
using OGNES.Input;
using System.Numerics;

namespace OGNES.UI
{
    public class JoypadMacroWindow
    {
        private string _macroText = string.Empty;
        private string _macroError = string.Empty;
        public bool Visible = false;

        public void Draw(InputManager? inputManager)
        {
            if (!Visible) return;

            if (ImGui.Begin("Joypad Macro", ref Visible))
            {
                if (inputManager == null)
                {
                    ImGui.Text("No InputManager instance.");
                    ImGui.End();
                    return;
                }

                ImGui.TextWrapped("Format: [Buttons] [HoldFrames] [GapFrames]");
                ImGui.TextWrapped("Buttons: A, B, S (Select), T (Start), U, D, L, R (joined by +)");
                ImGui.TextWrapped("Example: A+B 10 5");

                ImGui.InputTextMultiline("##macro", ref _macroText, 1024, new Vector2(-1, 200));

                if (ImGui.Button("Run"))
                {
                    if (inputManager.MacroExecutor.ParseAndEnqueue(_macroText, out _macroError))
                    {
                        _macroError = string.Empty;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear Queue"))
                {
                    inputManager.MacroExecutor.Clear();
                    _macroError = string.Empty;
                }

                if (!string.IsNullOrEmpty(_macroError))
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), _macroError);
                }

                ImGui.Separator();
                ImGui.Text($"Status: {(inputManager.MacroExecutor.IsRunning ? "Running" : "Idle")}");
                ImGui.Text($"Queue: {inputManager.MacroExecutor.QueueCount} commands");
            }
            ImGui.End();
        }
    }
}
