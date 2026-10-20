namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsPengbiTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolvePengbiFirstActualTurn }];
    private int? FirstPengbiActualTurn(int owner) => ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>()
        .Where(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.ActorSeat == owner && e.Phase.ActualTurnOwnerSeat == owner)
        .Select(e => (int?)e.Phase.TurnNumber).Distinct().Order().FirstOrDefault();
}
