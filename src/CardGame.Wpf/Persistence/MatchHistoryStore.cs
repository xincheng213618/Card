using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Persistence;

public enum MatchOutcome { Win, Loss, Draw }

public sealed record MatchHistoryEntry(int FormatVersion, string Id, DateTimeOffset RecordedAtUtc,
    string Mode, int RulesVersion, MatchOutcome Outcome, MatchSummary Summary)
{
    [JsonIgnore] public string DateLabel => RecordedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    [JsonIgnore] public string OutcomeLabel => Outcome switch { MatchOutcome.Win => "胜利", MatchOutcome.Loss => "败北", _ => "平局" };
    [JsonIgnore] public string OutcomeColor => Outcome switch { MatchOutcome.Win => "#DCC17C", MatchOutcome.Loss => "#C58C84", _ => "#96B6AC" };
    [JsonIgnore] public PlayerMatchResult Human => Summary.Players.Single(player => player.IsHuman);
    [JsonIgnore] public string Title => $"{Human.GeneralName}{(Human.SecondaryGeneralName is null ? "" : $" / {Human.SecondaryGeneralName}")} · {Human.Camp}";
}

public sealed record MatchHistoryLoad(IReadOnlyList<MatchHistoryEntry> Entries, int UnreadableCount = 0);

public interface IMatchHistoryStore
{
    MatchHistoryLoad Read();
    void Record(MatchHistoryEntry entry);
}

public sealed class MemoryMatchHistoryStore : IMatchHistoryStore
{
    private readonly Dictionary<string, MatchHistoryEntry> _entries = [];
    public MatchHistoryLoad Read() => new(_entries.Values.OrderByDescending(entry => entry.RecordedAtUtc).Take(50).ToArray());
    public void Record(MatchHistoryEntry entry) => _entries.TryAdd(entry.Id, entry);
}

/// <summary>Immutable per-match public summaries. Never stores a checkpoint, seed or hidden cards.</summary>
public sealed class FileMatchHistoryStore : IMatchHistoryStore
{
    private const int MaximumFileBytes = 128 * 1024;
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FileMatchHistoryStore(string? directory = null) => _directory = Path.GetFullPath(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "History"));

    public MatchHistoryLoad Read()
    {
        if (!Directory.Exists(_directory)) return new([]);
        var entries = new List<MatchHistoryEntry>();
        var unreadable = 0;
        // Use the recorded date, so copying/restoring files cannot reorder the match history.
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("*.json"))
        {
            try { entries.Add(ReadEntry(file.FullName)); }
            catch (Exception error) when (IsHistoryError(error)) { unreadable++; }
        }
        return new(entries.OrderByDescending(entry => entry.RecordedAtUtc).Take(50).ToArray(), unreadable);
    }

    public void Record(MatchHistoryEntry entry)
    {
        Validate(entry);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, entry.Id + ".json");
        if (File.Exists(path)) { ReadEntry(path); return; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("战绩数据过大。");
        var temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { ReadEntry(path); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static MatchHistoryEntry ReadEntry(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("战绩文件过大。");
        var entry = JsonSerializer.Deserialize<MatchHistoryEntry>(stream, JsonOptions) ?? throw new InvalidDataException("战绩为空。");
        Validate(entry);
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), entry.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("战绩文件标识不一致。");
        return entry;
    }

    private static void Validate(MatchHistoryEntry entry)
    {
        if (entry.FormatVersion != 1 || entry.Id is not { Length: 64 } || !entry.Id.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(entry.Mode) || entry.Mode.Length > 80 || entry.RulesVersion < 1 || !Enum.IsDefined(entry.Outcome) ||
            entry.Summary is not { TurnCount: >= 0, Players.Count: > 0 and <= 16 } ||
            entry.Summary.Players.Any(player => player is null) || entry.Summary.Players.Count(player => player.IsHuman) != 1 ||
            entry.Summary.Players.Select(player => player.Seat).Distinct().Count() != entry.Summary.Players.Count ||
            entry.Summary.Players.Any(player => player.Seat < 0 || string.IsNullOrWhiteSpace(player.GeneralName) || player.GeneralName.Length > 80 ||
                player.SecondaryGeneralName is { Length: > 80 } ||
                string.IsNullOrWhiteSpace(player.Camp) || player.Camp.Length > 30 ||
                new[] { player.CardsUsed, player.Responses, player.DamageDealt, player.DamageTaken, player.Recovery, player.RescueCards, player.Defeats }.Any(value => value < 0)))
            throw new InvalidDataException("战绩格式无效或版本不受支持。");
    }

    public static bool IsHistoryError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;
}
