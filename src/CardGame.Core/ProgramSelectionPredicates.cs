namespace CardGame.Core;

// Selection modules receive small read-only rule queries. They cannot retain a
// live character, enter the command session, publish a prompt, or mutate zones.
internal interface IProgramSelectionCardState : IProgramSelectionQuery
{
    CardLocation Location(int cardId);
    Suit EffectiveSuit(int ownerSeat, Card card);
    bool IsAlive(int seat);
    bool IsSelfDiscardForbidden(int ownerSeat, Card card, CardLocation location);
    bool IsForeignEquipmentDiscardPrevented(int ownerSeat, Card card, CardLocation location);
    bool IsActiveSourceEquipment(int ownerSeat, string skillId, Card card);
    int? UniqueMaximumHandCardId(int ownerSeat);
}

internal interface IProgramSelectionDiamondState : IProgramSelectionQuery
{
    int PlayerCount { get; }
    bool HasDelayedDiamondJudgment();
    bool IsValidSeat(int seat);
    bool JudgmentAreaAbolished(int seat);
    bool HasIndulgenceJudgment(int seat);
    bool IsTurnHandCardRestricted(int ownerSeat, Card card);
    bool IsPlayPhaseCardRestricted(int ownerSeat, Card card);
    bool IsCardUseForbidden(int ownerSeat, CardKind kind, CardActionType actionType);
    bool IsDirectedTargetProhibited(int ownerSeat, int targetSeat, CardKind kind);
    bool IsTargetProhibited(int targetSeat, CardKind kind, Suit suit, bool? isRed);
    bool HasBeneficiarySuitShield(int ownerSeat, int targetSeat, Suit suit);
}

internal interface IProgramSelectionTargetState : IProgramSelectionQuery
{
    bool IsValidSeat(int seat);
    bool IsAlive(int seat);
    int Hp(int seat);
    int HandCount(int seat);
    int MarkerCount(int seat, PlayerMarkerKind marker);
    bool IsFarthestInRange(int ownerSeat, int targetSeat);
    bool IsWithinAttackRange(int ownerSeat, int targetSeat);
    int TurnUsage(int ownerSeat, string skillId, string usageId);
    IEnumerable<GameTargetHandHpChoiceIssuedEvent> HandHpIssuedTargets { get; }
    IEnumerable<DrawDiscardCategoryTargetBannedEvent> CategoryRefundBans { get; }
    int TurnNumber { get; }
    int TurnOwnerSeat { get; }
    int DynamicDamagePaymentCount(int ownerSeat, string skillId);
    bool HasEmptySlotForOwnerHandEquipment(int ownerSeat, int targetSeat);
    bool CanUseProvidedSlashOn(int ownerSeat, int targetSeat);
}

internal interface IProgramSelectionPhaseLimitState : IProgramSelectionQuery
{
    int RoundNumber { get; }
    int TurnNumber { get; }
    string SkillInstanceId(int ownerSeat, string skillId);
    IEnumerable<ProgramHandComparisonResolvedEvent> HandComparisonResults { get; }
    int CountLiving(Role role);
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 10,
    SkillProgramEffectOp.DiscardTurnOverAndTakeHand)]
internal sealed class PhaseHandSeizureSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionCardState>();
        return state.Location(card.Id) is { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } location &&
            seat == context.OwnerSeat &&
            !state.IsSelfDiscardForbidden(context.OwnerSeat, card, location) &&
            !state.IsForeignEquipmentDiscardPrevented(context.OwnerSeat, card, location) &&
            !(location.Zone == CardZoneKind.Equipment &&
                state.IsActiveSourceEquipment(context.OwnerSeat, context.SkillId, card));
    }
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 20,
    SkillProgramEffectOp.DepositZengdaoActualEquipment)]
internal sealed class ZengdaoSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries) =>
        !card.IsGeneralWeapon;
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 30,
    SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest)]
