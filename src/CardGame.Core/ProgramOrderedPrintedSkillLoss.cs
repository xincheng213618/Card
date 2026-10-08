using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum OrderedPrintedSkillLossStage { LossChildren, DrawChildren, Complete }

// Durable lost grants, rather than pending execution state. The initiating
// program alone owns the unfinished skill-change/draw continuation.
public sealed record OrderedPrintedSkillLossLease
{
    private IReadOnlyList<string> _skills = Array.AsReadOnly(Array.Empty<string>());
    private IReadOnlyList<SkillGrant> _grants = Array.AsReadOnly(Array.Empty<SkillGrant>());
    public long ProgramFrameId { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public required string TemplateSourceId { get; init; }
    public required string GeneralId { get; init; }
    public int FrozenAttackRange { get; init; }
    public IReadOnlyList<string> LostSkillIds { get => _skills; init => _skills = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<SkillGrant> RemovedGrants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
}

public sealed record ProgramOrderedPrintedSkillLossReceipt
{
    public int InstructionIndex { get; init; }
    public long DamageWindowId { get; init; }
    public long DamageFrameId { get; init; }
    public long AttackFrameId { get; init; }
    public int? DamageSourceSeat { get; init; }
    public int DamageTargetSeat { get; init; }
    public int DamageAmount { get; init; }
    public DamageNature DamageNature { get; init; }
    public bool SourceLess { get; init; }
    public required OrderedPrintedSkillLossLease Lease { get; init; }
    public OrderedPrintedSkillLossStage Stage { get; init; }
    public bool DrawIssued { get; init; }
    public int DrawActual { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOrderedPrintedSkillLossReceipt? OrderedPrintedSkillLoss { get; init; }
}

// Each fact is scalar. SkillGrant and its optional provenance records are also
// immutable scalar records; no exposed collection crosses commit preparation.
public sealed record OrderedPrintedSkillLossIssuedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, string TemplateSourceId,
    string GeneralId, int FrozenAttackRange, int LostSkillCount, int RemovedGrantCount) : IGameEvent;
public sealed record OrderedPrintedSkillLostEvent(long FrameId, int Ordinal, string SkillId) : IGameEvent;
public sealed record OrderedPrintedSkillGrantRemovedEvent(long FrameId, SkillGrant Grant) : IGameEvent;
public sealed record OrderedPrintedSkillLossDrawIssuedEvent(long FrameId, int RequestedCount,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record OrderedPrintedSkillLossCompletedEvent(long FrameId, int LostSkillCount, int DrawActual) : IGameEvent;
public sealed record OrderedPrintedSkillGrantRestoredEvent(long FrameId, int OwnerSeat, SkillGrant Grant,
    bool Restored, string Outcome) : IGameEvent;
public sealed record OrderedPrintedSkillLossExpiredEvent(long FrameId, int OwnerSeat, int ActualTurnNumber,
    int ActualTurnOwnerSeat, int RestoredCount, int SkippedCount) : IGameEvent;

internal interface IOrderedPrintedSkillLossProgramHost
{
    SkillProgramStepOutcome ExecuteOrderedPrintedSkillLoss(ProgramSkillFrame frame);
}

internal sealed class LoseFirstPrintedSkillsUntilTurnEndAndDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new LoseFirstPrintedSkillsUntilTurnEndAndDrawHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 1, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class LoseFirstPrintedSkillsUntilTurnEndAndDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IOrderedPrintedSkillLossProgramHost)host).ExecuteOrderedPrintedSkillLoss(frame);
}

internal static class OrderedPrintedSkillLossComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op == SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw;
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsOperation(e.Op))) return;
        if (t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Any || t.Optional ||
            t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            t.Effects is not [{ Op: SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw, Target: SkillProgramEffectTarget.Owner,
                Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }] || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.Suits.Count != 0 || t.MinimumRank != 1 || t.MaximumRank != 13 ||
            t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.CardKinds.Count != 0 ||
            t.DamageCardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly ||
            t.MovementOccurrence is not null || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive || t.UsageScope is not null ||
            t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.GainPhaseQualification is not null ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.ChoiceGroup is not null || t.OwnerRelation is not null ||
            t.AllowNoEventTarget || t.IncludeResponseUses || t.SingleActionInstance || t.NoDyingAtActivation || t.DeferredTurnEndOnly ||
            t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.MarkerCost is not null ||
            t.HpChangeOccurrence != SkillProgramHpChangeOccurrence.PerEvent || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
            throw new InvalidOperationException($"Invalid skill program at {path}: ordered printed-skill loss requires one mandatory unconditional per-damage participant operation.");
    }
}
