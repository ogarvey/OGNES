using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;

namespace OGNES.UI.ImGuiHexEditor;

public static class HexEditor
{
    private static char HalfByteToPrintable(byte halfByte, bool lower)
    {
        return halfByte <= 9 ? (char)('0' + halfByte) : (char)((lower ? 'a' : 'A') + halfByte - 10);
    }

    private static byte KeyToHalfByte(ImGuiKey key)
    {
        if (key >= ImGuiKey.A && key <= ImGuiKey.F)
            return (byte)(key - ImGuiKey.A + 10);
        return (byte)(key - ImGuiKey.Key0);
    }

    private static bool HasAsciiRepresentation(byte b)
    {
        return b >= '!' && b <= '~';
    }

    private static int CalcBytesPerLine(float bytesAvailX, Vector2 byteSize, Vector2 spacing, bool showAscii, Vector2 charSize, int separators)
    {
        float byteWidth = byteSize.X + spacing.X + (showAscii ? charSize.X : 0f);
        int bytesPerLine = (int)(bytesAvailX / byteWidth);
        bytesPerLine = bytesPerLine <= 0 ? 1 : bytesPerLine;

        int actualSeparators = separators > 0 ? bytesPerLine / separators : 0;
        if (actualSeparators != 0 && separators > 0 && bytesPerLine > actualSeparators && (bytesPerLine - 1) % actualSeparators == 0)
            --actualSeparators;

        return separators > 0 ? CalcBytesPerLine(bytesAvailX - (actualSeparators * spacing.X), byteSize, spacing, showAscii, charSize, 0) : bytesPerLine;
    }

    private static uint CalcContrastColor(uint color)
    {
        byte r = (byte)(color & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)((color >> 16) & 0xFF);
        byte a = (byte)((color >> 24) & 0xFF);

        float l = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
        byte c = (byte)(l > 0.5f ? 0 : 255);
        return (uint)(c | (c << 8) | (c << 16) | (a << 24));
    }

    private static bool RangeRangeIntersection(int aMin, int aMax, int bMin, int bMax, out int outMin, out int outMax)
    {
        outMin = 0;
        outMax = 0;

        if (aMax < bMin || bMax < aMin)
            return false;

        outMin = Math.Max(aMin, bMin);
        outMax = Math.Min(aMax, bMax);

        return outMin <= outMax;
    }

    private static void RenderRectCornerCalcRounding(Vector2 ra, Vector2 rb, ref float rounding)
    {
        rounding = Math.Min(rounding, Math.Abs(rb.X - ra.X) * 0.5f);
        rounding = Math.Min(rounding, Math.Abs(rb.Y - ra.Y) * 0.5f);
    }

