using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PrepDiscardTargetReason = "skill-program.preparation-discard.target.ResolvePrepDiscardOrEnding";
    private const string PrepDiscardOwnerReason = "skill-program.preparation-discard.owner.ResolvePrepDiscardOrEnding";
    private const string PrepDiscardDrawReason = "program.preparation-discard.ending-draw";
    private long PrepDiscardSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private CardConversionSource PrepDiscardSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool PrepDiscardCanContinue(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private IReadOnlyList<PromptChoice> PrepDiscardCards(ProgramSkillFrame f, int payer, IReadOnlyList<int>? selected = null) =>
        BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, payer, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
            canSelect: (_, c) => !c.IsGeneralWeapon && !(selected ?? []).Contains(c.Id));
    private bool MatchesActualPrepDiscard(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            f.OwnerSeat != _currentSeat || context.OwnerSeat != f.OwnerSeat || context.SourceSeat != f.OwnerSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Continuation: ProgramLifecycleContinuation.NormalTurnStart } window ||
            window.OwnerSeat != f.OwnerSeat || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex])) return false;
        return GetProgramTrigger(f).Effects is [{ Op: SkillProgramEffectOp.ResolvePrepDiscardOrEnding }];
    }
    private bool CanRunPrepDiscard(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePrepDiscardOrEnding) ||
        c.OwnerSeat == _currentSeat && context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
        _players[c.OwnerSeat].IsAlive && _players.Any(p => p.IsAlive &&
            new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Any(z => _cardZones.CardsAt(new(z, p.Seat)).Any(card =>
                !card.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(c.OwnerSeat, card, new(z, p.Seat), OwnedCardMoveIntent.Discard) &&
                !(z == CardZoneKind.Equipment && p.Seat == c.OwnerSeat && IsActiveProgramSourceEquipmentCard(p.Seat, c.SkillId, c.SkillInstanceId, card)))));

    private SkillProgramStepOutcome ResolvePrepDiscardOrEnding(ProgramSkillFrame supplied, string endingBinding)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MatchesActualPrepDiscard(f) || f.InstructionIndex != 1)
            throw new InvalidOperationException("Preparation discard requires its exact original Prep candidate.");
        if (f.PrepDiscard is null)
            ReplaceRuntimeTop(f = f with { PrepDiscard = new(0, PrepDiscardSource(f), f.GameplayHash, _turnNumber, _currentSeat,
                f.WindowContext!.ParentFrameId, PrepDiscardStage.ChoosingTarget, null, 0, 0, 0, 0, [], []), ReexecuteParticipantInstruction = true });
        else ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = true });
        if (!ValidPrepDiscard(f)) throw new InvalidOperationException("Preparation discard lost its original turn, target or paid ledger.");
        var d = f.PrepDiscard!;
        if (d.Stage is PrepDiscardStage.TargetChildren or PrepDiscardStage.OwnerChildren)
        {
            if (f.PendingMovementContinuation is not null) throw new InvalidOperationException("Preparation discard returned before its paid children.");
            if (d.Stage == PrepDiscardStage.OwnerChildren) return FinishPrepDiscard(f, true);
            ReplaceRuntimeTop(f = f with { PrepDiscard = d with { Stage = PrepDiscardStage.ChoosingBenefit } });
        }
        if (!PrepDiscardCanContinue(f) || f.PrepDiscard!.TargetSeat is { } t && !_players[t].IsAlive)
            return FinishPrepDiscard(f, false);
        return PublishPrepDiscard(f);
    }
    private SkillProgramStepOutcome FinishPrepDiscard(ProgramSkillFrame f, bool completed)
    {
        ReplaceRuntimeTop(f with { PrepDiscard = f.PrepDiscard! with { Stage = PrepDiscardStage.Complete }, ReexecuteParticipantInstruction = false });
        AdvanceEventRulesAndQueueFact(new PrepDiscardFinishedEvent(f.Id, completed));
        return SkillProgramStepOutcome.Continue;
    }
    private Dictionary<string,string> PrepDiscardParameters(ProgramSkillFrame f, string step) => new()
    { ["program-action"] = "prep-discard-ending", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["prep-discard-step"] = step };
    private IReadOnlyList<PromptChoice> PrepDiscardChoices(ProgramSkillFrame f)
    {
        var d = f.PrepDiscard!;
        if (d.Stage == PrepDiscardStage.ChoosingTarget)
            return Array.AsReadOnly(_players.Where(p => p.IsAlive && PrepDiscardCards(f, p.Seat).Count > 0).Select(p =>
                new PromptChoice(new($"prep-discard.{f.Id}.target-{p.Seat}"), $"弃置 {p.Name} 的牌", [], [p.Seat], PrepDiscardParameters(f, "target"))).ToArray());
        if (d.Stage == PrepDiscardStage.ChoosingBenefit)
        {
            var choices = new List<PromptChoice>(); var n = d.TargetPayment!.NonEquipmentCount;
            if (PrepDiscardCards(f, f.OwnerSeat).Count >= n)
                choices.Add(new(new($"prep-discard.{f.Id}.pay"), $"本人弃置 {n} 张牌", [], [], PrepDiscardParameters(f, "pay")));
            choices.Add(new(new($"prep-discard.{f.Id}.ending"), $"本回合结束阶段原目标摸 {n} 张牌", [], [], PrepDiscardParameters(f, "ending")));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (d.Stage is not (PrepDiscardStage.SelectingTargetCards or PrepDiscardStage.SelectingOwnerCards)) return [];
        var payer = d.Stage == PrepDiscardStage.SelectingOwnerCards ? f.OwnerSeat : d.TargetSeat!.Value;
        if (d.SelectedCardIds.Count == d.RequiredCount)
            return [new(new($"prep-discard.{f.Id}.finish-selection"), "确认实际弃置", [], [], PrepDiscardParameters(f, "finish"))];
        return Array.AsReadOnly(PrepDiscardCards(f, payer, d.SelectedCardIds).Select(raw =>
        {
            var parameters = new Dictionary<string,string>(raw.Parameters);
            foreach (var pair in PrepDiscardParameters(f, "card")) parameters[pair.Key] = pair.Value;
            return raw with { Parameters = parameters };
        }).ToArray());
    }
    private SkillProgramStepOutcome PublishPrepDiscard(ProgramSkillFrame f)
    {
        var choices = PrepDiscardChoices(f);
        if (choices.Count == 0) return FinishPrepDiscard(f, false);
        var skill = _contentRegistry.GetSkill(f.SkillId); var d = f.PrepDiscard!;
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "请选择镇军的真实目标、弃牌或后续代价。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = d.TargetSeat ?? f.OwnerSeat,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 实际弃牌", skill.Description), Choices = choices };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolvePrepDiscardChoice(ProgramSkillFrame f, PromptChoice choice)
    {
        if (!ValidPrepDiscard(f) || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The preparation discard choice lost its owning frame.");
        var d = f.PrepDiscard!; var step = choice.Parameters.GetValueOrDefault("prep-discard-step");
        if (!PrepDiscardCanContinue(f) || d.TargetSeat is { } gone && !_players[gone].IsAlive)
        { ClearPendingDecision(); FinishPrepDiscard(f, false); AdvanceRuntimeProgram(f.Id); return; }
        if (step == "target" && d.Stage == PrepDiscardStage.ChoosingTarget)
        {
            if (choice.Targets is not [var target] || !_players[target].IsAlive || PrepDiscardCards(f, target).Count == 0)
                throw new InvalidOperationException("The original discard target is unavailable.");
            var hand = GetHand(_players[target]).Count; var hp = _players[target].Hp;
            var requested = Math.Max(1, hand - hp); var required = Math.Min(requested, PrepDiscardCards(f, target).Count);
            ClearPendingDecision(); ReplaceRuntimeTop(f = f with { PrepDiscard = d with { TargetSeat = target, FrozenHandCount = hand,
                FrozenHp = hp, RequestedCount = requested, RequiredCount = required, Stage = PrepDiscardStage.SelectingTargetCards } });
            AdvanceEventRulesAndQueueFact(new PrepDiscardTargetFrozenEvent(f.Id, d.Source, d.GameplayHash, d.ActualTurnNumber,
                d.ActualTurnOwnerSeat, d.PrepWindowId, target, hand, hp, requested, required));
            PublishPrepDiscard(f); return;
        }
        if (d.Stage == PrepDiscardStage.ChoosingBenefit && step is "pay" or "ending")
        {
            var n = d.TargetPayment!.NonEquipmentCount;
            if (step == "pay" && PrepDiscardCards(f, f.OwnerSeat).Count < n) throw new InvalidOperationException("The owner cannot pay the frozen true cost.");
            ClearPendingDecision(); ReplaceRuntimeTop(f = f with { PrepDiscard = d with { Deferred = step == "ending" } });
            AdvanceEventRulesAndQueueFact(new PrepDiscardBenefitChosenEvent(f.Id, step == "ending", d.TargetSeat!.Value, n));
            if (step == "ending")
            {
                IssuePrepDiscardEnding(f, GetProgramTrigger(f).Effects[0].StateId!);
                FinishPrepDiscard(f, true); AdvanceRuntimeProgram(f.Id); return;
            }
            if (n == 0) { FinishPrepDiscard(f, true); AdvanceRuntimeProgram(f.Id); return; }
            ReplaceRuntimeTop(f = f with { PrepDiscard = f.PrepDiscard! with { Stage = PrepDiscardStage.SelectingOwnerCards,
                RequiredCount = n, SelectedCardIds = [], SelectedFrom = [] } }); PublishPrepDiscard(f); return;
        }
        if (d.Stage is not (PrepDiscardStage.SelectingTargetCards or PrepDiscardStage.SelectingOwnerCards))
            throw new InvalidOperationException("The preparation discard is not selecting actual cost cards.");
        var payer = d.Stage == PrepDiscardStage.SelectingOwnerCards ? f.OwnerSeat : d.TargetSeat!.Value;
        if (step == "card")
        {
            if (!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot))
                throw new InvalidOperationException("Invalid opaque discard slot.");
            var cards = _cardZones.CardsAt(new(zone, payer));
            if (slot < 0 || slot >= cards.Count || !PrepDiscardCards(f, payer, d.SelectedCardIds).Any(raw => raw.Id == choice.Id) ||
                d.SelectedCardIds.Count >= d.RequiredCount)
                throw new InvalidOperationException("The selected actual discard entity is unavailable.");
            var id = cards[slot].Id; ClearPendingDecision();
            ReplaceRuntimeTop(f = f with { PrepDiscard = d with { SelectedCardIds = d.SelectedCardIds.Append(id).ToArray(),
                SelectedFrom = d.SelectedFrom.Append(new CardLocation(zone, payer)).ToArray() } }); PublishPrepDiscard(f); return;
        }
        if (step != "finish" || d.SelectedCardIds.Count != d.RequiredCount || d.RequiredCount <= 0 ||
            d.SelectedCardIds.Where((id, index) => _cardZones.GetLocation(id) != d.SelectedFrom[index]).Any())
            throw new InvalidOperationException("The preparation discard has not selected its exact frozen real count.");
        ClearPendingDecision(); PayPrepDiscard(f, payer, d.Stage == PrepDiscardStage.SelectingOwnerCards);
    }
    private void PayPrepDiscard(ProgramSkillFrame f, int payer, bool ownerCost)
    {
        var d = f.PrepDiscard!; var payment = new PrepDiscardPayment(payer, d.SelectedCardIds, d.SelectedFrom, PrepDiscardSequence, PrepDiscardSequence, 0, 0);
        ReplaceRuntimeTop(f with { PrepDiscard = d with { Stage = ownerCost ? PrepDiscardStage.OwnerChildren : PrepDiscardStage.TargetChildren,
            SelectedCardIds = [], SelectedFrom = [], TargetPayment = ownerCost ? d.TargetPayment : payment,
            OwnerPayment = ownerCost ? payment : d.OwnerPayment }, PendingMovementContinuation = new(payer, 0, null), ReexecuteParticipantInstruction = true });
        var reason = ownerCost ? PrepDiscardOwnerReason : PrepDiscardTargetReason;
        MoveProgramCardsFromMultipleSources(payment.CardIds, CardLocation.DiscardPile, new(reason), (_, records) =>
        {
            var paidRecords = records.Where(m => payment.CardIds.Contains(m.CardId) && m.From.OwnerSeat == payer &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == reason).ToArray();
            var paid = payment with { SequenceAfter = PrepDiscardSequence, ActualCount = paidRecords.Length,
                NonEquipmentCount = paidRecords.Count(m => !EquipmentCatalog.IsEquipment(m.CardKind)) };
            // Movement can queue SilverLion replacement onto this exact owner.
            // Merge into its fresh frame; never overwrite those queued fields.
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { PrepDiscard = current.PrepDiscard! with {
                TargetPayment = ownerCost ? current.PrepDiscard.TargetPayment : paid,
                OwnerPayment = ownerCost ? paid : current.PrepDiscard.OwnerPayment } });
        });
        // The atomic producer's callback precedes native equipment hooks. Freeze
        // the final interval only after SilverLion queues and WoodenOx cleanup;
        // merge the fresh owner instead of losing queued replacement recovery.
        var finished = GetActiveProgramFrame(f.Id);
        var finalPayment = (ownerCost ? finished.PrepDiscard!.OwnerPayment! : finished.PrepDiscard!.TargetPayment!) with { SequenceAfter = PrepDiscardSequence };
        ReplaceRuntimeTop(finished with { PrepDiscard = finished.PrepDiscard! with {
            TargetPayment = ownerCost ? finished.PrepDiscard.TargetPayment : finalPayment,
            OwnerPayment = ownerCost ? finalPayment : finished.PrepDiscard.OwnerPayment } });
        AdvanceEventRulesAndQueueFact(new PrepDiscardPaidEvent(f.Id, d.Source, d.GameplayHash, d.ActualTurnNumber,
            d.ActualTurnOwnerSeat, payer, ownerCost, finalPayment.SequenceBefore, finalPayment.SequenceAfter, finalPayment.ActualCount, finalPayment.NonEquipmentCount));
        if (!TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) &&
            !TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) && !TryBeginCardsMovedProgramWindow(f.Id))
            ReturnRuntimeProgramMovement(f.Id);
    }
    private bool TryReturnPrepDiscardMovement(ProgramSkillFrame f)
    {
        if (f.PrepDiscard is not { Stage: PrepDiscardStage.TargetChildren or PrepDiscardStage.OwnerChildren } && f.PrepDiscardEndingDraw is null) return false;
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("A preparation payment lost its exact movement return.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); return true;
    }
    private PromptChoice SelectAiPrepDiscard(PendingDecision decision, ProgramSkillFrame f)
    {
        var d = f.PrepDiscard!; var snapshot = CreateSnapshot(decision.PlayerSeat);
        if (d.Stage == PrepDiscardStage.ChoosingTarget)
            return decision.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(snapshot, c.Targets.Single(), new(0,0,0,0,0,1,false,false)) *
                Math.Min(PrepDiscardCards(f, c.Targets.Single()).Count, Math.Max(1, snapshot.Players[c.Targets.Single()].HandCount - snapshot.Players[c.Targets.Single()].Hp)))
                .ThenBy(c => c.Targets.Single()).First();
        if (d.Stage == PrepDiscardStage.ChoosingBenefit)
        {
            var pay = decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("prep-discard-step") == "pay");
            var benefit = _aiBrains[f.OwnerSeat].ScoreProgramTarget(snapshot, d.TargetSeat!.Value, new(0,0,0,1,0,0,false,false));
            return pay is not null && d.TargetPayment!.NonEquipmentCount > 0 && benefit < 0 ? pay :
                decision.Choices.Single(c => c.Parameters.GetValueOrDefault("prep-discard-step") == "ending");
        }
        // Only own exposed identities are scored. Opponent opaque hand slots
        // receive a fixed public prior, without hidden lookup or RNG use.
        return decision.Choices.OrderBy(c => c.Cards.Count == 1 ? GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.OwnerSeat]) : 5d)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    private sealed partial class ProgramSkillHost : IPrepDiscardEndingProgramHost
    {
        public SkillProgramStepOutcome ResolvePrepDiscardOrEnding(ProgramSkillFrame f, string binding) => engine.ResolvePrepDiscardOrEnding(f, binding);
        public SkillProgramStepOutcome DrawPrepDiscardEnding(ProgramSkillFrame f) => engine.DrawPrepDiscardEnding(f);
    }
}
