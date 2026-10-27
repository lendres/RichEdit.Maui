namespace RichEdit.Maui.Tests;

public sealed class SourceDisplayProjectionTests
{
    [Theory]
    [InlineData(2, "1 \n22  ")]
    [InlineData(4, "1   \n22  ")]
    [InlineData(8, "1       \n22      ")]
    public void TabsAndLineEndingsRoundTripSourceBoundaries(int tabSize, string expected)
    {
        var source = TextSnapshot.FromText("1\t\r\n22\t");
        var projection = new SourceDisplayProjection(new LfTextProjection(source), tabSize);
        Assert.Equal(expected, projection.Display.ToString());
        for (int position = 0; position <= source.Length; position++)
        {
            // There is no native caret between the two halves of CRLF.
            if (position == 3)
            {
                continue;
            }
            Assert.Equal(position, projection.ToSource(projection.ToDisplay(position)));
        }
        Assert.Equal("1\t\r\n22\t", source.ToString());
    }

    [Fact]
    public void PositionsInsideExpandedTabsSnapToSourceBoundaries()
    {
        var projection = new SourceDisplayProjection(new LfTextProjection(TextSnapshot.FromText("\tX")), 8);
        Assert.Equal(0, projection.ToSource(1));
        Assert.Equal(0, projection.ToSource(4));
        Assert.Equal(1, projection.ToSource(5));
        Assert.Equal(1, projection.ToSource(8));
        Assert.Equal(2, projection.ToSource(9));
    }

    [Theory]
    [InlineData(0, 0, "4444")]
    [InlineData(3, 2, "\nnew\tline\r\n")]
    [InlineData(1, 7, "")]
    [InlineData(7, 1, " ")]
    public void IncrementalProjectionMatchesFreshLayout(int start, int length, string replacement)
    {
        var source = TextSnapshot.FromText("1\tX\r\n22\tY\n333\tZ");
        var before = new SourceDisplayProjection(new LfTextProjection(source), 4);
        var updated = source.Replace(new(start, length), replacement);
        var incremental = SourceDisplayProjection.Create(new LfTextProjection(updated), 4, before);
        var expected = new SourceDisplayProjection(new LfTextProjection(updated), 4);
        Assert.Equal(expected.Display.ToString(), incremental.Display.ToString());
        for (int position = 0; position <= updated.Length; position++)
        {
            Assert.Equal(expected.ToDisplay(position), incremental.ToDisplay(position));
        }
        for (int position = 0; position <= expected.Display.Length; position++)
        {
            Assert.Equal(expected.ToSource(position), incremental.ToSource(position));
        }
    }

    [Fact]
    public void SharedSourceSupportsIndependentTabSizes()
    {
        var source = TextSnapshot.FromText("1\tX");
        var normalized = new LfTextProjection(source);
        var narrow = new SourceDisplayProjection(normalized, 4);
        var wide = new SourceDisplayProjection(normalized, 8);
        Assert.Equal("1   X", narrow.Display.ToString());
        Assert.Equal("1       X", wide.Display.ToString());
        Assert.Same(source, narrow.Source);
        Assert.Same(source, wide.Source);
    }

    [Fact]
    public void NativeInsertionRebuildsOnlyTheChangedLogicalLine()
    {
        var source = TextSnapshot.FromText("\tX\n\tY");
        var before = new SourceDisplayProjection(new LfTextProjection(source), 4);
        var updated = source.Replace(new(0, 0), "x");
        var observed = before.AfterNativeEdit(updated, TextSnapshot.FromText("x    X\n    Y"), new(0, 0), 1);
        var rebuilt = SourceDisplayProjection.Create(new LfTextProjection(updated), 4, observed);
        Assert.Equal("x   X\n    Y", rebuilt.Display.ToString());
        for (int position = 0; position <= updated.Length; position++)
        {
            Assert.Equal(position, rebuilt.ToSource(rebuilt.ToDisplay(position)));
        }
    }
}
