namespace CropVCam.App.Settings;

/// <summary>
/// User-chosen values persisted across app launches. <see cref="CameraName"/>
/// is the camera's DirectShow FriendlyName (not <c>CameraDevice.Index</c>,
/// which depends on enumeration order and can shift between launches).
/// </summary>
internal sealed record AppSettings(string? CameraName, double Magnification, string OutputName);
