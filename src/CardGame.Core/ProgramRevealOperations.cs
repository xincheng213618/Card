namespace CardGame.Core;

/// <summary>
/// Reveals one hand card of the selected target. The random mode draws a blind
/// candidate (equivalent to choosing among face-down cards); the chooser mode
/// privately shows the whole hand to the chooser before one card is revealed.
/// </summary>
internal sealed class RevealTargetHandCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTargetHandCard;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealTargetHandCardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.Reveal,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "chooserRef", "cardOwnerRef", "resultBind", "mode", "suits", "allowDecline", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var chooser = r.RequiredParticipantReference("chooserRef", ProgramParticipantRef.Owner);
        if (chooser.Kind != ProgramParticipantRef.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.chooserRef: only the owner may choose the revealed card.");
        var cardOwner = r.RequiredParticipantReference("cardOwnerRef");
        if (cardOwner.Kind != ProgramParticipantRef.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.cardOwnerRef: the revealed card must come from the selected target.");
        var suits = r.OptionalEnumArray<Suit>("suits") ?? Array.Empty<Suit>();
        if (r.Has("suits") && suits.Count == 0)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.suits: the eligible suit list must be nonempty.");
        var allowDecline = r.Has("allowDecline") && r.RequiredBool("allowDecline");
        if (allowDecline && suits.Count == 0)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.allowDecline: decline requires an eligible suit filter.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            chooserRef: chooser, cardOwnerRef: cardOwner,
            resultBind: r.RequiredIdentifier("resultBind"),
            revealMode: r.RequiredEnum<SkillProgramRevealMode>("mode"),
            suits: suits, allowDecline: allowDecline);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new CaptureSourceCard(effect.ResultBind!, 1,
            CardOwner: SkillProgramEffectTarget.SelectedTarget)];
}

public sealed class RevealTargetHandCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTargetHandCard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.RevealTargetHandCard(
            frame,
            effect.ChooserRef ?? throw new InvalidOperationException("revealTargetHandCard has no chooser."),
            effect.CardOwnerRef ?? throw new InvalidOperationException("revealTargetHandCard has no card owner."),
            effect.ResultBind ?? throw new InvalidOperationException("revealTargetHandCard has no result bind."),
            effect.RevealMode ?? throw new InvalidOperationException("revealTargetHandCard has no mode."),
            effect.Suits,
            effect.AllowDecline);
}
