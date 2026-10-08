namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksRoundGainedSourceUses => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget);
    private long RoundGainedMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;

    private void ObserveRoundGainedRoundBoundary(IGameEvent payload)
    {
        if (!TracksRoundGainedSourceUses || payload is not RoundStartedEvent round) return;
        if (CompleteProgramEventHistory().OfType<RoundGainedRoundBoundaryEvent>().Any(e => e.RoundNumber == round.RoundNumber))
            throw new InvalidOperationException("A round-gained source ledger cannot issue its round boundary twice.");
        AdvanceEventRulesAndQueueFact(new RoundGainedRoundBoundaryEvent(round.RoundNumber, round.ActorSeat, RoundGainedMovementSequence));
    }

    private RoundGainedRoundBoundaryEvent? RoundGainedCurrentBoundary() => _roundNumber > 0
        ? CompleteProgramEventHistory().OfType<RoundGainedRoundBoundaryEvent>().SingleOrDefault(e => e.RoundNumber == _roundNumber) : null;

    private IReadOnlyList<RoundGainedMaterialAcquisition>? RoundGainedMaterials(CharacterState owner, string skillId,
        string policyId, IReadOnlyList<int> ids, RoundGainedRoundBoundaryEvent boundary)
    {
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count) return null;
        var excluded = CompleteProgramEventHistory().OfType<RoundGainedEquipmentDrawIssuedEvent>().Where(e =>
            e.Qualification.ActorSeat == owner.Seat && e.Qualification.Source.SkillId == skillId &&
            e.Qualification.PolicyId == policyId && e.DrawRoundNumber == boundary.RoundNumber).SelectMany(e => e.DrawnCardIds).ToHashSet();
        var result = new List<RoundGainedMaterialAcquisition>();
        foreach (var id in ids)
        {
            if (excluded.Contains(id)) return null;
            var gain = _cardMovements.LastOrDefault(m => m.CardId == id && m.Sequence > boundary.MovementSequence &&
                m.To == CardLocation.Hand(owner.Seat) && ActualHandGainOriginalSource(m) != CardLocation.Hand(owner.Seat));
            if (gain is null) return null;
            result.Add(new(id, gain.Sequence, ActualHandGainOriginalSource(gain)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private bool InRoundGainedOwnPlay(CharacterState owner) => owner.IsAlive && _winner == Winner.None &&
        _currentSeat == owner.Seat && _phase == TurnPhase.Play;

    private bool HasRoundGainedBasicBonus(CharacterState owner, CardKind kind, IReadOnlyList<int> ids)
    {
        if (!TracksRoundGainedSourceUses || GetProgramCardCategory(kind) != SkillProgramCardCategory.Basic ||
            ids is null || ids.Count == 0 || !InRoundGainedOwnPlay(owner)) return false;
        var policies = CardPolicies(owner, SkillProgramCardPolicyKind.RoundGainedOtherSourceUse).ToArray();
        if (policies.Length == 0 || RoundGainedCurrentBoundary() is not { } boundary ||
            ids.Any(id => _cardZones.GetLocation(id).OwnerSeat != owner.Seat || FindOwnedPlayableCard(owner, id) is null)) return false;
        return policies.Any(item =>
            RoundGainedMaterials(owner, item.Source.SkillId, item.Policy.Id, ids, boundary) is not null);
    }

    private bool HasPotentialRoundGainedBasicMaterials(CharacterState owner, CardKind kind)
    {
        if (!TracksRoundGainedSourceUses || !InRoundGainedOwnPlay(owner) ||
            !HasCardPolicy(owner, SkillProgramCardPolicyKind.RoundGainedOtherSourceUse)) return false;
        return GetSlashUseCards(owner).Any(card => HasRoundGainedBasicBonus(owner, kind, [card.Id])) ||
            GetZhangbaSlashPairs(owner).Any(cards => HasRoundGainedBasicBonus(owner, kind, cards.Select(c => c.Id).ToArray())) ||
            GetProgramMultiCardViewAsSelections(owner, kind, forResponse: false)
                .Any(selection => HasRoundGainedBasicBonus(owner, selection.OutputKind, selection.Cards.Select(c => c.Id).ToArray()));
    }

    private bool HasIssuedRoundGainedBasicBonus(long useId, int actor) => LifecycleCardUse(useId) is { Action: { } action } use &&
        action.ActorSeat == actor && GetProgramCardCategory(use.CardKind) == SkillProgramCardCategory.Basic &&
        use.RoundGainedUseQualifications is { Count: > 0 } qualifications && qualifications.Any(q => q.ActorSeat == actor && ValidRoundGainedQualification(use, q));

    private void IssueRoundGainedUseQualifications(long useId, CardActionContext? action)
    {
        if (!TracksRoundGainedSourceUses || action is not { Type: CardActionType.Use } || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.PhysicalCards.Count == 0 || action.ActorSeat != action.ProviderSeat || !IsValidPlayerSeat(action.ActorSeat) ||
            !InRoundGainedOwnPlay(_players[action.ActorSeat]) ||
            LifecycleCardUse(useId) is not { PhysicalCardIds: { } physicalIds } use || use.Action?.ActionId != action.ActionId || use.SourceSeat != action.ActorSeat) return;
        var policies = CardPolicies(_players[action.ActorSeat], SkillProgramCardPolicyKind.RoundGainedOtherSourceUse).ToArray();
        if (policies.Length == 0 || RoundGainedCurrentBoundary() is not { } boundary) return;
        if (use.RoundGainedUseQualifications is not null || CompleteProgramEventHistory().OfType<RoundGainedUseQualifiedEvent>().Any(e => e.Qualification.CardUseFrameId == useId))
            throw new InvalidOperationException("A real Use cannot replace its frozen round-gained qualifications.");
        var owner = _players[action.ActorSeat];
        var qualifications = new List<RoundGainedUseQualification>();
        foreach (var group in policies.GroupBy(item => item.Source.SkillId))
        {
            var entry = group.OrderBy(item => item.Source.SkillInstanceId, StringComparer.Ordinal).First();
            var gains = RoundGainedMaterials(owner, entry.Source.SkillId, entry.Policy.Id, physicalIds, boundary);
            if (gains is null) continue;
            qualifications.Add(new(new(entry.Source.SkillId, entry.Policy.Id, owner.Seat, entry.Source.SkillInstanceId), entry.Source.Program.GameplayHash,
                entry.Policy.Id, boundary.RoundNumber, boundary.MovementSequence, useId, action.ActionId, owner.Seat, action.EffectiveKind) { MaterialGains = gains });
        }
        if (qualifications.Count == 0) return;
        UpdateLifecycleCardUse(useId, frame => frame with { RoundGainedUseQualifications = qualifications });
        foreach (var qualification in qualifications) AdvanceEventRulesAndQueueFact(new RoundGainedUseQualifiedEvent(qualification));
    }

    private bool SameRoundGainedQualification(RoundGainedUseQualification a, RoundGainedUseQualification b) =>
        a.Source == b.Source && a.GameplayHash == b.GameplayHash && a.PolicyId == b.PolicyId && a.RoundNumber == b.RoundNumber &&
        a.RoundStartMovementSequence == b.RoundStartMovementSequence && a.CardUseFrameId == b.CardUseFrameId && a.ActionId == b.ActionId &&
        a.ActorSeat == b.ActorSeat && a.EffectiveKind == b.EffectiveKind && a.MaterialGains.SequenceEqual(b.MaterialGains);

    private bool ValidRoundGainedQualification(CardUseFrame use, RoundGainedUseQualification q)
    {
        if (use.Action is not { Type: CardActionType.Use } action || use.PhysicalCardIds is not { } physicalIds || use.Id != q.CardUseFrameId || action.ActionId != q.ActionId ||
            use.CardKind != q.EffectiveKind || action.EffectiveKind != q.EffectiveKind || action.ProviderSeat != q.ActorSeat ||
            q.Source.OwnerSeat != q.ActorSeat || q.Source.BindingId != q.PolicyId || q.RoundNumber <= 0 || q.MaterialGains.Count == 0 ||
            !q.MaterialGains.Select(m => m.CardId).SequenceEqual(physicalIds) ||
            !q.MaterialGains.Select(m => m.CardId).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
            q.MaterialGains.Select(m => m.CardId).Distinct().Count() != q.MaterialGains.Count ||
            _contentRegistry.GetSkill(q.Source.SkillId).Program is not { } program || program.GameplayHash != q.GameplayHash ||
            program.CardPolicies.Count(p => p.Kind == SkillProgramCardPolicyKind.RoundGainedOtherSourceUse && p.Id == q.PolicyId) != 1) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<RoundGainedRoundBoundaryEvent>().Where(e => e.RoundNumber == q.RoundNumber).ToArray() is not [var boundary] ||
            boundary.MovementSequence != q.RoundStartMovementSequence ||
            history.OfType<RoundGainedUseQualifiedEvent>().Count(e => SameRoundGainedQualification(e.Qualification, q)) != 1) return false;
        foreach (var gain in q.MaterialGains)
            if (gain.MovementSequence <= boundary.MovementSequence || _cardMovements.SingleOrDefault(m => m.Sequence == gain.MovementSequence) is not { } movement ||
                movement.CardId != gain.CardId || movement.To != CardLocation.Hand(q.ActorSeat) || ActualHandGainOriginalSource(movement) != gain.OriginalSource ||
                gain.OriginalSource == CardLocation.Hand(q.ActorSeat)) return false;
        return true;
    }

    private void AssertRoundGainedUseQualifications(CardUseFrame use)
    {
        if (use.RoundGainedUseQualifications is null && !TracksRoundGainedSourceUses) return;
        var issued = CompleteProgramEventHistory().OfType<RoundGainedUseQualifiedEvent>().Where(e => e.Qualification.CardUseFrameId == use.Id).ToArray();
        if (use.RoundGainedUseQualifications is not { } qualifications)
        { if (issued.Length != 0) throw new InvalidOperationException("A qualified Use lost its owning round-source receipt."); return; }
        if (qualifications.Count == 0 || qualifications.Count != issued.Length || qualifications.Select(q => (q.Source.OwnerSeat, q.Source.SkillId)).Distinct().Count() != qualifications.Count ||
            qualifications.Any(q => !ValidRoundGainedQualification(use, q))) throw new InvalidOperationException("A round-source qualification lost its exact acquired materials, policy or native Use.");
    }

    private void CollectIssuedRoundGainedUseCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCommitted) ||
            _winner != Winner.None || _resolutionStack.LastOrDefault() is not CardUseFrame { RoundGainedUseQualifications: { } qualifications } use || use.Action?.ActionId != action.ActionId) return;
        AssertRoundGainedUseQualifications(use);
        foreach (var q in qualifications)
        {
            if (!_players[q.ActorSeat].IsAlive || action.ActorSeat != q.ActorSeat) continue;
            var op = window == SkillProgramTriggerWindow.CardUseTargetsFinalized ? SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget : SkillProgramEffectOp.DrawForRoundGainedEquipmentUse;
            var program = _contentRegistry.GetSkill(q.Source.SkillId).Program!;
            foreach (var trigger in program.Triggers.Where(t => t.Window == window && t.Effects is [{ } e] && e.Op == op))
            {
                if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized ? !IsOrdinaryTrick(use.CardKind) : GetProgramCardCategory(use.CardKind) != SkillProgramCardCategory.Equipment) continue;
                result.RemoveAll(c => c.OwnerSeat == q.ActorSeat && c.SkillId == q.Source.SkillId && c.TriggerId == trigger.Id && c.SkillInstanceId != q.Source.SkillInstanceId);
                if (result.Any(c => c.OwnerSeat == q.ActorSeat && c.SkillId == q.Source.SkillId && c.TriggerId == trigger.Id && c.SkillInstanceId == q.Source.SkillInstanceId)) continue;
                var target = action.TargetSeats.Count == 1 ? action.TargetSeats[0] : -1;
                result.Add(new(q.ActorSeat, target, q.Source.SkillId, trigger.Id, q.GameplayHash, q.Source.SkillInstanceId, trigger.Priority,
                    CreateCardActionProgramContext(action, window, 0, q.ActorSeat, target, CaptureProgramTriggerFacts(_players[q.ActorSeat], action))));
            }
        }
    }

    private RoundGainedUseQualification? ExactIssuedRoundGainedUseCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, bool paid = false)
    {
        if (context.CardUse is not { } card || context.Window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCommitted) ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !paid && (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.ParentFrameId != card.ParentCardUseFrameId || window.Action.ActionId != card.CardActionId ||
            LifecycleCardUse(window.ParentFrameId) is not { RoundGainedUseQualifications: { } qualifications, Action: { Type: CardActionType.Use } action } use ||
            action.ActionId != card.CardActionId || action.ActorSeat != candidate.OwnerSeat || context.OwnerSeat != candidate.OwnerSeat || context.SourceSeat != action.ActorSeat ||
            !window.Action.TargetSeats.SequenceEqual(action.TargetSeats) || !window.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return null;
        var q = qualifications.SingleOrDefault(q => q.ActorSeat == candidate.OwnerSeat && q.Source.SkillId == candidate.SkillId && q.Source.SkillInstanceId == candidate.SkillInstanceId && q.GameplayHash == candidate.GameplayHash);
        var current = window.Candidates[window.CandidateIndex];
        var program = _contentRegistry.GetSkill(candidate.SkillId).Program;
        var trigger = program?.Triggers.SingleOrDefault(t => t.Id == candidate.BindingId);
        var equipment = context.Window == SkillProgramTriggerWindow.CardUseCommitted;
        var op = equipment ? SkillProgramEffectOp.DrawForRoundGainedEquipmentUse : SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget;
        var index = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (q is null || current.OwnerSeat != candidate.OwnerSeat || current.SkillId != candidate.SkillId || current.TriggerId != candidate.BindingId || current.SkillInstanceId != candidate.SkillInstanceId ||
            current.GameplayHash != candidate.GameplayHash || program?.GameplayHash != candidate.GameplayHash || trigger?.Window != context.Window ||
            trigger.Effects is not [{ } effect] || effect.Op != op || CreateCardActionProgramContext(window, current) != context ||
            window.Continuation != (equipment ? ProgramCardContinuation.CommittedSimpleCard : ProgramCardContinuation.FinalizedTrick) ||
            (equipment ? GetProgramCardCategory(use.CardKind) != SkillProgramCardCategory.Equipment : !IsOrdinaryTrick(use.CardKind)) ||
            index < 1 || _resolutionStack[index - 1].Id != use.Id || !ValidRoundGainedQualification(use, q) ||
            (equipment ? !HasExactRoundGainedCommittedEquipmentUse(use, q) : !HasExactAcceptedActualHandGainUse(use, action))) return null;
        return q;
    }

    // Equipment reaches its native committed window after placement, before
    // simple-card finalized acceptance. Its declaration and owning qualified
    // Use prove this stage; its material need not remain in Processing.
    private bool HasExactRoundGainedCommittedEquipmentUse(CardUseFrame use, RoundGainedUseQualification q) =>
        use.ProgramUseCommitted && use.Action is { Type: CardActionType.Use } action && action.ActorSeat == q.ActorSeat &&
        action.ProviderSeat == q.ActorSeat && action.ResponderSeat is null && action.OpponentSeat is null &&
        GetProgramCardCategory(use.CardKind) == SkillProgramCardCategory.Equipment &&
        CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == use.CardId &&
            e.CardKind == use.CardKind && e.SourceSeat == q.ActorSeat) == 1 &&
        CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Count(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual(action.TargetSeats)) == 1;

    private bool HasIssuedRoundGainedUseCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => ExactIssuedRoundGainedUseCandidate(candidate, context) is not null;
    private bool CanContinueIssuedRoundGainedUse(ProgramSkillFrame frame) =>
        frame.RoundGainedEquipmentDrawReceipt is not null && ValidRoundGainedEquipmentDraw(frame) ||
        frame.TriggerId is { } trigger && frame.WindowContext is { } context && ExactIssuedRoundGainedUseCandidate(new(frame.OwnerSeat, frame.SkillId, trigger, frame.SkillInstanceId, frame.GameplayHash, 0), context) is not null;
}
