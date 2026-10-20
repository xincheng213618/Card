namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuLuKangShenduan(ProgramSkillFrame f)
    {
        if (f.WuLuKangShenduan is not { } r || f.InstructionIndex != 1 || f.TriggerId != "native-pindian" || r.Source != WuLuKangSource(f) || r.GameplayHash != f.GameplayHash ||
            f.WindowContext is not { WuLuKangPindian: { } c } context || context.ParentFrameId != r.WindowId || c != new WuLuKangPindianContext(r.ContestId, r.WindowStage, r.ComparisonIndex, r.ParticipantSeat) ||
            _resolutionStack.SingleOrDefault(w => w.Id == r.WindowId) is not WuLuKangPindianFrame w || w.ParentFrameId != r.ContestId || w.Stage != r.WindowStage ||
            w.ComparisonIndex != r.ComparisonIndex || w.CandidateIndex >= w.Entries.Count || !MountObserverCandidateMatches(f, w.Entries[w.CandidateIndex].Candidate) ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            _resolutionStack.SingleOrDefault(p => p.Id == r.ContestId) is not { WuLuKangContest: { } contest } parent || !ValidWuLuKangContest(parent) ||
            ProgramEventHistory<WuLuKangShenduanStartedEvent>().Count(e => e == new WuLuKangShenduanStartedEvent(f.Id, r.WindowId, r.ContestId, r.Source, r.GameplayHash, r.WindowStage, r.ComparisonIndex, r.ParticipantSeat)) != 1) return false;
        var ownIndex = _resolutionStack.FindIndex(frame => frame.Id == f.Id); var windowIndex = _resolutionStack.FindIndex(frame => frame.Id == w.Id);
        if (ownIndex != windowIndex + 1 || context.OwnerSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat || context.SourceSeat != contest.SourceSeat ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) return false;
        if (r.Payment is { } payment && !contest.Replacements.Contains(payment)) return false;
        if (r.WindowStage == WuLuKangPindianStage.GroupClose) return contest.Cancelled && r.Payment is null && r.Recipients.Count == 0 && r.Draw is null && r.Stage == WuLuKangShenduanStage.CancelChildren;
        if (r.WindowStage == WuLuKangPindianStage.BeforeSourceReplacement) return r.Recipients.Count == 0 && r.Draw is null && r.RecipientIndex == 0 &&
            r.Stage is WuLuKangShenduanStage.DiscardChoice or WuLuKangShenduanStage.DiscardChildren or WuLuKangShenduanStage.RefillChildren or WuLuKangShenduanStage.ReserveChildren or WuLuKangShenduanStage.CancelChildren;
        if (w.Result is not { } result || r.Payment is not null || !r.Recipients.SequenceEqual(new[] { (result.SourceSeat, result.SourceRank), (result.OpponentSeat, result.OpponentRank) }.Where(i => i.Item2 == 13).Select(i => i.Item1).Distinct()) ||
            r.RecipientIndex < 0 || r.RecipientIndex > r.Recipients.Count || r.Stage is not (WuLuKangShenduanStage.BottomChildren or WuLuKangShenduanStage.Drawing or WuLuKangShenduanStage.DrawChildren)) return false;
        return r.Draw is not { } draw || r.Stage == WuLuKangShenduanStage.DrawChildren && draw.RecipientSeat == r.Recipients[r.RecipientIndex] && draw.RequestedCount == 1 &&
            ProgramEventHistory<WuLuKangKDrawReturnedEvent>().Any(e => e == new WuLuKangKDrawReturnedEvent(f.Id, draw.RecipientSeat, draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count, draw.AttemptFrameId, draw.SequenceBefore, draw.SequenceAfter));
    }
}
