namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginProgramJudgmentWindow(
        JudgmentResolution pending,
        Card judgmentCard,
        Suit effectiveSuit,
        bool succeeded)
    {
        if (_contentRegistry is null) return false;
        if (!_players[pending.TargetSeat].IsAlive) return false;
        var candidates = _players.Where(player => player.IsAlive).OrderBy(player => player.Seat)
            .SelectMany(owner => EnabledUniqueProgramTriggers(owner, SkillProgramTriggerWindow.JudgmentFinalized)
                .OrderBy(binding => binding.SkillId, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.Id, StringComparer.Ordinal)
                .Where(binding => MatchesFinalJudgment(
                    binding.Trigger, owner, pending, judgmentCard, effectiveSuit))
                .Select(binding => new ProgramJudgmentTriggerCandidate(
                    owner.Seat, binding.Program.Id, binding.Trigger.Id, binding.Program.GameplayHash)))
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
        CanStartFinalJudgmentEffects(owner, trigger);

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
               CanStartFinalJudgmentEffects(owner, trigger);
    }

    private bool CanStartFinalJudgmentEffects(CharacterState owner, SkillProgramTrigger trigger)
    {
        var first = trigger.Effects.FirstOrDefault(effect =>
            effect.Condition.Evaluate(CreateSkillContext(owner)));
        if (first is null) return false;
        return first.Op != SkillProgramTriggerEffectOp.SelectTarget ||
               GetProgramJudgmentTargetSeats(owner, first).Count > 0;
    }

    private IReadOnlyList<int> GetProgramJudgmentTargetSeats(
        CharacterState owner,
        SkillProgramTriggerEffect effect)
    {
        if (effect is not
            {
                Op: SkillProgramTriggerEffectOp.SelectTarget,
                TargetKind: { } targetKind
            })
            return [];
        return _players
            .Where(target => target.IsAlive &&
                (targetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded ||
                 target.Seat != owner.Seat) &&
                (targetKind is SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving ||
                 target.Hp < target.MaxHp))
            .Select(target => target.Seat)
            .Order()
            .ToArray();
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
            var owner = _players[candidate.OwnerSeat];
            if (!frame.Activated)
            {
                if (!CanRunProgramJudgmentTrigger(candidate, trigger, frame.Judgment) ||
                    !GetSkillBindingShard(owner)!.HasProgram(candidate.SkillId, candidate.GameplayHash))
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
            if (!effect.Condition.Evaluate(CreateSkillContext(owner))) continue;
            if (effect.Op == SkillProgramTriggerEffectOp.Draw)
            {
                if (!owner.IsAlive) continue;
                DrawCards(owner, effect.Amount, log: true,
                    reason: new CardMoveReason($"skill-program.{candidate.SkillId}.judgment.draw"));
            }
            else if (effect.Op == SkillProgramTriggerEffectOp.Recover)
            {
                if (!owner.IsAlive) continue;
                var amount = Math.Min(effect.Amount, owner.MaxHp - owner.Hp);
                if (amount <= 0) continue;
                var recovery = BeginRecovery(frame.Id, owner.Seat, owner.Seat, amount);
                owner.Hp += amount;
                QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, owner.Seat, amount, owner.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
            else if (effect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            {
                ExposeProgramJudgmentTargetPrompt(
                    (ProgramJudgmentTriggerWindowFrame)_resolutionStack[^1],
                    candidate,
                    effect);
                return;
            }
            else if (effect.Op == SkillProgramTriggerEffectOp.Damage)
            {
                BeginProgramJudgmentDamage(
                    (ProgramJudgmentTriggerWindowFrame)_resolutionStack[^1],
                    candidate,
                    effect);
                return;
            }
            else if (effect.Op == SkillProgramTriggerEffectOp.CauseDeath)
            {
                BeginProgramJudgmentCauseDeath(
                    (ProgramJudgmentTriggerWindowFrame)_resolutionStack[^1],
                    candidate,
                    effect);
                return;
            }
            else throw new InvalidOperationException("Unsupported final judgment trigger effect.");
        }
    }

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
        var completed = CompleteFinalizedJudgment(
            pending,
            judgmentCard,
            succeeded,
            allowPostJudgmentSkills: false);
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
            InstructionIndex = 0,
            Activated = false,
            SelectedTargetSeat = null
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

    private void ExposeProgramJudgmentTargetPrompt(
        ProgramJudgmentTriggerWindowFrame frame,
        ProgramJudgmentTriggerCandidate candidate,
        SkillProgramTriggerEffect effect)
    {
        var owner = _players[candidate.OwnerSeat];
        var targetSeats = GetProgramJudgmentTargetSeats(owner, effect);
        if (targetSeats.Count == 0)
        {
            ContinueProgramJudgmentWindow();
            return;
        }
        var skill = _contentRegistry!.Skills[candidate.SkillId];
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramJudgmentTarget,
            owner.Seat,
            $"【{skill.Name}】：选择一名角色作为判定效果目标。",
            [],
            targetSeats,
            SourceSeat: owner.Seat,
            IncomingCard: frame.Judgment.CardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = frame.Judgment.SubjectSeat,
            Choices = targetSeats.Select(targetSeat => new PromptChoice(
                new ChoiceId($"program-judgment-target.seat-{targetSeat}"),
                $"选择 {_players[targetSeat].Name}。",
                [],
                [targetSeat],
                new Dictionary<string, string>
                {
                    ["action"] = "program-judgment-target",
                    ["target-seat"] = targetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })).ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void BeginProgramJudgmentDamage(
        ProgramJudgmentTriggerWindowFrame frame,
        ProgramJudgmentTriggerCandidate candidate,
        SkillProgramTriggerEffect effect)
    {
        var targetSeat = effect.Target == SkillProgramTriggerEffectTarget.JudgmentSubject
            ? frame.Judgment.SubjectSeat
            : frame.SelectedTargetSeat;
        if (targetSeat is not { } resolvedTargetSeat ||
            !_players[resolvedTargetSeat].IsAlive ||
            effect.DamageNature is not { } nature)
        {
            ContinueProgramJudgmentWindow();
            return;
        }
        QueueGameEvent(new ProgramJudgmentDamageRequestedEvent(
            frame.Id,
            frame.Judgment.JudgmentFrameId,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            resolvedTargetSeat,
            effect.Amount,
            nature));
        AddLog("SkillTriggered",
            $"{_players[candidate.OwnerSeat].Name} 的【{_contentRegistry!.Skills[candidate.SkillId].Name}】将对 {_players[resolvedTargetSeat].Name} 造成 {effect.Amount} 点{GetDamageNatureLabel(nature)}伤害。",
            candidate.OwnerSeat,
            resolvedTargetSeat);
        var attack = new AttackResolution(
            frame.Id,
            candidate.OwnerSeat,
            resolvedTargetSeat,
            card: null,
            damageAmount: effect.Amount,
            playedCardKind: null,
            damageNatureOverride: nature,
            programJudgmentFrameId: frame.Id);
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }

    private void BeginProgramJudgmentCauseDeath(
        ProgramJudgmentTriggerWindowFrame frame,
        ProgramJudgmentTriggerCandidate candidate,
        SkillProgramTriggerEffect effect)
    {
        var targetSeat = effect.Target == SkillProgramTriggerEffectTarget.JudgmentSubject
            ? frame.Judgment.SubjectSeat
            : frame.SelectedTargetSeat;
        if (targetSeat is not { } resolvedTargetSeat || !_players[resolvedTargetSeat].IsAlive)
        {
            ContinueProgramJudgmentWindow();
            return;
        }
        var suspendedJudgment = _pendingJudgment ??
            throw new InvalidOperationException("Configured causeDeath lost its finalized judgment.");
        if (suspendedJudgment.FrameId != frame.ParentFrameId ||
            suspendedJudgment.FrameId != frame.Judgment.JudgmentFrameId)
            throw new InvalidOperationException("Configured causeDeath belongs to another judgment window.");

        var causeId = ++_resolutionSequence;
        var continuation = new ProgramCauseDeathResolution(
            causeId,
            frame.Id,
            suspendedJudgment,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            resolvedTargetSeat);
        QueueGameEvent(new ProgramCauseDeathDeclaredEvent(
            causeId,
            frame.Id,
            frame.Judgment.JudgmentFrameId,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            resolvedTargetSeat));
        AddLog(
            "CauseDeath",
            $"{_players[candidate.OwnerSeat].Name} 的【{_contentRegistry!.Skills[candidate.SkillId].Name}】令 {_players[resolvedTargetSeat].Name} 直接死亡。",
            candidate.OwnerSeat,
            resolvedTargetSeat);

        _pendingJudgment = null;
        try
        {
            BeginPlayerDeath(
                frame.Id,
                _players[resolvedTargetSeat],
                killer: null,
                attack: null,
                dying: null,
                causingProgramCauseDeath: continuation);
        }
        catch
        {
            if (_pendingJudgment is null)
            {
                _pendingJudgment = suspendedJudgment;
            }
            throw;
        }
    }

    private void CompleteProgramCauseDeath(ProgramCauseDeathResolution pending)
    {
        if (_pendingJudgment is not null ||
            _resolutionStack.LastOrDefault() is not ProgramJudgmentTriggerWindowFrame frame ||
            frame.Id != pending.ParentFrameId ||
            frame.ParentFrameId != pending.Judgment.FrameId)
        {
            throw new InvalidOperationException(
                "Configured causeDeath did not return to its judgment trigger window.");
        }
        _pendingJudgment = pending.Judgment;
        ContinueProgramJudgmentWindow();
    }

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

    private CommandResult SubmitProgramJudgmentTargetAnswer(
        int actorSeat,
        PromptId prompt,
        ChoiceId choice)
    {
        var error = ValidateHumanPrompt(
            actorSeat,
            DecisionKind.ProgramJudgmentTarget,
            prompt,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(item => item.Id == choice);
        if (selected is null)
            return Reject(CommandErrorCode.InvalidChoice, "The judgment target choice is not available.");
        return Accept(() =>
        {
            ResolveProgramJudgmentTargetChoice(selected);
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

    private void ResolveProgramJudgmentTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramJudgmentTriggerWindowFrame ??
            throw new InvalidOperationException("Missing judgment trigger frame for target selection.");
        var candidate = frame.Candidates[frame.CandidateIndex];
        var program = _contentRegistry!.Skills[candidate.SkillId].Program ??
            throw new InvalidOperationException("The judgment target program is unavailable.");
        if (program.GameplayHash != candidate.GameplayHash)
            throw new InvalidOperationException("A running judgment target definition changed.");
        var trigger = program.Triggers.Single(item => item.Id == candidate.TriggerId);
        var selectionEffect = trigger.Effects[frame.InstructionIndex - 1];
        var legalTargets = GetProgramJudgmentTargetSeats(_players[candidate.OwnerSeat], selectionEffect);
        if (selected.Targets is not [var targetSeat] || !legalTargets.Contains(targetSeat))
            throw new InvalidOperationException("The selected judgment effect target is no longer legal.");
        ClearPendingDecision();
        _resolutionStack[^1] = frame with { SelectedTargetSeat = targetSeat };
        QueueGameEvent(new ProgramJudgmentTargetSelectedEvent(
            frame.Id,
            frame.Judgment.JudgmentFrameId,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            targetSeat));
        ContinueProgramJudgmentWindow();
    }

    private bool IsAiProgramJudgmentPending() =>
        _pendingDecision is
        {
            Kind: DecisionKind.ProgramJudgmentTrigger or DecisionKind.ProgramJudgmentTarget,
            PlayerSeat: var playerSeat
        } &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiProgramJudgment()
    {
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI judgment program prompt is missing.");
        if (_players[decision.PlayerSeat].IsHuman)
            throw new InvalidOperationException("A human judgment program prompt cannot use the AI route.");
        if (decision.Kind == DecisionKind.ProgramJudgmentTrigger)
        {
            ResolveProgramJudgmentChoice(decision.Choices[0]);
            return;
        }
        if (decision.Kind != DecisionKind.ProgramJudgmentTarget)
            throw new InvalidOperationException("Unsupported AI judgment program prompt.");
        var (targetSeat, thought) = _aiBrains[decision.PlayerSeat].ChooseLeijiTarget(
            CreateSnapshot(decision.PlayerSeat),
            decision.ValidTargetSeats,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = targetSeat is { } seat
            ? decision.Choices.Single(choice => choice.Targets.SequenceEqual([seat]))
            : decision.Choices[0];
        ResolveProgramJudgmentTargetChoice(selected);
        PublishState();
    }

    private bool IsProgramJudgmentPromptValid(ProgramJudgmentTriggerWindowFrame frame)
    {
        if (_pendingDecision is not { } decision ||
            frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            decision.PlayerSeat != frame.Candidates[frame.CandidateIndex].OwnerSeat ||
            decision.Choices.Any(choice => choice.Cards.Count != 0))
            return false;
        if (decision.Kind == DecisionKind.ProgramJudgmentTrigger)
            return decision.Choices.Count == 2 && decision.ValidTargetSeats.Count == 0;
        if (decision.Kind != DecisionKind.ProgramJudgmentTarget || frame.InstructionIndex <= 0)
            return false;
        var candidate = frame.Candidates[frame.CandidateIndex];
        var trigger = _contentRegistry!.Skills[candidate.SkillId].Program!.Triggers
            .Single(item => item.Id == candidate.TriggerId);
        var selection = trigger.Effects[frame.InstructionIndex - 1];
        var targets = GetProgramJudgmentTargetSeats(_players[candidate.OwnerSeat], selection);
        return decision.ValidTargetSeats.SequenceEqual(targets) &&
               decision.Choices.Count == targets.Count &&
               decision.Choices.All(choice =>
                   choice.Targets is [var targetSeat] && targets.Contains(targetSeat));
    }

    private void AssertProgramJudgmentWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_pendingDecision?.Kind is
                DecisionKind.ProgramJudgmentTrigger or DecisionKind.ProgramJudgmentTarget)
                throw new InvalidOperationException("A judgment trigger prompt lost its frame.");
            return;
        }
        var pending = _pendingJudgment;
        var frame = pending is null
            ? null
            : frames.LastOrDefault(candidate =>
                candidate.Judgment.JudgmentFrameId == pending.FrameId);
        var suspendedFrames = frames.Where(candidate => !ReferenceEquals(candidate, frame)).ToArray();
        var suspendedCauseDeaths = EnumeratePendingDeaths()
            .Select(death => death.CausingProgramCauseDeath)
            .Where(cause => cause is not null)
            .ToArray();
        if (suspendedFrames.Any(suspended =>
                !suspendedCauseDeaths.Any(cause =>
                    cause!.ParentFrameId == suspended.Id &&
                    cause.Judgment.FrameId == suspended.Judgment.JudgmentFrameId)))
            throw new InvalidOperationException("A suspended judgment trigger window lost its direct-death continuation.");
        if (frame is null)
        {
            if (pending is not null &&
                _pendingDecision?.Kind is DecisionKind.ProgramJudgmentTrigger or DecisionKind.ProgramJudgmentTarget)
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
        if (pending is null || pending.ResultSucceeded is null ||
            (!ReferenceEquals(_resolutionStack.Last(), frame) && !resolvingDamage) ||
            judgmentFrame is null || judgmentFrame.Id != frame.ParentFrameId ||
            pending.FrameId != frame.Judgment.JudgmentFrameId ||
            pending.TargetSeat != frame.Judgment.SubjectSeat ||
            pending.CurrentCard?.Id != frame.Judgment.CardId ||
            frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            (!resolvingDamage && prompt is not null && !IsProgramJudgmentPromptValid(frame)))
            throw new InvalidOperationException("A final judgment trigger window has an invalid cursor or prompt.");
    }

    private IEnumerable<DeathResolution> EnumeratePendingDeaths()
    {
        for (var death = _pendingDeath; death is not null; death = death.Parent)
            yield return death;
    }

    private sealed class ProgramCauseDeathResolution(
        long causeId,
        long parentFrameId,
        JudgmentResolution judgment,
        string skillId,
        string triggerId,
        int sourceSeat,
        int targetSeat)
    {
        public long CauseId { get; } = causeId;
        public long ParentFrameId { get; } = parentFrameId;
        public JudgmentResolution Judgment { get; } = judgment;
        public string SkillId { get; } = skillId;
        public string TriggerId { get; } = triggerId;
        public int SourceSeat { get; } = sourceSeat;
        public int TargetSeat { get; } = targetSeat;
    }
}
