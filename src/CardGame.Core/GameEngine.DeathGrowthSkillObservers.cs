namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidDeathGrowthSkill(ProgramSkillFrame f)
    {
        if (f.DeathGrowthSkill is not { } r || r.InstructionIndex != f.InstructionIndex || f.TriggerId is null ||
            r.Source != DeathGrowthSource(f) || r.GameplayHash != f.GameplayHash || f.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied } context ||
            context.ParentFrameId != r.WindowFrameId || context.TargetSeat != r.VictimSeat || f.OwnerSeat == r.VictimSeat ||
            GetPausedPrivateOfferEffect(f) is not { Op: SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill } effect ||
            effect.Amount != r.RequestedDraw || !effect.SkillIds.SequenceEqual(r.SkillIds) ||
            _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowFrameId) is not { } window ||
            window.DeathFrameId != r.DeathFrameId || window.VictimSeat != r.VictimSeat || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) || window.Contexts[window.CandidateIndex] != context ||
            _resolutionStack.OfType<DeathFrame>().SingleOrDefault(d => d.Id == r.DeathFrameId) is not { } death ||
            death.VictimSeat != r.VictimSeat || death.KillerProgramsResolved || _players[r.VictimSeat].IsAlive ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence || r.DrawCount < 0 || r.DrawCount > r.RequestedDraw)
            return false;
        var facts = CompleteProgramEventHistory().OfType<DeathGrowthDrawIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (facts is not [var issued] || issued.Receipt.InstructionIndex != r.InstructionIndex || issued.Receipt.WindowFrameId != r.WindowFrameId ||
            issued.Receipt.DeathFrameId != r.DeathFrameId || issued.Receipt.VictimSeat != r.VictimSeat || issued.Receipt.Source != r.Source ||
            issued.Receipt.GameplayHash != r.GameplayHash || !issued.Receipt.SkillIds.SequenceEqual(r.SkillIds) ||
            issued.Receipt.RequestedDraw != r.RequestedDraw || issued.Receipt.MovementBefore != r.MovementBefore ||
            issued.Receipt.MovementAfter != r.MovementAfter || issued.Receipt.DrawCount != r.DrawCount ||
            issued.Receipt.OriginalDeathHash != r.OriginalDeathHash || issued.Receipt.OriginalDyingFrameId != r.OriginalDyingFrameId ||
            issued.Receipt.OriginalDyingHash != r.OriginalDyingHash || issued.Receipt.OriginalDamageHash != r.OriginalDamageHash ||
            !ExactDeathGrowthOriginalCursor(f, death) ||
            CompleteProgramEventHistory().OfType<DeathGrowthSkillGrantedEvent>().Any(e => e.ProgramFrameId == f.Id)) return false;
        var draws = _cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == DeathGrowthDrawReason).ToArray();
        if (draws.Length != r.DrawCount) return false;
        return r.Stage switch {
            DeathGrowthSkillStage.DrawChildren => f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == f.OwnerSeat,
            DeathGrowthSkillStage.ChooseSkill => f.PendingMovementContinuation is null,
            _ => false };
    }
    private void RequireDeathGrowthSkill(ProgramSkillFrame f)
    { if (!ValidDeathGrowthSkill(f)) throw new InvalidOperationException("Death growth lost its exact deceased victim, candidate, source, draw invoice or instruction."); }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 90)]
    private void AssertRegisteredDeathGrowthSkill(ProgramSkillFrame frame)
    {
        if (frame.DeathGrowthSkill is not null) RequireDeathGrowthSkill(frame);
    }
    private static string DeathGrowthCursorHash(ResolutionFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DeathGrowthSkillObservers.DeathGrowthCursorHash(frame);
    // Structural only: ActiveDying must never be consulted while proving its suspended cursor.
    private bool ExactDeathGrowthOriginalCursor(ProgramSkillFrame f, DeathFrame death)
    {
        var r = f.DeathGrowthSkill!; var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != r.WindowFrameId || _resolutionStack[index - 2].Id != death.Id ||
            DeathGrowthCursorHash(death) != r.OriginalDeathHash) return false;
        if (death.ReturnKind == DeathReturnKind.Dying)
            return index >= 3 && _resolutionStack[index - 3] is DyingFrame dying && dying.Id == death.ParentFrameId &&
                dying.Id == r.OriginalDyingFrameId && dying.VictimSeat == death.VictimSeat &&
                dying.ResponderIndex == dying.ResponderSeats.Count && DeathGrowthCursorHash(dying) == r.OriginalDyingHash &&
                ExactDeathGrowthDyingParent(index - 3, dying, r) &&
                CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id &&
                    e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) == 1;
        return death.ReturnKind == DeathReturnKind.ProgramSkill && r.OriginalDyingFrameId is null && r.OriginalDyingHash is null && r.OriginalDamageHash is null &&
            index >= 3 && _resolutionStack[index - 3] is ProgramSkillFrame parent && parent.Id == death.ParentFrameId;
    }
    private bool ExactDeathGrowthDyingParent(int index, DyingFrame dying, DeathGrowthSkillReceipt receipt)
    {
        if (index < 1 || _resolutionStack[index - 1].Id != dying.ParentFrameId) return false;
        var parent = _resolutionStack[index - 1];
        return dying.Continuation switch
        {
            DyingContinuationKind.ProgramSkill => parent is ProgramSkillFrame && receipt.OriginalDamageHash is null && dying.KillerSeat is null,
            DyingContinuationKind.Damage => parent is DamageFrame damage && damage.TargetSeat == dying.VictimSeat &&
                DeathGrowthCursorHash(damage) == receipt.OriginalDamageHash,
            DyingContinuationKind.AttackHpLoss => parent is ProgramSkillFrame { AttackAttempt: { } attack, AttackReturn: not null } &&
                attack.TargetSeat == dying.VictimSeat && dying.KillerSeat is null && receipt.OriginalDamageHash is null,
            _ => false
        };
    }
    private bool IsOriginalDyingSuspendedByDeathGrowth(DyingFrame dying) =>
        IsValidPlayerSeat(dying.VictimSeat) && !_players[dying.VictimSeat].IsAlive &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DeathGrowthSkill?.OriginalDyingFrameId == dying.Id && ValidDeathGrowthSkill(f));
    private bool DeathGrowthFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.DeathGrowthSkill is not { Stage: DeathGrowthSkillStage.DrawChildren } r || !ValidDeathGrowthSkill(root) || child is not CardsMovedTriggerWindowFrame moved)
            return false;
        return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == DeathGrowthDrawReason);
    }
    private ProgramSkillFrame? DeathGrowthObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.DeathGrowthFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1570)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1690)]
    private bool HasDeathGrowthDamageObserver(long id) => DeathGrowthObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 160)]
    private bool IsDeathGrowthDying() => ActiveDying is { } dying && DeathGrowthObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 710)]
    private bool AllowsDeathGrowthNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            DeathGrowthObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = GetPausedPrivateOfferEffect(observer);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 440)]
    private bool TryAdvanceDeathGrowthSubtree() => _pendingDecision is null && DeathGrowthObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
