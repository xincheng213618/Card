using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RuanJiDiscardAction = "anniversary-ruan-ji-discard", RuanJiRevealReturn = "anniversary-ruan-ji-shown";
    private static CardConversionSource RuanJiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool KeepsAnniversaryRuanJiAlcohol(int seat) => IsValidPlayerSeat(seat) &&
        _players[seat].IsAlive && HasRuntimeSkill(_players[seat], AnniversaryRuanJiComposition.Jiudun);
    [ProgramCapability]
    private sealed class AnniversaryRuanJiCapability(GameEngine engine) : IAnniversaryRuanJiHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginRuanJi(f, op); }

    private CardActionContext? RuanJiWindowAction(ProgramSkillWindowContext context) => context.CardUse is { } c
        ? _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId && w.Action.ActionId == c.CardActionId)?.Action ??
          LifecycleCardUse(c.ParentCardUseFrameId)?.Action ?? ProgramEventHistory<CardActionAcceptedEvent>().LastOrDefault(e => e.Action.ActionId == c.CardActionId)?.Action
        : null;
    private bool RuanJiIncoming(int owner, ProgramSkillWindowContext context) =>
        context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && context.CardUse is { ActorSeat: var actor, IsPublicRed: false } c &&
        actor != owner && LifecycleCardUse(c.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
        action.ActionId == c.CardActionId && action.ActorSeat == actor && action.EffectiveDesignatedTargetSeats.Contains(owner) &&
        use.TargetSeats.Contains(owner);
    private Card[] RuanJiDiscardable(int owner) => GetHand(_players[owner]).Where(card =>
        !IsSelfHandCategoryDiscardForbidden(owner, card, CardLocation.Hand(owner), OwnedCardMoveIntent.Discard)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 42800,
        SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen, SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse, SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun)]
    private bool RuanJiTriggerLegal(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        return trigger.Effects.Single().Op switch
        {
            SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen => GetHand(_players[candidate.OwnerSeat]).Count > 0,
            SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse => RuanJiWindowAction(context) is { } action &&
                HasRuanJiShownRedUse(candidate.OwnerSeat, candidate.SkillInstanceId, action),
            _ => RuanJiIncoming(candidate.OwnerSeat, context) && (!_players[candidate.OwnerSeat].HasAlcoholEffect || RuanJiDiscardable(candidate.OwnerSeat).Length > 0)
        };
    }

    private SkillProgramStepOutcome BeginRuanJi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.RuanJi is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Ruan Ji requires his original unpaid trigger frame.");
        var incoming = op == SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun;
        if (incoming && !RuanJiIncoming(f.OwnerSeat, context)) throw new InvalidOperationException("Jiudun lost its genuine other-owner black Use target.");
        var r = new RuanJiReceipt(RuanJiSource(f), f.GameplayHash, op, f.InstructionIndex, context.ParentFrameId, _turnNumber,
            context.CardUse?.ParentCardUseFrameId ?? 0, context.CardUse?.CardActionId ?? 0,
            incoming && _players[f.OwnerSeat].HasAlcoholEffect,
            op == SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen ? RuanJiStage.Revealing : RuanJiStage.Drawing);
        if (op == SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen)
            r = r with { Hand = GetHand(_players[f.OwnerSeat]).Select(c => new RuanJiShownEntity(c.Id, c.Kind, EffectiveSuit(_players[f.OwnerSeat], c), SuitColor(EffectiveSuit(_players[f.OwnerSeat], c)))).ToArray() };
        ReplaceRuntimeTop(f = f with { RuanJi = r });
        AdvanceEventRulesAndQueueFact(new RuanJiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.ActualTurn, r.UseId, r.ActionId, r.WasDrunk));
        if (op == SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen)
        {
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Select(ToSnapshot).ToArray())));
            if (TryBeginPublicHandRevealWindow(f.Id, RuanJiRevealReturn)) return SkillProgramStepOutcome.AwaitChild;
            CompleteRuanJiReveal(GetActiveProgramFrame(f.Id)); return SkillProgramStepOutcome.AwaitChild;
        }
        if (r.WasDrunk)
        {
            ReplaceRuntimeTop(f = f with { RuanJi = r with { Stage = RuanJiStage.DiscardChoice } });
            PublishRuanJiDiscard(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(op == SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse
            ? "skill-program.anniversary:zhaowen.red" : "skill-program.anniversary:jiudun.draw"));
        return SkillProgramStepOutcome.AwaitChild;
    }
    [PublicHandRevealReturn(RuanJiRevealReturn)]
    private void CompleteRuanJiReveal(ProgramSkillFrame f)
    {
        RequireRuanJi(f); var r = f.RuanJi!;
        if (r.Stage != RuanJiStage.Revealing) throw new InvalidOperationException("Zhaowen's native public reveal returned twice.");
        foreach (var card in r.Hand)
            AdvanceEventRulesAndQueueFact(new RuanJiShownEntityAuthorizedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, card.Id, card.Kind, card.Suit, card.IsRed));
        FinishRuanJi(GetActiveProgramFrame(f.Id));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse, SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun)]
    private void ReturnRuanJiDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); RequireRuanJi(f); var r = f.RuanJi!;
        if (r.Stage != RuanJiStage.Drawing || r.Draw is not null || f.NativeDraw is not null || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != 1)
            throw new InvalidOperationException("Ruan Ji lost his exact original native Draw1 return.");
        ReplaceRuntimeTop(f = f with { RuanJi = r with { Stage = RuanJiStage.DrawChildren, Draw = draw } });
        AdvanceEventRulesAndQueueFact(new RuanJiDrawReturnedEvent(f.Id, draw.AttemptFrameId, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter));
        ContinueRuanJi(f.Id);
    }
    private IReadOnlyList<PromptChoice> RuanJiDiscardChoices(ProgramSkillFrame f) => Array.AsReadOnly(RuanJiDiscardable(f.OwnerSeat).Select(c =>
        new PromptChoice(new($"ruan-ji.{f.Id}.discard.{c.Id}"), $"弃置手牌【{c.DisplayName}】，令此牌对你无效", [c.Id], [],
            new Dictionary<string, string> { ["program-action"] = RuanJiDiscardAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    private void PublishRuanJiDiscard(ProgramSkillFrame f)
    {
        var choices = RuanJiDiscardChoices(f);
        if (choices.Count == 0) { FinishRuanJi(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "酒遁：弃置一张手牌，令原黑色牌对你无效。", choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(RuanJiDiscardAction)]
    private void AnswerRuanJiDiscard(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RuanJi.Stage: RuanJiStage.DiscardChoice } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !RuanJiDiscardChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Jiudun requires its owner's exact private current hand payment.");
        RequireRuanJi(f); ClearPendingDecision(); var r = f.RuanJi!; var cardId = selected.Cards.Single();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
            RuanJi = r with { Stage = RuanJiStage.DiscardChildren, DiscardId = cardId, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [cardId], new("skill-program.anniversary:jiudun.discard"), (batch, records) =>
        {
            if (records is not [var paid] || paid.CardId != cardId || paid.From != CardLocation.Hand(f.OwnerSeat) ||
                paid.To != CardLocation.DiscardPile && !IsRedirectedPaidDiscardMovement(paid, paid.From, new("skill-program.anniversary:jiudun.discard")))
                throw new InvalidOperationException("Jiudun did not pay its selected real hand discard.");
            var active = GetActiveProgramFrame(f.Id); var receipt = active.RuanJi!;
            ReplaceRuntimeTop(active with { RuanJi = receipt with { After = paid.Sequence, BatchId = batch } });
            AdvanceEventRulesAndQueueFact(new RuanJiDiscardPaidEvent(active.Id, cardId, batch, receipt.Before, paid.Sequence));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainRuanJi(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueRuanJi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RuanJi: { } r } f || f.Id != id) return;
        RequireRuanJi(f);
        if (r.Stage is RuanJiStage.Revealing or RuanJiStage.Drawing) return;
        if (r.Stage == RuanJiStage.DiscardChoice) { if (_pendingDecision is null) PublishRuanJiDiscard(f); return; }
        if (DrainRuanJi(id)) return;
        f = GetActiveProgramFrame(id); r = f.RuanJi!;
        if (r.Stage == RuanJiStage.DiscardChildren)
        {
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            MarkCardEffectIneffective(r.UseId, f.OwnerSeat);
            var use = LifecycleCardUse(r.UseId)!;
            AdvanceEventRulesAndQueueFact(new ProgramCardEffectNullifiedEvent(f.Id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat, use.SourceSeat, use.Id, use.CardKind));
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, use.SourceSeat, f.OwnerSeat, use.CardKind, CardEffectSkipReason.SkillNullified));
            FinishRuanJi(f); return;
        }
        if (r.Stage == RuanJiStage.WineIssued)
        {
            if (f.VirtualBasicDraft is not null) throw new InvalidOperationException("Jiudun resumed before its native Wine typed return.");
            if (ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.WineId && e.CardId == 0 && e.CardKind == CardKind.Alcohol) != 1)
                throw new InvalidOperationException("Jiudun lost its native Wine completion.");
            AdvanceEventRulesAndQueueFact(new RuanJiWineReturnedEvent(f.Id, r.WineId!.Value, r.WineActionId!.Value));
            FinishRuanJi(f); return;
        }
        if (r.Operation == SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            _players[f.OwnerSeat].HasAlcoholEffect || IsCardUseForbidden(f.OwnerSeat, CardKind.Alcohol, CardActionType.Use) ||
            IsDirectedCardTargetProhibited(f.OwnerSeat, f.OwnerSeat, CardKind.Alcohol))
        { FinishRuanJi(f); return; }
        BeginRuanJiWine(f);
    }
    private void BeginRuanJiWine(ProgramSkillFrame f)
    {
        var r = f.RuanJi!; var actor = _players[f.OwnerSeat]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, r.ActionId, CardActionType.Use,
            actor.Seat, actor.Seat, null, null, null, CardKind.Alcohol, [actor.Seat], [], [r.Source], effectiveSuit: Suit.None, effectiveRank: 0));
        ReplaceRuntimeTop(f = f with { RuanJi = r with { Stage = RuanJiStage.WineIssued, WineId = id, WineActionId = action.ActionId },
            VirtualBasicDraft = new(f.InstructionIndex, id) });
        AdvanceEventRulesAndQueueFact(new RuanJiWineIssuedEvent(f.Id, id, action.ActionId, r.UseId));
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, CardKind.Alcohol, [actor.Seat], PhysicalCardIds: [])
        { Action = action, VirtualBasicReturn = new(f.Id, f.InstructionIndex), FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Alcohol, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [actor.Seat]));
        RecordYingboCardUse(id, actor.Seat, CardKind.Alcohol); RecordProgramUsedBasicCard(actor.Seat, CardKind.Alcohol); RecordActualPlayPhaseUse(action);
        BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol));
    }
    private void FinishRuanJi(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { RuanJi = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 42800)]
    private bool ResumeRuanJi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RuanJi: not null } f || f.Id != id) return false; ContinueRuanJi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 42800)]
    private bool ReturnRuanJiMovement(ProgramSkillFrame f) => f.RuanJi is not null && ResumeRuanJi(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun)]
    private PromptChoice AiRuanJiDiscard(PendingDecision prompt, ProgramSkillFrame frame) => prompt.Choices.First();
}
