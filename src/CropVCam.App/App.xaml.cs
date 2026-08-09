using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CropVCam.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
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
    }
}
