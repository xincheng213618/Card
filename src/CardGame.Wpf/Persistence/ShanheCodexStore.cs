using System.IO;
using System.Text.Json;

namespace CardGame.Wpf.Persistence;

/// <summary>Account progression is independent of the replaceable expedition save.</summary>
public sealed record ShanheCodexProfile
{
    public int FormatVersion { get; init; } = 1;
    public int Books { get; init; }
    public string[] UnlockedRewardIds { get; init; } = [];
    public string?[] EquippedTacticIds { get; init; } = new string?[6];
    public string[] ClaimedRewards { get; init; } = [];
}

public interface IShanheCodexStore
{
    bool Exists { get; }
    ShanheCodexProfile Read();
    void Write(ShanheCodexProfile profile);
}

public sealed class MemoryShanheCodexStore : IShanheCodexStore
{
    private string? _json;
    public bool Exists => _json is not null;
    public ShanheCodexProfile Read() => FileShanheCodexStore.Decode(_json ?? throw new FileNotFoundException());
    public void Write(ShanheCodexProfile profile) => _json = FileShanheCodexStore.Encode(profile);
}

public sealed class FileShanheCodexStore : IShanheCodexStore
{
    public FileShanheCodexStore(string? directory = null) => PathName = Path.Combine(Path.GetFullPath(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "Saves")), "shanhe-codex.json");
    public string PathName { get; }
    public bool Exists => File.Exists(PathName);
    public ShanheCodexProfile Read()
    {
        if (new FileInfo(PathName).Length > 2 * 1024 * 1024) throw new InvalidDataException("山河册文件过大。");
        return Decode(File.ReadAllText(PathName));
    }
    public void Write(ShanheCodexProfile profile)
    {
        var json = Encode(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
                stream.Flush(true);
            }
            if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".previous", true);
            else File.Move(temporary, PathName);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string Encode(ShanheCodexProfile profile)
    {
        Validate(profile);
        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }
    internal static ShanheCodexProfile Decode(string json)
    {
        var profile = JsonSerializer.Deserialize<ShanheCodexProfile>(json) ?? throw new InvalidDataException("山河册文件为空。");
        Validate(profile);
        return profile;
    }
    private static void Validate(ShanheCodexProfile profile)
    {
        if (profile.FormatVersion != 1 || profile.Books < 0 || profile.UnlockedRewardIds is null ||
            profile.EquippedTacticIds is not { Length: 6 } || profile.ClaimedRewards is null ||
            profile.UnlockedRewardIds.Length > 2000 || profile.ClaimedRewards.Length > 20000 ||
            profile.UnlockedRewardIds.Any(string.IsNullOrWhiteSpace) || profile.ClaimedRewards.Any(string.IsNullOrWhiteSpace) ||
            profile.UnlockedRewardIds.Distinct(StringComparer.Ordinal).Count() != profile.UnlockedRewardIds.Length ||
            profile.ClaimedRewards.Distinct(StringComparer.Ordinal).Count() != profile.ClaimedRewards.Length ||
            profile.EquippedTacticIds.OfType<string>().Distinct(StringComparer.Ordinal).Count() != profile.EquippedTacticIds.OfType<string>().Count() ||
            profile.EquippedTacticIds.OfType<string>().Any(id => !profile.UnlockedRewardIds.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("山河册进度无效，原文件已保留。");
    }
}
