using System.Drawing;
using System.Windows.Forms;

namespace CropVCam.App;

/// <summary>
/// Wraps <see cref="NotifyIcon"/> (WPF has no tray-icon API of its own) so
/// <see cref="MainWindow"/> can hide there while streaming instead of fully
/// exiting. Uses a stock system icon - this repo ships no icon asset of its
/// own.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;

    public event Action? RestoreRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("開く", null, (_, _) => RestoreRequested?.Invoke());
        _contextMenu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "crop-vcam",
            Visible = false,
            ContextMenuStrip = _contextMenu,
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke();
    }

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
