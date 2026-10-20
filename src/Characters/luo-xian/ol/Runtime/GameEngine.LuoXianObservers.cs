namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidDaili(ProgramSkillFrame frame)
    {
        if (frame.Daili is not { } pending) return false;
        var receipt = pending.Receipt;
        if (receipt.Source != DailiSource(frame) || receipt.GameplayHash != frame.GameplayHash || receipt.InstructionIndex != frame.InstructionIndex ||
            receipt.InstructionIndex != 1 || receipt.ActualTurn != _turnNumber || receipt.TurnOwnerSeat != _currentSeat || receipt.ShownHandCount < 0 || receipt.ShownHandCount % 2 != 0 ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } context || context.ParentFrameId != receipt.ParentFrameId ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ResolveDailiTurnEnd || !Enum.IsDefined(pending.Stage) ||
            CompleteProgramEventHistory().OfType<DailiTurnEndIssuedEvent>().Count(e => e.FrameId == frame.Id && e.Receipt == receipt) != 1 ||
            CompleteProgramEventHistory().OfType<DailiTurnedOverEvent>().Count(e => e.FrameId == frame.Id && e.WasFaceDown == receipt.WasFaceDown && e.IsFaceDown != receipt.WasFaceDown) != 1)
            return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 1 || _resolutionStack[index - 1] is not DeferredTurnEndFrame parent || parent.Id != receipt.ParentFrameId ||
            parent.TurnNumber != receipt.ActualTurn || parent.OwnerSeat != receipt.TurnOwnerSeat || !MatchesAfterTurnEndedChild(parent, frame, context)) return false;
        if (pending.Stage == DailiStage.FlipChildren) return frame.PendingMovementContinuation is null && pending.Before == 0 && pending.After == 0 && pending.Count == 0;
        if (frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } movement || movement.SubjectSeat != frame.OwnerSeat ||
            pending.Before < 0 || pending.After < pending.Before || pending.After > _movementSequence || pending.Count is < 0 or > 3 ||
            CompleteProgramEventHistory().OfType<DailiDrawPaidEvent>().Count(e => e.FrameId == frame.Id && e.Before == pending.Before && e.After == pending.After && e.Count == pending.Count) != 1) return false;
        var drawn = CompleteProgramEventHistory().OfType<DailiCardDrawnEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        return drawn.Length == pending.Count && drawn.Select(e => e.MovementSequence).Distinct().Count() == drawn.Length && drawn.All(e => e.OwnerSeat == frame.OwnerSeat) &&
            DailiDrawnMovements(frame).All(m => m.Sequence > pending.Before && m.Sequence <= pending.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == "skill-program.daili.draw") &&
            _cardMovements.Count(m => m.Sequence > pending.Before && m.Sequence <= pending.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == "skill-program.daili.draw") == pending.Count;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26420)]
    private void RequireDaili(ProgramSkillFrame frame)
    {
        if (frame.Daili is not null && !ValidDaili(frame)) throw new InvalidOperationException("Daili lost its exact ended-turn candidate, paid flip, original drawn entities or native child return.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26420)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26420)]
    private bool ContinueIssuedDaili(ProgramSkillFrame frame) => ValidDaili(frame);
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 110)]
    private bool IsDailiMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        effect?.Op == SkillProgramEffectOp.ResolveDailiTurnEnd && frame.Daili is { Stage: DailiStage.DrawChildren } &&
        movement is { BeforeCount: 0, CoverageResultBind: null } && movement.SubjectSeat == frame.OwnerSeat && ValidDaili(frame);
    private bool DailiFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (!ValidDaili(frame) || frame.Daili is not { } pending) return false;
        if (pending.Stage == DailiStage.FlipChildren && child is ProgramLifecycleTriggerWindowFrame flip)
            return flip.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp &&
                flip.ResumeProgramFrameId == frame.Id && flip.CharacterStateContinuation == CharacterStateContinuation.Program &&
                flip.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && flip.OwnerSeat == frame.OwnerSeat &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == flip.Id && e.Change.ParentFrameId == frame.Id && e.Change.TargetSeat == frame.OwnerSeat);
        return pending.Stage == DailiStage.DrawChildren && child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(frame, moved) &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > pending.Before && m.Sequence <= pending.After &&
                m.Reason.Value == "skill-program.daili.draw");
    }
    private ProgramSkillFrame? DailiObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.DailiFirstChild(root, child),
        static (engine, index) => engine.HasMovementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 420)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 530)]
    private bool HasDailiDamageObserver(long id) => DailiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 560)]
    private bool HasDailiBeforeDamageObserver(long id) => DailiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 390)]
    private bool IsDailiDying() => ActiveDying is { } dying && DailiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26420)]
    private bool AdvanceDailiSubtree() => _pendingDecision is null && DailiObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1030)]
    private bool AllowsDailiNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || frame.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != frame.Id || DailiObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
}
