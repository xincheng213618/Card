namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureVirtualDuelLegacy, 3)]
    private void CaptureMoDcLegacyDuel(ProgramSkillFrame f, int actor, int target)
    {
        if (!HasMoDiaoChan || ActiveMoDcPromises(actor).Length == 0 || CurrentActualDiscardRecoveryPhase() is not { } phase) return;
        f = GetActiveProgramFrame(f.Id);
        if (f.MoDiaoChanLegacyDuel is not null || FrozenProgramDuelInstructionIndex(f.Id) != f.InstructionIndex ||
            f.CardAttack is not { EffectiveCardKind: CardKind.Duel, Active: true, CardId: null, PhysicalCardIds.Count: 0 })
            throw new InvalidOperationException("Huanhuo direct Duel requires its real freshly issued native producer.");
        var entry = ++_resolutionSequence;
        var r = new MoDiaoChanLegacyDuelReceipt(entry, f.Id, f.InstructionIndex, MoDcSource(f), f.GameplayHash, actor, target);
        ReplaceRuntimeFrame(f.Id, f = f with { MoDiaoChanLegacyDuel = r }); AdvanceEventRulesAndQueueFact(new MoDiaoChanLegacyDuelIssuedEvent(r));
        foreach (var p in ActiveMoDcPromises(actor))
        {
            var ordinal = ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim.PromiseId == p.PromiseId) + 1;
            var q = new MoDiaoChanUseClaim(p.PromiseId, f.Id, -entry, actor, actor, CardKind.Duel, phase.Token, _turnNumber, ordinal,
                p.Source with { BindingId = "affected-use-completed" }, p.GameplayHash);
            ReplaceRuntimeFrame(f.Id, f = f with { MoDiaoChanClaims = (f.MoDiaoChanClaims ?? []).Append(q).ToArray() }); AdvanceEventRulesAndQueueFact(new MoDiaoChanUseClaimedEvent(q));
        }
    }
    private bool MoDcLegacyProducerMatches(ProgramSkillFrame parent, MoDiaoChanLegacyDuelReceipt r) => r.ProducerFrameId == parent.Id &&
        r.ProducerSource == MoDcSource(parent) && r.ProducerGameplayHash == parent.GameplayHash &&
        _contentRegistry.GetSkill(parent.SkillId).Program?.GameplayHash == r.ProducerGameplayHash && FrozenProgramDuelInstructionIndex(parent.Id) == r.InstructionIndex &&
        parent.CardAttack is { EffectiveCardKind: CardKind.Duel, CardId: null, PhysicalCardIds.Count: 0 } &&
        ProgramEventHistory<MoDiaoChanLegacyDuelIssuedEvent>().Count(e => e.Receipt == (r with { Returned = false, Settled = false, WindowId = 0, DamageApplied = false, FinalTarget = 0 })) == 1;

    // Runs before Wenlan's own direct-Duel completion. Return routes through the
    // same native phase again after signing Settled, so other real producers keep their order.
    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 29080)]
    private bool ReturnMoDcLegacyDuel(AttackCompletionReceipt completion)
    {
        if (completion.ProgramFrameId is not { } parentId || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoDiaoChanLegacyDuel: { Settled: false } r } parent || parent.Id != parentId) return false;
        if (!MoDcLegacyProducerMatches(parent, r) || completion.ResolutionId != parent.Id || completion.DelayedTurn || completion.RequestedSlashChild ||
            completion.DelayedTurnSeat is not null || parent.CardAttack is not { Active: false } || ActiveDuel is not null || ActiveCardAttack is not null)
            throw new InvalidOperationException("Huanhuo lost its completely returned native direct Duel.");
        if (!r.Returned)
        {
            r = r with { Returned = true, DamageApplied = completion.DamageWasApplied, FinalTarget = completion.FinalTargetSeat };
            ReplaceRuntimeFrame(parent.Id, parent = parent with { MoDiaoChanLegacyDuel = r });
            AdvanceEventRulesAndQueueFact(new MoDiaoChanLegacyDuelReturnedEvent(r.EntryId, parent.Id, r.DamageApplied, r.FinalTarget));
        }
        return BeginNextMoDcLegacyClaim(parent);
    }
    private bool BeginNextMoDcLegacyClaim(ProgramSkillFrame parent)
    {
        var r = parent.MoDiaoChanLegacyDuel!;
        var claim = parent.MoDiaoChanClaims?.FirstOrDefault(q => q.ActionId == -r.EntryId && !ProgramEventHistory<MoDiaoChanUseSettledEvent>().Any(e => e.PromiseId == q.PromiseId && e.ActionId == q.ActionId));
        if (claim is null || _winner != Winner.None)
        {
            ReplaceRuntimeFrame(parent.Id, parent with { MoDiaoChanLegacyDuel = r with { Settled = true, WindowId = 0 } });
            AdvanceEventRulesAndQueueFact(new MoDiaoChanLegacyDuelResumedEvent(r.EntryId, parent.Id)); return false;
        }
        var id = ++_resolutionSequence; r = r with { WindowId = id };
        ReplaceRuntimeFrame(parent.Id, parent with { MoDiaoChanLegacyDuel = r });
        var candidate = new ProgramTriggerCandidate(claim.Source.OwnerSeat, claim.Source.SkillId, claim.Source.BindingId, claim.Source.SkillInstanceId, claim.GameplayHash, 0);
        var context = new ProgramSkillWindowContext(SkillProgramTriggerWindow.CardUseCompleted, id, candidate.OwnerSeat,
            SourceSeat: r.ActorSeat, TargetSeat: r.TargetSeat, Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]));
        PushRuntimeFrame(new MoDiaoChanDirectDuelWindowFrame(id, parent.Id, r, claim, candidate, context));
        BeginProgramBinding(candidate, context); return true;
    }
    private MoDiaoChanUseClaim? MoDcDirectClaim(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (_resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not MoDiaoChanDirectDuelWindowFrame w || w.Candidate != c || w.Context != context ||
            context.CardUse is not null || context.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramSkillFrame p || p.MoDiaoChanLegacyDuel != w.Duel ||
            !w.Duel.Returned || w.Duel.Settled || w.Duel.WindowId != w.Id || !MoDcLegacyProducerMatches(p, w.Duel) ||
            w.Claim.ActionId != -w.Duel.EntryId || w.Claim.ParentFrameId != p.Id || p.MoDiaoChanClaims?.Contains(w.Claim) != true ||
            ProgramEventHistory<MoDiaoChanLegacyDuelReturnedEvent>().Count(e => e.EntryId == w.Duel.EntryId && e.ProducerFrameId == p.Id) != 1) return null;
        return w.Claim;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ReturnCompletedProgramBinding, 1)]
    private bool TryReturnMoDcLegacyBinding(ProgramSkillFrame child, bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not MoDiaoChanDirectDuelWindowFrame w || child.WindowContext?.ParentFrameId != w.Id) return false;
        if (MoDcDirectClaim(w.Candidate, w.Context) != w.Claim || child.SkillId != w.Candidate.SkillId || child.SkillInstanceId != w.Candidate.SkillInstanceId ||
            child.OwnerSeat != w.Candidate.OwnerSeat || child.GameplayHash != w.Candidate.GameplayHash || completed && !ProgramEventHistory<MoDiaoChanUseSettledEvent>().Any(e => e.PromiseId == w.Claim.PromiseId && e.ActionId == w.Claim.ActionId))
            throw new InvalidOperationException("Huanhuo native direct Duel lost its exact completed child.");
        PopResolutionFrame(w.Id, ResolutionFrameKind.MoDiaoChanDirectDuelWindow);
        var parent = GetActiveProgramFrame(w.ParentFrameId); if (BeginNextMoDcLegacyClaim(parent)) return true;
        var r = GetActiveProgramFrame(parent.Id).MoDiaoChanLegacyDuel!;
        if (!NativeReturns.ReturnAttack(NativeAttackReturnPhase.BeforeParentResume, new(parent.Id, false, parent.Id, null, DamageWasApplied: r.DamageApplied, FinalTargetSeat: r.FinalTarget))) AdvanceRuntimeProgram(parent.Id);
        return true;
    }
}
