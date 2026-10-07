namespace CardGame.Core;

internal interface IYanJunProgramHost
{
    SkillProgramStepOutcome GuanchaoChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome GuanchaoRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome XunxianGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 观潮 launch: at the play phase start the owner commits this phase to strictly
// ascending or strictly descending used-card ranks.
internal sealed class GuanchaoChoosePatternDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoChoosePattern;
    public override ISkillProgramEffectHandler Handler { get; } = new GuanchaoChoosePatternHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class GuanchaoChoosePatternHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoChoosePattern;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYanJunProgramHost)host).GuanchaoChoosePattern(f, e);
}

// 观潮 draw: every committed own play-phase use checks the whole rank ledger of
// this phase (including the current card) against the committed pattern.
internal sealed class GuanchaoRankDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoRankDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new GuanchaoRankDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.AdjustNormalDraw,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),
        new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [])
    ];
}

public sealed class GuanchaoRankDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoRankDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYanJunProgramHost)host).GuanchaoRankDraw(f, e);
}

// 逊贤: a used or played card that settled into the discard pile is given to one
// character with more hand cards or more health than the owner.
internal sealed class XunxianGiftUsedCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XunxianGiftUsedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new XunxianGiftUsedCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiftSettledUsedCard,
        static (effect, context) => context.GiftSettledUsedCard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

public sealed class XunxianGiftUsedCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XunxianGiftUsedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYanJunProgramHost)host).XunxianGiftUsedCard(f, e);
}

// Public-only pricing for the 逊贤 transfer: the recipient visibly gains the
// settled entity and the owner's board mass shifts by the same public card.
internal sealed partial class ProgramAiEstimateContext
{
    internal void GiftSettledUsedCard(SkillProgramEffect effect)
    {
        _targetDraw += 1d;
        _otherAdjustment += 1d;
    }
}
