namespace CardGame.Core;

public sealed record ProgramJudgmentEntityOriginEvent(long JudgmentFrameId, int SubjectSeat, int CardId, Suit EffectiveSuit, int EntryMovementSequence) : IGameEvent;
public sealed record ProgramDiscardedEntityOriginEvent(int MovementSequence, int SourceSeat, Suit EffectiveSuit, long? JudgmentFrameId) : IGameEvent;
public sealed record ProgramDiscardedEntityClaimedEvent(long FrameId, CardConversionSource Origin, string ProvenanceId, int CardId,
    int DiscardMovementSequence, int ClaimMovementSequence, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record ProgramProvenanceFaceUpResetEvent(int OwnerSeat, int ActualTurnNumber) : IGameEvent;
public enum ProgramProvenanceClaimStage { Claiming, Claimed, Choosing, Flipping }
public sealed record ProgramProvenanceClaimReceipt(int InstructionIndex, string ProvenanceId, int CardId, int DiscardMovementSequence,
    int ClaimMovementSequence, ProgramProvenanceClaimStage Stage);
public interface IDiscardedProvenanceProgramHost
{
    SkillProgramStepOutcome ClaimDiscardedEntityWithProvenance(ProgramSkillFrame frame, string provenanceId);
    SkillProgramStepOutcome OfferFaceUpForOutsideClaims(ProgramSkillFrame frame, string provenanceId);
    SkillProgramStepOutcome UseVirtualAlcohol(ProgramSkillFrame frame);
}
internal sealed class ClaimDiscardedEntityWithProvenanceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimDiscardedEntityWithProvenanceHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimMovedCards, static (_, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "provenanceId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), sourceBind:r.RequiredIdentifier("provenanceId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
internal sealed class OfferFaceUpForOutsideClaimsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferFaceUpForOutsideClaims;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferFaceUpForOutsideClaimsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "provenanceId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), sourceBind:r.RequiredIdentifier("provenanceId"));
        RequireAlways(e,r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
internal sealed class UseVirtualAlcoholDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualAlcohol;
    public override ISkillProgramEffectHandler Handler { get; } = new UseVirtualAlcoholHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition()); RequireAlways(e,r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireActivationEntry()];
}
public sealed class ClaimDiscardedEntityWithProvenanceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IDiscardedProvenanceProgramHost)host).ClaimDiscardedEntityWithProvenance(f,e.SourceBind!);
}
public sealed class OfferFaceUpForOutsideClaimsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferFaceUpForOutsideClaims;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IDiscardedProvenanceProgramHost)host).OfferFaceUpForOutsideClaims(f,e.SourceBind!);
}
public sealed class UseVirtualAlcoholHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualAlcohol;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IDiscardedProvenanceProgramHost)host).UseVirtualAlcohol(f);
}
internal static class DiscardedProvenanceComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window, int cards, bool target, int targets)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.UseVirtualAlcohol) &&
            (window is not null || cards != 0 || target || targets != 0 || effects.Count != 1))
            throw new InvalidOperationException($"Invalid skill program at {path}: useVirtualAlcohol requires one standalone zero-card zero-target activation.");
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance or SkillProgramEffectOp.OfferFaceUpForOutsideClaims)) return;
        if (window != SkillProgramTriggerWindow.DiscardPileReceived || effects.Count != 2 ||
            effects[0].Op != SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance || effects[1].Op != SkillProgramEffectOp.OfferFaceUpForOutsideClaims || effects[0].SourceBind != effects[1].SourceBind)
            throw new InvalidOperationException($"Invalid skill program at {path}: provenance claim requires an exact claim/threshold pair in discardPileReceived.");
    }
}
