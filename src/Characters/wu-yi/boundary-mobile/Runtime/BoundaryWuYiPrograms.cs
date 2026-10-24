using System.Text.Json.Serialization;

namespace CardGame.Core;

internal static class BoundaryWuYiComposition
{
    internal const string Skill = "boundary:benxi-mobile", Cost = "benxi-cost", Start = "pay-play-start", Targets = "next-use-targets", Reward = "issued-use-reward";

    [ProgramCatalogValidation(53100)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.PrepareBoundaryWuYiBenxi or SkillProgramEffectOp.ArmBoundaryWuYiBenxi or
                SkillProgramEffectOp.ResolveBoundaryWuYiTargets or SkillProgramEffectOp.RewardBoundaryWuYiUse)) return;
        if (program.Id != Skill || program.Activations.Count != 0 || program.Triggers.Count != 3 ||
            program.Triggers.SingleOrDefault(t => t.Id == Start) is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting, Optional: true,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own,
                Effects: [{ Op: SkillProgramEffectOp.PrepareBoundaryWuYiBenxi }, { Op: SkillProgramEffectOp.SelectOwnedCards, MinimumCards: 1, MaximumCards: int.MaxValue, ResultBind: Cost },
                    { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: Cost, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true }, { Op: SkillProgramEffectOp.ArmBoundaryWuYiBenxi }] } ||
            program.Triggers.SingleOrDefault(t => t.Id == Targets) is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, SingleActionInstance: true,
                Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryWuYiTargets }] } ||
            program.Triggers.SingleOrDefault(t => t.Id == Reward) is not { Window: SkillProgramTriggerWindow.CardUseCompleted,
                OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, SingleActionInstance: true,
                Effects: [{ Op: SkillProgramEffectOp.RewardBoundaryWuYiUse }, { Op: SkillProgramEffectOp.Draw, Amount: 5 }, { Op: SkillProgramEffectOp.AwaitOwnedCardMovement }] })
            throw new InvalidOperationException(path + ": mobile boundary Benxi requires one native whole-cost Play start, one issued first eligible use and its whole-use damage reward.");
        var start = program.Triggers.Single(t => t.Id == Start);
        var targets = program.Triggers.Single(t => t.Id == Targets);
        var reward = program.Triggers.Single(t => t.Id == Reward);
        if (!start.Effects[1].Zones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) ||
            !targets.CardCategories.Order().SequenceEqual(new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.InstantTrick }.Order()) ||
            targets.IsSkillInvocation || reward.IsSkillInvocation || targets.Subject is not null ||
            program.Triggers.Any(t => t.IncludeResponseUses || t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null ||
                t.SourceSkillId is not null || t.SourceViewAsId is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path + ": mobile boundary Benxi must preserve its exact source, all-owned-zone cost and one whole native eligible use without another response or usage gate.");
    }
}

internal interface IBoundaryWuYiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class BoundaryWuYiDescriptor : ProgramOperationDescriptorBase, IActualPlayPhaseUseLedgerOperation
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new BoundaryWuYiHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveBoundaryWuYiTargets ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.PrepareBoundaryWuYiBenxi => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        SkillProgramEffectOp.ArmBoundaryWuYiBenxi => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting), new ReadCardSet(BoundaryWuYiComposition.Cost)],
        SkillProgramEffectOp.ResolveBoundaryWuYiTargets => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new RequireCardActionActor()]
    };
}
internal sealed class PrepareBoundaryWuYiBenxiDescriptor : BoundaryWuYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareBoundaryWuYiBenxi; }
internal sealed class ArmBoundaryWuYiBenxiDescriptor : BoundaryWuYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmBoundaryWuYiBenxi; }
internal sealed class ResolveBoundaryWuYiTargetsDescriptor : BoundaryWuYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryWuYiTargets; }
internal sealed class RewardBoundaryWuYiUseDescriptor : BoundaryWuYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardBoundaryWuYiUse; }
internal sealed class BoundaryWuYiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IBoundaryWuYiHost>().Execute(frame, op);
}

public sealed record BoundaryWuYiPhaseReceipt(CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase, long Before);
public sealed record BoundaryWuYiBenxiGrantedEvent(long FrameId, CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase, int X, long Before, long After) : IGameEvent;
public sealed record BoundaryWuYiBenxiConsumedEvent(long GrantFrameId, long ActionId, long UseId, int Actor, CardKind Kind, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record BoundaryWuYiRewardIssuedEvent(long FrameId, long GrantFrameId, long UseId, long ActionId, int Owner) : IGameEvent;
public sealed record BoundaryWuYiTargetDraft(BoundaryWuYiBenxiGrantedEvent Grant, long UseId, long ActionId, long WindowId)
{
    private readonly IReadOnlyList<int> _original = [], _added = [];
    public IReadOnlyList<int> Original { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Added { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryWuYiPhaseReceipt? BoundaryWuYiPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryWuYiTargetDraft? BoundaryWuYiTargets { get; init; }
}
