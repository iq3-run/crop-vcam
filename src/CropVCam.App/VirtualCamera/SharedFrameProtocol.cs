namespace CropVCam.App.VirtualCamera;

/// <summary>
/// Layout shared with the native filter side:
/// src/CropVCam.Filter/src/SharedFrameProtocol.h
/// Keep the two definitions in sync when changing anything here.
/// </summary>
internal static class SharedFrameProtocol
{
    public const uint FrameMagic = 0x31435643; // "CVC1"
    public const int PixelFormatBgr24 = 1;
    public const int HeaderSize = 32;

    // Upper bound the shared region is sized for. Output frames track the
    // physical camera's own resolution (see MainViewModel.OnFrameCaptured),
    // clamped to this if a camera exceeds it. Must stay in lockstep with
    // kMaxWidth/kMaxHeight on the C++ side - the region size is fixed at
    // creation time and cannot be resized later, and a past drift between
    // the two sides' sizes made the filter unable to open the mapping at all.
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;
    public const int MaxStrideBytes = MaxWidth * 3;
    public const int MaxPayloadBytes = MaxStrideBytes * MaxHeight;
    public const long SharedRegionBytes = HeaderSize + MaxPayloadBytes;

    public const string MapName = "Local\\CropVCam_Data";
    public const string MutexName = "Local\\CropVCam_Mutex";
    public const string ReadyEventName = "Local\\CropVCam_Ready";
}
