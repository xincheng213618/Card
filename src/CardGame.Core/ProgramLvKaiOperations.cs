namespace CardGame.Core;

internal interface ILvKaiProgramHost
{
    SkillProgramStepOutcome TunanUseRevealedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 图南: the selected counterpart picks how to use the revealed top card —
// as itself (no distance limit) or converted into a Slash at normal distance.
internal sealed class TunanUseRevealedCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TunanUseRevealedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new TunanUseRevealedCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseBoundCardByTarget,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting),
         new ReadSelectedTarget(), new ReadCardSet(effect.SourceBind!),
         new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}

public sealed class TunanUseRevealedCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TunanUseRevealedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILvKaiProgramHost)host).TunanUseRevealedCard(f, e);
}

// 闭境 mark: registers the chosen hand cards as Bijing cards until their recast.
internal sealed class BijingMarkHandCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BijingMarkHandCards;
    public override ISkillProgramEffectHandler Handler { get; } = new BijingMarkHandCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.CaptureSelectedCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),
         new ReadCardSet(effect.SourceBind!)];
}

public sealed class BijingMarkHandCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BijingMarkHandCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILvKaiProgramHost)host).BijingMarkHandCards(f, e);
}

// 闭境 recast: at the owner's preparation phase the marked cards still in hand
// are discarded and replaced one-for-one.
internal sealed class BijingRecastMarkedCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BijingRecastMarkedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new BijingRecastMarkedCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class BijingRecastMarkedCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BijingRecastMarkedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILvKaiProgramHost)host).BijingRecastMarkedCards(f, e);
}

// 闭境 punish: when another character's discard phase starts and the owner lost
// a Bijing card during that turn, the turn owner discards two cards of choice.
internal sealed class BijingPunishDiscardPhaseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BijingPunishDiscardPhase;
    public override ISkillProgramEffectHandler Handler { get; } = new BijingPunishDiscardPhaseHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (effect, context) => context.DiscardSelected(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];
}

public sealed class BijingPunishDiscardPhaseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BijingPunishDiscardPhase;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILvKaiProgramHost)host).BijingPunishDiscardPhase(f, e);
}
