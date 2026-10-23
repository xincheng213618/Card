namespace CardGame.Core;

internal interface IOwnTurnEnemyHandGainProgramHost
{
    void BindOwnTurnEnemyHandGain(ProgramSkillFrame frame);
}

// The binding reuses the current movement window and ordinary selected target;
// the following shared Damage instruction owns all native damage children.
internal sealed class BindOwnTurnEnemyHandGainDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindOwnTurnEnemyHandGain;
    public override ISkillProgramEffectHandler Handler { get; } = new BindOwnTurnEnemyHandGainHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.CaptureSelectedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var threshold = reader.RequiredInt("amount");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || threshold is < 0 or > 256)
            throw new InvalidOperationException("Own-turn enemy hand-gain binding needs its owner and a bounded hand threshold.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, threshold, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained), new SelectSingleTarget()];
}

internal sealed class BindOwnTurnEnemyHandGainHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BindOwnTurnEnemyHandGain;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IOwnTurnEnemyHandGainProgramHost>().BindOwnTurnEnemyHandGain(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal static class OwnTurnEnemyHandGainComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.BindOwnTurnEnemyHandGain)) return;
        if (trigger.Window != SkillProgramTriggerWindow.CardsGained || trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.Optional ||
            trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || !trigger.DestinationZones.SequenceEqual([CardZoneKind.Hand]) ||
            trigger.UsageLimit is not null || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.BindOwnTurnEnemyHandGain },
                { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1,
                    ActorReference: null, TargetReference: null, DamageNature: null, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"{path}: own-turn enemy gain requires one mandatory hand-batch binding followed by one ordinary selected-target damage.");
    }
}
