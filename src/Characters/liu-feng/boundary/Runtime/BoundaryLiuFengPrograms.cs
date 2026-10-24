using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IBoundaryLiuFengHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class BoundaryLiuFengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new BoundaryLiuFengHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard, Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveBoundaryLiuFengXiansi ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)] : [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveBoundaryLiuFengXiansiDescriptor : BoundaryLiuFengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryLiuFengXiansi; }
internal sealed class ResolveBoundaryLiuFengQinlingDescriptor : BoundaryLiuFengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryLiuFengQinling; }
internal sealed class BoundaryLiuFengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IBoundaryLiuFengHost>().Begin(f, op);
}
internal static class BoundaryLiuFengComposition
{
    internal const string Xiansi = "boundary:xiansi", Qinling = "boundary:qinling", Activation = "discard-for-native-slash";
    [ProgramCatalogValidation(50200)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveBoundaryLiuFengXiansi or SkillProgramEffectOp.ResolveBoundaryLiuFengQinling)) return;
        var clean = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.Contributions.Count == 0 && p.CardIdentities.Count == 0 && p.BooleanStates.Count == 0;
        var valid = p.Id == Xiansi ? p.Activations.Count == 0 && p.Triggers is [{ Id: "collect-up-to-two", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, Optional: true, Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryLiuFengXiansi, Target: SkillProgramEffectTarget.Owner }] }] && p.CardPolicies is [{ Id: "public-pile-slash", Kind: SkillProgramCardPolicyKind.ForeignPublicPileSlash }] :
            p.Id == Qinling && p.Triggers.Count == 0 && p.CardPolicies.Count == 0 && p.Activations is [{ Id: Activation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryLiuFengQinling, Target: SkillProgramEffectTarget.Owner }] }];
        if (!clean || !valid) throw new InvalidOperationException(path + ": OL Liu Feng requires the two-card public-pile collection and original actual-turn discard before a wholly native zero-material Slash.");
    }
}
public enum BoundaryLiuFengStage { Collect, Targets, Cost, MovementChildren, Issuing, NativeUse, NativeTail, Completed, RewardDraw }
public sealed record BoundaryLiuFengReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, int TurnOwner, BoundaryLiuFengStage Stage)
{
    private IReadOnlyList<int> _cards = [], _targets = [];
    private IReadOnlyList<CardLocation> _from = [];
    private IReadOnlyList<CardMovementRecord> _moves = [];
    private IReadOnlyList<long> _batches = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
    public string? Movement { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long CostSequence { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public bool Damaged { get; init; }
    public bool ConsequencePaid { get; init; }
    public NativeDrawOutcome? RewardDraw { get; init; }
    public int BaseDamage { get; init; }
    public BoundaryLiuFengNeed? Need { get; init; }
    private PendingDecision? _originalDecision;
    public PendingDecision? OriginalDecision { get => _originalDecision; init => _originalDecision = value is null ? null : RequestedDeckBasicFrame.FreezeDecision(value); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryLiuFengReceipt? BoundaryLiuFeng { get; init; } }
public sealed record BoundaryLiuFengUseOrigin(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int Turn, int TurnOwner, int Actor, int BaseDamage);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryLiuFengUseOrigin? BoundaryLiuFeng { get; init; } }
public sealed record BoundaryLiuFengStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, int TurnOwner) : IGameEvent;
public sealed record BoundaryLiuFengSelectionEvent(long FrameId, int Count) : IGameEvent;
public sealed record BoundaryLiuFengCostPaidEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record BoundaryLiuFengMovementEvent(long FrameId, string Movement, int Count, long Before, long After) : IGameEvent;
public sealed record BoundaryLiuFengUseIssuedEvent(BoundaryLiuFengUseOrigin Origin) : IGameEvent;
public sealed record BoundaryLiuFengUseReturnedEvent(long FrameId, long UseId, long ActionId, bool Damaged) : IGameEvent;
public sealed record BoundaryLiuFengNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record BoundaryLiuFengConsequenceEvent(long FrameId, long UseId, bool Damaged, bool MovedCost) : IGameEvent;
public sealed record BoundaryLiuFengRewardDrawReturnedEvent(long FrameId, long UseId, long? AttemptFrameId, int ActualDrawCount, int ReplacementGainCount, long Before, long After) : IGameEvent;
public sealed record BoundaryLiuFengCompletedEvent(long FrameId) : IGameEvent;
public enum BoundaryLiuFengIntent { BorrowedSword, Qinglong, ProgramSlash, ProgramNearestSlash, AssistedSlash, NearestLegalSlash, FixedSlash, GenericBasic }
public sealed record BoundaryLiuFengNeed(BoundaryLiuFengIntent Intent, long OwnerFrameId, long RequestFrameId, long? ParentActionId, int ActorSeat, int InstructionIndex, SkillProgramEffectOp? RequestOp, IReadOnlyList<int> Targets)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Targets.ToArray());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public bool Equals(BoundaryLiuFengNeed? other) => other is not null && Intent == other.Intent && OwnerFrameId == other.OwnerFrameId && RequestFrameId == other.RequestFrameId && ParentActionId == other.ParentActionId && ActorSeat == other.ActorSeat && InstructionIndex == other.InstructionIndex && RequestOp == other.RequestOp && Targets.SequenceEqual(other.Targets);
    public override int GetHashCode() => HashCode.Combine(Intent, OwnerFrameId, RequestFrameId, ParentActionId, ActorSeat, InstructionIndex, RequestOp, Targets.Count);
}
public sealed record BoundaryLiuFengRequestStartedEvent(long FrameId, long OwnerFrameId, long RequestFrameId, int ActorSeat, int InstructionIndex, SkillProgramEffectOp? RequestOp, BoundaryLiuFengIntent Intent, PromptId PromptId, long Revision, string DecisionHash) : IGameEvent;
public sealed record BoundaryLiuFengRequestReleasedEvent(long FrameId, long OwnerFrameId, long RequestFrameId, bool Issued) : IGameEvent;
public sealed record BoundaryLiuFengKeActionEvent(long RequestFrameId, int InstructionIndex, long FrameId, int TargetSeat) : IGameEvent;
public sealed record BoundaryLiuFengCaller(long FrameId, CardConversionSource Source, string Hash, BoundaryLiuFengNeed Need, int CardId, CardLocation From, long CostSequence, int TargetSeat, long UseId = 0, long ActionId = 0, bool Returned = false);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryLiuFengCaller? BoundaryLiuFengCaller { get; init; } }
