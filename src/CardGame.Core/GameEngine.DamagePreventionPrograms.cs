namespace CardGame.Core;

/// <summary>Generic before-damage program window. It contains no character or skill ids.</summary>
public sealed partial class GameEngine
{
    private bool TryBeginBeforeDamageProgramWindowForAttack(
        AttackResolution attack,
        int amount,
        DamageNature nature)
    {
        if (attack.BeforeDamageProgramsResolved || amount <= 0) return false;
        return TryBeginBeforeDamageProgramWindow(
            attack.SourceSeat,
            attack.TargetSeat,
            amount,
            nature,
            BeforeDamageProgramContinuation.Attack);
    }

    private bool TryBeginBeforeDamageProgramWindowForGanglie(DamageSkillResolution ganglie)
    {
        if (ganglie.BeforeDamageProgramsResolved) return false;
        return TryBeginBeforeDamageProgramWindow(
            ganglie.OwnerSeat,
            ganglie.SourceSeat,
            amount: 1,
            DamageNature.Normal,
            BeforeDamageProgramContinuation.Ganglie);
    }

    private bool TryBeginBeforeDamageProgramWindow(
        int sourceSeat,
        int targetSeat,
        int amount,
        DamageNature nature,
        BeforeDamageProgramContinuation continuation)
    {
        if (_rulesVersion < 138 || _contentRegistry is null || amount <= 0 ||
            !IsValidPlayerSeat(sourceSeat) || !IsValidPlayerSeat(targetSeat) ||
            !_players[targetSeat].IsAlive)
            return false;

        var target = _players[targetSeat];
        var candidates = _players
            .Where(owner => owner.IsAlive && owner.Seat != targetSeat)
            .SelectMany(owner =>
            {
                var facts = CaptureProgramTriggerFacts(owner) with { EventTargetHp = target.Hp };
                return CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.BeforeDamageApplied)
                    .Where(candidate => GetProgramTrigger(candidate).Condition.Evaluate(
                        facts, candidate.SkillId, candidate.SkillInstanceId))
                    .Select(candidate => new BeforeDamageProgramCandidate(candidate, facts));
            })
            .OrderByDescending(item => item.Candidate.Priority)
            .ThenBy(item => (item.Candidate.OwnerSeat - targetSeat + _playerCount) % _playerCount)
            .ThenBy(item => item.Candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.BindingId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return false;

        var parent = _resolutionStack.LastOrDefault() ??
            throw new InvalidOperationException("Before-damage programs require an active parent frame.");
        var frame = new BeforeDamageProgramWindowFrame(
            ++_resolutionSequence,
            parent.Id,
            sourceSeat,
            targetSeat,
            amount,
            nature,
            continuation,
            Array.AsReadOnly(candidates));
        _resolutionStack.Add(frame);
        ContinueBeforeDamageProgramWindow();
        return true;
    }

    private ProgramSkillWindowContext CreateBeforeDamageProgramContext(
        BeforeDamageProgramWindowFrame frame,
        BeforeDamageProgramCandidate item) =>
        new(
            SkillProgramTriggerWindow.BeforeDamageApplied,
            frame.Id,
            item.Candidate.OwnerSeat,
            SourceSeat: frame.SourceSeat,
            TargetSeat: frame.TargetSeat,
            Amount: frame.Amount,
            OccurrenceIndex: item.Candidate.OccurrenceIndex,
            Facts: item.Facts);

    private void ContinueBeforeDamageProgramWindow()
    {
        while (_resolutionStack.LastOrDefault() is BeforeDamageProgramWindowFrame frame)
        {
            if (frame.Prevented || frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.BeforeDamageProgramWindow);
                ResumeAfterBeforeDamageProgramWindow(frame);
                return;
            }

            var item = frame.Candidates[frame.CandidateIndex];
            var context = CreateBeforeDamageProgramContext(frame, item);
            if (!CanRunProgramTrigger(item.Candidate, context))
            {
                AdvanceBeforeDamageProgramCandidate(frame, activated: false, completed: false);
                continue;
            }

            var trigger = GetProgramTrigger(item.Candidate);
            if (trigger.Optional)
            {
                _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                ExposeProgramTriggerDecision(item.Candidate, context);
                return;
            }
            BeginProgramBinding(item.Candidate, context);
            return;
        }
    }

    private void AdvanceBeforeDamageProgramCandidate(
        BeforeDamageProgramWindowFrame frame,
        bool activated,
        bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not BeforeDamageProgramWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The before-damage program cursor is no longer current.");
        var candidate = current.Candidates[current.CandidateIndex].Candidate;
        QueueGameEvent(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.BeforeDamageApplied,
            activated,
            completed));
        _resolutionStack[^1] = current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        };
    }

    private void PreventProgramCurrentDamage(ProgramSkillFrame program)
    {
        if (program.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                ParentFrameId: var parentFrameId
            } || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not BeforeDamageProgramWindowFrame frame ||
            frame.Id != parentFrameId || frame.Prevented)
            throw new InvalidOperationException("Damage prevention lost its before-damage parent window.");

        _resolutionStack[^2] = frame with { Prevented = true };
        QueueGameEvent(new ProgramDamagePreventedEvent(
            frame.Id,
            program.SkillId,
            program.TriggerId!,
            program.OwnerSeat,
            frame.SourceSeat,
            frame.TargetSeat,
            frame.Amount));
        AddLog(
            "DamagePrevented",
            $"{_players[program.OwnerSeat].Name} 防止了 {_players[frame.TargetSeat].Name} 即将受到的 {frame.Amount} 点伤害。",
            program.OwnerSeat,
            frame.TargetSeat);
    }

    private void ResumeAfterBeforeDamageProgramWindow(BeforeDamageProgramWindowFrame frame)
    {
        switch (frame.Continuation)
        {
            case BeforeDamageProgramContinuation.Attack:
            {
                var attack = _pendingAttack ??
                    throw new InvalidOperationException("The before-damage attack continuation is unavailable.");
                if (attack.SourceSeat != frame.SourceSeat || attack.TargetSeat != frame.TargetSeat)
                    throw new InvalidOperationException("The before-damage attack participants changed.");
                attack.MarkBeforeDamageProgramsResolved();
                if (frame.Prevented)
                {
                    CompleteAttack(attack);
                    return;
                }
                if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
                return;
            }
            case BeforeDamageProgramContinuation.Ganglie:
            {
                var ganglie = _pendingDamageSkill ??
                    throw new InvalidOperationException("The before-damage Ganglie continuation is unavailable.");
                if (ganglie.OwnerSeat != frame.SourceSeat || ganglie.SourceSeat != frame.TargetSeat)
                    throw new InvalidOperationException("The before-damage Ganglie participants changed.");
                ganglie.BeforeDamageProgramsResolved = true;
                if (frame.Prevented) CompleteGangliePunishment(ganglie);
                else ApplyGangliePunishmentDamage(ganglie);
                return;
            }
            default:
                throw new InvalidOperationException("Unsupported before-damage program continuation.");
        }
    }
}
