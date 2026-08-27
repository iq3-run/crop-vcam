namespace CropVCam.App.Settings;

/// <summary>
/// User-chosen values persisted across app launches. <see cref="CameraName"/>
/// is the camera's DirectShow FriendlyName (not <c>CameraDevice.Index</c>,
/// which depends on enumeration order and can shift between launches).
/// <see cref="UnregisterOnExit"/> defaults to true (matches pre-existing
/// behavior) so a settings.json saved before this field existed still
/// deletes the registry entry on exit, same as it always did.
/// </summary>
internal sealed record AppSettings(
    string? CameraName,
    double Magnification,
    string OutputName,
    bool UnregisterOnExit = true);
