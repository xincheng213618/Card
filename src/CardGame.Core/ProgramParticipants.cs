using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ProgramParticipantRef
{
    Owner,
    Actor,
    EventTarget,
    EventSource,
    SelectedTarget,
    SelectedFirst,
    SelectedSecond,
    ResultSource,
    ResultOpponent
}

public sealed record ProgramParticipantReference
{
    public ProgramParticipantReference(ProgramParticipantRef kind, string? resultBind = null)
    {
        if (kind is ProgramParticipantRef.ResultSource or ProgramParticipantRef.ResultOpponent)
        {
            if (string.IsNullOrWhiteSpace(resultBind))
                throw new ArgumentException("A result participant requires an explicit result binding.", nameof(resultBind));
        }
        else if (resultBind is not null)
        {
            throw new ArgumentException("Only result participants may name a result binding.", nameof(resultBind));
        }
        Kind = kind;
        ResultBind = resultBind;
    }

    public ProgramParticipantRef Kind { get; }
    public string? ResultBind { get; }
}

public enum SkillProgramCardActionOwnerRelation
{
    Actor,
    Target,
    Observer,
    ConversionSource = 4
}

public sealed record CardUseDebitIdentity(
    long CardActionId,
    int ActorSeat,
    SkillRuleQuery Query,
    int TurnNumber,
    TurnPhase Phase,
    int PhaseInstanceId);

public sealed record ProgramCardUseContext(
    long CardActionId,
    long ParentCardUseFrameId,
    int ActorSeat,
    int? EventTargetSeat,
    CardKind EffectiveKind,
    Suit? PublicSuit,
    bool? IsPublicRed,
    bool WasUsageDebited,
    CardUseDebitIdentity? DebitIdentity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? DesignatedTargetSeats = null);
