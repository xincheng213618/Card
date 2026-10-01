namespace CardGame.Core;

/// <summary>Generic owner-death program boundary. It contains no character or skill ids.</summary>
public sealed partial class GameEngine
{
    private void ChangeProgramAttributedMarker(
        ProgramSkillFrame frame,
        ProgramParticipantReference targetReference,
        PlayerMarkerKind marker,
        int amount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (targetReference.Kind == ProgramParticipantRef.EventSource)
        {
            if (active.WindowContext is not
                { Window: SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                    SkillProgramTriggerWindow.AfterDamageApplied } || amount is < 1 or > 20)
                throw new InvalidOperationException(
                    "Attributed marker mutation requires an after-damage event source.");
        }
        else if (targetReference.Kind == ProgramParticipantRef.Owner)
        {
            if (active.WindowContext is not
                { Window: SkillProgramTriggerWindow.GameStarting or SkillProgramTriggerWindow.AfterDamageApplied or
                    SkillProgramTriggerWindow.CardsMoved } || amount is < 1 or > 20)
                throw new InvalidOperationException(
                    "Owner-attributed marker mutation requires an after-damage or card-movement trigger.");
        }
        else
        {
            throw new InvalidOperationException("Attributed marker mutation requires an owner or event source.");
        }
        var targetSeat = ResolveProgramParticipant(active, targetReference);
        if (!IsValidPlayerSeat(targetSeat) ||
            targetSeat == active.OwnerSeat && targetReference.Kind != ProgramParticipantRef.Owner)
            return;

        var target = _players[targetSeat];
        var sourceKey = (marker, active.OwnerSeat);
        target.MarkerSourceCounts[sourceKey] = target.MarkerSourceCounts.GetValueOrDefault(sourceKey) + amount;
        var count = target.Markers.GetValueOrDefault(marker) + amount;
        target.Markers[marker] = count;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(
            active.Id,
            targetSeat,
            marker,
            Delta: amount,
            Count: count,
            SkillOwnerSeat: active.OwnerSeat,
            Reason: $"skill-program.{active.SkillId}.{GetProgramBindingId(active)}.marker"));
    }

    private SkillProgramStepOutcome CauseProgramDeathUnlessBoundCardKind(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<CardKind> excludedCardKinds)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } ||
            active.SelectedTargetSeats is not [var targetSeat])
            throw new InvalidOperationException("Configured direct death requires an owner-death selected target.");
        var binding = GetProgramCardSet(active, sourceBind);
        if (binding.CardIds.Count == 0)
            return SkillProgramStepOutcome.Continue;
        if (binding.CardIds.Count != 1 || binding.SourceLocations.Count != 1)
            throw new InvalidOperationException("Configured direct death requires exactly one bound judgment card.");
        var location = binding.SourceLocations[0];
        if (_cardZones.GetLocation(binding.CardIds[0]) != location)
            throw new InvalidOperationException("The bound judgment card left its frozen location.");
        var card = _cardZones.CardsAt(location).Single(item => item.Id == binding.CardIds[0]);
        MoveCard(
            card,
            location,
            CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{active.SkillId}.{GetProgramBindingId(active)}.judgment-finish"));
        var target = _players[targetSeat];
        if (excludedCardKinds.Contains(card.Kind) || !target.IsAlive || _winner != Winner.None)
            return SkillProgramStepOutcome.Continue;

        AdvanceEventRulesAndQueueFact(new ProgramSkillCauseDeathDeclaredEvent(
            active.Id,
            active.SkillId,
            GetProgramBindingId(active),
            active.OwnerSeat,
            targetSeat));
        AddLog(
            "DirectDeath",
            $"{target.Name} 的判定结果不满足【{_contentRegistry!.GetSkill(active.SkillId).Name}】的存活条件，其直接死亡。",
            active.OwnerSeat,
            targetSeat);
        BeginPlayerDeath(
            active.Id,
            target,
            killer: null,
            attack: null,
            dying: null,
            causingProgramSkillFrameId: active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private DeathFrame GetCurrentDeathFrame(long expectedId)
    {
        var death = _resolutionStack.OfType<DeathFrame>().LastOrDefault();
        if (death is null || death.Id != expectedId)
            throw new InvalidOperationException("The continued death resolution is not current.");
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == death.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != death.ParentFrameId ||
            (death.ReturnKind switch
            {
                DeathReturnKind.Dying => _resolutionStack[index - 1] is not DyingFrame,
                DeathReturnKind.ProgramSkill => _resolutionStack[index - 1] is not ProgramSkillFrame,
                _ => true
            }))
            throw new InvalidOperationException("The death frame lost its typed return parent.");
        return death;
    }

    private void UpdateDeathFrame(long frameId, Func<DeathFrame, DeathFrame> update)
    {
        var death = GetCurrentDeathFrame(frameId);
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == death.Id);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, update(death));
    }

    private bool TryBeginOwnerDiedProgramWindow(DeathFrame death, CharacterState owner)
    {
        if (_winner != Winner.None || owner.IsAlive)
            return false;

        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner,
            SkillProgramTriggerWindow.OwnerDied,
            facts);
        if (candidates.Count == 0)
            return false;

        var frame = new ProgramDeathTriggerWindowFrame(
            ++_resolutionSequence,
            death.Id,
            owner.Seat,
            death.KillerSeat,
            candidates,
            facts);
        PushRuntimeFrame(frame);
        AdvanceRuntimeTop<ProgramDeathTriggerWindowFrame>();
        return true;
    }

    private ProgramSkillWindowContext CreateOwnerDiedProgramContext(
        ProgramDeathTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate) =>
        new(
            SkillProgramTriggerWindow.OwnerDied,
            frame.Id,
            frame.OwnerSeat,
            SourceSeat: frame.KillerSeat,
            TargetSeat: frame.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: frame.Facts);

    private void ContinueOwnerDiedProgramWindowCore()
    {
        while (_resolutionStack.LastOrDefault() is ProgramDeathTriggerWindowFrame frame)
        {
            var death = GetCurrentDeathFrame(frame.DeathFrameId);
            if (death.VictimSeat != frame.OwnerSeat)
                throw new InvalidOperationException("The owner-death program window lost its death parent.");

            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramDeathTriggerWindow);
                UpdateDeathFrame(death.Id, current => current with { OwnerDiedProgramsResolved = true });
                ContinueDeathResolution(death.Id);
                return;
            }

            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = CreateOwnerDiedProgramContext(frame, candidate);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceOwnerDiedProgramCandidate(frame, candidate, activated: false, completed: false);
                continue;
            }

            var trigger = GetProgramTrigger(candidate);
            if (trigger.Optional)
            {
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }

            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvanceOwnerDiedProgramCandidate(
        ProgramDeathTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramDeathTriggerWindowFrame current ||
            current.Id != frame.Id ||
            current.Candidates[current.CandidateIndex] != candidate)
            throw new InvalidOperationException("The owner-death program cursor changed before it advanced.");

        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.OwnerDied,
            activated,
            completed));
        AdvanceOwnerDiedProgramCursor(current);
    }

    private void AdvanceOwnerDiedProgramCursor(ProgramDeathTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramDeathTriggerWindowFrame current ||
            current.Id != frame.Id)
            throw new InvalidOperationException("The owner-death program cursor is not active.");
        ReplaceRuntimeTop(current with { CandidateIndex = current.CandidateIndex + 1 });
    }

    private void AssertOwnerDiedProgramInvariant()
    {
        var death = _resolutionStack.OfType<DeathFrame>().LastOrDefault() ??
            throw new InvalidOperationException("An owner-death program invariant requires an active death.");

        var window = _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().LastOrDefault();
        if (window is null)
            throw new InvalidOperationException("An unfinished death has no owner-death program window.");
        if (window.DeathFrameId != death.Id ||
            window.OwnerSeat != death.VictimSeat ||
            window.KillerSeat != death.KillerSeat ||
            window.CandidateIndex < 0 ||
            window.CandidateIndex >= window.Candidates.Count ||
            death.OwnerDiedProgramsResolved ||
            _players[window.OwnerSeat].IsAlive)
            throw new InvalidOperationException("The owner-death program window has an invalid parent or cursor.");

        var windowIndex = _resolutionStack.FindLastIndex(frame =>
            frame is ProgramDeathTriggerWindowFrame candidate && candidate.Id == window.Id);
        var programs = _resolutionStack
            .Skip(windowIndex + 1)
            .OfType<ProgramSkillFrame>()
            .Where(program => program.WindowContext is
            {
                Window: SkillProgramTriggerWindow.OwnerDied,
                ParentFrameId: var parentFrameId
            } && parentFrameId == window.Id)
            .ToArray();
        if (programs is [var program])
        {
            if (program.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context ||
                context.ParentFrameId != window.Id ||
                program.OwnerSeat != window.OwnerSeat)
                throw new InvalidOperationException("The active owner-death program binding lost its window.");
            return;
        }
        if (programs.Length > 1)
            throw new InvalidOperationException("An owner-death program window has multiple active bindings.");

        if (_resolutionStack.LastOrDefault() is not ProgramDeathTriggerWindowFrame top || top.Id != window.Id ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: var responder } ||
            responder != window.OwnerSeat)
            throw new InvalidOperationException("The owner-death program window lost its optional trigger prompt.");
    }

    private bool TryBeginKillDiedProgramWindow(DeathFrame death)
    {
        if (_winner != Winner.None)
            return false;

        var collected = new List<(ProgramTriggerCandidate Candidate, SkillProgramTriggerFacts Facts)>();
        foreach (var player in _players.Where(item => item.IsAlive))
        {
            var facts = CaptureProgramTriggerFacts(player) with
            {
                DeathKillerIsOwner = death.KillerSeat == player.Seat,
                DeathVictimCleanupCardCount = death.CleanedUpCardIds.Count,
                DeathExtinguishedFaction = GetEffectiveFactionId(_players[death.VictimSeat]) is { } faction &&
                    !_players.Any(other => other.IsAlive && GetEffectiveFactionId(other) == faction)
            };
            foreach (var candidate in CollectEligibleProgramTriggerCandidates(
                player, SkillProgramTriggerWindow.CharacterDied, facts))
                collected.Add((candidate, facts));
        }

        if (collected.Count == 0)
            return false;

        var ordered = collected
            .OrderBy(item => (item.Candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count)
            .ThenByDescending(item => item.Candidate.Priority)
            .ThenBy(item => item.Candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.BindingId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.OccurrenceIndex)
            .ToArray();
        var frameId = ++_resolutionSequence;
        var frame = new ProgramKillTriggerWindowFrame(
            frameId,
            death.Id,
            death.VictimSeat,
            death.KillerSeat,
            ordered.Select(item => item.Candidate).ToArray(),
            ordered.Select(item => new ProgramSkillWindowContext(
                SkillProgramTriggerWindow.CharacterDied,
                frameId,
                item.Candidate.OwnerSeat,
                SourceSeat: death.KillerSeat,
                TargetSeat: death.VictimSeat,
                OccurrenceIndex: item.Candidate.OccurrenceIndex,
                Facts: item.Facts)).ToArray());
        PushRuntimeFrame(frame);
        AdvanceRuntimeTop<ProgramKillTriggerWindowFrame>();
        return true;
    }

    private void ContinueKillDiedProgramWindowCore()
    {
        while (_resolutionStack.LastOrDefault() is ProgramKillTriggerWindowFrame frame)
        {
            var death = GetCurrentDeathFrame(frame.DeathFrameId);

            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramDeathTriggerWindow);
                UpdateDeathFrame(death.Id, current => current with { KillerProgramsResolved = true });
                ContinueDeathResolution(death.Id);
                return;
            }

            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = frame.Contexts[frame.CandidateIndex];
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceKillDiedProgramCandidate(frame, candidate, activated: false, completed: false);
                continue;
            }

            var trigger = GetProgramTrigger(candidate);
            if (trigger.Optional)
            {
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }

            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvanceKillDiedProgramCandidate(
        ProgramKillTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramKillTriggerWindowFrame current ||
            current.Id != frame.Id ||
            current.Candidates[current.CandidateIndex] != candidate)
            throw new InvalidOperationException("The killer-death program cursor changed before it advanced.");

        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.CharacterDied,
            activated,
            completed));
        ReplaceRuntimeTop(current with { CandidateIndex = current.CandidateIndex + 1 });
    }

    private void AssertKillDiedProgramInvariant()
    {
        var death = _resolutionStack.OfType<DeathFrame>().LastOrDefault() ??
            throw new InvalidOperationException("A killer-death program invariant requires an active death.");

        var window = _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().LastOrDefault();
        if (window is null)
            throw new InvalidOperationException("An unfinished death has no killer-death program window.");
        if (window.DeathFrameId != death.Id ||
            window.CandidateIndex < 0 ||
            window.CandidateIndex >= window.Candidates.Count ||
            death.KillerProgramsResolved)
            throw new InvalidOperationException("The killer-death program window has an invalid parent or cursor.");

        var windowIndex = _resolutionStack.FindLastIndex(frame =>
            frame is ProgramKillTriggerWindowFrame candidate && candidate.Id == window.Id);
        var programs = _resolutionStack
            .Skip(windowIndex + 1)
            .OfType<ProgramSkillFrame>()
            .Where(program => program.WindowContext is
            {
                Window: SkillProgramTriggerWindow.CharacterDied,
                ParentFrameId: var parentFrameId
            } && parentFrameId == window.Id)
            .ToArray();
        if (programs is [var program])
        {
            if (program.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied } context ||
                context.ParentFrameId != window.Id)
                throw new InvalidOperationException("The active killer-death program binding lost its window.");
            return;
        }
        if (programs.Length > 1)
            throw new InvalidOperationException("A killer-death program window has multiple active bindings.");

        if (_resolutionStack.LastOrDefault() is not ProgramKillTriggerWindowFrame top || top.Id != window.Id ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: var responder } ||
            responder != top.Candidates[top.CandidateIndex].OwnerSeat)
            throw new InvalidOperationException("The killer-death program window lost its optional trigger prompt.");
    }

    private void ContinueDeathResolution(long deathFrameId)
    {
        var death = GetCurrentDeathFrame(deathFrameId);
        var owner = _players[death.VictimSeat];
        if (!death.OwnerDiedProgramsResolved && TryBeginOwnerDiedProgramWindow(death, owner)) return;
        if (!death.OwnerDiedProgramsResolved)
        {
            UpdateDeathFrame(death.Id, current => current with { OwnerDiedProgramsResolved = true });
            death = GetCurrentDeathFrame(death.Id);
        }
        if (!death.KillerProgramsResolved && TryBeginKillDiedProgramWindow(death)) return;
        if (!death.KillerProgramsResolved)
            UpdateDeathFrame(death.Id, current => current with { KillerProgramsResolved = true });
        CompleteDeathResolution(death.Id);
    }

    private void CompleteDeathResolution(long deathFrameId)
    {
        var death = GetCurrentDeathFrame(deathFrameId);

        ClearAttributedMarkerSources(_players[death.VictimSeat], death.Id);
        PopResolutionFrame(death.Id, ResolutionFrameKind.Death);

        if (_resolutionStack.LastOrDefault()?.Id != death.ParentFrameId)
            throw new InvalidOperationException("A death resolution lost its return parent.");
        if (death.ReturnKind == DeathReturnKind.Dying)
        {
            if (_resolutionStack[^1] is not DyingFrame ||
                ActiveDying is not { } dying || dying.FrameId != death.ParentFrameId)
                throw new InvalidOperationException("A death resolution lost its dying parent.");
            CompleteDyingAfterDeath(dying, survived: false);
            return;
        }
        if (death.ReturnKind == DeathReturnKind.ProgramSkill)
        {
            if (_resolutionStack[^1] is not ProgramSkillFrame)
                throw new InvalidOperationException("A death resolution lost its program parent.");
            AdvanceRuntimeProgram(death.ParentFrameId);
            return;
        }
        throw new InvalidOperationException("A death resolution has no continuation.");
    }

    private int GetMarkerSourceCount(CharacterState player, PlayerMarkerKind marker, int skillOwnerSeat) =>
        player.MarkerSourceCounts.GetValueOrDefault((marker, skillOwnerSeat));

    internal void ClaimProgramDeathCleanupCards(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId)
            throw new InvalidOperationException("A death-claim requires the active program frame.");
        var context = active.WindowContext ??
            throw new InvalidOperationException("A death-claim requires a trigger window context.");
        if (context.Window is not SkillProgramTriggerWindow.CharacterDied)
            throw new InvalidOperationException("A death-claim requires a characterDied window.");
        var window = _resolutionStack.OfType<ProgramKillTriggerWindowFrame>()
            .LastOrDefault(item => item.Id == context.ParentFrameId) ??
            throw new InvalidOperationException("A death-claim lost its killer-death window.");
        var windowIndex = _resolutionStack.FindLastIndex(item => item.Id == window.Id);
        var deathIndex = _resolutionStack.FindLastIndex(item => item.Id == window.DeathFrameId);
        if (deathIndex < 0 || deathIndex >= windowIndex ||
            _resolutionStack[deathIndex] is not DeathFrame death)
            throw new InvalidOperationException("A death-claim lost its death parent.");
        if (frame.OwnerSeat == death.VictimSeat)
            throw new InvalidOperationException("A death-claim cannot target its own death.");

        var claimable = death.CleanedUpCardIds
            .Where(cardId => _cardZones.GetLocation(cardId) == CardLocation.DiscardPile)
            .Select(cardId => _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == cardId))
            .ToArray();
        if (claimable.Length == 0) return;
        var reason = new CardMoveReason(
            $"skill-program.{frame.SkillId}.{GetProgramBindingId(active)}.claim-death-cleanup");
        MoveCards(claimable, CardLocation.DiscardPile, CardLocation.Hand(frame.OwnerSeat), reason);
    }

    private void ClearAttributedMarkerSources(CharacterState owner, long resolutionId)
    {
        foreach (var player in _players)
        {
            var attributed = player.MarkerSourceCounts
                .Where(item => item.Key.SkillOwnerSeat == owner.Seat)
                .ToArray();
            foreach (var item in attributed)
            {
                player.MarkerSourceCounts.Remove(item.Key);
                if (item.Value <= 0) continue;
                var total = player.Markers.GetValueOrDefault(item.Key.Marker);
                if (total < item.Value)
                    throw new InvalidOperationException("An attributed marker source exceeds its public total.");
                var remaining = total - item.Value;
                if (remaining == 0) player.Markers.Remove(item.Key.Marker);
                else player.Markers[item.Key.Marker] = remaining;
                AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(
                    resolutionId,
                    player.Seat,
                    item.Key.Marker,
                    Delta: -item.Value,
                    Count: remaining,
                    SkillOwnerSeat: owner.Seat,
                    Reason: "program.attributed-marker.death-clear"));
            }
        }
    }

}

public sealed record ProgramSkillCauseDeathDeclaredEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int SourceSeat,
    int TargetSeat) : IGameEvent;

public sealed record ProgramExtraTurnPendedEvent(
    long FrameId,
    string SkillId,
    int Seat) : IGameEvent;
