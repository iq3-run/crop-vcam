namespace CropVCam.App.Camera;

/// <summary>
/// A physical (or at least non-CropVCam) capture device as seen through
/// DirectShow's video input category. <see cref="Index"/> matches the
/// enumeration order OpenCvSharp's DSHOW backend uses to open a device.
/// </summary>
internal sealed record CameraDevice(int Index, string Name);
