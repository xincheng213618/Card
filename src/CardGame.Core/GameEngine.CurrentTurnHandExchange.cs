using System.Collections.ObjectModel;
using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int CurrentTurnHandExchangeSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string CurrentTurnHandExchangeReason(ProgramSkillFrame frame, string part) =>
        $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.OfferCurrentTurnHandExchange}.{part}";
    private static CardConversionSource CurrentTurnHandExchangeSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    private TurnEndingBoundaryFrame RequireCurrentTurnHandExchangeParent(ProgramSkillFrame frame)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, Facts: { } facts } context ||
            frame.TriggerId is null || frame.ActivationId != frame.TriggerId || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 || context.OwnerSeat != frame.OwnerSeat ||
            index < 1 || _resolutionStack[index - 1] is not TurnEndingBoundaryFrame parent || parent.Id != context.ParentFrameId ||
            parent.OwnerSeat != _currentSeat || parent.TurnNumber != _turnNumber ||
            context.SourceSeat != parent.OwnerSeat || context.TargetSeat != parent.OwnerSeat ||
            parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } ||
            !MountObserverCandidateMatches(frame, candidate) || context.OccurrenceIndex != candidate.OccurrenceIndex ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("Current-turn hand exchange lost its exact TurnEnding parent, candidate, instruction or source.");
        var trigger = GetProgramTrigger(frame);
        CurrentTurnHandExchangeContract.ValidateTrigger(frame.SkillId, trigger.Effects, trigger.Window, trigger.Subject,
            trigger.TurnOwnerScope, trigger.Optional, trigger.Condition);
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.OfferCurrentTurnHandExchange }] ||
            (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own) != (parent.OwnerSeat == frame.OwnerSeat) ||
            facts != (parent.Items[parent.ItemIndex].Facts ?? parent.Facts) || facts.EventTargetHandCount > 1 ||
            !trigger.Condition.Evaluate(facts, frame.SkillId, frame.SkillInstanceId) ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.BindingId == frame.TriggerId &&
                fact.SkillInstanceId == frame.SkillInstanceId && fact.OwnerSeat == frame.OwnerSeat && fact.Window == context.Window) != 1)
            throw new InvalidOperationException("Current-turn hand exchange lost its frozen low-hand eligibility or original binding.");
        return parent;
    }

    private SkillProgramStepOutcome BeginCurrentTurnHandExchange(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var parent = RequireCurrentTurnHandExchangeParent(frame);
        if (frame.CurrentTurnHandExchange is not null || frame.PendingMovementContinuation is not null ||
            !_players[frame.OwnerSeat].IsAlive || !_players[parent.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) || _winner != Winner.None)
            throw new InvalidOperationException("Current-turn hand exchange cannot offer twice or replace its living original skill instance.");
        var receipt = new ProgramCurrentTurnHandExchangeReceipt(1, CurrentTurnHandExchangeSource(frame), frame.GameplayHash,
            parent.Id, parent.TurnNumber, parent.OwnerSeat, CurrentTurnHandExchangeStage.Offered);
        ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt });
        AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeOfferedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            receipt.ParentFrameId, receipt.ActualTurnNumber, receipt.ParticipantSeat));
        PublishCurrentTurnHandExchangeChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private Card[] CurrentTurnHandExchangeReturnCards(ProgramSkillFrame frame) =>
        GetHand(_players[frame.OwnerSeat]).Concat(GetEquipment(frame.OwnerSeat))
            .Where(card => !(card.IsGeneralWeapon && _cardZones.GetLocation(card.Id).Zone == CardZoneKind.Equipment) &&
                !IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Transfer))
            .OrderBy(card => card.Id).ToArray();

    private IReadOnlyList<PromptChoice> CurrentTurnHandExchangeChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.CurrentTurnHandExchange ?? throw new InvalidOperationException("The exchange choice lost its owning receipt.");
        var choices = new List<PromptChoice>();
        void Add(string option, string description, IReadOnlyList<int> cards)
        {
            choices.Add(new(new($"current-turn-hand-exchange.{frame.Id}.{receipt.Stage}.{option}"), description,
                Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(new[] { receipt.ParticipantSeat }),
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    ["program-action"] = "current-turn-hand-exchange",
                    ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option
                })));
        }
        if (receipt.Stage == CurrentTurnHandExchangeStage.Offered)
        {
            Add("accept", "摸一张牌，然后按原赠牌数量交换。", []);
            Add("decline", "不发动。", []);
        }
        else if (receipt.Stage == CurrentTurnHandExchangeStage.SelectingReturn)
        {
            foreach (var id in receipt.CandidateCardIds.Where(id => !receipt.SelectedCardIds.Contains(id)))
                Add($"card:{id}", $"交回【{GetAdvancedCard(id).DisplayName}】（{receipt.SelectedCardIds.Count + 1}/{receipt.RequiredReturnCount}）", [id]);
        }
        else throw new InvalidOperationException("A movement child cannot expose an exchange choice.");
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishCurrentTurnHandExchangeChoice(ProgramSkillFrame frame)
    {
        var receipt = frame.CurrentTurnHandExchange!;
        var chooser = receipt.Stage == CurrentTurnHandExchangeStage.Offered ? receipt.ParticipantSeat : frame.OwnerSeat;
        var choices = CurrentTurnHandExchangeChoices(frame);
        if (choices.Count == 0) throw new InvalidOperationException("An unfinished exchange must publish a payable exact choice.");
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            receipt.Stage == CurrentTurnHandExchangeStage.Offered ? "是否摸一张牌并交换手牌？" : $"向原回合角色交回 {receipt.RequiredReturnCount} 张手牌或装备。",
            Array.AsReadOnly(choices.SelectMany(choice => choice.Cards).Distinct().ToArray()),
            Array.AsReadOnly(new[] { receipt.ParticipantSeat }), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = receipt.ParticipantSeat, Choices = choices,
            RequiredCardCount = receipt.Stage == CurrentTurnHandExchangeStage.SelectingReturn ? receipt.RequiredReturnCount - receipt.SelectedCardIds.Count : 0,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveCurrentTurnHandExchangeChoice(ProgramSkillFrame frame, PromptChoice supplied)
    {
        AssertCurrentTurnHandExchange(frame);
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.CurrentTurnHandExchange is not { } receipt ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != (receipt.Stage == CurrentTurnHandExchangeStage.Offered ? receipt.ParticipantSeat : frame.OwnerSeat) ||
            supplied.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) ||
            supplied.Parameters.GetValueOrDefault("program-action") != "current-turn-hand-exchange")
            throw new InvalidOperationException("The exchange answer lost its exact actor, prompt, owning frame or stage.");
        var choice = CurrentTurnHandExchangeChoices(frame).SingleOrDefault(item => item.Id == supplied.Id) ??
            throw new InvalidOperationException("The exchange answer is not an exact current choice.");
        if (receipt.Stage == CurrentTurnHandExchangeStage.Offered)
        {
            ClearPendingDecision();
            if (choice.Parameters["option"] == "decline") { CompleteCurrentTurnHandExchange(frame, "declined"); return; }
            if (choice.Parameters["option"] != "accept" || _winner != Winner.None || !_players[frame.OwnerSeat].IsAlive ||
                !_players[receipt.ParticipantSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
                throw new InvalidOperationException("Only the original living participant may accept an unpaid exchange.");
            var before = CurrentTurnHandExchangeSequence;
            receipt = receipt with { Stage = CurrentTurnHandExchangeStage.DrawChildren, DrawBefore = before, DrawAfter = before };
            ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            var drawn = DrawCards(_players[receipt.ParticipantSeat], 1, true, new(CurrentTurnHandExchangeReason(frame, "draw")));
            var after = CurrentTurnHandExchangeSequence;
            ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with
                { CurrentTurnHandExchange = receipt with { ActualDrawCount = drawn.Count, DrawAfter = after } });
            AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeDrawIssuedEvent(frame.Id, receipt.ParticipantSeat, drawn.Count, before, after));
            DrainCurrentTurnHandExchangeMovement(frame.Id);
            return;
        }
        if (receipt.Stage != CurrentTurnHandExchangeStage.SelectingReturn || choice.Cards is not [var selectedId] ||
            receipt.SelectedCardIds.Contains(selectedId) || receipt.SelectedCardIds.Count >= receipt.RequiredReturnCount ||
            !CurrentTurnHandExchangeReturnCards(frame).Any(card => card.Id == selectedId) ||
            _cardZones.GetLocation(selectedId) != receipt.CandidateLocations[receipt.CandidateCardIds.ToList().IndexOf(selectedId)])
            throw new InvalidOperationException("Exchange return requires distinct original private HE candidates up to its frozen payable count.");
        ClearPendingDecision();
        receipt = receipt with { SelectedCardIds = receipt.SelectedCardIds.Append(selectedId).ToArray() };
        ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt });
        if (receipt.SelectedCardIds.Count < receipt.RequiredReturnCount) { PublishCurrentTurnHandExchangeChoice(frame); return; }
        IssueCurrentTurnHandExchangeReturn(frame);
    }

    private bool TryDrainCurrentTurnHandExchangeMovement(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);
    private void DrainCurrentTurnHandExchangeMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (!TryDrainCurrentTurnHandExchangeMovement(frame)) ReturnCurrentTurnHandExchangeMovement(frame);
    }
    private bool ReturnCurrentTurnHandExchangeMovement(ProgramSkillFrame frame)
    {
        if (frame.CurrentTurnHandExchange is null) return false;
        AssertCurrentTurnHandExchange(frame);
        if (frame.PendingMovementContinuation is not { } pending ||
            !IsCurrentTurnHandExchangeMovement(frame, GetPausedPrivateOfferEffect(frame), pending))
            throw new InvalidOperationException("Exchange movement returned to a different instruction or unpaid stage.");
        if (TryDrainCurrentTurnHandExchangeMovement(frame)) return true;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        ResumeCurrentTurnHandExchange(frame.Id);
        return true;
    }

    private bool ResumeCurrentTurnHandExchange(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.CurrentTurnHandExchange is not { } receipt)
            return false;
        AssertCurrentTurnHandExchange(frame);
        if (frame.PendingMovementContinuation is not null) { DrainCurrentTurnHandExchangeMovement(frameId); return true; }
        if (receipt.Stage is CurrentTurnHandExchangeStage.Offered or CurrentTurnHandExchangeStage.SelectingReturn) return true;
        if (receipt.Stage == CurrentTurnHandExchangeStage.ReturnChildren)
        { CompleteCurrentTurnHandExchange(frame, "returned"); return true; }
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[receipt.ParticipantSeat].IsAlive)
        { CompleteCurrentTurnHandExchange(frame, "participant-unavailable"); return true; }
        if (receipt.Stage == CurrentTurnHandExchangeStage.DrawChildren)
        {
            if (receipt.ParticipantSeat == frame.OwnerSeat) { CompleteCurrentTurnHandExchange(frame, "self-draw"); return true; }
            if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            { CompleteCurrentTurnHandExchange(frame, "source-unavailable-before-gift"); return true; }
            var cards = GetHand(_players[receipt.ParticipantSeat]).Select(card => card.Id).ToArray();
            var before = CurrentTurnHandExchangeSequence;
            receipt = receipt with { Stage = CurrentTurnHandExchangeStage.GiftChildren, GivenCardIds = cards,
                FrozenGivenCount = cards.Length, GiftBefore = before, GiftAfter = before };
            ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            void Commit(int after)
            {
                ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { CurrentTurnHandExchange = receipt with { GiftAfter = after } });
                AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeGiftIssuedEvent(frame.Id, receipt.ParticipantSeat,
                    frame.OwnerSeat, receipt.FrozenGivenCount, before, after));
            }
            if (cards.Length == 0) Commit(before);
            else MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(frame.OwnerSeat), new(CurrentTurnHandExchangeReason(frame, "gift")),
                (_, records) => Commit(records.Max(movement => movement.Sequence)));
            DrainCurrentTurnHandExchangeMovement(frame.Id);
            return true;
        }
        var candidates = CurrentTurnHandExchangeReturnCards(frame);
        var required = Math.Min(receipt.FrozenGivenCount, candidates.Length);
        receipt = receipt with { Stage = CurrentTurnHandExchangeStage.SelectingReturn, RequiredReturnCount = required,
            CandidateCardIds = candidates.Select(card => card.Id).ToArray(), CandidateLocations = candidates.Select(card => _cardZones.GetLocation(card.Id)).ToArray() };
        ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt });
        AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeReturnStartedEvent(frame.Id, receipt.FrozenGivenCount, candidates.Length, required));
        if (required == 0) IssueCurrentTurnHandExchangeReturn(frame);
        else PublishCurrentTurnHandExchangeChoice(frame);
        return true;
    }

    private void IssueCurrentTurnHandExchangeReturn(ProgramSkillFrame frame)
    {
        var receipt = frame.CurrentTurnHandExchange!;
        if (receipt.Stage != CurrentTurnHandExchangeStage.SelectingReturn || receipt.SelectedCardIds.Count != receipt.RequiredReturnCount ||
            receipt.SelectedCardIds.Any(id => !CurrentTurnHandExchangeReturnCards(frame).Any(card => card.Id == id)))
            throw new InvalidOperationException("Exchange return cannot pay a smaller unissued count or replace its chosen entities.");
        var before = CurrentTurnHandExchangeSequence;
        receipt = receipt with { Stage = CurrentTurnHandExchangeStage.ReturnChildren, ReturnBefore = before, ReturnAfter = before };
        ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        void Commit(int after)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { CurrentTurnHandExchange = receipt with { ReturnAfter = after } });
            AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeReturnIssuedEvent(frame.Id, frame.OwnerSeat, receipt.ParticipantSeat,
                receipt.FrozenGivenCount, receipt.RequiredReturnCount, before, after));
        }
        if (receipt.RequiredReturnCount == 0) Commit(before);
        else MoveProgramCardsFromMultipleSources(receipt.SelectedCardIds, CardLocation.Hand(receipt.ParticipantSeat),
            new(CurrentTurnHandExchangeReason(frame, "return")), (_, records) => Commit(records.Max(movement => movement.Sequence)));
        DrainCurrentTurnHandExchangeMovement(frame.Id);
    }

    private void CompleteCurrentTurnHandExchange(ProgramSkillFrame frame, string outcome)
    {
        if (frame.PendingMovementContinuation is not null) throw new InvalidOperationException("Exchange cannot finish before its native movement children return.");
        var receipt = frame.CurrentTurnHandExchange!;
        AdvanceEventRulesAndQueueFact(new CurrentTurnHandExchangeResolvedEvent(frame.Id, outcome, receipt.FrozenGivenCount,
            receipt.Stage == CurrentTurnHandExchangeStage.ReturnChildren ? receipt.RequiredReturnCount : 0));
        ReplaceRuntimeTop(frame = frame with { CurrentTurnHandExchange = null });
        FinishProgramSkill(frame, true);
    }

    private PromptChoice SelectAiCurrentTurnHandExchange(PendingDecision decision, ProgramSkillFrame frame)
    {
        AssertCurrentTurnHandExchange(frame);
        var receipt = frame.CurrentTurnHandExchange!;
        if (receipt.Stage == CurrentTurnHandExchangeStage.Offered)
            return decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option") == "accept");
        // These are the chooser's own published entities; no foreign hidden hand is used for scoring.
        return decision.Choices.OrderBy(choice => GetAdvancedCard(choice.Cards.Single()).Kind == CardKind.Peach ? 1 : 0)
            .ThenBy(choice => choice.Cards.Single()).First();
    }

    private bool IsCurrentTurnHandExchangeMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.OfferCurrentTurnHandExchange && frame.CurrentTurnHandExchange?.Stage is
            CurrentTurnHandExchangeStage.DrawChildren or CurrentTurnHandExchangeStage.GiftChildren or CurrentTurnHandExchangeStage.ReturnChildren &&
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        ValidCurrentTurnHandExchangeReceipt(frame);

    private bool ValidCurrentTurnHandExchangeReceipt(ProgramSkillFrame frame)
    {
        if (frame.CurrentTurnHandExchange is not { } receipt) return true;
        var parent = RequireCurrentTurnHandExchangeParent(frame);
        if (receipt.InstructionIndex != frame.InstructionIndex || receipt.Source != CurrentTurnHandExchangeSource(frame) ||
            receipt.GameplayHash != frame.GameplayHash || receipt.ParentFrameId != parent.Id || receipt.ActualTurnNumber != parent.TurnNumber ||
            receipt.ParticipantSeat != parent.OwnerSeat || !Enum.IsDefined(receipt.Stage) ||
            receipt.ActualDrawCount is < 0 or > 1 || receipt.DrawBefore < 0 || receipt.DrawAfter < receipt.DrawBefore ||
            receipt.DrawAfter > CurrentTurnHandExchangeSequence || receipt.FrozenGivenCount != receipt.GivenCardIds.Count ||
            receipt.GivenCardIds.Distinct().Count() != receipt.GivenCardIds.Count ||
            receipt.CandidateCardIds.Count != receipt.CandidateLocations.Count ||
            receipt.CandidateCardIds.Distinct().Count() != receipt.CandidateCardIds.Count ||
            receipt.CandidateLocations.Any(location => location.OwnerSeat != frame.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            receipt.SelectedCardIds.Distinct().Count() != receipt.SelectedCardIds.Count ||
            receipt.SelectedCardIds.Any(id => !receipt.CandidateCardIds.Contains(id)) ||
            receipt.RequiredReturnCount < 0 || receipt.RequiredReturnCount > receipt.FrozenGivenCount ||
            receipt.RequiredReturnCount > receipt.CandidateCardIds.Count || receipt.SelectedCardIds.Count > receipt.RequiredReturnCount)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<CurrentTurnHandExchangeOfferedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray() is not [var offered] ||
            offered != new CurrentTurnHandExchangeOfferedEvent(frame.Id, receipt.Source, receipt.GameplayHash, parent.Id, parent.TurnNumber, parent.OwnerSeat) ||
            history.OfType<CurrentTurnHandExchangeResolvedEvent>().Any(fact => fact.FrameId == frame.Id)) return false;
        bool Invoice(int before, int after, string part, int count, Func<CardMovementRecord, bool> matches) =>
            before >= 0 && after >= before && after <= CurrentTurnHandExchangeSequence &&
            _cardMovements.Count(movement => movement.Sequence > before && movement.Sequence <= after &&
                movement.Reason.Value == CurrentTurnHandExchangeReason(frame, part)) == count &&
            _cardMovements.Where(movement => movement.Sequence > before && movement.Sequence <= after &&
                movement.Reason.Value == CurrentTurnHandExchangeReason(frame, part)).All(matches) &&
            (count > 0 || before == after);
        var draws = history.OfType<CurrentTurnHandExchangeDrawIssuedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        var gifts = history.OfType<CurrentTurnHandExchangeGiftIssuedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        var starts = history.OfType<CurrentTurnHandExchangeReturnStartedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        var returns = history.OfType<CurrentTurnHandExchangeReturnIssuedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        if (receipt.Stage == CurrentTurnHandExchangeStage.Offered)
            return draws.Length == 0 && gifts.Length == 0 && starts.Length == 0 && returns.Length == 0 && receipt.GivenCardIds.Count == 0 &&
                receipt.CandidateCardIds.Count == 0 && receipt.SelectedCardIds.Count == 0 && frame.PendingMovementContinuation is null;
        if (draws is not [var draw] || draw != new CurrentTurnHandExchangeDrawIssuedEvent(frame.Id, receipt.ParticipantSeat,
                receipt.ActualDrawCount, receipt.DrawBefore, receipt.DrawAfter) ||
            !Invoice(receipt.DrawBefore, receipt.DrawAfter, "draw", receipt.ActualDrawCount,
                movement => movement.From == CardLocation.DrawPile && movement.To == CardLocation.Hand(receipt.ParticipantSeat))) return false;
        if (receipt.Stage == CurrentTurnHandExchangeStage.DrawChildren)
            return gifts.Length == 0 && starts.Length == 0 && returns.Length == 0 && receipt.FrozenGivenCount == 0 && receipt.CandidateCardIds.Count == 0;
        if (receipt.ParticipantSeat == frame.OwnerSeat || gifts is not [var gift] ||
            gift != new CurrentTurnHandExchangeGiftIssuedEvent(frame.Id, receipt.ParticipantSeat, frame.OwnerSeat,
                receipt.FrozenGivenCount, receipt.GiftBefore, receipt.GiftAfter) || receipt.GiftBefore < receipt.DrawAfter ||
            !Invoice(receipt.GiftBefore, receipt.GiftAfter, "gift", receipt.FrozenGivenCount, movement =>
                receipt.GivenCardIds.Contains(movement.CardId) && movement.From == CardLocation.Hand(receipt.ParticipantSeat) && movement.To == CardLocation.Hand(frame.OwnerSeat)) ||
            receipt.GivenCardIds.Any(id => _cardMovements.Count(movement => movement.Sequence > receipt.GiftBefore &&
                movement.Sequence <= receipt.GiftAfter && movement.CardId == id && movement.Reason.Value == CurrentTurnHandExchangeReason(frame, "gift")) != 1)) return false;
        if (receipt.Stage == CurrentTurnHandExchangeStage.GiftChildren)
            return starts.Length == 0 && returns.Length == 0 && receipt.RequiredReturnCount == 0 && receipt.CandidateCardIds.Count == 0;
        if (starts is not [var start] || start != new CurrentTurnHandExchangeReturnStartedEvent(frame.Id, receipt.FrozenGivenCount,
                receipt.CandidateCardIds.Count, receipt.RequiredReturnCount) ||
            receipt.RequiredReturnCount != Math.Min(receipt.FrozenGivenCount, receipt.CandidateCardIds.Count)) return false;
        if (receipt.Stage == CurrentTurnHandExchangeStage.SelectingReturn)
            return returns.Length == 0 && frame.PendingMovementContinuation is null && receipt.CandidateCardIds.Select((id, index) => (id, index)).All(item =>
                _cardZones.GetLocation(item.id) == receipt.CandidateLocations[item.index] && CurrentTurnHandExchangeReturnCards(frame).Any(card => card.Id == item.id));
        return returns is [var returned] && returned == new CurrentTurnHandExchangeReturnIssuedEvent(frame.Id, frame.OwnerSeat,
            receipt.ParticipantSeat, receipt.FrozenGivenCount, receipt.RequiredReturnCount, receipt.ReturnBefore, receipt.ReturnAfter) &&
            receipt.SelectedCardIds.Count == receipt.RequiredReturnCount && receipt.ReturnBefore >= receipt.GiftAfter &&
            Invoice(receipt.ReturnBefore, receipt.ReturnAfter, "return", receipt.RequiredReturnCount, movement =>
                receipt.SelectedCardIds.Contains(movement.CardId) && movement.To == CardLocation.Hand(receipt.ParticipantSeat) &&
                movement.From == receipt.CandidateLocations[receipt.CandidateCardIds.ToList().IndexOf(movement.CardId)]) &&
            receipt.SelectedCardIds.All(id => _cardMovements.Count(movement => movement.Sequence > receipt.ReturnBefore &&
                movement.Sequence <= receipt.ReturnAfter && movement.CardId == id && movement.Reason.Value == CurrentTurnHandExchangeReason(frame, "return")) == 1);
    }

    private void AssertCurrentTurnHandExchange(ProgramSkillFrame frame)
    {
        if (frame.CurrentTurnHandExchange is not null && !ValidCurrentTurnHandExchangeReceipt(frame))
            throw new InvalidOperationException("Current-turn hand exchange lost its immutable identity, actual native payment, frozen gift quantity or exact return selection.");
        if (frame.CurrentTurnHandExchange is not { } receipt) return;
        if (receipt.Stage is CurrentTurnHandExchangeStage.Offered or CurrentTurnHandExchangeStage.SelectingReturn)
        {
            var chooser = receipt.Stage == CurrentTurnHandExchangeStage.Offered ? receipt.ParticipantSeat : frame.OwnerSeat;
            var expected = CurrentTurnHandExchangeChoices(frame);
            if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.PendingMovementContinuation is not null ||
                _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                prompt.PlayerSeat != chooser || prompt.SourceSeat != frame.OwnerSeat || prompt.TargetSeat != receipt.ParticipantSeat ||
                prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.Choices.Count != expected.Count ||
                !prompt.ValidCardIds.SequenceEqual(expected.SelectMany(choice => choice.Cards).Distinct()) ||
                !prompt.ValidTargetSeats.SequenceEqual([receipt.ParticipantSeat]) ||
                prompt.Choices.Zip(expected).Any(pair => pair.First.Id != pair.Second.Id ||
                    !pair.First.Cards.SequenceEqual(pair.Second.Cards) || !pair.First.Targets.SequenceEqual(pair.Second.Targets) ||
                    pair.First.Parameters.Count != pair.Second.Parameters.Count ||
                    pair.First.Parameters.Any(parameter => pair.Second.Parameters.GetValueOrDefault(parameter.Key) != parameter.Value)))
                throw new InvalidOperationException("Current-turn hand exchange lost its exact private chooser and prepared choices.");
        }
        else
        {
            var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
            if (frame.PendingMovementContinuation is { } pending)
            {
                if (!IsCurrentTurnHandExchangeMovement(frame, GetPausedPrivateOfferEffect(frame), pending))
                    throw new InvalidOperationException("Current-turn hand exchange lost its pending native movement return.");
            }
            else if (index != _resolutionStack.Count - 1 || _pendingDecision is not null)
                throw new InvalidOperationException("A returned exchange instruction cannot retain an unfinished child or prompt.");
            if (index + 1 < _resolutionStack.Count && !CurrentTurnHandExchangeFirstChild(frame, _resolutionStack[index + 1]))
                throw new InvalidOperationException("Current-turn hand exchange retained an unrelated movement child.");
        }
    }

    private bool CurrentTurnHandExchangeFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        var receipt = frame.CurrentTurnHandExchange!;
        var part = receipt.Stage == CurrentTurnHandExchangeStage.DrawChildren ? "draw" :
            receipt.Stage == CurrentTurnHandExchangeStage.GiftChildren ? "gift" : "return";
        var before = part == "draw" ? receipt.DrawBefore : part == "gift" ? receipt.GiftBefore : receipt.ReturnBefore;
        var after = part == "draw" ? receipt.DrawAfter : part == "gift" ? receipt.GiftAfter : receipt.ReturnAfter;
        var reason = CurrentTurnHandExchangeReason(frame, part);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == frame.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.AwaitingProgramFrameId == frame.Id && moved.Batch.OriginOwnerSeat == frame.OwnerSeat &&
                moved.Batch.OriginSkillId == frame.SkillId && moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(movement => _cardMovements.Contains(movement) &&
                    (movement.Sequence > before && movement.Sequence <= after && movement.Reason.Value == reason ||
                     part == "return" && movement.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                     movement.From == CardLocation.WoodenOxGrain(frame.OwnerSeat) && movement.To == CardLocation.DiscardPile &&
                     _cardMovements.Any(cost => cost.Sequence > before && cost.Sequence <= after &&
                         receipt.SelectedCardIds.Contains(cost.CardId) && cost.CardKind == CardKind.WoodenOx &&
                         cost.From == CardLocation.Equipment(frame.OwnerSeat) && cost.Reason.Value == reason)));
        var lion = part == "return" && _cardMovements.Any(movement => movement.Sequence > before && movement.Sequence <= after &&
            receipt.SelectedCardIds.Contains(movement.CardId) && movement.CardKind == CardKind.SilverLion &&
            movement.From == CardLocation.Equipment(frame.OwnerSeat) && movement.Reason.Value == reason);
        if (!lion) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == frame.Id && hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == frame.OwnerSeat && hp.Change.TargetSeat == frame.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame replacement && RecoveryReplacementFrameRidesOn(replacement, frame) &&
            replacement.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replacement.Attempt.Completion.MoveReason?.Value == reason && replacement.Attempt.SourceSeat == frame.OwnerSeat &&
            replacement.Attempt.TargetSeat == frame.OwnerSeat && replacement.Attempt.Amount == 1;
    }

    private sealed partial class ProgramSkillHost : ICurrentTurnHandExchangeHost
    {
        public SkillProgramStepOutcome OfferCurrentTurnHandExchange(ProgramSkillFrame frame) => engine.BeginCurrentTurnHandExchange(frame);
    }
}
