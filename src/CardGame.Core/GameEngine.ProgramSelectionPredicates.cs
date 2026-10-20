namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class PredicateSelectionQueries(GameEngine engine) :
        IProgramSelectionCardState, IProgramSelectionDiamondState,
        IProgramSelectionTargetState, IProgramSelectionPhaseLimitState
    {
        public CardLocation Location(int cardId) => engine._cardZones.GetLocation(cardId);
        public Suit EffectiveSuit(int ownerSeat, Card card) => engine.EffectiveSuit(engine._players[ownerSeat], card);
        public bool IsAlive(int seat) => engine._players[seat].IsAlive;
        public bool IsSelfDiscardForbidden(int ownerSeat, Card card, CardLocation location) =>
            engine.IsSelfHandCategoryDiscardForbidden(ownerSeat, card, location, OwnedCardMoveIntent.Discard);
        public bool IsForeignEquipmentDiscardPrevented(int ownerSeat, Card card, CardLocation location) =>
            engine.IsForeignEquipmentDiscardPrevented(ownerSeat, card, location, OwnedCardMoveIntent.Discard);
        public bool IsActiveSourceEquipment(int ownerSeat, string skillId, Card card) =>
            engine.IsActiveProgramSourceEquipmentCard(ownerSeat, skillId,
                engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId), card);
        public int? UniqueMaximumHandCardId(int ownerSeat) => engine.UniqueMaximumHandCard(ownerSeat)?.Id;

        public int PlayerCount => engine._players.Count;
        public bool HasDelayedDiamondJudgment() => engine.DiamondJudgments().Any();
        public bool JudgmentAreaAbolished(int seat) => engine._players[seat].JudgmentAreaAbolished;
        public bool HasIndulgenceJudgment(int seat) => engine.HasJudgmentEffectiveCard(engine._players[seat], CardKind.Indulgence);
        public bool IsTurnHandCardRestricted(int ownerSeat, Card card) => engine.IsTurnHandCardRestricted(engine._players[ownerSeat], card);
        public bool IsPlayPhaseCardRestricted(int ownerSeat, Card card) => engine.IsPlayPhasePhysicalCardRestricted(engine._players[ownerSeat], card);
        public bool IsCardUseForbidden(int ownerSeat, CardKind kind, CardActionType actionType) =>
            engine.IsCardUseForbidden(ownerSeat, kind, actionType);
        public bool IsDirectedTargetProhibited(int ownerSeat, int targetSeat, CardKind kind) =>
            engine.IsDirectedCardTargetProhibited(ownerSeat, targetSeat, kind);
        public bool IsTargetProhibited(int targetSeat, CardKind kind, Suit suit, bool? isRed) =>
            engine.IsCardTargetProhibited(engine._players[targetSeat], kind, suit, isRed);
        public bool HasBeneficiarySuitShield(int ownerSeat, int targetSeat, Suit suit) =>
            engine.HasBeneficiarySuitShield(ownerSeat, targetSeat, suit);

        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public int Hp(int seat) => engine._players[seat].Hp;
        public int HandCount(int seat) => engine.GetHand(engine._players[seat]).Count;
        public int MarkerCount(int seat, PlayerMarkerKind marker) => engine._players[seat].Markers.GetValueOrDefault(marker);
        public bool IsFarthestInRange(int ownerSeat, int targetSeat) =>
            engine.IsFarthestInRange(engine._players[ownerSeat], engine._players[targetSeat]);
        public bool IsWithinAttackRange(int ownerSeat, int targetSeat) => engine.IsWithinAttackRange(ownerSeat, targetSeat);
        public int TurnUsage(int ownerSeat, string skillId, string usageId) =>
            engine._skillRuntimeState.GetUsage(ownerSeat, skillId, usageId, SkillUsageScope.Turn);
        public IEnumerable<GameTargetHandHpChoiceIssuedEvent> HandHpIssuedTargets =>
            engine.CompleteProgramEventHistory().OfType<GameTargetHandHpChoiceIssuedEvent>();
        public IEnumerable<DrawDiscardCategoryTargetBannedEvent> CategoryRefundBans =>
            engine.CompleteProgramEventHistory().OfType<DrawDiscardCategoryTargetBannedEvent>();
        public int DynamicDamagePaymentCount(int ownerSeat, string skillId) =>
            engine.DynamicDiscardDamageCards(ownerSeat, skillId,
                engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId)).Length;
        public bool HasEmptySlotForOwnerHandEquipment(int ownerSeat, int targetSeat) =>
            engine.HasEmptyEquipmentSlotForOwnerHandEquipment(engine._players[ownerSeat], engine._players[targetSeat]);
        public bool CanUseProvidedSlashOn(int ownerSeat, int targetSeat) =>
            engine.CanUseProvidedSlashTarget(engine._players[ownerSeat], engine._players[targetSeat]);

        public int TurnNumber => engine._turnNumber;
        public int RoundNumber => engine._roundNumber;
        public int TurnOwnerSeat => engine._currentSeat;
        public string SkillInstanceId(int ownerSeat, string skillId) =>
            engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId);
        public IEnumerable<ProgramHandComparisonResolvedEvent> HandComparisonResults =>
            engine.HandComparisonHistory().OfType<ProgramHandComparisonResolvedEvent>();
        public int CountLiving(Role role) => engine._players.Count(player => player.IsAlive && player.Role == role);
    }
}
