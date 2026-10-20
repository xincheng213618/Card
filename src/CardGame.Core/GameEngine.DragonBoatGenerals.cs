using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record DragonBoatActualUseEvent(long UseId, long ActionId, int ActualTurn, int PhaseInstance,
    int ActorSeat, int TargetCount) : IGameEvent;
public sealed record DragonBoatActualUseTargetEvent(long UseId, long ActionId, int Index, int TargetSeat) : IGameEvent;
public sealed record DragonBoatDianhuArmedEvent(long FrameId, int OwnerSeat, string SkillInstanceId,
    int TargetSeat, long GameStartingWindowId) : IGameEvent;
public sealed record DragonBoatDrawPaidEvent(long FrameId, string SkillId, int OwnerSeat, int RecipientSeat,
    int RequestedCount, int ActualCount, long SequenceBefore, long SequenceAfter, long? AttemptFrameId, bool Replaced) : IGameEvent;
public sealed record DragonBoatXingzhaoGrantEvent(int SourceSeat, int RecipientSeat, string GrantId,
    string SkillId, string SkillInstanceId, bool Added) : IGameEvent;
public enum DragonBoatGeneralStage { Choosing, DrawAttempt, DrawChildren }
public sealed record DragonBoatGeneralReceipt(int InstructionIndex, long WindowId, int ActualTurn,
    DragonBoatGeneralStage Stage, int? RecipientSeat = null, int RequestedCount = 0, NativeDrawOutcome? Draw = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DragonBoatGeneralReceipt? DragonBoatGeneral { get; init; }
}

public sealed partial class GameEngine
{
    private bool DragonBoatAllies(int owner, int other) => IsDragonBoatActivity && IsValidPlayerSeat(owner) &&
        IsValidPlayerSeat(other) && _players[owner].IsAlive && _players[other].IsAlive &&
        _players[owner].TeamId is { } team && _players[other].TeamId == team;

