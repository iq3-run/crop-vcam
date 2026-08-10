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
using CropVCam.App.VirtualCamera;
using OpenCvSharp;

namespace CropVCam.App;

internal sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double MinMagnification = 1.0;
    public const double MaxMagnification = 4.0;
    private const string DefaultOutputName = "Cropped Virtual Camera";
    private const string FilterDllFileName = "CropVCamFilter.dll";

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

        SelectedCamera = CameraDevices.FirstOrDefault();
    }

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

    private void StartStreaming()
    {
        ErrorMessage = null;
        try
        {
            FilterRegistrar.EnsureRegistered(ResolveFilterDllPath());
            FilterRegistrar.SetFriendlyName(OutputName);

            _frameWriter = new SharedFrameWriter();

            var capture = new CameraCapture(SelectedCamera!.Index);
            capture.FrameCaptured += OnFrameCaptured;
            capture.FrameProcessingFailed += OnFrameProcessingFailed;
            capture.Start();
            _capture = capture;

            IsRunning = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StopStreaming();
        }
    }

    private void StopStreaming()
    {
        if (_capture is not null)
        {
            _capture.FrameCaptured -= OnFrameCaptured;
            _capture.FrameProcessingFailed -= OnFrameProcessingFailed;
            _capture.Dispose(); // joins the capture thread, so no OnFrameCaptured call is still in flight after this
            _capture = null;
        }

        _frameWriter?.Dispose();
        _frameWriter = null;

        IsRunning = false;
    }

    private void OnFrameCaptured(Mat sourceFrame)
    {
        // The slider clamps itself, but the bound TextBox doesn't - guard
        // against a typed 0/negative/huge value reaching the crop math.
        var magnification = Math.Clamp(Magnification, MinMagnification, MaxMagnification);
        using var cropped = CenterCropScaler.CropAndScale(
            sourceFrame, magnification, SharedFrameProtocol.OutputWidth, SharedFrameProtocol.OutputHeight);

        var pixelBytes = ToBgr24Bytes(cropped);
        _frameWriter?.WriteFrame(pixelBytes);
        UpdatePreview(pixelBytes, cropped.Width, cropped.Height);
    }

    private void OnFrameProcessingFailed(Exception ex)
    {
        Application.Current.Dispatcher.BeginInvoke(() => ErrorMessage = ex.Message);
    }

    private static byte[] ToBgr24Bytes(Mat bgrMat)
    {
        var bytes = new byte[bgrMat.Width * bgrMat.Height * 3];
        Marshal.Copy(bgrMat.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private void UpdatePreview(byte[] bgrPixels, int width, int height)
    {
        // If the UI thread is stalled, dispatcher-queued updates (each
        // holding a ~2.7MB frame) would otherwise pile up unbounded; drop
        // frames instead of queuing behind ones that haven't rendered yet.
        if (Interlocked.Exchange(ref _previewUpdatePending, 1) == 1)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgrPixels, width * 3);
                bitmap.Freeze();
                PreviewImage = bitmap;
            }
            finally
            {
                Interlocked.Exchange(ref _previewUpdatePending, 0);
            }
        });
    }

    // Called from App.OnExit, separately from StopStreaming/Dispose - the
    // "停止" button only halts streaming and deliberately leaves the
    // registration in place for the next "開始"; only a full app exit
    // cleans up the registry.
    public static void UnregisterFilter() => FilterRegistrar.TryUnregister(ResolveFilterDllPath());

    private static string ResolveFilterDllPath() => Path.Combine(AppContext.BaseDirectory, FilterDllFileName);

    public void Dispose() => StopStreaming();
}
