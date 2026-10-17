namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class SelectionSourceQueries(GameEngine engine) : IProgramSelectionSourceQuery
    {
        public IReadOnlyList<ProgramSelectionParticipant> Participants() => Array.AsReadOnly(engine._players.Select(player =>
            new ProgramSelectionParticipant(player.Seat, player.IsAlive, player.Hp, player.MaxHp, player.HasHpValue,
                engine.GetHand(player).Count, engine.GetEquipment(player).Count, engine.UsesTreatedWounded ? engine.IsRuleWounded(player) : null)).ToArray());
        public IReadOnlyList<Card> Hand(int seat) => Array.AsReadOnly(engine.GetHand(engine._players[seat]).Select(ProgramSelectionProjection.Freeze).ToArray());
        public IReadOnlyList<Card> Equipment(int seat) => Array.AsReadOnly(engine.GetEquipment(engine._players[seat]).Select(ProgramSelectionProjection.Freeze).ToArray());
        public CardLocation Location(int cardId) => engine._cardZones.GetLocation(cardId);
        public bool IsHandRestricted(int seat, Card card) => engine.IsTurnHandCardRestricted(engine._players[seat], card);
        public bool HasCardIdentity(int seat, Card card) => engine.HasProgramCardIdentity(engine._players[seat], card);
        public bool IsUseForbidden(int seat, CardKind kind) => engine.IsCardUseForbidden(seat, kind, CardActionType.Use);
        public bool IsPhysicalUseForbidden(int seat, int cardId) => engine.IsTurnPhysicalUseForbidden(seat, [cardId]);
        public Suit EffectiveSuit(int seat, Card card) => engine.EffectiveSuit(engine._players[seat], card);
        public CardKind EffectiveInputKind(int seat, Card card) => engine.EffectiveConversionInputKind(engine._players[seat], card);
        public IReadOnlyList<ProgramMultiCardViewAsSelection> Conversions(int seat, CardKind kind) =>
            Array.AsReadOnly(engine.GetProgramMultiCardViewAsSelections(engine._players[seat], kind, false)
                .Select(selection => ProgramSelectionProjection.Freeze(selection)!).ToArray());
        public bool HasHpLossTargets(int seat) => engine.HasHpLossSlashTargets(engine._players[seat]);
        public bool CanUseSlash(int owner, int target, IReadOnlyList<Card>? cards, bool ignoreDistance) => cards is null
            ? engine.CanUseVirtualSlashTarget(engine._players[owner], engine._players[target], ignoreDistance: ignoreDistance)
            : engine.CanUseVirtualSlashTarget(engine._players[owner], engine._players[target],
                physicalSuit: engine.PhysicalGroupSuit(engine._players[owner], cards),
                effectiveColor: engine.PhysicalGroupColor(engine._players[owner], cards), physicalCardIds: cards.Select(card => card.Id).ToArray());
        public bool CanUseGlobal(int owner, CardKind kind) => engine.CanUseGlobalCard(engine._players[owner], kind);
        public bool CanUseOrdinaryTrick(int owner, CardKind? kind, IReadOnlyList<int> physicalCards) =>
            engine.BuildProgramOrdinaryTrickUseOptions(engine._players[owner], kind, physicalCardIds: physicalCards, includeNextActualUseAdjustment: true).Count != 0;
        public bool CanRequestFactionSlash(int owner, string faction) => engine.CanUseFactionSlashRequest(engine._players[owner], faction);
        public bool HasNextUseAdjustment(int owner) => engine.CurrentNextActualUseAdjustment(owner) is not null;
        public bool IsTargetProhibited(int target, CardKind kind, int owner, IReadOnlyList<Card> cards) =>
            engine.IsCardTargetProhibited(engine._players[target], kind, engine.PhysicalGroupSuit(engine._players[owner], cards), engine.PhysicalGroupColor(engine._players[owner], cards));
        public bool HasSuitShield(int owner, int target, IReadOnlyList<Card> cards) =>
            engine.HasBeneficiarySuitShield(owner, target, engine.PhysicalGroupSuit(engine._players[owner], cards));
        public int HpLossMaximum(int owner, CardKind kind) => engine.HpLossSlashMaximum(engine._players[owner], kind);
    }
}
