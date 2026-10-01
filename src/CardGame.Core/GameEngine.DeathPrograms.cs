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
        QueueGameEvent(new PlayerMarkerChangedEvent(
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

        QueueGameEvent(new ProgramSkillCauseDeathDeclaredEvent(
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

    private bool TryBeginOwnerDiedProgramWindow(DeathResolution death, CharacterState owner)
    {
        if (_winner != Winner.None || owner.IsAlive)
        {
            death.OwnerDiedProgramsResolved = true;
            return false;
        }

        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner,
            SkillProgramTriggerWindow.OwnerDied,
            facts);
        if (candidates.Count == 0)
        {
            death.OwnerDiedProgramsResolved = true;
            return false;
        }

        var frame = new ProgramDeathTriggerWindowFrame(
            ++_resolutionSequence,
            death.FrameId,
            owner.Seat,
            death.KillerSeat,
            candidates,
            facts);
        _resolutionStack.Add(frame);
        ContinueOwnerDiedProgramWindow();
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

    private void ContinueOwnerDiedProgramWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramDeathTriggerWindowFrame frame)
        {
            if (_pendingDeath is not { } death ||
                death.FrameId != frame.DeathFrameId ||
                death.VictimSeat != frame.OwnerSeat)
                throw new InvalidOperationException("The owner-death program window lost its death parent.");

            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramDeathTriggerWindow);
                death.OwnerDiedProgramsResolved = true;
                ContinueDeathResolution(death);
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

        QueueGameEvent(new ProgramBindingResolvedEvent(
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
        _resolutionStack[^1] = current with { CandidateIndex = current.CandidateIndex + 1 };
    }

    private void AssertOwnerDiedProgramInvariant()
    {
        var death = _pendingDeath ??
            throw new InvalidOperationException("An owner-death program invariant requires an active death.");
        var deathIndex = _resolutionStack.FindLastIndex(frame =>
            frame is DeathFrame deathFrame && deathFrame.Id == death.FrameId);
        if (deathIndex < 0 ||
            _resolutionStack[deathIndex] is not DeathFrame parent ||
            parent.VictimSeat != death.VictimSeat ||
            parent.KillerSeat != death.KillerSeat)
            throw new InvalidOperationException("An active death lost its public death frame.");

        var window = _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().LastOrDefault();
        if (window is null)
            throw new InvalidOperationException("An unfinished death has no owner-death program window.");
        if (window.DeathFrameId != death.FrameId ||
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

    private bool TryBeginKillDiedProgramWindow(DeathResolution death)
    {
        if (_winner != Winner.None)
        {
            death.KillerProgramsResolved = true;
            return false;
        }

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
        {
            death.KillerProgramsResolved = true;
            return false;
        }

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
            death.FrameId,
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
        _resolutionStack.Add(frame);
        ContinueKillDiedProgramWindow();
        return true;
    }

    private void ContinueKillDiedProgramWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramKillTriggerWindowFrame frame)
        {
            if (_pendingDeath is not { } death || death.FrameId != frame.DeathFrameId)
                throw new InvalidOperationException("The killer-death program window lost its death parent.");

            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramDeathTriggerWindow);
                death.KillerProgramsResolved = true;
                ContinueDeathResolution(death);
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

        QueueGameEvent(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.CharacterDied,
            activated,
            completed));
        _resolutionStack[^1] = current with { CandidateIndex = current.CandidateIndex + 1 };
    }

    private void AssertKillDiedProgramInvariant()
    {
        var death = _pendingDeath ??
            throw new InvalidOperationException("A killer-death program invariant requires an active death.");
        var deathIndex = _resolutionStack.FindLastIndex(frame =>
            frame is DeathFrame deathFrame && deathFrame.Id == death.FrameId);
        if (deathIndex < 0 ||
            _resolutionStack[deathIndex] is not DeathFrame parent ||
            parent.VictimSeat != death.VictimSeat ||
            parent.KillerSeat != death.KillerSeat)
            throw new InvalidOperationException("An active death lost its public death frame.");

        var window = _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().LastOrDefault();
        if (window is null)
            throw new InvalidOperationException("An unfinished death has no killer-death program window.");
        if (window.DeathFrameId != death.FrameId ||
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

    private void ContinueDeathResolution(DeathResolution death)
    {
        if (!ReferenceEquals(_pendingDeath, death))
            throw new InvalidOperationException("The continued death resolution is not current.");
        var owner = _players[death.VictimSeat];
        if (!death.OwnerDiedProgramsResolved && TryBeginOwnerDiedProgramWindow(death, owner)) return;
        death.OwnerDiedProgramsResolved = true;
        if (!death.KillerProgramsResolved && TryBeginKillDiedProgramWindow(death)) return;
        death.KillerProgramsResolved = true;
        CompleteDeathResolution(death);
    }

    private void CompleteDeathResolution(DeathResolution death)
    {
        if (!ReferenceEquals(_pendingDeath, death))
            throw new InvalidOperationException("The completed death resolution is not current.");

        ClearAttributedMarkerSources(_players[death.VictimSeat], death.FrameId);
        PopResolutionFrame(death.FrameId, ResolutionFrameKind.Death);
        _pendingDeath = death.Parent;

        if (death.Dying is not null)
        {
            CompleteDyingAfterDeath(death.Dying, survived: false);
            return;
        }
        if (death.CausingProgramSkillFrameId is { } programFrameId)
        {
            ContinueProgramSkill(programFrameId);
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
        var death = _pendingDeath;
        while (death is not null && death.FrameId != window.DeathFrameId)
            death = death.Parent;
        if (death is null)
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
                QueueGameEvent(new PlayerMarkerChangedEvent(
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

    private sealed class DeathResolution(
        long frameId,
        long parentFrameId,
        int victimSeat,
        int? killerSeat,
        DyingResolution? dying,
        long? causingProgramSkillFrameId,
        DeathResolution? parent)
    {
        public long FrameId { get; } = frameId;
        public long ParentFrameId { get; } = parentFrameId;
        public int VictimSeat { get; } = victimSeat;
        public int? KillerSeat { get; } = killerSeat;
        public DyingResolution? Dying { get; } = dying;
        public long? CausingProgramSkillFrameId { get; } = causingProgramSkillFrameId;
        public DeathResolution? Parent { get; } = parent;
        public bool OwnerDiedProgramsResolved { get; set; }
        public bool KillerProgramsResolved { get; set; }
        public IReadOnlyList<int> CleanedUpCardIds { get; set; } = [];
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
