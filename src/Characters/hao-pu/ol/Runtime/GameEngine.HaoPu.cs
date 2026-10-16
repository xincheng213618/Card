using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhenyingDiscardReason = "skill-program.hao-pu.zhenying.discard", ZhenyingDrawReason = "skill-program.hao-pu.zhenying.draw";
    private static CardConversionSource ZhenyingSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ZhenyingHandPlan FreezeZhenyingHand(int seat)
    {
        var hand = GetHand(_players[seat]);
        return new(seat, Array.AsReadOnly(hand.Select(c => c.Id).ToArray()), ProtectedIds: Array.AsReadOnly(hand
            .Where(c => IsSelfHandCategoryDiscardForbidden(seat, c, CardLocation.Hand(seat), OwnedCardMoveIntent.Discard)).Select(c => c.Id).ToArray()));
    }
    private SkillProgramStepOutcome BeginZhenying(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var activation = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Activation;
        if (activation is null || f.Zhenying is not null || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var other] || other == f.OwnerSeat || !IsValidPlayerSeat(other) ||
            !_players[f.OwnerSeat].IsAlive || !_players[other].IsAlive || GetHand(_players[other]).Count > GetHand(_players[f.OwnerSeat]).Count ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, activation.UsageGroup)) is < 1 or > 2)
            throw new InvalidOperationException("Zhenying requires its exact zero-card twice-per-Play activation and actual hand-count target.");
        HaoPuContract.Activation(f.SkillId, activation);
        var r = new ZhenyingReceipt(ZhenyingSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId,
            other, _movementSequence, FreezeZhenyingHand(f.OwnerSeat), FreezeZhenyingHand(other));
        ReplaceRuntimeTop(f = f with { Zhenying = r });
        AdvanceEventRulesAndQueueFact(new ZhenyingStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId,
            other, r.StartMovementSequence, r.OwnerPlan.OriginalIds.Count, r.OtherPlan.OriginalIds.Count));
        PublishZhenyingChoice(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private static ZhenyingHandPlan ZhenyingPlan(ZhenyingReceipt r) => r.SelectionIndex == 0 ? r.OwnerPlan : r.OtherPlan;
    private static ZhenyingReceipt WithZhenyingPlan(ZhenyingReceipt r, ZhenyingHandPlan plan) =>
        r.SelectionIndex == 0 ? r with { OwnerPlan = plan } : r with { OtherPlan = plan };
    private static int ZhenyingKeptCount(ZhenyingHandPlan p) => Math.Min(p.OriginalIds.Count, Math.Max(p.DesiredCount ?? 0, p.ProtectedIds?.Count ?? 0));
    private IReadOnlyList<PromptChoice> ZhenyingChoices(ProgramSkillFrame f)
    {
        var r = f.Zhenying!; var p = ZhenyingPlan(r);
        Dictionary<string, string> Params(string branch) => new() { ["program-action"] = "zhenying", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
            ["chooser-seat"] = p.Seat.ToString(CultureInfo.InvariantCulture), ["slot"] = r.SlotIndex.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == ZhenyingStage.ChoosingCount)
            return Array.AsReadOnly(Enumerable.Range(0, 3).Select(count =>
            {
                var parameters = Params("count"); parameters["count"] = count.ToString(CultureInfo.InvariantCulture);
                return new PromptChoice(new($"zhenying.{f.Id}.{p.Seat}.count.{count}"), $"将手牌调整至{count}张", [], [], parameters);
            }).ToArray());
        if (r.Stage != ZhenyingStage.ChoosingKeptCards || p.DesiredCount is null || p.KeptIds is null)
            throw new InvalidOperationException("Zhenying has no uncommitted private hand choice.");
        if (p.KeptIds.Count == ZhenyingKeptCount(p))
            return Array.AsReadOnly(new[] { new PromptChoice(new($"zhenying.{f.Id}.{p.Seat}.empty.{r.SlotIndex}"),
                "确认此保留位留空", [], [], Params("empty")) });
        return Array.AsReadOnly(p.OriginalIds.Except(p.KeptIds).Order().Select(id => new PromptChoice(
            new($"zhenying.{f.Id}.{p.Seat}.keep.{r.SlotIndex}.{id}"), $"保留【{GetAttackCard(id).DisplayName}】", [id], [], Params("keep"))).ToArray());
    }
    private void PublishZhenyingChoice(ProgramSkillFrame f)
    {
        var p = ZhenyingPlan(f.Zhenying!); var choices = ZhenyingChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) throw new InvalidOperationException("A sealed Zhenying choice has no legal continuation.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, p.Seat,
            f.Zhenying!.Stage == ZhenyingStage.ChoosingCount ? "私下选择手牌调整至0、1或2张；双方选择完成后同时执行。" : "私下选择要保留的原手牌；双方选定前不移动或公开选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = p.Seat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[p.Seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("zhenying")]
    private void AnswerZhenying(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Zhenying lost its choice owner.");
        RequireZhenying(f); var r = f.Zhenying!; var p = ZhenyingPlan(r);
        if (_pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != p.Seat ||
            !ZhenyingChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Zhenying requires the exact published chooser-owned private commitment.");
        ClearPendingDecision();
        if (r.Stage == ZhenyingStage.ChoosingCount)
        {
            var count = int.Parse(choice.Parameters["count"], CultureInfo.InvariantCulture);
            p = p with { DesiredCount = count, KeptIds = p.OriginalIds.Count <= count ? p.OriginalIds : p.ProtectedIds ?? [] };
            ReplaceRuntimeTop(f = f with { Zhenying = WithZhenyingPlan(r, p) with { Stage = ZhenyingStage.ChoosingKeptCards, SlotIndex = 0 } });
            PublishZhenyingChoice(f); return;
        }
        else p = p with { KeptIds = Array.AsReadOnly(p.KeptIds!.Concat(choice.Cards).ToArray()) };
        if (r.SlotIndex == 0)
        {
            ReplaceRuntimeTop(f = f with { Zhenying = WithZhenyingPlan(r, p) with { SlotIndex = 1 } });
            PublishZhenyingChoice(f); return;
        }
        if (p.KeptIds!.Count != ZhenyingKeptCount(p)) throw new InvalidOperationException("Two sealed Zhenying slots must finish the selected retained set.");
        p = p with { Committed = true };
        r = WithZhenyingPlan(r, p);
        ReplaceRuntimeTop(f = f with { Zhenying = r });
        AdvanceEventRulesAndQueueFact(new ZhenyingChoiceCommittedEvent(f.Id, p.Seat));
        if (r.SelectionIndex == 0)
        {
            ReplaceRuntimeTop(f = f with { Zhenying = r with { SelectionIndex = 1, SlotIndex = 0, Stage = ZhenyingStage.ChoosingCount } });
            PublishZhenyingChoice(f); return;
        }
        PayZhenyingAdjustment(f);
    }
    private void PayZhenyingAdjustment(ProgramSkillFrame f)
    {
        var r = f.Zhenying!; var before = _movementSequence;
        if (!r.OwnerPlan.Committed || !r.OtherPlan.Committed) throw new InvalidOperationException("Both Zhenying hands must be sealed before either moves.");
        ReplaceRuntimeTop(f = f with { Zhenying = r with { Stage = ZhenyingStage.AdjustmentChildren, PaymentBefore = before, PaymentAfter = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        foreach (var p in new[] { r.OwnerPlan, r.OtherPlan })
        {
            var discarded = p.OriginalIds.Except(p.KeptIds!).Select(GetAttackCard).ToArray();
            if (discarded.Length > 0) MoveCards(discarded, CardLocation.Hand(p.Seat), CardLocation.DiscardPile, new(ZhenyingDiscardReason));
        }
        var ownerDraw = Math.Max(0, r.OwnerPlan.DesiredCount!.Value - r.OwnerPlan.OriginalIds.Count);
        var otherDraw = Math.Max(0, r.OtherPlan.DesiredCount!.Value - r.OtherPlan.OriginalIds.Count);
        var drawnOwner = ownerDraw == 0 ? 0 : DrawCards(_players[f.OwnerSeat], ownerDraw, true, new(ZhenyingDrawReason)).Count;
        var drawnOther = otherDraw == 0 ? 0 : DrawCards(_players[r.OtherSeat], otherDraw, true, new(ZhenyingDrawReason)).Count;
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { Zhenying = f.Zhenying! with { PaymentAfter = _movementSequence, OwnerDrawCount = drawnOwner, OtherDrawCount = drawnOther } });
        AdvanceEventRulesAndQueueFact(new ZhenyingAdjustmentPaidEvent(f.Id, before, _movementSequence,
            r.OwnerPlan.DesiredCount.Value, r.OtherPlan.DesiredCount.Value, drawnOwner, drawnOther,
            GetHand(_players[f.OwnerSeat]).Count, GetHand(_players[r.OtherSeat]).Count));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainZhenyingChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26500)]
    private bool ResumeZhenying(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zhenying: { } r } f || f.Id != id) return false;
        RequireZhenying(f);
        if (r.Stage is ZhenyingStage.ChoosingCount or ZhenyingStage.ChoosingKeptCards)
        { if (_pendingDecision is null) PublishZhenyingChoice(f); return true; }
        if (r.Stage == ZhenyingStage.DuelIssued) throw new InvalidOperationException("An issued Zhenying Duel needs its exact native return.");
        if (DrainZhenyingChildren(id)) return true;
        f = GetActiveProgramFrame(id); r = f.Zhenying!;
        var ownerCount = GetHand(_players[f.OwnerSeat]).Count; var otherCount = GetHand(_players[r.OtherSeat]).Count;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.OtherSeat].IsAlive || ownerCount == otherCount)
        { FinishZhenying(f, false); return true; }
        var actor = ownerCount < otherCount ? f.OwnerSeat : r.OtherSeat; var target = actor == f.OwnerSeat ? r.OtherSeat : f.OwnerSeat;
        if (!CanIssueSelectedActorDuel(target, actor)) { FinishZhenying(f, false); return true; }
        var useId = _resolutionSequence + 1;
        var origin = new ZhenyingDuelOrigin(f.Id, r.InstructionIndex, useId, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId,
            actor, target, ownerCount, otherCount);
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, Zhenying = r with { Stage = ZhenyingStage.DuelIssued, Duel = origin } });
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        if (BeginCardUse(card, actor, [target], CardKind.Duel, physicalCardIds: [], zhenyingDuel: origin) != useId)
            throw new InvalidOperationException("Zhenying lost its reserved exact native Duel use.");
        AdvanceEventRulesAndQueueFact(new ZhenyingDuelIssuedEvent(origin));
        BeginJizhiOrNullificationWindow(useId, card, actor, [target], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return true;
    }
    private void FinishZhenying(ProgramSkillFrame f, bool duel)
    {
        AdvanceEventRulesAndQueueFact(new ZhenyingCompletedEvent(f.Id, duel));
        ReplaceRuntimeTop(f = f with { Zhenying = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26500)]
    private bool ReturnZhenyingMovement(ProgramSkillFrame f) => f.Zhenying is not null && ResumeZhenying(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZhenyingSimultaneousHands)]
    private PromptChoice ChooseZhenyingAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.Zhenying!.Stage == ZhenyingStage.ChoosingCount)
            return prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("count") == "2");
        // Only this chooser's original cards are examined; the other commitment
        // and private hand never affect the AI's sealed choice.
        return prompt.Choices.OrderByDescending(c => c.Cards is [var id] && IsSlashCard(GetAttackCard(id).Kind))
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    [ProgramCapability]
    private sealed class HaoPuCapability(GameEngine engine) : IHaoPuProgramHost
    { public SkillProgramStepOutcome ResolveZhenying(ProgramSkillFrame f) => engine.BeginZhenying(f); }
}
