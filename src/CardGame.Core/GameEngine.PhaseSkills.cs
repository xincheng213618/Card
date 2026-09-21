namespace CardGame.Core;

/// <summary>Host adapter: phase scheduling, authoritative effects and command routing.</summary>
public sealed partial class GameEngine
{
    private bool TryBeginPhaseSkill(PhaseSkillWindow window, PlayerRuntime owner)
    {
        if (_contentRegistry is null || !owner.IsAlive || _winner != Winner.None) return false;
        if (_pendingDecision is not null || _resolutionStack.Count != 0)
            throw new InvalidOperationException("Phase skills require a clean phase boundary.");

        foreach (var skillId in EnabledContentSkillIds(owner))
        {
            if (_contentRegistry.Skills[skillId].PhaseSkill is not { } module || module.Window != window)
                continue;
            var binding = $"phase-module:{window}";
            if (_skillRuntimeState.GetUsage(owner.Seat, skillId, binding, SkillUsageScope.Phase) != 0)
                continue;
            var context = new PhaseSkillContext(owner.Seat, _currentSeat, _turnNumber, _phase,
                owner.Hp, owner.MaxHp, GetHand(owner).Count,
                CountCardsUsedByCurrentPlayerThisTurn(owner.Seat), IsClassicIdentityMode);
            if (module.CreatePlan(context) is not { } candidate) continue;
            var plan = candidate.Freeze(skillId);
            if (!_skillRuntimeState.TryConsumeUsage(owner.Seat, skillId, binding, SkillUsageScope.Phase, 1))
                throw new InvalidOperationException("A phase activation was already offered.");
            var frame = new PhaseSkillFrame(++_resolutionSequence, skillId, binding,
                $"seat-{owner.Seat}:{skillId}", window, context, plan);
            _resolutionStack.Add(frame);
            _pendingDecision = new PendingDecision(plan.LegacyDecisionKind, owner.Seat, plan.Prompt, [], [])
            {
                PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = owner.Seat,
                SkillPrompt = plan.Presentation,
                Choices = Array.AsReadOnly(new[] { plan.Activate, plan.Skip })
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return true;
        }
        return false;
    }

    private int CountCardsUsedByCurrentPlayerThisTurn(int ownerSeat)
    {
        var turnStart = _events.FindLastIndex(envelope => envelope.Payload is TurnStartedEvent started &&
            started.TurnNumber == _turnNumber && started.ActorSeat == ownerSeat);
        return _events.Skip(turnStart + 1).Count(envelope =>
            envelope.Payload is CardUseDeclaredEvent declared && declared.SourceSeat == ownerSeat);
    }

    private CommandResult SubmitPhaseSkillPromptAnswer(PromptChoice selected)
    {
        if (_pendingDecision?.SkillPrompt is null || _resolutionStack.LastOrDefault() is not PhaseSkillFrame)
            return Reject(CommandErrorCode.InvalidPrompt, "There is no current module skill activation.");
        return Accept(() =>
        {
            ResolvePhaseSkillChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private bool IsAiPhaseSkillPending() =>
        _pendingDecision is { SkillPrompt: not null } decision && !_players[decision.PlayerSeat].IsHuman;

    private void ResolvePendingAiPhaseSkill()
    {
        if (!IsAiPhaseSkillPending() || _resolutionStack.LastOrDefault() is not PhaseSkillFrame frame)
            throw new InvalidOperationException("There is no AI module skill activation.");
        ResolvePhaseSkillChoice(frame.Plan.AiPrefersActivation ? frame.Plan.Activate : frame.Plan.Skip);
        PublishState();
    }

    private void ResolvePhaseSkillChoice(PromptChoice selected)
    {
        AssertPhaseSkillInvariant();
        var frame = (PhaseSkillFrame)_resolutionStack[^1];
        var used = selected.Id == frame.Plan.Activate.Id;
        if (!used && selected.Id != frame.Plan.Skip.Id)
            throw new InvalidOperationException("The module choice was not published.");
        var owner = _players[frame.Context.OwnerSeat];
        var module = _contentRegistry!.Skills[frame.SkillId].PhaseSkill!;
        ClearPendingDecision();
        var drawn = new List<int>();
        if (used)
        {
            foreach (var effect in frame.Plan.Effects)
            {
                // The cursor advances before applying an effect, just like the program runtime.
                frame = frame with { InstructionIndex = frame.InstructionIndex + 1, Step = ResolutionFrameStep.ResolvingEffect };
                _resolutionStack[^1] = frame;
                switch (effect)
                {
                    case DrawSkillCards draw:
                        drawn.AddRange(DrawCards(owner, draw.Count, draw.LogDraw, draw.Reason));
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported module skill effect.");
                }
            }
        }
        var result = new SkillActivationResult(used, Array.AsReadOnly(drawn.ToArray()));
        if (module.CreateResolvedEvent(frame.Context, result) is { } auditEvent) QueueGameEvent(auditEvent);
        QueueGameEvent(new SkillModuleResolvedEvent(frame.Id, frame.SkillId, owner.Seat, used));
        AddLog(used ? "SkillTriggered" : "SkillSkipped",
            used ? $"{owner.Name} 发动【{frame.Plan.Presentation.Name}】，摸了 {drawn.Count} 张牌。"
                 : $"{owner.Name} 未发动【{frame.Plan.Presentation.Name}】。", owner.Seat);
        PopResolutionFrame(frame.Id, ResolutionFrameKind.PhaseSkill);
        switch (frame.Window)
        {
            case PhaseSkillWindow.PlayEnding: CompleteCurrentPlayPhase(); break;
            case PhaseSkillWindow.TurnEnding: EndTurn(); break;
            default: throw new InvalidOperationException("Unsupported phase continuation.");
        }
    }

    private void AssertPhaseSkillInvariant()
    {
        var frames = _resolutionStack.OfType<PhaseSkillFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_pendingDecision?.SkillPrompt is not null)
                throw new InvalidOperationException("A module prompt has no phase frame.");
            return;
        }
        if (frames.Length != 1 || _resolutionStack.Count != 1 ||
            _pendingDecision is not { SkillPrompt: not null } decision)
            throw new InvalidOperationException("A phase skill must own one clean, paused frame.");
        var frame = frames[0];
        var owner = _players[frame.Context.OwnerSeat];
        if (!owner.IsAlive || owner.Seat != _currentSeat || frame.Context.Phase != _phase ||
            frame.Context.TurnNumber != _turnNumber || !HasRuntimeSkill(owner, frame.SkillId) ||
            decision.PlayerSeat != owner.Seat || decision.SkillPrompt.SkillId != frame.SkillId ||
            decision.TargetSeat != owner.Seat || !decision.IsPrivate || decision.Choices.Count != 2 ||
            decision.Choices[0].Id != frame.Plan.Activate.Id || decision.Choices[1].Id != frame.Plan.Skip.Id ||
            frame.Step != ResolutionFrameStep.AwaitingResponse || frame.InstructionIndex != 0 ||
            _status != (owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running))
            throw new InvalidOperationException("The phase activation no longer matches its owner, prompt or boundary.");
    }
}
