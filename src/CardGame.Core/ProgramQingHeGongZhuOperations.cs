namespace CardGame.Core;

// 长姬: a finished use that designated several targets including the owner
// draws one card per other designated target.
internal sealed class ChangjiDesignationDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiDesignationDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new ChangjiDesignationDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.AdjustNormalDraw,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}

public sealed class ChangjiDesignationDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiDesignationDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IQingHeGongZhuProgramHost)host).ChangjiDesignationDraw(f, e);
}

// 谮构 gift: the selected hand cards move to the activation's recipient, the
// owner draws the same count, and the moved entities are marked as 谮构 cards.
internal sealed class ZengouGiftMarkedCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouGiftMarkedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ZengouGiftMarkedCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiftMarkedCards,
        static (effect, context) => context.GiftMarkedCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("A Zengou gift requires its selected recipient.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new ReadSelectedTarget(),
        new ReadCardSet(effect.SourceBind!),
        new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.SelectedTargetHand)
    ];
}

public sealed class ZengouGiftMarkedCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouGiftMarkedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IQingHeGongZhuProgramHost)host).ZengouGiftMarkedCards(f, e);
}

// 谮构 punish: the gifted character's next HP increase or card use reveals the
// whole hand and costs one HP per marked card still held; mandatory.
internal sealed class ZengouPunishRecipientDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouPunishRecipient;
    public override ISkillProgramEffectHandler Handler { get; } = new ZengouPunishRecipientHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (effect, context) => context.LoseHp(new(
            SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.SelectedTarget, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows(
            [SkillProgramTriggerWindow.CardUseCommitted, SkillProgramTriggerWindow.AfterHealthChanged])];
}

public sealed class ZengouPunishRecipientHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouPunishRecipient;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IQingHeGongZhuProgramHost)host).ZengouPunishRecipient(f, e);
}

// Public-only pricing for the 谮构 gift: the recipient visibly gains the marked
// entities and the owner redraws the same number of unknown cards.
internal sealed partial class ProgramAiEstimateContext
{
    internal void GiftMarkedCards(SkillProgramEffect effect)
    {
        _targetDraw += 1d;
        _otherAdjustment += 1d;
    }
}
