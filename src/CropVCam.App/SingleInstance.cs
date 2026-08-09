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
        var thread = new Thread(() =>
        {
            while (true)
            {
                _activateEvent.WaitOne();
                onActivateRequested();
            }
        })
        {
            IsBackground = true,
            Name = "CropVCam-ActivationListener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
        _activateEvent.Dispose();
    }
}
