using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>
/// A trusted-host checkpoint for a command-driven match. It stores only the
/// immutable setup metadata and accepted command prefix needed to reconstruct
/// the state; it is not a player snapshot and must not be sent to clients.
/// </summary>
public sealed record GameCheckpoint(
    int SchemaVersion,
    GameOptions Options,
    IReadOnlyList<GameCommand> Commands,
    long Revision,
    string ModeId,
    IReadOnlyList<string> ContentPackages,
    string ContentHash)
{
    public const int CurrentSchemaVersion = 3;
    // Development save compatibility epoch. This project is not released yet,
    // so checkpoints from any other rules version are rejected instead of
    // migrated. Adding content is versioned by its package and content hash.
    // 102-114 were retired development epochs; do not reuse one for new semantics.
    public const int CurrentRulesVersion = 176;

    public int RulesVersion { get; init; }
}

/// <summary>JSON boundary for trusted-host checkpoint files.</summary>
public static class GameCheckpointJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(GameCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return JsonSerializer.Serialize(checkpoint, Options);
    }

    public static GameCheckpoint Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<GameCheckpoint>(json, Options) ??
            throw new InvalidOperationException("The checkpoint JSON did not contain a checkpoint.");
    }
}

/// <summary>
/// Replays a trusted-host command journal against a fresh deterministic engine.
/// Every command is submitted through the normal revision and prompt validation
/// boundary, so a corrupted or out-of-order journal fails instead of silently
/// changing the rules path.
/// </summary>
public static class GameReplay
{
    public static GameEngine Replay(
        GameOptions options,
        IEnumerable<GameCommand> commands,
        ContentRegistry contentRegistry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(contentRegistry);

        var engine = GameEngine.CreateStandard(options, contentRegistry);
        var commandIndex = 0;
        foreach (var command in commands)
        {
            var result = engine.Submit(command);
            if (!result.Accepted)
            {
                var error = result.Error;
                var expectedPrompt = command switch
                {
                    AnswerPromptCommand answer => answer.PromptId,
                    PlayCardCommand play => play.PromptId,
                    EndPlayPhaseCommand endPlay => endPlay.PromptId,
                    DiscardCardsCommand discard => discard.PromptId,
                    SelectGeneralCommand selectGeneral => selectGeneral.PromptId,
                    UseProgramSkillCommand program => program.PromptId,
                    UseEquipmentEffectCommand equipment => equipment.PromptId,
                    _ => (PromptId?)null
                };
                var promptContext = expectedPrompt is { } expected
                    ? $" Expected prompt {expected.Value}; current prompt " +
                      $"{engine.PendingDecision?.PromptId.Value.ToString() ?? "none"} " +
                      $"({engine.PendingDecision?.Kind.ToString() ?? "none"}) at revision {engine.Revision}."
                    : string.Empty;
                throw new InvalidOperationException(
                    $"Replay command {commandIndex} ({command.GetType().Name}) was rejected " +
                    $"({error?.Code}): {error?.Message}{promptContext}");
            }

            commandIndex++;
        }

        return engine;
    }

    /// <summary>
    /// Restores a trusted-host checkpoint by replaying its accepted command
    /// prefix through the ordinary command boundary.
    /// </summary>
    public static GameEngine Restore(
        GameCheckpoint checkpoint,
        ContentRegistry contentRegistry)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(contentRegistry);
        if (checkpoint.SchemaVersion != GameCheckpoint.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Checkpoint schema {checkpoint.SchemaVersion} is not supported; " +
                $"expected {GameCheckpoint.CurrentSchemaVersion}.");
        }

        if (checkpoint.RulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            throw new InvalidOperationException(
                $"Checkpoint rules version {checkpoint.RulesVersion} is not supported; " +
                $"this development build requires exactly {GameCheckpoint.CurrentRulesVersion}.");
        }

        if (checkpoint.Options is null ||
            checkpoint.Commands is null ||
            checkpoint.ContentPackages is null ||
            checkpoint.ModeId is null ||
            string.IsNullOrWhiteSpace(checkpoint.ContentHash) ||
            checkpoint.Revision < 0 ||
            checkpoint.Commands.Count != checkpoint.Revision)
        {
            throw new InvalidOperationException("The checkpoint metadata is incomplete or inconsistent.");
        }

        var expectedPackages = GetPackageSignatures(contentRegistry);
        if (!checkpoint.ContentPackages.SequenceEqual(expectedPackages, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The checkpoint content package signature does not match the supplied registry.");
        }

        var expectedContentHash = GetContentHash(contentRegistry);
        if (!string.Equals(checkpoint.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The checkpoint content hash does not match the supplied registry.");
        }

        var engine = Replay(
            checkpoint.Options,
            checkpoint.Commands,
            contentRegistry);
        if (engine.Revision != checkpoint.Revision ||
            !string.Equals(engine.ModeId, checkpoint.ModeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Checkpoint replay did not reproduce the recorded revision and mode.");
        }

        return engine;
    }

    internal static IReadOnlyList<string> GetPackageSignatures(ContentRegistry contentRegistry) =>
        contentRegistry.Packages
            .Select(package => $"{package.Id}@{package.Version}")
            .ToArray();

    internal static string GetContentHash(ContentRegistry contentRegistry) =>
        contentRegistry.ContentHash;
}
