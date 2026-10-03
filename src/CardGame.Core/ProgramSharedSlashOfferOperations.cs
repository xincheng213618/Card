namespace CardGame.Core;

public enum SharedSlashOfferStage { GiftChildren, ChoosingOption, ChoosingTarget, SlashIssued, OwnerDrawIssued, RecipientDrawIssued, Complete }

public sealed record SharedSlashBenefitReturn(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, int ActorSeat, int OriginalTargetSeat, long CardUseFrameId, long CardActionId);
public sealed record SharedSlashOfferReceipt(int InstructionIndex, string SourceBind, CardConversionSource Source,
    int ActorSeat, CardActionCost GiftCost, long GiftMovementSequence = 0,
    SharedSlashOfferStage Stage = SharedSlashOfferStage.GiftChildren,
    SharedSlashBenefitReturn? SlashReturn = null, bool? CausedDamage = null);
public sealed record SharedSlashGiftCommittedEvent(long ProgramFrameId, CardConversionSource Source,
    int RecipientSeat, long MovementSequence) : IGameEvent;
public sealed record SharedSlashUseIssuedEvent(SharedSlashBenefitReturn Return) : IGameEvent;
public sealed record SharedSlashUseReturnedEvent(SharedSlashBenefitReturn Return, bool CausedDamage) : IGameEvent;
public sealed record SharedSlashDrawIssuedEvent(long ProgramFrameId, int RecipientSeat, int Count) : IGameEvent;

public interface ISharedSlashOfferProgramHost
{
    SkillProgramStepOutcome GiveBoundCardThenOffer(ProgramSkillFrame frame, int actorSeat, string sourceBind);
}

internal sealed class GiveBoundCardThenOfferVirtualSlashOrSharedDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveBoundCardThenOfferVirtualSlashOrSharedDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (effect, context) =>
        {
            context.ShownBoundGift(effect);
            context.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, [])));
            context.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.SelectedTarget, 1, new(SkillProgramConditionKind.Always, 0, [])));
            context.PublicControlValue(6d);
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("A shared Slash offer requires its selected gift recipient.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new RequireOwnedCardSet(e.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand, CardZoneKind.Equipment]),
         new ReadSingleCardSet(e.SourceBind!), new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.SelectedTargetHand)];

    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCardCount, bool selectedTarget,
        SkillProgramTargetKind? targetKind, int targetSetMaximum)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw)) return;
        if (window is not null || selectedCardCount != 0 || !selectedTarget || targetSetMaximum != 0 ||
            targetKind != SkillProgramTargetKind.OtherLiving || effects.Count != 2 ||
            effects[0] is not { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
                MinimumCards: 1, MaximumCards: 1, NumberExpression: null, Condition.Kind: SkillProgramConditionKind.Always } first ||
            effects[1].Op != SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw ||
            first.ResultBind != effects[1].SourceBind || first.Zones.Count == 0 ||
            first.Zones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {path}: shared Slash gift requires an exact one-card owner HE selection and terminal offer in a one-target active binding.");
    }
}

public sealed class GiveBoundCardThenOfferVirtualSlashOrSharedDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) =>
        ((ISharedSlashOfferProgramHost)host).GiveBoundCardThenOffer(f, targetSeat, e.SourceBind!);
}
