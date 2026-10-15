using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record BeiMiHuTurnPreludeFrame(
    long Id, BeiMiHuBeforeTurnProof Proof, bool RoundAdvanced, int Stage = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? WindowId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.BeiMiHuTurnPrelude, Step);

public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeiMiHuBeforeTurnProof? BeforeTurnPreludeProof { get; init; }
}

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeiMiHuBeforeTurnProof? BeforeTurnPreludeProof { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QingzhongPlayPhaseKey? QingzhongEndingPhase { get; init; }
}

public sealed partial record PlayPhaseStartingBoundaryFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QingzhongPlayPhaseKey? QingzhongStartingPhase { get; init; }
}

public sealed partial class GameEngine
{
    private bool HasBeiMiHuPreludeBindings(CharacterState player, SkillProgramTriggerWindow window,
        SkillProgramEffectOp operation) => player.IsAlive && GetSkillBindingShard(player).GetInstanceTriggers(window)
            .Any(binding => ProgramInstructionResolver.Default.Features(binding.Trigger).HasOperation(operation));

    private bool TryBeginBeiMiHuActualTurnPrelude(CharacterState owner)
    {
        if (!HasBeiMiHuPreludeBindings(owner, SkillProgramTriggerWindow.TurnBeforeStart,
                SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn) &&
            !_players.Any(player => HasBeiMiHuPreludeBindings(player, SkillProgramTriggerWindow.RoundStarting,
                SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart)) &&
            !(_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn) &&
                HasBeiMiHuPreludeBindings(owner, SkillProgramTriggerWindow.TurnBeforeStart,
                    SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn) && SimaHuiOwnTurns(owner.Seat) == 0)) return false;
        if (_pendingDecision is not null || _resolutionStack.Count != 0 || owner.Seat != _currentSeat)
            throw new InvalidOperationException("Before-turn work requires a clean actual turn boundary.");
        var oldRound = _roundNumber;
        BeginRoundForTurn(owner);
        var events = CompleteProgramEventHistory().ToArray();
        var roundIndex = Array.FindLastIndex(events, item => item is RoundStartedEvent round && round.RoundNumber == _roundNumber);
        if (roundIndex < 0 || _roundNumber < 1)
            throw new InvalidOperationException("Before-turn work requires the actual round-start fact.");
        var id = ++_resolutionSequence;
        var proof = new BeiMiHuBeforeTurnProof(id, checked(_turnNumber + 1), _roundNumber, owner.Seat, roundIndex + 1L);
        PushRuntimeFrame(new BeiMiHuTurnPreludeFrame(id, proof, _roundNumber != oldRound));
        AdvanceRuntimeFrame(id);
        return true;
    }

    private bool ValidBeiMiHuBeforeTurnPrelude(BeiMiHuBeforeTurnProof proof)
    {
        if (_resolutionStack.FirstOrDefault() is not BeiMiHuTurnPreludeFrame parent || parent.Id != proof.PreludeFrameId ||
            parent.Proof != proof || proof.UpcomingOwnerSeat != _currentSeat || proof.UpcomingTurnNumber != _turnNumber + 1 ||
            proof.ActualRoundNumber != _roundNumber || proof.RoundStartedEventSequence < 1 || parent.Stage is < 0 or > 2)
            return false;
        var ordinal = proof.RoundStartedEventSequence - 1;
        var events = CompleteProgramEventHistory().ToArray();
        if (ordinal > int.MaxValue || events.ElementAtOrDefault((int)ordinal) is not RoundStartedEvent round ||
            round.RoundNumber != proof.ActualRoundNumber ||
            ordinal != Array.FindLastIndex(events, item => item is RoundStartedEvent latest && latest.RoundNumber == proof.ActualRoundNumber)) return false;
        var lastTurn = Array.FindLastIndex(events, item => item is TurnStartedEvent);
        // BeginRoundForTurn never advances a round for an Extra turn. A new
        // normal round belongs to this prelude only when its actual fact follows
        // the preceding turn and names this upcoming actor. If that actor died,
        // the next living actor can reuse the round without claiming its start.
        var beganThisRound = _turnProgression.Kind == ActualTurnKind.Normal && ordinal > lastTurn && round.ActorSeat == proof.UpcomingOwnerSeat;
        return parent.RoundAdvanced == beganThisRound;
    }

    private bool ValidBeiMiHuPreludeLifecycleWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if (window.BeforeTurnPreludeProof is not { } proof || !ValidBeiMiHuBeforeTurnPrelude(proof) ||
            _resolutionStack.FirstOrDefault() is not BeiMiHuTurnPreludeFrame parent || parent.WindowId != window.Id || window.OwnerSeat != proof.UpcomingOwnerSeat ||
            window.CandidateIndex < 0 || window.CandidateIndex > window.Candidates.Count) return false;
        return parent.Stage switch
        {
            0 => parent.RoundAdvanced && window.Window == SkillProgramTriggerWindow.RoundStarting &&
                window.Continuation == ProgramLifecycleContinuation.BeiMiHuPreludeRound,
            1 => window.Window == SkillProgramTriggerWindow.TurnBeforeStart &&
                window.Continuation == ProgramLifecycleContinuation.BeiMiHuPreludeOwn,
            _ => false
        };
    }

    private bool HasBeiMiHuTurnPreludeBoundary()
    {
        if (_resolutionStack.FirstOrDefault() is not BeiMiHuTurnPreludeFrame parent) return false;
        if (!ValidBeiMiHuBeforeTurnPrelude(parent.Proof))
            throw new InvalidOperationException("The before-turn parent lost its upcoming turn and actual round receipt.");
        var windows = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
            .Where(window => window.BeforeTurnPreludeProof is not null).ToArray();
        if (windows.Length > 1 || windows.Length == 1 && !ValidBeiMiHuPreludeLifecycleWindow(windows[0]))
            throw new InvalidOperationException("The before-turn child lost its exact typed parent return.");
        return true;
    }

    private void ContinueBeiMiHuTurnPrelude()
    {
        if (_resolutionStack.LastOrDefault() is not BeiMiHuTurnPreludeFrame parent ||
            !ValidBeiMiHuBeforeTurnPrelude(parent.Proof) || _pendingDecision is not null)
            throw new InvalidOperationException("Before-turn progression lost its active typed parent.");
        while (parent.Stage < 2)
        {
            var isRound = parent.Stage == 0;
            var window = isRound ? SkillProgramTriggerWindow.RoundStarting : SkillProgramTriggerWindow.TurnBeforeStart;
            var operation = isRound ? SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart :
                SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn;
            if (_winner == Winner.None && _players[parent.Proof.UpcomingOwnerSeat].IsAlive && (!isRound || parent.RoundAdvanced))
            {
                var participants = _players.Where(player => player.IsAlive && (isRound || player.Seat == parent.Proof.UpcomingOwnerSeat)).ToArray();
                var facts = participants.ToDictionary(player => player.Seat, CaptureProgramTriggerFacts);
                var candidates = participants.SelectMany(player => CollectEligibleProgramTriggerCandidates(player, window, facts[player.Seat]))
                    .Where(candidate => ProgramInstructionResolver.Default.Features(GetProgramTrigger(candidate)).HasOperation(operation) ||
                        !isRound && ProgramInstructionResolver.Default.Features(GetProgramTrigger(candidate)).HasOperation(SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn) &&
                        SimaHuiOwnTurns(candidate.OwnerSeat) == 0)
                    .OrderBy(candidate => (candidate.OwnerSeat - parent.Proof.UpcomingOwnerSeat + _players.Count) % _players.Count)
                    .ThenByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
                if (candidates.Length != 0)
                {
                    var child = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, parent.Proof.UpcomingOwnerSeat,
                        window, candidates, isRound ? ProgramLifecycleContinuation.BeiMiHuPreludeRound : ProgramLifecycleContinuation.BeiMiHuPreludeOwn,
                        facts[parent.Proof.UpcomingOwnerSeat])
                    { ParticipantFacts = facts, BeforeTurnPreludeProof = parent.Proof };
                    ReplaceRuntimeTop(parent with { WindowId = child.Id });
                    PushRuntimeFrame(child);
                    AdvanceRuntimeFrame(child.Id);
                    return;
                }
            }
            ReplaceRuntimeTop(parent = parent with { Stage = parent.Stage + 1 });
        }
        var owner = _players[parent.Proof.UpcomingOwnerSeat];
        PopResolutionFrame(parent.Id, ResolutionFrameKind.BeiMiHuTurnPrelude);
        if (_winner != Winner.None) return;
        if (!owner.IsAlive) { BeginTurn(); return; }
        BeginTurnAfterBeforeTurnPrelude(owner);
    }

    private void ReturnBeiMiHuTurnPreludeWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!ValidBeiMiHuPreludeLifecycleWindow(window) || window.CandidateIndex != window.Candidates.Count ||
            _resolutionStack.LastOrDefault() is not BeiMiHuTurnPreludeFrame parent)
            throw new InvalidOperationException("Completed before-turn work lost its singular parent return.");
        ReplaceRuntimeTop(parent with { Stage = parent.Stage + 1, WindowId = null });
        AdvanceRuntimeFrame(parent.Id);
    }
}
