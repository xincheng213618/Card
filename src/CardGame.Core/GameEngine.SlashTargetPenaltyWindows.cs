namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasSlashTargetPenaltyPrograms => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast);
    private bool MatchesSlashTargetPenaltyUse(SlashTargetPenaltyIdentity r)
    {
        if (r.FrozenDistance != 1 || r.Source.OwnerSeat != r.ActorSeat || string.IsNullOrWhiteSpace(r.Source.SkillId) ||
            string.IsNullOrWhiteSpace(r.Source.BindingId) || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || !IsValidPlayerSeat(r.TargetSeat) ||
            LifecycleCardUse(r.CardUseFrameId) is not { } use || !IsSlashCard(use.CardKind) || use.CardKind != r.EffectiveKind)
            return false;
        // Reuse the mature same-use actor-replacement and legacy producer proof.
        var target = r.TargetSeat;
        if (!use.TargetSeats.Contains(target))
        {
            if (use.TargetSeats.Count == 0 || !CompleteProgramEventHistory().OfType<SlashTargetPenaltyOfferedEvent>().Any(e => e.Identity == r)) return false;
            target = use.TargetSeats[0];
        }
        return MatchesSlashTargetBenefitUse(new(use.Id, r.ActionId, r.ActorSeat, r.ProviderSeat,
            r.EffectiveKind, target, r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.LegacyProducerProgramId));
    }
    private bool TryBeginSlashTargetPenalties(CardAttackHandle attack, bool legacy = false, bool afterActualTargets = false)
    {
        if (!HasSlashTargetPenaltyPrograms || _winner != Winner.None || LifecycleCardUse(attack.ResolutionId) is not { } use ||
            !IsSlashCard(use.CardKind) || attack.IsSourceLess || !IsValidPlayerSeat(use.SourceSeat) || !_players[use.SourceSeat].IsAlive)
            return false;
        var owner = _players[use.SourceSeat];
        var facts = use.Action is { } action ? CaptureProgramTriggerFacts(owner, action) : CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.ActualSlashTargetPenalty, facts)
            .Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast }]).ToArray();
        if (candidates.Length == 0) return false;
        var visited = (use.SlashTargetPenaltyVisits ?? []).ToList();
        var entries = new List<SlashTargetPenaltyEntry>();
        foreach (var target in use.TargetSeats.Distinct())
        {
            if (!IsValidPlayerSeat(target) || target == owner.Seat || !_players[target].IsAlive) continue;
            long? producer = null;
            if (use.Action is null) producer = SlashBenefitLegacyProducer(use, target);
            else if (use.Action.Type != CardActionType.Use || use.Action.ActorSeat != use.SourceSeat || use.Action.EffectiveKind != use.CardKind) continue;
            if (use.Action is null && producer is null) continue;
            foreach (var c in candidates)
            {
                var source = new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId);
                if (visited.Any(v => v.ActorSeat == owner.Seat && v.TargetSeat == target && v.Source.SkillId == c.SkillId && v.Source.BindingId == c.BindingId)) continue;
                // Announced targets are visited even when their distance is not one.
                // Later HP/equipment changes cannot manufacture a second opportunity.
                visited.Add(new(owner.Seat, target, source, c.GameplayHash));
                var distance = GetCombatDistance(owner.Seat, target);
                if (distance != 1) continue;
                var identity = new SlashTargetPenaltyIdentity(use.Id, use.Action?.ActionId, owner.Seat, use.Action?.ProviderSeat ?? owner.Seat,
                    use.CardKind, target, _turnNumber, _currentSeat, producer, source, c.GameplayHash, distance);
                entries.Add(new(c, identity));
            }
        }
        UpdateLifecycleCardUse(use.Id, u => u with { SlashTargetPenaltyVisits = visited });
        if (entries.Count == 0) return false;
        var id = ++_resolutionSequence;
        for (var i = 0; i < entries.Count; i++) AdvanceEventRulesAndQueueFact(new SlashTargetPenaltyOfferedEvent(id, i, entries[i].Identity));
        PushRuntimeFrame(new SlashTargetPenaltyWindowFrame(id, use.Id,
            afterActualTargets ? legacy ? SlashTargetPenaltyReturn.AfterActualTargetsLegacy : SlashTargetPenaltyReturn.AfterActualTargetsSlash :
                legacy ? SlashTargetPenaltyReturn.LegacySlash : SlashTargetPenaltyReturn.FinalizedSlash, entries));
        AdvanceRuntimeTop<SlashTargetPenaltyWindowFrame>(); return true;
    }
    private bool SlashTargetPenaltyWindowMatches(SlashTargetPenaltyWindowFrame w)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == w.Id);
        if (index < 1 || _resolutionStack[index - 1] is not CardUseFrame use || use.Id != w.ParentFrameId ||
            !Enum.IsDefined(w.ReturnKind) || w.Entries.Count == 0 || w.CandidateIndex < 0 || w.CandidateIndex > w.Entries.Count) return false;
        for (var i = 0; i < w.Entries.Count; i++)
        {
            var e = w.Entries[i]; var r = e.Identity; var c = e.Candidate;
            if (r.CardUseFrameId != use.Id || r.Source.SkillId != c.SkillId || r.Source.BindingId != c.BindingId ||
                r.Source.OwnerSeat != c.OwnerSeat || r.Source.SkillInstanceId != c.SkillInstanceId || r.GameplayHash != c.GameplayHash ||
                !MatchesSlashTargetPenaltyUse(r) || CompleteProgramEventHistory().OfType<SlashTargetPenaltyOfferedEvent>().Count(f =>
                    f.WindowId == w.Id && f.CandidateIndex == i && f.Identity == r) != 1 ||
                !(use.SlashTargetPenaltyVisits ?? []).Any(v => v.ActorSeat == r.ActorSeat && v.TargetSeat == r.TargetSeat && v.Source == r.Source && v.GameplayHash == r.GameplayHash)) return false;
        }
        return true;
    }
    private bool SlashTargetPenaltyProgramMatches(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.ActualSlashTargetPenalty } context ||
            _resolutionStack.OfType<SlashTargetPenaltyWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            !SlashTargetPenaltyWindowMatches(parent) || parent.CandidateIndex >= parent.Entries.Count) return false;
        var e = parent.Entries[parent.CandidateIndex]; var r = e.Identity;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id && MountObserverCandidateMatches(f, e.Candidate) &&
            context.OwnerSeat == f.OwnerSeat && context.SourceSeat == r.ActorSeat && context.TargetSeat == r.TargetSeat &&
            context.OccurrenceIndex == e.Candidate.OccurrenceIndex && context.SlashTargetPenalty == r &&
            f.InstructionIndex is 0 or 1;
    }
    private bool CanRunSlashTargetPenalty(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context.SlashTargetPenalty is { } r && context.Window == SkillProgramTriggerWindow.ActualSlashTargetPenalty &&
        r.Source.SkillId == c.SkillId && r.Source.BindingId == c.BindingId && r.Source.OwnerSeat == c.OwnerSeat &&
        r.Source.SkillInstanceId == c.SkillInstanceId && r.GameplayHash == c.GameplayHash && MatchesSlashTargetPenaltyUse(r) &&
        _winner == Winner.None && _players[c.OwnerSeat].IsAlive && _players[r.TargetSeat].IsAlive &&
        LifecycleCardUse(r.CardUseFrameId)!.TargetSeats.Contains(r.TargetSeat) &&
        HasRuntimeSkillInstance(_players[c.OwnerSeat], c.SkillId, c.SkillInstanceId);
    private void ContinueSlashTargetPenaltyWindow()
    {
        while (_resolutionStack.LastOrDefault() is SlashTargetPenaltyWindowFrame w)
        {
            if (!SlashTargetPenaltyWindowMatches(w)) throw new InvalidOperationException("The target penalty lost its original same-use window.");
            if (w.CandidateIndex >= w.Entries.Count || _winner != Winner.None)
            {
                PopResolutionFrame(w.Id, ResolutionFrameKind.SlashTargetPenaltyWindow);
                var attack = ActiveCardAttack;
                if (attack is null || attack.ResolutionId != w.ParentFrameId) throw new InvalidOperationException("The target penalty lost its exact Slash return.");
                if (_winner != Winner.None) { SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); CompleteAttack(attack); }
                else if (w.ReturnKind is SlashTargetPenaltyReturn.AfterActualTargetsSlash or SlashTargetPenaltyReturn.AfterActualTargetsLegacy)
                    ContinueSlashAfterTargetBenefitAdditions(attack, w.ReturnKind == SlashTargetPenaltyReturn.AfterActualTargetsLegacy);
                else if (w.ReturnKind == SlashTargetPenaltyReturn.LegacySlash)
                {
                    if (TryBeginSlashTargetPenalties(attack, true) || TryBeginSlashTargetBenefits(attack, true) || TryBeginActualUseTargetPrograms(attack, ActualUseTargetReturnKind.LegacyVirtualSlash)) return;
                    ContinueSlashAfterResponsePrograms(attack);
                }
                else ContinueSlashAfterFinalizedTargets(attack);
                return;
            }
            var e = w.Entries[w.CandidateIndex]; var r = e.Identity;
            var context = new ProgramSkillWindowContext(SkillProgramTriggerWindow.ActualSlashTargetPenalty, w.Id, e.Candidate.OwnerSeat,
                SourceSeat: r.ActorSeat, TargetSeat: r.TargetSeat, OccurrenceIndex: e.Candidate.OccurrenceIndex) { SlashTargetPenalty = r };
            if (!CanRunSlashTargetPenalty(e.Candidate, context)) { ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); continue; }
            ReplaceRuntimeTop(w with { Step = ResolutionFrameStep.AwaitingResponse }); BeginProgramBinding(e.Candidate, context); return;
        }
    }
    private void CompleteSlashTargetPenaltyBinding(ProgramSkillFrame f)
    {
        if (_resolutionStack.LastOrDefault() is not SlashTargetPenaltyWindowFrame w || w.Id != f.WindowContext?.ParentFrameId ||
            w.CandidateIndex >= w.Entries.Count || !MountObserverCandidateMatches(f, w.Entries[w.CandidateIndex].Candidate))
            throw new InvalidOperationException("The target penalty binding lost its exact parent candidate.");
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceRuntimeTop<SlashTargetPenaltyWindowFrame>();
    }
}
