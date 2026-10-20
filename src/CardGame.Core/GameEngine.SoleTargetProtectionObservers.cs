namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SoleTargetProtectionFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!HasExactSoleProtectionRoot(f) || f.SoleTargetProtection is not { Cost: not null } r) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (moved.ResumeProgramFrameId is not null || moved.Id != moved.Batch.Id || moved.Batch.ParentFrameId != f.Id ||
                moved.Batch.AwaitingProgramFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat ||
                moved.Batch.OriginSkillId != f.SkillId || moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
            return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (r.Stage switch
            {
                SoleTargetProtectionStage.CostChildren => m.Sequence > r.CostSequenceBefore && m.Sequence <= r.CostSequenceAfter &&
                    m.CardId == r.Cost.CardId && m.From == r.Cost.From && m.Reason.Value == SoleProtectionCostReason,
                SoleTargetProtectionStage.DrawChildren => m.Sequence > r.RewardSequenceBefore && m.Sequence <= r.RewardSequenceAfter &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.sole-target-protection.draw",
                SoleTargetProtectionStage.QuarantineChildren => LifecycleCardUse(r.CardUseFrameId)?.SoleTargetQuarantine is { } q &&
                    m.Sequence > q.QuarantineSequenceBefore && m.Sequence <= q.QuarantineSequenceAfter && m.From == CardLocation.Processing &&
                    m.To == CardLocation.OutsideGame && m.Reason.Value == SoleProtectionQuarantineReason && q.Materials.Any(x => x.CardId == m.CardId),
                _ => false
            }));
        }
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == SoleTargetProtectionStage.CostChildren && r.Cost.Kind == CardKind.SilverLion &&
            r.Cost.From == CardLocation.Equipment(f.OwnerSeat) && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id &&
            hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Kind == HpChangeKind.Recovery && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
            CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
    }
    private ProgramSkillFrame? SoleTargetProtectionObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.SoleTargetProtectionFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    private bool HasSoleTargetProtectionCardObserver(long useId) => SoleTargetProtectionObserverRoot()?.SoleTargetProtection?.CardUseFrameId == useId;
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 110)]
    private bool IsSoleTargetProtectionDying() => ActiveDying is { } dying && SoleTargetProtectionObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 40)]
    private bool TryAdvanceSoleTargetProtectionSubtree() => _pendingDecision is null && SoleTargetProtectionObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);

    private bool IsSoleTargetQuarantinedAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !HasExactSoleTargetQuarantine(use) ||
            attack.CardUserSeat != use.SourceSeat || attack.EffectiveCardKind != use.CardKind ||
            attack.PhysicalCards.Any(c => !use.Action!.PhysicalCards.Any(p => p.CardId == c.Id) ||
                !IsSoleTargetQuarantinedMaterial(use.Id, c.Id) && !IsProgramAlternativeCost(use.Action!, c.Id) &&
                !IsCurrentUsePhysicalCardClaim(use.Id, c.Id) && !IsExchangedCardClaim(use.Action!.ActionId, c.Id)) ||
            use.SoleTargetQuarantine!.Materials.Any(m => !attack.PhysicalCards.Any(c => c.Id == m.CardId))) return false;
        var parentIds = _resolutionStack.OfType<CardUseFrame>().Where(p => p.Id != use.Id && p.Action is { Type: CardActionType.Use })
            .SelectMany(p => p.PhysicalCardIds ?? (p.CardId == 0 ? [] : new[] { p.CardId })).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToHashSet();
        return processing.Select(c => c.Id).ToHashSet().SetEquals(parentIds);
    }
}
