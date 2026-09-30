namespace CardGame.Core;

/// <summary>A completed action's public physical payment, offered once to another player.</summary>
public sealed record ProgramCompletedCardGiftDraft(long CardActionId, int? RecipientSeat = null, bool GiftWasRed = false, int? RequestTargetSeat = null);
public sealed record CompletedCardGiftedEvent(long FrameId, long CardActionId, int OwnerSeat, int RecipientSeat,
    IReadOnlyList<int> CardIds, bool IsRed) : IGameEvent;

internal interface ICompletedCardGiftProgramHost
{
    SkillProgramStepOutcome OfferCompletedCardGift(ProgramSkillFrame frame);
}

internal sealed class OfferCompletedCardGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCompletedCardGift;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferCompletedCardGiftHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A completed-card gift belongs to the card action's actor.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class OfferCompletedCardGiftHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCompletedCardGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ICompletedCardGiftProgramHost)host).OfferCompletedCardGift(frame);
}
