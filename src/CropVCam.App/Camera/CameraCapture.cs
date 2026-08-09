using OpenCvSharp;

namespace CropVCam.App.Camera;

/// <summary>
/// Pulls frames off a physical camera on a dedicated background thread and
/// raises <see cref="FrameCaptured"/> synchronously for each one. The Mat
/// passed to subscribers is reused across frames, so it is only valid for
/// the duration of the event call - copy anything you need to keep.
/// </summary>
internal sealed class CameraCapture : IDisposable
{
    private readonly int _deviceIndex;
    private CancellationTokenSource? _cancellation;
    private Thread? _captureThread;
    private VideoCapture? _videoCapture;

    public event Action<Mat>? FrameCaptured;

    public CameraCapture(int deviceIndex)
    {
        _deviceIndex = deviceIndex;
    }

    public void Start()
    {
        if (_captureThread is not null)
        {
            return;
        }

        _videoCapture = new VideoCapture(_deviceIndex, VideoCaptureAPIs.DSHOW);
        if (!_videoCapture.IsOpened())
        {
            _videoCapture.Dispose();
            _videoCapture = null;
            throw new InvalidOperationException("カメラを開けませんでした。");
        }

        _cancellation = new CancellationTokenSource();
        _captureThread = new Thread(() => CaptureLoop(_cancellation.Token))
        {
            IsBackground = true,
            Name = "CropVCam-Capture",
        };
        _captureThread.Start();
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;
        _cancellation?.Dispose();
        _cancellation = null;

        _videoCapture?.Dispose();
        _videoCapture = null;
    }

    public void Dispose() => Stop();

    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        while (!token.IsCancellationRequested)
        {
            if (_videoCapture is null || !_videoCapture.Read(frame) || frame.Empty())
            {
                continue;
            }

            FrameCaptured?.Invoke(frame);
        }
    }
}
