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
    [NotifyPropertyChangedFor(nameof(CanEditOutputName))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private bool isRunning;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private string? errorMessage;

    public string StartStopButtonText => IsRunning ? "停止" : "開始";
    public bool CanEditOutputName => !IsRunning;

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
            var filterDllPath = Path.Combine(AppContext.BaseDirectory, FilterDllFileName);
            FilterRegistrar.EnsureRegistered(filterDllPath);
            FilterRegistrar.SetFriendlyName(OutputName);

            _frameWriter = new SharedFrameWriter();

            var capture = new CameraCapture(SelectedCamera!.Index);
            capture.FrameCaptured += OnFrameCaptured;
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
            _capture.Dispose(); // joins the capture thread, so no OnFrameCaptured call is still in flight after this
            _capture = null;
        }

        _frameWriter?.Dispose();
        _frameWriter = null;

        IsRunning = false;
    }

    private void OnFrameCaptured(Mat sourceFrame)
    {
        using var cropped = CenterCropScaler.CropAndScale(
            sourceFrame, Magnification, SharedFrameProtocol.OutputWidth, SharedFrameProtocol.OutputHeight);

        var pixelBytes = ToBgr24Bytes(cropped);
        _frameWriter?.WriteFrame(pixelBytes);
        UpdatePreview(pixelBytes, cropped.Width, cropped.Height);
    }

    private static byte[] ToBgr24Bytes(Mat bgrMat)
    {
        var bytes = new byte[bgrMat.Width * bgrMat.Height * 3];
        Marshal.Copy(bgrMat.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private void UpdatePreview(byte[] bgrPixels, int width, int height)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgrPixels, width * 3);
            bitmap.Freeze();
            PreviewImage = bitmap;
        });
    }

    public void Dispose() => StopStreaming();
}
