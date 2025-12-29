using Hexa.NET.ImGui;
using OGNES.Components;
using System.Collections.Generic;

namespace OGNES.UI
{
    public class CpuLogWindow
    {
        public void Draw(Cpu? cpu, Ppu? ppu, List<string> logBuffer, ref bool isRunning)
        {
            if (ImGui.Begin("CPU Log"))
            {
                if (ImGui.Button(isRunning ? "Pause" : "Resume"))
                {
                    isRunning = !isRunning;
                }
                ImGui.SameLine();
                if (ImGui.Button("Step"))
                {
                    if (cpu != null)
                    {
                        logBuffer.Add(cpu.GetStateLog());
                        if (logBuffer.Count > 1000) logBuffer.RemoveAt(0);
                        cpu.Step();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Step Frame"))
                {
                    if (cpu != null && ppu != null)
                    {
                        ppu.FrameReady = false;
                        while (!ppu.FrameReady)
                        {
                            cpu.Step();
                        }
                    }
                }

                ImGui.BeginChild("LogScroll");
                foreach (var line in logBuffer)
                {
                    ImGui.Text(line);
                }
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
                ImGui.EndChild();
            }
            ImGui.End();
        }
    }
}
