using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZixiPlacementStage { Selecting, MovementChildren }
public sealed record ZixiPlacementEntity(int CardId, CardKind PrintedKind, CardLocation From, CardKind SlotKind);
public sealed record ZixiPlacementGrain(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record ZixiPlacementReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    ActualDiscardRecoveryPhaseKey Phase, int InstructionIndex)
{
    public ZixiPlacementStage Stage { get; init; }
    public bool Paid { get; init; }
    public int? TargetSeat { get; init; }
    public long SequenceBefore { get; init; }
    public long PrimaryAfter { get; init; }
    public long SequenceAfter { get; init; }
    public long BatchId { get; init; }
    public bool TailSealed { get; init; }
    private readonly IReadOnlyList<ZixiPlacementEntity> _entities = Array.AsReadOnly(Array.Empty<ZixiPlacementEntity>());
    public IReadOnlyList<ZixiPlacementEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<ZixiPlacementGrain> _grains = Array.AsReadOnly(Array.Empty<ZixiPlacementGrain>());
    public IReadOnlyList<ZixiPlacementGrain> Grains { get => _grains; init => _grains = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZixiPlacementReceipt? ZixiPlacement { get; init; }
}
public sealed record ZixiPlacementOpenedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record ZixiPlacementCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int EntityCount, long SequenceBefore) : IGameEvent;
public sealed record ZixiPlacementEntityFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardKind SlotKind) : IGameEvent;
public sealed record ZixiPlacementGrainFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, CardLocation From) : IGameEvent;
public sealed record ZixiQinPlacedEvent(long FrameId, int Index, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int CardId, CardKind PrintedKind, CardLocation From, CardKind SlotKind, long MovementSequence, long BatchId) : IGameEvent;
public sealed record ZixiPlacementPaidEvent(long FrameId, long BatchId, int EntityCount, long SequenceBefore, long PrimaryAfter) : IGameEvent;
public sealed record ZixiPlacementTailSealedEvent(long FrameId, long BatchId, int GrainCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record ZixiPlacementCompletedEvent(long FrameId, bool Paid, int EntityCount) : IGameEvent;

internal interface ILeDaQiaoZixiPlacementHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveZixiQinJudgmentPlacementDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new ZixiPlacementHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ZixiPlacementHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<ILeDaQiaoZixiPlacementHost>().Resolve(frame);
}
internal static class ZixiPlacementComposition
{
    [ProgramCatalogValidation(1190)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:zixi" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
                .Any(e => e.Op == SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement)) return;
        if (p.Id != "le:zixi" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Contributions.Count != 0 || p.Triggers.Count != 3 ||
            p.Triggers.SingleOrDefault(t => t.Id == "start-place-qin") is not { } start ||
            p.Triggers.SingleOrDefault(t => t.Id == "end-place-qin") is not { } end ||
            !Exact(start, SkillProgramTriggerWindow.PlayPhaseStarting) || !Exact(end, SkillProgramTriggerWindow.PlayEnding))
            throw new InvalidOperationException(path + ": Zixi placement requires its two exact optional actual Play boundaries and the separate unique-target effect trigger.");
    }
    private static bool Exact(SkillProgramTrigger t, SkillProgramTriggerWindow window) =>
        t.Window == window && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.MarkerCost is null && t.ChoiceGroup is null &&
        t.OrderGroup is null && !t.EvaluateConditionAtResolution && t.OwnerRelation is null && t.SourceSkillId is null &&
        t.SourceViewAsId is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 &&
        t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.MovementOccurrence is null &&
        !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded &&
        t.Effects is [{ Op: SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
}
