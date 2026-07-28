using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class ScoreboardWindow : Window
{
    private const int WmDisplayChange = 0x007E;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);
    private HwndSource? source;
    private bool isFullScreen;
    private bool isClosing;
    private string? targetDeviceName;

    public ScoreboardWindow(
        ScoreboardViewModel viewModel,
        IMonitorService monitorService,
        ControllerViewModel controllerViewModel,
        IAppSettingsService settingsService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        MonitorService = monitorService;
        ControllerViewModel = controllerViewModel;
        SettingsService = settingsService;
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
        monitorService.DisplaysChanged += OnDisplaysChanged;
        settingsService.SettingsChanged += OnSettingsChanged;
        controllerViewModel.ToggleScoreboardFullScreenRequested += OnToggleFullScreenRequested;
        controllerViewModel.ToggleBlackoutRequested += OnToggleBlackoutRequested;
    }

    private ScoreboardViewModel ViewModel { get; }

    private IMonitorService MonitorService { get; }

    private ControllerViewModel ControllerViewModel { get; }

    private IAppSettingsService SettingsService { get; }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProcedure);

        MonitorService.Refresh();
        if (MonitorService.Monitors.Count > 1)
        {
            ShowFullScreen(GetPreferredPublicMonitor());
        }
        else
        {
            ShowPreview();
        }
    }

    private void OnToggleFullScreenRequested(object? sender, EventArgs e) =>
        ToggleFullScreen();

    private void OnToggleBlackoutRequested(object? sender, EventArgs e) =>
        ViewModel.ToggleBlackout();

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(HandleDisplaysChanged);
            return;
        }

        HandleDisplaysChanged();
    }

    private void HandleDisplaysChanged()
    {
        if (!isFullScreen)
        {
            return;
        }

        var target = MonitorService.Monitors.FirstOrDefault(
            monitor => monitor.DeviceName == targetDeviceName);
        if (target is null || (target.IsPrimary && MonitorService.Monitors.Count == 1))
        {
            ShowPreview();
            return;
        }

        ShowFullScreen(target);
    }

    private void ToggleFullScreen()
    {
        if (isFullScreen)
        {
            ShowPreview();
            return;
        }

        MonitorService.Refresh();
        ShowFullScreen(GetPreferredPublicMonitor());
    }

    private DisplayMonitor GetPreferredPublicMonitor()
    {
        var monitors = MonitorService.Monitors;
        var selectedDevice = SettingsService.Current.SelectedMonitorDeviceName;
        return monitors.FirstOrDefault(
                   monitor =>
                       selectedDevice.Length > 0 &&
                       monitor.DeviceName == selectedDevice)
            ?? monitors.FirstOrDefault(monitor => !monitor.IsPrimary)
            ?? (monitors.Count > 0
                ? monitors[0]
                : new DisplayMonitor("Primary", 0, 0, 1920, 1080, true));
    }

    private void ShowFullScreen(DisplayMonitor monitor)
    {
        isFullScreen = true;
        targetDeviceName = monitor.DeviceName;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = SettingsService.Current.ScoreboardTopmost;
        Cursor = Cursors.None;

        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(
            handle,
            HwndTopmost,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            SwpShowWindow);
    }

    private void ShowPreview()
    {
        isFullScreen = false;
        targetDeviceName = null;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Topmost = false;
        Cursor = Cursors.Arrow;
        Width = 960;
        Height = 540;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmDisplayChange)
        {
            _ = Dispatcher.BeginInvoke(MonitorService.Refresh);
        }

        return nint.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isClosing || System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ApplyDisplaySettings);
            return;
        }

        ApplyDisplaySettings();
    }

    private void ApplyDisplaySettings()
    {
        if (isFullScreen)
        {
            MonitorService.Refresh();
            ShowFullScreen(GetPreferredPublicMonitor());
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        isClosing = true;
        source?.RemoveHook(WindowProcedure);
        MonitorService.DisplaysChanged -= OnDisplaysChanged;
        SettingsService.SettingsChanged -= OnSettingsChanged;
        ControllerViewModel.ToggleScoreboardFullScreenRequested -= OnToggleFullScreenRequested;
        ControllerViewModel.ToggleBlackoutRequested -= OnToggleBlackoutRequested;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
