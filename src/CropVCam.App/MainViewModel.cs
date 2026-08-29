using System.Buffers;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CropVCam.App.Camera;
using CropVCam.App.Processing;
using CropVCam.App.Settings;
using CropVCam.App.VirtualCamera;
using OpenCvSharp;

namespace CropVCam.App;

internal sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double MinMagnification = 1.0;
    public const double MaxMagnification = 4.0;
    private const string DefaultOutputName = "Cropped Virtual Camera";
    private const string FilterDllFileName = "CropVCamFilter.dll";

    // ArrayPool<byte>.Shared caps pooled arrays at ~1MiB; every frame here
    // (6.2MiB at 1080p, 23.7MiB at 4K) exceeds that, so .Shared would just
    // allocate fresh each time and defeat the point. A dedicated pool sized
    // for our own max frame keeps these buffers actually reused.
    private static readonly ArrayPool<byte> FrameBufferPool =
        ArrayPool<byte>.Create(maxArrayLength: SharedFrameProtocol.MaxPayloadBytes, maxArraysPerBucket: 2);

    private CameraCapture? _capture;
    private SharedFrameWriter? _frameWriter;

    [ObservableProperty]
    private ObservableCollection<CameraDevice> cameraDevices = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private CameraDevice? selectedCamera;

    [ObservableProperty]
    private double magnification = 2.5;

    [ObservableProperty]
    private string outputName = DefaultOutputName;

    [ObservableProperty]
    private bool unregisterOnExit = AppSettings.DefaultUnregisterOnExit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopButtonText))]
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private bool isRunning;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private string? errorMessage;

    private int _previewUpdatePending;

    public string StartStopButtonText => IsRunning ? "停止" : "開始";
    public bool CanEditSettings => !IsRunning;

    public MainViewModel()
    {
        foreach (var device in CameraEnumerator.EnumerateCameras())
        {
            CameraDevices.Add(device);
        }

        var savedSettings = SettingsStore.Load();
        if (savedSettings is not null)
        {
            Magnification = Math.Clamp(savedSettings.Magnification, MinMagnification, MaxMagnification);
            if (!string.IsNullOrWhiteSpace(savedSettings.OutputName))
            {
                OutputName = savedSettings.OutputName;
            }
            UnregisterOnExit = savedSettings.UnregisterOnExit;
        }

        // Matched by name, not CameraDevice.Index - index depends on
        // enumeration order, which can shift between launches (USB
        // reconnects, other devices added/removed). Falls back to the first
        // camera if the saved one isn't present (unplugged, renamed, or no
        // saved settings yet). Triggers OnSelectedCameraChanged, which
        // starts preview capture.
        SelectedCamera = CameraDevices.FirstOrDefault(d => d.Name == savedSettings?.CameraName)
            ?? CameraDevices.FirstOrDefault();
    }

    // Read by UnregisterFilter() below. Set from SaveSettings() rather than
    // relying on SettingsStore.Load() at exit time - SettingsStore.Save()
    // swallows IOException/UnauthorizedAccessException, so a failed write
    // could otherwise leave disk holding a stale (or absent) value that
    // disagrees with what the checkbox actually showed this session.
    private static bool s_unregisterOnExit = AppSettings.DefaultUnregisterOnExit;

    // Called from MainWindow.Closed, before Dispose - captures the
    // in-memory state as of shutdown rather than saving on every
    // Magnification/OutputName change (both are bound with
    // UpdateSourceTrigger=PropertyChanged, so they change per slider tick /
    // keystroke; settings can't change at all while streaming since
    // CanEditSettings is false, so "value at close time" is always the
    // final one).
    public void SaveSettings()
    {
        s_unregisterOnExit = UnregisterOnExit;
        SettingsStore.Save(new AppSettings(SelectedCamera?.Name, Magnification, OutputName, UnregisterOnExit));
    }

    partial void OnSelectedCameraChanged(CameraDevice? value) => RestartPreviewCapture(value);

    [RelayCommand(CanExecute = nameof(CanStartStop))]
    private void StartStop()
    {
        if (IsRunning)
        {
            StopStreaming();
        }
        else
        {
            StartStreaming();
        }
    }

    private bool CanStartStop() => IsRunning || SelectedCamera is not null;

    // Preview capture runs continuously once a camera is selected, independent
    // of the "開始"/"停止" (streaming) lifecycle below - so the user can see
    // the crop/zoom before ever registering the virtual camera. This is also
    // what lets the native filter learn the physical camera's real resolution
    // before a downstream app (e.g. Zoom) connects to it (see SharedFrameWriter.WriteFrame).
    private void RestartPreviewCapture(CameraDevice? device)
    {
        StopPreviewCapture();
        if (device is null)
        {
            return;
        }

        try
        {
            var capture = new CameraCapture(device.Index);
            capture.FrameCaptured += OnFrameCaptured;
            capture.FrameProcessingFailed += OnFrameProcessingFailed;
            capture.Start();
            _capture = capture;
            ErrorMessage = null; // clear any error from a previously-selected camera that failed to open
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void StopPreviewCapture()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.FrameProcessingFailed -= OnFrameProcessingFailed;
        _capture.Dispose(); // joins the capture thread, so no OnFrameCaptured call is still in flight after this
        _capture = null;
    }

    private void StartStreaming()
    {
        ErrorMessage = null;

        // Preview capture normally already runs from camera selection, but
        // retry here in case it failed earlier (e.g. camera was busy then).
        if (_capture is null)
        {
            RestartPreviewCapture(SelectedCamera);
        }
        if (_capture is null)
        {
            return; // RestartPreviewCapture already surfaced the failure via ErrorMessage
        }

        try
        {
            FilterRegistrar.EnsureRegistered(ResolveFilterDllPath());
            FilterRegistrar.SetFriendlyName(OutputName);

            _frameWriter = new SharedFrameWriter();

            IsRunning = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StopStreaming();
        }
    }

    // Only stops the virtual-camera broadcast (registration stays, frame
    // writing stops) - preview capture on the physical camera keeps running
    // so the user still sees a live picture while stopped.
    private void StopStreaming()
    {
        _frameWriter?.Dispose();
        _frameWriter = null;

        IsRunning = false;
    }

    private void OnFrameCaptured(Mat sourceFrame)
    {
        // The slider clamps itself, but the bound TextBox doesn't - guard
        // against a typed 0/negative/huge value reaching the crop math.
        var magnification = Math.Clamp(Magnification, MinMagnification, MaxMagnification);
        var (outputWidth, outputHeight) = ClampToSharedRegionLimit(sourceFrame.Width, sourceFrame.Height);
        using var cropped = CenterCropScaler.CropAndScale(sourceFrame, magnification, outputWidth, outputHeight);

        var byteCount = cropped.Width * cropped.Height * 3;
        var pixelBytes = FrameBufferPool.Rent(byteCount);
        var ownedByPreview = false;
        try
        {
            Marshal.Copy(cropped.Data, pixelBytes, 0, byteCount);
            _frameWriter?.WriteFrame(pixelBytes, cropped.Width, cropped.Height);
            ownedByPreview = UpdatePreview(pixelBytes, cropped.Width, cropped.Height);
        }
        finally
        {
            if (!ownedByPreview)
            {
                FrameBufferPool.Return(pixelBytes);
            }
        }
    }

    // Output tracks the physical camera's own resolution 1:1 (magnification
    // 1.0 is a no-op crop, scaled back up to that same size). The shared
    // memory region is only sized for SharedFrameProtocol.MaxWidth/MaxHeight
    // though, so a camera beyond that gets scaled down first, aspect ratio preserved.
    private static (int Width, int Height) ClampToSharedRegionLimit(int width, int height)
    {
        if (width <= SharedFrameProtocol.MaxWidth && height <= SharedFrameProtocol.MaxHeight)
        {
            return (width, height);
        }

        var scale = Math.Min(
            (double)SharedFrameProtocol.MaxWidth / width,
            (double)SharedFrameProtocol.MaxHeight / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private void OnFrameProcessingFailed(Exception ex)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ErrorMessage = ex.Message);
    }

    // Returns whether ownership of bgrPixels was handed off to the dispatched
    // callback (which returns it to FrameBufferPool once done) - false means
    // the frame was dropped and the caller must return it immediately.
    private bool UpdatePreview(byte[] bgrPixels, int width, int height)
    {
        // If the UI thread is stalled, dispatcher-queued updates (each
        // holding a ~2.7MB frame) would otherwise pile up unbounded; drop
        // frames instead of queuing behind ones that haven't rendered yet.
        if (Interlocked.Exchange(ref _previewUpdatePending, 1) == 1)
        {
            return false;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgrPixels, width * 3);
                bitmap.Freeze();
                PreviewImage = bitmap;
            }
            finally
            {
                FrameBufferPool.Return(bgrPixels);
                Interlocked.Exchange(ref _previewUpdatePending, 0);
            }
        });

        return true;
    }

    // Called from App.OnExit, separately from StopStreaming/Dispose - the
    // "停止" button only halts streaming and deliberately leaves the
    // registration in place for the next "開始"; only a full app exit
    // cleans up the registry, and only if the user opted into that via the
    // UnregisterOnExit checkbox. Reads s_unregisterOnExit rather than an
    // instance because App holds no MainViewModel reference; MainWindow's
    // Closed handler already calls SaveSettings() (which sets the field)
    // before OnExit runs.
    public static void UnregisterFilter()
    {
        if (s_unregisterOnExit)
        {
            FilterRegistrar.TryUnregister(ResolveFilterDllPath());
        }
    }

    private static string ResolveFilterDllPath() => Path.Combine(AppContext.BaseDirectory, FilterDllFileName);

    public void Dispose()
    {
        StopStreaming();
        StopPreviewCapture();
    }
}
