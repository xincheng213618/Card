namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidFirstSlashDuelBenefit(ProgramSkillFrame f)
    {
        if (f.FirstSlashDuelBenefit is not { } r || r.InstructionIndex != f.InstructionIndex || f.TriggerId is null ||
            r.Source != FirstSlashDuelSource(f) || r.GameplayHash != f.GameplayHash || r.FrozenDiamondCount < 0 ||
            r.TurnNumber != _turnNumber || r.TurnOwnerSeat != _currentSeat || f.OwnerSeat != _currentSeat ||
            f.WindowContext?.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || f.WindowContext.ParentFrameId != r.WindowFrameId ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit) return false;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowFrameId);
        var legacy = _resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>().SingleOrDefault(w => w.Id == r.WindowFrameId);
        var use = LifecycleCardUse(r.CardUseFrameId);
        var candidateMatches = window is not null && r.LegacyProducerFrameId is null && window.ParentFrameId == use?.Id &&
            window.Action.ActionId == r.ActionId && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
            MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            legacy is not null && use is not null && r.ActionId is null && r.LegacyProducerFrameId == legacy.ProducerFrameId &&
            legacy.ParentFrameId == use.Id && legacy.CandidateIndex >= 0 && legacy.CandidateIndex < legacy.Candidates.Count &&
            MountObserverCandidateMatches(f, legacy.Candidates[legacy.CandidateIndex]) && IsFirstLegacySlashUse(use, legacy.ProducerFrameId, requireSingle: r.SelectedBranch is null);
        if (!candidateMatches || use is null ||
            use.Action?.ActionId != r.ActionId || use.SourceSeat != f.OwnerSeat || !IsFirstSlashDuelKind(use.CardKind) ||
            !CompleteProgramEventHistory().OfType<FirstSlashDuelUseRecordedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                e.ActionId == r.ActionId && e.Ordinal == 1 && e.ActorSeat == f.OwnerSeat && e.TurnNumber == r.TurnNumber && e.TurnOwnerSeat == r.TurnOwnerSeat)) return false;
        var issued = CompleteProgramEventHistory().OfType<FirstSlashDuelBenefitIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.SelectedBranch is null) return issued.Length == 0 && use.TargetSeats.SequenceEqual([r.TargetSeat]);
        if (r.SelectedBranch != "draw" || issued is not [var fact] || fact.Receipt != r || r.DrawCount < 0 || r.DrawCount > r.FrozenDiamondCount ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat) return false;
        var draws = _cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == FirstSlashDuelDrawReason).ToArray();
        return draws.Length == r.DrawCount;
    }
    private void RequireFirstSlashDuelBenefit(ProgramSkillFrame f)
    { if (!ValidFirstSlashDuelBenefit(f)) throw new InvalidOperationException("The first Slash/Duel benefit lost its exact declared use, candidate, source or once-issued draw."); }
    private bool FirstSlashDuelFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!CanContinueFirstSlashDuelBenefit(root) || root.FirstSlashDuelBenefit is not { } r || child is not CardsMovedTriggerWindowFrame moved) return false;
        return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
            moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == FirstSlashDuelDrawReason);
    }
    private ProgramSkillFrame? FirstSlashDuelObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.FirstSlashDuelFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    private bool HasFirstSlashDuelDamageObserver(long id) => FirstSlashDuelObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsFirstSlashDuelDying() => ActiveDying is { } dying && FirstSlashDuelObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsFirstSlashDuelNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            FirstSlashDuelObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvanceFirstSlashDuelSubtree() => _pendingDecision is null && FirstSlashDuelObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
