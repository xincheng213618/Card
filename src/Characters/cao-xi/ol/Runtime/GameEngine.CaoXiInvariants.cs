namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidCaoXiGangshu(ProgramSkillFrame f)
    {
        if (f.CaoXiGangshu is not { } r || f.SkillId != "ol:gangshu" || f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != CaoXiSource(f) ||
            r.GameplayHash != f.GameplayHash || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } c || c.ParentFrameId != r.ParentWindowId ||
            use.CardActionId != r.ActionId || use.ParentCardUseFrameId != r.CardUseFrameId || use.ActorSeat != f.OwnerSeat ||
            MatchesProgramCardCategory(use.EffectiveKind, [SkillProgramCardCategory.Basic]) || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveGangshuAttributeIncrement ||
            r.Numbers != CurrentCaoXiNumbers(f.OwnerSeat) || !CaoXiQueries.Any(q => r.Numbers.Value(q) < 5) ||
            CompleteProgramEventHistory().OfType<CaoXiGangshuStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ParentWindowId == r.ParentWindowId && e.CardUseFrameId == r.CardUseFrameId && e.ActionId == r.ActionId && e.Numbers == r.Numbers) != 1) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return i > 0 && _resolutionStack[i - 1] is ProgramCardTriggerWindowFrame w && w.Id == r.ParentWindowId && w.Action.ActionId == r.ActionId &&
            w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && w.Candidates[w.CandidateIndex] is { } candidate && candidate.OwnerSeat == f.OwnerSeat &&
            candidate.SkillId == f.SkillId && candidate.TriggerId == f.TriggerId && candidate.SkillInstanceId == f.SkillInstanceId && candidate.GameplayHash == f.GameplayHash;
    }
    private bool ValidCaoXiJianxuan(ProgramSkillFrame f)
    {
        if (f.CaoXiJianxuan is not { } r || f.SkillId != "ol:jianxuan" || f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != CaoXiSource(f) ||
            r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } c || c.ParentFrameId != r.ParentWindowId ||
            c.DamageFrameId != r.DamageFrameId || c.TargetSeat != f.OwnerSeat || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveJianxuanRepeatedDraw ||
            CompleteProgramEventHistory().OfType<CaoXiJianxuanStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ParentWindowId == r.ParentWindowId && e.DamageFrameId == r.DamageFrameId && e.ActualTurn == r.ActualTurn) != 1) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 2 || _resolutionStack[i - 1] is not DamageTriggerWindowFrame w || w.Id != r.ParentWindowId || w.ParentFrameId != r.DamageFrameId ||
            w.TargetSeat != f.OwnerSeat || w.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) || _resolutionStack[i - 2] is not DamageFrame damage ||
            damage.Id != r.DamageFrameId || damage.TargetSeat != f.OwnerSeat || damage.Amount != c.Amount || damage.Amount <= 0) return false;
        var recipients = CompleteProgramEventHistory().OfType<CaoXiJianxuanRecipientEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var paid = CompleteProgramEventHistory().OfType<CaoXiJianxuanDrawPaidEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Cursor).ToArray();
        var compared = CompleteProgramEventHistory().OfType<CaoXiJianxuanComparedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Cursor).ToArray();
        if (r.Stage == CaoXiJianxuanStage.ChoosingRecipient)
            return r.RecipientSeat is null && r.Cursor == 0 && r.Before == 0 && r.After == 0 && r.ActualDrawCount == 0 && recipients.Length == 0 && paid.Length == 0 && compared.Length == 0;
        if (r.RecipientSeat is not { } recipient || !IsValidPlayerSeat(recipient) || recipients is not [{ RecipientSeat: var chosen }] || chosen != recipient || r.Cursor < 1 ||
            paid.Length != r.Cursor || compared.Length != r.Cursor - 1 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.ActualDrawCount is < 0 or > 1) return false;
        for (var n = 0; n < paid.Length; n++)
        {
            var p = paid[n];
            if (p.Cursor != n + 1 || p.RecipientSeat != recipient || p.ActualDrawCount is < 0 or > 1 || p.Before < 0 || p.After < p.Before || p.After > _movementSequence ||
                _cardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason.Value == CaoXiDrawReason &&
                    m.Sequence > p.Before && m.Sequence <= p.After) != p.ActualDrawCount) return false;
            if (n < compared.Length && (compared[n].Cursor != n + 1 || compared[n].RecipientSeat != recipient || !compared[n].Repeat ||
                !compared[n].Numbers.Contains(compared[n].HandCount) || p.ActualDrawCount != 1)) return false;
        }
        var last = paid[^1]; return last.Before == r.Before && last.After == r.After && last.ActualDrawCount == r.ActualDrawCount;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26810)]
    private void RequireCaoXi(ProgramSkillFrame f)
    {
        if (f.CaoXiGangshu is not null && !ValidCaoXiGangshu(f) || f.CaoXiJianxuan is not null && !ValidCaoXiJianxuan(f))
            throw new InvalidOperationException("Cao Xi lost its original source, actual use/damage window, single recipient or paid per-draw cursor.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && (f.CaoXiGangshu is not null || f.CaoXiJianxuan?.Stage == CaoXiJianxuanStage.ChoosingRecipient) &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, CaoXiChoices(f))))
            throw new InvalidOperationException("Cao Xi lost its exact unissued owner choice.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26810)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26810)]
    private bool ContinuePaidCaoXi(ProgramSkillFrame f) => f.CaoXiJianxuan is { Stage: CaoXiJianxuanStage.DrawChildren, Cursor: > 0 } && ValidCaoXiJianxuan(f);
}
