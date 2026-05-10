// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using Brush = SixLabors.ImageSharp.Drawing.Processing.Brush;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using FontStyle = SixLabors.Fonts.FontStyle;
using Pen = SixLabors.ImageSharp.Drawing.Processing.Pen;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using RectangleF = SixLabors.ImageSharp.RectangleF;
using Size = SixLabors.ImageSharp.Size;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Small custom rich text editor drawn directly into a WebGPU canvas.
///
/// The sample intentionally keeps the editor model simple: the backing text is a
/// single string, style ranges are stored in source grapheme coordinates, and all
/// visual behavior is delegated back to the Fonts layout result. That means text
/// editing stays source-oriented while caret movement, selection rectangles,
/// wrapping, bidi reordering, and hit testing are exercised through the public
/// interaction APIs.
/// </summary>
internal sealed class RichTextEditorScene : RenderScene
{
    private const float EditorMargin = 36F;
    private const float EditorPadding = 28F;
    private const float MinimumFontSize = 16F;
    private const float MaximumFontSize = 72F;

    private static readonly Color BackgroundColor = Color.ParseHex("#101820");
    private static readonly Color EditorColor = Color.ParseHex("#F7FAFC");
    private static readonly Color BorderColor = Color.ParseHex("#9AA7B2");
    private static readonly Color SelectionColor = Color.ParseHex("#D6EAFF");
    private static readonly Color SelectionBorderColor = Color.ParseHex("#8ABDEB");
    private static readonly Color CaretColor = Color.ParseHex("#FF5A1F");
    private static readonly Color GuideColor = Color.ParseHex("#D6DEE6");
    private static readonly EditorStyle DefaultStyle = new(FontStyle.Regular, TextDecorations.None, Color.ParseHex("#17212B"), 30F);
    private static readonly Brush BackgroundBrush = Brushes.Solid(BackgroundColor);
    private static readonly Brush EditorBrush = Brushes.Solid(EditorColor);
    private static readonly Brush SelectionBrush = Brushes.Solid(SelectionColor);
    private static readonly Brush DefaultTextBrush = Brushes.Solid(DefaultStyle.Fill);
    private static readonly Pen BorderPen = Pens.Solid(BorderColor, 1.5F);
    private static readonly Pen SelectionBorderPen = Pens.Solid(SelectionBorderColor, 1.5F);
    private static readonly Pen CaretPen = Pens.Solid(CaretColor, 3F);
    private static readonly Pen SecondaryCaretPen = Pens.Solid(CaretColor, 1.5F);
    private static readonly Pen GuidePen = Pens.Solid(GuideColor, 1F);

    private readonly Dictionary<FontKey, Font> fontCache = [];

    // Style runs use source grapheme ranges, not UTF-16 indices. The Fonts APIs
    // expose grapheme indices for interaction, so the editor can apply formatting
    // without splitting surrogate pairs, combining sequences, or emoji clusters.
    private readonly List<StyleRun> runs = [];
    private readonly List<RichTextRun> richTextRuns = [];

    private FontFamily fontFamily;

    private string text =
        "ImageSharp.Drawing\n" +
        "A focused editor surface can draw text, selection, and caret from the same measured layout.";

    private int caretIndex;
    private int selectionAnchorIndex;
    private bool draggingSelection;
    private EditorStyle currentStyle = DefaultStyle;
    private TextMetrics? lastMetrics;
    private RectangleF lastEditorBounds;
    private PointF lastTextOrigin;
    private RichTextOptions? cachedTextOptions;
    private CaretPosition caret;
    private CaretPosition selectionAnchor;
    private bool hasLayout;
    private bool metricsDirty = true;
    private bool textOptionsDirty = true;
    private bool caretGeometryDirty = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextEditorScene"/> class.
    /// </summary>
    public RichTextEditorScene()
    {
        FontFamily family = SystemFonts.Collection.Families.FirstOrDefault();
        this.fontFamily = family.Name is null
            ? SystemFonts.CreateFont(SystemFonts.Families.First().Name, DefaultStyle.Size, FontStyle.Regular).Family
            : family;

        // Seed the whole document with a default style first, then layer a couple
        // of visible ranges on top so the sample starts with rich text content.
        int graphemeCount = this.text.GetGraphemeCount();
        this.runs.Add(new StyleRun(0, graphemeCount, DefaultStyle));

        // Convert from UTF-16 string positions into grapheme indices at the API
        // boundary. All editing and formatting after this point stays grapheme-based.
        this.SetStyleRange(
            0,
            this.GetGraphemeIndex(this.text.IndexOf('\n')),
            static _ => new EditorStyle(
                FontStyle.Bold,
                TextDecorations.None,
                Color.ParseHex("#145DA0"),
                40F));

        int measuredStart = this.text.IndexOf("measured", StringComparison.Ordinal);
        int measuredEnd = measuredStart + "measured layout".Length;

        // This second range demonstrates that decoration, color, and font size
        // can all be changed independently on arbitrary grapheme-aligned spans.
        this.SetStyleRange(
            this.GetGraphemeIndex(measuredStart),
            this.GetGraphemeIndex(measuredEnd),
            static style => new EditorStyle(
                style.FontStyle,
                style.Decorations | TextDecorations.Underline,
                Color.ParseHex("#B33A3A"),
                style.Size));
    }

