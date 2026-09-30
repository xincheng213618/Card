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

    private bool TryBeginBeforeDamageProgramWindow(
        int sourceSeat,
        int targetSeat,
        int amount,
        DamageNature nature,
        BeforeDamageProgramContinuation continuation)
    {
        if (amount <= 0 ||
            !IsValidPlayerSeat(sourceSeat) || !IsValidPlayerSeat(targetSeat) ||
            !_players[targetSeat].IsAlive)
            return false;

        var target = _players[targetSeat];
        var candidates = _players
            .Where(owner => owner.IsAlive)
            .SelectMany(owner =>
            {
                var facts = CaptureProgramTriggerFacts(owner) with
                {
                    EventTargetHp = target.Hp,
                    DamageSourceGender = _pendingAttack?.IsDelayedJudgmentDamage != true &&
                        _contentRegistry.Skills.Values.Any(skill => skill.Program?.Triggers.Any(trigger =>
                            HasTriggerCondition(trigger.Condition, SkillProgramTriggerConditionKind.DamageSourceGenderIs)) == true)
                        ? _players[sourceSeat].Gender : null
                };
                return CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.BeforeDamageApplied)
                    .Where(candidate => GetProgramTrigger(candidate).Subject switch
                    {
                        SkillProgramTriggerSubject.DamageTarget => owner.Seat == targetSeat &&
                            (_pendingAttack?.DamageRedirected != true ||
                             !GetProgramTrigger(candidate).Effects.Any(effect =>
                                 effect.Op == SkillProgramEffectOp.RedirectCurrentDamage)),
                        SkillProgramTriggerSubject.DamageSource => owner.Seat == sourceSeat && owner.Seat != targetSeat,
                        SkillProgramTriggerSubject.Owner => owner.Seat != targetSeat,
                        _ => false
                    })
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

        var parentFrameId = _resolutionStack.LastOrDefault()?.Id ??
            (continuation == BeforeDamageProgramContinuation.Attack
                ? _pendingAttack?.ResolutionId : null) ??
            throw new InvalidOperationException("Before-damage programs require an attack continuation.");
        var frame = new BeforeDamageProgramWindowFrame(
            ++_resolutionSequence,
            parentFrameId,
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
            if (frame.Prevented || frame.RedirectedTargetSeat is not null ||
                frame.CandidateIndex >= frame.Candidates.Count)
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
                if (attack.SourceSeat != frame.SourceSeat || attack.TargetSeat !=
                    (frame.RedirectedTargetSeat ?? frame.TargetSeat))
                    throw new InvalidOperationException("The before-damage attack participants changed.");
                if (frame.RedirectedTargetSeat is null) attack.MarkBeforeDamageProgramsResolved();
                if (frame.Prevented)
                {
                    CompleteAttack(attack);
                    return;
                }
                if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
                return;
            }
            default:
                throw new InvalidOperationException("Unsupported before-damage program continuation.");
        }
    }
}
