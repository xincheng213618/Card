namespace CardGame.Core;

/// <summary>Generic before-damage program window. It contains no character or skill ids.</summary>
public sealed partial class GameEngine
{
    private bool? _hasRangePreventionPrograms;
    private bool HasRangePreventionPrograms => _hasRangePreventionPrograms ??= _contentRegistry.Skills.Values.Any(s =>
        s.Program?.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.PreventOwnPlayOutsideTargetRangeDamage)) == true);

    private bool TryVisitRangePreventionChainTarget(IDamageAttempt attack)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt))
            throw new InvalidOperationException("Chain range prevention lost its exact live attack owner.");
        if (attack is ProgramAttackHandle)
        {
            var state = GetProgramAttackState(attack.ResolutionId);
            if (state.RangePreventionVisitedTargets?.Contains(attack.TargetSeat) == true) return false;
            UpdateProgramAttackState(attack.ResolutionId, current => current with
            { RangePreventionVisitedTargets = Array.AsReadOnly((current.RangePreventionVisitedTargets ?? []).Append(attack.TargetSeat).ToArray()) });
        }
        else
        {
            var state = GetCardAttackState(attack.ResolutionId);
            if (state.RangePreventionVisitedTargets?.Contains(attack.TargetSeat) == true) return false;
            UpdateCardAttackState(attack.ResolutionId, current => current! with
            { RangePreventionVisitedTargets = Array.AsReadOnly((current!.RangePreventionVisitedTargets ?? []).Append(attack.TargetSeat).ToArray()) });
        }
        return true;
    }
    private bool TryBeginBeforeDamageProgramWindowForAttack(
        IDamageAttempt attack,
        int amount,
        DamageNature nature)
    {
        if (amount <= 0) return false;
        var rangeChainOnly = attack.BeforeDamageProgramsResolved;
        if (rangeChainOnly && (!attack.IsChainPropagation || !HasRangePreventionPrograms ||
            !TryVisitRangePreventionChainTarget(attack))) return false;
        return TryBeginBeforeDamageProgramWindow(
            attack.SourceSeat,
            attack.TargetSeat,
            amount,
            nature,
            BeforeDamageProgramContinuation.Attack, rangeChainOnly);
    }

    private bool TryBeginBeforeDamageProgramWindow(
        int sourceSeat,
        int targetSeat,
        int amount,
        DamageNature nature,
        BeforeDamageProgramContinuation continuation, bool rangeChainOnly = false)
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
                    EventTargetMarkerCounts = HasAttributedEventOperations() ? new Dictionary<PlayerMarkerKind,int>(target.Markers) : null,
                    OtherDamageSourceAlive = sourceSeat != owner.Seat && CurrentDamageAttempt?.IsSourceLess != true && _players[sourceSeat].IsAlive,
                    BlockedDamageSourceSkills = GetSkillBindingShard(owner).ProgramInstances.Where(instance => IsDamageOfferPairBlocked(owner.Seat, instance.SkillId, sourceSeat)).Select(instance => instance.SkillId).Distinct().ToArray(),
                    DamageCardIsSlash = CurrentDamageAttempt is { EffectiveCardKind: { } beforeDamageKind, IsChainPropagation: false, IsSourceLess: false } && IsSlashCard(beforeDamageKind),
                    DirectCardUseDamage = CurrentDamageAttempt is { Card: not null, IsChainPropagation: false, IsSourceLess: false },
                    DamageSourceGender = CurrentDamageAttempt?.IsDelayedJudgmentDamage != true && CurrentDamageAttempt?.IsSourceLess != true &&
                        _contentRegistry.Skills.Values.Any(skill => skill.Program?.Triggers.Any(trigger =>
                            HasTriggerCondition(trigger.Condition, SkillProgramTriggerConditionKind.DamageSourceGenderIs)) == true)
                        ? _players[sourceSeat].Gender : null
                };
                return CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.BeforeDamageApplied)
                    .Where(candidate => !rangeChainOnly || GetProgramTrigger(candidate).Effects.Any(e => e.Op == SkillProgramEffectOp.PreventOwnPlayOutsideTargetRangeDamage))
                    .Where(candidate => GetProgramTrigger(candidate).Subject switch
                    {
                        SkillProgramTriggerSubject.DamageTarget => owner.Seat == targetSeat &&
                            (CurrentDamageAttempt?.DamageRedirected != true ||
                             !GetProgramTrigger(candidate).Effects.Any(effect =>
                                 effect.Op == SkillProgramEffectOp.RedirectCurrentDamage)),
                        SkillProgramTriggerSubject.DamageSource => CurrentDamageAttempt?.IsSourceLess != true && owner.Seat == sourceSeat && owner.Seat != targetSeat,
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
                ? CurrentDamageAttempt?.ResolutionId : null) ??
            throw new InvalidOperationException("Before-damage programs require an attack continuation.");
        var frame = new BeforeDamageProgramWindowFrame(
            ++_resolutionSequence,
            parentFrameId,
            sourceSeat,
            targetSeat,
            amount,
            nature,
            continuation,
            Array.AsReadOnly(candidates))
        { ContinuationAttackResolutionId = rangeChainOnly ? CurrentDamageAttempt!.ResolutionId : null };
        PushRuntimeFrame(frame);
        AdvanceRuntimeTop<BeforeDamageProgramWindowFrame>();
        return true;
    }

    private ProgramSkillWindowContext CreateBeforeDamageProgramContext(
        BeforeDamageProgramWindowFrame frame,
        BeforeDamageProgramCandidate item) =>
        new(
            SkillProgramTriggerWindow.BeforeDamageApplied,
            frame.Id,
            item.Candidate.OwnerSeat,
            SourceSeat: BeforeDamageHasNoSource(frame) ? null : frame.SourceSeat,
            TargetSeat: frame.TargetSeat,
            Amount: frame.Amount,
            OccurrenceIndex: item.Candidate.OccurrenceIndex,
            Facts: item.Facts);

    private bool BeforeDamageHasNoSource(BeforeDamageProgramWindowFrame frame) =>
        frame.Continuation == BeforeDamageProgramContinuation.Attack && CurrentDamageAttempt?.IsSourceLess == true;

    private void ContinueBeforeDamageProgramWindowCore()
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
                ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
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
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.BeforeDamageApplied,
            activated,
            completed));
        ReplaceRuntimeTop(current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
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

        ReplaceRuntimeFrame(_resolutionStack[^2].Id, frame with { Prevented = true });
        AdvanceEventRulesAndQueueFact(new ProgramDamagePreventedEvent(
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
                var attack = CurrentDamageAttempt ??
                    throw new InvalidOperationException("The before-damage attack continuation is unavailable.");
                if (frame.ContinuationAttackResolutionId is { } exactAttackId && attack.ResolutionId != exactAttackId ||
                    attack.SourceSeat != frame.SourceSeat || attack.TargetSeat !=
                    (frame.RedirectedTargetSeat ?? frame.TargetSeat))
                    throw new InvalidOperationException("The before-damage attack participants changed.");
                if (frame.RedirectedTargetSeat is null) attack.MarkBeforeDamageProgramsResolved();
                if (frame.Prevented)
                {
                    CompleteDamageAttack(attack);
                    return;
                }
                if (!ApplyAttackDamage(attack)) CompleteDamageAttack(attack);
                return;
            }
            default:
                throw new InvalidOperationException("Unsupported before-damage program continuation.");
        }
    }
}
