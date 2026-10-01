namespace CardGame.Core;

internal interface IFinalTargetPublicPileHost
{
    SkillProgramStepOutcome ExecuteFinalTargetPublicPile(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed record RequirePublicPileActivation : ProgramResourceOperation;
internal sealed record RequireAwaitedHandPayment : ProgramResourceOperation;
internal abstract class FinalTargetPublicPileDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e,c) => c.FinalTargetPublicPile(e));
    protected SkillProgramEffect ParseOwner(ProgramOperationNodeReader r, bool source)
    {
        if (source) r.AllowOnly("op", "target", "skillIds", "condition");
        else r.AllowOnly("op", "target", "condition");
        var skills = source ? r.RequiredIdentifierArray("skillIds") : [];
        if (source && skills.Count != 1) throw new InvalidOperationException("Public pile operation requires exactly one source skill.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), skillIds: skills);
        RequireAlways(effect, r.Path); return effect;
    }
}
internal sealed class CollectFinalTargetCardInPublicPileDescriptor : FinalTargetPublicPileDescriptor
{
    internal static IReadOnlyList<CardKind> Kinds { get; } = [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel, CardKind.FireAttack, CardKind.BarbarianAssault, CardKind.ArrowBarrage];
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectFinalTargetCardInPublicPile;
    public override ISkillProgramEffectHandler Handler { get; } = new CollectFinalTargetCardInPublicPileHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, false);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, Kinds)];
}
internal sealed class ExchangePublicPileHandDescriptor : FinalTargetPublicPileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePublicPileHand;
    public override ISkillProgramEffectHandler Handler { get; } = new ExchangePublicPileHandHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, true);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequirePublicPileExchangeBoundary()];
}
internal sealed class ObtainPublicPileCardDescriptor : FinalTargetPublicPileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainPublicPileCard;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainPublicPileCardHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, true);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequirePublicPileActivation()];
}
internal sealed class DiscardPublicZoneAfterHandPaymentDescriptor : FinalTargetPublicPileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardPublicZoneAfterHandPayment;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardPublicZoneAfterHandPaymentHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, false);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireAwaitedHandPayment()];
}
public abstract class FinalTargetPublicPileHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IFinalTargetPublicPileHost)h).ExecuteFinalTargetPublicPile(e,f);
}
public sealed class CollectFinalTargetCardInPublicPileHandler : FinalTargetPublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectFinalTargetCardInPublicPile; }
public sealed class ExchangePublicPileHandHandler : FinalTargetPublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePublicPileHand; }
public sealed class ObtainPublicPileCardHandler : FinalTargetPublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainPublicPileCard; }
public sealed class DiscardPublicZoneAfterHandPaymentHandler : FinalTargetPublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardPublicZoneAfterHandPayment; }
public sealed record FinalTargetCardStoredEvent(long ActionId, int OwnerSeat, int TargetSeat, int CardId) : IGameEvent;
