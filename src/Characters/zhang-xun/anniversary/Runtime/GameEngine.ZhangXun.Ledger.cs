namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Derived opt-in only. The command journal reissues this event on recovery;
    // every actual entitlement below still proves the original immutable lease.
    private bool _hasIssuedAnniversaryZhangXun;
    private bool HasAnniversaryZhangXunCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 45200)] private void ObserveAnniversaryZhangXunLease(ZhangXunSuizhengLeaseEvent fact) => _hasIssuedAnniversaryZhangXun = true;
    private sealed record ZhangXunDueLease(ZhangXunSuizhengLeaseEvent Lease, int Ordinal, int StartIndex, int DueTurn);
    private bool ValidZhangXunLease(ZhangXunSuizhengLeaseEvent lease, IReadOnlyList<IGameEvent> history)
    {
        if (lease.FrameId <= 0 || lease.EndingWindowId <= 0 || lease.IssuedTurn < 1 || !IsValidPlayerSeat(lease.RecipientSeat) ||
            !IsValidPlayerSeat(lease.Source.OwnerSeat) || lease.Source.SkillId != AnniversaryZhangXunComposition.Skill || lease.Source.BindingId != AnniversaryZhangXunComposition.Arm ||
            !_contentRegistry.Skills.TryGetValue(lease.Source.SkillId, out var skill) || skill.Program?.GameplayHash != lease.Hash ||
            history.OfType<ZhangXunSuizhengLeaseEvent>().Count(e => e.FrameId == lease.FrameId) != 1) return false;
        return history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == lease.FrameId && e.OwnerSeat == lease.Source.OwnerSeat &&
            e.SkillId == lease.Source.SkillId && e.BindingId == lease.Source.BindingId && e.SkillInstanceId == lease.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.TurnEnding) == 1;
    }
    private IReadOnlyList<ZhangXunDueLease> ActiveZhangXunLeases()
    {
        if (!HasAnniversaryZhangXunCapability || !_hasIssuedAnniversaryZhangXun) return [];
        var history = CompleteProgramEventHistory().ToArray(); var result = new List<ZhangXunDueLease>(); var ordinal = 0;
        for (var i = 0; i < history.Length; i++)
        {
            if (history[i] is not ZhangXunSuizhengLeaseEvent lease) continue;
            var index = ordinal++;
            if (!ValidZhangXunLease(lease, history) || !_players[lease.RecipientSeat].IsAlive ||
                history.Skip(i + 1).OfType<PlayerDiedEvent>().Any(e => e.VictimSeat == lease.RecipientSeat)) continue;
            var start = Array.FindIndex(history, i + 1, e => e is TurnStartedEvent t && t.ActorSeat == lease.RecipientSeat && t.TurnNumber > lease.IssuedTurn);
            if (start < 0 || history[start] is not TurnStartedEvent due || due.TurnNumber != _turnNumber || due.ActorSeat != _turnProgression.OwnerSeat ||
                history.Skip(start + 1).OfType<TurnEndedEvent>().Any(t => t.TurnNumber == due.TurnNumber && t.ActorSeat == due.ActorSeat)) continue;
            result.Add(new(lease, index, start, due.TurnNumber));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private bool HasZhangXunSlashBenefit(int seat) => HasAnniversaryZhangXunCapability && _hasIssuedAnniversaryZhangXun &&
        _phase == TurnPhase.Play && _currentSeat == seat && _turnProgression.OwnerSeat == seat && ActiveZhangXunLeases().Any(d => d.Lease.RecipientSeat == seat);
    private IEnumerable<RuleQueryContribution> AnniversaryZhangXunSlashContributions(CharacterState player, SkillRuleQuery query)
    {
        if (query != SkillRuleQuery.SlashLimit || !HasAnniversaryZhangXunCapability || !_hasIssuedAnniversaryZhangXun ||
            _phase != TurnPhase.Play || _currentSeat != player.Seat || _turnProgression.OwnerSeat != player.Seat) return [];
        return ActiveZhangXunLeases().Where(d => d.Lease.RecipientSeat == player.Seat).Select(d => (RuleQueryContribution)new FiniteRuleQueryContribution(
            $"anniversary:suizheng:{d.Lease.FrameId}", SkillRuleOperation.Add, 1)).ToArray();
    }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 45200)] private bool ZhangXunCardDistance(ProgramCardDistanceQuery q) => IsSlashCard(q.Kind) && HasZhangXunSlashBenefit(q.Actor.Seat);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 45200)] [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 45200)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 45200)] [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 45200)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 45200)] [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 45200)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 45200)] private bool ZhangXunSlashDistance(ProgramSlashDistanceQuery q) => IsSlashCard(q.Kind) && HasZhangXunSlashBenefit(q.Actor.Seat);
    private bool LegalZhangXunFollowupTarget(int owner, int target) => IsValidPlayerSeat(owner) && IsValidPlayerSeat(target) && _players[owner].IsAlive && _players[target].IsAlive && owner != target &&
        !IsCardUseForbidden(owner, CardKind.Slash, CardActionType.Use) && CanUseVirtualSlashTarget(_players[owner], _players[target], CardKind.Slash, ignoreDistance: true, countsTowardLimit: false) &&
        !IsDirectedCardTargetProhibited(owner, target, CardKind.Slash) && !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None);
    private IReadOnlyList<ZhangXunSuizhengDamageWitness> ZhangXunVictims(ZhangXunDueLease due, int cutoff)
    {
        var history = CompleteProgramEventHistory().ToArray();
        if (cutoff < due.StartIndex || cutoff > history.Length) throw new InvalidOperationException("Suizheng's damage cutoff must belong to its original due actual turn.");
        return Array.AsReadOnly(history.Take(cutoff).Select((fact, index) => (fact, index)).Where(x => x.index > due.StartIndex &&
            x.fact is DamageAppliedEvent { Amount: > 0, SourceLess: false } damage && damage.SourceSeat == due.Lease.RecipientSeat && LegalZhangXunFollowupTarget(due.Lease.Source.OwnerSeat, damage.TargetSeat))
            .Select(x => new ZhangXunSuizhengDamageWitness(x.index, ((DamageAppliedEvent)x.fact).TargetSeat)).DistinctBy(x => x.TargetSeat).ToArray());
    }
    private bool ZhangXunPhaseUsed(long leaseId, int turn, int phase) => ProgramEventHistory<ZhangXunSuizhengFollowupStartedEvent>().Any(e => e.LeaseId == leaseId && e.ActualTurn == turn && e.PhaseInstance == phase) ||
        ProgramEventHistory<ZhangXunSuizhengPhaseClosedEvent>().Any(e => e.LeaseId == leaseId && e.ActualTurn == turn && e.PhaseInstance == phase);
    private IEnumerable<ProgramTriggerCandidate> IssuedAnniversaryZhangXunPlayEndingCandidates(CharacterState phaseActor)
    {
        if (!HasAnniversaryZhangXunCapability || !_hasIssuedAnniversaryZhangXun || !phaseActor.IsAlive || _winner != Winner.None ||
            _phase != TurnPhase.Play || phaseActor.Seat != _currentSeat || phaseActor.Seat != _turnProgression.OwnerSeat) yield break;
        foreach (var due in ActiveZhangXunLeases().Where(d => d.Lease.RecipientSeat == phaseActor.Seat && _players[d.Lease.Source.OwnerSeat].IsAlive))
        {
            if (ZhangXunPhaseUsed(due.Lease.FrameId, due.DueTurn, _cardUseDebitPhaseInstanceId) || ZhangXunVictims(due, CompleteProgramEventHistory().Count()).Count == 0) continue;
            var trigger = _contentRegistry.GetSkill(due.Lease.Source.SkillId).Program!.Triggers.Single(t => t.Id == AnniversaryZhangXunComposition.Followup);
            yield return new(due.Lease.Source.OwnerSeat, due.Lease.Source.SkillId, trigger.Id, due.Lease.Source.SkillInstanceId, due.Lease.Hash, trigger.Priority, due.Ordinal);
        }
    }
    private ZhangXunDueLease? ExactZhangXunCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (!HasAnniversaryZhangXunCapability || !_hasIssuedAnniversaryZhangXun || c.SkillId != AnniversaryZhangXunComposition.Skill || c.BindingId != AnniversaryZhangXunComposition.Followup ||
            !IsValidPlayerSeat(c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive || w.Window != SkillProgramTriggerWindow.PlayEnding || w.OwnerSeat != c.OwnerSeat || w.SourceSeat != _currentSeat ||
            w.OccurrenceIndex != c.OccurrenceIndex || _phase != TurnPhase.Play || _currentSeat != _turnProgression.OwnerSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(f => f.Id == w.ParentFrameId) is not { Window: SkillProgramTriggerWindow.PlayEnding } parent ||
            parent.OwnerSeat != _currentSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return null;
        return ActiveZhangXunLeases().SingleOrDefault(d => d.Ordinal == c.OccurrenceIndex && d.Lease.Source.OwnerSeat == c.OwnerSeat && d.Lease.Source.SkillId == c.SkillId &&
            d.Lease.Source.SkillInstanceId == c.SkillInstanceId && d.Lease.Hash == c.GameplayHash && d.Lease.RecipientSeat == _currentSeat);
    }
    private bool IsIssuedAnniversaryZhangXunCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        var due = ExactZhangXunCandidate(c, w);
        if (due is null || _winner != Winner.None) return false;
        if (ZhangXunPhaseUsed(due.Lease.FrameId, due.DueTurn, _cardUseDebitPhaseInstanceId))
            return _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ZhangXunSuizheng is { } r && r.LeaseId == due.Lease.FrameId && r.ParentWindowId == w.ParentFrameId &&
                r.ActualTurn == due.DueTurn && r.PhaseInstance == _cardUseDebitPhaseInstanceId && MountObserverCandidateMatches(f, c));
        return ZhangXunVictims(due, CompleteProgramEventHistory().Count()).Count > 0;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 45200, SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash)]
    private bool CanOfferZhangXunFollowup(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context) => IsIssuedAnniversaryZhangXunCandidate(c, context);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 45201)] private void CloseZhangXunPhase(ProgramBindingResolvedEvent fact)
    {
        if (!HasAnniversaryZhangXunCapability || !_hasIssuedAnniversaryZhangXun || fact.SkillId != AnniversaryZhangXunComposition.Skill || fact.BindingId != AnniversaryZhangXunComposition.Followup ||
            fact.Window != SkillProgramTriggerWindow.PlayEnding || _resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.PlayEnding } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count) return;
        var c = parent.Candidates[parent.CandidateIndex];
        if (c.OwnerSeat != fact.OwnerSeat || c.SkillId != fact.SkillId || c.BindingId != fact.BindingId || c.SkillInstanceId != fact.SkillInstanceId) return;
        var due = ActiveZhangXunLeases().SingleOrDefault(d => d.Ordinal == c.OccurrenceIndex && d.Lease.Source.OwnerSeat == c.OwnerSeat && d.Lease.Source.SkillInstanceId == c.SkillInstanceId && d.Lease.Hash == c.GameplayHash && d.Lease.RecipientSeat == parent.OwnerSeat);
        if (due is null || ProgramEventHistory<ZhangXunSuizhengPhaseClosedEvent>().Any(e => e.LeaseId == due.Lease.FrameId && e.WindowId == parent.Id)) return;
        if (fact.Activated)
        {
            // Completion pops the original Program child before publishing its
            // own ID; refusal instead publishes the still-active parent ID.
            if (fact.FrameId == parent.Id || ProgramEventHistory<ZhangXunSuizhengFollowupStartedEvent>().Where(e => e.FrameId == fact.FrameId).ToArray() is not [var started] ||
                started.ParentWindowId != parent.Id || started.LeaseId != due.Lease.FrameId || started.ActualTurn != due.DueTurn || started.RecipientSeat != parent.OwnerSeat ||
                started.PhaseInstance != _cardUseDebitPhaseInstanceId ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.FrameId && e.OwnerSeat == c.OwnerSeat && e.SkillId == c.SkillId &&
                    e.BindingId == c.BindingId && e.SkillInstanceId == c.SkillInstanceId && e.Window == SkillProgramTriggerWindow.PlayEnding) != 1) return;
        }
        else if (fact.FrameId != parent.Id) return;
        AdvanceEventRulesAndQueueFact(new ZhangXunSuizhengPhaseClosedEvent(due.Lease.FrameId, parent.Id, _turnNumber, _cardUseDebitPhaseInstanceId, fact.Activated));
    }
}
