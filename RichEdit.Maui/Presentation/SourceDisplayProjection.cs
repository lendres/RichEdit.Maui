using System.Text;

namespace RichEdit.Maui;

// Per-view display state: tabs and exact line endings remain in the source.
internal sealed class SourceDisplayProjection
{
    private readonly LfTextProjection _lineEndings;
    private readonly record struct Tab(int Position, int Start, int Width);
    private readonly Tab[] _tabs;
    private readonly TextSpan? _dirtyRange;
    internal TextSnapshot Source => _lineEndings.Source;
    internal TextSnapshot Display { get; }
    internal int TabSize { get; }
    internal bool HasTabs => _tabs.Length != 0;

    internal SourceDisplayProjection(LfTextProjection lineEndings, int tabSize = 0)
    {
        _lineEndings = lineEndings;
        TabSize = tabSize;
        bool containsTabs = false;
        if (tabSize > 0)
        {
            foreach (var chunk in lineEndings.Display.GetChunks(new(0, lineEndings.Display.Length)))
            {
                if (chunk.Span.Contains('\t'))
                {
                    containsTabs = true;
                    break;
                }
            }
        }
        if (!containsTabs)
        {
            Display = lineEndings.Display;
            _tabs = [];
            return;
        }
        var text = new StringBuilder();
        var tabs = new List<Tab>();
        int column = 0;
        int position = 0;
        foreach (var chunk in lineEndings.Display.GetChunks(new(0, lineEndings.Display.Length)))
        {
            foreach (char character in chunk.Span)
            {
                if (character == '\t')
                {
                    int width = tabSize - column % tabSize;
                    tabs.Add(new(position, text.Length, width));
                    text.Append(' ', width);
                    column += width;
                }
                else
                {
                    text.Append(character);
                    if (character == '\n')
                    {
                        column = 0;
                    }
                    else
                    {
                        if (!char.IsLowSurrogate(character) && char.GetUnicodeCategory(character) is not System.Globalization.UnicodeCategory.NonSpacingMark and not System.Globalization.UnicodeCategory.EnclosingMark)
                        {
                            column++;
                        }
                    }
                }
                position++;
            }
        }
        _tabs = tabs.ToArray();
        Display = _tabs.Length == 0 ? lineEndings.Display : TextSnapshot.FromText(text.ToString());
    }

    private SourceDisplayProjection(LfTextProjection lineEndings, int tabSize, Tab[] tabs, TextSnapshot display, TextSpan? dirtyRange = null)
    {
        _lineEndings = lineEndings;
        TabSize = tabSize;
        _tabs = tabs;
        Display = display;
        _dirtyRange = dirtyRange;
    }

    internal static SourceDisplayProjection Create(LfTextProjection lineEndings, int tabSize, SourceDisplayProjection? previous)
    {
        if (previous is null || previous.TabSize != tabSize || tabSize == 0)
        {
            return new(lineEndings, tabSize);
        }
        var difference = previous._lineEndings.Display.FindDifference(lineEndings.Display, out _);
        if (difference is null && previous._dirtyRange is null)
        {
            return new(lineEndings, tabSize, previous._tabs, previous.Display);
        }
        if (difference is not null && previous._dirtyRange is not null)
        {
            return new(lineEndings, tabSize);
        }
        TextSpan oldRange = difference?.Before ?? previous._dirtyRange!.Value;
        TextSpan newRange = difference?.After ?? oldRange;
        var oldText = previous._lineEndings.Display;
        var newText = lineEndings.Display;
        int start = oldText.GetLine(oldText.GetPosition(oldRange.Start).Line).Span.Start;
        var oldLastLine = oldText.GetLine(oldText.GetPosition(oldRange.End).Line);
        var newLastLine = newText.GetLine(newText.GetPosition(newRange.End).Line);
        int oldEnd = oldLastLine.Span.End + oldLastLine.LineBreakLength;
        int newEnd = newLastLine.Span.End + newLastLine.LineBreakLength;
        int displayStart = previous.ToDisplay(previous._lineEndings.ToSource(start));
        int displayEnd = previous.ToDisplay(previous._lineEndings.ToSource(oldEnd));
        var replacement = new SourceDisplayProjection(new LfTextProjection(newText.Slice(new(start, newEnd - start))), tabSize);
        var display = previous.Display.Slice(new(0, displayStart)).Concat(replacement.Display)
            .Concat(previous.Display.Slice(new(displayEnd, previous.Display.Length - displayEnd)));
        int sourceShift = newEnd - oldEnd;
        int displayShift = replacement.Display.Length - (displayEnd - displayStart);
        var tabs = new List<Tab>();
        foreach (Tab tab in previous._tabs)
        {
            if (tab.Position < start)
            {
                tabs.Add(tab);
            }
        }
        foreach (Tab tab in replacement._tabs)
        {
            tabs.Add(new(tab.Position + start, tab.Start + displayStart, tab.Width));
        }
        foreach (Tab tab in previous._tabs)
        {
            if (tab.Position >= oldEnd)
            {
                tabs.Add(new(tab.Position + sourceShift, tab.Start + displayShift, tab.Width));
            }
        }
        return new(lineEndings, tabSize, tabs.ToArray(), display);
    }

