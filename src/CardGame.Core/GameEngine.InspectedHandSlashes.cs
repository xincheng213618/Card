using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string InspectedHandDiscardReason = "program.hp-hand-inspection.discard";
    private CardConversionSource InspectedHandSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome BeginInspectedHandSlash(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var owner = _players[f.OwnerSeat];
        if (f.InspectedHandSlash is not null || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            f.SelectedTargetSeats is not [var target] || !IsValidPlayerSeat(target) || target == owner.Seat ||
            _winner != Winner.None || !owner.IsAlive || owner.Hp < 1 || !_players[target].IsAlive || GetHand(_players[target]).Count == 0 ||
            _currentSeat != owner.Seat || _phase != TurnPhase.Play || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("HP hand inspection requires its exact unpaid living activation.");
        var d = new InspectedHandSlashDraft(f.InstructionIndex, InspectedHandSource(f), f.GameplayHash,
            _turnNumber, _currentSeat, target, owner.Hp, owner.Hp - 1, InspectedHandSlashStage.HpChildren, []);
        ReplaceRuntimeTop(f with { InspectedHandSlash = d });
        AdvanceEventRulesAndQueueFact(new InspectedHandHpPaidEvent(f.Id, d.Source, d.GameplayHash,
            d.TurnNumber, d.TurnOwnerSeat, target, d.HpBefore, d.HpAfter));
        var outcome = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1);
        if (outcome != SkillProgramStepOutcome.AwaitChild) AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeInspectedHandSlash(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.InspectedHandSlash is not { } d) return false;
        if (!ValidInspectedHandSlash(f)) throw new InvalidOperationException("HP hand inspection lost its original once-paid activation.");
        if (d.Stage == InspectedHandSlashStage.SlashIssued)
            throw new InvalidOperationException("An issued hand-inspection Slash requires its exact typed return.");
        if (d.Stage == InspectedHandSlashStage.Viewing)
        { if (_pendingDecision is null) PublishInspectedHandChoice(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id)) return true;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PendingMovementContinuation = null });
        // Source qualification was consumed by the actual cost; later loss cannot repeat or retract that payment.
        if (d.Stage == InspectedHandSlashStage.DiscardChildren || _winner != Winner.None ||
            !_players[f.OwnerSeat].IsAlive || !_players[d.TargetSeat].IsAlive || GetHand(_players[d.TargetSeat]).Count == 0)
        { FinishInspectedHandSlash(f, false); return true; }
        var hand = GetHand(_players[d.TargetSeat]);
        ReplaceRuntimeTop(f = f with { InspectedHandSlash = d with { Stage = InspectedHandSlashStage.Viewing,
            ViewedCardIds = hand.Select(c => c.Id).ToArray(), ContainsPrintedDodge = hand.Any(c => c.Kind == CardKind.Dodge) } });
        AdvanceEventRulesAndQueueFact(new InspectedHandViewedEvent(f.Id, f.OwnerSeat, d.TargetSeat, hand.Count));
        PublishInspectedHandChoice(f); return true;
    }
    private Dictionary<string, string> InspectedHandParameters(ProgramSkillFrame f, string option) => new()
    { ["program-action"] = "inspected-hand-slash", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option };
    private IReadOnlyList<PromptChoice> InspectedHandChoices(ProgramSkillFrame f)
    {
        var d = f.InspectedHandSlash!;
        if (d.ContainsPrintedDodge) return [new(new($"inspected-hand.{f.Id}.slash"), "已观看手牌，结算视为使用杀。", [], [], InspectedHandParameters(f, "slash"))];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, d.TargetSeat, [CardZoneKind.Hand],
            OwnedCardMoveIntent.Discard, canSelect: (_, card) => d.ViewedCardIds.Contains(card.Id)).Select(choice =>
        {
            var args = choice.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var pair in InspectedHandParameters(f, "discard")) args[pair.Key] = pair.Value;
            var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
            var card = GetHand(_players[d.TargetSeat])[slot];
            return new PromptChoice(choice.Id, $"弃置【{card.DisplayName}】（{card.Suit} {card.RankText}）", [card.Id], [], args);
        }).ToArray());
    }
    private void PublishInspectedHandChoice(ProgramSkillFrame f)
    {
        var choices = InspectedHandChoices(f);
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[f.InspectedHandSlash!.TargetSeat].IsAlive || choices.Count == 0)
        { FinishInspectedHandSlash(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "观看其当前全部手牌，随后按牌面结算。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.InspectedHandSlash.TargetSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 观看手牌", skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private IEnumerable<CardSnapshot> InspectedHandPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.OwnerSeat == viewer && f.InspectedHandSlash is { Stage: InspectedHandSlashStage.Viewing })
        .SelectMany(f => f.InspectedHandSlash!.ViewedCardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.InspectedHandSlash.TargetSeat))
            .Select(GetAttackCard)).Select(ToSnapshot);
    private void ResolveInspectedHandChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.InspectedHandSlash is not { Stage: InspectedHandSlashStage.Viewing } d ||
            !ValidInspectedHandSlash(f) || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != f.OwnerSeat || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            !AssistedChoicesEqual([choice], InspectedHandChoices(f).Where(c => c.Id == choice.Id).ToArray()) ||
            !GetHand(_players[d.TargetSeat]).Select(c => c.Id).SequenceEqual(d.ViewedCardIds))
            throw new InvalidOperationException("The inspected hand choice must retain its private viewer, complete current hand and exact payment.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[d.TargetSeat].IsAlive)
        { FinishInspectedHandSlash(f, false); return; }
        if (d.ContainsPrintedDodge) { IssueInspectedHandSlash(f); return; }
        if (choice.Cards is not [var cardId] || choice.Parameters.GetValueOrDefault("option") != "discard" ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(d.TargetSeat) ||
            IsForeignEquipmentDiscardPrevented(f.OwnerSeat, GetAttackCard(cardId), CardLocation.Hand(d.TargetSeat), OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("An inspected card cannot bypass actual discard eligibility.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f with { InspectedHandSlash = d with { Stage = InspectedHandSlashStage.DiscardChildren,
            DiscardedCardId = cardId, SequenceBefore = before, SequenceAfter = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAttackCard(cardId), CardLocation.Hand(d.TargetSeat), CardLocation.DiscardPile, new(InspectedHandDiscardReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { InspectedHandSlash = f.InspectedHandSlash! with { SequenceAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new InspectedHandDiscardPaidEvent(f.Id, d.TargetSeat, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ReturnInspectedHandMovement(ProgramSkillFrame f)
    {
        if (f.InspectedHandSlash is not { Stage: InspectedHandSlashStage.HpChildren or InspectedHandSlashStage.DiscardChildren }) return false;
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private PromptChoice SelectAiInspectedHand(PendingDecision decision, ProgramSkillFrame f) =>
        f.InspectedHandSlash!.ContainsPrintedDodge ? decision.Choices.Single() : decision.Choices
            .OrderByDescending(c => CardCatalog.Get(GetAttackCard(c.Cards.Single()).Kind).HandKeepValue)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First(); // Only the already-authorized viewer's revealed choices.
    private void FinishInspectedHandSlash(ProgramSkillFrame f, bool issued)
    {
        AdvanceEventRulesAndQueueFact(new InspectedHandFinishedEvent(f.Id, issued));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { InspectedHandSlash = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    private sealed partial class ProgramSkillHost : IInspectedHandSlashProgramHost
    { public SkillProgramStepOutcome PayHpInspectHandThenDiscardOrSlash(ProgramSkillFrame f) => engine.BeginInspectedHandSlash(f); }
}
