using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum OrdinaryBingyiStage { SelectingRecipients, Drawing, DrawChildren }
public sealed record OrdinaryBingyiShownCard(int CardId, CardKind PrintedKind, Suit EffectiveSuit);
public sealed record OrdinaryBingyiReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, long WindowId, long BatchId, int OccurrenceIndex,
    bool UniformColor, bool? Color, OrdinaryBingyiStage Stage)
{
    public bool Paid { get; init; }
    public bool RecipientsCommitted { get; init; }
    public int Cursor { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<OrdinaryBingyiShownCard> _shownCards = Array.AsReadOnly(Array.Empty<OrdinaryBingyiShownCard>());
    public IReadOnlyList<OrdinaryBingyiShownCard> ShownCards { get => _shownCards; init => _shownCards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _otherRecipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> OtherRecipients { get => _otherRecipients; init => _otherRecipients = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrdinaryBingyiReceipt? OrdinaryBingyi { get; init; }
}
public sealed record OrdinaryBingyiShownEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, long WindowId, long BatchId, int OccurrenceIndex,
    int HandCount, bool UniformColor, bool? Color) : IGameEvent;
public sealed record OrdinaryBingyiShownEntityEvent(long FrameId, int Index, int CardId,
    CardKind PrintedKind, Suit EffectiveSuit) : IGameEvent;
public sealed record OrdinaryBingyiRecipientsCommittedEvent(long FrameId, int Count) : IGameEvent;
public sealed record OrdinaryBingyiRecipientEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record OrdinaryBingyiDrawReturnedEvent(long FrameId, int DrawIndex, int RecipientSeat,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record OrdinaryBingyiRecipientSkippedEvent(long FrameId, int DrawIndex, int RecipientSeat) : IGameEvent;
public sealed record OrdinaryBingyiCompletedEvent(long FrameId, bool UniformColor, int RecipientCount) : IGameEvent;

internal interface IOrdinaryGuYongHost
{
    SkillProgramStepOutcome Bingyi(ProgramSkillFrame frame);
}
internal sealed class ResolveOrdinaryGuYongBingyiDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new OrdinaryGuYongBingyiHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class OrdinaryGuYongBingyiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IOrdinaryGuYongHost>().Bingyi(f);
}
internal static class OrdinaryGuYongComposition
{
    [ProgramCatalogValidation(790)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi)) return;
        if (p.Id != "ol:bingyi" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.Triggers is not [var t] ||
            t.Id != "ordinary-discard-reveal-share" || t.Window != SkillProgramTriggerWindow.DiscardPileReceived || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || !t.MovementDiscardOnly || t.DiscardOwnerScope != SkillProgramDiscardOwnerScope.Own || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch ||
            t.SourceZones.Count != 2 || !t.SourceZones.Contains(CardZoneKind.Hand) || !t.SourceZones.Contains(CardZoneKind.Equipment) || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.OrderGroup is not null || t.EvaluateConditionAtResolution || t.OwnerRelation is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.DamageOccurrence is not null || t.IncludeResponseUses || t.SingleActionInstance || t.DeferredTurnEndOnly || t.IgnoreOwnSkillMovements || t.GainPhaseQualification is not null || t.NoDyingAtActivation || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.AllowNoEventTarget || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": ordinary Bingyi requires an optional whole owned-HE discard batch and its own actual-phase, reveal and recipient cursor.");
    }
}
