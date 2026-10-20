namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MouXiahouShiTurnKey? CurrentMouXiahouShiTurn()
    {
        var history = CompleteProgramEventHistory().ToArray();
        if (history.LastOrDefault(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent) is not TurnStartedEvent turn ||
            turn.TurnNumber != _turnNumber || turn.ActorSeat != _turnProgression.OwnerSeat || _turnProgression.TurnNumber != _turnNumber) return null;
        return new(turn.TurnNumber, turn.ActorSeat, history.OfType<TurnStartedEvent>().Count());
    }
    private bool MouXiahouShiTurnBefore(IGameEvent fact, MouXiahouShiTurnKey turn)
    {
        var h = CompleteProgramEventHistory().ToArray(); var index = Array.FindIndex(h, e => Equals(e, fact));
        if (index < 0 || turn.Ordinal < 1) return false;
        var prefix = h.Take(index).ToArray();
        return prefix.LastOrDefault(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent) is TurnStartedEvent start &&
            start.TurnNumber == turn.TurnNumber && start.ActorSeat == turn.OwnerSeat && prefix.OfType<TurnStartedEvent>().Count() == turn.Ordinal;
    }
    private bool ValidMouXiahouShiPaidHistory(MouXiahouShiYanyuDiscardPaidEvent e) => e.Source.SkillId == MouXiahouShiComposition.Yanyu && e.Source.BindingId == "discard-slash" &&
        MouXiahouShiSlash(e.PrintedKind) && e.From.OwnerSeat == e.Source.OwnerSeat && e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        e.Phase.ActorSeat == e.Source.OwnerSeat && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.TurnNumber == e.Turn.TurnNumber &&
        e.Phase.ActualTurnOwnerSeat == e.Turn.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(e.Phase) && MouXiahouShiTurnBefore(e, e.Turn) &&
        ProgramEventHistory<MouXiahouShiYanyuStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && !s.Support && s.Turn == e.Turn && s.Phase == e.Phase) == 1 &&
        _cardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason.Value == MouXiahouShiDiscardReason) == 1;
    private bool ExactMouXiahouShiSupportContext(ProgramSkillFrame f) => f.SkillId == MouXiahouShiComposition.Yanyu && f.ActivationId == "support-at-play-ending" && f.TriggerId == f.ActivationId &&
        f.InstructionIndex == 1 && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        f.WindowContext is { Window: SkillProgramTriggerWindow.PlayEnding } c && c.OwnerSeat == f.OwnerSeat && c.SourceSeat == f.OwnerSeat &&
        _resolutionStack.SingleOrDefault(w => w.Id == c.ParentFrameId) is ProgramLifecycleTriggerWindowFrame w && w.OwnerSeat == f.OwnerSeat && w.Window == c.Window &&
        w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]);
    private bool ExactMouXiahouShiDamageContext(ProgramSkillFrame f)
    {
        if (f.SkillId != MouXiahouShiComposition.Qiaoshi || f.ActivationId != "foreign-damage-source-choice" || f.TriggerId != f.ActivationId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } c ||
            c.OwnerSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat || c.SourceSeat is not { } source || source == f.OwnerSeat || c.Amount <= 0 ||
            _resolutionStack.SingleOrDefault(i => i.Id == c.ParentFrameId) is not DamageTriggerWindowFrame w || w.TriggerWindow != c.Window ||
            w.TargetSeat != f.OwnerSeat || w.SourceSeat != source || w.ParentFrameId != c.DamageFrameId || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            w.Candidates[w.CandidateIndex].ToProgramCandidate() is not { } candidate || !MountObserverCandidateMatches(f, candidate)) return false;
        // Startup verifies the active native factory. After issuance an inner
        // recovery child may own a different active attack; retain authority on
        // this exact original ancestor and its signed native request instead.
        if (f.MouXiahouShiQiaoshi is { } issued)
        {
            var original = new ProgramSkillWindowContext(w.TriggerWindow, w.Id, f.OwnerSeat, SourceSeat: source, TargetSeat: w.TargetSeat,
                DamageFrameId: w.ParentFrameId, Amount: issued.DamageAmount, OccurrenceIndex: candidate.OccurrenceIndex);
            return c with { Facts = null } == original && issued.WindowId == w.Id && issued.DamageFrameId == w.ParentFrameId &&
                _resolutionStack.SingleOrDefault(i => i.Id == issued.DamageFrameId) is DamageFrame damage && damage.ParentFrameId == issued.AttackResolutionId &&
                damage.SourceSeat == source && damage.TargetSeat == f.OwnerSeat && damage.Amount == issued.DamageAmount &&
                ProgramEventHistory<DamageRequestedEvent>().Count(e => e.ResolutionId == damage.Id && e.SourceSeat == source && e.TargetSeat == f.OwnerSeat &&
                    e.Amount == damage.Amount && e.Nature == damage.Nature && !e.SourceLess) == 1 &&
                ProgramEventHistory<DamageTriggerWindowOpenedEvent>().Count(e => e.ResolutionId == w.Id && e.DamageFrameId == damage.Id && e.SourceSeat == source &&
                    e.TargetSeat == f.OwnerSeat && e.CardId == w.SourceCardId && e.CardKind == w.SourceCard &&
                    e.Candidates.Any(item => item.ToProgramCandidate() is { } frozen && MountObserverCandidateMatches(f, frozen))) == 1;
        }
        var expected = CreateAfterDamageProgramContext(w, candidate);
        if (c with { Facts = null } != expected with { Facts = null }) return false;
        var attack = GetDamageTriggerAttack(w);
        return attack.DamageWasApplied && !attack.IsSourceLess && attack.SourceSeat == source && attack.TargetSeat == f.OwnerSeat && attack.DamageAmount == c.Amount;
    }
    private bool MouXiahouShiYanyuRoot(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiYanyu is not { } r || f.MouXiahouShiQiaoshi is not null || r.Source != MouXiahouShiSource(f) || r.GameplayHash != f.GameplayHash ||
            f.SkillId != MouXiahouShiComposition.Yanyu || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouXiahouShiComposition.IsYanyu(p) ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            r.Phase.TurnNumber != r.Turn.TurnNumber || r.Phase.ActualTurnOwnerSeat != r.Turn.OwnerSeat) return false;
        var started = new MouXiahouShiYanyuStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.Phase, r.Support, r.WindowId, r.FrozenDiscardCount);
        return ProgramEventHistory<MouXiahouShiYanyuStartedEvent>().Count(e => e == started) == 1 && MouXiahouShiTurnBefore(started, r.Turn) &&
            (r.Support ? ExactMouXiahouShiSupportContext(f) && r.WindowId == f.WindowContext!.ParentFrameId && r.FrozenDiscardCount > 0 && f.SelectedCardIds.Count == 0 :
                f.ActivationId == "discard-slash" && f.TriggerId is null && f.WindowContext is null && f.SelectedCardIds.Count == 1 && f.SelectedTargetSeats.Count == 0 && r.WindowId == 0 && r.FrozenDiscardCount == 0);
    }
    private bool ValidMouXiahouShiYanyu(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiYanyu is not { } r || !MouXiahouShiYanyuRoot(f) || r.Before < 0 || r.Before > r.After || r.After > _movementSequence) return false;
        var paid = ProgramEventHistory<MouXiahouShiYanyuDiscardPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Support)
        {
            if (r.TargetSeat is not null || r.Stage == MouXiahouShiYanyuStage.TargetChoice) return false;
            if (r.Payment is not { } payment) return r.Stage == MouXiahouShiYanyuStage.Discarding && paid.Length == 0 && r.Draw is null;
            if (f.SelectedCardIds is not [var id] || payment.CardId != id || paid is not [var cost] || !ValidMouXiahouShiPaidHistory(cost) ||
                cost.Source != r.Source || cost.Turn != r.Turn || cost.Phase != r.Phase ||
                payment != new MouXiahouShiDiscardPayment(cost.CardId, cost.PrintedKind, cost.From, cost.To, cost.Sequence)) return false;
        }
        else
        {
            if (paid.Length != 0 || r.Payment is not null || r.Stage is MouXiahouShiYanyuStage.Discarding or MouXiahouShiYanyuStage.DiscardChildren ||
                r.FrozenDiscardCount != MouXiahouShiTurnDiscards(f.OwnerSeat, r.Turn)) return false;
            var accepted = ProgramEventHistory<MouXiahouShiYanyuTargetAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.TargetSeat is null) return r.Stage == MouXiahouShiYanyuStage.TargetChoice && accepted.Length == 0 && r.Draw is null;
            if (!IsValidPlayerSeat(r.TargetSeat.Value) || r.TargetSeat == f.OwnerSeat || accepted is not [var target] ||
                target != new MouXiahouShiYanyuTargetAcceptedEvent(f.Id, r.TargetSeat.Value, r.FrozenDiscardCount, checked(r.FrozenDiscardCount * 3))) return false;
        }
        return r.Stage is MouXiahouShiYanyuStage.DiscardChildren or MouXiahouShiYanyuStage.Drawing ? r.Draw is null :
            r.Stage == MouXiahouShiYanyuStage.DrawChildren && r.Draw is { } draw && MouXiahouShiDrawSigned(f, draw, r.Support ? r.TargetSeat!.Value : f.OwnerSeat, r.Support ? checked(r.FrozenDiscardCount * 3) : 1);
    }
    private bool ValidMouXiahouShiQiaoshi(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiQiaoshi is not { } r || f.MouXiahouShiYanyu is not null || r.Source != MouXiahouShiSource(f) || r.GameplayHash != f.GameplayHash ||
            !ExactMouXiahouShiDamageContext(f) || !Enum.IsDefined(r.Stage) || r.WindowId != f.WindowContext!.ParentFrameId || r.DamageFrameId != f.WindowContext.DamageFrameId ||
            r.DamageSourceSeat != f.WindowContext.SourceSeat || r.DamageAmount != f.WindowContext.Amount || r.DamageSourceSeat == f.OwnerSeat ||
            r.Before < 0 || r.Before > r.After || r.After > _movementSequence || _contentRegistry.GetSkill(f.SkillId).Program is not { } p ||
            p.GameplayHash != f.GameplayHash || !MouXiahouShiComposition.IsQiaoshi(p)) return false;
        var start = new MouXiahouShiQiaoshiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.WindowId, r.DamageFrameId, r.AttackResolutionId, r.DamageSourceSeat, r.DamageAmount);
        if (ProgramEventHistory<MouXiahouShiQiaoshiStartedEvent>().Count(e => e == start) != 1 || !MouXiahouShiTurnBefore(start, r.Turn)) return false;
        var branches = ProgramEventHistory<MouXiahouShiQiaoshiBranchAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Branch is null) return r.Stage == MouXiahouShiQiaoshiStage.SourceChoice && branches.Length == 0 && r.Draw is null && !r.RecoveryReturned && r.RequestedRecovery == 0 && r.BaseRecovery == 0 && r.RecoveryHpBefore == 0;
        if (r.Branch is not ("recover-and-draw" or "decline") || branches is not [var branch] ||
            branch != new MouXiahouShiQiaoshiBranchAcceptedEvent(f.Id, r.Source, r.Turn, r.DamageSourceSeat, r.Branch) || r.Stage == MouXiahouShiQiaoshiStage.SourceChoice) return false;
        if (r.Branch == "decline") return r.Stage == MouXiahouShiQiaoshiStage.RecoveryChildren && r.RequestedRecovery == 0 && r.BaseRecovery == 0 && r.Draw is null && !r.RecoveryReturned;
        if (r.RequestedRecovery != r.DamageAmount || r.BaseRecovery < 0 || r.BaseRecovery > r.RequestedRecovery ||
            ProgramEventHistory<MouXiahouShiQiaoshiRecoveryIssuedEvent>().Count(e => e == new MouXiahouShiQiaoshiRecoveryIssuedEvent(f.Id, r.DamageSourceSeat, f.OwnerSeat, r.DamageAmount, r.RequestedRecovery, r.RecoveryHpBefore, r.BaseRecovery)) != 1) return false;
        var returned = ProgramEventHistory<MouXiahouShiQiaoshiRecoveryReturnedEvent>().Count(e => e == new MouXiahouShiQiaoshiRecoveryReturnedEvent(f.Id, r.DamageSourceSeat, f.OwnerSeat, r.RequestedRecovery));
        if (returned != (r.RecoveryReturned ? 1 : 0) || r.Stage is MouXiahouShiQiaoshiStage.Drawing or MouXiahouShiQiaoshiStage.DrawChildren && !r.RecoveryReturned) return false;
        return r.Stage == MouXiahouShiQiaoshiStage.DrawChildren ? r.Draw is { } draw && MouXiahouShiDrawSigned(f, draw, r.DamageSourceSeat, 2) : r.Draw is null;
    }
    private bool MouXiahouShiDrawSigned(ProgramSkillFrame f, NativeDrawOutcome d, int recipient, int requested) => d.RecipientSeat == recipient && d.RequestedCount == requested &&
        ProgramEventHistory<MouXiahouShiDrawReturnedEvent>().Count(e => e == new MouXiahouShiDrawReturnedEvent(f.Id, recipient, requested, d.ActualDrawIds.Count,
            d.ReplacementGainedIds.Count, d.SequenceBefore, d.SequenceAfter, d.AttemptFrameId)) == 1;
    private bool ValidMouXiahouShi(ProgramSkillFrame f) => f.MouXiahouShiYanyu is not null ? ValidMouXiahouShiYanyu(f) : f.MouXiahouShiQiaoshi is not null && ValidMouXiahouShiQiaoshi(f);
    private bool PermitIssuedMouXiahouShi(ProgramSkillFrame f) => ValidMouXiahouShi(f) &&
        (f.MouXiahouShiYanyu is { Payment: not null } or { TargetSeat: not null } || f.MouXiahouShiQiaoshi is { Branch: "recover-and-draw" });
    private bool HasPaidMouXiahouShiSelection(ProgramSkillFrame f) => f.MouXiahouShiYanyu is { Support: false, Payment: not null } && ValidMouXiahouShiYanyu(f);
}
