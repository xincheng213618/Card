using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WangChangAction = "anniversary-wang-chang", WangChangDiscardReason = "skill-program.anniversary:wang-chang.discard", WangChangDrawReason = "skill-program.anniversary:kaiji.draw";
    private static CardConversionSource WangChangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryWangChangHost(GameEngine engine) : IAnniversaryWangChangHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginWangChang(f, op); }

    private Card[] WangChangOwnCosts(int seat, string instance) => GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Where(c =>
        !IsSelfHandCategoryDiscardForbidden(seat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !IsActiveProgramSourceEquipmentCard(seat, AnniversaryWangChangComposition.Kaiji, instance, c)).OrderBy(c => c.Id).ToArray();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 46500, SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji)]
    private bool CanActivateWangChang(ProgramActivationLegalityContext c) => c.Owner.IsAlive && c.Owner.MaxHp > 0 && c.OwnerSeat == _currentSeat && _phase == TurnPhase.Play &&
        (GetProgramConversionPolarity(c.OwnerSeat, AnniversaryWangChangComposition.Kaiji) == SkillPolarity.Yang ||
            GetSkillBindingShard(c.Owner).ProgramInstances.Where(i => i.SkillId == AnniversaryWangChangComposition.Kaiji).Any(i => WangChangOwnCosts(c.OwnerSeat, i.SkillInstanceId).Length > 0));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 46500, SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi)]
    private bool CanOfferWangChang(ProgramTriggerLegalityContext c) => c.Owner.IsAlive && c.OwnerSeat == _currentSeat && YangBiaoDiscardCount() > 0 && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat);

    private SkillProgramStepOutcome BeginWangChang(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryWangChang is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !WangChangOriginal(f, operation))
            throw new InvalidOperationException("Wang Chang lost his original phase activation or own Ending candidate.");
        var kaiji = operation == SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji;
        var polarity = kaiji ? GetProgramConversionPolarity(f.OwnerSeat, f.SkillId) : SkillPolarity.Yang;
        var maximum = kaiji ? _players[f.OwnerSeat].MaxHp : YangBiaoDiscardCount();
        var stage = !kaiji ? AnniversaryWangChangStage.Targets : polarity == SkillPolarity.Yang ? AnniversaryWangChangStage.Drawing : AnniversaryWangChangStage.OwnCards;
        var r = new AnniversaryWangChangReceipt(WangChangSource(f), f.GameplayHash, operation, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, _cardUseDebitPhaseInstanceId, maximum, polarity, stage);
        ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryWangChangStartedEvent(f.Id, r.Source, r.Hash, operation, r.ParentId, r.Turn, r.Phase, maximum, polarity));
        if (stage == AnniversaryWangChangStage.Drawing)
        {
            CommitProgramConversionPolarity(f); f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Before = _movementSequence, After = _movementSequence } });
            BeginOperationProgramDraw(f, f.OwnerSeat, maximum, new(WangChangDrawReason));
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishWangChang(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IEnumerable<(Card Card, CardLocation From, int Slot, bool Hidden)> WangChangForeignCards(ProgramSkillFrame f)
    {
        var r = f.AnniversaryWangChang!;
        if (r.Cursor >= r.Targets.Count || !_players[r.Targets[r.Cursor]].IsAlive) yield break;
        var subject = r.Targets[r.Cursor];
        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
        foreach (var item in _cardZones.CardsAt(new(zone, subject)).Select((card, index) => (Card: card, From: new CardLocation(zone, subject), Slot: index, Hidden: zone == CardZoneKind.Hand)))
            if (!IsForeignEquipmentDiscardPrevented(f.OwnerSeat, item.Card, item.From, OwnedCardMoveIntent.Discard) && !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, item.Card, item.From, OwnedCardMoveIntent.Discard)) yield return item;
    }
    private PromptChoice WangChangChoice(ProgramSkillFrame f, string verb, string suffix, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null, IReadOnlyDictionary<string, string>? extra = null)
    {
        var parameters = new Dictionary<string, string> { ["program-action"] = WangChangAction, ["verb"] = verb, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        if (extra is not null) foreach (var pair in extra) parameters[pair.Key] = pair.Value;
        return new(new($"wang-chang.{f.Id}.{verb}.{suffix}"), label, cards ?? [], targets ?? [], parameters);
    }
    private IReadOnlyList<PromptChoice> WangChangChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryWangChang!; var choices = new List<PromptChoice>();
        if (r.Stage == AnniversaryWangChangStage.OwnCards)
        {
            if (r.Cards.Count < r.Maximum) choices.AddRange(WangChangOwnCosts(f.OwnerSeat, f.SkillInstanceId).Where(c => !r.Cards.Contains(c.Id)).Select(c => WangChangChoice(f, "own", c.Id.ToString(CultureInfo.InvariantCulture), "选择 " + c.DisplayName, [c.Id])));
            if (r.Cards.Count > 0) choices.Add(WangChangChoice(f, "pay", "done", $"弃置已选{r.Cards.Count}张牌"));
        }
        else if (r.Stage == AnniversaryWangChangStage.Targets)
        {
            if (r.Targets.Count < r.Maximum) choices.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && !r.Targets.Contains(p.Seat)).Select(p => WangChangChoice(f, "target", p.Seat.ToString(CultureInfo.InvariantCulture), "选择 " + p.Name, targets: [p.Seat])));
            choices.Add(WangChangChoice(f, "seal", "done", r.Targets.Count == 0 ? "不选择角色" : $"确认{r.Targets.Count}名角色"));
        }
        else if (r.Stage == AnniversaryWangChangStage.ForeignCard)
            choices.AddRange(WangChangForeignCards(f).Select(m => WangChangChoice(f, "foreign", $"{m.From.Zone}.{m.Slot}", m.Hidden ? $"第{m.Slot + 1}张手牌" : PublicPileCardLabel(m.Card), m.Hidden ? [] : [m.Card.Id], extra: new Dictionary<string, string> { ["source-zone"] = m.From.Zone.ToString(), ["slot-index"] = m.Slot.ToString(CultureInfo.InvariantCulture), ["card-owner-seat"] = r.Targets[r.Cursor].ToString(CultureInfo.InvariantCulture) })));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishWangChang(ProgramSkillFrame f)
    {
        var r = f.AnniversaryWangChang!; var choices = WangChangChoices(f);
        if (choices.Count == 0) { ContinueWangChang(f.Id); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        var text = r.Stage == AnniversaryWangChangStage.OwnCards ? $"开济·阴：选择至少1张、至多{r.Maximum}张手牌或装备牌。" : r.Stage == AnniversaryWangChangStage.Targets ? $"平袭：选择至多{r.Maximum}名其他角色；全部弃牌后再分别使用杀。" : $"平袭：弃置{_players[r.Targets[r.Cursor]].Name}的一张牌；暗手牌只显示牌位。";
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, text, Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji, SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi)]
    private PromptChoice WangChangAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.AnniversaryWangChang!;
        if (r.Stage == AnniversaryWangChangStage.OwnCards) return p.Choices.FirstOrDefault(c => c.Parameters["verb"] == "pay") ?? p.Choices[0];
        if (r.Stage == AnniversaryWangChangStage.Targets) return p.Choices.FirstOrDefault(c => c.Targets is [var seat] && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[seat])) ?? p.Choices.Last();
        return p.Choices[0];
    }
    [ProgramAnswer(WangChangAction)] private void AnswerWangChang(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryWangChang: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !ValidWangChang(f) || !AssistedChoicesEqual([choice], WangChangChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Wang Chang rejects another owner, native cursor, cost card or target menu.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (!_players[f.OwnerSeat].IsAlive) { FinishWangChang(f); return; }
        switch (choice.Parameters["verb"])
        {
            case "own":
                ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Cards = r.Cards.Append(choice.Cards.Single()).ToArray() } }); PublishWangChang(f); return;
            case "pay":
                if (r.Cards.Count < 1 || r.Cards.Count > r.Maximum || !r.Cards.All(id => WangChangOwnCosts(f.OwnerSeat, f.SkillInstanceId).Any(c => c.Id == id))) throw new InvalidOperationException("Kaiji lost its accepted dynamic HE payment.");
                CommitProgramConversionPolarity(f); f = GetActiveProgramFrame(f.Id);
                PayWangChangDiscard(f, r.Cards, f.OwnerSeat); return;
            case "target":
                var target = choice.Targets.Single();
                ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Targets = r.Targets.Append(target).ToArray() } });
                AdvanceEventRulesAndQueueFact(new AnniversaryWangChangTargetSelectedEvent(f.Id, r.Targets.Count, target)); PublishWangChang(f); return;
            case "seal":
                ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { TargetsSealed = true, Cursor = 0, Stage = AnniversaryWangChangStage.ForeignCard } });
                AdvanceEventRulesAndQueueFact(new AnniversaryWangChangTargetsSealedEvent(f.Id, r.Targets.Count)); ContinueWangChang(f.Id); return;
            case "foreign":
                var material = WangChangForeignCards(f).Single(m => m.From.Zone.ToString() == choice.Parameters["source-zone"] && m.Slot.ToString(CultureInfo.InvariantCulture) == choice.Parameters["slot-index"]);
                PayWangChangDiscard(f, [material.Card.Id], r.Targets[r.Cursor]); return;
        }
        throw new InvalidOperationException("Wang Chang received an unknown native choice.");
    }
    private void PayWangChangDiscard(ProgramSkillFrame f, IReadOnlyList<int> ids, int subject)
    {
        var r = f.AnniversaryWangChang!; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Stage = AnniversaryWangChangStage.MovementChildren, Before = before, After = before, Moves = [] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, new(WangChangDiscardReason), (_, records) =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { AnniversaryWangChang = active.AnniversaryWangChang! with { After = _movementSequence, Moves = records } });
            if (subject != f.OwnerSeat) foreach (var move in records) RecordBianFuRenForeignDiscardProducer(move, f.OwnerSeat, f.Id);
            AdvanceEventRulesAndQueueFact(new AnniversaryWangChangDiscardPaidEvent(f.Id, r.Cursor, subject, records.Count, before, _movementSequence));
        });
        ContinueWangChang(f.Id); AdvanceRulesAndPublishState();
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji)] private void ReturnWangChangDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryWangChang;
        if (r is not { Stage: AnniversaryWangChangStage.Drawing, Polarity: SkillPolarity.Yang } || !ValidWangChang(f) || f.NativeDraw is not null || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Maximum || outcome.SequenceBefore != r.Before)
            throw new InvalidOperationException("Kaiji lost its one issued native draw return.");
        ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Stage = AnniversaryWangChangStage.MovementChildren, Draw = outcome, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryWangChangDrawReturnedEvent(f.Id, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter)); ContinueWangChang(f.Id);
    }
    private void ContinueWangChang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryWangChang: { } r } f || f.Id != id) return;
        if (!ValidWangChang(f)) throw new InvalidOperationException("Wang Chang lost his original issued operation or native continuation.");
        if (r.Stage is AnniversaryWangChangStage.Targets or AnniversaryWangChangStage.OwnCards) { if (_pendingDecision is null) PublishWangChang(f); return; }
        if (r.Stage == AnniversaryWangChangStage.MovementChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return;
            f = GetActiveProgramFrame(id); r = f.AnniversaryWangChang!;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji) { FinishWangChang(f); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r = r with { Cursor = r.Cursor + 1, Stage = AnniversaryWangChangStage.ForeignCard } });
        }
        if (r.Stage == AnniversaryWangChangStage.ForeignCard)
        {
            while (r.Cursor < r.Targets.Count && (!_players[r.Targets[r.Cursor]].IsAlive || !WangChangForeignCards(f).Any()))
            { ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r = r with { Cursor = r.Cursor + 1 } }); }
            if (r.Cursor < r.Targets.Count) { if (_pendingDecision is null) PublishWangChang(f); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r = r with { SlashCursor = 0, Stage = AnniversaryWangChangStage.ReturnedSlash } });
        }
        if (r.Stage == AnniversaryWangChangStage.ReturnedSlash)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishWangChang(f); return; }
            while (r.SlashCursor < r.Targets.Count)
            {
                var target = r.Targets[r.SlashCursor];
                if (_players[target].IsAlive && CanUseVirtualSlashTarget(_players[f.OwnerSeat], _players[target], CardKind.Slash, Suit.None,
                        effectiveRank: 0, physicalCardIds: [], conversionSource: r.Source, countsTowardLimit: false) &&
                    !IsDirectedCardTargetProhibited(f.OwnerSeat, target, CardKind.Slash) && !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null))
                {
                    ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r with { Stage = AnniversaryWangChangStage.IssuingSlash, UseId = 0, ActionId = 0, NativeReturned = false } });
                    UseTriggeredProgramVirtualSlash(f, target); return;
                }
                ReplaceRuntimeTop(f = f with { AnniversaryWangChang = r = r with { SlashCursor = r.SlashCursor + 1 } });
            }
            FinishWangChang(f);
        }
    }
    private void FinishWangChang(ProgramSkillFrame f)
    {
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { AnniversaryWangChang = f.AnniversaryWangChang! with { Stage = AnniversaryWangChangStage.Completed }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new AnniversaryWangChangCompletedEvent(f.Id)); AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 46500)] private bool ResumeWangChang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryWangChang: { Stage: not AnniversaryWangChangStage.Completed } } f || f.Id != id) return false;
        ContinueWangChang(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 46500)] private bool ReturnWangChangMovement(ProgramSkillFrame f) => f.AnniversaryWangChang is not null && ResumeWangChang(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 46500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 46500)]
    private bool KeepWangChangPaid(ProgramSkillFrame f) => f.AnniversaryWangChang is { } r && (r.TargetsSealed || r.Stage is AnniversaryWangChangStage.Drawing or AnniversaryWangChangStage.MovementChildren) && ValidWangChang(f);
}