    private static void RenderTopLeftCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X + 0.5f, a.Y + 0.5f);
        Vector2 rb = new(b.X, b.Y);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(new Vector2(ra.X, rb.Y), 0, 3, 6);
        drawList.PathArcToFast(new Vector2(ra.X + rounding, ra.Y + rounding), rounding, 6, 9);
        drawList.PathArcToFast(new Vector2(rb.X, ra.Y), 0, 9, 12);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderBottomRightCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X, a.Y + 0.5f);
        Vector2 rb = new(b.X - 0.5f, b.Y + 0.5f);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(new Vector2(rb.X, ra.Y), 0, 9, 12);
        drawList.PathArcToFast(new Vector2(rb.X - rounding, rb.Y - rounding), rounding, 0, 3);
        drawList.PathArcToFast(new Vector2(ra.X, rb.Y), 0, 3, 6);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderTopRightCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X + 0.5f, a.Y + 0.5f);
        Vector2 rb = new(b.X - 0.5f, b.Y);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(ra, 0f, 6, 9);
        drawList.PathArcToFast(new Vector2(rb.X - rounding, ra.Y + rounding), rounding, 9, 12);
        drawList.PathArcToFast(rb, 0f, 0, 3);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderBottomLeftCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X + 0.5f, a.Y + 0.5f);
        Vector2 rb = new(b.X + 0.5f, b.Y + 0.5f);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(new Vector2(rb.X, rb.Y), 0f, 0, 3);
        drawList.PathArcToFast(new Vector2(ra.X + rounding, rb.Y - rounding), rounding, 3, 6);
        drawList.PathArcToFast(new Vector2(ra.X, ra.Y), 0f, 9, 12);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderBottomCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X + 0.5f, a.Y + 0.5f);
        Vector2 rb = new(b.X + 0.5f, b.Y + 0.5f);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(new Vector2(rb.X, ra.Y), 0f, 0, 3);
        drawList.PathArcToFast(new Vector2(rb.X - rounding, rb.Y - rounding), rounding, 0, 3);
        drawList.PathArcToFast(new Vector2(ra.X + rounding, rb.Y - rounding), rounding, 3, 6);
        drawList.PathArcToFast(new Vector2(ra.X, ra.Y), 0f, 9, 12);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderTopCornerRect(ImDrawListPtr drawList, Vector2 a, Vector2 b, uint color, float rounding)
    {
        Vector2 ra = new(a.X + 0.5f, a.Y + 0.5f);
        Vector2 rb = new(b.X - 0.5f, b.Y + 0.5f);

        RenderRectCornerCalcRounding(ra, rb, ref rounding);

        drawList.PathArcToFast(new Vector2(ra.X, rb.Y), 0f, 3, 6);
        drawList.PathArcToFast(new Vector2(ra.X + rounding, ra.Y + rounding), rounding, 6, 9);
        drawList.PathArcToFast(new Vector2(rb.X - rounding, ra.Y + rounding), rounding, 9, 12);
        drawList.PathArcToFast(new Vector2(rb.X, rb.Y), 0f, 0, 3);
        drawList.PathStroke(color, ImDrawFlags.None, 1f);
    }

    private static void RenderByteDecorations(ImDrawListPtr drawList, Vector2 bbMin, Vector2 bbMax, uint bgColor,
        HexEditorHighlightFlags flags, uint borderColor, float rounding,
        int offset, int rangeMin, int rangeMax, int bytesPerLine, int i, int lineBase)
    {
        bool hasBorder = (flags & HexEditorHighlightFlags.Border) != 0;

        if (!hasBorder)
        {
            drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
            return;
        }

        if (rangeMin == rangeMax)
        {
            drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.None);
            drawList.AddRect(bbMin, bbMax, borderColor, rounding, ImDrawFlags.None, 1f);
            return;
        }

        int startLine = rangeMin / bytesPerLine;
        int endLine = rangeMax / bytesPerLine;
        int currentLine = lineBase / bytesPerLine;

        bool isStartLine = startLine == (lineBase / bytesPerLine);
        bool isEndLine = endLine == (lineBase / bytesPerLine);
        bool isLastByte = i == (bytesPerLine - 1);

        bool renderedBg = false;

        if (offset == rangeMin)
        {
            if (!isLastByte)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersTopLeft);
                RenderTopLeftCornerRect(drawList, bbMin, bbMax, borderColor, rounding);

                if (startLine == endLine)
                    drawList.AddLine(new Vector2(bbMin.X, bbMax.Y), new Vector2(bbMax.X, bbMax.Y), borderColor, 1f);
            }
            else
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersTop);
                RenderTopCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
            }

            renderedBg = true;
        }
        else if (i == 0)
        {
            if (isEndLine)
            {
                if (offset == rangeMax)
                {
                    drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersBottom);
                    RenderBottomCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                }
                else
                {
                    drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersBottomLeft);
                    RenderBottomLeftCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                }

                renderedBg = true;
            }
            else if (currentLine == startLine + 1 && (rangeMin % bytesPerLine) != 0)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersTopLeft);
                RenderTopLeftCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                renderedBg = true;
            }
            else
            {
                if (!renderedBg)
                {
                    drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
                    renderedBg = true;
                }

                drawList.AddLine(new Vector2(bbMin.X, bbMin.Y), new Vector2(bbMin.X, bbMax.Y), borderColor, 1f);
            }
        }

        if (i != 0 && offset == rangeMax)
        {
            if (startLine == endLine)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersTopRight);
                RenderTopRightCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                drawList.AddLine(new Vector2(bbMin.X, bbMax.Y), new Vector2(bbMax.X, bbMax.Y), borderColor, 1f);
            }
            else
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersBottomRight);
                RenderBottomRightCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
            }

            renderedBg = true;
        }
        else if (isLastByte && offset != rangeMin)
        {
            if (isStartLine)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersTopRight);
                RenderTopRightCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                renderedBg = true;
            }
            else if (currentLine == endLine - 1 && (rangeMax % bytesPerLine) != bytesPerLine - 1)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, rounding, ImDrawFlags.RoundCornersBottomRight);
                RenderBottomRightCornerRect(drawList, bbMin, bbMax, borderColor, rounding);
                renderedBg = true;
            }
            else
            {
                if (!renderedBg)
                {
                    drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
                    renderedBg = true;
                }

                drawList.AddLine(new Vector2(bbMax.X - 1f, bbMin.Y), new Vector2(bbMax.X - 1f, bbMax.Y), borderColor, 1f);
            }
        }

        if ((isStartLine && offset != rangeMin && !isLastByte && offset != rangeMax)
            || (currentLine == startLine + 1 && (i < (rangeMin % bytesPerLine) && i != 0)))
        {
            if (!renderedBg)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
                renderedBg = true;
            }

            drawList.AddLine(new Vector2(bbMin.X, bbMin.Y), new Vector2(bbMax.X, bbMin.Y), borderColor, 1f);
        }

        if ((isEndLine && offset != rangeMax && i != 0)
            || (currentLine == endLine - 1 && (i > (rangeMax % bytesPerLine) && !isLastByte)))
        {
            if (!renderedBg)
            {
                drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
                renderedBg = true;
            }

            drawList.AddLine(new Vector2(bbMin.X, bbMax.Y), new Vector2(bbMax.X, bbMax.Y), borderColor, 1f);
        }

        if (!renderedBg)
            drawList.AddRectFilled(bbMin, bbMax, bgColor, 0f, ImDrawFlags.None);
    }

    public static bool BeginHexEditor(string strId, HexEditorState state, Vector2 size = default, ImGuiChildFlags childFlags = ImGuiChildFlags.None, ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None)
    {
        if (!ImGui.BeginChild(strId, size, childFlags, windowFlags))
            return false;

        Vector2 charSize = ImGui.CalcTextSize("0");
        Vector2 byteSize = new(charSize.X * 2f, charSize.Y);

        var style = ImGui.GetStyle();
        Vector2 spacing = style.ItemSpacing;

        Vector2 contentAvail = ImGui.GetContentRegionAvail();

        float addressMaxSize;
        int addressMaxChars;
        if (state.ShowAddress)
        {
            int addressChars = state.AddressChars;
            if (addressChars == -1)
                addressChars = state.MaxBytes.ToString("X").Length;

            addressMaxChars = addressChars + 1;
            addressMaxSize = charSize.X * addressMaxChars + spacing.X * 0.5f;
        }
        else
        {
            addressMaxSize = 0f;
            addressMaxChars = 0;
        }

        float bytesAvailX = contentAvail.X - addressMaxSize;
        if (ImGui.GetScrollMaxY() > 0f)
            bytesAvailX -= style.ScrollbarSize;

        bool showAscii = state.ShowAscii;

        if (showAscii)
            bytesAvailX -= charSize.X * 0.5f;

        bytesAvailX = bytesAvailX < 0f ? 0f : bytesAvailX;

        int bytesPerLine;

        if (state.BytesPerLine == -1)
        {
            bytesPerLine = CalcBytesPerLine(bytesAvailX, byteSize, spacing, showAscii, charSize, state.Separators);
        }
        else
        {
            bytesPerLine = state.BytesPerLine;
        }

        // One-shot scroll request (used by tools like the Memory Viewer search).
        if (state.RequestScrollToByte >= 0 && bytesPerLine > 0)
        {
            int clamped = Math.Clamp(state.RequestScrollToByte, 0, Math.Max(0, state.MaxBytes - 1));
            int lineIndex = clamped / bytesPerLine;
            float lineHeight = byteSize.Y + spacing.Y;
            float targetY = (lineIndex * lineHeight) - (contentAvail.Y * 0.35f);
            if (targetY < 0f) targetY = 0f;
            ImGui.SetScrollY(targetY);
            state.RequestScrollToByte = -1;
        }

        int actualSeparators = bytesPerLine / state.Separators;
        if (bytesPerLine % state.Separators == 0)
            --actualSeparators;

        int linesCount;
        if (bytesPerLine != 0)
        {
            linesCount = state.MaxBytes / bytesPerLine;
            if (linesCount * bytesPerLine < state.MaxBytes)
            {
                ++linesCount;
            }
        }
        else
            linesCount = 0;

        var drawList = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();

        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);
        uint textDisabledColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        uint textSelectedBgColor = ImGui.GetColorU32(ImGuiCol.TextSelectedBg);
        uint separatorColor = ImGui.GetColorU32(ImGuiCol.Separator);
        uint borderColor = ImGui.GetColorU32(ImGuiCol.FrameBgActive);

        bool lowercaseBytes = state.LowercaseBytes;

        int selectStartByte = state.SelectStartByte;
        int selectStartSubbyte = state.SelectStartSubByte;
        int selectEndByte = state.SelectEndByte;
        int selectEndSubbyte = state.SelectEndSubByte;
        int lastSelectedByte = state.LastSelectedByte;
        int selectDragByte = state.SelectDragByte;
        int selectDragSubbyte = state.SelectDragSubByte;

        int nextSelectStartByte = selectStartByte;
        int nextSelectStartSubbyte = selectStartSubbyte;
        int nextSelectEndByte = selectEndByte;
        int nextSelectEndSubbyte = selectEndSubbyte;
        int nextLastSelectedByte = lastSelectedByte;
        int nextSelectDragByte = selectDragByte;
        int nextSelectDragSubbyte = selectDragSubbyte;

        ImGuiKey hexKeyPressed = ImGuiKey.None;

        if (state.EnableClipboard && ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.C))
        {
            if (state.SelectStartByte != -1)
            {
                int bytesCount = (state.SelectEndByte + 1) - state.SelectStartByte;
                byte[] bytes = new byte[bytesCount];

                int readBytes;

                if (state.ReadCallback != null)
                    readBytes = state.ReadCallback(state, state.SelectStartByte, bytes, bytesCount);
                else if (state.Bytes != null)
                {
                    Array.Copy(state.Bytes, state.SelectStartByte, bytes, 0, bytesCount);
                    readBytes = bytesCount;
                }
                else
                {
                    readBytes = 0;
                }

                if (readBytes > 0)
                {
                    var sb = new StringBuilder();

                    for (int i = 0, absI = state.SelectStartByte; i < readBytes; i++, absI++)
                    {
                        byte b = bytes[i];
                        sb.Append(HalfByteToPrintable((byte)((b & 0xF0) >> 4), lowercaseBytes));
                        sb.Append(HalfByteToPrintable((byte)(b & 0x0F), lowercaseBytes));

                        if (bytesPerLine != 0 && ((absI % bytesPerLine) == bytesPerLine - 1) && absI != 0)
                        {
                            if ((state.ClipboardFlags & HexEditorClipboardFlags.Multiline) != 0)
                                sb.AppendLine();
                        }
                        else
                        {
                            sb.Append(' ');
                        }
                    }

                    ImGui.SetClipboardText(sb.ToString());
                }
            }
        }
        else
        {
            if (lastSelectedByte != -1)
            {
                bool anyPressed = false;
                if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
                {
                    if (selectStartSubbyte == 0)
                    {
                        if (lastSelectedByte == 0)
                        {
                            nextLastSelectedByte = 0;
                        }
                        else
                        {
                            nextLastSelectedByte = lastSelectedByte - 1;
                            nextSelectStartSubbyte = 1;
                        }
                    }
                    else
                        nextSelectStartSubbyte = 0;

                    anyPressed = true;
                }
                else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
                {
                    if (selectStartSubbyte != 0)
                    {
                        if (lastSelectedByte >= state.MaxBytes - 1)
                        {
                            nextLastSelectedByte = state.MaxBytes - 1;
                        }
                        else
                        {
                            nextLastSelectedByte = lastSelectedByte + 1;
                            nextSelectStartSubbyte = 0;
                        }
                    }
                    else
                        nextSelectStartSubbyte = 1;

                    anyPressed = true;
                }
                else if (bytesPerLine != 0)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                    {
                        if (lastSelectedByte >= bytesPerLine)
                        {
                            nextLastSelectedByte = lastSelectedByte - bytesPerLine;
                        }

                        anyPressed = true;
                    }
                    else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                    {
                        if (lastSelectedByte < state.MaxBytes - bytesPerLine)
                        {
                            nextLastSelectedByte = lastSelectedByte + bytesPerLine;
                        }

                        anyPressed = true;
                    }
                }

                if (anyPressed)
                {
                    nextSelectStartByte = nextLastSelectedByte;
                    nextSelectEndByte = nextLastSelectedByte;
                }
            }

            for (ImGuiKey key = ImGuiKey.A; key <= ImGuiKey.F; key++)
            {
                if (ImGui.IsKeyPressed(key))
                {
                    hexKeyPressed = key;
                    break;
                }
            }

            if (hexKeyPressed == ImGuiKey.None)
            {
                for (ImGuiKey key = ImGuiKey.Key0; key <= ImGuiKey.Key9; key++)
                {
                    if (ImGui.IsKeyPressed(key))
                    {
                        hexKeyPressed = key;
                        break;
                    }
                }
            }
        }

        byte[] lineBuf = new byte[bytesPerLine > 128 ? bytesPerLine : 128];
        
        Vector2 mousePos = ImGui.GetMousePos();
        bool mouseLeftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        var clipper = new ImGuiListClipper();
        clipper.Begin(linesCount, byteSize.Y + spacing.Y);

        unsafe
        {
            while (clipper.Step())
            {
                int clipperLines = clipper.DisplayEnd - clipper.DisplayStart;

                Vector2 cursor = ImGui.GetCursorScreenPos();

                Vector2 asciiCursor = new(
                    cursor.X + addressMaxSize + (spacing.X * 0.5f) + (bytesPerLine * (byteSize.X + spacing.X)) + (actualSeparators * spacing.X),
                    cursor.Y
                );

                if (showAscii)
                {
                    drawList.AddLine(asciiCursor, new Vector2(asciiCursor.X, asciiCursor.Y + clipperLines * (byteSize.Y + spacing.Y)), separatorColor, 1f);
                }

                for (int n = clipper.DisplayStart; n < clipper.DisplayEnd; n++)
                {
                    int lineBase = n * bytesPerLine;
                    if (state.ShowAddress)
                    {
                        string addressText;
                        if (state.GetAddressNameCallback != null && state.GetAddressNameCallback(state, lineBase, out string? customAddress) && customAddress != null)
                        {
                            addressText = customAddress;
                        }
                        else
                        {
                            addressText = lineBase.ToString($"X{addressMaxChars - 1}");
                        }

                        Vector2 textSize = ImGui.CalcTextSize(addressText);
                        drawList.AddText(cursor, textColor, addressText);
                        drawList.AddText(new Vector2(cursor.X + textSize.X, cursor.Y), textDisabledColor, ":");
                        cursor.X += addressMaxSize;
                    }

                    int maxBytesPerLine = state.MaxBytes - lineBase;
                    maxBytesPerLine = maxBytesPerLine > bytesPerLine ? bytesPerLine : maxBytesPerLine;

                    int bytesRead;
                    if (state.ReadCallback == null)
                    {
                        if (state.Bytes != null)
                        {
                            Array.Copy(state.Bytes, lineBase, lineBuf, 0, maxBytesPerLine);
                            bytesRead = maxBytesPerLine;
                        }
                        else
                        {
                            bytesRead = 0;
                        }
                    }
                    else
                    {
                        bytesRead = state.ReadCallback(state, lineBase, lineBuf, maxBytesPerLine);
                    }

                    cursor.X += spacing.X * 0.5f;

                    for (int i = 0; i < bytesPerLine; i++)
                    {
                        Vector2 byteBbMin = new(cursor.X, cursor.Y);
                        Vector2 byteBbMax = new(cursor.X + byteSize.X, cursor.Y + byteSize.Y);

                        Vector2 itemBbMin = new(byteBbMin.X - spacing.X * 0.5f, byteBbMin.Y);
                        Vector2 itemBbMax = new(byteBbMax.X + spacing.X * 0.5f, byteBbMax.Y + spacing.Y * 0.5f);

                        if (n != clipper.DisplayStart)
                            itemBbMin.Y -= spacing.Y * 0.5f;

                        int offset = bytesPerLine * n + i;
                        byte byteValue;

                        Vector2 byteAscii = new(
                            asciiCursor.X + (charSize.X * i) + spacing.X,
                            asciiCursor.Y + (charSize.Y + spacing.Y) * (n - clipper.DisplayStart)
                        );

                        string text;
                        if (offset < state.MaxBytes && i < bytesRead)
                        {
                            byteValue = lineBuf[i];
                            text = $"{HalfByteToPrintable((byte)((byteValue & 0xF0) >> 4), lowercaseBytes)}{HalfByteToPrintable((byte)(byteValue & 0x0F), lowercaseBytes)}";
                        }
                        else
                        {
                            byteValue = 0x00;
                            text = "??";
                        }

                        uint id = ImGui.GetID(offset);

                        uint byteTextColor = (offset >= state.MaxBytes || (state.RenderZeroesDisabled && byteValue == 0x00) || i >= bytesRead) ? textDisabledColor : textColor;

                        if (offset >= selectStartByte && offset <= selectEndByte)
                        {
                            HexEditorHighlightFlags flags = state.SelectionHighlightFlags;

                            if (selectStartByte == selectEndByte)
                            {
                                flags &= ~HexEditorHighlightFlags.FullSized;
                            }

                            Vector2 bbMin = (flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMin : byteBbMin;
                            Vector2 bbMax = (flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMax : byteBbMax;

                            if (selectStartByte == selectEndByte)
                            {
                                if (selectStartSubbyte != 0)
                                    bbMin.X = (byteBbMin.X + byteBbMax.X) / 2f;
                                else
                                    bbMax.X = (byteBbMin.X + byteBbMax.X) / 2f;
                            }

                            RenderByteDecorations(drawList, bbMin, bbMax, textSelectedBgColor, flags, borderColor,
                                style.FrameRounding, offset, selectStartByte, selectEndByte, bytesPerLine, i, lineBase);

                            if ((flags & HexEditorHighlightFlags.Ascii) != 0)
                            {
                                Vector2 asciiMin = byteAscii;
                                Vector2 asciiMax = new(byteAscii.X + charSize.X, byteAscii.Y + charSize.Y);
                                RenderByteDecorations(drawList, asciiMin, asciiMax, textSelectedBgColor, flags, borderColor,
                                    style.FrameRounding, offset, offset, offset, bytesPerLine, i, lineBase);
                            }
                        }
                        else
                        {
                            bool singleHighlight = false;

                            if (state.SingleHighlightCallback != null)
                            {
                                HexEditorHighlightFlags flags = state.SingleHighlightCallback(state, offset,
                                    out uint color, out uint customTextColor, out uint customBorderColor);

                                if ((flags & HexEditorHighlightFlags.Apply) != 0)
                                {
                                    uint highlightBorderColor;

                                    if ((flags & HexEditorHighlightFlags.BorderAutomaticContrast) != 0)
                                        highlightBorderColor = CalcContrastColor(color);
                                    else if ((flags & HexEditorHighlightFlags.OverrideBorderColor) != 0)
                                        highlightBorderColor = customBorderColor;
                                    else
                                        highlightBorderColor = borderColor;

                                    singleHighlight = true;

                                    Vector2 bbMin = (flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMin : byteBbMin;
                                    Vector2 bbMax = (flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMax : byteBbMax;

                                    RenderByteDecorations(drawList, bbMin, bbMax, color, flags, highlightBorderColor,
                                        style.FrameRounding, offset, offset, offset, bytesPerLine, i, lineBase);

                                    if ((flags & HexEditorHighlightFlags.Ascii) != 0)
                                    {
                                        Vector2 asciiMin = byteAscii;
                                        Vector2 asciiMax = new(byteAscii.X + charSize.X, byteAscii.Y + charSize.Y);
                                        RenderByteDecorations(drawList, asciiMin, asciiMax, color, flags, highlightBorderColor,
                                            style.FrameRounding, offset, offset, offset, bytesPerLine, i, lineBase);
                                    }

                                    if ((flags & HexEditorHighlightFlags.TextAutomaticContrast) != 0)
                                        byteTextColor = CalcContrastColor(color);
                                    else
                                        byteTextColor = customTextColor;
                                }
                            }

                            if (!singleHighlight)
                            {
                                foreach (var range in state.HighlightRanges)
                                {
                                    if (lineBase + i >= range.From && lineBase + i <= range.To)
                                    {
                                        uint highlightBorderColor;

                                        if ((range.Flags & HexEditorHighlightFlags.BorderAutomaticContrast) != 0)
                                            highlightBorderColor = CalcContrastColor(range.Color);
                                        else if ((range.Flags & HexEditorHighlightFlags.OverrideBorderColor) != 0)
                                            highlightBorderColor = range.BorderColor;
                                        else
                                            highlightBorderColor = borderColor;

                                        Vector2 bbMin = (range.Flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMin : byteBbMin;
                                        Vector2 bbMax = (range.Flags & HexEditorHighlightFlags.FullSized) != 0 ? itemBbMax : byteBbMax;

                                        RenderByteDecorations(drawList, bbMin, bbMax, range.Color, range.Flags, highlightBorderColor,
                                            style.FrameRounding, offset, range.From, range.To, bytesPerLine, i, lineBase);

                                        if ((range.Flags & HexEditorHighlightFlags.Ascii) != 0)
                                        {
                                            Vector2 asciiMin = byteAscii;
                                            Vector2 asciiMax = new(byteAscii.X + charSize.X, byteAscii.Y + charSize.Y);
                                            RenderByteDecorations(drawList, asciiMin, asciiMax, range.Color, range.Flags, highlightBorderColor,
                                                style.FrameRounding, offset, range.From, range.To, bytesPerLine, i, lineBase);
                                        }

                                        if ((range.Flags & HexEditorHighlightFlags.TextAutomaticContrast) != 0)
                                            byteTextColor = CalcContrastColor(range.Color);
                                    }
                                }
                            }
                        }

                        drawList.AddText(byteBbMin, byteTextColor, text);

                        if (offset == selectStartByte)
                        {
                            state.SelectCursorAnimationTime += io.DeltaTime;

                            if (!io.ConfigInputTextCursorBlink || (state.SelectCursorAnimationTime % 1.20f) <= 0.80f)
                            {
                                Vector2 pos = new(byteBbMin.X, byteBbMax.Y);

                                if (selectStartSubbyte != 0)
                                    pos.X += charSize.X;

                                drawList.AddLine(pos, new Vector2(pos.X + charSize.X, pos.Y), textColor, 1f);
                            }
                        }

                        bool hovered = ImGui.IsMouseHoveringRect(itemBbMin, itemBbMax);

                        if (selectDragByte != -1 && offset == selectDragByte && !mouseLeftDown)
                        {
                            nextSelectDragByte = -1;
                        }
                        else
                        {
                            if (hovered)
                            {
                                bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

                                if (clicked)
                                {
                                    nextSelectStartByte = offset;
                                    nextSelectEndByte = offset;
                                    nextSelectDragByte = offset;
                                    nextSelectDragSubbyte = mousePos.X > (byteBbMin.X + byteBbMax.X) / 2f ? 1 : 0;
                                    nextSelectStartSubbyte = nextSelectDragSubbyte;
                                    nextLastSelectedByte = offset;
                                }
                                else if (mouseLeftDown && selectDragByte != -1)
                                {
                                    if (offset >= selectDragByte)
                                    {
                                        nextSelectEndByte = offset;
                                    }
                                    else
                                    {
                                        nextSelectStartByte = offset;
                                        nextSelectEndByte = selectDragByte;
                                        nextSelectStartSubbyte = 0;
                                    }
                                }
                            }
                        }



                        if (offset == lastSelectedByte && !state.ReadOnly && hexKeyPressed != ImGuiKey.None)
                        {
                            int subbyte = offset == selectStartByte ? selectStartSubbyte : selectEndSubbyte;

                            byte wbyte;
                            if (subbyte != 0)
                                wbyte = (byte)((byteValue & 0xF0) | KeyToHalfByte(hexKeyPressed));
                            else
                                wbyte = (byte)((KeyToHalfByte(hexKeyPressed) << 4) | (byteValue & 0x0F));

                            if (state.WriteCallback == null)
                            {
                                if (state.Bytes != null)
                                    state.Bytes[n * bytesPerLine + i] = wbyte;
                            }
                            else
                            {
                                state.WriteCallback(state, n * bytesPerLine + i, new byte[] { wbyte }, 1);
                            }

                            if (subbyte == 0)
                            {
                                nextSelectStartByte = offset;
                                nextSelectEndByte = offset;
                                nextSelectStartSubbyte = 1;
                            }
                            else
                            {
                                nextLastSelectedByte = offset + 1;
                                if (nextLastSelectedByte >= state.MaxBytes - 1)
                                    nextLastSelectedByte = state.MaxBytes - 1;
                                else
                                    nextSelectStartSubbyte = 0;

                                nextSelectStartByte = nextLastSelectedByte;
                                nextSelectEndByte = nextLastSelectedByte;
                            }

                            state.SelectCursorAnimationTime = 0f;
                        }

                        cursor.X += byteSize.X + spacing.X;
                        if (i > 0 && state.Separators > 0 && (i + 1) % state.Separators == 0
                            && i != bytesPerLine - 1)
                            cursor.X += spacing.X;

                        if (showAscii)
                        {
                            byte asciiByteValue = offset < state.MaxBytes ? lineBuf[i] : (byte)0x00;
                            bool hasAscii = HasAsciiRepresentation(asciiByteValue);

                            string asciiText = hasAscii ? ((char)asciiByteValue).ToString() : ".";
                            drawList.AddText(byteAscii, byteTextColor, asciiText);
                        }

                        ImGui.SetCursorScreenPos(cursor);
                    }

                    ImGui.NewLine();
                    cursor = ImGui.GetCursorScreenPos();
                }
            }
        }

        state.SelectStartByte = nextSelectStartByte;
        state.SelectStartSubByte = nextSelectStartSubbyte;
        state.SelectEndByte = nextSelectEndByte;
        state.SelectEndSubByte = nextSelectEndSubbyte;
        state.LastSelectedByte = nextLastSelectedByte;
        state.SelectDragByte = nextSelectDragByte;
        state.SelectDragSubByte = nextSelectDragSubbyte;

        return true;
    }

    public static void EndHexEditor()
    {
        ImGui.EndChild();
    }

    public static bool CalcHexEditorRowRange(int rowOffset, int rowBytesCount, int rangeMin, int rangeMax, out int outMin, out int outMax)
    {
        return RangeRangeIntersection(rowOffset, rowOffset + rowBytesCount, rangeMin, rangeMax, out outMin, out outMax);
    }
}
