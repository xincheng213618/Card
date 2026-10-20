using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ForcedHandSelectionSnapshot(string SkillId, long LockId, long PhaseToken, int CardId, int RemainingUses);
public sealed partial record PendingDecision
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForcedHandSelectionSnapshot? ForcedHandSelection { get; init; }
}
public enum MoDiaoChanStage { Drawing, DrawChildren, DiscardChoice, CostChildren, TargetChoice, EntryChoice, Obtaining, GainChildren, RedirectChoice, RedirectChildren, Finished }
public sealed record MoDiaoChanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int Round, MoDiaoChanStage Stage)
{
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public long PromiseId { get; init; }
    public long CohortId { get; init; }
    public int ActorSeat { get; init; } = -1;
    public int ProviderSeat { get; init; } = -1;
    public int TargetSeat { get; init; } = -1;
    public int PreviousTarget { get; init; } = -1;
    public int Cursor { get; init; }
    public int CostCount { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MoDiaoChanUseClaim(long PromiseId, long ParentFrameId, long ActionId, int ActorSeat, int ProviderSeat, CardKind Kind,
    long PhaseToken, int ActualTurn, int Ordinal, CardConversionSource Source, string GameplayHash);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoDiaoChanReceipt? MoDiaoChan { get; init; } }
public abstract partial record ResolutionFrame
{
    private readonly IReadOnlyList<MoDiaoChanUseClaim>? _moDiaoChanClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MoDiaoChanUseClaim>? MoDiaoChanClaims { get => _moDiaoChanClaims; init => _moDiaoChanClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MoDiaoChanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int Round) : IGameEvent;
public sealed record MoDiaoChanPromiseEvent(long PromiseId, long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, int AfterTurnStartOrdinal) : IGameEvent;
public sealed record MoDiaoChanLockEvent(long LockId, int ActorSeat, int ActualTurn, long PhaseToken, int CardId, int PoolCount, int RandomIndex, ulong RandomBefore, ulong RandomAfter) : IGameEvent;
public sealed record MoDiaoChanUseClaimedEvent(MoDiaoChanUseClaim Claim) : IGameEvent;
public sealed record MoDiaoChanUseSettledEvent(long FrameId, long PromiseId, long ActionId) : IGameEvent;
public sealed record MoDiaoChanRandomDiscardEvent(long FrameId, int ActorSeat, int PoolCount, int RandomIndex, uint RandomBefore, uint RandomAfter, int? CardId) : IGameEvent;
public sealed record MoDiaoChanUseMaterialEvent(long UseId, long ActionId, int CardId, CardLocation From, long Before) : IGameEvent;
public sealed record MoDiaoChanLegacyDuelReceipt(long EntryId, long ProducerFrameId, int InstructionIndex, CardConversionSource ProducerSource,
    string ProducerGameplayHash, int ActorSeat, int TargetSeat, bool Returned = false, bool Settled = false, long WindowId = 0, bool DamageApplied = false, int FinalTarget = 0);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoDiaoChanLegacyDuelReceipt? MoDiaoChanLegacyDuel { get; init; } }
public sealed record MoDiaoChanDirectDuelWindowFrame(long Id, long ParentFrameId, MoDiaoChanLegacyDuelReceipt Duel,
    MoDiaoChanUseClaim Claim, ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)
    : ResolutionFrame(Id, ResolutionFrameKind.MoDiaoChanDirectDuelWindow, ResolutionFrameStep.ResolvingEffect);
public sealed record MoDiaoChanLegacyDuelIssuedEvent(MoDiaoChanLegacyDuelReceipt Receipt) : IGameEvent;
public sealed record MoDiaoChanLegacyDuelReturnedEvent(long EntryId, long ProducerFrameId, bool DamageApplied, int FinalTarget) : IGameEvent;
public sealed record MoDiaoChanLegacyDuelResumedEvent(long EntryId, long ProducerFrameId) : IGameEvent;
public sealed record MoDiaoChanCostEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MoDiaoChanMovementEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MoDiaoChanDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record MoDiaoChanQingshiEnteredEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int Round) : IGameEvent;
public sealed record MoDiaoChanCohortStartedEvent(long CohortId, long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int RecipientCount) : IGameEvent;
public sealed record MoDiaoChanCardGrantedEvent(long CohortId, long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId, CardKind Kind, long Sequence) : IGameEvent;
public sealed record MoDiaoChanObtainChoiceEvent(long FrameId, int RecipientSeat, int PoolCount, int RandomIndex, ulong RandomBefore, ulong RandomAfter, int? CardId) : IGameEvent;
public sealed record MoDiaoChanRetargetedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int ActorSeat, int ProviderSeat, int PreviousTarget, int NewTarget) : IGameEvent;
public sealed record MoDiaoChanRewardClaimedEvent(long FrameId, long UseId, int ChainCursor, CardConversionSource Source) : IGameEvent;
public sealed record MoDiaoChanDiscardClaimedEvent(long FrameId, CardConversionSource Source, int CardId, long Sequence) : IGameEvent;
public sealed record MoDiaoChanCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;

internal interface IMoDiaoChanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MoDiaoChanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound => [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)],
        SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)],
        SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],
        SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)]
    };
    public override ISkillProgramEffectHandler Handler => new MoDiaoChanHandler(Op);
}
internal sealed class MoDiaoChanRoundDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class MoDiaoChanUseDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse; }
internal sealed class MoDiaoChanPrepDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class MoDiaoChanRedirectDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect; }
internal sealed class MoDiaoChanDamageDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage; }
internal sealed class MoDiaoChanDiscardDescriptor : MoDiaoChanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoDiaoChanQingshiDiscard; }
internal sealed class MoDiaoChanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMoDiaoChanHost>().Resolve(f, op);
}
