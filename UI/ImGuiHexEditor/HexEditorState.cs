using System.Numerics;

namespace OGNES.UI.ImGuiHexEditor;

public delegate int HexEditorReadCallback(HexEditorState state, int offset, byte[] buffer, int size);
public delegate int HexEditorWriteCallback(HexEditorState state, int offset, byte[] buffer, int size);
public delegate bool HexEditorGetAddressNameCallback(HexEditorState state, int offset, out string addressName);
public delegate HexEditorHighlightFlags HexEditorSingleHighlightCallback(HexEditorState state, int offset, out uint color, out uint textColor, out uint borderColor);
public delegate void HexEditorHighlightRangesCallback(HexEditorState state, int displayStart, int displayEnd);

public class HexEditorState
{
    public byte[]? Bytes;
    public int MaxBytes;
    public int BytesPerLine = -1;
    public bool ShowPrintable = false;
    public bool LowercaseBytes = false;
    public bool RenderZeroesDisabled = true;
    public bool ShowAddress = true;
    public int AddressChars = -1;
    public bool ShowAscii = true;
    public bool ReadOnly = false;
    public int Separators = 8;
    public object? UserData = null;
    public List<HexEditorHighlightRange> HighlightRanges = new();
    public bool EnableClipboard = true;
    public HexEditorClipboardFlags ClipboardFlags = HexEditorClipboardFlags.Multiline;

    public HexEditorReadCallback? ReadCallback = null;
    public HexEditorWriteCallback? WriteCallback = null;
    public HexEditorGetAddressNameCallback? GetAddressNameCallback = null;
    public HexEditorSingleHighlightCallback? SingleHighlightCallback = null;
    public HexEditorHighlightRangesCallback? HighlightRangesCallback = null;

    public int SelectStartByte = -1;
    public int SelectStartSubByte = 0;
    public int SelectEndByte = -1;
    public int SelectEndSubByte = 0;
    public int LastSelectedByte = -1;
    public int SelectDragByte = -1;
    public int SelectDragSubByte = 0;
    public float SelectCursorAnimationTime = 0f;

	// One-shot request: scroll the view so this byte offset is visible.
	// Set to -1 to disable. The hex editor will reset it to -1 after applying.
	public int RequestScrollToByte = -1;

    public HexEditorHighlightFlags SelectionHighlightFlags = HexEditorHighlightFlags.FullSized | HexEditorHighlightFlags.Ascii;
}
