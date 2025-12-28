namespace OGNES.UI.ImGuiHexEditor;

[Flags]
public enum HexEditorHighlightFlags
{
    None = 0,
    Apply = 1 << 0,
    TextAutomaticContrast = 1 << 1,
    FullSized = 1 << 2, // Highlight entire byte space including its container, has no effect on ascii
    Ascii = 1 << 3, // Highlight ascii (doesn't affect single byte highlighting)
    Border = 1 << 4,
    OverrideBorderColor = 1 << 5,
    BorderAutomaticContrast = 1 << 6,
}
