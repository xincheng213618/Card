namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasShenJwPaidPingxiang(ProgramSkillFrame f) => f.ShenJiangWei is { Operation: SkillProgramEffectOp.ResolveShenJiangWeiPingxiang } r &&
        ProgramEventHistory<ShenJiangWeiPingxiangPaidEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.MaximumBefore == r.MaximumBefore && e.MaximumAfter == r.MaximumAfter && e.MaximumBefore > 9 && e.MaximumBefore - e.MaximumAfter == 9) == 1;
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 38400)]
    private bool ProveShenJwMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending)
    {
        if (f.ShenJiangWei is not { Operation: SkillProgramEffectOp.ResolveShenJiangWeiJiufa } r || e?.Op != r.Operation ||
            r.Stage is not (ShenJiangWeiStage.RevealChildren or ShenJiangWeiStage.GainChildren or ShenJiangWeiStage.DiscardChildren) ||
            pending != f.PendingMovementContinuation || pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        var paid = ProgramEventHistory<ShenJiangWeiMovementPaidEvent>().Where(p => p.FrameId == f.Id && p.Stage == r.Stage).ToArray();
        if (paid is not [var p] || p.Before != r.Before || p.After != r.After || p.After > _movementSequence || p.Count < 0) return false;
        var moves = _cardMovements.Where(m => m.Sequence > p.Before && m.Sequence <= p.After).ToArray();
        var allowed = r.Stage == ShenJiangWeiStage.GainChildren ? r.Selected : r.Stage == ShenJiangWeiStage.DiscardChildren ? r.Revealed.Except(r.Selected).ToArray() : r.Revealed;
        var exact = moves.Where(m => allowed.Contains(m.CardId) && (r.Stage == ShenJiangWeiStage.RevealChildren
            ? m.From == CardLocation.DrawPile && m.To == CardLocation.Processing
            : m.From == CardLocation.Processing && m.To == (r.Stage == ShenJiangWeiStage.GainChildren ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile)) &&
            m.Reason.Value == (r.Stage == ShenJiangWeiStage.RevealChildren ? "skill-program.anniversary:jiufa." + f.ActivationId + ".reveal" : r.Stage == ShenJiangWeiStage.GainChildren ? "skill-program.anniversary:jiufa.obtain" : "skill-program.anniversary:jiufa.discard")).ToArray();
        return exact.Length == p.Count && exact.Select(m => m.CardId).Distinct().Count() == p.Count;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38400)]
    private void RequireShenJw(ProgramSkillFrame f)
    {
        if (f.ShenJiangWei is not { } r) return;
        if (r.Source != ShenJwSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) ||
            !Enum.IsDefined(r.Stage) || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Cycle < 0 ||
            ProgramEventHistory<ShenJiangWeiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.ParentId == r.ParentId && e.MovementSequence == r.MovementSequence) != 1)
            throw new InvalidOperationException("Shen Jiang Wei lost his owning frame, content, native occurrence or payment receipt.");
        if (r.ParentId != 0)
        {
            var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
            if (index < 1 || _resolutionStack[index - 1].Id != r.ParentId || _resolutionStack[index - 1] switch {
                CardsMovedTriggerWindowFrame w => w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) || f.WindowContext?.MovementBatch?.Id != w.Batch.Id,
                HpChangedTriggerWindowFrame w => w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) || f.WindowContext?.HpChange != w.Change,
                ProgramCardTriggerWindowFrame w => w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex])) || f.WindowContext?.CardUse?.CardActionId != w.Action.ActionId,
                _ => true }) throw new InvalidOperationException("Shen Jiang Wei lost the original suspended native parent candidate.");
        }
        if (r.Operation == SkillProgramEffectOp.ResolveShenJiangWeiPingxiang)
        {
            if (!HasShenJwPaidPingxiang(f)) throw new InvalidOperationException("Pingxiang requires its real paid nine maximum HP.");
            var issued = ProgramEventHistory<ShenJiangWeiSlashIssuedEvent>().Select(e => e.Origin).Where(o => o.FrameId == f.Id).ToArray();
            var returned = ProgramEventHistory<ShenJiangWeiSlashReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (issued.Length != r.SlashCount || !issued.Select(o => o.Ordinal).SequenceEqual(Enumerable.Range(1, r.SlashCount)) ||
                issued.Any(o => o.Source != r.Source || o.GameplayHash != r.GameplayHash) || returned.Select(e => e.Ordinal).Distinct().Count() != returned.Length ||
                returned.Any(e => !issued.Any(o => o.Ordinal == e.Ordinal && o.UseId == e.UseId && o.ActionId == e.ActionId)) ||
                r.Stage is ShenJiangWeiStage.SlashChoice or ShenJiangWeiStage.SlashChildren or ShenJiangWeiStage.LostSkillChildren && returned.Length != r.SlashCount ||
                r.Stage == ShenJiangWeiStage.NativeUse && returned.Length != r.SlashCount - 1)
                throw new InvalidOperationException("Pingxiang changed its actual ordered issuance and completed-return budget.");
        }
        if (r.Operation == SkillProgramEffectOp.ResolveShenJiangWeiTianren)
        {
            if (ProgramEventHistory<ShenJiangWeiGrowthPaidEvent>().Count(e => e.FrameId == f.Id) != r.Cycle)
                throw new InvalidOperationException("Tianren changed its original paid growth cursor.");
            if (r.MovementSequence > 0 && (ProgramEventHistory<ShenJiangWeiMarkReceivedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.MovementSequence == r.MovementSequence) != 1 ||
                f.WindowContext?.MovementBatch is not { } b || f.WindowContext.MovementIndex is not { } i || b.Movements[i].Sequence != r.MovementSequence || !ShenJwDiscardIndexes(b).Contains(i))) throw new InvalidOperationException("Tianren lost its actual non-use discard receipt.");
            if (r.Cycle > 0 && ProgramEventHistory<ShenJiangWeiGrowthPaidEvent>().Count(e => e.FrameId == f.Id && e.Cycle == r.Cycle && e.Removed == e.MaximumBefore && e.MarkerBefore >= e.Removed && e.MaximumBefore == r.MaximumBefore && e.MaximumAfter == r.MaximumAfter) != 1) throw new InvalidOperationException("Tianren lost its once-only threshold payment.");
            if (r.Stage == ShenJiangWeiStage.DrawChildren && !ProgramEventHistory<ShenJiangWeiDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Cycle == r.Cycle && e.Before == r.Before && e.After == r.After && e.ActualCount == _cardMovements.Count(m => m.Sequence > e.Before && m.Sequence <= e.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.anniversary:tianren.draw"))) throw new InvalidOperationException("Tianren lost its native draw invoice.");
        }
        if (r.Operation == SkillProgramEffectOp.ResolveShenJiangWeiJiufa)
        {
            var native = ShenJwActionParent(f.WindowContext!)!;
            if (ProgramEventHistory<ShenJiangWeiNameRecordedEvent>().Count(e => e.FrameId == (native.ResponseCompletion is null ? native.ParentFrameId : native.Id) && e.Source == r.Source && e.ActionId == r.ActionId && e.Cycle == r.Cycle && e.DistinctCount == 9) != 1)
                throw new InvalidOperationException("Jiufa lost the exact ninth completed native card-name occurrence.");
            if (r.Revealed.Count > 9 || r.Revealed.Distinct().Count() != r.Revealed.Count || r.Selected.Any(id => !r.Revealed.Contains(id)) || r.Selected.Distinct().Count() != r.Selected.Count ||
                r.RankIndex < 0 || r.RankIndex > r.Ranks.Count || r.Ranks.Distinct().Count() != r.Ranks.Count ||
                r.Selected.Any(id => ProgramEventHistory<ShenJiangWeiRankChosenEvent>().Count(e => e.FrameId == f.Id && e.CardId == id && e.Rank == GetAdvancedCard(id).Rank) != 1) ||
                r.Selected.GroupBy(id => GetAdvancedCard(id).Rank).Any(g => g.Count() != 1 || r.Revealed.Count(id => GetAdvancedCard(id).Rank == g.Key) < 2)) throw new InvalidOperationException("Jiufa lost its public nine-card pool or one-card-per-repeated-rank selections.");
            if (r.Revealed.Count > 0 && !GetProgramCardSet(f, ShenJwReveal).CardIds.SequenceEqual(r.Revealed)) throw new InvalidOperationException("Jiufa changed its original native public reveal.");
        }
        if (r.SlashCount is < 0 or > 9 || r.Stage == ShenJiangWeiStage.NativeUse && (LifecycleCardUse(r.UseId) is not { } use || !IsShenJwNativeSlash(use))) throw new InvalidOperationException("Pingxiang lost its exact current native FireSlash.");
    }
}
