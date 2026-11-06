using ArduSim.Core.Boards;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.App.Views;
using ICSharpCode.AvalonEdit.Editing;

namespace ArduSim.App;

/// <summary>
/// View glue only: window chrome (with Windows 11 snap layouts on the maximize button), panel
/// sizes, keyboard routing, palette drag and dialogs. Everything else is in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;
    private const int WmSysCommand = 0x0112;
    private const long ScKeyMenu = 0xF100;
    private const double CollapsedSide = 24;
    private const double CollapsedBottom = 32;

    private readonly MainViewModel _viewModel;
    private bool _closing;
    private Point? _dragStart;
    private double _paletteWidth;
    private double _propertiesWidth;
    private double _bottomHeight;
    private bool _serialFollows = true;
    private bool _logFollows = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.AskToSave = name => UnsavedDialog.Ask(this, name);
        viewModel.ShowLibraries = dialog => LibraryDialog.Open(this, dialog);
        viewModel.PickFolder = title =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        };

        PanelLayout layout = viewModel.Layout;
        _paletteWidth = Math.Clamp(layout.PaletteWidth, 180, 360);
        _propertiesWidth = Math.Clamp(layout.PropertiesWidth, 220, 420);
        _bottomHeight = Math.Max(120, layout.BottomHeight);
        _splitRatio = Math.Clamp(layout.SplitRatio, 0.15, 0.85);
        RestoreWindow(layout.Window);
        OnStateChanged();
        ApplyLayout();
        ApplyCenterLayout();
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.ShowPartRequested += part => Canvas.BringIntoView(part);
        viewModel.SerialAppended += SerialOutput.AppendText;
        viewModel.SerialReplaced += OnSerialReplaced;
        viewModel.FocusSerialRequested += () => Dispatcher.BeginInvoke(() => SerialInputBox.Focus(), DispatcherPriority.Input);
        viewModel.FocusNewProjectRequested += () => Dispatcher.BeginInvoke(
            () =>
            {
                NewProjectBox.Focus();
                NewProjectBox.SelectAll();
            },
            DispatcherPriority.Input);
        StateChanged += (_, _) => OnStateChanged();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        Loaded += (_, _) => ExampleThumbnails.Render(this, viewModel);
        Theme.Changed += () => ExampleThumbnails.Render(this, viewModel);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing)
        {
            return;
        }

        if (!_viewModel.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        RememberSizes();
        RememberSplit();
        _viewModel.SaveLayout(_paletteWidth, _propertiesWidth, _bottomHeight, _splitRatio, CurrentBounds());

        // Stop the Runner before the window goes away; then close for real, outside this handler
        // (Close() is not allowed while a close is in progress, even when DisposeAsync finished synchronously).
        e.Cancel = true;
        _closing = true;
        await _viewModel.DisposeAsync();
        _ = Dispatcher.BeginInvoke(Close);
    }

    // ---- window chrome -------------------------------------------------------------------------

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged()
    {
        // A maximized chrome-less window overhangs the screen by its whole frame, on every side.
        Thickness frame = SystemParameters.WindowNonClientFrameThickness;
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(frame.Left, frame.Bottom, frame.Right, frame.Bottom) : default;
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxButton.ToolTip = Strings.Get(WindowState == WindowState.Maximized ? "Window_Restore" : "Window_Maximize");
    }

    /// <summary>Reports the maximize button as HTMAXBUTTON so Windows 11 shows snap layouts on hover.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmSysCommand when (wParam.ToInt64() & 0xFFF0) == ScKeyMenu && lParam == IntPtr.Zero && !Canvas.InGesture:
                // Alt alone: custom chrome has no menu bar, so the project menu is the one that opens.
                OnProjectMenu(ProjectButton, new RoutedEventArgs());
                handled = true;
                break;
            case WmNcHitTest when IsOverMaxButton(lParam):
                MaxButton.Tag = "Hot";
                handled = true;
                return HtMaxButton;
            case WmNcMouseMove when wParam.ToInt32() != HtMaxButton:
            case WmNcMouseLeave:
                MaxButton.Tag = null;
                break;
            case WmNcLButtonDown when wParam.ToInt32() == HtMaxButton:
                handled = true;
                break;
            case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                handled = true;
                OnMaximize(MaxButton, new RoutedEventArgs());
                break;
        }

        return IntPtr.Zero;
    }

    private bool IsOverMaxButton(IntPtr lParam)
    {
        if (!MaxButton.IsVisible)
        {
            return false;
        }

        long value = lParam.ToInt64();
        var screen = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        Point local = MaxButton.PointFromScreen(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < MaxButton.ActualWidth && local.Y < MaxButton.ActualHeight;
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsDialog.Open(this, _viewModel);

    private void OnAccuracy(object sender, RoutedEventArgs e) => AccuracySheet.Open(this);

    private void OnProjectMenu(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)FindResource("ProjectMenu");
        menu.DataContext = _viewModel;
        menu.PlacementTarget = ProjectButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- panels --------------------------------------------------------------------------------

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PaletteOpen) or nameof(MainViewModel.PropertiesOpen) or nameof(MainViewModel.BottomOpen))
        {
            RememberSizes();
            ApplyLayout();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTab) && _viewModel.IsSceneTab)
        {
            Canvas.Focus();
        }
        else if (e.PropertyName == nameof(MainViewModel.SplitView))
        {
            RememberSplit();
            ApplyCenterLayout();
            _ = Dispatcher.BeginInvoke(Canvas.Refit, DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(MainViewModel.AutoScroll) && _viewModel.AutoScroll)
        {
            _serialFollows = true;
            SerialOutput.ScrollToEnd();
        }
        else if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.ProjectName) or nameof(MainViewModel.SelectedBoard) or nameof(MainViewModel.HasProject))
        {
            _ = Dispatcher.BeginInvoke(UpdateToolbarStage, DispatcherPriority.Loaded);

            // A run started from the editor: part keys need the keyboard (docs/UI.md §4.4).
            if (e.PropertyName == nameof(MainViewModel.Status) && _viewModel.Status == SimStatus.Starting && _viewModel.IsCanvasVisible && Keyboard.FocusedElement is TextArea or TextBox)
            {
                Canvas.Focus();
            }
        }
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        RememberSizes();
        RememberSplit();
    }

    private void RememberSizes()
    {
        if (!_narrow && _viewModel.PaletteOpen && PaletteColumn.ActualWidth > CollapsedSide)
        {
            _paletteWidth = PaletteColumn.ActualWidth;
        }

        if (!_narrow && _viewModel.PropertiesOpen && PropertiesColumn.ActualWidth > CollapsedSide)
        {
            _propertiesWidth = PropertiesColumn.ActualWidth;
        }

        if (_viewModel.BottomOpen && BottomRow.ActualHeight > CollapsedBottom)
        {
            _bottomHeight = BottomRow.ActualHeight;
        }
    }

    private void ApplyLayout()
    {
        PaletteColumn.MinWidth = _viewModel.PaletteOpen ? 180 : CollapsedSide;
        PaletteColumn.MaxWidth = _viewModel.PaletteOpen ? 360 : CollapsedSide;
        PaletteColumn.Width = new GridLength(!_viewModel.PaletteOpen ? CollapsedSide : _narrow ? 200 : _paletteWidth);
        PropertiesColumn.MinWidth = _viewModel.PropertiesOpen ? 220 : CollapsedSide;
        PropertiesColumn.MaxWidth = _viewModel.PropertiesOpen ? 420 : CollapsedSide;
        PropertiesColumn.Width = new GridLength(!_viewModel.PropertiesOpen ? CollapsedSide : _narrow ? 220 : _propertiesWidth);
        BottomRow.MinHeight = _viewModel.BottomOpen ? 120 : CollapsedBottom;
        BottomRow.MaxHeight = _viewModel.BottomOpen ? double.PositiveInfinity : CollapsedBottom;
        BottomRow.Height = new GridLength(_viewModel.BottomOpen ? _bottomHeight : CollapsedBottom);
    }

    private void OnAddSignal(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddSignalButton, Placement = PlacementMode.Bottom };
        foreach (BoardPin pin in _viewModel.Timeline.PinChoices)
        {
            menu.Items.Add(new MenuItem { Header = pin.Name, Command = _viewModel.Timeline.AddPinCommand, CommandParameter = pin });
        }

        menu.IsOpen = menu.Items.Count > 0;
    }

    private void OnAddBus(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddBusButton, Placement = PlacementMode.Bottom };
        foreach (BusChoice choice in _viewModel.Timeline.BusChoices)
        {
            menu.Items.Add(new MenuItem { Header = choice.Title, Command = _viewModel.Timeline.AddBusCommand, CommandParameter = choice });
        }

        if (menu.Items.Count > 0)
        {
            var other = new MenuItem { Header = Strings.Get("Timeline_OtherPins") };
            other.Click += (_, _) => CustomBusPopup.IsOpen = true;
            menu.Items.Add(new Separator());
            menu.Items.Add(other);
        }

        menu.IsOpen = menu.Items.Count > 0;
    }

    private void OnCustomBusAdded(object sender, RoutedEventArgs e) => CustomBusPopup.IsOpen = false;

    private void OnOpenBottomTab(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedBottomTab = int.Parse((string)((FrameworkElement)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _viewModel.BottomOpen = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // The bottom panel takes at most 60 % of the height (docs/UI.md §2.1).
        BottomRow.MaxHeight = _viewModel.BottomOpen ? Math.Max(120, ActualHeight * 0.6) : CollapsedBottom;
        _viewModel.SetCompactHeight(ActualHeight < CompactHeight);
        bool narrow = ActualWidth < NarrowWidth;
        if (narrow != _narrow)
        {
            RememberSizes();
            _narrow = narrow;
            ApplyLayout();
        }
    }

    // ---- keyboard ------------------------------------------------------------------------------

    private static bool TextHasFocus => Keyboard.FocusedElement is TextBox or TextArea;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (key == Key.F10 && modifiers == ModifierKeys.None)
        {
            // F10 is a system key (it would focus the menu bar); here it steps one loop().
            if (_viewModel.StepLoopCommand.CanExecute(null))
            {
                _viewModel.StepLoopCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (key == Key.F11 && !e.IsRepeat)
        {
            string milliseconds = modifiers == ModifierKeys.Control ? "100" : modifiers == ModifierKeys.Shift ? "10" : "1";
            if (_viewModel.StepTimeCommand.CanExecute(milliseconds))
            {
                _viewModel.StepTimeCommand.Execute(milliseconds);
            }

            e.Handled = true;
        }
        else if (key == Key.F6 && (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            CyclePanes(modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            ShortcutSheet.Open(this);
            e.Handled = true;
        }
        else if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            // Ctrl+F follows focus: the editor keeps it; otherwise the open tab, then the part search.
            if (!_viewModel.HasProject)
            {
                ExampleSearchBox.Focus();
                ExampleSearchBox.SelectAll();
                e.Handled = true;
            }
            else if (Keyboard.FocusedElement is not TextArea)
            {
                if (_viewModel.IsSceneTab)
                {
                    FocusPaletteSearch();
                }
                else
                {
                    _viewModel.VisibleDocument?.RequestFind();
                }

                e.Handled = true;
            }
        }
        else if (key == Key.Escape && _viewModel.Banner is not (BannerKind.None or BannerKind.Installing) && !TextHasFocus && !(_viewModel.Scene?.IsWiring ?? false)
            && !Canvas.InGesture && Keyboard.FocusedElement is not (ComboBox or ComboBoxItem or MenuItem))
        {
            _viewModel.CloseBannerCommand.Execute(null);
            e.Handled = true;
        }
        else if (!TextHasFocus && !e.IsRepeat && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 && _viewModel.OnKey(key.ToString(), down: true))
        {
            e.Handled = true;
        }
        else if (_viewModel.IsCanvasVisible && !Canvas.IsKeyboardFocusWithin && !BottomTabs.IsKeyboardFocusWithin
            && Keyboard.FocusedElement is not (TextBox or TextArea or ComboBox or ComboBoxItem or MenuItem or ListBoxItem) && Canvas.HandlesKey(key, modifiers))
        {
            // Canvas commands work whichever panel last had the focus.
            Canvas.Focus();
            Canvas.HandleKey(e);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!TextHasFocus && _viewModel.OnKey(key.ToString(), down: false))
        {
            e.Handled = true;
        }
    }

    // ---- palette -------------------------------------------------------------------------------

    private static PaletteItem? PaletteItemAt(object source) =>
        (source as DependencyObject) is { } element && ItemsControl.ContainerFromElement(null, element) is ListBoxItem { DataContext: PaletteItem item } ? item : null;

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(this);

    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start)
        {
            return;
        }

        Vector moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        if (PaletteItemAt(e.OriginalSource) is { } item)
        {
            if (_viewModel.Scene is { IsLocked: true })
            {
                _viewModel.ShowLockedToast();
                return;
            }

            if (!_viewModel.IsCanvasVisible)
            {
                _viewModel.SelectedTab = SceneTab.Instance;
            }

            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(SchematicCanvas.PartTypeFormat, item.Request), DragDropEffects.Copy);
        }
    }

    private void OnPaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PaletteItemAt(e.OriginalSource) is { } item)
        {
            PlacePart(item);
        }
    }

    private void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: PaletteItem item })
        {
            PlacePart(item);
            e.Handled = true;
        }
    }

    private void PlacePart(PaletteItem item)
    {
        if (_viewModel.Scene is { IsLocked: true })
        {
            _viewModel.ShowLockedToast();
            return;
        }

        if (!_viewModel.IsCanvasVisible)
        {
            _viewModel.SelectedTab = SceneTab.Instance;
        }

        Canvas.PlaceAtCenter(item.Request);
        Canvas.Focus();
    }

    // ---- center tabs ---------------------------------------------------------------------------

    private void OnAddTab(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddTabButton, Placement = PlacementMode.Bottom };
        var newFile = new MenuItem { Header = Strings.Get("Explorer_NewFile") };
        newFile.Click += (_, _) => AskFileName(null, AddTabButton);
        menu.Items.Add(newFile);
        IReadOnlyList<string> files = _viewModel.ClosedFiles();
        menu.Items.Add(new Separator());
        if (files.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Strings.Get("Tab_NoClosedFiles"), IsEnabled = false });
        }

        foreach (string file in files)
        {
            menu.Items.Add(new MenuItem { Header = System.IO.Path.GetFileName(file), Command = _viewModel.OpenSketchFileCommand, CommandParameter = file });
        }

        menu.IsOpen = true;
    }

    // ---- properties ----------------------------------------------------------------------------

    private void OnIdKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.Properties?.RenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.Properties is { } properties && _viewModel.Scene?.SinglePart is { } part)
        {
            properties.IdText = part.Id;
            e.Handled = true;
        }
    }

    private void OnIdLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.Properties?.RenameCommand.Execute(null);

    private void OnFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PropertyField field })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // A valid value hands the keyboard back to the schematic; an error stays with the field.
                field.Commit();
                if (field.Error.Length == 0 && _viewModel.IsCanvasVisible)
                {
                    Canvas.Focus();
                }

                e.Handled = true;
                break;
            case Key.Escape:
                field.Revert();
                if (_viewModel.IsCanvasVisible)
                {
                    Canvas.Focus();
                }

                e.Handled = true;
                break;
            case Key.Up or Key.Down when field.Step(e.Key == Key.Up ? 1 : -1):
                e.Handled = true;
                break;
        }
    }

    private void OnFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PropertyField field })
        {
            field.Commit();
        }
    }

    /// <summary>The key field captures the next key press: its name becomes the part's shortcut (Backspace clears it).</summary>
    private void OnKeyFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PropertyField field })
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        field.Text = key is Key.Back or Key.Delete or Key.Escape ? "" : key.ToString();
        field.Commit();
        e.Handled = true;
    }

    // ---- bottom panel --------------------------------------------------------------------------

    private void OnSerialInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_viewModel.SendSerialCommand.CanExecute(null))
            {
                _viewModel.SendSerialCommand.Execute(null);
            }
            else
            {
                _viewModel.ShowEditorMessage(Strings.Get("Serial_NotRunning"));
            }

            e.Handled = true;
        }
    }

    private void OnSerialReplaced(string text)
    {
        SerialOutput.Text = text;
        _serialFollows |= text.Length == 0;
    }

    // The Monitor follows new text until the user scrolls away from the end; scrolling back resumes it.
    private void OnSerialScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (UserScrolled(e))
        {
            _serialFollows = AtEnd(e);
        }
        else if (_serialFollows && _viewModel.AutoScroll && SerialOutput.SelectionLength == 0)
        {
            SerialOutput.ScrollToEnd();
        }
    }

    private void OnDiagnosticClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DiagnosticItem item })
        {
            _viewModel.OpenDiagnosticCommand.Execute(item);
        }
    }


    private void OnDiagnosticKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: DiagnosticItem item })
        {
            _viewModel.OpenDiagnosticCommand.Execute(item);
            e.Handled = true;
        }
    }
}
