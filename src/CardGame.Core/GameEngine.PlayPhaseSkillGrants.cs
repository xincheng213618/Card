namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPlayPhaseSkillGrantCapability =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantPlayPhaseSkills) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantPlayPhaseSkills);

    private sealed partial class ProgramSkillHost : IPlayPhaseSkillGrantHost
    {
        public void GrantPlayPhaseSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
            engine.GrantProgramPlayPhaseSkills(frame, skillIds);
    }

    private void GrantProgramPlayPhaseSkills(ProgramSkillFrame supplied, IReadOnlyList<string> skillIds)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        ValidateProgramTurnEffectGrant(frame);
        if (frame != supplied || frame.InstructionIndex <= 0 ||
            frame.PendingMovementContinuation is not null ||
            frame.SelectedCardPayment is not null && frame.SelectedCardPaymentResult is null ||
            !_setupComplete || _status != EngineStatus.Running || _winner != Winner.None ||
            _phase != TurnPhase.Play || frame.OwnerSeat != _currentSeat ||
            _cardUseDebitPhaseInstanceId <= 0 || !_players[frame.OwnerSeat].IsAlive ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program ||
            program.GameplayHash != frame.GameplayHash ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("A play-phase skill grant requires its living active owner and an unpaid-free instruction in the actual Play phase.");

        var effect = ProgramInstructionResolver.Default.Resolve(frame, program)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.GrantPlayPhaseSkills ||
            effect.Target != SkillProgramEffectTarget.Owner ||
            effect.Condition.Kind != SkillProgramConditionKind.Always ||
            effect.TargetReference is not null || effect.SkillIds.Count == 0 ||
            !effect.SkillIds.SequenceEqual(skillIds, StringComparer.Ordinal) ||
            skillIds.Distinct(StringComparer.Ordinal).Count() != skillIds.Count)
            throw new InvalidOperationException("A play-phase skill grant no longer matches its exact configured instruction.");

        var history = CompleteProgramEventHistory();
        if (frame.TriggerId is null)
        {
            if (frame.WindowContext is not null ||
                !program.Activations.Any(activation => activation.Id == frame.ActivationId) ||
                history.OfType<ProgramSkillStartedEvent>().Count(started =>
                    started.FrameId == frame.Id && started.OwnerSeat == frame.OwnerSeat &&
                    started.SkillId == frame.SkillId && started.ActivationId == frame.ActivationId) != 1)
                throw new InvalidOperationException("A play-phase skill grant lost its original activation binding.");
        }
        else
        {
            var trigger = GetProgramTrigger(frame);
            PlayPhaseSkillGrantContract.ValidateTrigger(frame.SkillId, trigger);
            var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
            if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } context ||
                context.OwnerSeat != frame.OwnerSeat || context.SourceSeat != frame.OwnerSeat ||
                context.TargetSeat != frame.OwnerSeat || index < 1 ||
                _resolutionStack[index - 1] is not PlayPhaseStartingBoundaryFrame parent ||
                parent.Id != context.ParentFrameId || parent.OwnerSeat != frame.OwnerSeat ||
                parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
                parent.Items[parent.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } ||
                !MountObserverCandidateMatches(frame, candidate) ||
                context.OccurrenceIndex != candidate.OccurrenceIndex ||
                context.Facts != (parent.Items[parent.ItemIndex].Facts ?? parent.Facts) ||
                history.OfType<ProgramBindingStartedEvent>().Count(started =>
                    started.FrameId == frame.Id && started.OwnerSeat == frame.OwnerSeat &&
                    started.SkillId == frame.SkillId && started.SkillInstanceId == frame.SkillInstanceId &&
                    started.BindingId == frame.TriggerId && started.Window == context.Window) != 1)
                throw new InvalidOperationException("A play-phase skill grant lost its exact own phase-start parent or candidate.");
        }

        var expiry = new SkillGrantPhaseExpiry(_turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId);
        if (history.OfType<TurnEndedEvent>().Any(ended => ended.TurnNumber == _turnNumber) ||
            history.OfType<ProgramPlayPhaseSkillBoundaryClosedEvent>().Any(closed => closed.Expiry == expiry))
            throw new InvalidOperationException("A closed Play phase cannot issue another skill grant.");

        var bindingId = GetProgramBindingId(frame);
        var source = new CardConversionSource(frame.SkillId, bindingId, frame.OwnerSeat, frame.SkillInstanceId);
        var sourceId = $"phase:{expiry.TurnNumber}:{expiry.PhaseActorSeat}:{expiry.PhaseInstanceId}:" +
            $"donor:{frame.OwnerSeat}:{frame.SkillId}:{frame.SkillInstanceId}:{bindingId}:" +
            $"frame:{frame.Id}:effect:{frame.InstructionIndex - 1}";
        if (history.OfType<ProgramPlayPhaseSkillsGrantedEvent>().Any(issued =>
                issued.FrameId == frame.Id && issued.Grants.Any(grant => grant.SourceId == sourceId)))
            throw new InvalidOperationException("A play-phase skill instruction cannot issue its grant twice.");

        var owner = _players[frame.OwnerSeat];
        foreach (var skillId in skillIds) _ = _contentRegistry.GetSkill(skillId);
        var grants = skillIds.Select(skillId =>
        {
            // A new lifetime owns a new source, while an already effective
            // permanent or turn skill keeps its quota and runtime state.
            var existing = GetSkillBindingShard(owner).ActiveGrants.Where(grant => grant.SkillId == skillId)
                .OrderBy(grant => grant.PhaseExpiry is not null ? 2 :
                    grant.TurnExpiry is not null || grant.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            return new SkillGrant($"{sourceId}:{skillId}", skillId,
                existing?.SkillInstanceId ?? $"phase-shared:{owner.Seat}:{skillId}:" +
                    $"{expiry.TurnNumber}:{expiry.PhaseActorSeat}:{expiry.PhaseInstanceId}", sourceId,
                PhaseExpiry: expiry);
        }).ToArray();
        foreach (var grant in grants) owner.SkillGrants.Grant(grant);
        AdvanceEventRulesAndQueueFact(new ProgramPlayPhaseSkillsGrantedEvent(frame.Id, source,
            frame.GameplayHash, owner.Seat, expiry, Array.AsReadOnly(grants)));
    }

    private void CloseCurrentPlayPhaseSkillGrants(string reason)
    {
        if (!HasPlayPhaseSkillGrantCapability || _turnNumber <= 0 ||
            !IsValidPlayerSeat(_currentSeat) || _cardUseDebitPhaseInstanceId <= 0) return;
        var expiry = new SkillGrantPhaseExpiry(_turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId);
        ClosePlayPhaseSkillGrants(expiry, reason);
    }

    private void ExpirePlayPhaseSkillGrantsForEndedTurn(int turnNumber)
    {
        if (!HasPlayPhaseSkillGrantCapability) return;
        var expiries = PhaseSkillGrantExpiries().Where(expiry => expiry.TurnNumber == turnNumber).ToArray();
        if (expiries.Length == 0) return;
        if (turnNumber <= 0 || !CompleteProgramEventHistory().OfType<TurnEndedEvent>()
                .Any(ended => ended.TurnNumber == turnNumber))
            throw new InvalidOperationException("Play-phase fallback expiry requires its real turn-ended fact.");
        foreach (var expiry in expiries) ClosePlayPhaseSkillGrants(expiry, "turn-ended");
    }

    private void ExpireAllPlayPhaseSkillGrants(string reason)
    {
        if (!HasPlayPhaseSkillGrantCapability) return;
        foreach (var expiry in PhaseSkillGrantExpiries()) ClosePlayPhaseSkillGrants(expiry, reason);
    }

    private SkillGrantPhaseExpiry[] PhaseSkillGrantExpiries() => _players
        .SelectMany(player => player.SkillGrants.Grants)
        .Select(grant => grant.PhaseExpiry).OfType<SkillGrantPhaseExpiry>().Distinct()
        .OrderBy(expiry => expiry.TurnNumber).ThenBy(expiry => expiry.PhaseActorSeat)
        .ThenBy(expiry => expiry.PhaseInstanceId).ToArray();

    private void ClosePlayPhaseSkillGrants(SkillGrantPhaseExpiry expiry, string reason)
    {
        var due = _players.OrderBy(player => player.Seat).SelectMany(player => player.SkillGrants.Grants
            .Where(grant => grant.PhaseExpiry == expiry)
            .Select(grant => (Recipient: player, Grant: grant))).ToArray();
        // Old catalogs and repeated boundary callbacks produce no new events.
        if (due.Length == 0) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!CompleteProgramEventHistory().OfType<ProgramPlayPhaseSkillBoundaryClosedEvent>()
                .Any(closed => closed.Expiry == expiry))
            AdvanceEventRulesAndQueueFact(new ProgramPlayPhaseSkillBoundaryClosedEvent(expiry, reason));
        foreach (var (recipient, grant) in due)
        {
            if (!recipient.SkillGrants.RemoveGrant(grant.GrantId))
                throw new InvalidOperationException("A Play-phase expiry lost its exact independent grant source.");
            AdvanceEventRulesAndQueueFact(new ProgramPlayPhaseSkillGrantExpiredEvent(recipient.Seat, grant, reason));
        }
    }
}