internal sealed class BlackGiftSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionCardState>();
        return state.Location(card.Id) == CardLocation.Hand(context.OwnerSeat) &&
            state.EffectiveSuit(context.OwnerSeat, card) is Suit.Spade or Suit.Club;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 40,
    SkillProgramEffectOp.UseDiamondDelayedOrDiscard)]
internal sealed class DiamondDelayedSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionCardState>();
        if (!AllowsPayment(context, card, state)) return false;
        var diamond = queries.Get<IProgramSelectionDiamondState>();
        if (diamond.HasDelayedDiamondJudgment()) return true;
        for (var targetSeat = 0; targetSeat < diamond.PlayerCount; targetSeat++)
            if (AllowsPayment(context, card, state) && diamond.IsValidSeat(targetSeat) && targetSeat != context.OwnerSeat &&
                state.IsAlive(targetSeat) && !diamond.JudgmentAreaAbolished(targetSeat) && !diamond.HasIndulgenceJudgment(targetSeat) &&
                !diamond.IsTurnHandCardRestricted(context.OwnerSeat, card) && !diamond.IsPlayPhaseCardRestricted(context.OwnerSeat, card) &&
                !diamond.IsCardUseForbidden(context.OwnerSeat, CardKind.Indulgence, CardActionType.Use) &&
                !diamond.IsDirectedTargetProhibited(context.OwnerSeat, targetSeat, CardKind.Indulgence) &&
                !diamond.IsTargetProhibited(targetSeat, CardKind.Indulgence,
                    state.EffectiveSuit(context.OwnerSeat, card), SuitColor(state.EffectiveSuit(context.OwnerSeat, card))) &&
                !diamond.HasBeneficiarySuitShield(context.OwnerSeat, targetSeat, state.EffectiveSuit(context.OwnerSeat, card)))
                return true;
        return false;
    }

    private static bool AllowsPayment(ProgramSelectionContext context, Card card, IProgramSelectionCardState state) =>
        state.IsAlive(context.OwnerSeat) &&
        state.Location(card.Id) is { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } &&
        seat == context.OwnerSeat && !card.IsGeneralWeapon &&
        !state.IsActiveSourceEquipment(context.OwnerSeat, context.SkillId, card) &&
        state.EffectiveSuit(context.OwnerSeat, card) == Suit.Diamond;

    private static bool? SuitColor(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond ? true : suit is Suit.Club or Suit.Spade ? false : null;
}

[ProgramSelectionModule(ProgramSelectionStage.CardSuit, 10, SkillProgramEffectOp.UseDiamondDelayedOrDiscard)]
internal sealed class DiamondDelayedSelectionSuitProvider : IProgramSelectionSuitProvider
{
    public Suit GetSuit(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries) =>
        queries.Get<IProgramSelectionCardState>().EffectiveSuit(context.OwnerSeat, card);
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDeclaration, 10, SkillProgramEffectOp.DepositSelectedSourceCurse)]
internal sealed class SourceCurseSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries) =>
        !card.IsGeneralWeapon;
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 10,
    SkillProgramEffectOp.CompareSelectedHandWithHpHand)]
internal sealed class HandComparisonSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionTargetState>();
        return state.Hp(targetSeat) > 0 && state.HandCount(targetSeat) > 0;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 20, SkillProgramEffectOp.DamageFarthestCharacter)]
internal sealed class FarthestDamageSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionTargetState>();
        return state.IsFarthestInRange(context.OwnerSeat, targetSeat) &&
            state.TurnUsage(context.OwnerSeat, context.SkillId, $"target:{targetSeat}") == 0;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 30, SkillProgramEffectOp.TransferXionghuoMark)]
internal sealed class XuRongSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionTargetState>();
        return targetSeat != context.OwnerSeat && state.IsAlive(targetSeat) &&
            state.MarkerCount(targetSeat, PlayerMarkerKind.Baoli) < 3;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 40,
    SkillProgramEffectOp.ResolveGameTargetHandHpChoice)]
