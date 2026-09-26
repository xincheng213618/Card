using System.IO;
using System.Text.Json;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Audio;

public sealed record AudioBinding
{
    public string? GeneralKey { get; init; }
    public string? GeneralName { get; init; }
    public string? SkinId { get; init; }
    public string? SkinName { get; init; }
    public string? SkillId { get; init; }
    public string? SkillName { get; init; }
    public string? LineText { get; init; }
    public string? Role { get; init; }
}

public sealed record GameAudioAsset
{
    public string Id { get; init; } = "";
    public string AssignmentStatus { get; init; } = "unmapped";
    public string Role { get; init; } = "";
    public string? GeneralKey { get; init; }
    public string? GeneralName { get; init; }
    public string? SkinId { get; init; }
    public string? SkinName { get; init; }
    public string? SkillId { get; init; }
    public string? SkillName { get; init; }
    public string? LineText { get; init; }
    public string LocalPath { get; init; } = "";
    public string DeliveredSha256 { get; init; } = "";
    public long DeliveredBytes { get; init; }
    public string SourceUrl { get; init; } = "";
    public AudioBinding[]? Bindings { get; init; }
    public string FilePath
    {
        get
        {
            const string prefix = "src/CardGame.Wpf/Assets/Audio/Official/";
            if (!LocalPath.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid audio path.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "Official")) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, LocalPath[prefix.Length..]));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Audio path escapes assets.");
            return path;
        }
    }
}

public sealed record GeneralVoice(GameAudioAsset Asset, AudioBinding Binding)
{
    public string GeneralName => Binding.GeneralName ?? Binding.GeneralKey ?? "";
    public string SkinName => Binding.SkinName ?? "皮肤未对应";
    public string SkillName => Binding.Role == "death" ? "阵亡语音" : Binding.SkillName ?? "武将台词";
    public string LineText => string.IsNullOrWhiteSpace(Binding.LineText) ? "台词文本暂未收录，点击试听" : Binding.LineText;
    public string Caption => $"{GeneralName} · {SkinName} · {SkillName}";
}

/// <summary>Only explicit, verified bindings can select gameplay voices. Unknown files remain archived.</summary>
public static class GameAudioCatalog
{
    private sealed record Catalog(int SchemaVersion, GameAudioAsset[] Assets);
    public static IReadOnlyList<GameAudioAsset> Assets { get; } = Load();
    public static IReadOnlyList<GeneralVoice> Voices { get; } = Assets
        .Where(asset => asset.AssignmentStatus == "verified")
        .SelectMany(asset => GetBindings(asset).Select(binding => new GeneralVoice(asset, binding)))
        .Where(voice => voice.Binding.GeneralKey is not null && voice.Binding.Role is "skill" or "death")
        .DistinctBy(voice => (voice.Asset.Id, voice.Binding.GeneralKey, voice.Binding.SkinId, voice.Binding.SkillName, voice.Binding.Role)).ToArray();

    public static GameAudioAsset? Music(string role) => Assets.FirstOrDefault(asset => asset.AssignmentStatus == "verified" && asset.Role == role);
    public static IReadOnlyList<GeneralVoice> ForGeneral(string generalId) => Voices
        .Where(voice => voice.Binding.GeneralKey == GeneralArt.NormalizeKey(generalId)).ToArray();
    public static IReadOnlyList<GeneralVoice> ForSkill(string generalId, string? skinId, string skillName, string role = "skill") =>
        ForGeneral(generalId).Where(voice => !string.IsNullOrEmpty(skinId) && voice.Binding.SkinId == skinId &&
            voice.Binding.Role == role && (role == "death" || voice.Binding.SkillName == skillName)).ToArray();

    private static IEnumerable<AudioBinding> GetBindings(GameAudioAsset asset)
    {
        var primary = new AudioBinding { GeneralKey = asset.GeneralKey, GeneralName = asset.GeneralName,
            SkinId = asset.SkinId, SkinName = asset.SkinName, SkillId = asset.SkillId, SkillName = asset.SkillName,
            LineText = asset.LineText, Role = asset.Role };
        yield return primary;
        foreach (var binding in asset.Bindings ?? [])
            yield return binding with { GeneralKey = binding.GeneralKey ?? primary.GeneralKey, GeneralName = binding.GeneralName ?? primary.GeneralName,
                SkillId = binding.SkillId ?? primary.SkillId, SkillName = binding.SkillName ?? primary.SkillName,
                LineText = binding.LineText ?? primary.LineText, Role = binding.Role ?? primary.Role };
    }

    private static GameAudioAsset[] Load()
    {
        using var stream = typeof(GameAudioCatalog).Assembly.GetManifestResourceStream("CardGame.GameAudioCatalog.json")
            ?? throw new InvalidDataException("Audio catalog missing.");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (catalog is not { SchemaVersion: 1, Assets: not null }) throw new InvalidDataException("Unsupported audio catalog.");
        return catalog.Assets;
    }
}
