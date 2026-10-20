namespace CardGame.Core;

public sealed partial class GameEngine
{

    private GuanPhaseFocusEvent? GuanPhaseFocus(int owner) => CompleteProgramEventHistory().OfType<GuanPhaseFocusEvent>()
        .LastOrDefault(e => e.OwnerSeat == owner && e.TurnNumber == _turnNumber && e.PhaseSerial == _cardUseDebitPhaseInstanceId && _phase == TurnPhase.Play && _currentSeat == owner);
    private int GuanFocusedSlashCount(GuanPhaseFocusEvent focus) => CompleteProgramEventHistory().OfType<GuanFocusedSlashEvent>()
        .Count(e => e.OwnerSeat == focus.OwnerSeat && e.TargetSeat == focus.TargetSeat && e.TurnNumber == focus.TurnNumber && e.PhaseSerial == focus.PhaseSerial);
    private bool GuanFocusAllowsSlash(int owner, int target) => GuanPhaseFocus(owner) is { } focus && focus.TargetSeat == target && GuanFocusedSlashCount(focus) < 3;
    private bool GuanFocusProhibitsSlash(int owner, int target, CardKind kind) => IsSlashCard(kind) && GuanPhaseFocus(owner) is { } focus && focus.TargetSeat == target && GuanFocusedSlashCount(focus) >= 3;

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 210)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 30)]
    private bool DistancePhysicalSlash210GuanFocusAllowsSlash(ProgramSlashDistanceQuery q) =>
        GuanFocusAllowsSlash(q.Actor.Seat, q.Target.Seat);

}
