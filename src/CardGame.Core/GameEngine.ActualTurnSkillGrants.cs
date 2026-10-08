namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void GrantProgramTurnSkills(ProgramSkillFrame frame, int targetSeat,
        IReadOnlyList<string> skillIds, SkillProgramTurnSkillExpiry? expiry)
    {
        var active = GetActiveProgramFrame(frame.Id);
        ValidateProgramTurnEffectGrant(active);
        if (active != frame || active.InstructionIndex <= 0 || active.PendingMovementContinuation is not null ||
            active.SelectedCardPayment is not null && active.SelectedCardPaymentResult is null ||
            !IsValidPlayerSeat(targetSeat) ||
            expiry is not null && expiry != SkillProgramTurnSkillExpiry.ActualTurnEnd)
            throw new InvalidOperationException("A participant turn grant lost its exact active instruction or recipient.");

        var effect = ProgramInstructionResolver.Default.Resolve(active,
            _contentRegistry.GetSkill(active.SkillId).Program!)
            .GetPausedInstruction(active.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.GrantTurnSkills || effect.TurnSkillExpiry != expiry ||
            !effect.SkillIds.SequenceEqual(skillIds, StringComparer.Ordinal) ||
            targetSeat != (effect.TargetReference is { } reference
                ? ResolveProgramParticipant(active, reference) : active.OwnerSeat))
            throw new InvalidOperationException("A participant turn grant no longer matches its configured operation.");

        if (effect.TargetReference is { Kind: ProgramParticipantRef.Actor })
            ValidateTurnSkillGrantActor(active, targetSeat);
        else if (effect.TargetReference is not null)
            throw new InvalidOperationException("A turn skill grant requires its owner or exact card-action actor.");

        if (_turnProgression.TurnNumber != _turnNumber || _turnProgression.OwnerSeat != _currentSeat ||
            _phase is TurnPhase.NotStarted or TurnPhase.Finished ||
            CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(ended =>
                ended.TurnNumber == _turnNumber && ended.ActorSeat == _currentSeat))
            throw new InvalidOperationException("A turn skill grant requires an actual ongoing turn.");

        // A paid movement child may have killed its recipient. The payment stays paid;
        // a dead participant receives no new capability and opens no second cost window.
        if (!_players[targetSeat].IsAlive || !_players[active.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId)) return;

        var recipient = _players[targetSeat];
        var granted = skillIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var skillId in granted) _ = _contentRegistry.GetSkill(skillId);
        var bindingId = GetProgramBindingId(active);
        var sourceId = $"turn:{_turnNumber}:{_currentSeat}:donor:{active.OwnerSeat}:{active.SkillId}:" +
            $"{active.SkillInstanceId}:{bindingId}:frame:{active.Id}:effect:{active.InstructionIndex - 1}";
        var turnExpiry = expiry == SkillProgramTurnSkillExpiry.ActualTurnEnd
            ? new SkillGrantTurnExpiry(_turnNumber, _currentSeat) : null;
        foreach (var skillId in granted)
        {
            // Sources own separate grants, while one effective skill keeps one runtime
            // instance. In particular, expiry cannot remove the original printed skill.
            var existing = GetSkillBindingShard(recipient).ActiveGrants.Where(grant => grant.SkillId == skillId)
                .OrderBy(grant => grant.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            var instanceId = existing?.SkillInstanceId ?? $"turn-shared:{recipient.Seat}:{skillId}";
            var grantId = $"{sourceId}:{skillId}";
            recipient.SkillGrants.Grant(new SkillGrant(grantId, skillId, instanceId, sourceId,
                TurnExpiry: turnExpiry));
        }
        AdvanceEventRulesAndQueueFact(new ProgramTurnSkillsGrantedEvent(active.Id, active.SkillId,
            bindingId, active.OwnerSeat, Array.AsReadOnly(granted))
        { RecipientSeat = recipient.Seat, GrantSourceId = sourceId, TurnExpiry = turnExpiry });
    }

    private void ValidateTurnSkillGrantActor(ProgramSkillFrame frame, int targetSeat)
    {
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramCardTriggerWindowFrame parent ||
            frame.WindowContext is not { CardUse: { } use } context || parent.Id != context.ParentFrameId ||
            context.OwnerSeat != frame.OwnerSeat || context.Window != GetCardActionWindow(parent) ||
            use.ParentCardUseFrameId != parent.ParentFrameId || use.CardActionId != parent.Action.ActionId ||
            use.ActorSeat != parent.Action.ActorSeat || use.EffectiveKind != parent.Action.EffectiveKind ||
            targetSeat != use.ActorSeat || context.SourceSeat != use.ActorSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count)
            throw new InvalidOperationException("A turn skill beneficiary requires its exact suspended card-action parent.");
        var candidate = parent.Candidates[parent.CandidateIndex];
        if (candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.SkillInstanceId != frame.SkillInstanceId || candidate.TriggerId != frame.TriggerId ||
            candidate.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("A turn skill beneficiary lost its exact card-action candidate.");
    }

    // The caller runs this only after queuing the matching actual TurnEnded fact,
    // before any deferred after-turn-ended program can observe an expired grant.
    private void ExpireActualTurnSkillGrants(int turnNumber, int turnOwnerSeat)
    {
        var expiry = new SkillGrantTurnExpiry(turnNumber, turnOwnerSeat);
        var due = _players.SelectMany(recipient => recipient.SkillGrants.Grants
            .Where(grant => grant.TurnExpiry == expiry).Select(grant => (Recipient: recipient, Grant: grant))).ToArray();
        if (due.Length == 0) return;
        if (turnNumber <= 0 || !IsValidPlayerSeat(turnOwnerSeat) ||
            !CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(ended =>
                ended.TurnNumber == turnNumber && ended.ActorSeat == turnOwnerSeat))
            throw new InvalidOperationException("Actual-turn skill grants expire only after their exact turn-ended fact.");
        foreach (var (recipient, grant) in due)
        {
            recipient.SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new ProgramTurnSkillGrantExpiredEvent(recipient.Seat, grant.GrantId,
                grant.SkillId, grant.SkillInstanceId, grant.SourceId, grant.TurnExpiry!));
        }
    }
}
