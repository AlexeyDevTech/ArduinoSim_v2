using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.App.Views;
using ArduSim.Core.Projects;

namespace ArduSim.App;

/// <summary>View glue for the shell: the toolbar's folding stages, the Рядом layout, the explorer's menus and dropped folders.</summary>
public partial class MainWindow
{
    private const int LastToolbarStage = 3;

    // Below this height the bottom panel folds itself until there is output (docs/UI.md §2.1).
    private const double CompactHeight = 760;

    // Below this width the side panels draw at their minimums; the saved widths stay.
    private const double NarrowWidth = 1200;

    // The caption area keeps this much empty for dragging the window.
    private const double MinDragWidth = 24;

    private bool _narrow;
    private double _splitRatio = 0.55;
    private ExplorerEntry? _renaming;
    private string? _newFileFolder;

    // ---- window bounds -------------------------------------------------------------------------

    private void RestoreWindow(WindowBounds? saved)
    {
        Rect work = SystemParameters.WorkArea;
        if (saved is null)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, work.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, work.Height));
            return;
        }

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Width = Math.Clamp(saved.Width, MinWidth, Math.Max(MinWidth, screen.Width));
        Height = Math.Clamp(saved.Height, MinHeight, Math.Max(MinHeight, screen.Height));

        // A monitor may have gone: the title bar must stay reachable.
        if (screen.IntersectsWith(new Rect(saved.Left + 40, saved.Top, Math.Max(1, saved.Width - 80), 48)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
        }

        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private WindowBounds CurrentBounds()
    {
        Rect normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowBounds(normal.Left, normal.Top, normal.Width, normal.Height, WindowState == WindowState.Maximized);
    }

    // ---- toolbar stages ------------------------------------------------------------------------

    private void OnTitleRowSized(object sender, SizeChangedEventArgs e) => UpdateToolbarStage();

    /// <summary>The first stage whose natural width fits: nothing is ever clipped, and the board, Запуск, the clock and the status stay.</summary>
    private void UpdateToolbarStage()
    {
        double available = TitleRow.ActualWidth - CaptionButtons.ActualWidth - MinDragWidth;
        if (available <= 0)
        {
            return;
        }

        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        for (int stage = 0; stage <= LastToolbarStage; stage++)
        {
            ApplyToolbarStage(stage);

            // Measure does not descend into clean ancestors: a label hidden inside a button would not count.
            InvalidateTree(ToolbarLeft);
            InvalidateTree(ToolbarRight);
            ToolbarLeft.Measure(unbounded);
            ToolbarRight.Measure(unbounded);
            if (ToolbarLeft.DesiredSize.Width + ToolbarRight.DesiredSize.Width <= available)
            {
                break;
            }
        }
    }

    private static void InvalidateTree(UIElement element)
    {
        element.InvalidateMeasure();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is UIElement child)
            {
                InvalidateTree(child);
            }
        }
    }

    private void ApplyToolbarStage(int stage)
    {
        Visibility labels = stage == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuildLabel.Visibility = labels;
        PauseLabel.Visibility = labels;
        ResumeLabel.Visibility = labels;
        StopLabel.Visibility = labels;
        PauseButton.MinWidth = stage == 0 ? 108 : 0;
        ProjectNameLabel.MaxWidth = stage switch { 0 => 180, 1 => 140, 2 => 120, _ => 80 };

        StepButtons.Visibility = stage < 2 ? Visibility.Visible : Visibility.Collapsed;
        StepMenuButton.Visibility = stage < 2 ? Visibility.Collapsed : Visibility.Visible;

        bool folded = stage >= LastToolbarStage;
        SoundBox.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        SpeedBox.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        MoreButton.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
        BoardBox.MaxWidth = folded ? 112 : double.PositiveInfinity;
        StatusPill.MinWidth = folded ? 0 : 104;
        StatusPill.MaxWidth = folded ? 124 : double.PositiveInfinity;
        Visibility separators = folded ? Visibility.Collapsed : Visibility.Visible;
        ToolbarSeparator1.Visibility = separators;
        ToolbarSeparator2.Visibility = separators;
        ToolbarSeparator3.Visibility = separators;
    }

    private void OnStepMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = StepMenuButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "loop()", InputGestureText = "F10", Command = _viewModel.StepLoopCommand });
        foreach ((string milliseconds, string gesture) in new[] { ("1", "F11"), ("10", "Shift+F11"), ("100", "Ctrl+F11") })
        {
            menu.Items.Add(new MenuItem { Header = Strings.Get("Toolbar_Step" + milliseconds), InputGestureText = gesture, Command = _viewModel.StepTimeCommand, CommandParameter = milliseconds });
        }

        menu.IsOpen = true;
    }

    private void OnMoreMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        var sound = new MenuItem { Header = Strings.Get("Toolbar_Sound"), IsCheckable = true, IsChecked = _viewModel.SoundOn };
        sound.Click += (_, _) => _viewModel.SoundOn = sound.IsChecked;
        menu.Items.Add(sound);
        menu.Items.Add(new Separator());
        foreach (SpeedOption speed in _viewModel.Speeds)
        {
            var item = new MenuItem { Header = speed.Label, IsCheckable = true, IsChecked = speed == _viewModel.SelectedSpeed };
            item.Click += (_, _) => _viewModel.SelectedSpeed = speed;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>A double click in the list of screens opens that screen.</summary>
    private void OnScreenRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Screens is { } screens && screens.SelectedRow is not null)
        {
            screens.IsFace = true;
        }
    }

    // ---- code beside the schematic -----------------------------------------------------------------

    private void ApplyCenterLayout()
    {
        if (_viewModel.SplitView)
        {
            CanvasColumn.MinWidth = 240;
            CanvasColumn.Width = new GridLength(_splitRatio, GridUnitType.Star);
            SplitColumn.Width = new GridLength(1);
            CodeColumn.MinWidth = 240;
            CodeColumn.Width = new GridLength(1 - _splitRatio, GridUnitType.Star);
            Grid.SetColumn(Editors, 2);
        }
        else
        {
            CanvasColumn.MinWidth = 0;
            CanvasColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitColumn.Width = new GridLength(0);
            CodeColumn.MinWidth = 0;
            CodeColumn.Width = new GridLength(0);
            Grid.SetColumn(Editors, 0);
        }
    }

    private void RememberSplit()
    {
        if (CanvasColumn.ActualWidth > 0 && CodeColumn.ActualWidth > 0)
        {
            _splitRatio = Math.Clamp(CanvasColumn.ActualWidth / (CanvasColumn.ActualWidth + CodeColumn.ActualWidth), 0.15, 0.85);
        }
    }

    private void OnTabsWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = (ScrollViewer)sender;
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnCenterTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CenterTabs.SelectedItem is { } tab)
        {
            CenterTabs.ScrollIntoView(tab);
        }
    }

    /// <summary>A middle click closes a code tab.</summary>
    private void OnCenterTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle
            && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(CenterTabs, source) is ListBoxItem { DataContext: CodeDocumentViewModel document })
        {
            _viewModel.CloseTabCommand.Execute(document);
            e.Handled = true;
        }
    }

    // ---- start view -----------------------------------------------------------------------------

    private void OnExampleSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.ExampleSearch = "";
            e.Handled = true;
        }
    }

    // ---- palette search -----------------------------------------------------------------------------

    private void OnPaletteSearchKey(object sender, KeyEventArgs e)
    {
        PaletteViewModel palette = _viewModel.Palette;
        switch (e.Key)
        {
            case Key.Down:
                palette.MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Up:
                palette.MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.Enter when palette.Highlighted is { } item:
                if (_viewModel.Scene is { IsLocked: true })
                {
                    _viewModel.ShowLockedToast();
                }
                else
                {
                    palette.Search = "";
                    PlacePart(item);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                if (palette.Search.Length > 0)
                {
                    palette.Search = "";
                }
                else if (_viewModel.IsCanvasVisible)
                {
                    Canvas.Focus();
                }

                e.Handled = true;
                break;
        }
    }

    private void OnAddPartRequested(object? sender, EventArgs e) => FocusPaletteSearch();

    /// <summary>Enter on a selected part: Properties opens with the keyboard in its first field.</summary>
    private void OnOpenProperties(object? sender, EventArgs e)
    {
        _viewModel.PropertiesOpen = true;
        _ = Dispatcher.BeginInvoke(
            () => PropertiesBody.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            DispatcherPriority.Input);
    }

    private void FocusPaletteSearch()
    {
        _viewModel.PaletteOpen = true;
        _viewModel.LeftTab = MainViewModel.PartsPage;
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                PaletteSearch.Focus();
                PaletteSearch.SelectAll();
            },
            DispatcherPriority.Input);
    }

    // ---- between panes -----------------------------------------------------------------------------

    /// <summary>Ctrl+F6, Ctrl+Shift+F6: toolbar, left panel, center, properties, bottom panel, around.</summary>
    private void CyclePanes(bool backwards)
    {
        FrameworkElement[] panes = [ToolbarLeft, LeftPanel, Canvas, Editors, PropertiesBody, BottomTabs];
        int current = Array.FindIndex(panes, p => p.IsKeyboardFocusWithin);
        for (int step = 1; step <= panes.Length; step++)
        {
            FrameworkElement next = panes[(current + (backwards ? -step : step) + (panes.Length * 2)) % panes.Length];
            if (!next.IsVisible)
            {
                continue;
            }

            if (next == Canvas ? Canvas.Focus() : next.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
            {
                return;
            }
        }
    }

    // ---- help -------------------------------------------------------------------------------------

    private void OnShowHelp(object? sender, EventArgs e) => ShortcutSheet.Open(this);

    private void OnHelpMenu(object sender, RoutedEventArgs e) => ShortcutSheet.Open(this);

    // ---- project explorer -----------------------------------------------------------------------------

    // Left and right fold and unfold, as in any tree.
    private void OnExplorerRowKey(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExplorerEntry { IsFolder: true } folder } && e.Key is Key.Left or Key.Right)
        {
            _viewModel.SetFolderExpanded(folder, e.Key == Key.Right);
            e.Handled = true;
        }
    }

    private void OnExplorerRowMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExplorerEntry { IsFolder: true } folder } folderRow)
        {
            var folderMenu = new ContextMenu { PlacementTarget = folderRow };
            var newHere = new MenuItem { Header = Strings.Get("Explorer_NewFileHere") };
            newHere.Click += (_, _) => AskFileName(null, folderRow, folder.Path);
            var foldAll = new MenuItem { Header = Strings.Get("Explorer_FoldAll") };
            foldAll.Click += (_, _) => _viewModel.SetAllFoldersExpanded(false);
            var unfoldAll = new MenuItem { Header = Strings.Get("Explorer_UnfoldAll") };
            unfoldAll.Click += (_, _) => _viewModel.SetAllFoldersExpanded(true);
            var revealFolder = new MenuItem { Header = Strings.Get("Explorer_Reveal") };
            revealFolder.Click += (_, _) => MainViewModel.RevealFile(folder);
            folderMenu.Items.Add(newHere);
            folderMenu.Items.Add(new Separator());
            folderMenu.Items.Add(foldAll);
            folderMenu.Items.Add(unfoldAll);
            folderMenu.Items.Add(new Separator());
            folderMenu.Items.Add(revealFolder);
            folderMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement { DataContext: ExplorerEntry { Kind: ExplorerKind.Library } library } libraryRow)
        {
            var libraryMenu = new ContextMenu { PlacementTarget = libraryRow };
            var openHeader = new MenuItem { Header = Strings.Get("Explorer_Open") };
            openHeader.Click += (_, _) => _viewModel.OpenExplorerEntryCommand.Execute(library);
            var revealLibrary = new MenuItem { Header = Strings.Get("Explorer_Reveal") };
            revealLibrary.Click += (_, _) => MainViewModel.RevealFile(library);
            libraryMenu.Items.Add(openHeader);
            libraryMenu.Items.Add(revealLibrary);
            if (library.CanRemove)
            {
                var removeLibrary = new MenuItem { Header = Strings.Get("Explorer_LibraryRemove") };
                removeLibrary.Click += (_, _) => _viewModel.RemoveLibraryCommand.Execute(library);
                libraryMenu.Items.Add(new Separator());
                libraryMenu.Items.Add(removeLibrary);
            }

            libraryMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (sender is not FrameworkElement { DataContext: ExplorerEntry { Kind: ExplorerKind.File } entry } row)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = row };
        var open = new MenuItem { Header = Strings.Get("Explorer_Open") };
        open.Click += (_, _) => _viewModel.OpenExplorerEntryCommand.Execute(entry);
        var rename = new MenuItem { Header = Strings.Get("Explorer_Rename"), IsEnabled = !entry.IsMain, ToolTip = entry.IsMain ? Strings.Get("File_MainFixed") : null };
        rename.Click += (_, _) => AskFileName(entry, row);
        var delete = new MenuItem { Header = Strings.Get("Explorer_Delete"), IsEnabled = !entry.IsMain, ToolTip = entry.IsMain ? Strings.Get("File_MainFixed") : Strings.Get("Explorer_DeleteTip") };
        delete.Click += (_, _) => _viewModel.DeleteSketchFile(entry);
        var reveal = new MenuItem { Header = Strings.Get("Explorer_Reveal") };
        reveal.Click += (_, _) => MainViewModel.RevealFile(entry);
        var besideIt = new MenuItem { Header = Strings.Get("Explorer_NewFileHere") };
        besideIt.Click += (_, _) => AskFileName(null, row, Path.GetDirectoryName(entry.Path));
        menu.Items.Add(open);
        menu.Items.Add(besideIt);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        menu.Items.Add(new Separator());
        menu.Items.Add(reveal);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnNewFile(object sender, RoutedEventArgs e) => AskFileName(null, NewFileButton);

    private void AskFileName(ExplorerEntry? renaming, UIElement anchor, string? inFolder = null)
    {
        _renaming = renaming;
        _newFileFolder = inFolder;
        FileNamePrompt.Text = Strings.Get(renaming is null ? "Explorer_NewFilePrompt" : "Explorer_RenamePrompt");
        FileNameBox.Text = renaming is null ? "" : Path.GetFileName(renaming.Path) ?? "";
        FileNameError.Visibility = Visibility.Collapsed;
        FileNamePopup.PlacementTarget = anchor;
        FileNamePopup.IsOpen = true;
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                FileNameBox.Focus();
                FileNameBox.Select(0, Path.GetFileNameWithoutExtension(FileNameBox.Text).Length);
            },
            DispatcherPriority.Input);
    }

    private void OnFileNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FileNamePopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            string? error = _renaming is { } entry ? _viewModel.RenameSketchFile(entry, FileNameBox.Text) : _viewModel.CreateSketchFile(FileNameBox.Text, _newFileFolder);
            FileNameError.Text = error ?? "";
            FileNameError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            FileNamePopup.IsOpen = error is not null;
            e.Handled = true;
        }
    }

    // ---- a folder or a sketch dropped on the window ------------------------------------------------

    private static string? DroppedProject(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths)
        {
            return null;
        }

        string path = paths[0];
        string name = Path.GetFileName(path);
        return Directory.Exists(path) || name.Equals(ProjectFile.FileName, StringComparison.OrdinalIgnoreCase)
            || name.Equals("platformio.ini", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".ino", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        bool accepted = _viewModel.CanEdit && DroppedProject(e) is not null;
        e.Effects = accepted ? DragDropEffects.Link : DragDropEffects.None;
        DropOverlay.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DroppedProject(e) is { } path)
        {
            // Not inside the drop: the unsaved-changes dialog must not hold Explorer up.
            _ = Dispatcher.BeginInvoke(async () => await _viewModel.OpenDroppedAsync(path));
            e.Handled = true;
        }
    }

    // ---- bottom panel and properties ------------------------------------------------------------------

    private void OnLogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogList.SelectedItem is { } item)
        {
            // A row picked above the last one holds the view until the user scrolls back down.
            _logFollows &= Equals(item, LogList.Items[^1]);
            _ = Dispatcher.BeginInvoke(() => LogList.ScrollIntoView(item), DispatcherPriority.Loaded);
        }
    }

    // The content stayed and the view moved: the user scrolled, or a follow got to the end.
    private static bool UserScrolled(ScrollChangedEventArgs e) => e.VerticalChange != 0 && e.ExtentHeightChange == 0;

    private static bool AtEnd(ScrollChangedEventArgs e) => e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 0.5;

    // New rows are followed while the last one is in view; an emptied list starts following again.
    private void OnLogScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (UserScrolled(e))
        {
            _logFollows = AtEnd(e);
        }
        else if (e.ExtentHeight <= e.ViewportHeight)
        {
            _logFollows = true;
        }
        else if (_logFollows && e.OriginalSource is ScrollViewer list)
        {
            list.ScrollToEnd();
        }
    }

    private void OnLogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyLogRow();
            e.Handled = true;
        }
    }

    private void OnLogMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = LogList.SelectedItem is null;

    private void OnLogCopy(object sender, RoutedEventArgs e) => CopyLogRow();

    private void CopyLogRow()
    {
        if (LogList.SelectedItem is LogItem item)
        {
            try
            {
                Clipboard.SetText(string.Create(CultureInfo.CurrentCulture, $"{item.Time} {item.Message}"));
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another process holds the clipboard; nothing to do.
            }
        }
    }

    private void OnPinRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PinRow row })
        {
            _viewModel.Properties?.HighlightPin(row);
        }
    }

    private void OnFieldWheel(object sender, MouseWheelEventArgs e)
    {
        // The wheel steps a focused number; otherwise it scrolls the panel as usual.
        if (sender is TextBox { IsKeyboardFocused: true, DataContext: PropertyField field } && field.Step(e.Delta > 0 ? 1 : -1))
        {
            e.Handled = true;
        }
    }
}
