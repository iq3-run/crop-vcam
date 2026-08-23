using System.ComponentModel;
using System.Windows;

namespace CropVCam.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly TrayIcon _trayIcon = new();

    // Set only by the tray icon's "終了" menu item, so Closing (below) lets
    // the window actually close instead of minimizing it to the tray again.
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        WireTrayIcon();
        WireWindowLifecycle();
    }

    // NotifyIcon has no message loop of its own - its events fire via the
    // same native message loop WPF's Dispatcher already pumps on this
    // thread, so no cross-thread marshaling is needed here.
    private void WireTrayIcon()
    {
        _trayIcon.RestoreRequested += RestoreFromTray;
        _trayIcon.ExitRequested += ExitFromTray;
    }

    private void WireWindowLifecycle()
    {
        // Covers every way the window can become visible again - the tray's
        // "開く", or a second launch's activation request (see
        // App.ActivateMainWindow, which also just calls Show()) - so the
        // tray icon never lingers once the window is back.
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _trayIcon.Visible = false;
        }
    }

    // While streaming, actually closing would unregister the virtual camera
    // and cut the feed mid-call (e.g. Zoom) - minimize to the tray instead.
    // Only the tray icon's "終了" (which sets _exitRequested first) closes
    // for real.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || !_viewModel.IsRunning)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _trayIcon.Visible = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _trayIcon.Dispose();
        _viewModel.SaveSettings();
        _viewModel.Dispose();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
