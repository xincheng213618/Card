namespace CardGame.Core;

internal interface IDeferredCardProgramHost
{
    SkillProgramStepOutcome ViewTopCardsAndObtainMatchingCards(ProgramSkillFrame frame, int seat, int amount, IReadOnlyList<SkillProgramCardCategory> categories);
    SkillProgramStepOutcome DepositBoundCardsUntilNextTurn(ProgramSkillFrame frame, string bind);
    SkillProgramStepOutcome ObtainDeferredPile(ProgramSkillFrame frame);
    void RewardDeferredProviders(ProgramSkillFrame frame);
}

internal sealed class ViewTopCardsAndObtainMatchingCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ViewTopCardsAndObtainMatchingCardsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "cardCategories", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        var categories = r.RequiredEnumArray<SkillProgramCardCategory>("cardCategories");
        if (target != SkillProgramEffectTarget.SelectedTarget || amount is < 1 or > 8 || categories.Count == 0)
            throw new InvalidOperationException($"Invalid private participant top-card view at {r.Path}.");
        return new(Op, target, amount, r.Condition(), cardCategories: categories);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}

internal sealed class DepositBoundCardsUntilNextTurnDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositBoundCardsUntilNextTurn;
    public override ISkillProgramEffectHandler Handler { get; } = new DepositBoundCardsUntilNextTurnHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.SelectedTarget, 3, [CardZoneKind.Hand, CardZoneKind.Equipment]), new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.OwnerPersistentZone)];
}

internal sealed class ObtainDeferredPileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDeferredPile;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainDeferredPileHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class RewardDeferredProvidersDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardDeferredProviders;
    public override ISkillProgramEffectHandler Handler { get; } = new RewardDeferredProvidersHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public abstract class DeferredCardHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    {
        var deferred = (IDeferredCardProgramHost)host;
        switch (Op)
        {
            case SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards:
                return deferred.ViewTopCardsAndObtainMatchingCards(frame, seat, effect.Amount, effect.CardCategories);
            case SkillProgramEffectOp.DepositBoundCardsUntilNextTurn:
                return deferred.DepositBoundCardsUntilNextTurn(frame, effect.SourceBind!);
            case SkillProgramEffectOp.ObtainDeferredPile: return deferred.ObtainDeferredPile(frame);
            case SkillProgramEffectOp.RewardDeferredProviders: deferred.RewardDeferredProviders(frame); return SkillProgramStepOutcome.Continue;
            default: throw new InvalidOperationException("Unsupported deferred-card instruction.");
        }
    }
}

public sealed class ViewTopCardsAndObtainMatchingCardsHandler : DeferredCardHandler
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards;
}

public sealed class DepositBoundCardsUntilNextTurnHandler : DeferredCardHandler
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositBoundCardsUntilNextTurn;
}

public sealed class ObtainDeferredPileHandler : DeferredCardHandler
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDeferredPile;
}

public sealed class RewardDeferredProvidersHandler : DeferredCardHandler
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardDeferredProviders;
}
