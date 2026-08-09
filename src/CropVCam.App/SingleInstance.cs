namespace CropVCam.App;

/// <summary>
/// Enforces one running instance. A second launch signals the first one
/// (via a named event) to bring its window to the foreground, then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Local\\CropVCam_SingleInstance";
    private const string ActivateEventName = "Local\\CropVCam_ActivateRequest";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly ManualResetEvent _stopEvent = new(initialState: false);
    private Thread? _listenerThread;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        // Both instances open/create the same named event up front, so a
        // second instance can never race ahead of the first one listening.
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
    }

    public bool IsFirstInstance { get; }

    public void NotifyExistingInstance() => _activateEvent.Set();

    public void ListenForActivationRequests(Action onActivateRequested)
    {
        _listenerThread = new Thread(() => ListenLoop(onActivateRequested))
        {
            IsBackground = true,
            Name = "CropVCam-ActivationListener",
        };
        _listenerThread.Start();
    }

    public void Dispose()
    {
        // Signal the listener thread to exit its wait and join it BEFORE
        // disposing the handles it waits on - disposing a WaitHandle out
        // from under a thread still blocked in WaitOne/WaitAny throws
        // ObjectDisposedException there, which is unhandled and would
        // crash the whole process (this runs on every normal app close).
        _stopEvent.Set();
        _listenerThread?.Join(TimeSpan.FromSeconds(2));

        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
        _activateEvent.Dispose();
        _stopEvent.Dispose();
    }

    private void ListenLoop(Action onActivateRequested)
    {
        var handles = new WaitHandle[] { _stopEvent, _activateEvent };
        while (WaitHandle.WaitAny(handles) == 1)
        {
            onActivateRequested();
        }
    }
}
