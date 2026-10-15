namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class GuanYuTargetSelectionQuery(GameEngine engine) : IGuanYuTargetSelectionQuery
    {
        public int RoundNumber => engine._roundNumber;
        public long? CurrentProgramFrameId => (engine._resolutionStack.LastOrDefault() as ProgramSkillFrame)?.Id;
        public IReadOnlyList<ProgramTargetParticipantFacts> Participants => Array.AsReadOnly(engine._players.Select(player =>
            new ProgramTargetParticipantFacts(player.Seat, player.IsAlive, player.HasHpValue, player.Hp, player.MaxHp)).ToArray());
        public IReadOnlyList<DemonRoundFocusEvent> FocusHistory =>
            Array.AsReadOnly(engine.CompleteProgramEventHistory().OfType<DemonRoundFocusEvent>().ToArray());
        public IReadOnlyList<DemonSupportGrantedEvent> SupportHistory =>
            Array.AsReadOnly(engine.CompleteProgramEventHistory().OfType<DemonSupportGrantedEvent>().ToArray());
    }
}