    /// <inheritdoc />
    public override string DisplayName => "Rich Text Editor";

    /// <summary>
    /// Gets the active font family name.
    /// </summary>
    public string FontFamilyName => this.fontFamily.Name;

    /// <summary>
    /// Gets a value indicating whether bold is active for new text or the current selection.
    /// </summary>
    public bool IsBold => (this.currentStyle.FontStyle & FontStyle.Bold) == FontStyle.Bold;

    /// <summary>
    /// Gets a value indicating whether italic is active for new text or the current selection.
    /// </summary>
    public bool IsItalic => (this.currentStyle.FontStyle & FontStyle.Italic) == FontStyle.Italic;

    /// <summary>
    /// Gets a value indicating whether underline is active for new text or the current selection.
    /// </summary>
    public bool IsUnderline => (this.currentStyle.Decorations & TextDecorations.Underline) == TextDecorations.Underline;

    /// <summary>
    /// Gets a value indicating whether strikeout is active for new text or the current selection.
    /// </summary>
    public bool IsStrikeout => (this.currentStyle.Decorations & TextDecorations.Strikeout) == TextDecorations.Strikeout;

    /// <summary>
    /// Gets the number of selected graphemes.
    /// </summary>
    public int SelectionLength => Math.Abs(this.caretIndex - this.selectionAnchorIndex);

    /// <summary>
    /// Gets the active font size for new text or the current selection.
    /// </summary>
    public float CurrentFontSize => this.currentStyle.Size;

    /// <inheritdoc />
    public override void Paint(DrawingCanvas canvas, TimeSpan deltaTime)
    {
        Size viewportSize = canvas.Bounds.Size;
        RectangleF editorBounds = CreateEditorBounds(viewportSize);
        bool boundsChanged = !this.hasLayout || !this.lastEditorBounds.Equals(editorBounds);
        RichTextOptions textOptions = this.CreateTextOptions(editorBounds, boundsChanged);
        bool layoutChanged = boundsChanged || this.metricsDirty || this.lastMetrics is null;

        // Measurement is the authoritative editor layout. The same metrics object
        // drives drawing, hit testing, caret navigation, and selection geometry so
        // the sample never has to reproduce layout decisions itself. Reuse the
        // cached metrics while only the caret or selection changes.
        TextMetrics metrics = layoutChanged
            ? canvas.MeasureText(textOptions, this.text)
            : this.lastMetrics!;

        // Input events arrive between frames. Cache the most recent layout so mouse
        // hit testing and keyboard navigation can use the exact geometry that was
        // drawn for the current viewport.
        this.lastMetrics = metrics;
        this.lastEditorBounds = editorBounds;
        this.lastTextOrigin = textOptions.Origin;
        this.hasLayout = true;
        this.metricsDirty = false;

        if (this.text.Length == 0)
        {
            this.caretIndex = 0;
            this.selectionAnchorIndex = 0;
        }
        else
        {
            if (layoutChanged || this.caretGeometryDirty)
            {
                // The text model stores source grapheme insertion indices because that is what editing needs.
                // Geometry is refreshed only when layout is stale. Rebuilding after every paint would discard
                // the preserved visual column used for repeated LineUp/LineDown navigation.
                this.caret = this.GetCaretAtGraphemeIndex(metrics, this.caretIndex);
                this.selectionAnchor = this.GetCaretAtGraphemeIndex(metrics, this.selectionAnchorIndex);
                this.caretGeometryDirty = false;
            }
        }

        canvas.Fill(BackgroundBrush, canvas.Bounds);
        canvas.Fill(EditorBrush, new RectangularPolygon(editorBounds));
        canvas.Draw(BorderPen, new RectangularPolygon(editorBounds));

        IPath editorClip = new RectangularPolygon(editorBounds);

        // Selection is painted before glyphs, matching normal editor behavior.
        // The clipping scope applies only to text so the editor chrome remains crisp.
        this.DrawSelection(canvas, metrics);
        canvas.Save(new DrawingOptions(), editorClip);
        canvas.DrawText(textOptions, this.text, DefaultTextBrush, pen: null);
        canvas.Restore();
        this.DrawCaret(canvas);

        canvas.DrawLine(
            GuidePen,
            new PointF(editorBounds.Left + EditorPadding, editorBounds.Top + EditorPadding - 8F),
            new PointF(editorBounds.Right - EditorPadding, editorBounds.Top + EditorPadding - 8F));
    }

