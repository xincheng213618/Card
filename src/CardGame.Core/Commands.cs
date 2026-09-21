using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>
/// A monotonically increasing identifier for one published player prompt.
/// Zero is reserved for a command that does not answer a prompt.
/// </summary>
public readonly record struct PromptId(long Value)
{
    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A stable identifier for one exact choice in a prompt.</summary>
public readonly record struct ChoiceId
{
    [JsonConstructor]
    public ChoiceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A choice id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// One complete selection exposed by a pending prompt. Cards and targets are
/// paired by this object; callers must not reconstruct legal combinations.
/// </summary>
public sealed record PromptChoice(
    ChoiceId Id,
    string Description,
    IReadOnlyList<int> Cards,
    IReadOnlyList<int> Targets,
    IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>
    /// Optional content references for non-card prompts, such as selecting a
    /// general. A choice still represents one complete legal selection.
    /// </summary>
    public IReadOnlyList<string> ContentIds { get; init; } = [];
}

/// <summary>Stable error categories for player-originated command rejection.</summary>
public enum CommandErrorCode
{
    None,
    NullCommand,
    NotStarted,
    AlreadyStarted,
    Completed,
    StaleRevision,
    InvalidActor,
    NotActorTurn,
    InvalidPrompt,
    InvalidChoice,
    InvalidCard,
    InvalidTarget,
    InvalidGeneral,
    IllegalAction,
    UnsupportedCommand,
    ReentrantOperation
}

public sealed record CommandError(CommandErrorCode Code, string Message);

/// <summary>
/// The only command input understood by the K2 control boundary. Commands are
/// data only, so they can later be recorded and replayed without WPF objects.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StartGameCommand), "start")]
[JsonDerivedType(typeof(AdvanceCommand), "advance")]
[JsonDerivedType(typeof(AdvanceOneStepCommand), "advance-one-step")]
[JsonDerivedType(typeof(PlayCardCommand), "play-card")]
[JsonDerivedType(typeof(RecastCardCommand), "recast-card")]
[JsonDerivedType(typeof(EndPlayPhaseCommand), "end-play")]
[JsonDerivedType(typeof(DiscardCardsCommand), "discard-cards")]
[JsonDerivedType(typeof(SelectGeneralCommand), "select-general")]
[JsonDerivedType(typeof(RevealGeneralCommand), "reveal-general")]
[JsonDerivedType(typeof(AnswerPromptCommand), "answer-prompt")]
[JsonDerivedType(typeof(RespondCommand), "respond")]
[JsonDerivedType(typeof(UseSkillCommand), "use-skill")]
[JsonDerivedType(typeof(UseEquipmentEffectCommand), "use-equipment-effect")]
[JsonDerivedType(typeof(UseProgramSkillCommand), "use-program-skill")]
public abstract record GameCommand(int ActorSeat, long ExpectedRevision);

/// <summary>Starts an unstarted match. ActorSeat is the trusted host (-1).</summary>
public sealed record StartGameCommand(long ExpectedRevision = 0) : GameCommand(-1, ExpectedRevision);

/// <summary>
/// Pumps deterministic AI until the next player boundary or game end. ActorSeat
/// defaults to the trusted host (-1).
/// </summary>
public sealed record AdvanceCommand(long ExpectedRevision, int ActorSeat = -1) :
    GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Commits one state-machine step, preserving paced host playback in the journal.</summary>
public sealed record AdvanceOneStepCommand(long ExpectedRevision, int ActorSeat = -1) :
    GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Uses one card with one exact target list from the current play prompt.</summary>
public sealed record PlayCardCommand(
    int ActorSeat,
    int CardId,
    IReadOnlyList<int> TargetSeats,
    long ExpectedRevision,
    PromptId? PromptId = null,
    CardKind? PlayedCardKind = null,
    int? TargetCardId = null) : GameCommand(ActorSeat, ExpectedRevision)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardConversionSource? ConversionSource { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillKind? CardKindModifierSkill { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillKind? TargetCountModifierSkill { get; init; }
}

/// <summary>Recasts an eligible physical hand card without using it as a trick.</summary>
public sealed record RecastCardCommand(int ActorSeat, int CardId, long ExpectedRevision, PromptId? PromptId = null)
    : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>
/// Uses the actor's currently available active skill. CardIds and TargetSeats
/// are exact private selections when the skill's published action requires
/// them; cardless skills keep both lists empty.
/// </summary>
public sealed record UseSkillCommand(
    int ActorSeat,
    SkillKind Skill,
    IReadOnlyList<int> CardIds,
    IReadOnlyList<int> TargetSeats,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Invokes one published activation from a compiled skill program.</summary>
public sealed record UseProgramSkillCommand(
    int ActorSeat,
    string SkillId,
    string ActivationId,
    IReadOnlyList<int> CardIds,
    IReadOnlyList<int> TargetSeats,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision)
{
    /// <summary>Identifies a different character who owns the invoked skill.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkillOwnerSeat { get; init; }
}

/// <summary>
/// Uses an equipped card's active conversion with an exact private card and
/// target selection. The equipment itself remains in its public slot unless
/// the published effect explicitly consumes it.
/// </summary>
public sealed record UseEquipmentEffectCommand(
    int ActorSeat,
    CardKind EquipmentKind,
    IReadOnlyList<int> CardIds,
    IReadOnlyList<int> TargetSeats,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Ends the current actor's play phase.</summary>
public sealed record EndPlayPhaseCommand(
    int ActorSeat,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Discards an exact, distinct subset from a private hand-limit prompt.</summary>
public sealed record DiscardCardsCommand(
    int ActorSeat,
    IReadOnlyList<int> CardIds,
    PromptId PromptId,
    long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Selects one exact general from the actor's private setup prompt.</summary>
public sealed record SelectGeneralCommand(
    int ActorSeat,
    string GeneralId,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>
/// Publicly reveals one selected general in the national-war lite adapter.
/// The current play or supported response prompt supplies the revision and
/// optional prompt id.
/// </summary>
public sealed record RevealGeneralCommand(
    int ActorSeat,
    GeneralSelectionSlot Slot,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(ActorSeat, ExpectedRevision);

/// <summary>Answers a published prompt by its exact stable choice identifier.</summary>
public sealed record AnswerPromptCommand(
    int ActorSeat,
    PromptId Prompt,
    ChoiceId Choice,
    long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision)
{
    public PromptId PromptId => Prompt;
}

/// <summary>Compatibility spelling for callers that model Dodge as a response.</summary>
public sealed record RespondCommand(
    int ActorSeat,
    PromptId Prompt,
    ChoiceId Choice,
    long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision)
{
    public PromptId PromptId => Prompt;
}

/// <summary>
/// Result of a command attempt. Rejected commands still return the current safe
/// result so a host can render the reason without catching a player-input error.
/// </summary>
public sealed record CommandResult(
    bool Accepted,
    CommandError? Error,
    long Revision,
    EngineRunResult Result)
{
    public EngineStatus Status => Result.Status;

    public Winner Winner => Result.Winner;

    public GameSnapshot State => Result.State;

    public PendingDecision? PendingDecision => Result.PendingDecision;
}

/// <summary>
/// JSON boundary for a trusted-host command journal. A journal contains only
/// immutable command data; it is never part of a player snapshot.
/// </summary>
public static class CommandJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IEnumerable<GameCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return JsonSerializer.Serialize(commands.ToArray(), Options);
    }

    public static IReadOnlyList<GameCommand> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<List<GameCommand>>(json, Options) ?? [];
    }
}
