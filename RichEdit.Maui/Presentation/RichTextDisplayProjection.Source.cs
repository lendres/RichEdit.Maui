namespace RichEdit.Maui;

internal sealed partial class RichTextDisplayProjection
{
    private SourceDisplayProjection? _sourceText;
    internal SourceDisplayProjection? SourceProjection => _sourceText;

    internal RichTextDisplayProjection WithSourceText(SourceDisplayProjection projection) =>
        new(Reservations.Select(item => item with { SourcePosition = projection.ToSource(item.SourcePosition) }))
        { _sourceText = projection };

    private RichTextDisplayProjection WithoutSourceText() => _sourceText is null ? this :
        new(Reservations.Select(item => item with { SourcePosition = _sourceText.ToDisplay(item.SourcePosition) }));
}
