using Hexa.NET.ImGui;
using Hexa.NET.GLFW;
using OGNES.Components;
using System.Collections.Generic;
using System.Numerics;

namespace OGNES.UI
{
    public class SettingsWindow
    {
        private bool _isMapping = false;
        private string _mappingButton = "";

        public void Draw(ref bool isOpen, AppSettings settings)
        {
            if (!isOpen) return;

            if (ImGui.Begin("Settings", ref isOpen))
            {
                if (ImGui.CollapsingHeader("Controls", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (ImGui.BeginTable("ControlsTable", 2))
                    {
                        foreach (var mapping in settings.KeyMappings)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text(mapping.Key);
                            
                            ImGui.TableNextColumn();
                            string btnLabel = _isMapping && _mappingButton == mapping.Key ? "Press a key..." : ((GlfwKey)mapping.Value).ToString();
                            if (ImGui.Button($"{btnLabel}##{mapping.Key}", new Vector2(-1, 0)))
                            {
                                _isMapping = true;
                                _mappingButton = mapping.Key;
                            }
                        }
                        ImGui.EndTable();
                    }
                }

                if (ImGui.CollapsingHeader("Audio", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    bool enabled = settings.AudioEnabled;
                    if (ImGui.Checkbox("Enable Audio", ref enabled))
                    {
                        settings.AudioEnabled = enabled;
                    }

                    float volume = settings.Volume;
                    if (ImGui.SliderFloat("Volume", ref volume, 0.0f, 1.0f, "%.2f"))
                    {
                        settings.Volume = volume;
                    }
                }
            }
            ImGui.End();
        }

        public unsafe void Update(AppSettings settings, GLFWwindowPtr window)
        {
            if (_isMapping)
            {
                // Poll all keys to see if any are pressed
                for (int i = (int)GlfwKey.Space; i <= (int)GlfwKey.Last; i++)
                {
                    if (GLFW.GetKey(window, i) == 1) // 1 is GLFW_PRESS
                    {
                        settings.KeyMappings[_mappingButton] = i;
                        _isMapping = false;
                        break;
                    }
                }
            }
        }
    }
}
