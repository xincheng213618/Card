namespace CardGame.Core;

/// <summary>Host adapter: phase scheduling, authoritative effects and command routing.</summary>
public sealed partial class GameEngine
{
    private CommandResult SubmitModulePromptAnswer(PromptChoice selected)
    {
        if (_pendingDecision?.SkillPrompt is null ||
            _resolutionStack.LastOrDefault() is not (PhaseSkillFrame or PindianFrame))
            return Reject(CommandErrorCode.InvalidPrompt, "There is no current module interaction.");
        return Accept(() =>
        {
            if (_resolutionStack[^1] is PindianFrame) ResolvePindianChoice(selected);
            else ResolvePhaseSkillChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private bool IsAiModulePending() =>
        _pendingDecision is { SkillPrompt: not null } decision && !_players[decision.PlayerSeat].IsHuman;

    private void ResolvePendingAiModule()
    {
        if (!IsAiModulePending()) throw new InvalidOperationException("There is no AI module interaction.");
        switch (_resolutionStack.LastOrDefault())
        {
            case PindianFrame: ResolvePendingAiPindian(); break;
            case PhaseSkillFrame phase:
                ResolvePhaseSkillChoice(phase.Plan.AiPrefersActivation ? phase.Plan.Activate : phase.Plan.Skip);
                PublishState();
                break;
            default: throw new InvalidOperationException("The module interaction lost its owning frame.");
        }
    }

    private bool TryBeginPhaseSkill(PhaseSkillWindow window, CharacterState owner)
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
                CountCardsUsedByCurrentPlayerThisTurn(owner.Seat), IsClassicIdentityMode,
                _players.Count(player => player.Seat != owner.Seat && player.IsAlive && GetHand(player).Count > 0));
            if (module.CreatePlan(context) is not { } candidate) continue;
            var plan = candidate.Freeze(skillId);
            if (plan.Effects.Any(effect => effect is BeginSkillPindian) &&
                (context.HandCount == 0 || context.PindianOpponentCount == 0)) continue;
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

    private void ResolvePhaseSkillChoice(PromptChoice selected)
    {
        AssertPhaseSkillInvariant();
        var frame = (PhaseSkillFrame)_resolutionStack[^1];
        var used = selected.Id == frame.Plan.Activate.Id;
        if (!used && selected.Id != frame.Plan.Skip.Id)
            throw new InvalidOperationException("The module choice was not published.");
        ClearPendingDecision();
        if (!used) { CompletePhaseSkill(frame, used: false); return; }
        ContinuePhaseSkill(frame);
    }

    private void ContinuePhaseSkill(PhaseSkillFrame frame)
    {
        var owner = _players[frame.Context.OwnerSeat];
        while (frame.InstructionIndex < frame.Plan.Effects.Count)
        {
            var effectIndex = frame.InstructionIndex;
            var effect = frame.Plan.Effects[effectIndex];
            // Advance before suspending for a child, so resume cannot repeat a paid effect.
            frame = frame with { InstructionIndex = frame.InstructionIndex + 1, Step = ResolutionFrameStep.ResolvingEffect };
            _resolutionStack[^1] = frame;
            switch (effect)
            {
                case DrawSkillCards draw:
                    frame = frame with { DrawnCardIds = Array.AsReadOnly((frame.DrawnCardIds ?? [])
                        .Concat(DrawCards(owner, draw.Count, draw.LogDraw, draw.Reason)).ToArray()) };
                    _resolutionStack[^1] = frame;
                    break;
                case BeginSkillPindian:
                    BeginSharedPindian(frame.Id, frame.Plan.Presentation, owner.Seat);
                    return;
                case GrantNextCardTargetAdjustment adjustment when
                    ShouldApplyPhaseSkillEffect(frame, adjustment.When):
                    var grantedAdjustment = _turnCardUseEffects.GrantTargetAdjustment(
                        _turnNumber,
                        _currentSeat,
                        frame.Id,
                        effectIndex,
                        CreateCardUseEffectSource(frame),
                        adjustment);
                    QueueGameEvent(new CardTargetAdjustmentGrantedEvent(grantedAdjustment));
                    break;
                case GrantNextCardTargetAdjustment:
                    break;
                case ForbidCardUseUntilTurnEnd prohibition when
                    ShouldApplyPhaseSkillEffect(frame, prohibition.When):
                    var grantedProhibition = _turnCardUseEffects.GrantProhibition(
                        _turnNumber,
                        _currentSeat,
                        frame.Id,
                        effectIndex,
                        CreateCardUseEffectSource(frame),
                        prohibition);
                    QueueGameEvent(new CardUseProhibitionGrantedEvent(grantedProhibition));
                    break;
                case ForbidCardUseUntilTurnEnd:
                    break;
                default: throw new InvalidOperationException("Unsupported module skill effect.");
            }
        }
        CompletePhaseSkill(frame, used: true);
    }

    private static CardUseEffectSource CreateCardUseEffectSource(PhaseSkillFrame frame) =>
        new(frame.SkillId, frame.BindingId, frame.Context.OwnerSeat, frame.SkillInstanceId);

    private static bool ShouldApplyPhaseSkillEffect(PhaseSkillFrame frame, SkillEffectCondition condition) =>
        condition switch
        {
            SkillEffectCondition.Always => true,
            SkillEffectCondition.PindianWon => frame.Pindian is { } result &&
                                                  result.SourceSeat == frame.Context.OwnerSeat &&
                                                  result.SourceWon,
            SkillEffectCondition.PindianNotWon => frame.Pindian is { } result &&
                                                     result.SourceSeat == frame.Context.OwnerSeat &&
                                                     !result.SourceWon,
            _ => throw new InvalidOperationException("Unsupported phase skill effect condition.")
        };

    private void CompletePhaseSkill(PhaseSkillFrame frame, bool used)
    {
        var owner = _players[frame.Context.OwnerSeat];
        var module = _contentRegistry!.Skills[frame.SkillId].PhaseSkill!;
        var result = new SkillActivationResult(used, frame.DrawnCardIds ?? [], frame.Pindian);
        if (module.CreateResolvedEvent(frame.Context, result) is { } auditEvent) QueueGameEvent(auditEvent);
        QueueGameEvent(new SkillModuleResolvedEvent(frame.Id, frame.SkillId, owner.Seat, used));
        AddLog(used ? "SkillTriggered" : "SkillSkipped",
            used ? $"{owner.Name} 发动【{frame.Plan.Presentation.Name}】。"
                 : $"{owner.Name} 未发动【{frame.Plan.Presentation.Name}】。", owner.Seat);
        PopResolutionFrame(frame.Id, ResolutionFrameKind.PhaseSkill);
        switch (frame.Window)
        {
            case PhaseSkillWindow.PlayStarting: TryBeginPhaseSkill(PhaseSkillWindow.PlayStarting, owner); break;
            case PhaseSkillWindow.PlayEnding: CompletePlayPhaseAfterProgramWindow(); break;
            case PhaseSkillWindow.TurnEnding: EndTurn(); break;
            default: throw new InvalidOperationException("Unsupported phase continuation.");
        }
    }

    private void AssertPhaseSkillInvariant()
    {
        var frames = _resolutionStack.OfType<PhaseSkillFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_pendingDecision?.SkillPrompt is { } prompt &&
                _contentRegistry?.Skills.GetValueOrDefault(prompt.SkillId)?.PhaseSkill is not null &&
                _resolutionStack.LastOrDefault() is not PindianFrame)
                throw new InvalidOperationException("A module prompt has no phase frame.");
            return;
        }
        if (frames.Length != 1 || _resolutionStack[0] != frames[0])
            throw new InvalidOperationException("A phase skill must own the phase boundary.");
        var frame = frames[0];
        var owner = _players[frame.Context.OwnerSeat];
        if (!owner.IsAlive || owner.Seat != _currentSeat || frame.Context.Phase != _phase ||
            frame.Context.TurnNumber != _turnNumber || !HasRuntimeSkill(owner, frame.SkillId))
            throw new InvalidOperationException("The phase activation no longer matches its owner or boundary.");
        if (_resolutionStack.Count == 2 && _resolutionStack[1] is PindianFrame child &&
            child.ParentFrameId == frame.Id && frame.Step == ResolutionFrameStep.ResolvingEffect &&
            frame.InstructionIndex > 0 && frame.Plan.Effects[frame.InstructionIndex - 1] is BeginSkillPindian)
            return; // The child validates its own private prompt and physical-card ownership.
        if (_resolutionStack.Count != 1 || _pendingDecision is not { SkillPrompt: not null } decision ||
            decision.PlayerSeat != owner.Seat || decision.SkillPrompt.SkillId != frame.SkillId ||
            decision.TargetSeat != owner.Seat || !decision.IsPrivate || decision.Choices.Count != 2 ||
            decision.Choices[0].Id != frame.Plan.Activate.Id || decision.Choices[1].Id != frame.Plan.Skip.Id ||
            frame.Step != ResolutionFrameStep.AwaitingResponse || frame.InstructionIndex != 0 ||
            _status != (owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running))
            throw new InvalidOperationException("The phase activation no longer matches its owner, prompt or boundary.");
    }
}