    internal static SourceDisplayProjection FromNative(TextSnapshot source, TextSnapshot display, int tabSize)
    {
        var normalized = new LfTextProjection(source);
        // Native input can insert a literal tab before the display is rebuilt.
        TextSpan? dirty = null;
        foreach (var chunk in display.GetChunks(new(0, display.Length)))
        {
            if (chunk.Span.Contains('\t'))
            {
                dirty = new(0, normalized.Display.Length);
                break;
            }
        }
        return new(normalized, tabSize, [], display, dirty);
    }

    public static implicit operator SourceDisplayProjection(LfTextProjection projection) => new(projection);

    internal int ToDisplay(int source, bool after = true)
    {
        int position = _lineEndings.ToDisplay(source, after);
        int low = 0;
        int high = _tabs.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_tabs[middle].Position < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low == 0 ? position : position + _tabs[low - 1].Start + _tabs[low - 1].Width - _tabs[low - 1].Position - 1;
    }

    internal int ToSource(int display)
    {
        int low = 0;
        int high = _tabs.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_tabs[middle].Start <= display)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        int position = display;
        if (low > 0)
        {
            Tab tab = _tabs[low - 1];
            position = display < tab.Start + tab.Width
                ? tab.Position + (display - tab.Start > tab.Width / 2.0 ? 1 : 0)
                : display - (tab.Start + tab.Width - tab.Position - 1);
        }
        return _lineEndings.ToSource(Math.Clamp(position, 0, _lineEndings.Display.Length));
    }

    internal TextSpan ToDisplay(TextSpan range)
    {
        int start = ToDisplay(range.Start, false);
        return new(start, ToDisplay(range.End) - start);
    }

    internal TextSpan ToSource(TextSpan range)
    {
        int start = ToSource(range.Start);
        return new(start, Math.Max(0, ToSource(range.End) - start));
    }

    // Retain surviving expansion positions until the next native publication.
    internal SourceDisplayProjection AfterNativeEdit(TextSnapshot source, TextSnapshot display, TextSpan removed, int insertedLength)
    {
        TextSpan sourceRange = ToSource(removed);
        int displayShift = insertedLength - removed.Length;
        var lineEndings = new LfTextProjection(source);
        int sourceShift = lineEndings.Display.Length - _lineEndings.Display.Length;
        var tabs = new List<Tab>();
        foreach (Tab tab in _tabs)
        {
            if (tab.Start + tab.Width <= removed.Start)
            {
                tabs.Add(tab);
            }
            else
            {
                if (tab.Start >= removed.End)
                {
                    tabs.Add(new(tab.Position + sourceShift, tab.Start + displayShift, tab.Width));
                }
            }
        }
        int dirtyStart = lineEndings.ToDisplay(sourceRange.Start);
        return new(lineEndings, TabSize, tabs.ToArray(), display, new(dirtyStart, Math.Min(insertedLength, lineEndings.Display.Length - dirtyStart)));
    }
}
