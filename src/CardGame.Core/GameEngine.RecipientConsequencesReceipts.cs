namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A terminal game preserves this producer's paid diagnostic receipt. No
    // prompt or successor is manufactured after the matching terminal fact.
    private bool RecipientConsequencesGameEnded() => _status == EngineStatus.Completed && _phase == TurnPhase.Finished &&
        _winner != Winner.None && CompleteProgramEventHistory().OfType<GameEndedEvent>().Count(e =>
            e.Winner == _winner && e.TeamId == (IsTeamMode ? _winnerTeamId : null) &&
            e.FactionId == (IsNationalWarMode ? _winnerFactionId : null)) == 1;
    private bool BlackGiftGameIssued(int owner, string skill) => CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>()
        .Any(e => e.OwnerSeat == owner && e.SkillId == skill);
    private bool PrintedLordGameIssued(int owner, string skill, string binding) => CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>()
        .Any(e => e.OwnerSeat == owner && e.SkillId == skill && e.BindingId == binding);
    private bool CanRunRecipientConsequences(ProgramTriggerCandidate c, SkillProgramTrigger trigger) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord) ||
        c.OwnerSeat == _currentSeat && !PrintedLordGameIssued(c.OwnerSeat, c.SkillId, c.BindingId) &&
        _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && p.Gender == GeneralGender.Male);

    private bool ValidBlackGiftContest(ProgramSkillFrame f)
    {
        if (f.BlackGiftContest is not { } r || r.InstructionIndex != 1 || f.InstructionIndex != 1 ||
            r.Source != RecipientConsequencesSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber ||
            r.TurnOwnerSeat != f.OwnerSeat || f.OwnerSeat != _currentSeat || (_phase != TurnPhase.Play && !RecipientConsequencesGameEnded()) ||
            f.TriggerId is not null || f.WindowContext is not null || f.SelectedCardIds is not [var card] || card != r.GiftCardId ||
            f.SelectedTargetSeats is not [var recipient] || recipient != r.RecipientSeat || recipient == f.OwnerSeat || !IsValidPlayerSeat(recipient) ||
            r.GiftSuit is not (Suit.Spade or Suit.Club) || r.GiftCardId <= 0 || r.GiftBefore < 0 || r.GiftAfter <= r.GiftBefore ||
            !Enum.IsDefined(r.Stage) || r.RequiredDiscards is < 0 or > 2 || r.SelectedDiscardIds.Count > r.RequiredDiscards ||
            r.SelectedDiscardIds.Distinct().Count() != r.SelectedDiscardIds.Count ||
            r.Stage != BlackGiftContestStage.ChoosingDiscard && r.SelectedDiscardIds.Count != 0 ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest }]) return false;
        var facts = CompleteProgramEventHistory().ToArray();
        if (facts.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            facts.OfType<BlackGiftContestGiftPaidEvent>().Count(e => e == new BlackGiftContestGiftPaidEvent(f.Id, r.Source, r.GameplayHash,
                r.ActualTurn, r.TurnOwnerSeat, recipient, r.GiftBefore, r.GiftAfter)) != 1 ||
            _cardMovements.Count(m => m.Sequence > r.GiftBefore && m.Sequence <= r.GiftAfter && m.CardId == card &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(recipient) && m.Reason.Value == BlackGiftReason(f)) != 1 ||
            _cardMovements.Count(m => m.Sequence > r.GiftBefore && m.Sequence <= r.GiftAfter) != 1) return false;
        if (r.Stage is BlackGiftContestStage.GiftPaid or BlackGiftContestStage.ChoosingSecond)
        {
            if (r.SecondSeat is not null || r.PindianFrameId is not null || r.Result is not null || r.Discard is not null || r.LossIndex != 0 || r.Losses.Count != 0) return false;
        }
        else
        {
            if (r.SecondSeat is not { } second || !IsValidPlayerSeat(second) || second == recipient || r.PindianFrameId is not { } pindian || pindian <= f.Id ||
                facts.OfType<BlackGiftContestStartedEvent>().Count(e => e == new BlackGiftContestStartedEvent(f.Id, recipient, second, pindian)) != 1) return false;
            if (r.Stage == BlackGiftContestStage.Pindian)
            {
                if (r.Result is not null || r.Discard is not null || r.Losses.Count != 0 || r.LossIndex != 0) return false;
                if (_resolutionStack.OfType<PindianFrame>().SingleOrDefault(p => p.Id == pindian) is { } child)
                { if (child.ParentFrameId != f.Id || child.SkillId != f.SkillId || child.SourceSeat != recipient || child.OpponentSeat != second ||
                    child.ProgramResultBind != BlackGiftResultBind(f) || child.ProgramResultVisibility != SkillProgramCardSetVisibility.Public) return false; }
                else if (!BlackGiftResultMatches(f, out _)) return false;
            }
            else if (r.Result is not { } result || !BlackGiftResultMatches(f, out var original) || result != original ||
                facts.OfType<BlackGiftContestResultCapturedEvent>().Count(e => e == new BlackGiftContestResultCapturedEvent(f.Id, pindian, result)) != 1) return false;
        }
        if (r.Discard is { } paid)
        {
            if (r.Result is null || BlackGiftWinner(r.Result) != paid.WinnerSeat || paid.CardIds.Count != r.RequiredDiscards || paid.CardIds.Count is < 1 or > 2 ||
                paid.CardIds.Count != paid.Locations.Count || paid.CardIds.Distinct().Count() != paid.CardIds.Count ||
                paid.Locations.Any(l => l.OwnerSeat != paid.WinnerSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                paid.Before < r.GiftAfter || paid.After <= paid.Before ||
                facts.OfType<BlackGiftContestDiscardPaidEvent>().Count(e => e == new BlackGiftContestDiscardPaidEvent(f.Id, paid.WinnerSeat, paid.CardIds.Count, paid.Before, paid.After)) != 1) return false;
            var records = _cardMovements.Where(m => m.Sequence > paid.Before && m.Sequence <= paid.After).ToArray();
            foreach (var pair in paid.CardIds.Select((id, i) => (id, from: paid.Locations[i])))
                if (records.Count(m => m.CardId == pair.id && m.From == pair.from && m.Reason.Value == BlackGiftDiscardReason(f) &&
                    (m.To == CardLocation.DiscardPile || pair.from == CardLocation.Equipment(paid.WinnerSeat) && m.To == CardLocation.OutsideGame && GetAdvancedCard(pair.id).IsGeneralWeapon)) != 1) return false;
            if (records.Where(m => !paid.CardIds.Contains(m.CardId)).Any(m => m.From != CardLocation.WoodenOxGrain(paid.WinnerSeat) ||
                m.To != CardLocation.DiscardPile || m.Reason != CardMoveReasons.WoodenOxGrainDiscard ||
                !records.Any(p => paid.CardIds.Contains(p.CardId) && p.CardKind == CardKind.WoodenOx && p.From == CardLocation.Equipment(paid.WinnerSeat)))) return false;
        }
        else if (r.Stage == BlackGiftContestStage.DiscardPaid) return false;
        if (r.Result is { } p)
        {
            var losers = BlackGiftLosers(p);
            if (r.LossIndex < 0 || r.LossIndex > losers.Length || r.Losses.Select(l => l.Seat).Distinct().Count() != r.Losses.Count ||
                r.Losses.Any(l => !losers.Contains(l.Seat) || l.BeforeHp <= 0 || l.AfterHp != Math.Max(0, l.BeforeHp - 1) ||
                    facts.OfType<BlackGiftContestLossPaidEvent>().Count(e => e.FrameId == f.Id && e.Index == Array.IndexOf(losers, l.Seat) && e.Loss == l) != 1 ||
                    facts.OfType<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == l.Seat &&
                        e.Amount == 1 && e.RemainingHp == l.AfterHp) != 1)) return false;
            if (r.Stage == BlackGiftContestStage.LossPaid && (r.LossIndex >= losers.Length || r.Losses.LastOrDefault()?.Seat != losers[r.LossIndex])) return false;
        }
        var pending = f.PendingMovementContinuation;
        if (r.Stage is BlackGiftContestStage.GiftPaid or BlackGiftContestStage.DiscardPaid or BlackGiftContestStage.Pindian)
        {
            if (pending is not { BeforeCount: 0, CoverageResultBind: null } ||
                pending.SubjectSeat != (r.Stage == BlackGiftContestStage.GiftPaid ? f.OwnerSeat :
                    r.Stage == BlackGiftContestStage.Pindian ? r.RecipientSeat : r.Discard!.WinnerSeat)) return false;
        }
        else if (pending is not null) return false;
        if (!RecipientConsequencesGameEnded() && _resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is BlackGiftContestStage.ChoosingSecond or BlackGiftContestStage.ChoosingDiscard)
        {
            var chooser = r.Stage == BlackGiftContestStage.ChoosingSecond ? f.OwnerSeat : BlackGiftWinner(r.Result!)!.Value;
            if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != chooser ||
                prompt.TargetSeat != chooser || prompt.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(prompt.Choices, BlackGiftChoices(f))) return false;
        }
        return true;
    }

    private bool ValidPrintedLordBenefit(ProgramSkillFrame f)
    {
        if (f.PrintedLordBenefit is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            r.Source != RecipientConsequencesSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber ||
            !ExactPrintedLordStart(f) || f.WindowContext!.ParentFrameId != r.WindowFrameId || !Enum.IsDefined(r.Stage) ||
            f.PendingMovementContinuation is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) return false;
        var facts = CompleteProgramEventHistory().ToArray();
        if (facts.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
            e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) != 1) return false;
        if (r.Stage == PrintedLordBenefitStage.ChoosingBeneficiary)
        {
            if (r.BeneficiarySeat != -1 || r.MaximumBefore != 0 || r.MaximumAfter != 0 || r.RecoveryRequested != 0 || r.PrintedQualifications.Count != 0) return false;
            if (_resolutionStack.LastOrDefault()?.Id != f.Id || RecipientConsequencesGameEnded()) return true;
            return _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == f.OwnerSeat &&
                p.TargetSeat == f.OwnerSeat && p.SkillPrompt?.SkillId == f.SkillId && AssistedChoicesEqual(p.Choices, PrintedLordChoices(f));
        }
        if (!IsValidPlayerSeat(r.BeneficiarySeat) || r.BeneficiarySeat == f.OwnerSeat || r.MaximumBefore < 1 || r.MaximumAfter != r.MaximumBefore + 1 ||
            r.RecoveryRequested is < 0 or > 1 ||
            facts.OfType<PrintedLordMaximumPaidEvent>().Count(e => e == new PrintedLordMaximumPaidEvent(f.Id, r.BeneficiarySeat, r.MaximumBefore, r.MaximumAfter)) != 1 ||
            facts.OfType<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == r.BeneficiarySeat && e.Delta == 1 && e.MaximumHp == r.MaximumAfter && e.SkillId == f.SkillId) < 1 ||
            r.PrintedQualifications.Select(q => q.GrantId).Distinct().Count() != r.PrintedQualifications.Count ||
            r.PrintedQualifications.Any(q => q.BeneficiarySeat != r.BeneficiarySeat || q.ProgramFrameId != f.Id || q.Issuer != r.Source || q.GameplayHash != r.GameplayHash ||
                q.TemplateSourceId is not (CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource) || string.IsNullOrWhiteSpace(q.GeneralId) ||
                q.GrantId != $"{q.TemplateSourceId}:{q.SkillId}" || q.SkillInstanceId != q.GrantId || !_contentRegistry.GetSkill(q.SkillId).Tags.HasFlag(SkillTag.Lord) ||
                !_contentRegistry.Generals.TryGetValue(q.GeneralId, out var general) || !general.SkillIds.Contains(q.SkillId))) return false;
        if (r.Stage is PrintedLordBenefitStage.MaximumPaid or PrintedLordBenefitStage.RecoveryPaid && r.PrintedQualifications.Count != 0) return false;
        if (r.Stage == PrintedLordBenefitStage.MaximumPaid) return r.RecoveryRequested == 0;
        return facts.OfType<PrintedLordRecoveryRequestedEvent>().Count(e => e == new PrintedLordRecoveryRequestedEvent(f.Id, r.BeneficiarySeat, r.RecoveryRequested)) == 1 &&
            (r.Stage is not (PrintedLordBenefitStage.Qualifying or PrintedLordBenefitStage.Complete) ||
                facts.OfType<PrintedLordQualificationCapturedEvent>().Count(e => e == new PrintedLordQualificationCapturedEvent(f.Id, r.BeneficiarySeat)) == 1) &&
            (r.Stage != PrintedLordBenefitStage.Complete || facts.OfType<PrintedLordQualificationIssuedEvent>().Count(e => e ==
                new PrintedLordQualificationIssuedEvent(f.Id, r.BeneficiarySeat, r.Source, r.GameplayHash)) == 1);
    }

    private void AssertRecipientConsequences(ProgramSkillFrame f)
    {
        if (f.BlackGiftContest is not null && !ValidBlackGiftContest(f) || f.PrintedLordBenefit is not null && !ValidPrintedLordBenefit(f) ||
            f.BlackGiftContest is not null && f.PrintedLordBenefit is not null)
            throw new InvalidOperationException("Recipient consequences lost their exact paid fact, original participant or owning instruction.");
    }
    private bool IsRecipientConsequencesMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        f.BlackGiftContest is { Stage: BlackGiftContestStage.GiftPaid or BlackGiftContestStage.DiscardPaid or BlackGiftContestStage.Pindian } &&
        e?.Op == SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest && pending == f.PendingMovementContinuation && ValidBlackGiftContest(f);
    private bool ReturnRecipientConsequencesMovement(ProgramSkillFrame f)
    {
        if (f.BlackGiftContest is null || f.PendingMovementContinuation is null) return false;
        AssertRecipientConsequences(f); AdvanceRuntimeProgram(f.Id); return true;
    }
}
