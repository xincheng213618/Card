using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Recorded AI decision policies preserve historical command replay.</summary>
public static class AiDecisionPolicy
{
    public const int CorrelatedActiveSkillVersion = 4;
    public const int CurrentVersion = CorrelatedActiveSkillVersion;

    public static bool IsSupported(int version) => version >= 1 && version <= CurrentVersion;
}

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
    // Runtime capability and diagnostic marker. Saved rules versions do not
    // gate restoration: replay uses the current implementation and validates
    // the checkpoint structure, content fingerprint and each recorded command.
    // 102-114 were retired development epochs; do not reuse one for new semantics.
    // 186: Dong Zhuo (jiuchi/roulin/benghuai/baonve) with the shared
    // livingPlayersMinHp fact, participant gender card-policy conditions and the
    // EventSource target kind; the lord-skill damage-source faction facts.
    // 187: Liu Shan (xiangle/fangquan/ruoyu) with extra-turn target
    // parameterization and the target-payer nullification conditions.
    // 188: Zhang Zhao & Zhang Hong (zhijian/guzheng) with the
    // selectedTargetEquipment destination, the empty-slot activation target
    // filter, the turn-owner discard-phase hand-discard fact, and the
    // named-choice hand-gain move branch.
    // 190: Jia Xu (wansha/luanwu/weimu) with the nearest-character forced Slash
    // participant operation and the suit-based trick target prohibition.
    // 191: Finalized equipment targets precede installation and commitment;
    // top-deck basic costs survive nested response observers and cleanup.
    // 192: A Program-owned virtual Duel keeps its exact participants when
    // the responder and opponent swap after a successful Slash response.
    // Borrowed Sword faction supply validates reconstructed continuation
    // handles by exact resolution identity instead of reference equality.
    // 194: Native optional phase declarations price their exact hand-cost
    // successor and public extra-turn beneficiary. The same accepted command
    // prefix and content hash can therefore choose different AI actions.
    // 195: Completed-card gift windows retain their exact program parent through
    // Qinglong follow-ups and the recipient's real reuse of the gifted entity.
    // Activation BooleanState gates read their owning runtime instance; pair
    // obtain/gift instructions resume their paid cursor instead of being rejected.
    // 197: AI judgment/replacement decisions carrying SkillPrompt advance through
    // their typed handler instead of accepting unchanged single-step commands.
    // 198: Resolved converted DrawTwo, IronChain and Alcohol finish with their
    // captured effective kind, retaining material identity in accepted event history.
    // 199: Round-start programs retain their lifecycle boundary and offer the
    // round choice to every living owner, including owners outside the turn seat.
    // 200: Tongxie follow-ups use exact settled action provenance: chain-issued
    // Slashes cannot reopen a chain, reused entities can start a later use,
    // and a Response cannot borrow an earlier Use of the same physical card.
    // The same batch repairs Jielie's paid dying continuation and Liu Hong's
    // real activation, slot choice and source-attributed game damage bonus.
    // 201: Liejie freezes the damage source's red appearance before its real
    // discard payment and drains each issued source-card movement once.
    // 202: Original-opponent target additions also open the existing finalized
    // window for an ordinary trick declared by its InstantTrick category.
    // 203: Recognize the actual discard payments of category refund,
    // last-damage reciprocity and phase-name prediction in movement triggers.
    // Delayed tricks open counterspell resolution at Judgment, after their
    // original use and placement have completed.
    // 211: A granted turn red-Slash benefit skips Dodge responses, including
    // converted Slashes, while retaining armor and damage resolution.
    // 212: Red-discard recovery stops offering targets at its frozen budget,
    // so native AI confirms the selection instead of attempting an extra seat.
    // 213: Identity selection reveals the Lord first, then the local player
    // chooses before private AI picks; team/national selection is human-first.
    // 214: Luochong retains every paid discard in its pending state and final
    // resolution event instead of reading a stale duplicate collection.
    // 215: Returning from a turn-start inserted phase restores the normal-flow
    // phase, so the subsequent real draw can open its after-draw programs.
    // 220: Qinglong suspends the dodged Slash until its inserted use and all
    // children finish; every target can trigger it without a distance check.
    // 222: Trick continuations retain the original entity after a validated
    // native claim, including Hua Man's paid outgoing Souying acquisition.
    // 223: FireAttack AI resolves published Zhitian costs from its private
    // deck-top view as well as its hand, allowing the payment to complete.
    // 224: Native claims survive hidden selections, later trick targets,
    // Borrowed Sword children and group cleanup. Single-card trick conversions
    // consume their published target grant; response exchanges retain their
    // original accepted Use even without another finalized-target subscriber.
    public const int CurrentRulesVersion = 224;
    // 187 was consumed by main-side Zhang He / Shen Zhao Yun work.

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
                    RequestGeneralRosterCommand roster => roster.PromptId,
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
