namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SlashBenefitDiscardLedger(int cardId, CardLocation from, long before, long after, string reason)
    {
        if (cardId <= 0 || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            from.OwnerSeat is not { } owner || !IsValidPlayerSeat(owner) || before < 0 || after <= before) return false;
        var records = _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after).ToArray();
        return records is [var moved] && moved.CardId == cardId && moved.From == from && moved.Reason.Value == reason &&
            moved.TurnNumber == _turnNumber && (moved.To == CardLocation.DiscardPile ||
                from.Zone == CardZoneKind.Equipment && moved.To == CardLocation.OutsideGame && GetAttackCard(cardId).IsGeneralWeapon);
    }
    private bool ValidSlashTargetBenefitReceipt(SlashTargetBenefitReceipt r)
    {
        if (r.OfferWindowId <= 0 || r.OfferCandidateIndex < 0 || r.Use.CardUseFrameId <= 0 ||
            !IsValidPlayerSeat(r.Use.ActorSeat) || !IsValidPlayerSeat(r.Use.ProviderSeat) || !IsValidPlayerSeat(r.Use.TargetSeat) ||
            r.Source.OwnerSeat != r.Use.ActorSeat || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } p || p.GameplayHash != r.GameplayHash ||
            ProgramInstructionResolver.Default.FindTrigger(p, r.Source.BindingId) is not
                { Window: SkillProgramTriggerWindow.ActualSlashTargetBenefit, Effects: [{ Op: SkillProgramEffectOp.OfferSlashTargetBenefit } offer] } ||
            offer.StateId != r.SettlementBinding || ProgramInstructionResolver.Default.FindTrigger(p, r.SettlementBinding) is not
                { Window: SkillProgramTriggerWindow.SlashDodgeCancelledBenefit, Effects: [{ Op: SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit }] } ||
            !MatchesSlashTargetBenefitUse(r.Use)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<SlashTargetBenefitOfferedEvent>().Count(e => e.CardUseFrameId == r.Use.CardUseFrameId &&
            e.WindowId == r.OfferWindowId && e.CandidateIndex == r.OfferCandidateIndex && e.ActorSeat == r.Use.ActorSeat &&
            e.TargetSeat == r.Use.TargetSeat && e.Source == r.Source && e.GameplayHash == r.GameplayHash) != 1) return false;
        if (r.Stage is SlashTargetBenefitStage.Offered or SlashTargetBenefitStage.Declined)
            return r.ProducerProgramId == 0 && !r.DrawBenefit && r.PaidCardId is null && r.PaidFrom is null &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.ActualDrawCount == 0;
        if (r.ProducerProgramId <= 0 || history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == r.ProducerProgramId &&
            e.SkillId == r.Source.SkillId && e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId &&
            e.OwnerSeat == r.Source.OwnerSeat && e.Window == SkillProgramTriggerWindow.ActualSlashTargetBenefit) != 1 ||
            history.OfType<SlashTargetBenefitPaidEvent>().Count(e => e.CardUseFrameId == r.Use.CardUseFrameId && e.ProgramFrameId == r.ProducerProgramId &&
            e.WindowId == r.OfferWindowId && e.CandidateIndex == r.OfferCandidateIndex && e.ActorSeat == r.Use.ActorSeat && e.TargetSeat == r.Use.TargetSeat &&
            e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.DrawBenefit == r.DrawBenefit && e.SequenceBefore == r.SequenceBefore &&
            e.SequenceAfter == r.SequenceAfter && e.ActualCount == (r.DrawBenefit ? r.ActualDrawCount : 1)) != 1) return false;
        if (r.DrawBenefit)
        {
            if (r.PaidCardId is not null || r.PaidFrom is not null || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore ||
                r.ActualDrawCount is < 0 or > 1 || _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.Source.OwnerSeat) && m.Reason.Value == SlashBenefitDrawReason) != r.ActualDrawCount)
                return false;
        }
        else if (r.ActualDrawCount != 0 || r.PaidCardId is not { } card || r.PaidFrom is not { } from || from.OwnerSeat != r.Use.TargetSeat ||
            !SlashBenefitDiscardLedger(card, from, r.SequenceBefore, r.SequenceAfter, SlashBenefitDiscardReason)) return false;
        var cancellations = history.OfType<SlashTargetBenefitCancellationEvent>().Where(e => e.CardUseFrameId == r.Use.CardUseFrameId &&
            e.WindowId == r.OfferWindowId && e.OfferCandidateIndex == r.OfferCandidateIndex && e.ActorSeat == r.Use.ActorSeat && e.TargetSeat == r.Use.TargetSeat).ToArray();
        if (r.Stage == SlashTargetBenefitStage.Paid) return cancellations.Length == 0;
        if (r.Stage is not (SlashTargetBenefitStage.CancellationQualified or SlashTargetBenefitStage.Settled or SlashTargetBenefitStage.Cancelled) || cancellations.Length != 1) return false;
        var settlements = history.OfType<SlashTargetBenefitSettledEvent>().Where(e => e.CardUseFrameId == r.Use.CardUseFrameId &&
            e.OfferWindowId == r.OfferWindowId && e.OfferCandidateIndex == r.OfferCandidateIndex).ToArray();
        if (r.Stage == SlashTargetBenefitStage.CancellationQualified) return settlements.Length == 0;
        if (r.Stage == SlashTargetBenefitStage.Cancelled && settlements.Length == 0) return true;
        if (settlements is not [var settled] || settled.ActorSeat != r.Use.ActorSeat || settled.TargetSeat != r.Use.TargetSeat ||
            settled.Paid != (r.Stage == SlashTargetBenefitStage.Settled) || history.OfType<ProgramBindingStartedEvent>().Count(e =>
                e.FrameId == settled.ProgramFrameId && e.SkillId == r.Source.SkillId && e.BindingId == r.SettlementBinding &&
                e.SkillInstanceId == r.Source.SkillInstanceId && e.OwnerSeat == r.Source.OwnerSeat && e.Window == SkillProgramTriggerWindow.SlashDodgeCancelledBenefit) != 1) return false;
        if (!settled.Paid) return settled.SequenceBefore == 0 && settled.SequenceAfter == 0;
        var costs = _cardMovements.Where(m => m.Sequence > settled.SequenceBefore && m.Sequence <= settled.SequenceAfter).ToArray();
        return costs is [var cost] && cost.From.OwnerSeat == r.Source.OwnerSeat &&
            SlashBenefitDiscardLedger(cost.CardId, cost.From, settled.SequenceBefore, settled.SequenceAfter, SlashBenefitSettlementReason);
    }
    private bool ValidSlashBenefitDraftPayment(ProgramSkillFrame f)
    {
        if (!SlashBenefitProgramParentMatches(f) || f.SlashTargetBenefitDraft is not { } d ||
            SlashTargetBenefitCurrent(d.Receipt) is not { } paid || !ValidSlashTargetBenefitReceipt(paid)) return false;
        if (!d.Settlement) return paid.ProducerProgramId == f.Id && d.Receipt == paid &&
            d.SequenceBefore == paid.SequenceBefore && d.SequenceAfter == paid.SequenceAfter &&
            (paid.DrawBenefit ? d.PaidCardId is null && d.PaidFrom is null : d.PaidCardId == paid.PaidCardId && d.PaidFrom == paid.PaidFrom);
        return paid.Stage is SlashTargetBenefitStage.CancellationQualified or SlashTargetBenefitStage.Settled &&
            d.PaidCardId is { } card && d.PaidFrom is { } from && from.OwnerSeat == f.OwnerSeat &&
            SlashBenefitDiscardLedger(card, from, d.SequenceBefore, d.SequenceAfter, SlashBenefitSettlementReason);
    }
    private ProgramSkillFrame? SlashBenefitPaidObserverRoot(long? exactDamageWindow = null)
    {
        for (var rootIndex = 1; rootIndex < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramSkillFrame root || root.SlashTargetBenefitDraft is not
                { Stage: SlashTargetBenefitDraftStage.PaidChildren } d || root.PendingMovementContinuation is not
                { BeforeCount: 0, CoverageResultBind: null } pending || !ValidSlashBenefitDraftPayment(root)) continue;
            var payer = d.Settlement ? root.OwnerSeat : d.Receipt.DrawBenefit ? root.OwnerSeat : d.Receipt.Use.TargetSeat;
            var reason = d.Settlement ? SlashBenefitSettlementReason : d.Receipt.DrawBenefit ? SlashBenefitDrawReason : SlashBenefitDiscardReason;
            if (pending.SubjectSeat != payer || d.SequenceAfter <= d.SequenceBefore || exactDamageWindow is { } id &&
                !_resolutionStack.Skip(rootIndex + 1).Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            if (rootIndex == _resolutionStack.Count - 1) return root;
            var first = _resolutionStack[rootIndex + 1];
            if (first is CardsMovedTriggerWindowFrame movement)
            {
                if (movement.Batch.ParentFrameId != root.Id || movement.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    movement.Batch.OriginSkillId != root.SkillId || movement.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                    movement.Batch.OriginOwnerSeat != root.OwnerSeat || movement.Batch.Movements.Count == 0 ||
                    movement.Batch.Movements.Any(m => m.Sequence <= d.SequenceBefore || m.Sequence > d.SequenceAfter || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                if (d.PaidCardId is not { } silver || !_cardMovements.Any(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter &&
                    m.CardId == silver && m.From == CardLocation.Equipment(payer) && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.SilverLion &&
                    m.Reason.Value == reason)) continue;
                if (first is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != payer || hp.Change.TargetSeat != payer || hp.Change.Amount != 1) continue;
                }
                else if (first is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != payer || recovery.Attempt.TargetSeat != payer || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var i = rootIndex + 1; i < _resolutionStack.Count; i++)
            {
                if (!PaidColorDamageClaimObserverEdge(i)) { valid = false; break; }
                if (_resolutionStack[i] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(i, dying) ||
                    IsPaidHandRepaymentProgramAlcoholRide(i, dying) || PolicyCounterspellVirtualAlcoholRide(i, dying) ||
                    PaidObserverDamageVirtualAlcoholRide(i, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasSlashBenefitDamageObserver(long id) => SlashBenefitPaidObserverRoot(id) is not null;
    private bool AllowsSlashTargetBenefitNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            _resolutionStack.LastOrDefault()?.Id != observer.Id || observer.WindowContext?.Window is not
                (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                 SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) ||
            SlashBenefitPaidObserverRoot() is not { SlashTargetBenefitDraft: { } paid } root ||
            ActiveCardAttack?.ResolutionId != paid.Receipt.Use.CardUseFrameId ||
            CurrentDamageAttempt?.ResolutionId != paid.Receipt.Use.CardUseFrameId ||
            root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool IsSlashTargetBenefitProgramDying() => ActiveDying is { } dying && SlashBenefitPaidObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsSlashTargetBenefitMovement(ProgramSkillFrame root, SkillProgramEffect? op, ProgramMovementContinuation pending) =>
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && root.SlashTargetBenefitDraft is { Stage: SlashTargetBenefitDraftStage.PaidChildren } d &&
        pending.SubjectSeat == (d.Settlement ? root.OwnerSeat : d.Receipt.DrawBenefit ? root.OwnerSeat : d.Receipt.Use.TargetSeat) &&
        op?.Op == (d.Settlement ? SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit : SkillProgramEffectOp.OfferSlashTargetBenefit) && ValidSlashBenefitDraftPayment(root);
    private bool HasSlashTargetBenefitUseObserver(long useId) => _resolutionStack.OfType<SlashTargetBenefitWindowFrame>().Any(w =>
        w.ParentFrameId == useId && SlashBenefitWindowMatches(w) && (_resolutionStack.LastOrDefault()?.Id == w.Id ||
            _resolutionStack.LastOrDefault() is ProgramSkillFrame f && f.WindowContext?.ParentFrameId == w.Id && SlashBenefitProgramParentMatches(f) ||
            SlashBenefitPaidObserverRoot()?.WindowContext?.ParentFrameId == w.Id));
    private bool SlashBenefitWindowMatches(SlashTargetBenefitWindowFrame w)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == w.Id);
        if (index < 1 || _resolutionStack[index - 1] is not CardUseFrame owner || owner.Id != w.ParentFrameId ||
            !Enum.IsDefined(w.ReturnKind) || w.Id <= 0 || w.Candidates.Count == 0 || w.Candidates.Count != w.Contexts.Count || w.CandidateIndex < 0 || w.CandidateIndex > w.Candidates.Count ||
            LifecycleCardUse(w.ParentFrameId) is not { } use || !IsSlashCard(use.CardKind)) return false;
        for (var i = 0; i < w.Candidates.Count; i++)
        {
            var c = w.Candidates[i]; var context = w.Contexts[i]; var r = context.SlashTargetBenefit;
            if (r is null || context.ParentFrameId != w.Id || context.OwnerSeat != c.OwnerSeat || context.SourceSeat != r.Use.ActorSeat ||
                context.TargetSeat != r.Use.TargetSeat || context.OccurrenceIndex != c.OccurrenceIndex || w.ParentFrameId != r.Use.CardUseFrameId ||
                r.Source.OwnerSeat != c.OwnerSeat || r.Source.SkillId != c.SkillId || r.Source.SkillInstanceId != c.SkillInstanceId || r.GameplayHash != c.GameplayHash ||
                (w.ReturnKind == SlashTargetBenefitReturn.DodgeCancelled ? context.Window != SkillProgramTriggerWindow.SlashDodgeCancelledBenefit ||
                    c.BindingId != r.SettlementBinding : context.Window != SkillProgramTriggerWindow.ActualSlashTargetBenefit || c.BindingId != r.Source.BindingId) ||
                (w.ReturnKind is SlashTargetBenefitReturn.LegacyVirtualSlash or SlashTargetBenefitReturn.AfterActualTargetsLegacy ? r.Use.ActionId is not null :
                    w.ReturnKind is SlashTargetBenefitReturn.FinalizedSlash or SlashTargetBenefitReturn.AfterActualTargetsSlash && r.Use.ActionId is null) ||
                SlashTargetBenefitCurrent(r) is not { } current || !ValidSlashTargetBenefitReceipt(current) ||
                (w.ReturnKind == SlashTargetBenefitReturn.DodgeCancelled
                    ? r != (current with { Stage = SlashTargetBenefitStage.CancellationQualified })
                    : r != (current with { Stage = SlashTargetBenefitStage.Offered, ProducerProgramId = 0, DrawBenefit = false,
                        PaidCardId = null, PaidFrom = null, SequenceBefore = 0, SequenceAfter = 0, ActualDrawCount = 0 }))) return false;
        }
        return true;
    }
    private void AssertSlashTargetBenefitProgram(ProgramSkillFrame f)
    {
        if (!IsSlashTargetBenefitWindow(f.WindowContext?.Window ?? default)) return;
        if (!SlashBenefitProgramParentMatches(f) || f.SlashTargetBenefitDraft is { } d &&
            (d.Settlement != (f.WindowContext!.Window == SkillProgramTriggerWindow.SlashDodgeCancelledBenefit) ||
             d.Stage == SlashTargetBenefitDraftStage.PaidChildren && (!ValidSlashBenefitDraftPayment(f) ||
                 f.PendingMovementContinuation is not null && SlashBenefitPaidObserverRoot()?.Id != f.Id)))
            throw new InvalidOperationException("A Slash target benefit lost its exact owning candidate/payment subtree.");
    }
    private void AssertSlashTargetBenefitWindows()
    {
        foreach (var w in _resolutionStack.OfType<SlashTargetBenefitWindowFrame>())
            if (!SlashBenefitWindowMatches(w)) throw new InvalidOperationException("A Slash benefit window lost its original use/candidate identity.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.SlashTargetBenefits is not { } receipts) continue;
            if (receipts.Count == 0 || receipts.Select(r => (r.OfferWindowId, r.OfferCandidateIndex)).Distinct().Count() != receipts.Count ||
                receipts.Select(r => (r.Source.OwnerSeat, r.Source.SkillId, r.Source.BindingId, r.Use.TargetSeat)).Distinct().Count() != receipts.Count ||
                receipts.Any(r => r.Use.CardUseFrameId != use.Id || !ValidSlashTargetBenefitReceipt(r)))
                throw new InvalidOperationException("A Slash benefit receipt cannot be duplicated or detached from its actual use.");
        }
    }
}
