namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ResolveProgramParticipant(frame, reference);

    private SkillProgramStepOutcome SelectAndMoveProgramOwnedCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        SkillProgramCardDestination destination,
        ProgramParticipantReference? destinationRef,
        string? resultBind,
        CardMoveReason reason,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        bool skipIfNoCards = false,
        bool allowSameOwnerHandReturn = false,
        string? coverageResultBind = null,
        bool awaitMovementTriggers = false,
        bool revealBeforeMove = false,
        IReadOnlyList<CardKind>? cardKinds = null,
        bool prohibitReplacingEquipment = false,
        IReadOnlyList<Suit>? suits = null) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().SelectAndMoveProgramOwnedCard(frame, chooser, cardOwner, zones, destination, destinationRef, resultBind, reason, cardCategories, skipIfNoCards, allowSameOwnerHandReturn, coverageResultBind, awaitMovementTriggers, revealBeforeMove, cardKinds, prohibitReplacingEquipment, suits);

    private bool HasOwnedProgramCardCategory(int ownerSeat,
        IReadOnlyList<CardZoneKind> zones, IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().HasOwnedProgramCardCategory(ownerSeat, zones, categories, kinds);

    private IReadOnlyList<PromptChoice> BuildOwnedCardPaymentChoices(
        long frameId, int chooserSeat, int cardOwnerSeat, IReadOnlyList<CardZoneKind> zones, OwnedCardMoveIntent intent,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        IReadOnlyList<Suit>? suits = null,
        Func<CardZoneKind, Card, bool>? canSelect = null,
        SkillProgramCardDestination destination = SkillProgramCardDestination.DiscardPile,
        int? destinationSeat = null,
        bool prohibitReplacingEquipment = false, int? correspondingZoneSeat = null) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().BuildOwnedCardPaymentChoices(frameId, chooserSeat, cardOwnerSeat, zones, intent, cardCategories, cardKinds, suits, canSelect, destination, destinationSeat, prohibitReplacingEquipment, correspondingZoneSeat);

    private void ResolveSelectAndMoveOwnedCardChoice(
        ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ResolveSelectAndMoveOwnedCardChoice(frame, effect, selected);

    private bool CanMoveProgramCardToCorrespondingZone(Card card, CardZoneKind sourceZone,
        int destinationSeat) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().CanMoveProgramCardToCorrespondingZone(card, sourceZone, destinationSeat);

    private CardLocation ProgramCorrespondingZoneLocation(Card card, CardZoneKind sourceZone,
        int destinationSeat) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ProgramCorrespondingZoneLocation(card, sourceZone, destinationSeat);

    private int CountLivingInAttackRange(int subjectSeat)
    {
        if (!_players[subjectSeat].IsAlive) return 0;
        var range = GetAttackRange(subjectSeat);
        return _players.Count(player => player.IsAlive && player.Seat != subjectSeat &&
            IsWithinAttackRange(subjectSeat, player.Seat));
    }

    private bool WouldEquipmentRemovalReduceCoverage(int subjectSeat, int equipmentCardId)
    {
        if (!_players[subjectSeat].IsAlive || !GetEquipment(_players[subjectSeat])
                .Any(card => card.Id == equipmentCardId)) return false;
        var subject = _players[subjectSeat];
        var projectedRange = ConvertRuleValue(EvaluateAttackRange(subject, equipmentCardId));
        var projectedCount = _players.Count(player => player.IsAlive && player.Seat != subjectSeat &&
            (HasSayingRoundRange(subjectSeat, player.Seat) || IsGameFactionAttackRangeTarget(subjectSeat, player.Seat) ||
             ConvertRuleValue(EvaluateDistance(subject, player, equipmentCardId)) <= projectedRange));
        return projectedCount < CountLivingInAttackRange(subjectSeat);
    }

    private void SetProgramAttackRangeCoverage(long frameId, string name, int subjectSeat,
        int beforeCount, int afterCount) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().SetProgramAttackRangeCoverage(frameId, name, subjectSeat, beforeCount, afterCount);

    private static bool IsProgramAttackRangeCoverageDecreased(ProgramSkillFrame frame, string bind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.IsProgramAttackRangeCoverageDecreased(frame,bind);

    private static bool MatchesProgramCardCategory(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesProgramCardCategory(kind,categories);

    // "Trick" keeps matching every trick; "InstantTrick" excludes delayed tricks (Indulgence, SupplyShortage, Lightning).
    private static bool MatchesSkillProgramCardCategory(CardKind kind, SkillProgramCardCategory category) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesSkillProgramCardCategory(kind,category);
    

    private static bool MatchesProgramCardFilter(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesProgramCardFilter(kind,categories,kinds);

    /// <summary>Equipment gifts cannot replace an equipped card: the matching slot must be free.</summary>
    private bool CanEnterEquipmentSlot(int recipientSeat, Card card) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().CanEnterEquipmentSlot(recipientSeat, card);

    private static SkillProgramCardCategory GetProgramCardCategory(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind);
}
