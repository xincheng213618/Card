using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string YangBiaoGiftReason = "skill-program.anniversary:zhaohan.give", YangBiaoDiscardReason = "skill-program.anniversary:yang-biao.discard", YangBiaoDrawReason = "skill-program.anniversary:jinjie.draw";
    private CardConversionSource YangBiaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryYangBiaoHost(GameEngine e) : IAnniversaryYangBiaoHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginYangBiao(f, op); }
    private long YangBiaoDrawPhaseId(ProgramSkillWindowContext? context = null)
    {
        var window = context is null ? null : _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId);
        var owned = window?.ResumeDrawPhaseObligationFrameId is { } phaseId ? _resolutionStack.OfType<DrawPhaseObligationFrame>().SingleOrDefault(p => p.Id == phaseId) :
            _resolutionStack.OfType<DrawPhaseObligationFrame>().LastOrDefault(p => p.OwnerSeat == (context?.OwnerSeat ?? _currentSeat) && p.ActualTurnNumber == _turnNumber);
        return owned is { IsExtra: true } ? owned.Id : _programPhaseSchedule is { Phase: TurnPhase.Draw } inserted ? inserted.Frame.Id : 0;
    }
    private AnniversaryYangBiaoDrawArmedEvent? YangBiaoArm(int owner, string instance, ProgramSkillWindowContext? context = null) => ProgramEventHistory<AnniversaryYangBiaoDrawArmedEvent>().LastOrDefault(e =>
        e.Source.OwnerSeat == owner && e.Source.SkillInstanceId == instance && e.Turn == _turnNumber && e.DrawPhaseId == YangBiaoDrawPhaseId(context) &&
        e.Source.SkillId == AnniversaryYangBiaoComposition.Zhaohan && e.Source.BindingId == "extra-normal-draw" && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash &&
        !ProgramEventHistory<AnniversaryYangBiaoStartedEvent>().Any(start => start.ArmId == e.FrameId));
    private int YangBiaoDiscardCount() => ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(m => m.To == CardLocation.DiscardPile &&
        ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(i => i.InvocationId == m.InvocationId && i.TurnNumber == _turnNumber && m.CompletionSequence > i.SequenceBefore && m.CompletionSequence <= i.SequenceAfter &&
            _cardMovements.Any(move => move.Sequence == m.CompletionSequence && move.CardId == m.CardId && move.To == m.To &&
                (move.From == m.From || GetProgramDiscardSource(move) == m.From) &&
                (move.Reason == i.Reason || move.Reason.Value == i.Reason.Value + "-finished" || move.Reason.Value == i.Reason.Value + "-discard")))).Select(m => m.CompletionSequence).Distinct().Count();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43700, SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee)]
    private bool CanOfferYangBiao(ProgramTriggerLegalityContext g) => g.SkillId switch
    {
        AnniversaryYangBiaoComposition.Zhaohan => YangBiaoArm(g.OwnerSeat, g.Candidate.SkillInstanceId, g.WindowContext) is not null,
        AnniversaryYangBiaoComposition.Jinjie => g.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } c && ActiveDying is { } dying && dying.VictimSeat == victim && _players[victim].IsAlive && _players[victim].Hp <= 0 &&
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == c.ParentFrameId && w.ResumeDyingFrameId == dying.Id),
        AnniversaryYangBiaoComposition.Juee => YangBiaoDiscardCount() > 0 && g.WindowContext is { SourceSeat: { } actor } && _players[actor].IsAlive,
        _ => true
    };
    private SkillProgramStepOutcome BeginYangBiao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!YangBiaoOriginal(f) || GetPausedPrivateOfferEffect(f)?.Op != op) throw new InvalidOperationException("Yang Biao requires his original native trigger candidate.");
        if (op == SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan)
        {
            if (f.SkillId != AnniversaryYangBiaoComposition.Zhaohan || f.TriggerId != "extra-normal-draw" || f.WindowContext?.Window != SkillProgramTriggerWindow.DrawPhaseStarting || f.InstructionIndex != 2 ||
                ProgramEventHistory<AnniversaryYangBiaoDrawArmedEvent>().Any(e => e.Source == YangBiaoSource(f) && e.DrawWindowId == f.WindowContext.ParentFrameId)) throw new InvalidOperationException("Zhaohan must arm once after the same native draw-window adjustment.");
            AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoDrawArmedEvent(f.Id, YangBiaoSource(f), f.GameplayHash, _turnNumber, f.WindowContext.ParentFrameId, YangBiaoDrawPhaseId(f.WindowContext))); return SkillProgramStepOutcome.Continue;
        }
        if (f.AnniversaryYangBiao is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Yang Biao cannot restart a paid local instruction.");
        var c = f.WindowContext!; var rescue = op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie; var ending = op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee;
        var arm = !rescue && !ending ? YangBiaoArm(f.OwnerSeat, f.SkillInstanceId, c) ?? throw new InvalidOperationException("Zhaohan lost its original +2 draw obligation.") : null;
        var r = new AnniversaryYangBiaoReceipt(YangBiaoSource(f), f.GameplayHash, op, c.ParentFrameId, _turnNumber, _roundNumber,
            rescue ? AnniversaryYangBiaoStage.Amount : ending ? c.SourceSeat == f.OwnerSeat ? AnniversaryYangBiaoStage.Recipient : AnniversaryYangBiaoStage.FreezeBudget : AnniversaryYangBiaoStage.ZhaohanBranch)
        { Target = rescue ? c.TargetSeat!.Value : ending && c.SourceSeat != f.OwnerSeat ? c.SourceSeat!.Value : -1, DyingId = rescue ? ActiveDying!.FrameId : 0, ArmId = arm?.FrameId ?? 0 };
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.Turn, r.Round, r.DyingId, r.ArmId));
        if (r.Stage == AnniversaryYangBiaoStage.FreezeBudget) ContinueYangBiao(f.Id); else PublishYangBiao(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> YangBiaoPaymentChoices(ProgramSkillFrame f, bool gift) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
        gift || f.AnniversaryYangBiao!.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan ? [CardZoneKind.Hand] : [CardZoneKind.Hand, CardZoneKind.Equipment], gift ? OwnedCardMoveIntent.Transfer : OwnedCardMoveIntent.Discard);
    private IReadOnlyList<PromptChoice> YangBiaoChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!; var list = new List<PromptChoice>();
        PromptChoice Choice(string option, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"yang-biao.{f.Id}.{r.Stage}.{option}"), label, cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "anniversary-yang-biao", ["option"] = option });
        if (r.Stage == AnniversaryYangBiaoStage.ZhaohanBranch)
        {
            if (GetHand(_players[f.OwnerSeat]).Count >= 2 && _players.Any(p => p.IsAlive && GetHand(p).Count == 0)) list.Add(Choice("gift", "交给一名空手角色两张手牌"));
            list.Add(Choice("discard", "弃置两张手牌"));
        }
        if (r.Stage == AnniversaryYangBiaoStage.Recipient)
            foreach (var p in _players.Where(p => p.IsAlive && (r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee ? p.Seat != f.OwnerSeat : GetHand(p).Count == 0))) list.Add(Choice("recipient-" + p.Seat, p.Name, targets: [p.Seat]));
        if (r.Stage == AnniversaryYangBiaoStage.Amount)
            for (var n = 0; n <= 3; n++) list.Add(Choice("amount-" + n, $"令{_players[r.Target].Name}摸{n}张牌"));
        if (r.Stage == AnniversaryYangBiaoStage.RescuePay)
        {
            list.Add(Choice("decline", "不弃牌回复"));
            if (YangBiaoPaymentChoices(f, false).SelectMany(c => c.Cards).Distinct().Count() >= r.Amount) list.Add(Choice("pay", $"弃置{r.Amount}张牌，令{_players[r.Target].Name}回复1点体力"));
        }
        if (r.Stage == AnniversaryYangBiaoStage.Materials)
            foreach (var c in YangBiaoPaymentChoices(f, r.Branch == "gift").Where(c => c.Cards is [var id] && !r.Cards.Contains(id))) list.Add(Choice("card-" + c.Cards[0], c.Label, c.Cards));
        if (r.Stage == AnniversaryYangBiaoStage.UsePlan)
            foreach (var (p, i) in YangBiaoPlans(f).Where(p => p.Kind == r.RandomKind).Select((p, i) => (p, i))) list.Add(Choice("use-" + i, "视为使用【" + CardCatalog.Get(p.Kind).DisplayName + "】" + (p.TargetCardId is { } id ? "：" + GetAdvancedCard(id).DisplayName : ""), targets: p.Targets));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishYangBiao(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { CompleteYangBiao(f); return; }
        var choices = YangBiaoChoices(f); if (choices.Count == 0) { CompleteYangBiao(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "杨彪：选择当前原生结算中的选项。", f.OwnerSeat);
    }
    [ProgramAnswer("anniversary-yang-biao")] private void AnswerYangBiao(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryYangBiao: { } r } f || !ValidYangBiao(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !YangBiaoChoices(f).Any(choice => AssistedChoicesEqual([choice], [c]))) throw new InvalidOperationException("Yang Biao rejects stale original-entity payment or target choices.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var option = c.Parameters["option"];
        if (r.Stage == AnniversaryYangBiaoStage.ZhaohanBranch)
        {
            var amount = option == "gift" ? 2 : Math.Min(2, YangBiaoPaymentChoices(f, false).SelectMany(choice => choice.Cards).Distinct().Count());
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Branch = option, Amount = amount, Stage = option == "gift" ? AnniversaryYangBiaoStage.Recipient : AnniversaryYangBiaoStage.Materials } });
            if (amount == 0) PayYangBiao(f); else PublishYangBiao(f); return;
        }
        if (r.Stage == AnniversaryYangBiaoStage.Recipient)
        {
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Target = c.Targets.Single(), Stage = r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee ? AnniversaryYangBiaoStage.FreezeBudget : AnniversaryYangBiaoStage.Materials } });
            if (r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee) ContinueYangBiao(f.Id); else PublishYangBiao(f); return;
        }
        if (r.Stage == AnniversaryYangBiaoStage.Amount)
        {
            var amount = int.Parse(option[7..], CultureInfo.InvariantCulture);
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Amount = amount, Stage = amount == 0 ? AnniversaryYangBiaoStage.RescuePay : AnniversaryYangBiaoStage.Drawing, Before = _movementSequence, After = _movementSequence } });
            if (amount == 0) PublishYangBiao(f); else BeginOperationProgramDraw(f, r.Target, amount, new(YangBiaoDrawReason)); return;
        }
        if (r.Stage == AnniversaryYangBiaoStage.RescuePay)
        {
            if (option == "decline") { CompleteYangBiao(f, false); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Branch = "rescue", Stage = AnniversaryYangBiaoStage.Materials } });
            if (r.Amount == 0) PayYangBiao(f); else PublishYangBiao(f); return;
        }
        if (r.Stage == AnniversaryYangBiaoStage.Materials)
        {
            var id = c.Cards.Single(); var ids = r.Cards.Append(id).ToArray();
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Cards = ids } });
            if (ids.Length < r.Amount) PublishYangBiao(f); else PayYangBiao(f); return;
        }
        var plan = YangBiaoPlans(f).Where(p => p.Kind == r.RandomKind).ElementAt(int.Parse(option[4..], CultureInfo.InvariantCulture));
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Plan = plan, Stage = AnniversaryYangBiaoStage.IssuingUse } }); IssueYangBiaoUse(f);
    }
    private void PayYangBiao(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!; var gift = r.Branch == "gift";
        if (r.Cards.Count != r.Amount || r.Cards.Any(id => !YangBiaoPaymentChoices(f, gift).Any(c => c.Cards.SequenceEqual([id]))) || gift && (!_players[r.Target].IsAlive || GetHand(_players[r.Target]).Count != 0)) throw new InvalidOperationException("Yang Biao lost his complete original Hand/HE payment or empty-handed receiver.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.CostChildren, Paid = true, From = r.Cards.Select(_cardZones.GetLocation).ToArray(), Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoPaymentIssuedEvent(f.Id, r.Branch, r.Target, r.Amount, before));
        void Paid(long _, IReadOnlyList<CardMovementRecord> __) { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { AnniversaryYangBiao = current.AnniversaryYangBiao! with { After = _movementSequence } }); }
        if (r.Amount > 0)
        { if (gift) MoveProgramCardsFromMultipleSources(r.Cards, CardLocation.Hand(r.Target), new(YangBiaoGiftReason), Paid); else MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Cards, new(YangBiaoDiscardReason), Paid); }
        ContinueYangBiao(f.Id);
    }
    private bool DrainYangBiao(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie)] private void ReturnYangBiaoDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryYangBiao!;
        if (!ValidYangBiao(f) || r.Stage != AnniversaryYangBiaoStage.Drawing || f.NativeDraw is not null || draw.RecipientSeat != r.Target || draw.RequestedCount != r.Amount) throw new InvalidOperationException("Jinjie lost its exact native dying-recipient draw.");
        ReplaceRuntimeTop(f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.DrawChildren, Draw = draw, Before = draw.SequenceBefore, After = draw.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoDrawReturnedEvent(f.Id, r.Target, r.Amount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)); ContinueYangBiao(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 43700)] private bool ContinueYangBiao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryYangBiao: { } r } f || f.Id != id || _pendingDecision is not null || r.Stage == AnniversaryYangBiaoStage.Completed) return false;
        if (!ValidYangBiao(f)) throw new InvalidOperationException("Yang Biao lost his original pending native cursor.");
        if (r.Stage is AnniversaryYangBiaoStage.CostChildren or AnniversaryYangBiaoStage.DrawChildren && DrainYangBiao(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryYangBiao!;
        if (r.Stage == AnniversaryYangBiaoStage.DrawChildren) { ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.RescuePay }, PendingMovementContinuation = null }); PublishYangBiao(f); return true; }
        if (r.Stage == AnniversaryYangBiaoStage.CostChildren) { CompleteYangBiao(f, r.Branch == "rescue"); return true; }
        if (r.Stage == AnniversaryYangBiaoStage.FreezeBudget)
        {
            if (TryBeginHappyQiaoQuery(f, r.Target, "anniversary-yang-biao-juee-maximum")) return true;
            var count = YangBiaoDiscardCount(); var maximum = _players[r.Target].MaxHp; var budget = Math.Min(count, maximum);
            ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r = r with { Stage = AnniversaryYangBiaoStage.NextUse, DiscardCount = count, Maximum = maximum, Budget = budget } });
            AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoBudgetEvent(f.Id, r.Target, count, maximum, budget));
        }
        if (r.Stage == AnniversaryYangBiaoStage.NativeUse && f.VirtualBasicDraft is null && r.Plan?.Kind == CardKind.Slash) { CompleteYangBiaoNative(f); return true; }
        if (r.Stage == AnniversaryYangBiaoStage.NextUse) { NextYangBiaoUse(f); return true; }
        if (r.Stage is AnniversaryYangBiaoStage.ZhaohanBranch or AnniversaryYangBiaoStage.Recipient or AnniversaryYangBiaoStage.Materials or AnniversaryYangBiaoStage.Amount or AnniversaryYangBiaoStage.RescuePay or AnniversaryYangBiaoStage.UsePlan) PublishYangBiao(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43700)] private bool ReturnYangBiaoMovement(ProgramSkillFrame f) => f.AnniversaryYangBiao is not null && ContinueYangBiao(f.Id);
    private void CompleteYangBiao(ProgramSkillFrame f, bool rescuePaid = false)
    {
        var r = f.AnniversaryYangBiao!; var jinjie = r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie;
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.Completed }, PendingMovementContinuation = null,
            SelectedTargetSeats = jinjie ? [r.Target] : f.SelectedTargetSeats,
            ChoiceBindings = jinjie ? Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("jinjie-pay", rescuePaid ? "paid" : "decline", f.OwnerSeat)).ToArray()) : f.ChoiceBindings });
        if (jinjie) AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoRescueSelectedEvent(f.Id, r.DyingId, r.Target, r.Amount, rescuePaid));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee)] private PromptChoice YangBiaoAi(PendingDecision p) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "pay") ?? p.Choices[0];
}
