namespace CardGame.Core;

// This receipt contains only immutable scalar identities/invoices. It is stored
// on the original AfterDamage program, never in an action-ID pending side table.
public sealed record ProgramCappedConversionBenefitReceipt(
    int InstructionIndex, CardConversionSource Source, string GameplayHash, string StateId,
    long DamageWindowFrameId, long DamageFrameId, int OccurrenceIndex,
    int RoundNumber, int FrozenSuccessfulModifications,
    long SequenceBefore, long SequenceAfter, int ActualDrawCount,
    bool ActualDrawAttempted, bool AwaitingMovement);

public sealed record CappedConversionBenefitIssuedEvent(long ProgramFrameId,
    ProgramCappedConversionBenefitReceipt Receipt) : IGameEvent;
public sealed record CappedConversionBenefitCompletedEvent(long ProgramFrameId,
    CardConversionSource Source, string StateId, int FrozenSuccessfulModifications,
    int ActualDrawCount, bool Modified, int FinalTier) : IGameEvent;

internal interface ICappedConversionBenefitHost
{
    SkillProgramStepOutcome DrawBeforeCappedConversionTierUpgrade(ProgramSkillFrame frame, string stateId);
}

internal sealed class DrawBeforeCappedConversionTierUpgradeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawBeforeCappedConversionTierUpgradeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e, c) => c.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw,
            SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A capped conversion benefit belongs to the original damage recipient.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class DrawBeforeCappedConversionTierUpgradeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) =>
        ((ICappedConversionBenefitHost)host).DrawBeforeCappedConversionTierUpgrade(f, e.StateId!);
}

internal static class CappedConversionBenefitComposition
{
    public static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade)) return;
        if (t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner ||
            !t.Optional || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Effects.Count != 1 ||
            t.Effects[0].Op != SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade)
            throw new InvalidOperationException(path + ": capped conversion requires one optional owner per-damage AfterDamage operation.");
    }
}
