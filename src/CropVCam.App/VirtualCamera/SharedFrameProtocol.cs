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

    public const int OutputWidth = 1280;
    public const int OutputHeight = 720;
    public const int OutputStrideBytes = OutputWidth * 3;
    public const int OutputPayloadBytes = OutputStrideBytes * OutputHeight;
    public const long SharedRegionBytes = HeaderSize + OutputPayloadBytes;

    public const string MapName = "Local\\CropVCam_Data";
    public const string MutexName = "Local\\CropVCam_Mutex";
    public const string ReadyEventName = "Local\\CropVCam_Ready";
}
