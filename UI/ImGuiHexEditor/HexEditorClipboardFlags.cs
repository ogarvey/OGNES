namespace OGNES.UI.ImGuiHexEditor;

[Flags]
public enum HexEditorClipboardFlags
{
    None = 0,
    Multiline = 1 << 0, // Separate resulting hex editor lines with carriage return
}
