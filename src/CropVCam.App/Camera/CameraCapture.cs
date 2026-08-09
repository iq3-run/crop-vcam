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
    private const int FrameReadRetryDelayMs = 10;

    private readonly int _deviceIndex;
    private CancellationTokenSource? _cancellation;
    private Thread? _captureThread;
    private VideoCapture? _videoCapture;

    public event Action<Mat>? FrameCaptured;

    /// <summary>Raised when a <see cref="FrameCaptured"/> subscriber throws, so the capture loop can keep running.</summary>
    public event Action<Exception>? FrameProcessingFailed;

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
        var stopped = _captureThread?.Join(TimeSpan.FromSeconds(2)) ?? true;
        _captureThread = null;
        _cancellation?.Dispose();
        _cancellation = null;

        if (!stopped)
        {
            // The capture thread is still blocked inside a native call (e.g.
            // VideoCapture.Read on a wedged driver). Disposing _videoCapture
            // out from under it would be a use-after-free, so it's left for
            // the GC/finalizer instead of freed here.
            return;
        }

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
                Thread.Sleep(FrameReadRetryDelayMs); // avoid busy-spinning while the camera is unavailable
                continue;
            }

            RaiseFrameCaptured(frame);
        }
    }

    private void RaiseFrameCaptured(Mat frame)
    {
        try
        {
            FrameCaptured?.Invoke(frame);
        }
        catch (Exception ex)
        {
            // A single bad frame (e.g. a transient shared-memory hiccup) must
            // not take down the whole capture thread/process.
            FrameProcessingFailed?.Invoke(ex);
        }
    }
}
