namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : ISlashSuitDiscardProgramHost
    {
        public SkillProgramStepOutcome SuppressCurrentSlashTargetAndJudgeSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginProgramSlashSuitDiscard(frame, effect);
    }

    private CardAttackHandle SlashSuitDiscardAttack(ProgramSkillFrame frame, bool requireActive = true)
    {
        var context = frame.WindowContext;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(item => item.Id == context?.ParentFrameId);
        var attack = window?.AttackOwnerFrameId is { } ownerId ? new CardAttackHandle(this, ownerId) : throw new InvalidOperationException("Suit discard lost its Slash owner.");
        var use = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        if (context is not { Window: SkillProgramTriggerWindow.SlashBeforeResponse, CardUse: { } action } ||
            context.TargetSeat != attack.TargetSeat || frame.OwnerSeat != attack.CardUserSeat ||
            use.Action?.ActionId != action.CardActionId || action.ParentCardUseFrameId != use.Id ||
            !IsSlashCard(use.CardKind) || requireActive && !SameAttackOwner(ActiveCardAttack, attack) ||
            frame.SlashSuitDiscard is { } draft &&
            (draft.CardUseFrameId != use.Id || draft.CardActionId != action.CardActionId || draft.TargetSeat != attack.TargetSeat))
            throw new InvalidOperationException("Suit discard lost its exact final target or actual Slash.");
        return attack;
    }

    private SkillProgramStepOutcome BeginProgramSlashSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var attack = SlashSuitDiscardAttack(frame);
        if (frame.SlashSuitDiscard is not null || !_players[frame.OwnerSeat].IsAlive || !_players[attack.TargetSeat].IsAlive)
            throw new InvalidOperationException("Suit judgment requires live actual participants and a clean cursor.");
        frame = frame with { SlashSuitDiscard = new(attack.ResolutionId, frame.WindowContext!.CardUse!.CardActionId,
            attack.TargetSeat, effect.JudgmentReason!, effect.ResultBind!, ProgramSlashSuitDiscardStage.Judging) };
        ReplaceRuntimeTop(frame);
        UpdateCardAttackState(attack.ResolutionId, state => state! with
        { JudgmentSuitDodgeRestriction = new(frame.Id, attack.TargetSeat) });
        IssueCurrentTurnNonLockedSkillSuppression(frame, attack.TargetSeat);
        return StartProgramJudgment(GetActiveProgramFrame(frame.Id), frame.OwnerSeat, effect.JudgmentReason!,
            effect.ResultBind!, SkillProgramCardSetVisibility.Public, frame.OwnerSeat);
    }

    // Freeze at the actual final result, before finalized-judgment gain/damage children can move the card.
    private void CaptureProgramSlashSuitJudgment(JudgmentFrame judgment, Suit suit)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == judgment.ParentFrameId) is not
            { SlashSuitDiscard: { Stage: ProgramSlashSuitDiscardStage.Judging } draft } frame) return;
        if (judgment.Continuation != JudgmentContinuationKind.ProgramSkill || judgment.TargetSeat != frame.OwnerSeat ||
            judgment.SourceSeat != frame.OwnerSeat || judgment.Reason != draft.JudgmentReason ||
            judgment.ProgramResultBind != draft.ResultBind || judgment.ParentAttackId != draft.CardUseFrameId ||
            draft.JudgmentFrameId is not null)
            throw new InvalidOperationException("Suit judgment result lost its typed parent.");
        ReplaceRuntimeFrame(frame.Id, frame with { SlashSuitDiscard = draft with
        { JudgmentFrameId = judgment.Id, FinalSuit = suit } });
    }

    private bool TryReturnProgramSlashSuitJudgment(JudgmentFrame judgment)
    {
        if (GetActiveProgramFrame(judgment.ParentFrameId) is not { SlashSuitDiscard: { } draft } frame) return false;
        if (draft.Stage != ProgramSlashSuitDiscardStage.Judging || judgment.TargetSeat != frame.OwnerSeat ||
            judgment.SourceSeat != frame.OwnerSeat || judgment.Reason != draft.JudgmentReason ||
            judgment.ProgramResultBind != draft.ResultBind || judgment.ParentAttackId != draft.CardUseFrameId ||
            draft.JudgmentFrameId is { } actualId && actualId != judgment.Id)
            throw new InvalidOperationException("Suit judgment receipt lost its exact instruction.");
        _ = SlashSuitDiscardAttack(frame);
        frame = frame with { SlashSuitDiscard = draft with
        { Stage = ProgramSlashSuitDiscardStage.JudgmentMovement, JudgmentFrameId = judgment.Id } };
        ReplaceRuntimeTop(frame);
        // A finalized-judgment claimant keeps its real card. Only our unclaimed Processing result is disposed.
        if (GetJudgmentCard(judgment) is { } card && _cardZones.GetLocation(card.Id) == CardLocation.Processing)
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.JudgmentFinish);
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private bool ResumeProgramSlashSuitDiscard(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.SlashSuitDiscard is not { } draft) return false;
        if (frame.PendingMovementContinuation is not null) return true;
        _ = SlashSuitDiscardAttack(frame);
        if (draft.Stage == ProgramSlashSuitDiscardStage.Judging) return true;
        if (draft.Stage == ProgramSlashSuitDiscardStage.Choosing) return true;
        if (draft.Stage == ProgramSlashSuitDiscardStage.PaymentMovement)
        {
            var paid = draft.PaymentMovementSequence is { } sequence && draft.PaidCardId is { } cardId &&
                _cardMovements.Any(move => move.Sequence == sequence && move.CardId == cardId &&
                    move.From == draft.PaidFrom && move.From.OwnerSeat == draft.TargetSeat &&
                    move.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                    move.To == CardLocation.DiscardPile && move.Reason.Value == "skill-program.slash-suit-discard.payment");
            FinishProgramSlashSuitDiscard(frame, paid);
            return false;
        }
        if (_winner != Winner.None || !_players[draft.TargetSeat].IsAlive || draft.FinalSuit is null or Suit.None ||
            !SlashSuitDiscardCards(draft).Any())
        {
            FinishProgramSlashSuitDiscard(frame, false);
            return false;
        }
        frame = frame with { SlashSuitDiscard = draft with { Stage = ProgramSlashSuitDiscardStage.Choosing } };
        ReplaceRuntimeTop(frame);
        PublishProgramSlashSuitDiscard(frame);
        return true;
    }

    private IEnumerable<Card> SlashSuitDiscardCards(ProgramSlashSuitDiscardDraft draft) =>
        GetHand(_players[draft.TargetSeat]).Concat(GetEquipment(_players[draft.TargetSeat]))
            .Where(card => draft.FinalSuit is { } suit && suit != Suit.None &&
                EffectiveSuit(_players[draft.TargetSeat], card) == suit);

    private IReadOnlyList<PromptChoice> SlashSuitDiscardChoices(ProgramSkillFrame frame)
    {
        var draft = frame.SlashSuitDiscard!;
        Dictionary<string, string> Parameters() => new() { ["program-action"] = "slash-suit-discard", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return SlashSuitDiscardCards(draft).Select(card => new PromptChoice(new($"slash-suit-discard.{frame.Id}.pay.{card.Id}"),
            $"弃置【{card.DisplayName}】{GetSuitDisplayName(draft.FinalSuit)}{card.RankText}", [card.Id], [], Parameters()))
            .Append(new(new($"slash-suit-discard.{frame.Id}.decline"), "不弃置，不能抵消此杀", [], [], Parameters())).ToArray();
    }

    private void PublishProgramSlashSuitDiscard(ProgramSkillFrame frame)
    {
        var draft = frame.SlashSuitDiscard!;
        var choices = SlashSuitDiscardChoices(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.TargetSeat,
            $"【{skill.Name}】：是否弃置一张{GetSuitDisplayName(draft.FinalSuit)}牌，使你能抵消此杀？",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, TargetSeat = draft.TargetSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool IsProgramSlashSuitDiscardChoiceLegal(PromptChoice choice) =>
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { SlashSuitDiscard: { Stage: ProgramSlashSuitDiscardStage.Choosing } } frame &&
        choice.Parameters.GetValueOrDefault("frame-id") == frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
        SlashSuitDiscardChoices(frame).Any(candidate => candidate.Id == choice.Id);

    private void ResolveProgramSlashSuitDiscardChoice(PromptChoice choice)
    {
        var frame = GetActiveProgramFrame(_resolutionStack[^1].Id);
        var draft = frame.SlashSuitDiscard!;
        if (!IsProgramSlashSuitDiscardChoiceLegal(choice)) throw new InvalidOperationException("Suit discard choice is stale.");
        _ = SlashSuitDiscardAttack(frame);
        ClearPendingDecision();
        if (choice.Cards is not [var cardId] || !_players[draft.TargetSeat].IsAlive || _winner != Winner.None)
        { FinishProgramSlashSuitDiscard(frame, false); AdvanceRuntimeProgram(frame.Id); return; }
        var from = _cardZones.GetLocation(cardId);
        var card = SlashSuitDiscardCards(draft).Single(candidate => candidate.Id == cardId);
        frame = frame with { SlashSuitDiscard = draft with
        { Stage = ProgramSlashSuitDiscardStage.PaymentMovement, PaidCardId = cardId, PaidFrom = from } };
        ReplaceRuntimeTop(frame);
        MoveCard(card, from, CardLocation.DiscardPile, new("skill-program.slash-suit-discard.payment"));
        var sequence = _cardMovements.LastOrDefault(move => move.CardId == cardId && move.From == from &&
            move.To == CardLocation.DiscardPile && move.Reason.Value == "skill-program.slash-suit-discard.payment")?.Sequence;
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame with { SlashSuitDiscard = frame.SlashSuitDiscard! with { PaymentMovementSequence = sequence } });
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
    }

    private void FinishProgramSlashSuitDiscard(ProgramSkillFrame frame, bool paid)
    {
        var draft = frame.SlashSuitDiscard!;
        var state = GetCardAttackState(draft.CardUseFrameId);
        if (paid && state.JudgmentSuitDodgeRestriction is { } restriction && restriction.ProgramFrameId == frame.Id &&
            restriction.TargetSeat == draft.TargetSeat)
            UpdateCardAttackState(draft.CardUseFrameId, current => current! with { JudgmentSuitDodgeRestriction = null });
        ReplaceRuntimeTop(frame with { SlashSuitDiscard = null });
        AdvanceEventRulesAndQueueFact(new ProgramSlashSuitDiscardResolvedEvent(frame.Id, frame.SkillId, frame.OwnerSeat,
            draft.CardUseFrameId, draft.TargetSeat, draft.FinalSuit, draft.PaidCardId, paid));
    }

    private bool IsSlashDodgeCancellationPrevented(CardAttackHandle attack) =>
        FinalTargetSlashReceipts(attack.ResolutionId, attack.TargetSeat).Any(item => item.PreventCancellation) ||
        HasCurrentCardEnhancement(attack.ResolutionId, CurrentCardEnhancement.Uncancelable) ||
        GetCardAttackState(attack.ResolutionId).JudgmentSuitDodgeRestriction?.TargetSeat == attack.TargetSeat;

    private PromptChoice SelectAiSlashSuitDiscard(PendingDecision decision, ProgramSkillFrame frame) =>
        decision.Choices.OrderBy(choice => choice.Cards.Count == 0 ? double.MaxValue :
            GetKeepValue(SlashSuitDiscardCards(frame.SlashSuitDiscard!).Single(card => card.Id == choice.Cards[0]),
                _players[frame.SlashSuitDiscard!.TargetSeat])).First();

    private void AssertProgramSlashSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.SlashSuitDiscard is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard ||
            paused.JudgmentReason != draft.JudgmentReason || paused.ResultBind != draft.ResultBind ||
            !IsValidPlayerSeat(draft.TargetSeat) || draft.TargetSeat == frame.OwnerSeat ||
            draft.Stage == ProgramSlashSuitDiscardStage.PaymentMovement && (draft.PaidCardId is null || draft.PaidFrom is null))
            throw new InvalidOperationException("Suit discard lost its typed cursor.");
        _ = SlashSuitDiscardAttack(frame, requireActive: false);
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.Stage == ProgramSlashSuitDiscardStage.Choosing &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != draft.TargetSeat ||
             !decision.IsPrivate || !decision.Choices.Select(choice => choice.Id).SequenceEqual(SlashSuitDiscardChoices(frame).Select(choice => choice.Id))))
            throw new InvalidOperationException("Suit discard lost its exact target-private HE choices.");
    }
}
