namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SpZhangLiaoFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.SpZhangLiao is not { } r) return false;
        AssertSpZhangLiao(root);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId == root.Id && moved.Batch.ParentFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.Reason.Value == SpZhangLiaoMovementReason(root));
        if (r.Stage == SpZhangLiaoStage.HighestChildren && r.VictimSeat is { } victim &&
            r.Selected is [var card] && _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == card &&
                m.From == CardLocation.Equipment(victim) && m.To == CardLocation.Hand(root.OwnerSeat) && m.CardKind == CardKind.SilverLion))
        {
            if (child is HpChangedTriggerWindowFrame hp)
                return hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id && hp.Continuation == PostEventContinuation.Program &&
                    hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == victim && hp.Change.TargetSeat == victim && hp.Change.Amount == 1;
            if (child is RecoveryReplacementFrame recovery)
                return recovery.ParentFrameId == root.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, root.Id) &&
                    recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == new CardMoveReason(SpZhangLiaoMovementReason(root)) &&
                    recovery.Attempt.SourceSeat == victim && recovery.Attempt.TargetSeat == victim && recovery.Attempt.Amount == 1;
        }
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }

    private string SpZhangLiaoMovementReason(ProgramSkillFrame f) => f.SpZhangLiao!.Stage switch {
        SpZhangLiaoStage.Payment => $"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.reveal",
        SpZhangLiaoStage.PaymentChildren => "skill-program.ol:mubing.DiscardSelected",
        SpZhangLiaoStage.ObtainChildren => "skill-program.ol:mubing.ObtainSelected",
        SpZhangLiaoStage.GiftChildren => "skill-program.ol:mubing.GiveSelected",
        SpZhangLiaoStage.HighestChildren => "skill-program.ol:ziqu.GiveSelected",
        SpZhangLiaoStage.CleanupChildren => "skill-program.ol:mubing.reveal-cleanup", _ => "" };
    private ProgramSkillFrame? SpZhangLiaoObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.SpZhangLiaoFirstChild(root, child),
        static (engine, child) => engine.HasMovementObserverSuffix(child));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 330)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 440)]
    private bool HasSpZhangLiaoDamageObserver(long id) => SpZhangLiaoObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        (id == root.WindowContext?.ParentFrameId || _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 300)]
    private bool IsSpZhangLiaoDying() => ActiveDying is { } dying && SpZhangLiaoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 940)]
    private bool AllowsSpZhangLiaoNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            SpZhangLiaoObserverRoot() is not { } root || root.Id == f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26070)]
    private bool TryAdvanceSpZhangLiaoSubtree() => _pendingDecision is null && SpZhangLiaoObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
