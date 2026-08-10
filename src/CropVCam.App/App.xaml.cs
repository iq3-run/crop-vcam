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

    // False for a second-instance process, which never registers anything
    // itself and shuts down immediately after relaying to the first instance
    // (still running, still using the registration) - OnExit must not
    // unregister the filter in that case.
    private bool _isPrimaryInstance;

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

        _isPrimaryInstance = true;
        _singleInstance.ListenForActivationRequests(ActivateMainWindow);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_isPrimaryInstance)
        {
            MainViewModel.UnregisterFilter();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void ActivateMainWindow()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return; // a relaunch signal arrived while we were already closing
        }

        // BeginInvoke (fire-and-forget), not Invoke: this runs on the
        // activation-listener thread, which SingleInstance.Dispose() joins
        // (with a timeout) from the UI thread during shutdown. A blocking
        // Invoke here could deadlock against that join for up to the join's
        // timeout, and then be left stranded mid-call once the dispatcher
        // finishes shutting down out from under it.
        Dispatcher.BeginInvoke(() =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return; // shutdown started while this was queued
            }

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
