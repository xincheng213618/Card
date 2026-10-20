namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuLuKangKegou(ProgramSkillFrame f)
    {
        if (f.WuLuKangKegou is not { } r || !WuLuKangKegouParent(f) || r.Source != WuLuKangSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != f.WindowContext!.ParentFrameId ||
            !WuLuKangQualifiedAtCutoff(f.OwnerSeat, r.Qualification) || r.Qualification.ActualTurn != _turnNumber || r.Qualification.TurnOwner != _turnProgression.OwnerSeat || r.Qualification.TurnOrdinal != WuLuKangTurnOrdinal ||
            f.WindowContext.Window == SkillProgramTriggerWindow.AfterTurnEnded && (!(r.Qualification.Used || r.Qualification.Played) || !WuLuKangActualEndedCutoff(r.Qualification)) ||
            r.Iteration < 0 || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<WuLuKangKegouStartedEvent>().Count(e => e == new WuLuKangKegouStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Qualification)) != 1) return false;
        if (r.PindianId is { } pair && ProgramEventHistory<WuLuKangKegouPindianIssuedEvent>().Count(e => e == new WuLuKangKegouPindianIssuedEvent(f.Id, r.Iteration, pair, r.OpponentSeat!.Value)) != 1) return false;
        if (r.Result is { } result && (result.SourceSeat != f.OwnerSeat || result.OpponentSeat != r.OpponentSeat ||
            ProgramEventHistory<WuLuKangKegouPindianReturnedEvent>().Count(e => e == new WuLuKangKegouPindianReturnedEvent(f.Id, r.Iteration, r.PindianId!.Value, result)) != 1 ||
            ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == r.PindianId && e.Result == result) != 1)) return false;
        if (r.NativeTargets.Count > 0 && (r.NativeTargets[0] != f.OwnerSeat || r.NativeTargets.Distinct().Count() != r.NativeTargets.Count ||
            r.NativeTargets.Where((seat, i) => ProgramEventHistory<WuLuKangKegouSlashTargetEvent>().Count(e => e == new WuLuKangKegouSlashTargetEvent(f.Id, r.Iteration, i, seat, r.NativeTargetsAdjusted)) != 1).Any())) return false;
        return r.Gains.Count <= 2 && r.Gains.Select(g => g.PrintedRank).Distinct().Count() == r.Gains.Count && r.Gains.All(g => g.From == CardLocation.DrawPile && g.To == CardLocation.Hand(f.OwnerSeat) &&
            _cardMovements.Count(m => m.Sequence == g.Sequence && m.CardId == g.CardId && m.From == g.From && m.To == g.To && m.Reason.Value == WuLuKangKegouGainReason) == 1 &&
            ProgramEventHistory<WuLuKangKegouGainPaidEvent>().Count(e => e == new WuLuKangKegouGainPaidEvent(f.Id, r.Iteration, g.CardId, g.PrintedRank, g.From, g.To, g.Sequence)) == 1);
    }
}
