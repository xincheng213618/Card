using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.Persistence;

/// <summary>The campaign journal and its current battle are replaced as one local file.</summary>
public sealed record ShanheCampaignSaveFile(
    int FormatVersion, DateTimeOffset SavedAtUtc, int Difficulty,
    ShanheCampaignCheckpoint Campaign, GameCheckpoint? Battle = null)
{
    public const int CurrentFormatVersion = 1;
}

public interface IShanheCampaignSaveStore
{
    bool Exists { get; }
    ShanheCampaignSaveFile Read();
    void Write(ShanheCampaignSaveFile save);
}

public sealed class MemoryShanheCampaignSaveStore : IShanheCampaignSaveStore
{
    private string? _json;
    public bool Exists => _json is not null;
    public ShanheCampaignSaveFile Read() => _json is { } json
        ? FileShanheCampaignSaveStore.Deserialize(json)
        : throw new FileNotFoundException("没有找到山河图征程存档。");
    public void Write(ShanheCampaignSaveFile save) => _json = FileShanheCampaignSaveStore.Serialize(save);
}

public sealed class FileShanheCampaignSaveStore : IShanheCampaignSaveStore
{
    private const int MaximumFileBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileShanheCampaignSaveStore(string? directory = null)
    {
        var root = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "Saves"));
        PathName = Path.Combine(root, "shanhe-campaign.json");
    }

    public string PathName { get; }
    public bool Exists => File.Exists(PathName);

    public ShanheCampaignSaveFile Read()
    {
        using var stream = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("山河图存档文件过大。");
        var save = JsonSerializer.Deserialize<ShanheCampaignSaveFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("山河图存档文件为空。");
        Validate(save);
        return save;
    }

    public void Write(ShanheCampaignSaveFile save)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Serialize(save));
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("山河图存档超过文件大小上限。");
        var directory = Path.GetDirectoryName(PathName)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".shanhe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".previous", ignoreMetadataErrors: true);
            else File.Move(temporary, PathName);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string Serialize(ShanheCampaignSaveFile save)
    {
        Validate(save);
        return JsonSerializer.Serialize(save, JsonOptions);
    }

    internal static ShanheCampaignSaveFile Deserialize(string json)
    {
        var save = JsonSerializer.Deserialize<ShanheCampaignSaveFile>(json, JsonOptions)
            ?? throw new InvalidDataException("山河图存档文件为空。");
        Validate(save);
        return save;
    }

    private static void Validate(ShanheCampaignSaveFile save)
    {
        if (save.FormatVersion != ShanheCampaignSaveFile.CurrentFormatVersion || save.Campaign is null ||
            save.Difficulty is < 1 or > 3)
            throw new InvalidDataException("山河图存档格式或难度不受支持。");
        if (save.Campaign.Commands is null || save.Campaign.Commands.Count > 100_000)
            throw new InvalidDataException("山河图征程命令记录不完整或过长。");
        if (save.Battle is { } battle && (battle.Commands is null || battle.Commands.Count > 100_000))
            throw new InvalidDataException("山河图战斗命令记录不完整或过长。");
    }
}
