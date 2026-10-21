using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginOrdinaryBingyi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OrdinaryBingyi is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.TriggerId != "ordinary-discard-reveal-share" || f.WindowContext is not { } context || !CanOfferOrdinaryBingyi(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), GetProgramTrigger(f), context))
            throw new InvalidOperationException("Ordinary Bingyi requires its original actual-phase owned discard and nonempty current hand.");
        var cards = GetHand(_players[f.OwnerSeat]).Select(c => new OrdinaryBingyiShownCard(c.Id, c.Kind, GetProgramEffectiveSuit(_players[f.OwnerSeat], c))).ToArray();
        var colors = cards.Select(c => c.EffectiveSuit is Suit.Heart or Suit.Diamond ? (bool?)true : c.EffectiveSuit is Suit.Spade or Suit.Club ? false : null).Distinct().ToArray();
        var r = new OrdinaryBingyiReceipt(OrdinaryGuYongSource(f), f.GameplayHash, context.MovementBatch!.DiscardRecoveryPhase!, context.ParentFrameId, context.MovementBatch.Id, context.OccurrenceIndex, colors.Length == 1, colors.Length == 1 ? colors[0] : null, OrdinaryBingyiStage.SelectingRecipients) { Paid = true, ShownCards = cards };
        ReplaceRuntimeTop(f = f with { OrdinaryBingyi = r });
        AdvanceEventRulesAndQueueFact(new OrdinaryBingyiShownEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.WindowId, r.BatchId, r.OccurrenceIndex, cards.Length, r.UniformColor, r.Color));
        for (var i = 0; i < cards.Length; i++) AdvanceEventRulesAndQueueFact(new OrdinaryBingyiShownEntityEvent(f.Id, i, cards[i].CardId, cards[i].PrintedKind, cards[i].EffectiveSuit));
        SetProgramCardSet(f.Id, OrdinaryBingyiShownBind, cards.Select(c => c.CardId).ToArray(), SkillProgramCardSetVisibility.Private, cards.Select(_ => CardLocation.Hand(f.OwnerSeat)).ToArray());
        RevealProgramBoundCards(GetActiveProgramFrame(f.Id), OrdinaryBingyiShownBind);
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【秉壹】，展示全部 {cards.Length} 张手牌。", f.OwnerSeat);
        f = GetActiveProgramFrame(f.Id);
        if (!r.UniformColor) { FinishOrdinaryBingyi(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishOrdinaryBingyi(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> OrdinaryBingyiChoices(ProgramSkillFrame f)
    {
        if (f.OrdinaryBingyi is not { Paid: true, UniformColor: true, Stage: OrdinaryBingyiStage.SelectingRecipients, RecipientsCommitted: false } r || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        PromptChoice Choice(string branch, int? peer, IReadOnlyList<int> after, string label) => new(new($"ordinary-bingyi.{f.Id}.{branch}.{peer?.ToString(CultureInfo.InvariantCulture) ?? "done"}"), label, [], after.Prepend(f.OwnerSeat).ToArray(), new Dictionary<string, string> { ["program-action"] = "ordinary-bingyi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["peer-seat"] = peer?.ToString(CultureInfo.InvariantCulture) ?? "" });
        var result = new List<PromptChoice>();
        foreach (var peer in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
        {
            var chosen = r.OtherRecipients.Contains(peer.Seat);
            if (!chosen && r.OtherRecipients.Count >= r.ShownCards.Count) continue;
            var next = chosen ? r.OtherRecipients.Where(s => s != peer.Seat).ToArray() : r.OtherRecipients.Append(peer.Seat).ToArray();
            result.Add(Choice("toggle", peer.Seat, next, (chosen ? "取消选择 " : "选择 ") + peer.Name));
        }
        result.Add(Choice("confirm", null, r.OtherRecipients, $"确定：自己与 {r.OtherRecipients.Count} 名其他角色各摸一张牌"));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishOrdinaryBingyi(ProgramSkillFrame f)
    {
        AssertOrdinaryBingyi(f);
        var choices = OrdinaryBingyiChoices(f);
        if (choices.Count == 0) { FinishOrdinaryBingyi(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"秉壹：选择至多 {f.OrdinaryBingyi!.ShownCards.Count} 名其他角色，自己与其各摸一张牌。", [], _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => p.Seat).ToArray(), SourceSeat: f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ordinary-bingyi")]
    private void AnswerOrdinaryBingyi(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrdinaryBingyi: { Stage: OrdinaryBingyiStage.SelectingRecipients } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !OrdinaryBingyiChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Ordinary Bingyi requires a current original recipient choice.");
        AssertOrdinaryBingyi(f); ClearPendingDecision();
        if (selected.Parameters["branch"] == "toggle")
        {
            var peer = int.Parse(selected.Parameters["peer-seat"], CultureInfo.InvariantCulture);
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, OrdinaryBingyi = r with { OtherRecipients = r.OtherRecipients.Contains(peer) ? r.OtherRecipients.Where(s => s != peer).ToArray() : r.OtherRecipients.Append(peer).ToArray() } });
            PublishOrdinaryBingyi(f); return;
        }
        var recipients = new[] { f.OwnerSeat }.Concat(r.OtherRecipients.OrderBy(s => RelativeTableOrder(r.Phase.ActualTurnOwnerSeat, s))).ToArray();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, OrdinaryBingyi = r with { RecipientsCommitted = true, Recipients = recipients, Stage = OrdinaryBingyiStage.Drawing } });
        AdvanceEventRulesAndQueueFact(new OrdinaryBingyiRecipientsCommittedEvent(f.Id, recipients.Length));
        for (var i = 0; i < recipients.Length; i++) AdvanceEventRulesAndQueueFact(new OrdinaryBingyiRecipientEvent(f.Id, i, recipients[i]));
        ContinueOrdinaryBingyi(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi)]
    private void ReturnOrdinaryBingyiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.OrdinaryBingyi is not { Paid: true, RecipientsCommitted: true, Stage: OrdinaryBingyiStage.Drawing } r || r.Cursor < 0 || r.Cursor >= r.Recipients.Count || outcome.RecipientSeat != r.Recipients[r.Cursor] || outcome.RequestedCount != 1 || ProgramEventHistory<OrdinaryBingyiDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.DrawIndex == r.Cursor)) throw new InvalidOperationException("Ordinary Bingyi lost or repeated its exact native recipient draw.");
        ReplaceRuntimeTop(f = f with { OrdinaryBingyi = r with { Stage = OrdinaryBingyiStage.DrawChildren, DrawOutcome = outcome }, PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new OrdinaryBingyiDrawReturnedEvent(f.Id, r.Cursor, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueOrdinaryBingyi(f.Id);
    }
    private bool DrainOrdinaryBingyi(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueOrdinaryBingyi(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { OrdinaryBingyi: { } r } f && f.Id == id)
        {
            AssertOrdinaryBingyi(f);
            if (r.Stage == OrdinaryBingyiStage.SelectingRecipients)
            {
                if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishOrdinaryBingyi(f); return; }
                if (_pendingDecision is null) PublishOrdinaryBingyi(f); return;
            }
            if (r.Stage == OrdinaryBingyiStage.DrawChildren)
            {
                if (DrainOrdinaryBingyi(f)) return;
                f = GetActiveProgramFrame(id); r = f.OrdinaryBingyi!;
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, OrdinaryBingyi = r with { Cursor = r.Cursor + 1, Stage = OrdinaryBingyiStage.Drawing, DrawOutcome = null } });
                continue;
            }
            if (r.Stage != OrdinaryBingyiStage.Drawing) throw new InvalidOperationException("Ordinary Bingyi has no extra resolution stage.");
            if (f.NativeDraw is not null) return;
            if (r.Cursor >= r.Recipients.Count || _winner != Winner.None) { FinishOrdinaryBingyi(f); return; }
            var seat = r.Recipients[r.Cursor];
            if (!_players[seat].IsAlive)
            {
                AdvanceEventRulesAndQueueFact(new OrdinaryBingyiRecipientSkippedEvent(f.Id, r.Cursor, seat));
                ReplaceRuntimeTop(f with { OrdinaryBingyi = r with { Cursor = r.Cursor + 1 } }); continue;
            }
            BeginOperationProgramDraw(f, seat, 1, new(OrdinaryBingyiDrawReason)); return;
        }
    }
    private void FinishOrdinaryBingyi(ProgramSkillFrame f)
    {
        var r = f.OrdinaryBingyi ?? throw new InvalidOperationException("Ordinary Bingyi completion lost its owning reveal.");
        AdvanceEventRulesAndQueueFact(new OrdinaryBingyiCompletedEvent(f.Id, r.UniformColor, r.Recipients.Count));
        ReplaceRuntimeTop(f = f with { OrdinaryBingyi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27598)]
    private bool ResumeOrdinaryBingyi(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrdinaryBingyi: not null } f || f.Id != id) return false; ContinueOrdinaryBingyi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27598)]
    private bool ReturnOrdinaryBingyiMovement(ProgramSkillFrame f) { if (f.OrdinaryBingyi is null) return false; ContinueOrdinaryBingyi(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi)]
    private PromptChoice ChooseOrdinaryBingyiAi(PendingDecision decision, ProgramSkillFrame f) => SelectAiProgramTargets(decision, SkillProgramTargetAiOrder.SupportDraw);
}
