namespace CardGame.Core;

public sealed partial class GameEngine
{

    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 60)]
    private bool DistanceBorrowedPaidWeaponSlash60GetCombatDistance(ProgramSlashDistanceQuery q) =>
        GetCombatDistance(q.Actor.Seat, q.Target.Seat) <= 1;

}
