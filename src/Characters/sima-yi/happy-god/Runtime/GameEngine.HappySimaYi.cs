namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappySimaRenjie = "happy:renjie", HappySimaJilue = "happy:jilue", HappySimaWansha = "happy:jilue-wansha";
    private static CardConversionSource HappySimaSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool AllowsHappySimaPeach(int actor) => actor == _currentSeat || !_players[_currentSeat].IsAlive || !HasRuntimeSkill(_players[_currentSeat], HappySimaWansha);

    private int HappySimaDiscardCount(int owner, ProgramSkillWindowContext? context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { MovementTiming: { Phase: TurnPhase.Discard } timing } batch } ||
            timing.PhaseActorSeat != owner || batch.TurnNumber != _turnNumber) return 0;
        return batch.Movements.Where(m => _cardMovements.Contains(m) && GetProgramDiscardSource(m) == CardLocation.Hand(owner)).Select(m => m.CardId).Distinct().Count();
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 41840)]
    private bool AllowsHappySimaTrigger(ProgramTriggerLegalityContext g)
    {
        if (g.SkillId == HappySimaRenjie && g.Trigger.Id == "discard-ren") return HappySimaDiscardCount(g.OwnerSeat, g.WindowContext) > 0;
        if (g.SkillId == HappySimaJilue && g.Trigger.Id == "dying-wansha") return g.OwnerSeat == _currentSeat && !HasRuntimeSkill(g.Owner, HappySimaWansha);
        return true;
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 41840, SkillProgramEffectOp.OfferHappySimaJilueFirstDraw)]
    private bool AllowsHappySimaActivation(ProgramActivationLegalityContext g) => g.SkillId != HappySimaJilue || !HasRuntimeSkill(g.Owner, HappySimaWansha);
    [ProgramCapability] private sealed class HappySimaYiHost(GameEngine engine) : IHappySimaYiHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.GainHappySimaRenjie
            ? engine.GainHappySimaRen(f) : engine.BeginHappySimaFirstDraw(f);
    }
    private SkillProgramStepOutcome GainHappySimaRen(ProgramSkillFrame f)
    {
        if (f.SkillId != HappySimaRenjie || f.InstructionIndex != 1 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Happy Renjie requires its actual initialization, damage or frozen discard batch.");
        var amount = f.TriggerId switch
        {
            "initial-ren" when context.Window == SkillProgramTriggerWindow.GameStarting => 1,
            "damage-ren" when context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == f.OwnerSeat => context.Amount,
            "discard-ren" => HappySimaDiscardCount(f.OwnerSeat, context),
            _ => 0
        };
        if (amount <= 0) throw new InvalidOperationException("Happy Renjie lost its exact positive native damage or actual hand-discard count.");
        new ProgramSkillHost(this).ChangeAttributedMarker(f, new(ProgramParticipantRef.Owner), PlayerMarkerKind.Ren, amount);
        return SkillProgramStepOutcome.Continue;
    }

    private long HappySimaFirstAcceptedFrame(int owner)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var start = Array.FindLastIndex(history, e => e is TurnStartedEvent turn && turn.TurnNumber == _turnNumber && turn.ActorSeat == _currentSeat);
        if (start < 0) return 0;
        foreach (var fact in history.Skip(start + 1))
        {
            if (fact is ProgramSkillStartedEvent active && active.SkillId == HappySimaJilue && active.OwnerSeat == owner) return active.FrameId;
            if (fact is ProgramBindingStartedEvent triggered && triggered.SkillId == HappySimaJilue && triggered.OwnerSeat == owner) return triggered.FrameId;
        }
        return 0;
    }
    private PlayerMarkerChangedEvent? HappySimaPaidRen(ProgramSkillFrame f)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var start = Array.FindIndex(history, e => e is ProgramSkillStartedEvent a && a.FrameId == f.Id && a.SkillId == f.SkillId && a.OwnerSeat == f.OwnerSeat && a.ActivationId == f.ActivationId ||
            e is ProgramBindingStartedEvent t && t.FrameId == f.Id && t.SkillId == f.SkillId && t.OwnerSeat == f.OwnerSeat && t.BindingId == f.TriggerId && t.SkillInstanceId == f.SkillInstanceId);
        return start < 0 ? null : history.Take(start).OfType<PlayerMarkerChangedEvent>().LastOrDefault(e => e.PlayerSeat == f.OwnerSeat && e.Marker == PlayerMarkerKind.Ren && e.Delta == -1 &&
            e.Reason == $"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.cost");
    }
    private SkillProgramStepOutcome BeginHappySimaFirstDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != HappySimaJilue || f.HappySimaYi is not null || HappySimaPaidRen(f) is not { } paid)
            throw new InvalidOperationException("Happy Jilue's reward requires its original accepted one-Ren payment.");
        if (HappySimaFirstAcceptedFrame(f.OwnerSeat) != f.Id) return SkillProgramStepOutcome.Continue;
        var r = new HappySimaYiReceipt(HappySimaSource(f), f.GameplayHash, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, f.InstructionIndex, paid.ResolutionId, HappySimaYiStage.Choice);
        ReplaceRuntimeTop(f = f with { HappySimaYi = r });
        AdvanceEventRulesAndQueueFact(new HappySimaJilueFirstOfferedEvent(f.Id, r.Source, r.Hash, r.Parent, r.Turn, r.Instruction, r.Payment));
        PublishHappySimaFirstDraw(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HappySimaFirstChoices(ProgramSkillFrame f) => new[] { ("draw", "本回合首次发动极略，摸一张牌"), ("decline", "不摸牌，继续原极略") }
        .Select(pair => new PromptChoice(new("happy-sima.first." + f.Id + "." + pair.Item1), pair.Item2, [], [], new Dictionary<string, string>
        { ["program-action"] = "happy-sima-first-draw", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["mode"] = pair.Item1 })).ToArray();
    private void PublishHappySimaFirstDraw(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = HappySimaFirstChoices(f);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "极略：本实际回合首次发动，可摸一张牌。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("happy-sima-first-draw")]
    private void AnswerHappySimaFirstDraw(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappySimaYi: { Stage: HappySimaYiStage.Choice } } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !HappySimaFirstChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Happy Jilue requires the exact original first-use choice.");
        RequireHappySimaFirst(f); ClearPendingDecision(); var take = choice.Parameters["mode"] == "draw";
        AdvanceEventRulesAndQueueFact(new HappySimaJilueFirstChosenEvent(f.Id, take));
        if (!take) { CompleteHappySimaFirst(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, HappySimaYi = f.HappySimaYi! with { Stage = HappySimaYiStage.Drawing } });
        BeginOperationProgramDraw(f, f.OwnerSeat, 1, HappySimaDrawReason);
    }
    private static CardMoveReason HappySimaDrawReason => new("skill.happy.jilue.first-draw");
    private void CompleteHappySimaFirst(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.ResolvingEffect, HappySimaYi = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 41840)]
    private bool ResumeHappySimaFirst(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappySimaYi: { } r } f || f.Id != id || _pendingDecision is not null) return false;
        RequireHappySimaFirst(f);
        if (r.Stage == HappySimaYiStage.Choice) { PublishHappySimaFirstDraw(f); return true; }
        if (r.Stage == HappySimaYiStage.Drawing) return true;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        CompleteHappySimaFirst(GetActiveProgramFrame(id)); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 41840)]
    private bool ReturnHappySimaFirstMovement(ProgramSkillFrame f) => f.HappySimaYi is not null && ResumeHappySimaFirst(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.OfferHappySimaJilueFirstDraw)]
    private void ReturnHappySimaFirstDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        RequireHappySimaFirst(f); var r = f.HappySimaYi!;
        if (r.Stage != HappySimaYiStage.Drawing || r.Draw is not null || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Happy Jilue lost its once-requested one-card native draw.");
        ReplaceRuntimeTop(f with { HappySimaYi = r with { Stage = HappySimaYiStage.Children, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HappySimaJilueDrawReturnedEvent(f.Id, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ResumeHappySimaFirst(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.OfferHappySimaJilueFirstDraw)]
    private PromptChoice ChooseHappySimaFirst(PendingDecision p) => p.Choices.First();
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 41840)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 41840)]
    private bool RetainHappySimaFirst(ProgramSkillFrame f) => f.HappySimaYi is not null;
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 41840)]
    private bool ProveHappySimaFirst(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation continuation) =>
        effect?.Op == SkillProgramEffectOp.OfferHappySimaJilueFirstDraw && f.HappySimaYi is { Stage: HappySimaYiStage.Children } &&
        continuation.SubjectSeat == f.OwnerSeat && continuation.BeforeCount == 0 && continuation.CoverageResultBind is null && ValidHappySimaDraw(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 41840)]
    private void RequireHappySimaFirst(ProgramSkillFrame f)
    {
        if (f.HappySimaYi is not { } r) return;
        if (f.SkillId != HappySimaJilue || r.Source != HappySimaSource(f) || r.Hash != f.GameplayHash || r.Parent != (f.WindowContext?.ParentFrameId ?? 0) ||
            r.Turn != _turnNumber || r.Instruction != f.InstructionIndex || HappySimaFirstAcceptedFrame(f.OwnerSeat) != f.Id ||
            HappySimaPaidRen(f)?.ResolutionId != r.Payment || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<HappySimaJilueFirstOfferedEvent>().Count(e => e == new HappySimaJilueFirstOfferedEvent(f.Id, r.Source, r.Hash, r.Parent, r.Turn, r.Instruction, r.Payment)) != 1 ||
            r.Stage == HappySimaYiStage.Children && !ValidHappySimaDraw(f))
            throw new InvalidOperationException("Happy Jilue lost its actual accepted first frame, one-Ren cost, source or native draw receipt.");
    }
    private bool ValidHappySimaDraw(ProgramSkillFrame f) => f.HappySimaYi?.Draw is { } draw && draw.RequestedCount == 1 && draw.RecipientSeat == f.OwnerSeat &&
        draw.SequenceBefore <= draw.SequenceAfter && draw.SequenceAfter <= _movementSequence &&
        _cardMovements.Where(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == HappySimaDrawReason)
            .Select(m => m.CardId).SequenceEqual(draw.ActualDrawIds) &&
        ProgramEventHistory<HappySimaJilueFirstChosenEvent>().Count(e => e.FrameId == f.Id && e.Draw) == 1 &&
        ProgramEventHistory<HappySimaJilueDrawReturnedEvent>().Count(e => e == new HappySimaJilueDrawReturnedEvent(f.Id, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId)) == 1;
}
