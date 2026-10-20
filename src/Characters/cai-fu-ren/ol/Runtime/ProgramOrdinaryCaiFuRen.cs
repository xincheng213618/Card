using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record OrdinaryQietingTurnContext(int ActualTurn, int TurnOwnerSeat, long HistoryCutoff, bool UsedOnAnother);
public sealed record OrdinaryQietingActualUseEvent(int ActualTurn, int TurnOwnerSeat, long NativeUseId,
    long? ActionId, int ActorSeat, bool UsedOnAnother, int DesignatedTargetCount) : IGameEvent;
public enum OrdinaryQietingStage { Choice, MovementChildren, Draw, DrawChildren, Complete }
public sealed record OrdinaryQietingReceipt(CardConversionSource Source, string GameplayHash,
    OrdinaryQietingTurnContext Qualification, long WindowId, int OccurrenceIndex, OrdinaryQietingStage Stage)
{
    public bool Paid { get; init; }
    public string? Option { get; init; }
    public int? CardId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _grainIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GrainIds { get => _grainIds; init => _grainIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrdinaryQietingTurnContext? OrdinaryQieting { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrdinaryQietingReceipt? OrdinaryQieting { get; init; }
}
public sealed record OrdinaryQietingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    OrdinaryQietingTurnContext Qualification, long WindowId, int OccurrenceIndex) : IGameEvent;
public sealed record OrdinaryQietingOptionIssuedEvent(long FrameId, string Option, int? CardId, CardKind? PrintedKind, long Before) : IGameEvent;
public sealed record OrdinaryQietingEquipmentMovedEvent(long FrameId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record OrdinaryQietingDrawReturnedEvent(long FrameId, int ActualCount, int ReplacementCount,
    long Before, long After, long? AttemptId) : IGameEvent;
public sealed record OrdinaryQietingCompletedEvent(long FrameId, bool Paid) : IGameEvent;

internal interface IOrdinaryCaiFuRenHost
{
    SkillProgramStepOutcome Xianzhou(ProgramSkillFrame frame, string stateId);
    SkillProgramStepOutcome Qieting(ProgramSkillFrame frame);
}
internal sealed class ResolveOrdinaryCaiFuRenXianzhouDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.PlaceSelectedEquipment());
    public override ISkillProgramEffectHandler Handler { get; } = new OrdinaryCaiFuRenXianzhouHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class OrdinaryCaiFuRenXianzhouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IOrdinaryCaiFuRenHost>().Xianzhou(f, e.StateId!);
}
internal sealed class ResolveOrdinaryCaiFuRenQietingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new OrdinaryCaiFuRenQietingHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class OrdinaryCaiFuRenQietingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IOrdinaryCaiFuRenHost>().Qieting(f);
}
internal static class OrdinaryCaiFuRenComposition
{
    [ProgramCatalogValidation(780)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou or SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting)) return;
        if (p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": ordinary Cai Fu Ren requires the complete donation and single ended-turn option contracts.");
        if (p.Id == "ol:xianzhou")
        {
            if (p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != "all-equipment-donation" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null || a.MarkerCost is not null || a.ContinueAfterOwnerDeath || a.Condition.Kind != SkillProgramConditionKind.Always || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou, Target: SkillProgramEffectTarget.Owner, StateId: "ordinary-xianzhou", Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": ordinary Xianzhou owns exactly one recipient and its post-payment limited opportunity.");
        }
        else
        {
            if (p.Id != "ol:qieting" || p.Activations.Count != 0 || p.Triggers is not [var t] || t.Id != "ordinary-turn-end" || t.Window != SkillProgramTriggerWindow.AfterTurnEnded || t.Subject != SkillProgramTriggerSubject.Owner || t.TurnOwnerScope != SkillProgramTurnOwnerScope.OtherLiving || !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.OrderGroup is not null || t.EvaluateConditionAtResolution || t.OwnerRelation is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.IncludeResponseUses || t.SingleActionInstance || t.DeferredTurnEndOnly || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": ordinary Qieting requires its exact actual-turn foreign-use qualification and one option.");
        }
    }
}