    [ProgramCapability]
    private sealed class DragonBoatGeneralHost(GameEngine engine) : IDragonBoatGeneralHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation, int targetSeat) =>
            engine.BeginDragonBoatGeneral(frame, operation, targetSeat);
    }
    [ProgramCapability]
    private sealed class DragonBoatAllyQuery(GameEngine engine) : IDragonBoatAllyQuery
    {
        public bool IsTeammate(int ownerSeat, int targetSeat) => ownerSeat != targetSeat && engine.DragonBoatAllies(ownerSeat, targetSeat);
    }

    // Called by the actual finalized-card-use producer, before collecting skills.
    // Scalar facts retain the previous action even after its native frame returns.
    private void CaptureDragonBoatFinalizedUse(long useId, CardActionContext action)
    {
        if (!IsDragonBoatActivity || _phase != TurnPhase.Play || _turnNumber < 1 ||
            action.Type != CardActionType.Use || action.ActorSeat != _currentSeat) return;
        if (LifecycleCardUse(useId) is not { Action: { } actual } use || actual.ActionId != action.ActionId ||
            !SameOverflowAction(actual, action) || !HasExactAcceptedActualHandGainUse(use, action))
            throw new InvalidOperationException("Dragon Boat Lianpian requires its exact finalized native card use.");
        if (ProgramEventHistory<DragonBoatActualUseEvent>().Any(e => e.UseId == useId)) return;
        var targets = SuFeiPrimaryTargets(action);
        AdvanceEventRulesAndQueueFact(new DragonBoatActualUseEvent(useId, action.ActionId, _turnNumber,
            _cardUseDebitPhaseInstanceId, action.ActorSeat, targets.Length));
        for (var index = 0; index < targets.Length; index++)
            AdvanceEventRulesAndQueueFact(new DragonBoatActualUseTargetEvent(useId, action.ActionId, index, targets[index]));
    }

    private IReadOnlyList<int> DragonBoatLianpianTargets(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (!IsDragonBoatActivity || context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            _phase != TurnPhase.Play || identity.ActorSeat != _currentSeat || !DragonBoatAllies(ownerSeat, identity.ActorSeat) ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { } action } use || action.ActionId != identity.CardActionId)
            return [];
        var uses = ProgramEventHistory<DragonBoatActualUseEvent>().Where(e => e.ActualTurn == _turnNumber &&
            e.PhaseInstance == _cardUseDebitPhaseInstanceId && e.ActorSeat == action.ActorSeat).ToArray();
        if (uses.Length < 2 || uses[^1].UseId != use.Id || uses[^1].ActionId != action.ActionId) return [];
        var previous = ProgramEventHistory<DragonBoatActualUseTargetEvent>().Where(e => e.UseId == uses[^2].UseId &&
            e.ActionId == uses[^2].ActionId).OrderBy(e => e.Index).Select(e => e.TargetSeat).ToArray();
        var current = ProgramEventHistory<DragonBoatActualUseTargetEvent>().Where(e => e.UseId == use.Id &&
            e.ActionId == action.ActionId).OrderBy(e => e.Index).Select(e => e.TargetSeat).ToArray();
        if (previous.Length != uses[^2].TargetCount || current.Length != uses[^1].TargetCount ||
            !current.SequenceEqual(SuFeiPrimaryTargets(action)))
            throw new InvalidOperationException("Activity Lianpian lost its actual consecutive target facts.");
        return Array.AsReadOnly(previous.Intersect(current).Where(s => IsValidPlayerSeat(s) && _players[s].IsAlive).ToArray());
    }

    private DragonBoatDianhuArmedEvent? DragonBoatDianhuTarget(int ownerSeat, string instance) =>
        ProgramEventHistory<DragonBoatDianhuArmedEvent>().SingleOrDefault(e => e.OwnerSeat == ownerSeat && e.SkillInstanceId == instance);
    private bool DragonBoatDianhuQualified(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsDragonBoatActivity && context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source,
            TargetSeat: { } target, Amount: > 0, DamageFrameId: { } damageId } && DragonBoatAllies(candidate.OwnerSeat, source) &&
        DragonBoatDianhuTarget(candidate.OwnerSeat, candidate.SkillInstanceId) is { } relation && relation.TargetSeat == target &&
        context.OccurrenceIndex >= 0 && context.OccurrenceIndex < context.Amount &&
        _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is { } window &&
        window.ParentFrameId == damageId && GetDamageTriggerAttack(window) is { IsSourceLess: false, DamageWasApplied: true } attack &&
        attack.SourceSeat == source && attack.TargetSeat == target && attack.DamageAmount == context.Amount;

    private bool DragonBoatTunjiangQualified(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (!IsDragonBoatActivity || context.Window != SkillProgramTriggerWindow.TurnEnding ||
            context.SourceSeat != _currentSeat || context.TargetSeat != _currentSeat || !DragonBoatAllies(ownerSeat, _currentSeat)) return false;
        // TurnStarted is an actual-turn boundary, including granted extra turns.
        var start = CompleteProgramEventHistory().ToList().FindLastIndex(e => e is TurnStartedEvent);
        return start >= 0 && !CompleteProgramEventHistory().Skip(start + 1).OfType<DamageAppliedEvent>()
            .Any(e => !e.SourceLess && e.SourceSeat == _currentSeat && e.Amount > 0);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 42000)]
    private bool AllowsDragonBoatGeneralTrigger(ProgramTriggerLegalityContext gate)
    {
        if (!gate.SkillId.StartsWith("dragonboat:", StringComparison.Ordinal)) return true;
        if (!IsDragonBoatActivity) return false;
        if (gate.SkillId == "dragonboat:lianpian") return DragonBoatLianpianTargets(gate.OwnerSeat, gate.Context).Count > 0;
        if (gate.SkillId == "dragonboat:dianhu" && gate.Context.Window == SkillProgramTriggerWindow.AfterDamageApplied)
            return DragonBoatDianhuQualified(gate.Candidate, gate.Context);
        if (gate.SkillId == "dragonboat:tunjiang") return DragonBoatTunjiangQualified(gate.OwnerSeat, gate.Context);
        if (gate.SkillId is "dragonboat:xingzhao" or "dragonboat:xingzhao-benefit")
            return _players[gate.OwnerSeat].TeamId is { } team && DragonBoatTeamScore(team) >=
                (gate.Context.Window == SkillProgramTriggerWindow.DiscardPhaseStarting ? 6 : 4);
        return true;
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 42000, SkillProgramEffectOp.DrawAndOfferJianjiExactCard)]
    private bool AllowsDragonBoatJianji(ProgramActivationLegalityContext gate) => gate.SkillId != "dragonboat:jianji" ||
        IsDragonBoatActivity && _players.Any(p => p.Seat != gate.OwnerSeat && DragonBoatAllies(gate.OwnerSeat, p.Seat));

    private SkillProgramStepOutcome BeginDragonBoatGeneral(ProgramSkillFrame supplied, SkillProgramEffectOp operation, int targetSeat)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsDragonBoatActivity || frame.TriggerId is null || frame.WindowContext is not { } context ||
            frame.DragonBoatGeneral is not null || !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("The activity general lost its actual unpaid trigger.");
        if (operation == SkillProgramEffectOp.ArmDragonBoatDianhu)
        {
            if (frame.SkillId != "dragonboat:dianhu" || context.Window != SkillProgramTriggerWindow.GameStarting ||
                frame.InstructionIndex != 2 || frame.SelectedTargetSeats is not [var selected] || selected != targetSeat ||
                targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive || DragonBoatDianhuTarget(frame.OwnerSeat, frame.SkillInstanceId) is not null)
                throw new InvalidOperationException("Activity Dianhu must record one real game-start target once.");
            AdvanceEventRulesAndQueueFact(new DragonBoatDianhuArmedEvent(frame.Id, frame.OwnerSeat, frame.SkillInstanceId,
                targetSeat, context.ParentFrameId));
            return SkillProgramStepOutcome.Continue;
        }
        if (frame.InstructionIndex != 1) throw new InvalidOperationException("Activity draw must have one exact producer.");
        var expected = operation switch
        {
            SkillProgramEffectOp.DrawDragonBoatConsecutiveTarget => "dragonboat:lianpian",
            SkillProgramEffectOp.DrawDragonBoatDianhu => "dragonboat:dianhu",
            SkillProgramEffectOp.DrawDragonBoatTunjiang => "dragonboat:tunjiang",
            _ => throw new InvalidOperationException("Unknown activity general operation.")
        };
        if (frame.SkillId != expected) throw new InvalidOperationException("Activity general operation has the wrong source.");
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId,
            frame.GameplayHash, 0);
        if (operation == SkillProgramEffectOp.DrawDragonBoatDianhu && !DragonBoatDianhuQualified(candidate, context) ||
            operation == SkillProgramEffectOp.DrawDragonBoatTunjiang && !DragonBoatTunjiangQualified(frame.OwnerSeat, context))
            throw new InvalidOperationException("The activity draw lost its exact native occurrence.");
        ReplaceRuntimeTop(frame = frame with { DragonBoatGeneral = new(frame.InstructionIndex, context.ParentFrameId,
            _turnNumber, DragonBoatGeneralStage.Choosing) });
        if (operation == SkillProgramEffectOp.DrawDragonBoatDianhu)
        { IssueDragonBoatGeneralDraw(frame, frame.OwnerSeat, 1); return SkillProgramStepOutcome.AwaitChild; }
        PublishDragonBoatGeneralChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> DragonBoatGeneralChoices(ProgramSkillFrame frame)
    {
        if (frame.DragonBoatGeneral is not { Stage: DragonBoatGeneralStage.Choosing } || frame.WindowContext is not { } context) return [];
        var targets = frame.SkillId == "dragonboat:lianpian" ? DragonBoatLianpianTargets(frame.OwnerSeat, context) :
            DragonBoatTunjiangQualified(frame.OwnerSeat, context) ? new[] { frame.OwnerSeat, _currentSeat }.Distinct().ToArray() : [];
        return Array.AsReadOnly(targets.Where(seat => _players[seat].IsAlive).Select(seat => new PromptChoice(
            new($"dragonboat.{frame.Id}.draw-{seat}"), $"令{_players[seat].Name}摸{(frame.SkillId == "dragonboat:tunjiang" ? 2 : 1)}张牌",
            [], [seat], new Dictionary<string, string> { ["program-action"] = "dragonboat-general" })).ToArray());
    }
    private void PublishDragonBoatGeneralChoice(ProgramSkillFrame frame)
    {
        var choices = DragonBoatGeneralChoices(frame);
        if (choices.Count == 0) { ReplaceRuntimeTop(frame = frame with { DragonBoatGeneral = null }); FinishProgramSkill(frame, true); return; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "选择本次摸牌的角色。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.DrawDragonBoatConsecutiveTarget, SkillProgramEffectOp.DrawDragonBoatTunjiang)]
    private PromptChoice ChooseDragonBoatGeneral(ProgramSkillFrame frame) => DragonBoatGeneralChoices(frame)
        .OrderByDescending(c => DragonBoatAllies(frame.OwnerSeat, c.Targets.Single())).ThenBy(c => GetHand(_players[c.Targets.Single()]).Count).First();
    [ProgramAnswer("dragonboat-general")]
    private void AnswerDragonBoatGeneral(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DragonBoatGeneral.Stage: DragonBoatGeneralStage.Choosing } frame ||
            _pendingDecision?.PlayerSeat != frame.OwnerSeat || !DragonBoatGeneralChoices(frame).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("The activity draw requires its exact published participant choice.");
        ClearPendingDecision();
        IssueDragonBoatGeneralDraw(frame, choice.Targets.Single(), frame.SkillId == "dragonboat:tunjiang" ? 2 : 1);
    }
    private static CardMoveReason DragonBoatGeneralDrawReason(string skillId) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DragonBoatGenerals.DragonBoatGeneralDrawReason(skillId);
    private void IssueDragonBoatGeneralDraw(ProgramSkillFrame frame, int recipient, int count)
    {
        if (frame.DragonBoatGeneral is not { Stage: DragonBoatGeneralStage.Choosing } receipt || !_players[recipient].IsAlive)
            throw new InvalidOperationException("An activity draw cannot repay a completed recipient.");
        ReplaceRuntimeTop(frame = frame with { DragonBoatGeneral = receipt with { Stage = DragonBoatGeneralStage.DrawAttempt,
            RecipientSeat = recipient, RequestedCount = count }, Step = ResolutionFrameStep.ResolvingEffect });
        BeginOperationProgramDraw(frame, recipient, count, DragonBoatGeneralDrawReason(frame.SkillId));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawDragonBoatConsecutiveTarget, SkillProgramEffectOp.DrawDragonBoatDianhu,
        SkillProgramEffectOp.DrawDragonBoatTunjiang)]
    private void ReturnDragonBoatGeneralDraw(ProgramSkillFrame input, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (frame.NativeDraw is not null || frame.DragonBoatGeneral is not { Stage: DragonBoatGeneralStage.DrawAttempt } receipt ||
            receipt.RecipientSeat != outcome.RecipientSeat || receipt.RequestedCount != outcome.RequestedCount ||
            outcome.SequenceAfter != _movementSequence || outcome.SequenceBefore > outcome.SequenceAfter ||
            outcome.ActualDrawIds.Count > receipt.RequestedCount || outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(outcome.RecipientSeat) &&
                m.Reason == DragonBoatGeneralDrawReason(frame.SkillId)).OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds))
            throw new InvalidOperationException("The activity draw lost its native request/material return.");
        ReplaceRuntimeTop(frame = frame with { DragonBoatGeneral = receipt with { Stage = DragonBoatGeneralStage.DrawChildren, Draw = outcome } });
        AdvanceEventRulesAndQueueFact(new DragonBoatDrawPaidEvent(frame.Id, frame.SkillId, frame.OwnerSeat, outcome.RecipientSeat,
            outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId,
            outcome.Replacement is not null));
        ResumeDragonBoatGeneral(frame.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 42000)]
    private bool ResumeDragonBoatGeneral(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DragonBoatGeneral: { } receipt } frame || frame.Id != id) return false;
        if (receipt.Stage == DragonBoatGeneralStage.Choosing) { if (_pendingDecision is null) PublishDragonBoatGeneralChoice(frame); return true; }
        if (receipt.Stage == DragonBoatGeneralStage.DrawAttempt) throw new InvalidOperationException("The activity draw resumed before its native request returned.");
        AssertDragonBoatGeneral(frame);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(id) with { DragonBoatGeneral = null });
        FinishProgramSkill(frame, true);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 42000)]
    private bool ReturnDragonBoatGeneralMovement(ProgramSkillFrame frame) => frame.DragonBoatGeneral is not null && ResumeDragonBoatGeneral(frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 42000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42000)]
    private bool ContinuePaidDragonBoatGeneral(ProgramSkillFrame frame)
    { if (frame.DragonBoatGeneral is not { Stage: DragonBoatGeneralStage.DrawAttempt or DragonBoatGeneralStage.DrawChildren }) return false; AssertDragonBoatGeneral(frame); return true; }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 42000)]
    private void AssertDragonBoatGeneral(ProgramSkillFrame frame)
    {
        if (frame.DragonBoatGeneral is not { } receipt) return;
        if (!IsDragonBoatActivity || receipt.InstructionIndex != frame.InstructionIndex || frame.InstructionIndex != 1 ||
            receipt.ActualTurn != _turnNumber || receipt.WindowId != frame.WindowContext?.ParentFrameId ||
            frame.SkillId is not ("dragonboat:lianpian" or "dragonboat:dianhu" or "dragonboat:tunjiang"))
            throw new InvalidOperationException("The activity general lost its actual owning trigger.");
        if (receipt.Stage == DragonBoatGeneralStage.Choosing)
        { if (receipt.RecipientSeat is not null || receipt.Draw is not null || frame.NativeDraw is not null) throw new InvalidOperationException("An unpaid activity choice cannot retain a draw."); return; }
        if (receipt.RecipientSeat is not { } recipient || !IsValidPlayerSeat(recipient) || receipt.RequestedCount is not (1 or 2))
            throw new InvalidOperationException("The activity draw changed its frozen recipient.");
        if (receipt.Stage == DragonBoatGeneralStage.DrawAttempt) return;
        if (receipt.Draw is not { } draw || draw.RecipientSeat != recipient || draw.RequestedCount != receipt.RequestedCount ||
            ProgramEventHistory<DragonBoatDrawPaidEvent>().Count(e => e == new DragonBoatDrawPaidEvent(frame.Id, frame.SkillId,
                frame.OwnerSeat, recipient, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter,
                draw.AttemptFrameId, draw.Replacement is not null)) != 1)
            throw new InvalidOperationException("The activity draw must retain its once-issued native payment fact.");
    }

    private IEnumerable<CharacterState> WenjiEligibleGiftRecipients(int ownerSeat, string skillId) => _players.Where(p => p.IsAlive &&
        p.Seat != ownerSeat && (skillId != "dragonboat:wenji" || DragonBoatAllies(ownerSeat, p.Seat)) && WenjiPayableCards(p.Seat).Count > 0);

    [ProgramAiChoice(SkillProgramEffectOp.DrawAndOfferJianjiExactCard)]
    private PromptChoice SelectActivityAwareJianjiChoice(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        DragonBoatJianjiAiChoice(decision, frame) ?? ProgramAiChoiceSelector.SelectAiJianjiChoice(decision, frame, effect);

    private PromptChoice? DragonBoatJianjiAiChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        if (frame.SkillId != "dragonboat:jianji" || !IsDragonBoatActivity || frame.HuangQuan is not { Stage: "offer" } pending) return null;
        var actions = JianjiActions(frame);
        var ranked = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("option") != "decline")
            .Select(choice =>
            {
                var index = int.Parse(choice.Parameters["option"], System.Globalization.CultureInfo.InvariantCulture);
                var action = actions[index];
                var harmful = action.Kind is LegalActionKind.Slash or LegalActionKind.Duel or LegalActionKind.FireAttack or
                    LegalActionKind.Snatch or LegalActionKind.Dismantlement or LegalActionKind.AgainstCurrent;
                var score = harmful ? action.TargetSeats.Any(s => DragonBoatAllies(pending.RecipientSeat, s)) ? -100 : 20 : 10;
                if (harmful && action.TargetSeats.Any(target => ProgramEventHistory<DragonBoatDianhuArmedEvent>().Any(e =>
                    e.OwnerSeat == frame.OwnerSeat && e.TargetSeat == target && HasRuntimeSkillInstance(_players[e.OwnerSeat], "dragonboat:dianhu", e.SkillInstanceId))))
                    score += 5;
                if (action.Kind is LegalActionKind.Zong or LegalActionKind.Peach or LegalActionKind.SameBoat &&
                    action.TargetSeats.Any(s => !DragonBoatAllies(pending.RecipientSeat, s))) score = -100;
                return (Choice: choice, Score: score);
            }).OrderByDescending(item => item.Score).ToArray();
        return ranked.Length != 0 && ranked[0].Score > 0 ? ranked[0].Choice :
            decision.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == "decline");
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawAndOfferJianjiExactCard)]
    private void ReturnDragonBoatJianjiDraw(ProgramSkillFrame input, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (frame.SkillId != "dragonboat:jianji" || !IsDragonBoatActivity || frame.NativeDraw is not null ||
            frame.HuangQuan is not { Kind: "jianji", Stage: "drawing" } pending || pending.RecipientSeat != outcome.RecipientSeat ||
            outcome.RequestedCount != 1 || outcome.ActualDrawIds.Count > 1 || outcome.SequenceAfter != _movementSequence ||
            outcome.SequenceBefore > outcome.SequenceAfter || outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(outcome.RecipientSeat) && m.Reason.Value == "program.jianji.draw")
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds))
            throw new InvalidOperationException("Activity Jianji lost its exact native draw request and actual card.");
        pending = pending with { Before = outcome.SequenceBefore, After = outcome.SequenceAfter,
            CardIds = outcome.ActualDrawIds, Stage = "draw-paid" };
        ReplaceRuntimeTop(frame = frame with { HuangQuan = pending, PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HuangQuanDrawPaidEvent(frame.Id, pending.Source, frame.GameplayHash, "jianji", pending.RecipientSeat,
            pending.ActualTurn, pending.TurnOwner, pending.ActualPhase, pending.Before, pending.After, pending.CardIds.Count, null));
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool _synchronizingDragonBoatGrants;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42000)]
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 42000)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 42000)]
    private void SynchronizeDragonBoatXingzhao()
    {
        if (!IsDragonBoatActivity || _synchronizingDragonBoatGrants || _players.Count != 8) return;
        _synchronizingDragonBoatGrants = true;
        try
        {
            var desired = new Dictionary<(int Seat, string Grant), (int Source, SkillGrant Grant)>();
            foreach (var source in _players.Where(p => p.IsAlive && EnabledContentSkillIds(p).Contains("dragonboat:xingzhao", StringComparer.Ordinal)))
            foreach (var instance in GetSkillBindingShard(source).ActiveGrants.Where(g => g.SkillId == "dragonboat:xingzhao"))
            foreach (var recipient in _players.Where(p => DragonBoatAllies(source.Seat, p.Seat)))
            {
                var skills = new List<string>();
                if (source.Seat != recipient.Seat) skills.Add("dragonboat:xingzhao-benefit");
                if (source.TeamId is { } team && DragonBoatTeamScore(team) >= 2) skills.Add("dragonboat:xunxun");
                foreach (var skill in skills)
                {
                    var id = $"dragonboat:xingzhao:{source.Seat}:{instance.SkillInstanceId}:{recipient.Seat}:{skill}";
                    var existing = recipient.SkillGrants.Grants.FirstOrDefault(g => g.GrantId == id);
                    var shared = GetSkillBindingShard(recipient).ActiveGrants.FirstOrDefault(g => g.SkillId == skill)?.SkillInstanceId ??
                        desired.Where(p => p.Key.Seat == recipient.Seat && p.Value.Grant.SkillId == skill)
                            .Select(p => p.Value.Grant.SkillInstanceId).FirstOrDefault() ?? id;
                    desired[(recipient.Seat, id)] = (source.Seat, existing ?? new(id, skill, shared, "dragonboat:xingzhao-source:" + source.Seat));
                }
            }
            foreach (var recipient in _players)
            {
                foreach (var stale in recipient.SkillGrants.Grants.Where(g => g.SourceId.StartsWith("dragonboat:xingzhao-source:", StringComparison.Ordinal) &&
                    !desired.ContainsKey((recipient.Seat, g.GrantId))).ToArray())
                {
                    recipient.SkillGrants.RemoveGrant(stale.GrantId);
                    AdvanceEventRulesAndQueueFact(new DragonBoatXingzhaoGrantEvent(-1, recipient.Seat, stale.GrantId, stale.SkillId, stale.SkillInstanceId, false));
                }
                foreach (var pair in desired.Where(p => p.Key.Seat == recipient.Seat && !recipient.SkillGrants.Grants.Any(g => g.GrantId == p.Key.Grant)))
                {
                    recipient.SkillGrants.Grant(pair.Value.Grant);
                    AdvanceEventRulesAndQueueFact(new DragonBoatXingzhaoGrantEvent(pair.Value.Source, recipient.Seat, pair.Value.Grant.GrantId,
                        pair.Value.Grant.SkillId, pair.Value.Grant.SkillInstanceId, true));
                }
            }
        }
        finally { _synchronizingDragonBoatGrants = false; }
    }
}
