namespace CardGame.Core;
public sealed partial class GameEngine
{
    // This proves the old rescue Dying below the new pre-recovery draw. It
    // deliberately differs from the normal observer proof for a new Dying
    // created by a draw child, which must lie above its own paid draw root.
    private bool IsZhuJunLegacyAlcoholPreRecoveryRide()
    {
        if (!HasZhuJunCapability || ActiveDying is not { } active) return false;
        var dyingIndex = _resolutionStack.FindIndex(f => f.Id == active.FrameId);
        if (dyingIndex < 0 || dyingIndex + 3 >= _resolutionStack.Count || _resolutionStack[dyingIndex] is not DyingFrame dying ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame producer || producer.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != dying.VictimSeat || context.OwnerSeat != producer.OwnerSeat ||
            context.SourceSeat != (GetDyingAttack(dying) is { IsSourceLess: false } originalAttack ? originalAttack.SourceSeat : (int?)null) || context.DamageFrameId != dying.DamageFrameId ||
            producer.OwnerSeat != dying.VictimSeat || producer.OwnerSeat != dying.ResponderSeat || producer.TriggerId is null || producer.ActivationId != producer.TriggerId ||
            _contentRegistry.GetSkill(producer.SkillId).Program is not { } definition || definition.GameplayHash != producer.GameplayHash ||
            GetPausedPrivateOfferEffect(producer)?.Op != SkillProgramEffectOp.UseVirtualDyingAlcohol ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: { } action, ZhuJunLegacyAlcohol: { Returned: false } r } use ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) || use.DyingResponse is not null || use.VirtualBasicReturn is not null ||
            r.UseId != use.Id || r.ActionId != action.ActionId || r.ProgramParentId != producer.Id || r.DyingId != dying.Id || r.InstructionIndex != producer.InstructionIndex || r.ProducerGameplayHash != producer.GameplayHash ||
            r.ProducerSource != new CardConversionSource(producer.SkillId, GetProgramBindingId(producer), producer.OwnerSeat, producer.SkillInstanceId) ||
            ProgramEventHistory<ZhuJunLegacyAlcoholWindowIssuedEvent>().Count(e => e.Return == r) != 1 || !IsExactLegacyActualUseCompletion(use) ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == producer.Id && e.OwnerSeat == producer.OwnerSeat && e.SkillId == producer.SkillId && e.SkillInstanceId == producer.SkillInstanceId && e.BindingId == producer.TriggerId && e.Window == context.Window) != 1 ||
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) != 1 ||
            _resolutionStack[dyingIndex + 3] is not ProgramCardTriggerWindowFrame { Continuation: ProgramCardContinuation.CommittedSimpleCard, SimpleContinuation: null, TrickContinuation: null, CompletedResponseReturn: null, AttackOwnerFrameId: null } window ||
            window.ParentFrameId != use.Id || window.Action.ActionId != action.ActionId || window.Action.ActorSeat != use.SourceSeat ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] is not { } candidate ||
            candidate.FrozenContext is not { } frozen || frozen.ParentFrameId != window.Id || frozen.CardUse?.ParentCardUseFrameId != use.Id ||
            ZhuJunQualification(ToSharedCandidate(candidate), frozen) is null) return false;
        if (dyingIndex + 4 == _resolutionStack.Count) return true;
        if (_resolutionStack[dyingIndex + 4] is not ProgramSkillFrame { ZhuJunCuipoDraw: not null } draw || draw.WindowContext != frozen || !MountObserverCandidateMatches(draw, ToSharedCandidate(candidate))) return false;
        AssertZhuJunCuipo(draw);
        return dyingIndex + 5 == _resolutionStack.Count || ZhuJunObserverRoot()?.Id == draw.Id;
    }
    private bool TryBeginZhuJunLegacyAlcoholUse(long useId, long programParentId, long dyingId)
    {
        if (!HasZhuJunCapability || LifecycleCardUse(useId) is not { Action: null, CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0 } use ||
            !ProgramEventHistory<ZhuJunCuipoQualifiedEvent>().Any(e => e.OriginFrameId == use.Id && e.ActionId is null && !e.Damage && e.EffectiveKind == CardKind.Alcohol && e.Source.OwnerSeat == use.SourceSeat && HasRuntimeSkillInstance(_players[use.SourceSeat], e.Source.SkillId, e.Source.SkillInstanceId))) return false;
        var at = _resolutionStack.FindIndex(f => f.Id == useId);
        if (at < 1 || at != _resolutionStack.Count - 1 || _resolutionStack[at - 1] is not ProgramSkillFrame parent || parent.Id != programParentId || parent.OwnerSeat != use.SourceSeat ||
            parent.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context || context.ParentFrameId != dyingId || ActiveDying?.FrameId != dyingId || !IsActualTurnLegacyVirtualUse(use))
            throw new InvalidOperationException("Cuipo legacy Alcohol lost its actual declared zero-entity dying producer.");
        use = NormalizeActualTurnLegacyCompletedUse(use, parent.Id);
        var returned = new ZhuJunLegacyAlcoholReturn(use.Id, parent.Id, dyingId, parent.InstructionIndex, new(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId), parent.GameplayHash, use.Action!.ActionId);
        UpdateLifecycleCardUse(use.Id, u => u with { ZhuJunLegacyAlcohol = returned });
        AdvanceEventRulesAndQueueFact(new ZhuJunLegacyAlcoholWindowIssuedEvent(returned));
        if (TryBeginProgramCardWindow(null, use.Action, SkillProgramTriggerWindow.CardUseCommitted, [use.SourceSeat], ProgramCardContinuation.CommittedSimpleCard)) return true;
        // A rejected/empty collection must never leave a callback or a pending
        // recovery. The original caller proceeds to its ordinary recovery.
        UpdateLifecycleCardUse(use.Id, u => u with { ZhuJunLegacyAlcohol = null }); return false;
    }
    private bool TryReturnZhuJunLegacyAlcohol(ProgramCardTriggerWindowFrame frame)
    {
        if (LifecycleCardUse(frame.ParentFrameId) is not { ZhuJunLegacyAlcohol: { Returned: false } r } use || frame.Continuation != ProgramCardContinuation.CommittedSimpleCard || frame.SimpleContinuation is not null) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || r.UseId != use.Id || use.CardId != 0 || use.CardKind != CardKind.Alcohol || use.PhysicalCardIds is not { Count: 0 } ||
            frame.Action.ActionId != r.ActionId || use.Action?.ActionId != r.ActionId || frame.Action.ActorSeat != use.SourceSeat || frame.AttackOwnerFrameId is not null || frame.CompletedResponseReturn is not null || frame.TrickContinuation is not null ||
            _resolutionStack.SingleOrDefault(f => f.Id == r.ProgramParentId) is not ProgramSkillFrame parent || parent.InstructionIndex != r.InstructionIndex || parent.GameplayHash != r.ProducerGameplayHash ||
            r.ProducerSource != new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId) || parent.WindowContext?.ParentFrameId != r.DyingId ||
            ProgramEventHistory<ZhuJunLegacyAlcoholWindowIssuedEvent>().Count(e => e.Return == r) != 1 || !IsExactLegacyActualUseCompletion(use) ||
            !frame.Candidates.Any(c => c.SkillId == "ol:cuipo" && ProgramEventHistory<ZhuJunCuipoDrawCompletedEvent>().Any(e => e.OriginFrameId == use.Id && e.ActionId is null)))
            throw new InvalidOperationException("Cuipo legacy Alcohol lost its exact completed draw and original native recovery return.");
        UpdateLifecycleCardUse(use.Id, u => u with { ZhuJunLegacyAlcohol = r with { Returned = true } });
        AdvanceEventRulesAndQueueFact(new ZhuJunLegacyAlcoholReturnedEvent(use.Id, frame.Id, r.ProgramParentId, r.DyingId));
        ResumeProgramVirtualDyingAlcoholRecovery(use.Id, r.ProgramParentId, r.DyingId); return true;
    }
}
