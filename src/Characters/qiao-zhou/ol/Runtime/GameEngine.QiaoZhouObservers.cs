namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26220)]
    private void AssertQiaoZhouForecast(ProgramSkillFrame f)
    {
        if (f.QiaoZhouForecast is not { } r) return;
        if (f.SkillId != "ol:xingbu" || GetProgramBindingId(f) != "forecast" || r.Source != QiaoZhouSource(f) ||
            r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context || r.EndingWindowId != context.ParentFrameId ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == r.EndingWindowId) is not { } ending ||
            ending.OwnerSeat != f.OwnerSeat || ending.TurnNumber != _turnNumber || ending.ItemIndex >= ending.Items.Count ||
            ending.Items[ending.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate) ||
            !Enum.IsDefined(r.Stage) || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            r.Cards.Count > 3 || r.Cards.Select(c => c.CardId).Distinct().Count() != r.Cards.Count ||
            r.RedCount != r.Cards.Count(c => IsRedSuit(c.Suit)) || r.Cards.Any(c => c.MovementSequence <= 0 || c.MovementSequence > _movementSequence ||
                _cardMovements.Count(m => m.Sequence == c.MovementSequence && m.CardId == c.CardId && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Processing && m.Reason.Value == QiaoZhouRevealReason) != 1) ||
            ProgramEventHistory<ProgramCardsRevealedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.Bind == QiaoZhouTableBind &&
                e.Cards.Select(c => c.Id).SequenceEqual(r.Cards.Select(c => c.CardId)) && e.Cards.Select(c => c.Suit).SequenceEqual(r.Cards.Select(c => c.Suit))) != 1)
            throw new InvalidOperationException("Xingbu lost its frozen native reveal, colors, ending candidate or movement provenance.");
    }
    private bool QiaoZhouFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.QiaoZhouForecast is not { } r) return false;
        AssertQiaoZhouForecast(root);
        return child is CardsMovedTriggerWindowFrame moved && moved.ResumeProgramFrameId == root.Id &&
            moved.Batch.ParentFrameId == root.Id && moved.Batch.OriginOwnerSeat == root.OwnerSeat &&
            moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                (r.Stage == QiaoZhouForecastStage.RevealChildren && (m.Reason.Value == QiaoZhouRevealReason || m.Reason == CardMoveReasons.Reshuffle &&
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile) || r.Stage == QiaoZhouForecastStage.CleanupChildren &&
                    m.Reason.Value == QiaoZhouCleanupReason && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile));
    }
    private ResolutionFrame? QiaoZhouObserverRoot()
    {
        var root = FindProgramObserverRoot(static (e, f, c) => e.QiaoZhouFirstChild(f, c), static (e, i) => e.HasMovementObserverSuffix(i));
        if (root is not null) return root;
        if (_resolutionStack.FirstOrDefault() is not QiaoZhouPreparationFrame prep || _resolutionStack.Count < 2 ||
            _resolutionStack[1] is not CardsMovedTriggerWindowFrame moved || prep.ActiveChildFrameId != moved.Id || !IsQiaoZhouPreparationMovement(prep, moved)) return null;
        AssertQiaoZhouPreparation(prep); return HasMovementObserverSuffix(0) ? prep : null;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 380)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 490)]
    private bool HasQiaoZhouDamageObserver(long id) => QiaoZhouObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame or DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 350)]
    private bool IsQiaoZhouDying() => ActiveDying is { } dying && QiaoZhouObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 990)]
    private bool AllowsQiaoZhouNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || QiaoZhouObserverRoot() is not { } root || root.Id == f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26220)]
    private bool AdvanceQiaoZhouSubtree() => _pendingDecision is null && QiaoZhouObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
