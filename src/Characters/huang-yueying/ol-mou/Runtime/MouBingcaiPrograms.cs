using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum MouBingcaiStage { MaterialChoice, CostChildren, Drawing, DrawChildren, AddNameChoice }

public sealed record MouBingcaiReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId, CardKind BasicKind)
{
    public MouBingcaiStage Stage { get; init; }
    public int? MaterialId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public CardKind? EffectiveKind { get; init; }
    public CardLocation? From { get; init; }
    public Suit EffectiveSuit { get; init; }
    public int? EffectiveRank { get; init; }
    public bool Paid { get; init; }
    public bool MatchesDamage { get; init; }
    public bool IsTrick { get; init; }
    public long RecastInvocationId { get; init; }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long MovementSequence { get; init; }
    public long DrawAttemptFrameId { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    private readonly IReadOnlyList<int> _actualDrawIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ActualDrawIds { get => _actualDrawIds; init => _actualDrawIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _replacementGainedIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ReplacementGainedIds { get => _replacementGainedIds; init => _replacementGainedIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouBingcaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId, CardKind BasicKind) : IGameEvent;
public sealed record MouBingcaiRecastPaidEvent(long FrameId, long RecastInvocationId, long BatchId, int OwnerSeat, int CardId, CardKind PrintedKind, CardKind EffectiveKind, CardLocation From, long MovementSequence, long Before, long After, bool MatchesDamage, bool IsTrick) : IGameEvent;
public sealed record MouBingcaiDrawReturnedEvent(long FrameId, long AttemptFrameId, long SequenceBefore, long SequenceAfter, int ActualDrawCount, int ReplacementGainedCount) : IGameEvent;
public sealed record MouBingcaiCompletedEvent(long FrameId, long UseId, bool Recast, bool Matched, bool AddedName) : IGameEvent;
internal sealed class MouBingcaiDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingBingcai;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouBingcaiHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouBingcaiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingBingcai;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouHuangYueyingHost>().ResolveMouHuangYueyingBingcai(frame);
}
