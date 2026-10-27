using Microsoft.UI.Text;
using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task DisplayExpandedTabsPreserveNativeEditsAndUndo() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("1\t= first\r\n4444\t= second");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(150);
            editor.SelectedRange = new(0, 0);
            fixture.Handler.PlatformView.Document.Selection.TypeText("22");
            await Task.Delay(150);
            Assert.Equal("221\t= first\r\n4444\t= second", editor.Document.Text);
            Assert.Equal(new TextSpan(2, 0), editor.SelectedRange);
            fixture.Handler.PlatformView.Document.GetText(TextGetOptions.None, out string nativeText);
            Assert.StartsWith("221 = first", nativeText);
            editor.Undo();
            Assert.Equal("1\t= first\r\n4444\t= second", editor.Document.Text);
            editor.Redo();
            Assert.Equal("221\t= first\r\n4444\t= second", editor.Document.Text);
            editor.TabSize = 8;
            Assert.Equal("221\t= first\r\n4444\t= second", editor.Document.Text);
            editor.SelectedRange = new(4, 0);
            var left = fixture.Handler.NavigateSelection(editor.Selections.Primary, EditorKey.Left, false);
            Assert.NotNull(left);
            Assert.Equal(3, left.Active);
            var right = fixture.Handler.NavigateSelection(left, EditorKey.Right, false);
            Assert.NotNull(right);
            Assert.Equal(4, right.Active);
        }
        finally
        {
            window.Content = null;
        }
    });

    [Theory]
    [InlineData(2, 12, "Consolas")]
    [InlineData(4, 14, "Consolas")]
    [InlineData(8, 20, "Consolas")]
    [InlineData(2, 20, "Courier New")]
    [InlineData(4, 12, "Courier New")]
    [InlineData(8, 14, "Courier New")]
    public Task ExpandedTabsAlignAcrossFontsAndLongLines(int tabSize, double fontSize, string fontFamily) => WindowsTestHost.RunAsync(async () =>
    {
        int[] prefixLengths = [3, 4, 5, 7, 8, 9, 127, 128, 129, 255, 256, 257];
        var lines = new List<string>();
        foreach (int prefixLength in prefixLengths)
        {
            int column = (prefixLength / tabSize + 1) * tabSize;
            lines.Add(new string('x', prefixLength) + "\t=");
            lines.Add(new string(' ', column) + "=");
        }
        using var fixture = new CodeEditorFixture(string.Join("\n", lines));
        fixture.Editor.FontFamily = fontFamily;
        fixture.Editor.FontSize = fontSize;
        fixture.Editor.TabSize = tabSize;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(150);
            int offset = 0;
            for (int index = 0; index < lines.Count; index += 2)
            {
                int actualOffset = offset + lines[index].Length - 1;
                int expectedOffset = offset + lines[index].Length + lines[index + 1].Length;
                double actualPosition = fixture.Handler.GetTextCaretBounds(actualOffset, RichTextCaretAffinity.Downstream)!.Value.Left;
                double expectedPosition = fixture.Handler.GetTextCaretBounds(expectedOffset, RichTextCaretAffinity.Downstream)!.Value.Left;
                Assert.True(Math.Abs(actualPosition - expectedPosition) <= 1, $"{fontFamily}, size={fontSize}, tabs={tabSize}, prefix={prefixLengths[index / 2]}: expected X={expectedPosition}, actual X={actualPosition}");
                offset += lines[index].Length + lines[index + 1].Length + 2;
            }
        }
        finally
        {
            window.Content = null;
        }
    });

    [Fact]
    public Task ExpandedTabsAreAtomicForSelectionDeletionAndNativeReplacement() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("\tX\tY");
        var editor = fixture.Editor;
        editor.TabSize = 8;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(150);
            // A native caret inside the first expanded tab snaps to its source boundary.
            fixture.Handler.PlatformView.Document.Selection.SetRange(2, 2);
            await Task.Delay(30);
            Assert.Equal(new TextSpan(0, 0), editor.SelectedRange);
            Assert.Equal(0, fixture.Handler.PlatformView.Document.Selection.StartPosition);
            editor.SelectedRange = new(1, 0);
            Assert.True(editor.TextView.SendKeyDown(EditorKey.Backspace, EditorKeyModifiers.None));
            Assert.Equal("X\tY", editor.Document.Text);
            editor.Undo();
            Assert.Equal("\tX\tY", editor.Document.Text);
            editor.SelectedRange = new(0, 0);
            Assert.True(editor.TextView.SendKeyDown(EditorKey.Delete, EditorKeyModifiers.None));
            Assert.Equal("X\tY", editor.Document.Text);
            editor.Undo();
            editor.Selections = new((TextSelection[])[new(3, 0)]);
            fixture.Handler.PlatformView.Document.Selection.TypeText("22");
            await Task.Delay(150);
            Assert.Equal("22Y", editor.Document.Text);
            editor.Undo();
            Assert.Equal("\tX\tY", editor.Document.Text);
        }
        finally
        {
            window.Content = null;
        }
    });

    [Fact]
    public Task ExpandedTabClipboardPreservesTabsAndLineEndings() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        const string source = "1\t= first\r\n4444\t= second";
        using var fixture = new CodeEditorFixture(source);
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            editor.SelectAll();
            await EditorContractTests.WithClipboardAccess(() => editor.CopyAsync());
            string? copiedText = null;
            await EditorContractTests.WithClipboardAccess(async () =>
            {
                copiedText = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
            });
            Assert.Equal(source, copiedText);
            editor.SelectedRange = new(source.Length, 0);
            await EditorContractTests.WithClipboardAccess(() => editor.PasteAsync());
            Assert.Equal(source + source, editor.Document.Text);
            editor.Undo();
            Assert.Equal(source, editor.Document.Text);
        }
        finally
        {
            window.Content = null;
        }
    });

    [Fact]
    public Task ExpandedTabsRespectSourceLengthLimitDuringNativeReplacement() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("\tX");
        var editor = fixture.Editor;
        editor.MaxLength = 3;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            editor.SelectedRange = new(0, 1);
            fixture.Handler.PlatformView.Document.Selection.TypeText("ab");
            await Task.Delay(100);
            Assert.Equal("abX", editor.Document.Text);
            editor.Undo();
            Assert.Equal("\tX", editor.Document.Text);
            editor.SelectedRange = new(0, 1);
            fixture.Handler.PlatformView.Document.Selection.TypeText("abc");
            await Task.Delay(100);
            Assert.Equal("\tX", editor.Document.Text);
            fixture.Handler.PlatformView.Document.GetText(TextGetOptions.None, out string nativeText);
            Assert.StartsWith("    X", nativeText);
        }
        finally
        {
            window.Content = null;
        }
    });
}
