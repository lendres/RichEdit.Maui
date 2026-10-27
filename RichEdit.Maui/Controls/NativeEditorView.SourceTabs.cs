namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private bool UsesExpandedTabs => OperatingSystem.IsWindows() &&
        (Handler as RichEditorHandler)?.NativeProjection.SourceProjection?.HasTabs == true;

    private SourceReadback RestoreExpandedSource(RichTextDocumentSnapshot native, TextSelectionState selection, SourceDisplayProjection projection, TextSpan? knownSourceReplacement)
    {
        if (!projection.HasTabs)
        {
            var restored = RestoreNativeSource(native, selection, knownSourceReplacement);
            return restored with { Projection = SourceDisplayProjection.FromNative(restored.Snapshot.TextBuffer, native.TextBuffer, projection.TabSize) };
        }
        var before = InputSnapshot;
        string previousText = projection.Display.ToString();
        string nativeText = native.Text;
        if (previousText == nativeText)
        {
            return new(before, new(projection.ToSource(selection.Anchor), projection.ToSource(selection.Active)), projection);
        }
        TextSpan removed = projection.ToDisplay(InputSelection.Range);
        if (!RichTextDocumentSnapshot.TryGetReplacement(previousText, nativeText, removed, out string inserted))
        {
            int prefix = previousText.AsSpan().CommonPrefixLength(nativeText);
            int suffix = 0;
            int maximumSuffix = Math.Min(previousText.Length, nativeText.Length) - prefix;
            while (suffix < maximumSuffix && previousText[^(suffix + 1)] == nativeText[^(suffix + 1)])
            {
                suffix++;
            }
            removed = new(prefix, previousText.Length - prefix - suffix);
            inserted = nativeText.Substring(prefix, nativeText.Length - prefix - suffix);
        }
        TextSpan sourceRange = projection.ToSource(removed);
        var source = before.TextBuffer.Replace(sourceRange, inserted.Replace("\n", SourceNewLine, StringComparison.Ordinal));
        var observed = projection.AfterNativeEdit(source, native.TextBuffer, removed, inserted.Length);
        return new(RichTextDocumentSnapshot.CreateSourceProjection(source), new(observed.ToSource(selection.Anchor), observed.ToSource(selection.Active)), observed);
    }
}
