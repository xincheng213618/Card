using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;

namespace CardGame.Wpf.Diagnostics;

public sealed record ExceptionReportResult(string? Directory, string? Error)
{
    public bool Saved => Directory is not null;
}

public interface IGameExceptionReportStore
{
    ExceptionReportResult Write(Exception exception, string operation, GameEngine? game = null,
        GameCommand? failedCommand = null);
}

/// <summary>
/// Local trusted-host diagnostics, separate from save slots and player views.
/// Reports are append-only incidents; starting or saving another match never replaces them.
/// </summary>
public sealed class FileGameExceptionReportStore(string? directory = null) : IGameExceptionReportStore
{
    private readonly string _directory = Path.GetFullPath(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "Logs", "Exceptions"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExceptionReportResult Write(Exception exception, string operation, GameEngine? game = null,
        GameCommand? failedCommand = null)
    {
        string reportDirectory;
        var createdAt = DateTimeOffset.UtcNow;
        try
        {
            reportDirectory = Path.Combine(_directory, $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(reportDirectory);
            // Persist the original stack before querying potentially damaged game state.
            WriteNew(reportDirectory, "exception.txt", $"UTC: {createdAt:O}\nOperation: {operation}\n\n{exception}");
        }
        catch (Exception error)
        {
            return new(null, error.Message);
        }

        var errors = new List<string>();
        GameCheckpoint? checkpoint = null;
        Capture("manifest.json", () => JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            CreatedAtUtc = createdAt,
            Operation = operation,
            ProcessId = Environment.ProcessId,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name?.StartsWith("CardGame.", StringComparison.Ordinal) == true)
                .Select(assembly => new
                {
                    Name = assembly.GetName().Name,
                    Version = assembly.GetName().Version?.ToString(),
                    InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    ModuleId = assembly.ManifestModule.ModuleVersionId
                }).ToArray(),
            IsFaulted = game?.IsFaulted,
            Revision = game?.Revision,
            RulesVersion = game?.RulesVersion,
            FailedCommandIncluded = failedCommand is not null
        }, JsonOptions));
        if (failedCommand is not null)
            Capture("failed-command.json", () => CommandJson.Serialize([failedCommand]));
        if (game is not null)
        {
            Capture("checkpoint.json", () =>
            {
                checkpoint = game.IsFaulted ? game.LastTrustedCheckpoint : game.CreateCheckpoint();
                return checkpoint is not null ? GameCheckpointJson.Serialize(checkpoint)
                    : throw new InvalidOperationException("No trusted command prefix is available for this session.");
            });
            Capture("battle-log.json", () => JsonSerializer.Serialize(game.Log.TakeLast(400).ToArray(), JsonOptions));
            Capture("resolution-stack.json", () => JsonSerializer.Serialize(game.ResolutionStack, JsonOptions));
            Capture("card-zones.json", () => JsonSerializer.Serialize(game.CreateCardZoneDiagnostics(), JsonOptions));
            Capture("player-snapshot.json", () => SnapshotJson.Serialize(game.State));
        }
        Capture("README.txt", () =>
            "This is a local trusted-host exception report. It may contain hidden cards and private commands.\n" +
            "It is independent of automatic/manual save slots and is never overwritten by a new match.\n\n" +
            "exception.txt: original exception, inner exceptions and stack trace.\n" +
            "manifest.json: runtime, exact assembly module IDs and failure context.\n" +
            "checkpoint.json: last committed, trusted command prefix, including seed, options, packages and content hash.\n" +
            "failed-command.json: the uncommitted Submit command that raised the exception, when available.\n" +
            "battle-log.json, resolution-stack.json, card-zones.json, player-snapshot.json: diagnostic context only.\n\n" +
            "Reproduce with the matching rules/content build: restore checkpoint.json via GameReplay.Restore,\n" +
            "then deserialize failed-command.json via CommandJson.Deserialize and Submit its single command.\n" +
            "Do not restore the damaged resolution stack as game state or bypass compatibility checks.\n" +
            (checkpoint is null ? "No checkpoint was captured; see capture-errors.txt.\n" : "") +
            (failedCommand is null ? "No failing Submit command was captured; inspect the exception operation and stack.\n" : ""));
        if (errors.Count > 0)
        {
            try { WriteNew(reportDirectory, "capture-errors.txt", string.Join("\n\n", errors)); }
            catch (Exception error) { errors.Add($"capture-errors.txt: {error.Message}"); }
        }
        return new(reportDirectory, errors.Count == 0 ? null : string.Join("; ", errors));

        void Capture(string name, Func<string> content)
        {
            try { WriteNew(reportDirectory, name, content()); }
            catch (Exception error) { errors.Add($"{name}: {error}"); }
        }
    }

    private static void WriteNew(string directory, string name, string content)
    {
        using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
