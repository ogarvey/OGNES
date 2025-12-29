using Hexa.NET.ImGui;
using OGNES.Components;

namespace OGNES.UI
{
    public class TestStatusWindow
    {
        public void Draw(Cpu? cpu, bool testActive, byte testStatus, string testOutput)
        {
            if (testActive)
            {
                if (ImGui.Begin("Test Status"))
                {
                    string statusText = testStatus switch
                    {
                        0x80 => "Running...",
                        0x81 => "Reset Required",
                        var s when s < 0x80 => $"Completed (Result: 0x{s:X2})",
                        _ => $"Unknown (0x{testStatus:X2})"
                    };
                    ImGui.Text($"Status: {statusText}");
                    if (testStatus == 0x81)
                    {
                        if (ImGui.Button("Reset CPU"))
                        {
                            cpu?.Reset();
                        }
                    }
                    ImGui.Separator();
                    ImGui.TextWrapped(testOutput);
                }
                ImGui.End();
            }
        }
    }
}
