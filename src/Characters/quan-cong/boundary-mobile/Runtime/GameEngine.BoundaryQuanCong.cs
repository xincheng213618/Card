using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryYaomingAction = "boundary-quan-cong-yaoming";
    private const string BoundaryYaomingDiscardReason = "skill-program.boundary:yaoming-mobile.discard";

    [ProgramCapability]
    private sealed class BoundaryQuanCongHost(GameEngine engine) : IBoundaryQuanCongProgramHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginBoundaryYaoming(frame);
    }

    private static CardConversionSource BoundaryYaomingSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    private (int Remaining, string? Pending) BoundaryYaomingState(int owner, string instance)
    {
        var last = ProgramEventHistory<BoundaryQuanCongYaomingStateEvent>()
            .LastOrDefault(e => e.OwnerSeat == owner && e.SkillInstanceId == instance);
        return last is null ? (2, null) : (last.Remaining, last.PendingOption);
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 51200,
        SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming)]
    private bool AllowsBoundaryYaoming(ProgramActivationLegalityContext gate) =>
        GetSkillBindingShard(gate.Owner).ProgramInstances.Any(i => i.SkillId == gate.SkillId &&
            BoundaryYaomingState(gate.OwnerSeat, i.SkillInstanceId).Remaining > 0);

    private bool OriginalBoundaryYaoming(ProgramSkillFrame frame)
    {
        if (frame.SkillId != BoundaryQuanCongComposition.Skill || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0) return false;
        if (frame.TriggerId is null) return frame.ActivationId == BoundaryQuanCongComposition.Activation;
        return frame.TriggerId == BoundaryQuanCongComposition.Trigger &&
            frame.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } window &&
            window.TargetSeat == frame.OwnerSeat &&
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == window.ParentFrameId) is { } damage &&
            DamageProgramMatchesWindow(frame, damage);
    }

    private SkillProgramStepOutcome BeginBoundaryYaoming(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.BoundaryQuanCongYaoming is not null || !OriginalBoundaryYaoming(frame))
            throw new InvalidOperationException("Yaoming requires its original phase or actual received-damage frame.");
        var receipt = new BoundaryQuanCongYaomingReceipt(BoundaryYaomingSource(frame), frame.GameplayHash,
            frame.WindowContext?.ParentFrameId ?? 0, _turnNumber, BoundaryQuanCongYaomingStage.Option);
        ReplaceRuntimeTop(frame = frame with { BoundaryQuanCongYaoming = receipt });
        AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingStartedEvent(frame.Id, receipt.Source,
            receipt.GameplayHash, receipt.ParentWindowId, receipt.Turn));
        if (frame.TriggerId is not null)
        {
            var state = BoundaryYaomingState(frame.OwnerSeat, frame.SkillInstanceId);
            var amount = frame.WindowContext!.Amount;
            AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingStateEvent(frame.OwnerSeat, frame.SkillInstanceId,
                frame.Id, "damage", Math.Min(4, state.Remaining + amount), state.Pending, amount));
        }
        PublishBoundaryYaoming(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private PromptChoice BoundaryYaomingChoice(ProgramSkillFrame frame, string option, int target, string label) =>
        new(new($"boundary-yaoming.{frame.Id}.{option}.{target}"), label, [], target < 0 ? [] : [target],
            new Dictionary<string, string> { ["program-action"] = BoundaryYaomingAction, ["option"] = option,
                ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) });

    private IReadOnlyList<PromptChoice> BoundaryYaomingChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.BoundaryQuanCongYaoming!;
        if (receipt.Stage == BoundaryQuanCongYaomingStage.Card)
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, receipt.TargetSeat,
                [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
                .Select(choice => choice with { Parameters = new Dictionary<string, string>(choice.Parameters)
                    { ["program-action"] = BoundaryYaomingAction, ["option"] = "card" } }).ToArray());
        if (receipt.Stage != BoundaryQuanCongYaomingStage.Option) return [];
        var result = new List<PromptChoice>();
        if (BoundaryYaomingState(frame.OwnerSeat, frame.SkillInstanceId).Remaining > 0 && _players[frame.OwnerSeat].IsAlive)
        {
            var own = GetHand(_players[frame.OwnerSeat]).Count;
            foreach (var player in _players.Where(p => p.IsAlive))
            {
                if (GetHand(player).Count <= own)
                    result.Add(BoundaryYaomingChoice(frame, "draw", player.Seat, $"令 {player.Name} 摸一张牌"));
                if (player.Seat != frame.OwnerSeat && GetHand(player).Count >= own &&
                    BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, player.Seat,
                        [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).Count > 0)
                    result.Add(BoundaryYaomingChoice(frame, "discard", player.Seat, $"弃置 {player.Name} 的一张牌"));
            }
        }
        result.Add(BoundaryYaomingChoice(frame, "skip", -1, "不发动邀名"));
        return Array.AsReadOnly(result.ToArray());
    }

    private void PublishBoundaryYaoming(ProgramSkillFrame frame)
    {
        var choices = BoundaryYaomingChoices(frame);
        if (choices.Count == 0 || choices.All(c => c.Parameters["option"] == "skip"))
        { CompleteBoundaryYaoming(frame); return; }
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.AwaitingResponse });
        var state = BoundaryYaomingState(frame.OwnerSeat, frame.SkillInstanceId);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var text = frame.BoundaryQuanCongYaoming!.Stage == BoundaryQuanCongYaomingStage.Card
            ? "邀名：弃置所选角色的一张手牌或装备牌。"
            : $"邀名：剩余 {state.Remaining}/4 次，上次记录：{(state.Pending == "draw" ? "摸牌" : state.Pending == "discard" ? "弃牌" : "无")}。选择效果和角色。";
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, text,
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()),
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(BoundaryYaomingAction)]
    private void AnswerBoundaryYaoming(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryQuanCongYaoming: { } receipt } frame ||
            !ValidBoundaryYaoming(frame) || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([answer], BoundaryYaomingChoices(frame).Where(c => c.Id == answer.Id).ToArray()))
            throw new InvalidOperationException("Yaoming requires its owning participant's current option, target or anonymous card slot.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = answer.Parameters["option"];
        if (option == "skip") { CompleteBoundaryYaoming(frame); return; }
        if (receipt.Stage == BoundaryQuanCongYaomingStage.Option)
        {
            var state = BoundaryYaomingState(frame.OwnerSeat, frame.SkillInstanceId);
            if (state.Remaining <= 0) throw new InvalidOperationException("Yaoming has no remaining charge.");
            var switched = state.Pending is not null && state.Pending != option;
            var target = answer.Targets.Single();
            ReplaceRuntimeTop(frame = frame with { BoundaryQuanCongYaoming = receipt = receipt with
                { Option = option, TargetSeat = target, Stage = option == "draw" ? BoundaryQuanCongYaomingStage.Drawing : BoundaryQuanCongYaomingStage.Card,
                    Before = _movementSequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingSelectedEvent(frame.Id, option, target));
            AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingStateEvent(frame.OwnerSeat, frame.SkillInstanceId,
                frame.Id, "use", Math.Min(4, state.Remaining - 1 + (switched ? 1 : 0)), switched ? null : option));
            if (option == "draw")
            { BeginOperationProgramDraw(frame, target, 1, new("skill-program.boundary:yaoming-mobile.draw")); return; }
            PublishBoundaryYaoming(frame); return;
        }
        var zone = Enum.Parse<CardZoneKind>(answer.Parameters["source-zone"]);
        var slot = int.Parse(answer.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var card = (zone == CardZoneKind.Hand ? GetHand(_players[receipt.TargetSeat]) : GetEquipment(_players[receipt.TargetSeat]))[slot];
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { BoundaryQuanCongYaoming = receipt with
            { Stage = BoundaryQuanCongYaomingStage.Children, DiscardedCardId = card.Id, Before = before, After = before },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(frame, frame.OwnerSeat, [card.Id], new(BoundaryYaomingDiscardReason), (_, _) =>
        {
            var active = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(active with { BoundaryQuanCongYaoming = active.BoundaryQuanCongYaoming! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingDiscardedEvent(frame.Id, receipt.TargetSeat,
                card.Id, before, _movementSequence));
        });
        ContinueBoundaryYaoming(frame.Id);
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming)]
    private void ReturnBoundaryYaomingDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!ValidBoundaryYaoming(frame) || frame.BoundaryQuanCongYaoming is not { Stage: BoundaryQuanCongYaomingStage.Drawing } receipt ||
            outcome.RecipientSeat != receipt.TargetSeat || outcome.RequestedCount != 1 || outcome.SequenceBefore < receipt.Before ||
            outcome.SequenceAfter < outcome.SequenceBefore)
            throw new InvalidOperationException("Yaoming draw must return once to the original charged action and selected recipient.");
        ReplaceRuntimeTop(frame = frame with { BoundaryQuanCongYaoming = receipt with
            { Stage = BoundaryQuanCongYaomingStage.Children, DrawAttemptId = outcome.AttemptFrameId, After = outcome.SequenceAfter },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BoundaryQuanCongYaomingDrawReturnedEvent(frame.Id, receipt.TargetSeat,
            outcome.AttemptFrameId, outcome.ActualDrawIds.Count));
        ContinueBoundaryYaoming(frame.Id);
    }

    private bool ValidBoundaryYaoming(ProgramSkillFrame frame) => frame.BoundaryQuanCongYaoming is { } receipt &&
        OriginalBoundaryYaoming(frame) && receipt.Source == BoundaryYaomingSource(frame) && receipt.GameplayHash == frame.GameplayHash &&
        receipt.ParentWindowId == (frame.WindowContext?.ParentFrameId ?? 0) && receipt.Turn == _turnNumber &&
        ProgramEventHistory<BoundaryQuanCongYaomingStartedEvent>().Count(e => e.FrameId == frame.Id &&
            e.Source == receipt.Source && e.GameplayHash == receipt.GameplayHash && e.ParentWindowId == receipt.ParentWindowId && e.Turn == receipt.Turn) == 1;

    [ProgramContinuation(ProgramContinuationKind.Resume, 51200)]
    private bool ContinueBoundaryYaoming(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryQuanCongYaoming: { } receipt } frame || frame.Id != id ||
            receipt.Stage == BoundaryQuanCongYaomingStage.Complete) return false;
        if (!ValidBoundaryYaoming(frame)) throw new InvalidOperationException("Yaoming lost its owning charged action or native return.");
        if (receipt.Stage is BoundaryQuanCongYaomingStage.Option or BoundaryQuanCongYaomingStage.Card)
        { if (_pendingDecision is null) PublishBoundaryYaoming(frame); return true; }
        if (receipt.Stage == BoundaryQuanCongYaomingStage.Drawing) return true;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        CompleteBoundaryYaoming(GetActiveProgramFrame(id)); return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 51200)]
    private bool ReturnBoundaryYaomingMovement(ProgramSkillFrame frame) =>
        frame.BoundaryQuanCongYaoming is not null && ContinueBoundaryYaoming(frame.Id);

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 51200)]
    private bool IsBoundaryYaomingPaidMovement(ProgramSkillFrame frame, SkillProgramEffect? paidEffect,
        ProgramMovementContinuation pending)
    {
        if (paidEffect?.Op != SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming ||
            frame.PendingMovementContinuation != pending || pending.SubjectSeat != frame.OwnerSeat ||
            pending.BeforeCount != 0 || pending.CoverageResultBind is not null ||
            frame.BoundaryQuanCongYaoming is not { Stage: BoundaryQuanCongYaomingStage.Children } receipt ||
            !ValidBoundaryYaoming(frame) || receipt.Before > receipt.After ||
            ProgramEventHistory<BoundaryQuanCongYaomingSelectedEvent>().Count(e => e.FrameId == frame.Id &&
                e.Option == receipt.Option && e.TargetSeat == receipt.TargetSeat) != 1 ||
            ProgramEventHistory<BoundaryQuanCongYaomingStateEvent>().Count(e => e.FrameId == frame.Id &&
                e.OwnerSeat == frame.OwnerSeat && e.SkillInstanceId == frame.SkillInstanceId && e.Cause == "use") != 1)
            return false;
        if (receipt.Option == "discard" && receipt.DiscardedCardId is { } cardId)
        {
            if (ProgramEventHistory<BoundaryQuanCongYaomingDiscardedEvent>().Count(e => e.FrameId == frame.Id &&
                    e.TargetSeat == receipt.TargetSeat && e.CardId == cardId && e.Before == receipt.Before && e.After == receipt.After) != 1)
                return false;
            var invoice = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().SingleOrDefault(e =>
                e.ParentFrameId == frame.Id && e.Reason.Value == BoundaryYaomingDiscardReason &&
                e.InitiatorSeat == frame.OwnerSeat && e.ExecutorSeat == frame.OwnerSeat && e.SubjectSeat == receipt.TargetSeat &&
                e.ActualCount == 1 && e.SequenceBefore == receipt.Before && e.SequenceAfter == receipt.After);
            return invoice is not null && ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Any(material =>
                material.InvocationId == invoice.InvocationId && material.CardId == cardId &&
                material.From.OwnerSeat == receipt.TargetSeat && material.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                _cardMovements.Any(move => move.Sequence == material.CompletionSequence && move.CardId == cardId &&
                    move.Sequence > receipt.Before && move.Sequence <= receipt.After && move.From == material.From &&
                    move.To == material.To && move.Reason.Value == BoundaryYaomingDiscardReason));
        }
        if (receipt.Option != "draw" || receipt.DiscardedCardId is not null ||
            ProgramEventHistory<BoundaryQuanCongYaomingDrawReturnedEvent>().Count(e => e.FrameId == frame.Id &&
                e.RecipientSeat == receipt.TargetSeat && e.AttemptId == receipt.DrawAttemptId) != 1) return false;
        return receipt.DrawAttemptId is not { } attempt ||
            ProgramEventHistory<NativeDrawAttemptStartedEvent>().Count(e => e.Context.AttemptFrameId == attempt &&
                e.Context.ParentFrameId == frame.Id && e.Context.RecipientSeat == receipt.TargetSeat &&
                e.Context.RequestedCount == 1 && e.Return.Kind == NativeDrawAttemptReturnKind.ProgramOperation &&
                e.Return.InstructionIndex == frame.InstructionIndex) == 1;
    }

    private void CompleteBoundaryYaoming(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame = frame with { BoundaryQuanCongYaoming = frame.BoundaryQuanCongYaoming! with
            { Stage = BoundaryQuanCongYaomingStage.Complete }, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(frame.Id);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 51200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 51200)]
    private bool KeepPaidBoundaryYaoming(ProgramSkillFrame frame) =>
        frame.BoundaryQuanCongYaoming is { Option: not null } && ValidBoundaryYaoming(frame);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 51200)]
    private void AssertBoundaryYaomingFrame(ProgramSkillFrame frame)
    {
        if (frame.BoundaryQuanCongYaoming is not null && !ValidBoundaryYaoming(frame))
            throw new InvalidOperationException("Yaoming changed its original charge, damage parent or frame identity.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming)]
    private PromptChoice BoundaryYaomingAi(PendingDecision pending, ProgramSkillFrame frame)
    {
        var state = BoundaryYaomingState(frame.OwnerSeat, frame.SkillInstanceId);
        return pending.Choices.OrderByDescending(choice =>
        {
            var option = choice.Parameters["option"];
            if (option == "skip") return -0.5;
            if (option == "card") return choice.Cards.Count == 1
                ? CardCatalog.Get(GetAdvancedCard(choice.Cards[0]).Kind).HandKeepValue : 5;
            var score = _aiBrains[frame.OwnerSeat].ScoreProgramTarget(CreateSnapshot(frame.OwnerSeat), choice.Targets.Single(),
                option == "draw" ? new(0, 0, 0, 1, 0, 0, false, false) : new(1, 0, 0, 0, 0, 0, false, false));
            return score + (state.Pending is not null && state.Pending != option ? 0.2 : 0);
        }).First();
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> BoundaryQuanCongPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != BoundaryQuanCongComposition.Skill) return [];
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(instance =>
        {
            var state = BoundaryYaomingState(owner.Seat, instance.SkillInstanceId);
            return new[] {
                new ProgramPublicRuleStateSnapshot(instance.SkillInstanceId, "yaoming-remaining", ProgramPublicRuleStateKind.GameActivationProgress, state.Remaining),
                new ProgramPublicRuleStateSnapshot(instance.SkillInstanceId, "yaoming-pending-option", ProgramPublicRuleStateKind.GameActivationProgress,
                    state.Pending == "discard" ? 1 : state.Pending == "draw" ? 2 : 0)
            };
        }).ToArray();
    }
}
