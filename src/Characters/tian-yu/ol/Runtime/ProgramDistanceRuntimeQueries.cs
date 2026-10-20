namespace CardGame.Core;

// Shared facts for distance rules. The adapter exposes journal entries and
// scalar identities, never a character, zone store, or resolution stack.
internal interface IDistanceRuleRuntimeQueries : IProgramSelectionQuery
{
    int RoundNumber { get; }
    int PlayerCount { get; }
    int AliveCount { get; }
    bool HasTriggerOperation(SkillProgramEffectOp op);
    bool HasSkill(int seat, string skillId);
    IReadOnlyList<T> History<T>() where T : IGameEvent;
    IEnumerable<int> OwnedCardIds(int seat);
    CardLocation Location(int cardId);
    IEnumerable<ProgramDirectedDistanceLedgerEntry> DirectedDistanceLedger();
}

internal readonly record struct ProgramDirectedDistanceLedgerEntry(
    int OwnerSeat, int TargetSeat, ProgramDirectedDistanceGrantedEvent? Grant);

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class DistanceRuleRuntimeQueries(GameEngine engine) : IDistanceRuleRuntimeQueries
    {
        public int RoundNumber => engine._roundNumber;
        public int PlayerCount => engine._playerCount;
        public int AliveCount => engine._players.Count(player => player.IsAlive);
        public bool HasTriggerOperation(SkillProgramEffectOp op) =>
            engine._contentRegistry.ProgramDependencies.HasTriggerOperation(op);
        public bool HasSkill(int seat, string skillId) => engine.HasRuntimeSkill(engine._players[seat], skillId);
        public IReadOnlyList<T> History<T>() where T : IGameEvent => engine.ProgramEventHistory<T>();
        public IEnumerable<int> OwnedCardIds(int seat) =>
            engine.GetHand(engine._players[seat]).Concat(engine.GetEquipment(engine._players[seat])).Select(card => card.Id);
        public CardLocation Location(int cardId) => engine._cardZones.GetLocation(cardId);
        public IEnumerable<ProgramDirectedDistanceLedgerEntry> DirectedDistanceLedger()
        {
            foreach (var fact in engine.CompleteProgramEventHistory())
                switch (fact)
                {
                    case ProgramDirectedDistanceGrantedEvent grant:
                        yield return new(grant.OwnerSeat, grant.TargetSeat, grant);
                        break;
                    case ProgramDirectedDistanceRevokedEvent revoke:
                        yield return new(revoke.OwnerSeat, revoke.TargetSeat, null);
                        break;
                }
        }
    }
}
