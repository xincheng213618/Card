namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginProgramJudgmentWindow(
        JudgmentResolution pending,
        Card judgmentCard,
        Suit effectiveSuit,
        bool succeeded)
    {
        if (_rulesVersion < 81 || _contentRegistry is null) return false;
        var owner = _players[pending.TargetSeat];
        if (!owner.IsAlive) return false;
        var candidates = EnabledSkillPrograms(owner)
            .OrderBy(program => program.Id, StringComparer.Ordinal)
            .SelectMany(program => program.Triggers
                .Where(trigger => MatchesFinalJudgment(trigger, owner, pending, judgmentCard, effectiveSuit))
                .OrderBy(trigger => trigger.Id, StringComparer.Ordinal)
                .Select(trigger => new ProgramJudgmentTriggerCandidate(
                    owner.Seat, program.Id, trigger.Id, program.GameplayHash)))
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
            succeeded);
        _resolutionStack.Add(new ProgramJudgmentTriggerWindowFrame(
            ++_resolutionSequence,
            pending.FrameId,
            context,
            Array.AsReadOnly(candidates)));
        return true;
    }

    private bool MatchesFinalJudgment(
        SkillProgramTrigger trigger,
        PlayerRuntime owner,
        JudgmentResolution pending,
        Card judgmentCard,
        Suit effectiveSuit) =>
        trigger.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
        trigger.Subject == SkillProgramTriggerSubject.Owner &&
        pending.TargetSeat == owner.Seat &&
        trigger.Suits.Contains(effectiveSuit) &&
        judgmentCard.Rank >= trigger.MinimumRank &&
        judgmentCard.Rank <= trigger.MaximumRank &&
        !trigger.ExcludedReasons.Contains(pending.Reason, StringComparer.Ordinal) &&
        trigger.Effects.Any(effect => effect.Condition.Evaluate(CreateSkillContext(owner)));

    private bool CanRunProgramJudgmentTrigger(
        ProgramJudgmentTriggerCandidate candidate,
        SkillProgramTrigger trigger,
        JudgmentFinalizedContext judgment)
    {
        var owner = _players[candidate.OwnerSeat];
        return owner.IsAlive &&
               judgment.SubjectSeat == owner.Seat &&
               trigger.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
               trigger.Subject == SkillProgramTriggerSubject.Owner &&
               trigger.Suits.Contains(judgment.Suit) &&
               judgment.Rank >= trigger.MinimumRank && judgment.Rank <= trigger.MaximumRank &&
               !trigger.ExcludedReasons.Contains(judgment.Reason, StringComparer.Ordinal) &&
               trigger.Effects.Any(effect => effect.Condition.Evaluate(CreateSkillContext(owner)));
    }

    private void ContinueProgramJudgmentWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame frame)
        {
            var pending = _pendingJudgment ??
                throw new InvalidOperationException("Missing judgment trigger continuation.");
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
            var owner = _players[candidate.OwnerSeat];
            if (!frame.Activated)
            {
                if (!CanRunProgramJudgmentTrigger(candidate, trigger, frame.Judgment) ||
                    !EnabledSkillPrograms(owner).Any(item => item.Id == candidate.SkillId))
                {
                    AdvanceProgramJudgmentCandidate(frame);
                    continue;
                }
                if (trigger.Optional)
                {
                    ExposeProgramJudgmentPrompt(frame, candidate);
                    return;
                }
                _resolutionStack[^1] = frame with { Activated = true };
                continue;
            }

            if (frame.InstructionIndex == trigger.Effects.Count)
            {
                AdvanceProgramJudgmentCandidate(frame);
                continue;
            }
            var effect = trigger.Effects[frame.InstructionIndex];
            _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex + 1 };
            if (!owner.IsAlive || !effect.Condition.Evaluate(CreateSkillContext(owner))) continue;
            if (effect.Op == SkillProgramTriggerEffectOp.Draw)
                DrawCards(owner, effect.Amount, log: true,
                    reason: new CardMoveReason($"skill-program.{candidate.SkillId}.judgment.draw"));
            else if (effect.Op == SkillProgramTriggerEffectOp.Recover)
            {
                var amount = Math.Min(effect.Amount, owner.MaxHp - owner.Hp);
                if (amount <= 0) continue;
                var recovery = BeginRecovery(frame.Id, owner.Seat, owner.Seat, amount);
                owner.Hp += amount;
                QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, owner.Seat, amount, owner.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
            else throw new InvalidOperationException("Unsupported final judgment trigger effect.");
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
            InstructionIndex = 0,
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
        _pendingDecision is { Kind: DecisionKind.ProgramJudgmentTrigger, PlayerSeat: var playerSeat } &&
        !_players[playerSeat].IsHuman;

    private void AssertProgramJudgmentWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_pendingDecision?.Kind == DecisionKind.ProgramJudgmentTrigger)
                throw new InvalidOperationException("A judgment trigger prompt lost its frame.");
            return;
        }
        var frame = frames.Single();
        var pending = _pendingJudgment;
        var prompt = _pendingDecision;
        if (_rulesVersion < 81 || pending is null || pending.ResultSucceeded is null ||
            !ReferenceEquals(_resolutionStack.Last(), frame) || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not JudgmentFrame judgmentFrame ||
            judgmentFrame.Id != frame.ParentFrameId || pending.FrameId != frame.Judgment.JudgmentFrameId ||
            pending.TargetSeat != frame.Judgment.SubjectSeat ||
            pending.CurrentCard?.Id != frame.Judgment.CardId ||
            frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            (prompt is not null &&
             (prompt.Kind != DecisionKind.ProgramJudgmentTrigger ||
              prompt.PlayerSeat != frame.Candidates[frame.CandidateIndex].OwnerSeat ||
              prompt.Choices.Count != 2 || prompt.Choices.Any(choice => choice.Cards.Count != 0))))
            throw new InvalidOperationException("A final judgment trigger window has an invalid cursor or prompt.");
    }
}
