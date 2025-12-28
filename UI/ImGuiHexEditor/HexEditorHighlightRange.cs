using System.Numerics;

namespace OGNES.UI.ImGuiHexEditor;

public struct HexEditorHighlightRange
{
    public int From;
    public int To;
    public uint Color;
    public uint BorderColor;
    public HexEditorHighlightFlags Flags;
}
