using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>
/// A symbol's five by eight dots: small in a list, large and editable in the right panel (docs/UI.md §2.10).
/// A press sets or clears the dot under it, and dragging does the same to the dots it passes.
/// </summary>
public sealed class GlyphView : FrameworkElement
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows),
        typeof(IReadOnlyList<byte>),
        typeof(GlyphView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _painting;
    private bool _paintOn;
    private (int Column, int Row) _last = (-1, -1);

    public GlyphView()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Theme.Changed += InvalidateVisual;
    }

    public IReadOnlyList<byte>? Rows
    {
        get => (IReadOnlyList<byte>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Dots can be changed here; the view model behind it is told.</summary>
    public bool IsEditable { get; set; }

    private bool IsOn(int column, int row) => Rows is { } rows && row < rows.Count && (rows[row] & (0x10 >> column)) != 0;

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        double dot = Math.Min(ActualWidth / 5, ActualHeight / 8);
        Brush on = (Brush)FindResource(IsEditable ? "Brush.Accent" : "Brush.Text.Primary");
        Brush off = (Brush)FindResource("Brush.Border");
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                drawingContext.DrawRectangle(IsOn(column, row) ? on : off, null, new Rect((column * dot) + 0.5, (row * dot) + 0.5, Math.Max(dot - 1, 1), Math.Max(dot - 1, 1)));
            }
        }
    }

    private (int Column, int Row) DotAt(Point position)
    {
        double dot = Math.Max(Math.Min(ActualWidth / 5, ActualHeight / 8), 1);
        return ((int)Math.Floor(position.X / dot), (int)Math.Floor(position.Y / dot));
    }

    private void Paint((int Column, int Row) at, bool finished)
    {
        if (DataContext is ScreensViewModel model)
        {
            model.SetGlyphDot(at.Column, at.Row, _paintOn, finished);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (!IsEditable)
        {
            return;
        }

        (int column, int row) = DotAt(e.GetPosition(this));
        if (column is < 0 or > 4 || row is < 0 or > 7)
        {
            return;
        }

        _paintOn = !IsOn(column, row);
        _painting = true;
        _last = (column, row);
        CaptureMouse();
        Paint(_last, finished: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_painting && DotAt(e.GetPosition(this)) is var at && at != _last && at.Column is >= 0 and <= 4 && at.Row is >= 0 and <= 7)
        {
            _last = at;
            Paint(at, finished: false);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_painting)
        {
            _painting = false;
            ReleaseMouseCapture();
            Paint(_last, finished: true);
            e.Handled = true;
        }
    }
}
