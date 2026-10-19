namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidWangKuangObligation(WangKuangRenxiaObligationIssuedEvent p) => HasWangKuangCapability && p.ActiveFrameId > 0 && p.Source.SkillId == "ol:renxia" &&
        p.Source.BindingId == "choose-first-branch" && p.Phase.OwnerSeat == p.Source.OwnerSeat && p.Phase.ActualTurn > 0 && p.Phase.PhaseInstance > 0 &&
        _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash == p.GameplayHash && Enum.IsDefined(p.FirstBranch) &&
        ProgramEventHistory<WangKuangRenxiaObligationIssuedEvent>().Count(e => e.ActiveFrameId == p.ActiveFrameId) == 1 &&
        ProgramEventHistory<WangKuangRenxiaStartedEvent>().Count(e => e.FrameId == p.ActiveFrameId && e.Source == p.Source && e.GameplayHash == p.GameplayHash && e.Phase == p.Phase && !e.Ending && e.ObligationId is null) == 1 &&
        ProgramEventHistory<WangKuangRenxiaBranchChosenEvent>().Count(e => e.FrameId == p.ActiveFrameId && e.Branch == p.FirstBranch) == 1 &&
        ProgramEventHistory<WangKuangRenxiaIterationPaidEvent>().Count(e => e.FrameId == p.ActiveFrameId && e.Cursor == 1 && e.Branch == p.FirstBranch &&
            (p.FirstBranch == WangKuangRenxiaBranch.Discard ? e.ActualCount == 2 : e.ActualCount is >= 0 and <= 2)) == 1;
    private bool ValidWangKuang(ProgramSkillFrame f)
    {
        if (f.WangKuangRenxia is not { } r || f.SkillId != "ol:renxia" || f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != WangKuangSource(f) ||
            r.GameplayHash != f.GameplayHash || r.Phase.OwnerSeat != f.OwnerSeat || r.Phase.ActualTurn != _turnNumber || r.Phase.PhaseInstance < 1 || !Enum.IsDefined(r.Stage) ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            GetPausedPrivateOfferEffect(f)?.Op != (r.Ending ? SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding : SkillProgramEffectOp.ResolveRenxiaRepeatedBranch) ||
            ProgramEventHistory<WangKuangRenxiaStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Phase == r.Phase && e.Ending == r.Ending && e.ObligationId == r.ObligationId) != 1) return false;
        if (r.Ending)
        {
            var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
            if (r.ObligationId is not { } obligation || i < 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding } context ||
                _resolutionStack[i - 1] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id != context.ParentFrameId || parent.OwnerSeat != f.OwnerSeat || parent.WangKuangRenxiaEndingPhase != r.Phase ||
                parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
                ProgramEventHistory<WangKuangRenxiaObligationIssuedEvent>().SingleOrDefault(e => e.ActiveFrameId == obligation) is not { } promise || !ValidWangKuangObligation(promise) ||
                promise.Phase != r.Phase || promise.Source.OwnerSeat != f.OwnerSeat || promise.Source.SkillInstanceId != f.SkillInstanceId || promise.GameplayHash != r.GameplayHash ||
                r.Branch == promise.FirstBranch || ProgramEventHistory<WangKuangRenxiaEndingConsumedEvent>().Count(e => e.ActiveFrameId == obligation && e.EndingFrameId == f.Id && e.WindowId == parent.Id && e.Phase == r.Phase) != 1) return false;
        }
        else if (f.WindowContext is not null || f.TriggerId is not null || r.ObligationId is not null || f.ActivationId != "choose-first-branch") return false;
        var paid = ProgramEventHistory<WangKuangRenxiaIterationPaidEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Cursor).ToArray();
        var compared = ProgramEventHistory<WangKuangRenxiaComparedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Cursor).ToArray();
        var branch = ProgramEventHistory<WangKuangRenxiaBranchChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == WangKuangRenxiaStage.BranchChoice) return !r.Ending && r.Branch is null && r.Cursor == 0 && paid.Length == 0 && compared.Length == 0 && branch.Length == 0 && r.Selected.Count == 0 && r.Candidates.Count == 0;
        if (r.Branch is not { } chosen || !Enum.IsDefined(chosen) || branch is not [{ Branch: var original }] || original != chosen || r.Cursor < 0 || paid.Length != r.Cursor ||
            compared.Length != r.Cursor - (r.Stage == WangKuangRenxiaStage.MovementChildren ? 1 : 0) || compared.Any(e => !e.Repeat) ||
            r.Candidates.Select(c => c.CardId).Distinct().Count() != r.Candidates.Count || r.Selected.Select(c => c.CardId).Distinct().Count() != r.Selected.Count ||
            r.Selected.Any(c => !r.Candidates.Contains(c)) || r.Candidates.Any(c => c.From.OwnerSeat != f.OwnerSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        for (var n = 0; n < paid.Length; n++)
        {
            var p = paid[n]; if (p.Cursor != n + 1 || p.Branch != chosen || p.Before < 0 || p.After < p.Before || p.After > _movementSequence || p.ActualCount is < 0 or > 2 ||
                n < compared.Length && compared[n].Cursor != n + 1) return false;
            if (chosen == WangKuangRenxiaBranch.Draw)
            { if (_cardMovements.Count(m => m.Sequence > p.Before && m.Sequence <= p.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == RenxiaDrawReason) != p.ActualCount) return false; }
            else
            {
                var cards = ProgramEventHistory<WangKuangRenxiaDiscardEntityEvent>().Where(e => e.FrameId == f.Id && e.Cursor == p.Cursor).ToArray();
                if (cards.Length != p.ActualCount || cards.Select(c => c.CardId).Distinct().Count() != cards.Length || cards.Any(c => c.From.OwnerSeat != f.OwnerSeat ||
                    c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || c.Sequence <= p.Before || c.Sequence > p.After ||
                    _cardMovements.Count(m => m.Sequence == c.Sequence && m.CardId == c.CardId && m.From == c.From && m.Reason.Value == RenxiaDiscardReason &&
                        m.To == NormalizeDiouCarrierDestination(GetAttackCard(c.CardId), c.From, (c.From.Zone == CardZoneKind.Equipment && GetAttackCard(c.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile))) != 1)) return false;
            }
        }
        if (r.Stage == WangKuangRenxiaStage.DiscardChoice) return chosen == WangKuangRenxiaBranch.Discard && r.RequiredCount is >= 1 and <= 2 && r.Selected.Count < r.RequiredCount &&
            r.Candidates.All(c => WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId).Contains(c));
        if (r.Cursor < 1 || r.Before != paid[^1].Before || r.After < paid[^1].After || r.After > _movementSequence || r.ActualCount != paid[^1].ActualCount) return false;
        if (chosen == WangKuangRenxiaBranch.Draw) return r.After == paid[^1].After && r.Selected.Count == 0 && r.Candidates.Count == 0;
        var tail = ProgramEventHistory<WangKuangRenxiaTailClosedEvent>().Where(e => e.FrameId == f.Id && e.Cursor == r.Cursor).ToArray();
        return r.Selected.Count == r.RequiredCount && r.Selected.Count == r.ActualCount && (r.After == paid[^1].After && tail.Length == 0 || tail is [{ After: var after }] && after == r.After);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26910)]
    private void AssertWangKuang(ProgramSkillFrame f)
    {
        if (f.WangKuangRenxia is not { } r) return;
        if (!ValidWangKuang(f)) throw new InvalidOperationException("Renxia lost its original source, exact actual Play obligation or frozen native per-iteration payment.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is WangKuangRenxiaStage.BranchChoice or WangKuangRenxiaStage.DiscardChoice &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, WangKuangChoices(f))))
            throw new InvalidOperationException("Renxia lost its private original-owner branch or physical discard chooser.");
    }
}
