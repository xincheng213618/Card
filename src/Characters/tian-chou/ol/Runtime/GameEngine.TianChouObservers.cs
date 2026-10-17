namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsTianChouRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.TianChouShandao is { Stage: TianChouShandaoStage.TopChildren, Tops.Count: > 0 } r && ValidTianChou(f) &&
        r.Tops[^1] is { Kind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } top && recovery.ParentFrameId == f.Id &&
        recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
        recovery.Attempt.Completion.MoveReason?.Value == TianChouTopReason && recovery.Attempt.SourceSeat == top.OwnerSeat && recovery.Attempt.TargetSeat == top.OwnerSeat && recovery.Attempt.Amount == 1;
    private bool TianChouFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.TianChouShandao is not { } r || !ValidTianChou(f)) return false;
        if (child is CardUseFrame use) return r.Stage == TianChouShandaoStage.NativeUse && use.TianChouShandao is { } origin && MatchesTianChouShandao(f, origin) &&
            use.Id == origin.CardUseFrameId && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && use.CardKind == origin.CardKind && ShownEntityUseActorMatches(use, origin.ActorSeat, origin.ActorSeat);
        if (child is RecoveryReplacementFrame recovery) return IsTianChouRecoveryParent(f, recovery);
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.Stage == TianChouShandaoStage.TopChildren && r.Tops.Count > 0 && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => IsTianChouTopMovement(r.Tops[^1], m));
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == TianChouShandaoStage.TopChildren && r.Tops.Count > 0 && r.Tops[^1] is { Kind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } top &&
            hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 &&
            hp.Change.SourceSeat == top.OwnerSeat && hp.Change.TargetSeat == top.OwnerSeat;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? TianChouObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.TianChouFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (x, n) => x.IsMovementObserverEdge(n) || x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
            static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasTianChouDamageObserver(long id) => TianChouObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasTianChouBeforeDamageObserver(long id) => HasTianChouDamageObserver(id);
    private bool IsTianChouDying() => ActiveDying is { } dying && TianChouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsTianChouNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || TianChouObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f); return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27090)] private bool AdvanceTianChouSubtree() => _pendingDecision is null && TianChouObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
