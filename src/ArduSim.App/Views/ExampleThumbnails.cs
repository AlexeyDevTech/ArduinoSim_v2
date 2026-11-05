using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>
/// Scene thumbnails for the examples gallery (docs/UI.md §2.7): each example's scene drawn by an
/// offscreen <see cref="SchematicCanvas"/>, one per idle slot so startup is not held up, again after a theme change.
/// </summary>
public static class ExampleThumbnails
{
    public const double Width = 154;
    public const double Height = 96;
    public const double TileHeight = 58;

    public static void Render(Visual owner, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(viewModel);
        double dpiScale = VisualTreeHelper.GetDpi(owner).DpiScaleX;
        foreach (BoardTile tile in viewModel.BoardTiles)
        {
            _ = owner.Dispatcher.BeginInvoke(
                () =>
                {
                    if (viewModel.PreviewBoard(tile.Board) is { } scene)
                    {
                        tile.Thumbnail = SchematicCanvas.Snapshot(scene, Width, TileHeight, dpiScale);
                    }
                },
                DispatcherPriority.ApplicationIdle);
        }

        foreach (ExampleItem example in viewModel.Examples)
        {
            _ = owner.Dispatcher.BeginInvoke(
                () =>
                {
                    if (viewModel.PreviewScene(example) is { } scene)
                    {
                        example.Thumbnail = SchematicCanvas.Snapshot(scene, Width, Height, dpiScale);
                    }
                },
                DispatcherPriority.ApplicationIdle);
        }
    }
}
