namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int AnniversaryLiuYongLimit(int owner) => ProgramEventHistory<AnniversaryLiuYongLimitExpandedEvent>().Any(e => e.OwnerSeat == owner && e.Turn == _turnNumber && e.PhaseInstance == _cardUseDebitPhaseInstanceId) ? 2 : 1;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 43400, SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning)]
    private bool CanActivateAnniversaryLiuYong(CharacterState owner) => owner.IsAlive && ProgramEventHistory<AnniversaryLiuYongStartedEvent>().Count(e => e.Source.OwnerSeat == owner.Seat && e.Mode == "gift" && e.Turn == _turnNumber && e.PhaseInstance == _cardUseDebitPhaseInstanceId) < AnniversaryLiuYongLimit(owner.Seat);
    [ProgramCapability] private sealed class AnniversaryLiuYongHost(GameEngine engine) : IAnniversaryLiuYongHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginAnniversaryLiuYong(f, op); }
    private SkillProgramStepOutcome BeginAnniversaryLiuYong(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryLiuYong is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Liu Yong requires his original unpaid instruction.");
        var gift = operation == SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning;
        if (gift && (f.SkillId != AnniversaryLiuYongComposition.Zhuning || f.ActivationId != "gift-and-use" || f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !CanActivateAnniversaryLiuYong(_players[f.OwnerSeat]))) throw new InvalidOperationException("Zhuning lost its actual Play-phase quota or other receiver.");
        var mode = gift ? "gift" : f.WindowContext?.Window == SkillProgramTriggerWindow.CardsMoved ? "transition" : "damage";
        var leader = mode == "damage" ? AnniversaryLiuYongLeader(f.OwnerSeat) : null;
        var r = new AnniversaryLiuYongReceipt(AnniversaryLiuYongSource(f), f.GameplayHash, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, _cardUseDebitPhaseInstanceId, mode,
            gift ? AnniversaryLiuYongStage.Gift : mode == "damage" && leader is not null ? AnniversaryLiuYongStage.HealthChildren : AnniversaryLiuYongStage.Drawing)
        { Recipient = gift ? f.SelectedTargetSeats.Single() : leader ?? f.OwnerSeat, Leader = leader };
        ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.PhaseInstance, mode));
        if (gift) PublishAnniversaryLiuYong(f);
        else if (r.Stage == AnniversaryLiuYongStage.HealthChildren)
        { new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.Recipient, 1, null, null); ContinueAnniversaryLiuYong(f.Id); }
        else BeginOperationProgramDraw(f, f.OwnerSeat, 1, AnniversaryLiuYongDrawReason);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private static CardMoveReason AnniversaryLiuYongGiftReason => new("skill.anniversary.zhuning.gift");
    private static CardMoveReason AnniversaryLiuYongDrawReason => new("skill.anniversary.fengxiang.draw");
    private IReadOnlyList<PromptChoice> AnniversaryLiuYongChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuYong!;
        PromptChoice Choice(string id, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => new(new($"anniversary-liuyong.{f.Id}.{id}"), label, cards, targets,
            new Dictionary<string, string> { ["program-action"] = "anniversary-liuyong", ["option"] = id });
        if (r.Stage == AnniversaryLiuYongStage.Gift)
        {
            var cards = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer)
                .Where(c => c.Cards.Count == 1 && !r.Cards.Contains(c.Cards[0]) && CanDeliverLianZhuGift(f.OwnerSeat, r.Recipient, c.Cards[0]));
            return cards.Select(c => Choice("card-" + c.Cards[0], c.Label, c.Cards, [])).Append(Choice("give", $"交给{_players[r.Recipient].Name}{r.Cards.Count}张牌并标记隙", [], [r.Recipient])).ToArray();
        }
        if (r.Stage != AnniversaryLiuYongStage.UseChoice) return [];
        return AnniversaryLiuYongPlans(f.OwnerSeat).Select((p, index) => Choice("use-" + index, $"视为使用【{CardCatalog.Get(p.Kind).DisplayName}】" + (p.Targets.Count == 0 ? "" : "：" + string.Join("、", p.Targets.Select(t => _players[t].Name))), [], p.Targets))
            .Append(Choice("finish", "不使用牌", [], [])).ToArray();
    }
    private void PublishAnniversaryLiuYong(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishProgramSkill(f, true); return; }
        var choices = AnniversaryLiuYongChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.AnniversaryLiuYong!.Stage == AnniversaryLiuYongStage.Gift ? "诛佞：选择交给目标的牌，完成时一次交付。" : "诛佞：选择原生虚拟牌用法，或结束。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = f.AnniversaryLiuYong.Stage == AnniversaryLiuYongStage.Gift, TargetSeat = f.AnniversaryLiuYong.Recipient, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-liuyong")] private void AnswerAnniversaryLiuYong(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuYong: { } r } f || !AnniversaryLiuYongChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Liu Yong rejects a stale private material or native target plan.");
        RequireAnniversaryLiuYong(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = choice.Parameters["option"];
        if (r.Stage == AnniversaryLiuYongStage.Gift)
        {
            if (option != "give") { ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r with { Cards = r.Cards.Append(choice.Cards.Single()).ToArray() } }); PublishAnniversaryLiuYong(f); return; }
            if (!_players[r.Recipient].IsAlive || r.Cards.Any(id => !CanDeliverLianZhuGift(f.OwnerSeat, r.Recipient, id))) throw new InvalidOperationException("Zhuning requires all original HE entities still transferable to its original receiver.");
            var before = _movementSequence;
            r = r with { GiftPaid = true, Stage = AnniversaryLiuYongStage.GiftChildren, From = r.Cards.Select(_cardZones.GetLocation).ToArray(), Before = before, After = before };
            ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r });
            _players[r.Recipient].AnniversaryLiuYongXiCards = _players[r.Recipient].AnniversaryLiuYongXiCards.Concat(r.Cards.Select(id => new AnniversaryLiuYongXi(r.Source, f.Id, id, r.Recipient))).ToArray();
            if (r.Cards.Count > 0) MoveProgramCardsFromMultipleSources(r.Cards, CardLocation.Hand(r.Recipient), AnniversaryLiuYongGiftReason, (_, _) =>
            { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { AnniversaryLiuYong = current.AnniversaryLiuYong! with { After = _movementSequence } }); });
            f = GetActiveProgramFrame(f.Id); r = f.AnniversaryLiuYong!;
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongGiftPaidEvent(f.Id, r.Source, r.Recipient, r.Cards.Count, before, r.After));
            _ = AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat); ContinueAnniversaryLiuYong(f.Id); return;
        }
        if (option == "finish") { FinishProgramSkill(f, true); return; }
        var plan = AnniversaryLiuYongPlans(f.OwnerSeat)[int.Parse(option[4..])];
        ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r with { Plan = plan, Stage = AnniversaryLiuYongStage.IssuingUse } });
        IssueAnniversaryLiuYongUse(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 43400)] private bool ContinueAnniversaryLiuYong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuYong: { } r } f || f.Id != id || _pendingDecision is not null) return false;
        RequireAnniversaryLiuYong(f);
        if (r.Stage is AnniversaryLiuYongStage.Gift or AnniversaryLiuYongStage.UseChoice) { PublishAnniversaryLiuYong(f); return true; }
        if (r.Stage is AnniversaryLiuYongStage.Drawing or AnniversaryLiuYongStage.NativeUse or AnniversaryLiuYongStage.IssuingUse) return true;
        if (DrainAnniversaryLiuYong(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryLiuYong!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == AnniversaryLiuYongStage.GiftChildren) { ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r with { Stage = AnniversaryLiuYongStage.UseChoice } }); PublishAnniversaryLiuYong(f); return true; }
        FinishProgramSkill(f, true); return true;
    }
    private bool DrainAnniversaryLiuYong(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.AnniversaryLiuYong!.Stage == AnniversaryLiuYongStage.GiftChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, f.AnniversaryLiuYong!.Stage == AnniversaryLiuYongStage.GiftChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Movement, 43400)] private bool ReturnAnniversaryLiuYongMovement(ProgramSkillFrame f) => f.AnniversaryLiuYong is not null && ContinueAnniversaryLiuYong(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang)]
    private void ReturnAnniversaryLiuYongDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.AnniversaryLiuYong is not { Stage: AnniversaryLiuYongStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 || outcome.SequenceAfter != _movementSequence) throw new InvalidOperationException("Fengxiang lost its exact one-card native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryLiuYong = r with { Stage = AnniversaryLiuYongStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongDrawReturnedEvent(f.Id, outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter, outcome.ActualDrawIds.Count)); ContinueAnniversaryLiuYong(f.Id);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 43400)] private void CaptureAnniversaryLiuYongDamage(DamageAppliedEvent fact)
    {
        if (fact.SourceCardUseResolutionId is not { } useId || fact.Amount <= 0) return;
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.AnniversaryLiuYong is { UseId: > 0, CausedDamage: false } r && r.UseId == useId).ToArray())
            ReplaceRuntimeFrame(f.Id, f with { AnniversaryLiuYong = f.AnniversaryLiuYong! with { CausedDamage = true } });
    }
    private void CompleteAnniversaryLiuYongUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuYong!;
        if (LifecycleCardUse(r.UseId) is not null || r.Stage != AnniversaryLiuYongStage.NativeReturned) throw new InvalidOperationException("Zhuning cannot resume before its whole native use returns.");
        if (!r.CausedDamage && !ProgramEventHistory<AnniversaryLiuYongLimitExpandedEvent>().Any(e => e.FrameId == f.Id)) AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongLimitExpandedEvent(f.Id, f.OwnerSeat, r.Turn, r.PhaseInstance));
        ContinueAnniversaryLiuYong(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning)] private PromptChoice ChooseAnniversaryLiuYong(PendingDecision p, ProgramSkillFrame f) => p.Choices.First(c => c.Parameters["option"] == (f.AnniversaryLiuYong!.Stage == AnniversaryLiuYongStage.Gift ? "give" : "finish"));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43400)] private bool RetainAnniversaryLiuYong(ProgramSkillFrame f) => f.AnniversaryLiuYong is not null;
}
