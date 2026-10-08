namespace CardGame.Core;

// A durable direction, not pending resolution state. Its event-history position
// makes the owner's next actual TurnStarted the expiry boundary.
public sealed record DamageCounterpartAcquisitionGrantedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, string TargetSkillId, int CounterpartSeat,
    long DamageFrameId, long AttackFrameId, int DamageSourceSeat, int DamageTargetSeat,
    int Amount, DamageNature Nature, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;

internal static class DamageCounterpartAcquisitionContract
{
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var p in programs.Values)
        foreach (var t in p.Triggers)
        foreach (var e in t.Effects.Where(e => e.Op == SkillProgramEffectOp.RecordDamageCounterpartAcquisition))
            if (e.SkillIds is not [var skillId] || !programs.TryGetValue(skillId, out var target) || !target.Triggers.Any(trigger =>
                    trigger.Effects is [{ Op: SkillProgramEffectOp.OfferPairedColorCardDisposition } paired] && paired.StateId == e.StateId))
                throw new InvalidOperationException($"Invalid skill program at {p.Id}: counterpart acquisition must name an existing paired-color disposition skill and matching state.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordDamageCounterpartAcquisition)) return;
        if (t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Any || t.Optional ||
            t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.EvaluateConditionAtResolution || t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null ||
            t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
            t.DamageCardKinds.Count != 0 || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.RecordDamageCounterpartAcquisition, Target: SkillProgramEffectTarget.Owner,
                StateId: not null, SkillIds: [not null], Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: damage counterpart acquisition requires one mandatory any-subject perDamage afterDamageApplied instruction.");
    }
}
internal interface IDamageCounterpartAcquisitionHost
{
    SkillProgramStepOutcome RecordDamageCounterpartAcquisition(ProgramSkillFrame frame, string stateId, string skillId);
}
internal sealed class RecordDamageCounterpartAcquisitionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordDamageCounterpartAcquisition;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageCounterpartAcquisitionHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "skillId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"), skillIds: [r.RequiredIdentifier("skillId")]);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DamageCounterpartAcquisitionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecordDamageCounterpartAcquisition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IDamageCounterpartAcquisitionHost)host).RecordDamageCounterpartAcquisition(frame, effect.StateId!, effect.SkillIds.Single());
}
