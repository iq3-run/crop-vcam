using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CropVCam.App;

public partial class App : Application
{
    // Passed to AllowSetForegroundWindow to mean "whichever process calls
    // SetForegroundWindow next may do so", since we don't know the first
    // instance's PID from here - see NativeMethods.AllowSetForegroundWindow.
    private const uint AsfwAny = 0xFFFFFFFF;

    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // Without this, Windows' foreground-lock rules mean the first
            // instance's own SetForegroundWindow call below would likely
            // just flash its taskbar entry instead of actually raising it.
            NativeMethods.AllowSetForegroundWindow(AsfwAny);
            _singleInstance.NotifyExistingInstance();
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        _singleInstance.ListenForActivationRequests(ActivateMainWindow);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void ActivateMainWindow()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return; // a relaunch signal arrived while we were already closing
        }

        Dispatcher.Invoke(() =>
        {
            var window = MainWindow;
            if (window is null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
            NativeMethods.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        });
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(uint dwProcessId);
    }
}
