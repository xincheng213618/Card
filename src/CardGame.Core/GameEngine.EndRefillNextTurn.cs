using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class EndRefillNextTurnProgramCapability(GameEngine engine) : IEndRefillNextTurnProgramHost
    {
        public SkillProgramStepOutcome RefillAndScheduleNextTurnSelfOnly(ProgramSkillFrame f) => engine.BeginEndRefillNextTurn(f);
    }
    private static string EndRefillReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.end-refill-next-turn.draw";
    private bool IsExactOwnAfterTurnEndedProgram(ProgramSkillFrame f) =>
        f.WindowContext is { Window: SkillProgramTriggerWindow.AfterTurnEnded } context &&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat == f.OwnerSeat && parent.TurnNumber == _turnNumber && parent.OwnerSeat == _currentSeat &&
        parent.AfterTurnEnded?.CurrentChild?.FrameId == f.Id && MatchesAfterTurnEndedChild(parent, f, context);
    private SkillProgramStepOutcome BeginEndRefillNextTurn(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!IsExactOwnAfterTurnEndedProgram(f) || f.InstructionIndex != 1 || f.EndRefillNextTurn is not null)
            throw new InvalidOperationException("End refill requires its actual completed own turn and one unissued instruction.");
        var r = new EndRefillNextTurnReceipt { WindowFrameId = f.WindowContext!.ParentFrameId,
            Source = CreateProgramTurnEffectSource(f), GameplayHash = f.GameplayHash, ActualTurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _currentSeat, Stage = EndRefillNextTurnStage.Target,
            Targets = _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray() };
        ReplaceRuntimeTop(f = f with { EndRefillNextTurn = r });
        AdvanceEventRulesAndQueueFact(new EndRefillNextTurnOfferedEvent(f.Id, r));
        PublishEndRefillNextTurnChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> EndRefillNextTurnChoices(ProgramSkillFrame f)
    {
        var r = f.EndRefillNextTurn!;
        var parameters = new Dictionary<string, string> { ["program-action"] = "end-refill-next-turn",
            ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        return r.Targets.Where(s => _players[s].IsAlive).Select(s => new PromptChoice(
            new($"end-refill-next-turn.{f.Id}.{s}"), $"令 {_players[s].Name} 摸至 {Math.Min(5, _players[s].MaxHp)} 张手牌，并于其下回合开始检查目标限制",
            [], [s], parameters)).Append(new(new($"end-refill-next-turn.{f.Id}.decline"), "不执行", [], [], parameters)).ToArray();
    }
    private void PublishEndRefillNextTurnChoice(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var choices = EndRefillNextTurnChoices(f);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse,
            EndRefillNextTurn = f.EndRefillNextTurn! with { Choices = choices } });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】选择一名角色。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = f.EndRefillNextTurn!.Choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("end-refill-next-turn")]
    private void ResolveEndRefillNextTurnChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("End refill lost its frame.");
        RequireEndRefillNextTurnReceipt(f);
        if (f.EndRefillNextTurn!.Stage != EndRefillNextTurnStage.Target ||
            !AssistedChoicesEqual([choice], f.EndRefillNextTurn.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], EndRefillNextTurnChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("End refill selected a stale participant or an already issued draw.");
        ClearPendingDecision();
        if (choice.Targets.Count == 0) { CompleteEndRefillNextTurn(f); return; }
        var target = _players[choice.Targets.Single()]; var hand = GetHand(target).Count;
        var requested = Math.Max(0, Math.Min(target.MaxHp, 5) - hand); var before = _movementSequence;
        var r = f.EndRefillNextTurn with { Stage = EndRefillNextTurnStage.MovementChildren, TargetSeat = target.Seat,
            FrozenHandCount = hand, FrozenMaxHp = target.MaxHp, DrawRequested = requested,
            MovementBefore = before, MovementAfter = before, Choices = [] };
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, EndRefillNextTurn = r,
            PendingMovementContinuation = new(target.Seat, 0, null) });
        var actual = DrawCards(target, requested, true, new(EndRefillReason(f))).Count;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { EndRefillNextTurn = r with { DrawActual = actual, MovementAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new EndRefillNextTurnIssuedEvent(f.Id, f.EndRefillNextTurn!));
        if (!DrainEndRefillNextTurnChildren(f)) ReturnEndRefillNextTurnMovement(f);
    }
    private bool DrainEndRefillNextTurnChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void CompleteEndRefillNextTurn(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new EndRefillNextTurnCompletedEvent(f.Id, f.EndRefillNextTurn!.TargetSeat));
        ReplaceRuntimeTop(f = f with { EndRefillNextTurn = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20560)]
    private bool ReturnEndRefillNextTurnMovement(ProgramSkillFrame f)
    {
        if (f.EndRefillNextTurn is null) return false;
        RequireEndRefillNextTurnReceipt(f);
        if (f.EndRefillNextTurn.Stage != EndRefillNextTurnStage.MovementChildren || f.PendingMovementContinuation is null)
            throw new InvalidOperationException("A refill movement returned without its frozen issued receipt.");
        if (DrainEndRefillNextTurnChildren(f)) return true;
        CompleteEndRefillNextTurn(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20560)]
    private bool ResumeEndRefillNextTurn(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { EndRefillNextTurn: not null } f || f.Id != id) return false;
        RequireEndRefillNextTurnReceipt(f);
        if (f.PendingMovementContinuation is not null && !DrainEndRefillNextTurnChildren(f)) ReturnEndRefillNextTurnMovement(f);
        return true;
    }
    private void RequireEndRefillNextTurnReceipt(ProgramSkillFrame f)
    {
        var r = f.EndRefillNextTurn ?? throw new InvalidOperationException("End refill lost its receipt.");
        if (!IsExactOwnAfterTurnEndedProgram(f) || f.InstructionIndex != 1 || f.TriggerId is null ||
            r.WindowFrameId != f.WindowContext!.ParentFrameId || r.Source != CreateProgramTurnEffectSource(f) ||
            r.GameplayHash != f.GameplayHash || r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly ||
            r.Targets.Count != r.Targets.Distinct().Count() || r.Targets.Any(s => !IsValidPlayerSeat(s)) ||
            CompleteProgramEventHistory().OfType<EndRefillNextTurnOfferedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Receipt.WindowFrameId == r.WindowFrameId && e.Receipt.Source == r.Source && e.Receipt.GameplayHash == r.GameplayHash &&
                e.Receipt.ActualTurnNumber == r.ActualTurnNumber && e.Receipt.Targets.SequenceEqual(r.Targets)) != 1)
            throw new InvalidOperationException("End refill lost its actual parent, source, candidate or instruction.");
        var issued = CompleteProgramEventHistory().OfType<EndRefillNextTurnIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Stage == EndRefillNextTurnStage.Target)
        {
            if (r.TargetSeat != -1 || issued.Length != 0 || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("An unissued refill retained a paid movement.");
            return;
        }
        if (r.Stage != EndRefillNextTurnStage.MovementChildren || issued is not [var fact] || !SameEndRefillReceipt(fact.Receipt, r) ||
            !r.Targets.Contains(r.TargetSeat) || r.FrozenHandCount < 0 || r.FrozenMaxHp < 0 ||
            r.DrawRequested != Math.Max(0, Math.Min(r.FrozenMaxHp, 5) - r.FrozenHandCount) || r.DrawActual < 0 || r.DrawActual > r.DrawRequested ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != r.TargetSeat)
            throw new InvalidOperationException("A paid refill lost its exact single movement producer.");
        var movements = _cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter && m.Reason.Value == EndRefillReason(f)).ToArray();
        if (movements.Length != r.DrawActual || movements.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(r.TargetSeat)))
            throw new InvalidOperationException("A paid refill does not match the actual private card movements.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 60)]
    private void AssertRegisteredEndRefillNextTurnReceipt(ProgramSkillFrame frame)
    {
        if (frame.EndRefillNextTurn is not null) RequireEndRefillNextTurnReceipt(frame);
    }
    private bool IsEndRefillNextTurnMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.EndRefillNextTurn is { Stage: EndRefillNextTurnStage.MovementChildren } r && e?.Op == SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly &&
        p.SubjectSeat == r.TargetSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 450)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 440)]
    private bool CanContinueEndRefillNextTurn(ProgramSkillFrame f)
    {
        if (f.EndRefillNextTurn is not { Stage: EndRefillNextTurnStage.MovementChildren }) return false;
        RequireEndRefillNextTurnReceipt(f); return true;
    }
    private static bool SameEndRefillReceipt(EndRefillNextTurnReceipt a, EndRefillNextTurnReceipt b) =>
        a.WindowFrameId == b.WindowFrameId && a.Source == b.Source && a.GameplayHash == b.GameplayHash &&
        a.ActualTurnNumber == b.ActualTurnNumber && a.ActualTurnOwnerSeat == b.ActualTurnOwnerSeat && a.Stage == b.Stage &&
        a.TargetSeat == b.TargetSeat && a.FrozenHandCount == b.FrozenHandCount && a.FrozenMaxHp == b.FrozenMaxHp &&
        a.DrawRequested == b.DrawRequested && a.DrawActual == b.DrawActual && a.MovementBefore == b.MovementBefore &&
        a.MovementAfter == b.MovementAfter && a.Targets.SequenceEqual(b.Targets);
    private void ConsumeEndRefillNextTurn(CharacterState current)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly)) return;
        var history = CompleteProgramEventHistory().ToArray();
        var consumed = history.OfType<EndRefillNextTurnConsumedEvent>().Select(e => e.ProgramFrameId).ToHashSet();
        var actual = history.OfType<TurnStartedEvent>().LastOrDefault();
        if (actual is null || actual.TurnNumber != _turnNumber || actual.ActorSeat != current.Seat)
            throw new InvalidOperationException("Refill due consumption requires the actual next turn-start fact.");
        foreach (var e in history.OfType<EndRefillNextTurnIssuedEvent>().Where(e => e.Receipt.TargetSeat == current.Seat &&
            e.Receipt.ActualTurnNumber < _turnNumber && !consumed.Contains(e.ProgramFrameId)))
        {
            var r = e.Receipt; var hand = GetHand(current).Count;
            var greatest = _players.Where(p => p.IsAlive).Select(p => GetHand(p).Count).DefaultIfEmpty(0).Max();
            var restricted = current.IsAlive && hand >= greatest;
            AdvanceEventRulesAndQueueFact(new EndRefillNextTurnConsumedEvent(e.ProgramFrameId, current.Seat, _turnNumber, hand, greatest, restricted));
            if (!restricted) continue;
            var granted = _turnCardUseEffects.GrantTargetRestriction(_turnNumber, current.Seat, e.ProgramFrameId, 0,
                r.Source, SkillProgramCardTargetRestriction.SelfOnly, null, current.Seat == r.Source.OwnerSeat ? null : current.Seat);
            AdvanceEventRulesAndQueueFact(new CardTargetRestrictionGrantedEvent(granted));
        }
    }
    private double EndRefillPublicChoiceScore(int owner, int target)
    {
        var snapshot = CreateSnapshot(owner); var p = snapshot.Players[target];
        if (!p.IsAlive) return double.NegativeInfinity;
        var draw = Math.Max(0, Math.Min(p.MaxHp, 5) - p.HandCount);
        var cancelDraw = _aiBrains[owner].ScoreCardEffectIntervention(snapshot, CardKind.DrawTwo, target);
        var likelyMost = p.HandCount + draw >= snapshot.Players.Where(q => q.IsAlive && q.Seat != target).Select(q => q.HandCount).DefaultIfEmpty(0).Max();
        return -cancelDraw * draw / 2d + (likelyMost ? cancelDraw * .5d : 0d);
    }
    [ProgramAiChoice(SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly)]
    private PromptChoice SelectAiEndRefillNextTurn(PendingDecision prompt, ProgramSkillFrame f) =>
        prompt.Choices.OrderByDescending(c => c.Targets.Count == 0 ? 0d : EndRefillPublicChoiceScore(f.OwnerSeat, c.Targets[0])).First();
    private double EndRefillNextTurnPublicScore(int owner) => CreateSnapshot(owner).Players.Where(p => p.IsAlive)
        .Select(p => EndRefillPublicChoiceScore(owner, p.Seat)).Append(0d).Max();
}