    /// <summary>
    /// Handles a key-down message from the host control.
    /// </summary>
    /// <param name="e">The WinForms key event.</param>
    /// <returns><see langword="true"/> when the key changed editor state.</returns>
    public bool OnKeyDown(KeyEventArgs e)
    {
        // Keyboard navigation is intentionally routed through CaretMovement rather
        // than manipulating indices directly. That keeps line wrapping, bidi jumps,
        // word movement, and vertical x-position preservation inside TextMetrics.
        switch (e.KeyCode)
        {
            case Keys.Left:
                return this.MoveCaret(e.Control ? CaretMovement.PreviousWord : CaretMovement.Previous, e.Shift);
            case Keys.Right:
                return this.MoveCaret(e.Control ? CaretMovement.NextWord : CaretMovement.Next, e.Shift);
            case Keys.Up:
                return this.MoveCaret(CaretMovement.LineUp, e.Shift);
            case Keys.Down:
                return this.MoveCaret(CaretMovement.LineDown, e.Shift);
            case Keys.Home:
                return this.MoveCaret(e.Control ? CaretMovement.TextStart : CaretMovement.LineStart, e.Shift);
            case Keys.End:
                return this.MoveCaret(e.Control ? CaretMovement.TextEnd : CaretMovement.LineEnd, e.Shift);
            case Keys.Back:
                this.Backspace();
                return true;
            case Keys.Delete:
                this.Delete();
                return true;
            case Keys.Enter:
                this.InsertText("\n");
                return true;
        }

        return e.Control && this.HandleControlKey(e.KeyCode);
    }

    /// <summary>
    /// Handles a printable character from the host control.
    /// </summary>
    /// <param name="keyChar">The character generated by the keyboard layout.</param>
    /// <returns><see langword="true"/> when the character changed editor state.</returns>
    public bool OnKeyPress(char keyChar)
    {
        if (char.IsControl(keyChar))
        {
            return false;
        }

        this.InsertText(keyChar.ToString());
        return true;
    }

    /// <inheritdoc />
    public override void OnMouseDown(MouseEventArgs e)
    {
        this.BeginSelection(e.X, e.Y);
        this.draggingSelection = e.Button == MouseButtons.Left;
    }

