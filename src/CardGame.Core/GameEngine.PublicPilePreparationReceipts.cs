namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidPublicPilePreparation(ProgramSkillFrame f)
    {
        if (f.PublicPilePreparation is not { } r || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            r.Issuer != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            f.WindowContext?.ParentFrameId != r.ParentId || !ExactPreparationParent(f, r.Operation) ||
            r.SelectedIds.Distinct().Count() != r.SelectedIds.Count || r.PaidIds.Distinct().Count() != r.PaidIds.Count ||
            r.PaidIds.Count != r.PaidFrom.Count || r.FrozenCount != r.PaidIds.Count || r.Before < 0 || r.After < r.Before ||
            r.Pile is { } pile && (pile.OwnerSeat != f.OwnerSeat || pile.Capacity != int.MaxValue ||
                (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? pile.SkillId != f.SkillId || pile.SkillInstanceId != f.SkillInstanceId :
                    pile.SkillId != GetProgramTrigger(f).Effects.Single().SkillIds.Single()))) return false;
        var facts = CompleteProgramEventHistory().ToArray();
        if (facts.OfType<PublicPilePreparationStartedEvent>().Count(e => e == new PublicPilePreparationStartedEvent(f.Id,
            r.Operation, r.Issuer, r.GameplayHash, r.ActualTurn, r.ParentId, r.Pile)) != 1) return false;
        if (r.Stage == PublicPilePreparationStage.Choosing)
        {
            if (r.PaidIds.Count != 0 || r.PaidFrom.Count != 0 || r.Branch != 0 || r.BeneficiarySeat != -1 || r.RecoveryIssued ||
                r.DrawRequested != 0 || r.DrawActual != 0 || r.Operation != SkillProgramEffectOp.StoreNonBasicOwnedPublicPile && r.SelectedIds.Count != 0 ||
                f.PendingMovementContinuation is not null) return false;
            if (PreparationGameEnded()) return true;
            if (r.SelectedIds.Any(id => !PreparationStoreCards(f).Any(c => c.Id == id))) return false;
            if (_resolutionStack.LastOrDefault()?.Id != f.Id) return true;
            return _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p && p.PlayerSeat == f.OwnerSeat &&
                p.TargetSeat == f.OwnerSeat && p.SkillPrompt?.SkillId == f.SkillId &&
                p.IsPrivate == (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile) &&
                AssistedChoicesEqual(p.Choices, PublicPilePreparationChoices(f));
        }
        if (!IsValidPlayerSeat(r.BeneficiarySeat) || r.SelectedIds.Count != 0 ||
            (r.Operation == SkillProgramEffectOp.ResolvePreparationPublicPile ? r.Branch is not (1 or 2) ||
                (r.Branch == 1 ? r.BeneficiarySeat != f.OwnerSeat : r.BeneficiarySeat == f.OwnerSeat) :
                r.Branch != 0 || r.BeneficiarySeat != f.OwnerSeat)) return false;
        var paid = facts.OfType<PublicPilePreparationPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (paid is not [var issued] || issued.Branch != r.Branch || issued.BeneficiarySeat != r.BeneficiarySeat || issued.Pile != r.Pile ||
            issued.Before != r.Before || issued.After != r.After || !issued.CardIds.SequenceEqual(r.PaidIds) || !issued.From.SequenceEqual(r.PaidFrom)) return false;
        var to = r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? r.Pile?.Location :
            r.Branch == 2 ? CardLocation.Hand(r.BeneficiarySeat) : CardLocation.DiscardPile;
        if (to is null || r.PaidIds.Count == 0 && r.After != r.Before || r.Operation == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage && r.PaidIds.Count != 1) return false;
        foreach (var pair in r.PaidIds.Select((id, i) => (id, from: r.PaidFrom[i])))
            if ((r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? pair.from.OwnerSeat != f.OwnerSeat || pair.from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) :
                    r.Pile is null || pair.from != r.Pile.Location) ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == pair.id && m.From == pair.from && m.To == to &&
                    m.Reason.Value == PreparationReason(f, "payment")) != 1) return false;
        if (r.Stage != PublicPilePreparationStage.PaymentChildren && r.Operation != SkillProgramEffectOp.ResolvePreparationPublicPile) return false;
        if (r.RecoveryIssued != (r.Branch == 2 && r.Stage is PublicPilePreparationStage.RecoveryChildren or PublicPilePreparationStage.DrawChildren) ||
            facts.OfType<PublicPilePreparationRecoveryIssuedEvent>().Count(e => e == new PublicPilePreparationRecoveryIssuedEvent(f.Id, r.BeneficiarySeat, 1)) !=
                (r.RecoveryIssued ? 1 : 0)) return false;
        if (r.Stage == PublicPilePreparationStage.DrawChildren)
        {
            if (r.DrawRequested < 0 || r.DrawActual < 0 || r.DrawActual > r.DrawRequested || r.DrawBefore < r.After || r.DrawAfter < r.DrawBefore ||
                r.Branch == 2 && r.DrawRequested != r.FrozenCount ||
                facts.OfType<PublicPilePreparationDrawIssuedEvent>().Count(e => e == new PublicPilePreparationDrawIssuedEvent(f.Id,
                    r.BeneficiarySeat, r.DrawRequested, r.DrawActual, r.DrawBefore, r.DrawAfter)) != 1 ||
                _cardMovements.Count(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(r.BeneficiarySeat) && m.Reason.Value == PreparationReason(f, "draw")) != r.DrawActual) return false;
        }
        else if (r.DrawRequested != 0 || r.DrawActual != 0 || r.DrawBefore != 0 || r.DrawAfter != 0) return false;
        if (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile && (r.Pile is null || r.PaidIds.Any(id =>
            _cardMovements.Any(m => m.CardId == id && m.Sequence > r.Before && m.Sequence <= r.After &&
                MatchesSkillProgramCardCategory(m.CardKind, SkillProgramCardCategory.Basic))))) return false;
        var awaiting = r.Stage is PublicPilePreparationStage.PaymentChildren or PublicPilePreparationStage.DrawChildren;
        return awaiting ? f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == f.OwnerSeat :
            f.PendingMovementContinuation is null;
    }
    private void AssertPublicPilePreparation(ProgramSkillFrame f)
    {
        if (f.PublicPilePreparation is not null && !ValidPublicPilePreparation(f))
            throw new InvalidOperationException("Public preparation lost its original window, exact paid invoice or once-issued successor.");
    }
    private bool IsPublicPilePreparationMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.PublicPilePreparation is { Stage: PublicPilePreparationStage.PaymentChildren or PublicPilePreparationStage.DrawChildren } r &&
        effect?.Op == r.Operation && pending == f.PendingMovementContinuation && ValidPublicPilePreparation(f);
    private bool PublicPilePreparationFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PublicPilePreparation is not { } r || !ValidPublicPilePreparation(f)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var awaited = f.PendingMovementContinuation is not null;
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            var before = r.Stage == PublicPilePreparationStage.DrawChildren ? r.DrawBefore : r.Before;
            var after = r.Stage == PublicPilePreparationStage.DrawChildren ? r.DrawAfter : r.After;
            return awaited && moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == f.Id && moved.Batch.AwaitingProgramFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after);
        }
        var lion = r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile && r.PaidIds.Select((id, i) => (id, from: r.PaidFrom[i])).Any(p =>
            p.from == CardLocation.Equipment(f.OwnerSeat) && _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == p.id &&
                m.CardKind == CardKind.SilverLion && m.From == p.from && m.To == r.Pile!.Location && m.Reason.Value == PreparationReason(f, "payment")));
        if (r.Stage != PublicPilePreparationStage.RecoveryChildren && !lion) return false;
        var target = lion ? f.OwnerSeat : r.BeneficiarySeat;
        var continuation = lion ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Continuation == continuation && hp.Change.ParentFrameId == f.Id &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == target && hp.Change.Amount > 0;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == continuation &&
            recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == target && recovery.Attempt.Amount == 1 &&
            (lion ? recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == PreparationReason(f, "payment") :
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == f.InstructionIndex);
    }
    private ProgramSkillFrame? PublicPilePreparationObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame f || !PublicPilePreparationFirstChild(f, _resolutionStack[i + 1])) continue;
            var exact = true;
            for (var n = i + 2; n < _resolutionStack.Count; n++)
            {
                if (_resolutionStack[n] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[n - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[n - 1], _resolutionStack[n]) && !HalfHandPaidDamageObserverEdge(n) && !PaidTargetObserverEdge(n))
                { exact = false; break; }
                if (_resolutionStack[n] is DyingFrame dying && n + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(n, dying) || IsPaidHandRepaymentRescueRide(n, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(n, dying) || PaidObserverDamageVirtualAlcoholRide(n, dying))) break;
            }
            if (exact) return f;
        }
        return null;
    }
    private bool IsPublicPilePreparationProgramDying() => ActiveDying is { } d && PublicPilePreparationObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPublicPilePreparationDamageObserver(long id) => _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && PublicPilePreparationObserverRoot() is not null;
    private bool AllowsPublicPilePreparationNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || f.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            PublicPilePreparationObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, effect.Target));
    }
}
