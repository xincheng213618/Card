namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasAnniversaryKuanshi => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi);
    private IEnumerable<AnniversaryKuanshiGrant> ActiveAnniversaryKuanshiGrants() => ProgramEventHistory<AnniversaryKuanshiArmedEvent>()
        .Select(e => e.Grant).Where(g => !ProgramEventHistory<AnniversaryKuanshiExpiredEvent>().Any(e => e.GrantId == g.FrameId));

    private void ArmAnniversaryKuanshi(ProgramSkillFrame supplied, int target)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryKanZeComposition.Kuanshi || f.TriggerId != AnniversaryKanZeComposition.Arm ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, SourceSeat: { } actor } w || actor != f.OwnerSeat || actor != _currentSeat ||
            f.SelectedTargetSeats is not [var selected] || selected != target || !IsValidPlayerSeat(target) || !_players[target].IsAlive || !_players[f.OwnerSeat].IsAlive ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != actor || parent.TurnNumber != _turnNumber || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate) ||
            ProgramEventHistory<AnniversaryKuanshiArmedEvent>().Any(e => e.Grant.FrameId == f.Id))
            throw new InvalidOperationException("Anniversary Kuanshi lost its unpaid native own ending and actual living recipient.");
        AdvanceEventRulesAndQueueFact(new AnniversaryKuanshiArmedEvent(new(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, _turnNumber, target)));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42900)]
    private void ExpireAnniversaryKuanshi(TurnStartedEvent fact)
    {
        if (!HasAnniversaryKuanshi) return;
        foreach (var grant in ActiveAnniversaryKuanshiGrants().Where(g => g.Source.OwnerSeat == fact.ActorSeat && fact.TurnNumber > g.ActualTurn).ToArray())
            AdvanceEventRulesAndQueueFact(new AnniversaryKuanshiExpiredEvent(grant.FrameId, fact.TurnNumber));
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 42900)]
    private void IssueAnniversaryKuanshiDamage(DamageAppliedEvent fact)
    {
        if (!HasAnniversaryKuanshi || fact.Amount <= 0) return;
        var grants = ActiveAnniversaryKuanshiGrants().Where(g => g.TargetSeat == fact.TargetSeat &&
            !ProgramEventHistory<AnniversaryKuanshiDamageIssuedEvent>().Any(e => e.GrantId == g.FrameId && e.ActualTurn == _turnNumber)).OrderBy(g => g.FrameId).ToArray();
        if (grants.Length == 0) return;
        var history = CompleteProgramEventHistory().ToArray(); var start = -1;
        for (var i = history.Length - 1; i >= 0; i--) if (history[i] is TurnStartedEvent t && t.TurnNumber == _turnNumber) { start = i; break; }
        if (start < 0) return;
        var total = history.Skip(start + 1).OfType<DamageAppliedEvent>().Where(e => e.TargetSeat == fact.TargetSeat).Sum(e => e.Amount);
        if (total < 2 || total - fact.Amount >= 2) return;
        var damage = _resolutionStack.OfType<DamageFrame>().LastOrDefault(f => f.SourceSeat == fact.SourceSeat && f.TargetSeat == fact.TargetSeat && f.Amount == fact.Amount && f.Nature == fact.Nature)
            ?? throw new InvalidOperationException("Anniversary Kuanshi requires the actual applied native damage frame.");
        foreach (var group in grants.GroupBy(g => (g.Source.OwnerSeat, g.Source.SkillInstanceId)))
        {
            var occurrence = 0;
            foreach (var grant in group)
                AdvanceEventRulesAndQueueFact(new AnniversaryKuanshiDamageIssuedEvent(grant.FrameId, damage.Id, _turnNumber, fact.TargetSeat, total - fact.Amount, total, occurrence++));
        }
    }

    private void AppendAnniversaryKanZeDamageCandidates(IDamageAttempt attack, SkillProgramTriggerWindow window, List<DamageTriggerCandidate> result)
    {
        if (!HasAnniversaryKuanshi || window != SkillProgramTriggerWindow.AfterDamageApplied) return;
        result.RemoveAll(c => c.ProgramId == AnniversaryKanZeComposition.Kuanshi && c.ProgramTriggerId == AnniversaryKanZeComposition.Reward);
        var damage = _resolutionStack.OfType<DamageFrame>().LastOrDefault(f => f.ParentFrameId == attack.ResolutionId && f.TargetSeat == attack.TargetSeat && f.Amount == attack.DamageAmount);
        if (damage is null || !_players[damage.TargetSeat].IsAlive) return;
        var trigger = _contentRegistry.GetSkill(AnniversaryKanZeComposition.Kuanshi).Program!.Triggers.Single(t => t.Id == AnniversaryKanZeComposition.Reward);
        foreach (var issued in ProgramEventHistory<AnniversaryKuanshiDamageIssuedEvent>().Where(e => e.DamageFrameId == damage.Id && e.ActualTurn == _turnNumber))
        {
            var grant = ProgramEventHistory<AnniversaryKuanshiArmedEvent>().Single(e => e.Grant.FrameId == issued.GrantId).Grant;
            result.Add(new(grant.Source.OwnerSeat, $"anniversary-kuanshi:{grant.FrameId}:{damage.Id}", trigger.Priority, false,
                grant.Source.SkillId, trigger.Id, grant.Source.SkillInstanceId!, grant.GameplayHash, issued.Occurrence));
        }
    }

    private AnniversaryKuanshiRecoveryReceipt? AnniversaryKuanshiWitness(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != AnniversaryKanZeComposition.Kuanshi || c.BindingId != AnniversaryKanZeComposition.Reward ||
            _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash != c.GameplayHash ||
            w.Window != SkillProgramTriggerWindow.AfterDamageApplied || w.OwnerSeat != c.OwnerSeat || w.OccurrenceIndex != c.OccurrenceIndex ||
            w.TargetSeat is not { } target || w.DamageFrameId is not { } damageId || w.Amount is not > 0 ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != damageId || parent.TargetSeat != target || parent.TriggerWindow != w.Window ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex].ToProgramCandidate() != c ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is not { } damage || damage.TargetSeat != target || damage.Amount != w.Amount)
            return null;
        var issues = ProgramEventHistory<AnniversaryKuanshiDamageIssuedEvent>().Where(e => e.DamageFrameId == damageId && e.ActualTurn == _turnNumber &&
            e.TargetSeat == target && e.Occurrence == c.OccurrenceIndex && e.PreviousDamage < 2 && e.CurrentDamage >= 2 && e.CurrentDamage - e.PreviousDamage == damage.Amount).ToArray();
        foreach (var issued in issues)
        {
            var grant = ProgramEventHistory<AnniversaryKuanshiArmedEvent>().SingleOrDefault(e => e.Grant.FrameId == issued.GrantId)?.Grant;
            if (grant is not null && grant.Source.SkillId == c.SkillId && grant.Source.OwnerSeat == c.OwnerSeat && grant.Source.SkillInstanceId == c.SkillInstanceId &&
                grant.GameplayHash == c.GameplayHash && grant.TargetSeat == target && grant.ActualTurn <= issued.ActualTurn)
                return new(grant, issued, parent.Id, 0);
        }
        return null;
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 42900)]
    private bool IsIssuedAnniversaryKanZeKuanshiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w) =>
        AnniversaryKuanshiWitness(c, w) is { } r && !ProgramEventHistory<AnniversaryKuanshiRecoveryBoundEvent>().Any(e => e.GrantId == r.Grant.FrameId && e.DamageFrameId == r.Damage.DamageFrameId);

    private void BindAnniversaryKuanshiDamage(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WindowContext is not { } w || f.InstructionIndex != 1 || f.AnniversaryKuanshi is not null ||
            AnniversaryKuanshiWitness(AnniversaryKanZeCandidate(f), w) is not { } receipt ||
            ProgramEventHistory<AnniversaryKuanshiRecoveryBoundEvent>().Any(e => e.GrantId == receipt.Grant.FrameId && e.DamageFrameId == receipt.Damage.DamageFrameId))
            throw new InvalidOperationException("Anniversary Kuanshi lost its exact issued second actual damage point.");
        receipt = receipt with { InstructionIndex = f.InstructionIndex };
        ReplaceRuntimeTop(f with { AnniversaryKuanshi = receipt });
        AdvanceEventRulesAndQueueFact(new AnniversaryKuanshiRecoveryBoundEvent(f.Id, receipt.Grant.FrameId, receipt.Damage.DamageFrameId, receipt.Damage.ActualTurn));
    }

    [ProgramCapability]
    private sealed class AnniversaryKanZeCapability(GameEngine engine) : IAnniversaryKanZeHost
    {
        public void Arm(ProgramSkillFrame frame, int target) => engine.ArmAnniversaryKuanshi(frame, target);
        public void Bind(ProgramSkillFrame frame) => engine.BindAnniversaryKuanshiDamage(frame);
    }
}