    /// <inheritdoc />
    public override void OnMouseMove(MouseEventArgs e)
    {
        if (!this.draggingSelection && (e.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        this.draggingSelection = true;
        this.ExtendSelection(e.X, e.Y);
    }

    /// <inheritdoc />
    public override void OnMouseUp(MouseEventArgs e) => this.draggingSelection = false;

    /// <summary>
    /// Starts a pointer selection operation at the supplied control coordinates.
    /// </summary>
    /// <param name="x">The pointer x coordinate.</param>
    /// <param name="y">The pointer y coordinate.</param>
    public void BeginSelection(int x, int y)
    {
        if (this.text.Length == 0)
        {
            // With no shaped text there is nothing to hit test. Keep the model in
            // the one legal insertion state so the next printable key can insert.
            this.caretIndex = 0;
            this.selectionAnchorIndex = 0;
            this.draggingSelection = true;
            return;
        }

        CaretPosition hitCaret = this.HitTest(x, y);
        this.caret = hitCaret;
        this.selectionAnchor = hitCaret;

        // Persist the source grapheme index from the hit result. The actual caret
        // line is refreshed during Paint so it stays correct after resize/reflow.
        this.caretIndex = hitCaret.GraphemeIndex;
        this.selectionAnchorIndex = hitCaret.GraphemeIndex;
        this.draggingSelection = true;
        this.currentStyle = this.GetStyleAt(hitCaret.GraphemeIndex);
        this.caretGeometryDirty = false;
    }

    /// <summary>
    /// Extends the active pointer selection to the supplied control coordinates.
    /// </summary>
    /// <param name="x">The pointer x coordinate.</param>
    /// <param name="y">The pointer y coordinate.</param>
    public void ExtendSelection(int x, int y)
    {
        if (this.text.Length == 0)
        {
            // Dragging through an empty editor should remain a collapsed selection.
            this.caretIndex = 0;
            return;
        }

        CaretPosition hitCaret = this.HitTest(x, y);
        this.caret = hitCaret;

        // Only the focus end moves while dragging. The anchor stays where the drag
        // started, which lets TextMetrics build the visual selection fragments.
        this.caretIndex = hitCaret.GraphemeIndex;
        this.currentStyle = this.GetStyleAt(hitCaret.GraphemeIndex);
        this.caretGeometryDirty = false;
    }

    /// <summary>
    /// Ends the active pointer selection operation.
    /// </summary>
    public void EndSelection() => this.draggingSelection = false;

    /// <summary>
    /// Toggles bold on the current selection or insertion style.
    /// </summary>
    public void ToggleBold()
        => this.ApplyStyle(static style => new EditorStyle(
            ToggleFontStyle(style.FontStyle, FontStyle.Bold),
            style.Decorations,
            style.Fill,
            style.Size));

    /// <summary>
    /// Toggles italic on the current selection or insertion style.
    /// </summary>
    public void ToggleItalic()
        => this.ApplyStyle(static style => new EditorStyle(
            ToggleFontStyle(style.FontStyle, FontStyle.Italic),
            style.Decorations,
            style.Fill,
            style.Size));

    /// <summary>
    /// Toggles underline on the current selection or insertion style.
    /// </summary>
    public void ToggleUnderline()
        => this.ApplyStyle(static style => new EditorStyle(
            style.FontStyle,
            ToggleDecoration(style.Decorations, TextDecorations.Underline),
            style.Fill,
            style.Size));

    /// <summary>
    /// Toggles strikeout on the current selection or insertion style.
    /// </summary>
    public void ToggleStrikeout()
        => this.ApplyStyle(static style => new EditorStyle(
            style.FontStyle,
            ToggleDecoration(style.Decorations, TextDecorations.Strikeout),
            style.Fill,
            style.Size));

    /// <summary>
    /// Applies the supplied text fill color to the current selection or insertion style.
    /// </summary>
    /// <param name="color">The color to apply.</param>
    public void SetFillColor(Color color)
        => this.ApplyStyle(style => new EditorStyle(style.FontStyle, style.Decorations, color, style.Size));

    /// <summary>
    /// Changes the font size for the current selection or insertion style.
    /// </summary>
    /// <param name="delta">The point-size delta.</param>
    public void ChangeFontSize(float delta)
        => this.ApplyStyle(style => new EditorStyle(
            style.FontStyle,
            style.Decorations,
            style.Fill,
            Math.Clamp(style.Size + delta, MinimumFontSize, MaximumFontSize)));

    /// <summary>
    /// Sets the font family used by the editor.
    /// </summary>
    /// <param name="name">The font family name.</param>
    public void SetFontFamily(string name)
    {
        if (!SystemFonts.Collection.TryGet(name, out FontFamily family))
        {
            return;
        }

        this.fontFamily = family;
        this.fontCache.Clear();
        this.metricsDirty = true;
        this.textOptionsDirty = true;
        this.caretGeometryDirty = true;
    }

    private bool HandleControlKey(Keys keyCode)
    {
        switch (keyCode)
        {
            case Keys.A:
                this.selectionAnchorIndex = 0;
                this.caretIndex = this.text.GetGraphemeCount();
                this.currentStyle = this.GetStyleAt(0);
                this.caretGeometryDirty = true;
                return true;
            case Keys.B:
                this.ToggleBold();
                return true;
            case Keys.I:
                this.ToggleItalic();
                return true;
            case Keys.U:
                this.ToggleUnderline();
                return true;
            case Keys.S:
                this.ToggleStrikeout();
                return true;
        }

        return false;
    }

    private RichTextOptions CreateTextOptions(RectangleF editorBounds, bool boundsChanged)
    {
        // RichTextOptions owns the drawing runs, fonts, and paint objects used by
        // both measurement and drawing. Reuse it while only caret/selection state
        // changes; rebuild it when text styling or the editor rectangle changes.
        if (!this.textOptionsDirty && !boundsChanged && this.cachedTextOptions is not null)
        {
            return this.cachedTextOptions;
        }

        float wrappingLength = Math.Max(1F, editorBounds.Width - (EditorPadding * 2F));
        PointF origin = new(editorBounds.Left + EditorPadding, editorBounds.Top + EditorPadding);

        // TextInteractionMode.Editor keeps ordinary trailing spaces addressable so
        // typing at the end of a wrapped line behaves like a real text editor.
        // Paragraph layout can trim that whitespace, but editor layout needs it
        // available for caret movement, selection, and subsequent insertion.
        this.cachedTextOptions = new RichTextOptions(this.GetFont(DefaultStyle))
        {
            Origin = origin,
            WrappingLength = wrappingLength,
            LineSpacing = 1.25F,
            TextDirection = TextDirection.LeftToRight,
            TextInteractionMode = TextInteractionMode.Editor,
            TextRuns = this.BuildTextRuns(),
        };

        this.textOptionsDirty = false;

        return this.cachedTextOptions;
    }

    private List<RichTextRun> BuildTextRuns()
    {
        this.richTextRuns.Clear();

        if (this.text.Length == 0)
        {
            // Text runs describe spans in existing text. Empty editor content is
            // represented by the insertion caret alone, not by a synthetic run.
            return this.richTextRuns;
        }

        foreach (StyleRun run in this.runs)
        {
            if (run.End <= run.Start)
            {
                continue;
            }

            // Each RichTextRun maps one editor style span to the drawing pipeline.
            // The run range remains in grapheme units; the Fonts layer performs the
            // necessary mapping back to shaped glyphs and UTF-16 source positions.
            this.richTextRuns.Add(new RichTextRun
            {
                Start = run.Start,
                End = run.End,
                Font = this.GetFont(run.Style),
                Brush = Brushes.Solid(run.Style.Fill),
                TextDecorations = run.Style.Decorations,
            });
        }

        return this.richTextRuns;
    }

    private void DrawSelection(DrawingCanvas canvas, TextMetrics metrics)
    {
        if (!this.HasSelection)
        {
            return;
        }

        ReadOnlySpan<FontRectangle> bounds = metrics.GetSelectionBounds(this.selectionAnchor, this.caret).Span;
        foreach (FontRectangle rectangle in bounds)
        {
            float width = Math.Max(2F, rectangle.Width);
            float height = Math.Max(2F, rectangle.Height);

            // Selection rectangles come from the same interaction API as hit testing and caret movement.
            // That keeps mixed-bidi ranges visually split instead of filling across reordered gaps.
            canvas.Fill(
                SelectionBrush,
                new RectangularPolygon(rectangle.X, rectangle.Y, width, height));

            canvas.Draw(
                SelectionBorderPen,
                new RectangularPolygon(rectangle.X, rectangle.Y, width, height));
        }
    }

    private void DrawCaret(DrawingCanvas canvas)
    {
        if (this.HasSelection)
        {
            return;
        }

        if (this.text.Length == 0)
        {
            float caretHeight = DefaultStyle.Size * 1.25F;

            // Empty text has no layout metrics, but the editor still needs an insertion
            // caret at the same origin where the first typed grapheme will be measured.
            canvas.DrawLine(
                CaretPen,
                this.lastTextOrigin,
                new PointF(this.lastTextOrigin.X, this.lastTextOrigin.Y + caretHeight));

            return;
        }

        canvas.DrawLine(
            CaretPen,
            new PointF(this.caret.Start.X, this.caret.Start.Y),
            new PointF(this.caret.End.X, this.caret.End.Y));

        if (this.caret.HasSecondary)
        {
            canvas.DrawLine(
                SecondaryCaretPen,
                new PointF(this.caret.SecondaryStart.X, this.caret.SecondaryStart.Y),
                new PointF(this.caret.SecondaryEnd.X, this.caret.SecondaryEnd.Y));
        }
    }

    private CaretPosition HitTest(float x, float y)
    {
        if (!this.hasLayout || this.lastMetrics is null || this.text.Length == 0)
        {
            return this.caret;
        }

        // HitTest returns a TextHit so ambiguous bidi positions can be represented.
        // Converting that hit through GetCaretPosition gives the editor the primary
        // and secondary caret geometry for the exact visual position clicked.
        TextHit hit = this.lastMetrics.HitTest(new Vector2(x, y));
        return this.lastMetrics.GetCaretPosition(hit);
    }

    private bool MoveCaret(CaretMovement movement, bool extendSelection)
    {
        if (!this.hasLayout || this.lastMetrics is null)
        {
            return false;
        }

        // Movement is delegated to TextMetrics because it owns the bidi, wrapping,
        // word-boundary, and vertical navigation rules for the current layout.
        CaretPosition moved = this.lastMetrics.MoveCaret(this.caret, movement);
        this.caret = moved;
        this.caretIndex = moved.GraphemeIndex;
        this.caretGeometryDirty = false;

        if (!extendSelection)
        {
            this.selectionAnchor = moved;
            this.selectionAnchorIndex = moved.GraphemeIndex;
        }

        this.currentStyle = this.GetStyleAt(moved.GraphemeIndex);
        return true;
    }

    private CaretPosition GetCaretAtGraphemeIndex(TextMetrics metrics, int graphemeIndex)
    {
        int target = Math.Clamp(graphemeIndex, 0, this.text.GetGraphemeCount());
        CaretPosition resolved = metrics.GetCaret(CaretPlacement.Start);

        // The editor model needs source indices for insertion/deletion, while the layout API owns
        // visual geometry. Walk through MoveCaret so bidi and line wrapping stay inside the layout layer.
        while (resolved.GraphemeIndex < target)
        {
            CaretPosition next = metrics.MoveCaret(resolved, CaretMovement.Next);
            if (next.GraphemeIndex == resolved.GraphemeIndex)
            {
                break;
            }

            resolved = next;
        }

        return resolved;
    }

    private void Backspace()
    {
        if (this.HasSelection)
        {
            this.DeleteSelection();
            return;
        }

        if (!this.hasLayout || this.lastMetrics is null)
        {
            return;
        }

        CaretPosition previous = this.lastMetrics.MoveCaret(this.caret, CaretMovement.Previous);
        if (previous.GraphemeIndex == this.caretIndex)
        {
            return;
        }

        this.DeleteRange(previous.GraphemeIndex, this.caretIndex);
        this.caretIndex = previous.GraphemeIndex;
        this.selectionAnchorIndex = previous.GraphemeIndex;
        this.currentStyle = this.GetStyleAt(previous.GraphemeIndex);
    }

    private void Delete()
    {
        if (this.HasSelection)
        {
            this.DeleteSelection();
            return;
        }

        if (!this.hasLayout || this.lastMetrics is null)
        {
            return;
        }

        CaretPosition next = this.lastMetrics.MoveCaret(this.caret, CaretMovement.Next);
        if (next.GraphemeIndex == this.caretIndex)
        {
            return;
        }

        this.DeleteRange(this.caretIndex, next.GraphemeIndex);
        this.currentStyle = this.GetStyleAt(this.caretIndex);
    }

    private void InsertText(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        // Insertions replace the current selection first. The final insertion point
        // is recalculated after deletion because deleting text shifts every later
        // style run and source grapheme index.
        int insertionIndex = Math.Clamp(this.SelectionStart, 0, this.text.GetGraphemeCount());
        if (this.HasSelection)
        {
            this.DeleteSelection();
            insertionIndex = Math.Clamp(this.caretIndex, 0, this.text.GetGraphemeCount());
        }

        int stringIndex = this.GetStringIndex(insertionIndex);
        int graphemeLength = value.GetGraphemeCount();
        this.text = this.text.Insert(stringIndex, value);

        // New text inherits the active style. The run table is then normalized so
        // adjacent equal styles merge back into a single span.
        this.InsertRun(insertionIndex, graphemeLength, this.currentStyle);

        this.caretIndex = insertionIndex + graphemeLength;
        this.selectionAnchorIndex = this.caretIndex;
        this.caretGeometryDirty = true;
    }

    private void InsertRun(int index, int length, EditorStyle style)
    {
        List<StyleRun> updated = new(this.runs.Count + 1);
        bool inserted = false;
        foreach (StyleRun run in this.runs)
        {
            if (run.End <= index)
            {
                updated.Add(run);
                continue;
            }

            if (run.Start >= index)
            {
                if (!inserted)
                {
                    updated.Add(new StyleRun(index, index + length, style));
                    inserted = true;
                }

                updated.Add(run.Shift(length));
                continue;
            }

            // Inserting inside an existing style range splits that range around the
            // inserted text so the original formatting is preserved on both sides.
            updated.Add(new StyleRun(run.Start, index, run.Style));
            updated.Add(new StyleRun(index, index + length, style));
            updated.Add(new StyleRun(index + length, run.End + length, run.Style));
            inserted = true;
        }

        if (!inserted)
        {
            updated.Add(new StyleRun(index, index + length, style));
        }

        this.runs.Clear();
        this.runs.AddRange(updated);
        this.NormalizeRuns();
    }

    private void DeleteSelection()
    {
        int graphemeCount = this.text.GetGraphemeCount();
        int start = Math.Clamp(this.SelectionStart, 0, graphemeCount);
        int end = Math.Clamp(this.SelectionEnd, 0, graphemeCount);
        this.DeleteRange(start, end);
        this.caretIndex = start;
        this.selectionAnchorIndex = start;
        this.currentStyle = this.GetStyleAt(start);
        this.caretGeometryDirty = true;
    }

    private void DeleteRange(int start, int end)
    {
        int length = end - start;
        int stringStart = this.GetStringIndex(start);
        int stringEnd = this.GetStringIndex(end);
        this.text = this.text.Remove(stringStart, stringEnd - stringStart);

        List<StyleRun> updated = new(this.runs.Count);
        foreach (StyleRun run in this.runs)
        {
            if (run.End <= start)
            {
                updated.Add(run);
                continue;
            }

            if (run.Start >= end)
            {
                updated.Add(run.Shift(-length));
                continue;
            }

            // A partially deleted style range keeps only the pieces that still have
            // backing text. Later ranges shift left by the removed grapheme count.
            if (run.Start < start)
            {
                updated.Add(new StyleRun(run.Start, start, run.Style));
            }

            if (run.End > end)
            {
                updated.Add(new StyleRun(start, run.End - length, run.Style));
            }
        }

        this.runs.Clear();
        this.runs.AddRange(updated);
        this.NormalizeRuns();
        this.caretGeometryDirty = true;
    }

    private void ApplyStyle(Func<EditorStyle, EditorStyle> update)
    {
        if (!this.HasSelection)
        {
            // A collapsed selection changes the insertion style. The style will be
            // materialized into a run only when the user actually types text.
            this.currentStyle = update(this.currentStyle);
            return;
        }

        // A non-empty selection rewrites the covered source grapheme range while
        // preserving any unaffected pieces of the surrounding style runs.
        this.SetStyleRange(this.SelectionStart, this.SelectionEnd, update);
        this.currentStyle = this.GetStyleAt(this.SelectionStart);
        this.caretGeometryDirty = true;
    }

    private void SetStyleRange(int start, int end, Func<EditorStyle, EditorStyle> update)
    {
        if (end <= start)
        {
            return;
        }

        List<StyleRun> updated = new(this.runs.Count + 2);
        foreach (StyleRun run in this.runs)
        {
            if (run.End <= start || run.Start >= end)
            {
                updated.Add(run);
                continue;
            }

            if (run.Start < start)
            {
                updated.Add(new StyleRun(run.Start, start, run.Style));
            }

            int overlapStart = Math.Max(run.Start, start);
            int overlapEnd = Math.Min(run.End, end);
            updated.Add(new StyleRun(overlapStart, overlapEnd, update(run.Style)));

            if (run.End > end)
            {
                updated.Add(new StyleRun(end, run.End, run.Style));
            }
        }

        this.runs.Clear();
        this.runs.AddRange(updated);
        this.NormalizeRuns();
    }

    private void NormalizeRuns()
    {
        this.runs.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        // Empty runs can appear after selection styling or deletion. They carry no
        // drawable text and would otherwise produce invalid RichTextRun ranges.
        for (int i = this.runs.Count - 1; i >= 0; i--)
        {
            if (this.runs[i].End <= this.runs[i].Start)
            {
                this.runs.RemoveAt(i);
            }
        }

        // Keep the source run table compact so invalidating the cached layout has
        // less work to do when the next paint rebuilds RichTextRun entries.
        for (int i = 1; i < this.runs.Count; i++)
        {
            StyleRun previous = this.runs[i - 1];
            StyleRun current = this.runs[i];
            if (previous.End == current.Start && previous.Style.Equals(current.Style))
            {
                this.runs[i - 1] = new StyleRun(previous.Start, current.End, previous.Style);
                this.runs.RemoveAt(i);
                i--;
            }
        }

        this.metricsDirty = true;
        this.textOptionsDirty = true;
    }

    private EditorStyle GetStyleAt(int graphemeIndex)
    {
        if (this.text.Length == 0 || this.runs.Count == 0)
        {
            return this.currentStyle;
        }

        int probe = Math.Clamp(graphemeIndex, 0, this.text.GetGraphemeCount() - 1);
        foreach (StyleRun run in this.runs)
        {
            if (probe >= run.Start && probe < run.End)
            {
                return run.Style;
            }
        }

        return this.runs[^1].Style;
    }

    private Font GetFont(EditorStyle style)
    {
        FontKey key = new(style.Size, style.FontStyle);
        if (!this.fontCache.TryGetValue(key, out Font? font))
        {
            font = this.fontFamily.CreateFont(style.Size, style.FontStyle);
            this.fontCache.Add(key, font);
        }

        return font;
    }

    private int GetStringIndex(int graphemeIndex)
    {
        if (graphemeIndex <= 0)
        {
            return 0;
        }

        // Editing APIs in this sample use grapheme indices, but string mutation
        // still requires UTF-16 offsets. Enumerating graphemes at this boundary
        // prevents insertion and deletion from splitting a user-perceived character.
        int stringIndex = 0;
        int currentGraphemeIndex = 0;
        SpanGraphemeEnumerator enumerator = new(this.text);
        while (enumerator.MoveNext())
        {
            if (currentGraphemeIndex == graphemeIndex)
            {
                return stringIndex;
            }

            stringIndex += enumerator.Current.Utf16Length;
            currentGraphemeIndex++;
        }

        return this.text.Length;
    }

    private int GetGraphemeIndex(int stringIndex)
    {
        // Initial style setup starts from ordinary string search results. Convert
        // those UTF-16 offsets once so the rest of the editor remains grapheme-based.
        int current = 0;
        int graphemeIndex = 0;
        SpanGraphemeEnumerator enumerator = new(this.text);
        while (enumerator.MoveNext())
        {
            if (current >= stringIndex)
            {
                return graphemeIndex;
            }

            current += enumerator.Current.Utf16Length;
            graphemeIndex++;
        }

        return graphemeIndex;
    }

    private bool HasSelection => this.caretIndex != this.selectionAnchorIndex;

    private int SelectionStart => Math.Min(this.caretIndex, this.selectionAnchorIndex);

    private int SelectionEnd => Math.Max(this.caretIndex, this.selectionAnchorIndex);

    private static RectangleF CreateEditorBounds(Size viewportSize)
    {
        float width = Math.Max(1F, viewportSize.Width - (EditorMargin * 2F));
        float height = Math.Max(1F, viewportSize.Height - (EditorMargin * 2F));

        return new RectangleF(EditorMargin, EditorMargin, width, height);
    }

    private static FontStyle ToggleFontStyle(FontStyle current, FontStyle flag)
        => (current & flag) == flag ? current & ~flag : current | flag;

    private static TextDecorations ToggleDecoration(TextDecorations current, TextDecorations flag)
        => (current & flag) == flag ? current & ~flag : current | flag;

    private readonly struct FontKey : IEquatable<FontKey>
    {
        public FontKey(float size, FontStyle style)
        {
            this.Size = size;
            this.Style = style;
        }

        public float Size { get; }

        public FontStyle Style { get; }

        public bool Equals(FontKey other)
            => this.Size.Equals(other.Size) && this.Style == other.Style;

        public override bool Equals(object? obj)
            => obj is FontKey other && this.Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(this.Size, this.Style);
    }

    private readonly struct EditorStyle : IEquatable<EditorStyle>
    {
        public EditorStyle(FontStyle fontStyle, TextDecorations decorations, Color fill, float size)
        {
            this.FontStyle = fontStyle;
            this.Decorations = decorations;
            this.Fill = fill;
            this.Size = size;
        }

        public FontStyle FontStyle { get; }

        public TextDecorations Decorations { get; }

        public Color Fill { get; }

        public float Size { get; }

        public bool Equals(EditorStyle other)
            => this.FontStyle == other.FontStyle
            && this.Decorations == other.Decorations
            && this.Fill.Equals(other.Fill)
            && this.Size.Equals(other.Size);

        public override bool Equals(object? obj)
            => obj is EditorStyle other && this.Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(this.FontStyle, this.Decorations, this.Fill, this.Size);
    }

    private readonly struct StyleRun
    {
        public StyleRun(int start, int end, EditorStyle style)
        {
            this.Start = start;
            this.End = end;
            this.Style = style;
        }

        public int Start { get; }

        public int End { get; }

        public EditorStyle Style { get; }

        public StyleRun Shift(int delta) => new(this.Start + delta, this.End + delta, this.Style);
    }
}
