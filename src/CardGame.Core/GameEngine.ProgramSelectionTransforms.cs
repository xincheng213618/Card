namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class SelectionParticipantQueries(GameEngine engine) : ISelectionParticipantQueries
    {
        public IReadOnlyList<int> Seats => Array.AsReadOnly(engine._players.Select(player => player.Seat).ToArray());
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool IsAlive(int seat) => engine._players[seat].IsAlive;
        public int HandCount(int seat) => engine.GetHand(engine._players[seat]).Count;
        public int EquipmentCount(int seat) => engine.GetEquipment(seat).Count;
        public int Hp(int seat) => engine._players[seat].Hp;
    }

    [ProgramCapability]
    private sealed class MatchingRecastSelectionQueries(GameEngine engine) : IMatchingRecastSelectionQueries
    {
        public IReadOnlyList<MatchingRecastMaterial> Materials(int ownerSeat) =>
            Array.AsReadOnly(engine.MatchingRecastMaterials(ownerSeat));
    }

    [ProgramCapability]
    private sealed class SourceCurseSelectionQueries(GameEngine engine) : ISourceCurseSelectionQueries
    {
        public bool HasCurse(int targetSeat) => engine.SourceCurseAt(targetSeat) is not null;
    }

    [ProgramCapability]
    private sealed class PairObtainSelectionQueries(GameEngine engine) : IPairObtainSelectionQueries
    {
        public string CurrentSkillInstance(int ownerSeat, string skillId) =>
            engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId);
        public bool CanObtainFrom(int ownerSeat, string skillId, string instanceId, int targetSeat) =>
            engine.HasPayablePairObtainTarget(ownerSeat, skillId, instanceId, targetSeat);
    }

    [ProgramCapability]
    private sealed class ForeignSlashSelectionQueries(GameEngine engine) : IForeignSlashSelectionQueries
    {
        public bool CanStart(int ownerSeat, int actorSeat) =>
            engine.CanStartSelectedForeignCardSlash(engine._players[ownerSeat], actorSeat);
    }

    [ProgramCapability]
    private sealed class DuelSelectionQueries(GameEngine engine) : IDuelSelectionQueries
    {
        public bool CanIssue(int ownerSeat, int actorSeat) => engine.CanIssueSelectedActorDuel(ownerSeat, actorSeat);
    }

    [ProgramCapability]
    private sealed class EquipmentPlacementSelectionQueries(GameEngine engine) : IEquipmentPlacementSelectionQueries
    {
        public bool CanPlace(int ownerSeat, int targetSeat, int cardId) =>
            engine.CanPlaceActivationEquipment(ownerSeat, targetSeat, cardId);
        public bool CanPlaceCaptured(int ownerSeat, int targetSeat, int cardId, string skillId) =>
            engine.CanPlaceCapturedEquipmentActivation(ownerSeat, targetSeat, cardId, skillId);
    }

    [ProgramCapability]
    private sealed class SelectionUsageQueries(GameEngine engine) : ISelectionUsageQueries
    {
        public bool CanUseDistinctTurnTarget(int ownerSeat, string skillId, string usageId, int targetSeat) =>
            engine.CanActivateDistinctTurnTarget(ownerSeat, skillId, usageId, targetSeat);
        public bool CanUsePhaseTarget(int ownerSeat, string skillId, string usageId, int targetSeat) =>
            engine.CanActivateTargetPhaseLedger(ownerSeat, skillId, usageId, targetSeat);
        public bool CanUseCategoryTarget(int ownerSeat, string skillId, string usageId, int cardId, int targetSeat) =>
            engine.CanActivateCategoryTargetLedger(ownerSeat, skillId, usageId, [cardId], [targetSeat]);
    }

    [ProgramCapability]
    private sealed class PindianSelectionQueries(GameEngine engine) : IPindianSelectionQueries
    {
        public bool HasNextActualUseStructure(SkillProgramActivation activation) =>
            engine.HasNextActualUsePindianStructure(activation);
        public int EffectiveRank(int ownerSeat, int cardId) =>
            engine.EffectivePindianRank(engine._players[ownerSeat], engine.GetHand(engine._players[ownerSeat]).Single(card => card.Id == cardId));
        public int HandKeepValue(int ownerSeat, int cardId) =>
            CardCatalog.Get(engine.GetHand(engine._players[ownerSeat]).Single(card => card.Id == cardId).Kind).HandKeepValue;
    }
}
