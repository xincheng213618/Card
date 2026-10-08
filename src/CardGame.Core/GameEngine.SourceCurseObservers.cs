namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SourceCurseMovementMatches(SourceCurseProgramReceipt r, int cardId,
        CardLocation from, CardLocation to, string reason) => _cardMovements.Count(m =>
            m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == cardId &&
            m.From == from && m.To == to && m.Reason.Value == reason) == 1;
    private bool ValidSourceCurseReceipt(ProgramSkillFrame f)
    {
        if (f.SourceCurseReceipt is not { } r || r.InstructionIndex != f.InstructionIndex ||
            r.ActualTurn != _turnNumber || r.Before < 0 || r.After < r.Before ||
            SourceCursePausedEffect(f) is not { } effect) return false;
        if (r.Stage == SourceCurseStage.DepositChildren)
        {
            if (effect.Op != SkillProgramEffectOp.DepositSelectedSourceCurse || r.Deposit is not { } d ||
                d.DepositFrameId != f.Id || d.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) ||
                d.GameplayHash != f.GameplayHash || d.CreatedTurn != r.ActualTurn ||
                !f.SelectedCardIds.SequenceEqual([d.CardId]) || !f.SelectedTargetSeats.SequenceEqual([d.TargetSeat]) ||
                r.PaymentFrom is not { } from || from.OwnerSeat != f.OwnerSeat ||
                from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || r.After <= r.Before ||
                !SourceCurseMovementMatches(r, d.CardId, from, d.Location, "program.source-curse.deposit")) return false;
            return CompleteProgramEventHistory().OfType<SourceCurseDepositedEvent>().Count(e =>
                e.Deposit == d && e.From == from && _cardMovements.Any(m =>
                    m.Sequence == e.MovementSequence && m.CardId == d.CardId && m.From == from && m.To == d.Location)) == 1;
        }
        if (r.Stage == SourceCurseStage.LossHpChildren)
        {
            if (effect.Op != SkillProgramEffectOp.LoseHpForLostSourceCurses || r.Deposit is not null ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } context ||
                context.ParentFrameId != r.OriginalParentId || r.LossIndex < 0 || r.LossIndex > r.Losses.Count ||
                r.Losses.Count == 0 || r.Losses.Select(l => l.Deposit.TargetSeat).Distinct().Count() != r.Losses.Count ||
                r.Losses.Any(l => l.ActualTurn != r.ActualTurn || !_sourceCurseLosses.Contains(l) ||
                    l.Deposit.Source.OwnerSeat != f.OwnerSeat || l.Deposit.Source.SkillId != f.SkillId ||
                    l.Deposit.Source.SkillInstanceId != f.SkillInstanceId || l.Deposit.GameplayHash != f.GameplayHash) ||
                _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(w => w.Id == r.OriginalParentId) is not { } parent ||
                !MatchesAfterTurnEndedChild(parent, f, context)) return false;
            return CompleteProgramEventHistory().OfType<SourceCurseLossRosterIssuedEvent>().Count(e =>
                e.ProgramFrameId == f.Id && e.ActualTurnEndFrameId == r.OriginalParentId &&
                e.ActualTurn == r.ActualTurn && e.Losses.SequenceEqual(r.Losses)) == 1 &&
                CompleteProgramEventHistory().OfType<SourceCurseHpLossIssuedEvent>().Where(e => e.ProgramFrameId == f.Id)
                    .All(e => e.ActualTurn == r.ActualTurn && e.RosterIndex >= 0 && e.RosterIndex < r.LossIndex &&
                        r.Losses[e.RosterIndex].Deposit.TargetSeat == e.TargetSeat) &&
                CompleteProgramEventHistory().OfType<SourceCurseHpLossIssuedEvent>().Where(e => e.ProgramFrameId == f.Id)
                    .GroupBy(e => e.RosterIndex).All(g => g.Count() == 1);
        }
        if (effect.Op != SkillProgramEffectOp.DrawForSourceCurseUse || r.Deposit is not { } curse ||
            curse.Source.OwnerSeat != f.OwnerSeat || curse.BenefitSkillId != f.SkillId ||
            curse.BenefitSkillInstanceId != f.SkillInstanceId || curse.BenefitGameplayHash != f.GameplayHash ||
            r.DrawCount is < 0 or > 1 || curse.ActualDrawCount is < 0 or > 2 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, SourceCurseUse: { } original } c ||
            c.ParentFrameId != r.OriginalParentId || original.ActionId != r.ActionId ||
            !SameSourceCurse(original.Deposit, curse) || original.ActorSeat != curse.TargetSeat ||
            original.EffectiveIsRed is not { } color || color != SuitColor(curse.PrintedSuit) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.Id == r.OriginalParentId) is not { } window ||
            window.Action.ActionId != r.ActionId || window.Action.ActorSeat != original.ActorSeat ||
            window.Action.Type != CardActionType.Use || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex]))) return false;
        var facts = CompleteProgramEventHistory().OfType<SourceCurseDrawIssuedEvent>().Where(e =>
            e.ProgramFrameId == f.Id && e.ParentFrameId == r.OriginalParentId && e.ActionId == r.ActionId).ToArray();
        if (facts is not [var issued] || issued.Deposit != curse || issued.ActualDrawCount != r.DrawCount ||
            issued.After < issued.Before || _cardMovements.Count(m => m.Sequence > issued.Before && m.Sequence <= issued.After &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) &&
                m.Reason.Value == "program.source-curse.draw") != r.DrawCount) return false;
        if (r.Stage == SourceCurseStage.DrawChildren)
            return r.Before == issued.Before && r.After == issued.After;
        return r.Stage == SourceCurseStage.ObtainChildren && r.DrawCount == 1 && curse.ActualDrawCount == 2 &&
            r.After > r.Before && SourceCurseMovementMatches(r, curse.CardId, curse.Location,
                CardLocation.Hand(f.OwnerSeat), "program.source-curse.obtain") &&
            CompleteProgramEventHistory().OfType<SourceCurseObtainedEvent>().Count(e =>
                e.ProgramFrameId == f.Id && e.Deposit == curse && e.Before == r.Before && e.After == r.After) == 1;
    }
    private void AssertSourceCurseReceipt(ProgramSkillFrame f)
    {
        if (f.SourceCurseReceipt is not null && !ValidSourceCurseReceipt(f))
            throw new InvalidOperationException("A source curse lost its original physical deposit, paid segment or exact typed parent.");
    }
    private void AssertSourceCurseState()
    {
        if (_sourceCurses.Select(c => c.TargetSeat).Distinct().Count() != _sourceCurses.Count ||
            _sourceCurses.Select(c => c.CardId).Distinct().Count() != _sourceCurses.Count ||
            _sourceCurses.Select(c => c.DepositFrameId).Distinct().Count() != _sourceCurses.Count)
            throw new InvalidOperationException("Each target has at most one distinct physical source curse.");
        foreach (var d in _sourceCurses)
            if (d.DepositFrameId <= 0 || !IsValidPlayerSeat(d.Source.OwnerSeat) || !IsValidPlayerSeat(d.TargetSeat) ||
                d.Source.OwnerSeat == d.TargetSeat || d.ActualDrawCount is < 0 or > 2 ||
                _cardZones.CardsAt(d.Location) is not [var card] || card.Id != d.CardId ||
                card.Kind != d.CardKind || card.Suit != d.PrintedSuit ||
                CompleteProgramEventHistory().OfType<SourceCurseDepositedEvent>().Count(e =>
                    SameSourceCurse(e.Deposit, d)) != 1)
                throw new InvalidOperationException("An issued curse lost its actual target-owned card or original source identity.");
        foreach (var loss in _sourceCurseLosses)
            if (!_cardMovements.Any(m => m.Sequence == loss.MovementSequence && m.TurnNumber == loss.ActualTurn &&
                m.CardId == loss.Deposit.CardId && m.From == loss.Deposit.Location && m.To == loss.Destination &&
                m.Reason.Value == loss.Reason))
                throw new InvalidOperationException("A curse loss must be backed by its exact real movement.");
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>()) AssertSourceCurseReceipt(f);
    }
    private bool SourceCurseFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.SourceCurseReceipt is not { } r || !ValidSourceCurseReceipt(root)) return false;
        if (r.Stage == SourceCurseStage.LossHpChildren)
        {
            if (r.CurrentHpTarget is not { } target) return false;
            if (child is DyingFrame dying) return dying.ParentFrameId == root.Id &&
                dying.Continuation == DyingContinuationKind.ProgramSkill && dying.VictimSeat == target &&
                (ActiveDying?.FrameId == dying.Id || (IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) ||
                 IsOriginalDyingSuspendedByOwnedDeathBenefit(dying)) &&
                CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Any(e =>
                    e.FrameId == root.Id && e.SkillId == root.SkillId && e.TargetSeat == target && e.RemainingHp == 0);
            return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == root.Id &&
                hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.Program &&
                hp.Change.TargetSeat == target && hp.Change.Kind == HpChangeKind.Loss;
        }
        if (root.PendingMovementContinuation is null) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
                moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage != SourceCurseStage.DepositChildren || r.Deposit?.CardKind != CardKind.SilverLion ||
            r.PaymentFrom != CardLocation.Equipment(root.OwnerSeat)) return false;
        if (child is HpChangedTriggerWindowFrame lion)
            return lion.Change.ParentFrameId == root.Id && lion.ResumeFrameId == root.Id &&
                lion.Continuation == PostEventContinuation.AwaitedProgramMovement && lion.Change.Kind == HpChangeKind.Recovery &&
                lion.Change.SourceSeat == root.OwnerSeat && lion.Change.TargetSeat == root.OwnerSeat && lion.Change.Amount == 1;
        return child is RecoveryReplacementFrame replacement && RecoveryReplacementFrameRidesOn(replacement, root) &&
            replacement.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replacement.Attempt.SourceSeat == root.OwnerSeat && replacement.Attempt.TargetSeat == root.OwnerSeat &&
            replacement.Attempt.Amount == 1 && replacement.Attempt.Completion.MoveReason?.Value == "program.source-curse.deposit";
    }
    private ProgramSkillFrame? SourceCurseObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root || !SourceCurseFirstChild(root, _resolutionStack[i + 1])) continue;
            if (_resolutionStack[i + 1] is DyingFrame original && i + 2 < _resolutionStack.Count &&
                ((IsOriginalDyingSuspendedByDyingSuits(original) || IsOriginalDyingSuspendedByRecipientCategoryMark(original)) ||
                 IsOriginalDyingSuspendedByOwnedDeathBenefit(original) && OwnedDeathBenefitObserverRoot() is not null ||
                 IsPaidHandRepaymentProgramAlcoholRide(i + 1, original) || IsPaidHandRepaymentRescueRide(i + 1, original) ||
                 PolicyCounterspellVirtualAlcoholRide(i + 1, original) || (TieredRoundZeroDyingRescueRide(i + 1, original) || DrawFundedDistinctBasicDyingRescueRide(i + 1, original)))) return root;
            var exact = true;
            for (var child = i + 2; child < _resolutionStack.Count; child++)
            {
                if (!(HalfHandPaidDamageObserverEdge(child) || PaidTargetObserverEdge(child) ||
                      DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]))) { exact = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    (IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying) ||
                     (TieredRoundZeroDyingRescueRide(child, dying) || DrawFundedDistinctBasicDyingRescueRide(child, dying)))) break;
            }
            if (exact) return root;
        }
        return null;
    }
    private bool AllowsSourceCurseNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            _resolutionStack.LastOrDefault()?.Id != observer.Id || ActiveDying is not null ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or
                SkillProgramTriggerWindow.AfterHpLost) || SourceCurseObserverRoot() is not { } root ||
            root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount &&
            source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) :
                ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool IsSourceCurseProgramDying() => ActiveDying is { } dying && SourceCurseObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasSourceCurseDamageObserver(long windowId) =>
        _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) && SourceCurseObserverRoot() is not null;
}
