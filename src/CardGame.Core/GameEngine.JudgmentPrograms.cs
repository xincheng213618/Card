namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginProgramJudgmentWindow(
        JudgmentResolution pending,
        Card judgmentCard,
        Suit effectiveSuit,
        bool succeeded)
    {
        if (!_players[pending.TargetSeat].IsAlive) return false;
        var candidates = _players.Where(player => player.IsAlive).OrderBy(player => player.Seat)
            .SelectMany(owner => EnabledUniqueProgramTriggers(owner, SkillProgramTriggerWindow.JudgmentFinalized)
                .OrderBy(binding => binding.SkillId, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.Id, StringComparer.Ordinal)
                .Where(binding => MatchesFinalJudgment(
                    binding.Trigger, owner, pending, judgmentCard, effectiveSuit))
                .Select(binding => new ProgramJudgmentTriggerCandidate(
                    owner.Seat, binding.Program.Id, binding.Trigger.Id,
                    binding.SkillInstanceId, binding.Program.GameplayHash)))
            .ToArray();
        if (candidates.Length == 0) return false;

        _pendingJudgment = pending;
        var context = new JudgmentFinalizedContext(
            pending.FrameId,
            pending.TargetSeat,
            pending.Reason,
            judgmentCard.Id,
            judgmentCard.Kind,
            effectiveSuit,
            judgmentCard.Rank,
            succeeded,
            pending.SourceSeat);
        _resolutionStack.Add(new ProgramJudgmentTriggerWindowFrame(
            ++_resolutionSequence,
            pending.FrameId,
            context,
            Array.AsReadOnly(candidates)));
        return true;
    }

    private bool MatchesFinalJudgment(
        SkillProgramTrigger trigger,
        CharacterState owner,
        JudgmentResolution pending,
        Card judgmentCard,
        Suit effectiveSuit) =>
        trigger.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
        (trigger.Subject == SkillProgramTriggerSubject.Any || pending.TargetSeat == owner.Seat) &&
        (trigger.JudgmentSource is null || trigger.JudgmentSource == SkillProgramTriggerSubject.Any ||
         pending.SourceSeat == owner.Seat) &&
        (trigger.JudgmentReasons.Count == 0 ||
         trigger.JudgmentReasons.Contains(pending.Reason, StringComparer.Ordinal)) &&
        trigger.Suits.Contains(effectiveSuit) &&
        judgmentCard.Rank >= trigger.MinimumRank &&
        judgmentCard.Rank <= trigger.MaximumRank &&
        !trigger.ExcludedReasons.Contains(pending.Reason, StringComparer.Ordinal) &&
        CanStartFinalJudgmentEffects(owner, trigger) &&
        (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ClaimJudgmentCard) ||
         _cardZones.GetLocation(judgmentCard.Id) == CardLocation.Judgment(pending.TargetSeat));

    private bool CanRunProgramJudgmentTrigger(
        ProgramJudgmentTriggerCandidate candidate,
        SkillProgramTrigger trigger,
        JudgmentFinalizedContext judgment)
    {
        var owner = _players[candidate.OwnerSeat];
        return owner.IsAlive &&
               trigger.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
               (trigger.Subject == SkillProgramTriggerSubject.Any || judgment.SubjectSeat == owner.Seat) &&
               (trigger.JudgmentSource is null || trigger.JudgmentSource == SkillProgramTriggerSubject.Any ||
                judgment.SourceSeat == owner.Seat) &&
               (trigger.JudgmentReasons.Count == 0 ||
                trigger.JudgmentReasons.Contains(judgment.Reason, StringComparer.Ordinal)) &&
               trigger.Suits.Contains(judgment.Suit) &&
               judgment.Rank >= trigger.MinimumRank && judgment.Rank <= trigger.MaximumRank &&
               !trigger.ExcludedReasons.Contains(judgment.Reason, StringComparer.Ordinal) &&
               CanStartFinalJudgmentEffects(owner, trigger) &&
               (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ClaimJudgmentCard) ||
                _cardZones.GetLocation(judgment.CardId) == CardLocation.Judgment(judgment.SubjectSeat));
    }

    private bool CanStartFinalJudgmentEffects(CharacterState owner, SkillProgramTrigger trigger)
    {
        var first = trigger.Effects.FirstOrDefault(effect =>
            effect.Condition.Evaluate(CreateSkillContext(owner)));
        if (first is null) return false;
        return first.Op != SkillProgramEffectOp.SelectTarget ||
               first.TargetKind is { } kind && GetProgramTargetSeats(owner.Seat, kind).Count > 0;
    }

    private void ContinueProgramJudgmentWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame frame)
        {
            var pending = _pendingJudgment ??
                throw new InvalidOperationException("Missing judgment trigger continuation.");
            if (_winner != Winner.None)
            {
                ShortCircuitProgramJudgmentWindow(frame, pending);
                return;
            }
            if (frame.CandidateIndex == frame.Candidates.Count)
            {
                var succeeded = pending.ResultSucceeded ??
                    throw new InvalidOperationException("A final judgment trigger lost its result.");
                var judgmentCard = pending.CurrentCard ??
                    throw new InvalidOperationException("A final judgment trigger lost its card.");
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramJudgmentTriggerWindow);
                var completed = CompleteFinalizedJudgment(pending, judgmentCard, succeeded);
                if (completed is { } result) ResumeCompletedJudgment(pending, result);
                return;
            }

            var candidate = frame.Candidates[frame.CandidateIndex];
            var program = _contentRegistry!.Skills[candidate.SkillId].Program!;
            if (program.GameplayHash != candidate.GameplayHash)
                throw new InvalidOperationException("A running judgment trigger definition changed.");
            var trigger = program.Triggers.Single(item => item.Id == candidate.TriggerId);
            var shared = new ProgramTriggerCandidate(
                candidate.OwnerSeat, candidate.SkillId, candidate.TriggerId,
                candidate.SkillInstanceId, candidate.GameplayHash, trigger.Priority);
            var context = CreateFinalizedJudgmentProgramContext(frame, candidate);
            if (!CanRunProgramTrigger(shared, context))
            {
                // This opportunity belongs to the frozen grant. Another grant of the
                // same skill cannot inherit it after the original instance expires.
                AdvanceProgramJudgmentCandidate(frame with { Activated = false });
                continue;
            }
            if (!frame.Activated)
            {
                if (trigger.Optional)
                {
                    ExposeProgramJudgmentPrompt(frame, candidate);
                    return;
                }
                _resolutionStack[^1] = frame with { Activated = true };
                continue;
            }

            BeginProgramBinding(shared, context);
            return;
        }
    }

    private ProgramSkillWindowContext CreateFinalizedJudgmentProgramContext(
        ProgramJudgmentTriggerWindowFrame frame, ProgramJudgmentTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.JudgmentFinalized, frame.Id, candidate.OwnerSeat,
            SourceSeat: frame.Judgment.SourceSeat,
            TargetSeat: frame.Judgment.SubjectSeat,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]),
            Judgment: frame.Judgment);

    private void ShortCircuitProgramJudgmentWindow(
        ProgramJudgmentTriggerWindowFrame frame,
        JudgmentResolution pending)
    {
        if (frame.CandidateIndex < frame.Candidates.Count && frame.Activated)
        {
            var candidate = frame.Candidates[frame.CandidateIndex];
            QueueGameEvent(new ProgramJudgmentTriggerResolvedEvent(
                frame.Id,
                frame.Judgment.JudgmentFrameId,
                candidate.SkillId,
                candidate.TriggerId,
                candidate.OwnerSeat,
                Activated: true));
        }

        var succeeded = pending.ResultSucceeded ??
            throw new InvalidOperationException("A terminal judgment trigger lost its result.");
        var judgmentCard = pending.CurrentCard ??
            throw new InvalidOperationException("A terminal judgment trigger lost its card.");
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramJudgmentTriggerWindow);
        var completed = CompleteFinalizedJudgment(pending, judgmentCard, succeeded);
        if (completed is { } result)
        {
            ResumeCompletedJudgment(pending, result);
        }
    }

    private void AdvanceProgramJudgmentCandidate(ProgramJudgmentTriggerWindowFrame frame)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        QueueGameEvent(new ProgramJudgmentTriggerResolvedEvent(
            frame.Id,
            frame.Judgment.JudgmentFrameId,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            frame.Activated));
        _resolutionStack[^1] = frame with
        {
            CandidateIndex = frame.CandidateIndex + 1,
            Activated = false
        };
    }

    private void ExposeProgramJudgmentPrompt(
        ProgramJudgmentTriggerWindowFrame frame,
        ProgramJudgmentTriggerCandidate candidate)
    {
        var skill = _contentRegistry!.Skills[candidate.SkillId];
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramJudgmentTrigger,
            candidate.OwnerSeat,
            $"【{skill.Name}】：{GetSuitDisplayName(frame.Judgment.Suit)} {frame.Judgment.Rank} 的判定结果已生效。",
            [],
            [],
            SourceSeat: candidate.OwnerSeat,
            IncomingCard: frame.Judgment.CardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = frame.Judgment.SubjectSeat,
            Choices =
            [
                ProgramJudgmentChoice("activate", "发动技能"),
                ProgramJudgmentChoice("skip", "不发动")
            ]
        };
        _status = _players[candidate.OwnerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private static PromptChoice ProgramJudgmentChoice(string action, string label) =>
        new(new ChoiceId($"program-judgment-trigger.{action}"), label, [], [],
            new Dictionary<string, string> { ["action"] = "program-judgment-trigger-" + action });

    private CommandResult SubmitProgramJudgmentTriggerAnswer(
        int actorSeat,
        PromptId prompt,
        ChoiceId choice)
    {
        var error = ValidateHumanPrompt(
            actorSeat,
            DecisionKind.ProgramJudgmentTrigger,
            prompt,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(item => item.Id == choice);
        if (selected is null)
            return Reject(CommandErrorCode.InvalidChoice, "The judgment trigger choice is not available.");
        return Accept(() =>
        {
            ResolveProgramJudgmentChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveProgramJudgmentChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramJudgmentTriggerWindowFrame ??
            throw new InvalidOperationException("Missing judgment trigger frame.");
        ClearPendingDecision();
        switch (selected.Parameters["action"])
        {
            case "program-judgment-trigger-skip":
                AdvanceProgramJudgmentCandidate(frame);
                break;
            case "program-judgment-trigger-activate":
                _resolutionStack[^1] = frame with { Activated = true };
                break;
            default:
                throw new InvalidOperationException("Unsupported judgment trigger choice.");
        }
        ContinueProgramJudgmentWindow();
    }

    private bool IsAiProgramJudgmentPending() =>
        _pendingDecision is
        {
            Kind: DecisionKind.ProgramJudgmentTrigger,
            PlayerSeat: var playerSeat
        } &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiProgramJudgment()
    {
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI judgment program prompt is missing.");
        if (_players[decision.PlayerSeat].IsHuman)
            throw new InvalidOperationException("A human judgment program prompt cannot use the AI route.");
        if (decision.Kind != DecisionKind.ProgramJudgmentTrigger)
            throw new InvalidOperationException("Unsupported AI judgment program prompt.");
        ResolveProgramJudgmentChoice(decision.Choices[0]);
    }

    private bool IsProgramJudgmentPromptValid(ProgramJudgmentTriggerWindowFrame frame)
    {
        if (_pendingDecision is not { } decision ||
            frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            decision.PlayerSeat != frame.Candidates[frame.CandidateIndex].OwnerSeat ||
            decision.Choices.Any(choice => choice.Cards.Count != 0))
            return false;
        return decision.Kind == DecisionKind.ProgramJudgmentTrigger &&
               decision.Choices.Count == 2 && decision.ValidTargetSeats.Count == 0;
    }

    private void AssertProgramJudgmentWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_pendingDecision?.Kind is
                DecisionKind.ProgramJudgmentTrigger)
                throw new InvalidOperationException("A judgment trigger prompt lost its frame.");
            return;
        }
        var pending = _pendingJudgment;
        var frame = pending is null
            ? null
            : frames.LastOrDefault(candidate =>
                candidate.Judgment.JudgmentFrameId == pending.FrameId);
        var suspendedFrames = frames.Where(candidate => !ReferenceEquals(candidate, frame)).ToArray();
        if (suspendedFrames.Any(suspended =>
                !_resolutionStack.OfType<ProgramSkillFrame>().Any(child =>
                    child.WindowContext is
                    { Window: SkillProgramTriggerWindow.JudgmentFinalized, ParentFrameId: var parentId } &&
                    parentId == suspended.Id)))
            throw new InvalidOperationException("A suspended judgment trigger window lost its program child.");
        if (frame is null)
        {
            if (pending is not null &&
                _pendingDecision?.Kind is DecisionKind.ProgramJudgmentTrigger)
                throw new InvalidOperationException("A judgment trigger prompt belongs to no active judgment window.");
            return;
        }
        var frameIndex = _resolutionStack.FindLastIndex(item => ReferenceEquals(item, frame));
        var judgmentFrame = frameIndex > 0 ? _resolutionStack[frameIndex - 1] as JudgmentFrame : null;
        var prompt = _pendingDecision;
        var resolvingDamage = _pendingAttack is
        {
            IsProgramJudgmentDamage: true,
            ProgramJudgmentFrameId: var programFrameId
        } && programFrameId == frame.Id;
        var resolvingProgram = _resolutionStack.LastOrDefault() is ProgramSkillFrame skillFrame &&
            skillFrame.WindowContext is
            { Window: SkillProgramTriggerWindow.JudgmentFinalized, ParentFrameId: var parentId } &&
            parentId == frame.Id;
        if (pending is null || pending.ResultSucceeded is null ||
            (!ReferenceEquals(_resolutionStack.Last(), frame) && !resolvingDamage && !resolvingProgram) ||
            judgmentFrame is null || judgmentFrame.Id != frame.ParentFrameId ||
            pending.FrameId != frame.Judgment.JudgmentFrameId ||
            pending.TargetSeat != frame.Judgment.SubjectSeat ||
            pending.CurrentCard?.Id != frame.Judgment.CardId ||
            frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            (!resolvingDamage && !resolvingProgram && prompt is not null &&
             !IsProgramJudgmentPromptValid(frame)))
            throw new InvalidOperationException("A final judgment trigger window has an invalid cursor or prompt.");
    }

}