internal sealed class GameHandHpSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var effect = context.Features.First(SkillProgramEffectOp.ResolveGameTargetHandHpChoice)!;
        var state = queries.Get<IProgramSelectionTargetState>();
        return state.IsValidSeat(targetSeat) && state.IsAlive(context.OwnerSeat) && state.IsAlive(targetSeat) &&
            state.HandCount(targetSeat) != state.Hp(targetSeat) &&
            !state.HandHpIssuedTargets.Any(receipt => receipt.OwnerSeat == context.OwnerSeat &&
                receipt.SkillId == context.SkillId && receipt.StateId == effect.StateId && receipt.TargetSeat == targetSeat);
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 50,
    SkillProgramEffectOp.DrawThenDiscardDistinctCategories)]
internal sealed class CategoryRefundSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var effect = context.Features.First(SkillProgramEffectOp.DrawThenDiscardDistinctCategories)!;
        var state = queries.Get<IProgramSelectionTargetState>();
        return state.IsValidSeat(targetSeat) && state.IsAlive(targetSeat) &&
            !state.CategoryRefundBans.Any(receipt => receipt.OwnerSeat == context.OwnerSeat &&
                receipt.SkillId == context.SkillId && receipt.StateId == effect.StateId && receipt.TargetSeat == targetSeat &&
                receipt.ActualTurnNumber == state.TurnNumber && receipt.ActualTurnOwnerSeat == state.TurnOwnerSeat);
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 60,
    SkillProgramEffectOp.DiscardTargetHpCardsAndDamage)]
internal sealed class DynamicDamageSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionTargetState>();
        return state.IsAlive(context.OwnerSeat) && state.IsAlive(targetSeat) && targetSeat != context.OwnerSeat &&
            state.Hp(targetSeat) > 0 && state.IsWithinAttackRange(context.OwnerSeat, targetSeat) &&
            state.DynamicDamagePaymentCount(context.OwnerSeat, context.SkillId) >= state.Hp(targetSeat);
    }
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 70)]
internal sealed class EmptyEquipmentSlotSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) =>
        !context.Activation.TargetRequiresEmptyEquipmentSlot ||
        queries.Get<IProgramSelectionTargetState>().HasEmptySlotForOwnerHandEquipment(context.OwnerSeat, targetSeat);
}

[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 80, SkillProgramEffectOp.RequestFactionCard)]
internal sealed class FactionRequestSelectionTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) =>
        queries.Get<IProgramSelectionTargetState>().CanUseProvidedSlashOn(context.OwnerSeat, targetSeat);
}

[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 10, SkillProgramEffectOp.CompareSelectedHandWithHpHand)]
internal sealed class HandComparisonSelectionPhaseLimit : IProgramSelectionPhaseLimitProvider
{
    public int? GetLimit(ProgramSelectionContext context, IProgramSelectionQueries queries)
    {
        var effect = context.Features.ForOperation(SkillProgramEffectOp.CompareSelectedHandWithHpHand).Single();
        var state = queries.Get<IProgramSelectionPhaseLimitState>();
        var instance = state.SkillInstanceId(context.OwnerSeat, context.SkillId);
        return state.HandComparisonResults.Any(result => result.OwnerSeat == context.OwnerSeat &&
            result.SkillId == context.SkillId && result.SkillInstanceId == instance && result.StateId == effect.StateId &&
            result.TurnNumber == state.TurnNumber && result.RankMatched) ? 2 : 1;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 20, SkillProgramEffectOp.ResolveRebelCountEquipmentChoice)]
internal sealed class BuZhiSelectionPhaseLimit : IProgramSelectionPhaseLimitProvider
{
    public int? GetLimit(ProgramSelectionContext context, IProgramSelectionQueries queries) =>
        queries.Get<IProgramSelectionPhaseLimitState>().CountLiving(Role.Rebel);
}
