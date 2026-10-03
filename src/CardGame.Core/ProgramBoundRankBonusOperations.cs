namespace CardGame.Core;

internal interface IBoundRankBonusProgramHost
{
    void RevealTopCardsWithNextBooleanBonus(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ObtainBoundCardsAndArmNextRevealBonus(ProgramSkillFrame frame, SkillProgramEffect effect);
    void RecoverOtherDyingVictimTo(ProgramSkillFrame frame, int hp);
}

internal sealed class RevealTopCardsWithNextBooleanBonusDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealTopCardsWithNextBooleanBonusHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal,
        static (effect, context) => context.Reveal(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "bonusAmount", "stateId", "resultBind", "visibility", "condition");
        var amount = r.RequiredInt("amount"); var bonus = r.RequiredInt("bonusAmount");
        var visibility = r.RequiredEnum<SkillProgramCardSetVisibility>("visibility");
        if (amount is < 1 or > 16 || bonus is < 1 or > 16 || amount + bonus > CardSubsetSelector.MaximumCandidateCount ||
            visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a next-use reveal requires a public pool with bounded base and bonus counts.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"), visibility: visibility,
            stateId: r.RequiredIdentifier("stateId"), maximumCards: bonus);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new CreateCardSet(effect.ResultBind!, effect.Amount + effect.MaximumCards, true)];
}

internal sealed class ObtainBoundCardsAndArmNextRevealBonusDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainBoundCardsAndArmNextRevealBonusHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "stateId", "maximumRankSum", "condition");
        var rank = r.RequiredInt("maximumRankSum");
        if (rank is < 1 or > 208) throw new InvalidOperationException($"Invalid skill program at {r.Path}: rank threshold must be 1..208.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), stateId: r.RequiredIdentifier("stateId"),
            maximumRankSum: rank, destination: SkillProgramCardDestination.OwnerHand);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.OwnerHand)];
}

internal sealed class RecoverOtherDyingVictimToDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverOtherDyingVictimTo;
    public override ISkillProgramEffectHandler Handler { get; } = new RecoverOtherDyingVictimToHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RecoverTo,
        static (effect, context) => context.RecoverOtherDyingVictimTo(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var hp = r.RequiredInt("amount");
        if (hp is < 1 or > 20) throw new InvalidOperationException($"Invalid skill program at {r.Path}: dying recovery HP must be 1..20.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), hp, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}

public sealed class RevealTopCardsWithNextBooleanBonusHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { ((IBoundRankBonusProgramHost)host).RevealTopCardsWithNextBooleanBonus(f, e); return SkillProgramStepOutcome.Continue; }
}
public sealed class ObtainBoundCardsAndArmNextRevealBonusHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IBoundRankBonusProgramHost)host).ObtainBoundCardsAndArmNextRevealBonus(f, e);
}
public sealed class RecoverOtherDyingVictimToHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverOtherDyingVictimTo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { ((IBoundRankBonusProgramHost)host).RecoverOtherDyingVictimTo(f, e.Amount); return SkillProgramStepOutcome.Continue; }
}
