using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Persistence;

public enum GameSaveSlot { Automatic, Manual }

public sealed record GameSaveFile(int FormatVersion, DateTimeOffset SavedAtUtc, bool AutoAdvance, GameCheckpoint Checkpoint)
{
    public const int CurrentFormatVersion = 1;
    public bool? MotionEnabled { get; init; }
    public bool? SoundEnabled { get; init; }
    public double? SoundVolume { get; init; }
    public string? PlaybackSpeedId { get; init; }
}

public interface IGameSaveStore
{
    DateTimeOffset? GetSavedAt(GameSaveSlot slot);
    GameSaveFile Read(GameSaveSlot slot);
    void Write(GameSaveSlot slot, GameSaveFile save);
}

/// <summary>Local, trusted-host files. Replace only after a complete temporary file has been flushed.</summary>
public sealed class FileGameSaveStore : IGameSaveStore
{
    private const long MaximumFileBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _directory;

    public FileGameSaveStore(string? directory = null) => _directory = Path.GetFullPath(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "Saves"));

    public string GetPath(GameSaveSlot slot) => Path.Combine(_directory, slot switch
    {
        GameSaveSlot.Automatic => "autosave.json",
        GameSaveSlot.Manual => "manual.json",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    });

    public DateTimeOffset? GetSavedAt(GameSaveSlot slot)
    {
        var path = GetPath(slot);
        return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
    }

    public GameSaveFile Read(GameSaveSlot slot)
    {
        using var stream = new FileStream(GetPath(slot), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("存档文件过大。");
        var save = JsonSerializer.Deserialize<GameSaveFile>(stream, JsonOptions) ??
            throw new InvalidDataException("存档文件为空。");
        Validate(save);
        return save;
    }

    public void Write(GameSaveSlot slot, GameSaveFile save)
    {
        Validate(save);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(save, JsonOptions);
        if (bytes.LongLength > MaximumFileBytes) throw new InvalidDataException("存档超过文件大小上限。");
        Directory.CreateDirectory(_directory);
        var destination = GetPath(slot);
        var temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".previous", ignoreMetadataErrors: true);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(GameSaveFile save)
    {
        if (save.FormatVersion != GameSaveFile.CurrentFormatVersion || save.Checkpoint is null)
            throw new InvalidDataException("存档格式不受支持。");
        if (save.Checkpoint.Commands is null || save.Checkpoint.Commands.Count > 100_000)
            throw new InvalidDataException("存档命令记录不完整或过长。");
        if (save.SoundVolume is { } volume && (!double.IsFinite(volume) || volume < 0 || volume > 1))
            throw new InvalidDataException("存档中的音量设置无效。");
        if (save.PlaybackSpeedId is { } speed && PlaybackSpeed.Find(speed) is null)
            throw new InvalidDataException("存档中的对局速度设置无效。");
    }
}
