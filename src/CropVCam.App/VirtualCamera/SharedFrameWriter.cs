using System.IO.MemoryMappedFiles;

namespace CropVCam.App.VirtualCamera;

/// <summary>
/// Writes cropped BGR24 frames into the shared memory region the native
/// filter (running in-process inside whatever app opens the virtual
/// camera, e.g. Zoom) reads from. See SharedFrameProtocol for the layout.
/// </summary>
internal sealed class SharedFrameWriter : IDisposable
{
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromMilliseconds(200);

    private readonly MemoryMappedFile _mappedFile;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _frameReady;
    private ulong _sequence;

    public SharedFrameWriter()
    {
        _mappedFile = MemoryMappedFile.CreateOrOpen(SharedFrameProtocol.MapName, SharedFrameProtocol.SharedRegionBytes);
        _view = _mappedFile.CreateViewAccessor();
        _mutex = new Mutex(initiallyOwned: false, SharedFrameProtocol.MutexName);
        _frameReady = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, SharedFrameProtocol.ReadyEventName);
    }

    /// <param name="bgr24Pixels">
    /// Exactly <see cref="SharedFrameProtocol.OutputWidth"/> x
    /// <see cref="SharedFrameProtocol.OutputHeight"/> pixels, top-down,
    /// 3 bytes per pixel (B, G, R).
    /// </param>
    public void WriteFrame(byte[] bgr24Pixels)
    {
        if (bgr24Pixels.Length != SharedFrameProtocol.OutputPayloadBytes)
        {
            throw new ArgumentException(
                $"フレームサイズが不正です。期待値={SharedFrameProtocol.OutputPayloadBytes}, 実際={bgr24Pixels.Length}",
                nameof(bgr24Pixels));
        }

        if (!_mutex.WaitOne(MutexTimeout))
        {
            return; // reader is mid-copy; drop this frame rather than block the capture loop
        }

        try
        {
            _sequence++;
            _view.Write(0, SharedFrameProtocol.FrameMagic);
            _view.Write(4, SharedFrameProtocol.OutputWidth);
            _view.Write(8, SharedFrameProtocol.OutputHeight);
            _view.Write(12, SharedFrameProtocol.OutputStrideBytes);
            _view.Write(16, SharedFrameProtocol.PixelFormatBgr24);
            _view.Write(20, _sequence);
            _view.WriteArray(SharedFrameProtocol.HeaderSize, bgr24Pixels, 0, bgr24Pixels.Length);
        }
        finally
        {
            _mutex.ReleaseMutex();
        }

        _frameReady.Set();
    }

    public void Dispose()
    {
        _view.Dispose();
        _mappedFile.Dispose();
        _mutex.Dispose();
        _frameReady.Dispose();
    }
}
