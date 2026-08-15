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
    /// Exactly <paramref name="width"/> x <paramref name="height"/> pixels,
    /// top-down, 3 bytes per pixel (B, G, R). Both dimensions must be within
    /// <see cref="SharedFrameProtocol.MaxWidth"/> / <see cref="SharedFrameProtocol.MaxHeight"/>
    /// - the shared region is sized for that upper bound and cannot hold more.
    /// </param>
    public void WriteFrame(byte[] bgr24Pixels, int width, int height)
    {
        if (width <= 0 || width > SharedFrameProtocol.MaxWidth)
        {
            throw new ArgumentException($"幅が不正です。幅={width} (上限 {SharedFrameProtocol.MaxWidth})", nameof(width));
        }
        if (height <= 0 || height > SharedFrameProtocol.MaxHeight)
        {
            throw new ArgumentException($"高さが不正です。高さ={height} (上限 {SharedFrameProtocol.MaxHeight})", nameof(height));
        }

        var strideBytes = width * 3;
        var payloadBytes = strideBytes * height;
        if (bgr24Pixels.Length != payloadBytes)
        {
            throw new ArgumentException(
                $"フレームサイズが不正です。期待値={payloadBytes}, 実際={bgr24Pixels.Length}",
                nameof(bgr24Pixels));
        }

        if (!TryAcquireMutex())
        {
            return; // reader is mid-copy; drop this frame rather than block the capture loop
        }

        try
        {
            _sequence++;
            _view.Write(0, SharedFrameProtocol.FrameMagic);
            _view.Write(4, width);
            _view.Write(8, height);
            _view.Write(12, strideBytes);
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

    private bool TryAcquireMutex()
    {
        try
        {
            return _mutex.WaitOne(MutexTimeout);
        }
        catch (AbandonedMutexException)
        {
            // The mutex is also held (across processes) by the native reader
            // running inside whatever app opened the virtual camera; if that
            // process died while holding it, .NET still grants us ownership
            // here - the shared region may be mid-write but we're about to
            // overwrite it anyway, so this is safe to treat as "acquired".
            return true;
        }
    }

    public void Dispose()
    {
        _view.Dispose();
        _mappedFile.Dispose();
        _mutex.Dispose();
        _frameReady.Dispose();
    }
}
