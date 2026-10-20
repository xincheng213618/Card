using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string KuangxiangDrawReason = "skill-program.cheng-gong-ying.kuangxiang.draw", KuangxiangDiscardReason = "skill-program.cheng-gong-ying.kuangxiang.discard";
    private bool HasChengGongYingCapability => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AdjustKuangxiangHandToFour) == true;
    private static CardConversionSource ChengGongYingSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cheng_gong_ying_ol_ChengGongYing.ChengGongYingSource(f);
    [ProgramCapability] private sealed class ChengGongYingCapability(GameEngine engine) : IChengGongYingHost
    { public SkillProgramStepOutcome Adjust(ProgramSkillFrame frame) => engine.BeginChengGongYing(frame); }
    private bool ChengGongYingParent(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c) =>
        c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && c.OwnerSeat == candidate.OwnerSeat && c.SourceSeat == candidate.OwnerSeat && c.TargetSeat == candidate.OwnerSeat &&
        _resolutionStack.SingleOrDefault(p => p.Id == c.ParentFrameId) is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } w &&
        w.OwnerSeat == candidate.OwnerSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && w.Candidates[w.CandidateIndex] == candidate;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27100)]
    private bool CanOfferChengGongYing(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustKuangxiangHandToFour) || ChengGongYingParent(candidate, c);
    private SkillProgramStepOutcome BeginChengGongYing(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ChengGongYing is not null || f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } c || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !ChengGongYingParent(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c))
            throw new InvalidOperationException("Kuangxiang requires its exact unpaid actual Preparation candidate.");
        var r = new ChengGongYingReceipt(ChengGongYingSource(f), f.GameplayHash, c.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, ChengGongYingStage.TargetChoice);
        ReplaceRuntimeTop(f = f with { ChengGongYing = r });
        AdvanceEventRulesAndQueueFact(new ChengGongYingStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Turn, r.TurnOwner));
        PublishChengGongYing(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool ChengGongYingCanDiscard(int payer, int id) => _cardZones.Contains(CardLocation.Hand(payer), id) &&
        !IsForeignEquipmentDiscardPrevented(payer, GetAdvancedCard(id), CardLocation.Hand(payer), OwnedCardMoveIntent.Discard) &&
        !IsSelfHandCategoryDiscardForbidden(payer, GetAdvancedCard(id), CardLocation.Hand(payer), OwnedCardMoveIntent.Discard);
    private int ChengGongYingChooser(ProgramSkillFrame f) => f.ChengGongYing!.Stage == ChengGongYingStage.DiscardChoice ? f.ChengGongYing.Recipient!.Value : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> ChengGongYingChoices(ProgramSkillFrame f)
    {
        var r = f.ChengGongYing!;
        PromptChoice Choice(string branch, string label, int? card = null, int? target = null) => new(new($"cheng-gong-ying.{f.Id}.{r.Stage}.{branch}.{card}.{target}.{r.Selected.Count}"), label,
            card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "cheng-gong-ying", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (_winner != Winner.None) return [];
        if (r.Stage == ChengGongYingStage.TargetChoice) return Array.AsReadOnly(_players.Where(p => p.IsAlive).Select(p => Choice("target", $"令{p.Name}将手牌调整为四张", target: p.Seat)).ToArray());
        if (r.Stage != ChengGongYingStage.DiscardChoice || r.Recipient is not { } payer || !_players[payer].IsAlive) return [];
        var choices = r.Candidates.Where(id => !r.Selected.Contains(id) && r.Selected.Count < r.Count && ChengGongYingCanDiscard(payer, id)).Select(id => Choice("pick", $"选择【{GetAdvancedCard(id).DisplayName}】", id)).ToList();
        choices.AddRange(r.Selected.Select(id => Choice("unpick", $"撤回【{GetAdvancedCard(id).DisplayName}】", id)));
        if (r.Selected.Count == r.Count) choices.Add(Choice("finish", $"弃置{r.Count}张手牌"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishChengGongYing(ProgramSkillFrame f)
    {
        var choices = ChengGongYingChoices(f); if (choices.Count == 0) { FinishChengGongYing(f); return; }
        PublishParticipantHandChoice(f, ChengGongYingChooser(f), choices, f.ChengGongYing!.Stage == ChengGongYingStage.TargetChoice ? "匡襄：选择一名角色。" : "匡襄：由获选角色弃置手牌至四张。", f.ChengGongYing.Recipient ?? f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("cheng-gong-ying")]
    private void AnswerChengGongYing(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ChengGongYing: { } r } f || _pendingDecision?.PlayerSeat != ChengGongYingChooser(f) ||
            !AssistedChoicesEqual([choice], ChengGongYingChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Kuangxiang lost its real chooser or selected original hand entities.");
        AssertChengGongYing(f); ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (r.Stage == ChengGongYingStage.TargetChoice)
        {
            var payer = choice.Targets.Single(); var hand = GetHand(_players[payer]); var count = Math.Abs(hand.Count - 4); var draw = hand.Count < 4;
            var candidates = hand.Where(c => ChengGongYingCanDiscard(payer, c.Id)).Select(c => c.Id).ToArray();
            ReplaceRuntimeTop(f = f with { ChengGongYing = r with { Recipient = payer, InitialHandCount = hand.Count, Count = draw ? count : Math.Min(count, candidates.Length), Candidates = draw ? [] : candidates, Stage = draw ? ChengGongYingStage.DrawChildren : ChengGongYingStage.DiscardChoice } });
            AdvanceEventRulesAndQueueFact(new KuangxiangTargetChosenEvent(f.Id, payer, hand.Count, count, draw));
            if (draw) IssueChengGongYingDraw(f);
            else if (hand.Count == 4 || count == 0) IssueChengGongYingDiscard(f);
            else PublishChengGongYing(f);
            return;
        }
        if (branch != "finish")
        { var id = choice.Cards.Single(); ReplaceRuntimeTop(f = f with { ChengGongYing = r with { Selected = branch == "pick" ? r.Selected.Append(id).ToArray() : r.Selected.Where(c => c != id).ToArray() } }); PublishChengGongYing(f); return; }
        if (r.Selected.Count != r.Count || r.Selected.Any(id => !ChengGongYingCanDiscard(r.Recipient!.Value, id))) throw new InvalidOperationException("Kuangxiang cannot discard replaced original hand entities.");
        IssueChengGongYingDiscard(f);
    }
    private void IssueChengGongYingDraw(ProgramSkillFrame f)
    {
        var r = f.ChengGongYing!; var before = _movementSequence; var payer = r.Recipient ?? throw new InvalidOperationException("Kuangxiang draw requires its frozen recipient.");
        ReplaceRuntimeTop(f = f with { ChengGongYing = r with { Stage = ChengGongYingStage.DrawChildren, Before = before, After = before }, PendingMovementContinuation = new(payer, 0, null) });
        if (r.Count > 0 && _players[payer].IsAlive && _winner == Winner.None) DrawProgramCards(f.Id, payer, r.Count, null, null, SkillProgramCardSetVisibility.Private, new(KuangxiangDrawReason));
        RecordChengGongYingMovement(f.Id);
        f = GetActiveProgramFrame(f.Id); r = f.ChengGongYing!;
        var gains = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(payer) && m.Reason.Value == KuangxiangDrawReason).ToArray();
        foreach (var move in gains) AdvanceEventRulesAndQueueFact(new KuangxiangCardObtainedEvent(f.Id, r.Source, payer, move.CardId, move.Sequence, GetProgramEffectiveSuit(_players[payer], GetAdvancedCard(move.CardId))));
        var reds = ProgramEventHistory<KuangxiangCardObtainedEvent>().Count(e => e.FrameId == f.Id && IsRedSuit(e.EffectiveSuit));
        if (reds > 0) AdvanceEventRulesAndQueueFact(new KuangxiangRedDrawIssuedEvent(f.Id, payer, reds, ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>().Select(e => e.Phase.Token).DefaultIfEmpty().Max()));
        ContinueChengGongYing(f.Id);
    }
    private void IssueChengGongYingDiscard(ProgramSkillFrame f)
    {
        var r = f.ChengGongYing!; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ChengGongYing = r with { Stage = ChengGongYingStage.DiscardChildren, Before = before, After = before }, PendingMovementContinuation = new(r.Recipient!.Value, 0, null) });
        if (r.Selected.Count > 0) MoveProgramLogicalDiscard(f, r.Recipient!.Value, r.Selected, new(KuangxiangDiscardReason));
        RecordChengGongYingMovement(f.Id); ContinueChengGongYing(f.Id);
    }
    private void RecordChengGongYingMovement(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.ChengGongYing!; var after = _movementSequence;
        var count = _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= after && (r.Stage != ChengGongYingStage.DrawChildren || m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.Recipient!.Value) && m.Reason.Value == KuangxiangDrawReason));
        var shuffles = r.Stage == ChengGongYingStage.DrawChildren ? _pendingCardsMovedBatches.Where(b => b.ParentFrameId == id && b.ParentBatchId is null && b.AwaitingProgramFrameId == id && b.TurnNumber == r.Turn && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.Count > 0 && b.Movements.All(m => m.Sequence > r.Before && m.Sequence <= after && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle && _cardMovements.Contains(m)) && b.SourceCounts is [{ Location: var source, CountBefore: var discarded, CountAfter: 0 }] && source == CardLocation.DiscardPile && discarded == b.Movements.Count && b.DestinationCounts is [{ Location: var destination, CountBefore: 0, CountAfter: var replenished }] && destination == CardLocation.DrawPile && replenished == b.Movements.Count)
            .Select(b => new ChengGongYingReshuffle(b.Id, id, b.Movements[0].Sequence - 1, b.Movements[^1].Sequence) { Movements = b.Movements }).ToArray() : [];
        ReplaceRuntimeTop(f with { ChengGongYing = r with { After = after, ActualCount = count, Reshuffles = shuffles } });
        foreach (var shuffle in shuffles) AdvanceEventRulesAndQueueFact(new KuangxiangReshuffleCapturedEvent(id, shuffle.BatchId, shuffle.Before, shuffle.After, shuffle.Movements.Count));
        AdvanceEventRulesAndQueueFact(new KuangxiangAdjustmentIssuedEvent(id, r.Recipient!.Value, r.Stage == ChengGongYingStage.DrawChildren, r.Before, after, count));
    }
    private void ContinueChengGongYing(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ChengGongYing: { } r } f || f.Id != id) return;
        AssertChengGongYing(f);
        if (r.Stage is ChengGongYingStage.TargetChoice or ChengGongYingStage.DiscardChoice) { if (_pendingDecision is null) PublishChengGongYing(f); return; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        FinishChengGongYing(GetActiveProgramFrame(id));
    }
    private void FinishChengGongYing(ProgramSkillFrame f)
    { var applied = ProgramEventHistory<KuangxiangAdjustmentIssuedEvent>().Any(e => e.FrameId == f.Id); AdvanceEventRulesAndQueueFact(new ChengGongYingCompletedEvent(f.Id, applied)); ReplaceRuntimeTop(f = f with { ChengGongYing = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27100)] private bool ResumeChengGongYing(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ChengGongYing: not null } f || f.Id != id) return false; ContinueChengGongYing(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27100)] private bool ReturnChengGongYingMovement(ProgramSkillFrame f)
    { if (f.ChengGongYing is null) return false; ContinueChengGongYing(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 270)]
    private bool IsChengGongYingMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) => f.ChengGongYing is { Recipient: { } payer, Stage: ChengGongYingStage.DrawChildren or ChengGongYingStage.DiscardChildren } && movement.SubjectSeat == payer && movement.BeforeCount == 0 && movement.CoverageResultBind is null && (effect is null || effect.Op == SkillProgramEffectOp.AdjustKuangxiangHandToFour);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27100)] private bool ContinuePaidChengGongYing(ProgramSkillFrame f)
    { if (f.ChengGongYing is null) return false; AssertChengGongYing(f); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.AdjustKuangxiangHandToFour)] private PromptChoice AiChengGongYing(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "finish") ?? p.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "target").OrderBy(c => Math.Abs(_players[c.Targets.Single()].Seat - f.OwnerSeat)).FirstOrDefault() ?? p.Choices.First(c => c.Parameters.GetValueOrDefault("branch") != "unpick");
}
