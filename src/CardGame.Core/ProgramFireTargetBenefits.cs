namespace CardGame.Core;

public sealed record ProgramFireTargetBenefit(int InstructionIndex, long DamageWindowId, long DamageFrameId,
    int OwnerSeat, int TargetSeat, int ActualTurnNumber, int ActualTurnOwnerSeat,
    long SequenceBefore, long SequenceAfter, int DrawCount, long GrantSequence, CardUseEffectSource Source);
public sealed record ProgramFireTargetDrawIssuedEvent(long ProgramFrameId, long DamageWindowId,
    int OwnerSeat, int TargetSeat, int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;

public enum NamedCardAcquisitionStage { Selecting, MovementChildren }
public sealed record ProgramNamedCardAcquisition(int InstructionIndex, CardKind PrintedKind,
    NamedCardAcquisitionStage Stage, bool SkillsRemoved, int? CardId = null,
    CardLocation? From = null, long SequenceBefore = 0, long SequenceAfter = 0);
public sealed record ProgramNamedCardAcquiredEvent(long ProgramFrameId, int OwnerSeat,
    CardKind PrintedKind, CardZoneKind? SourceZone, bool Obtained) : IGameEvent;
public sealed record ProgramSelectedSkillsLostEvent(long ProgramFrameId, int OwnerSeat,
    string InitiatingSkillId, int RemovedGrantCount) : IGameEvent;

internal interface IFireTargetBenefitProgramHost
{
    SkillProgramStepOutcome DrawFireTargetAndGrantTurnUseQuota(ProgramSkillFrame frame);
    SkillProgramStepOutcome LoseSkillsAndObtainNamedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class DrawFireTargetAndGrantTurnUseQuotaDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota;
    public override ISkillProgramEffectHandler Handler { get; } = new FireTargetBenefitHandler(SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class LoseSkillsAndObtainNamedCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseSkillsAndObtainNamedCard;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new FireTargetBenefitHandler(SkillProgramEffectOp.LoseSkillsAndObtainNamedCard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "outputKind", "skillIds", "condition");
        var ids = r.RequiredIdentifierArray("skillIds");
        if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("Named-card acquisition requires distinct skills to lose.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), outputKind: r.RequiredEnum<CardKind>("outputKind"), skillIds: ids);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}
internal sealed class FireTargetBenefitHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota
            ? ((IFireTargetBenefitProgramHost)host).DrawFireTargetAndGrantTurnUseQuota(f)
            : ((IFireTargetBenefitProgramHost)host).LoseSkillsAndObtainNamedCard(f, e);
}

internal static class FireTargetBenefitComposition
{
    internal static void Validate(SkillProgram program)
    {
        foreach (var trigger in program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota)))
            if (trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.DamageSource ||
                trigger.Optional || trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
                trigger.Effects is not [{ Op: SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota }])
                throw new InvalidOperationException("Fire-target benefit requires a standalone mandatory per-damage source trigger.");
    }
}
