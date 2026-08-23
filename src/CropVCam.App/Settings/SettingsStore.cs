using System.IO;
using System.Text.Json;

namespace CropVCam.App.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> to a JSON file under the current
/// user's local app data. Best-effort: persistence must never block app
/// startup or shutdown, so all failures (missing/corrupt file, no
/// read/write access) are swallowed rather than surfaced to the caller.
/// </summary>
internal static class SettingsStore
{
    private const string AppFolderName = "CropVCam";
    private const string FileName = "settings.json";

    public static AppSettings? Load()
    {
        try
        {
            var path = ResolvePath();
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = ResolvePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ResolvePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, FileName);
}
