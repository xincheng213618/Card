using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Wpf.Persistence;

public sealed record PlayerPreferences(
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] bool SoundEnabled,
    [property: JsonRequired] double SoundVolume,
    [property: JsonRequired] bool MotionEnabled)
{
    public Dictionary<string, string>? GeneralSkins { get; init; }
    public bool MusicEnabled { get; init; } = true;
    public bool VoiceEnabled { get; init; } = true;
}

public interface IPlayerPreferencesStore
{
    PlayerPreferences? Read();
    void Write(PlayerPreferences preferences);
}

public sealed class MemoryPlayerPreferencesStore : IPlayerPreferencesStore
{
    private PlayerPreferences? _preferences;
    public PlayerPreferences? Read() => _preferences;
    public void Write(PlayerPreferences preferences) => _preferences = preferences;
}

public sealed class FilePlayerPreferencesStore : IPlayerPreferencesStore
{
    private readonly string _path;
    public FilePlayerPreferencesStore(string? path = null) => _path = Path.GetFullPath(path ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "preferences.json"));

    public PlayerPreferences? Read()
    {
        if (!File.Exists(_path)) return null;
        using var stream = File.OpenRead(_path);
        if (stream.Length > 65536) throw new InvalidDataException("偏好文件过大。");
        var preferences = JsonSerializer.Deserialize<PlayerPreferences>(stream) ?? throw new InvalidDataException("偏好为空。");
        Validate(preferences);
        return preferences;
    }

    public void Write(PlayerPreferences preferences)
    {
        Validate(preferences);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(PlayerPreferences preferences)
    {
        if (preferences.FormatVersion != 1 || !double.IsFinite(preferences.SoundVolume) || preferences.SoundVolume is < 0 or > 1)
            throw new InvalidDataException("偏好格式无效或版本不受支持。");
        if (preferences.GeneralSkins is { } skins &&
            (skins.Count > 512 || skins.Any(pair => pair.Key.Length is 0 or > 100 || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 100)))
            throw new InvalidDataException("武将皮肤偏好无效。");
    }
}
