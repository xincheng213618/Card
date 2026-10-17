using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OrdinaryQietingMoveReason = "skill-program.cai-fu-ren.qieting.move";
    private const string OrdinaryQietingDrawReason = "skill-program.cai-fu-ren.qieting.draw";
    private bool CanMoveOrdinaryQietingEquipment(ProgramSkillFrame f, Card card)
    {
        var peer = f.OrdinaryQieting!.Qualification.TurnOwnerSeat;
        return !card.IsGeneralWeapon && CanPlaceOwnedEquipment(peer, f.OwnerSeat, card, CardLocation.Equipment(peer)) &&
            GetEquipment(_players[f.OwnerSeat]).All(c => EquipmentCatalog.Get(c.Kind).Slot != EquipmentCatalog.Get(card.Kind).Slot);
    }
    private SkillProgramStepOutcome BeginOrdinaryQieting(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OrdinaryQieting is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.TriggerId != "ordinary-turn-end" || f.WindowContext is not { OrdinaryQieting: { UsedOnAnother: false } q } c || _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == c.ParentFrameId) is not { } parent || !IsEligibleOrdinaryCaiFuRenQietingCandidate(parent, new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c))
            throw new InvalidOperationException("Ordinary Qieting requires its exact unused-on-others actual turn and original binding.");
        var r = new OrdinaryQietingReceipt(OrdinaryCaiFuRenSource(f), f.GameplayHash, q, parent.Id, c.OccurrenceIndex, OrdinaryQietingStage.Choice);
        ReplaceRuntimeTop(f = f with { OrdinaryQieting = r });
        AdvanceEventRulesAndQueueFact(new OrdinaryQietingStartedEvent(f.Id, r.Source, r.GameplayHash, q, parent.Id, c.OccurrenceIndex));
        PublishOrdinaryQieting(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> OrdinaryQietingChoices(ProgramSkillFrame f)
    {
        if (f.OrdinaryQieting is not { Stage: OrdinaryQietingStage.Choice, Paid: false } r || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        PromptChoice Choice(string option, string label, IReadOnlyList<int> cards) => new(new($"ordinary-qieting.{f.Id}.{option}"), label, cards, [f.OwnerSeat],
            new Dictionary<string, string> { ["program-action"] = "ordinary-qieting", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
        var choices = GetEquipment(_players[r.Qualification.TurnOwnerSeat]).Where(c => CanMoveOrdinaryQietingEquipment(f, c)).OrderBy(c => c.Id)
            .Select(c => Choice($"equipment:{c.Id}", $"将【{c.DisplayName}】置入你的空装备栏", [c.Id])).ToList();
        choices.Add(Choice("draw", "摸一张牌", []));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishOrdinaryQieting(ProgramSkillFrame f)
    {
        AssertOrdinaryQieting(f);
        var choices = OrdinaryQietingChoices(f); if (choices.Count == 0) { FinishOrdinaryQieting(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "窃听：置入其一张空槽装备，或摸一张牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [f.OwnerSeat], SourceSeat: f.OrdinaryQieting!.Qualification.TurnOwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ordinary-qieting")]
    private void AnswerOrdinaryQieting(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrdinaryQieting: { Stage: OrdinaryQietingStage.Choice, Paid: false } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !OrdinaryQietingChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Ordinary Qieting requires its current original single option.");
        AssertOrdinaryQieting(f); ClearPendingDecision();
        var option = selected.Parameters["option"];
        Card? card = option == "draw" ? null : GetAdvancedCard(selected.Cards.Single());
        if (card is not null && !CanMoveOrdinaryQietingEquipment(f, card)) throw new InvalidOperationException("Ordinary Qieting cannot replace equipment or substitute its source entity.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, OrdinaryQieting = r = r with
        {
            Paid = true, Option = option == "draw" ? "draw" : "equipment", CardId = card?.Id, PrintedKind = card?.Kind,
            Stage = card is null ? OrdinaryQietingStage.Draw : OrdinaryQietingStage.MovementChildren, Before = before, After = before,
            GrainIds = card?.Kind == CardKind.WoodenOx ? GetWoodenOxGrain(_players[r.Qualification.TurnOwnerSeat]).Select(c => c.Id).ToArray() : []
        }, PendingMovementContinuation = card is null ? null : new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new OrdinaryQietingOptionIssuedEvent(f.Id, r.Option!, card?.Id, card?.Kind, before));
        if (card is not null)
        {
            var from = CardLocation.Equipment(r.Qualification.TurnOwnerSeat);
            MoveCard(card, from, CardLocation.Equipment(f.OwnerSeat), new(OrdinaryQietingMoveReason), movement =>
            {
                var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { OrdinaryQieting = current.OrdinaryQieting! with { After = movement.Sequence } });
                AdvanceEventRulesAndQueueFact(new OrdinaryQietingEquipmentMovedEvent(f.Id, movement.CardId, movement.CardKind, from, movement.To, movement.Sequence));
            });
            var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = current with { OrdinaryQieting = current.OrdinaryQieting! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id, f.OwnerSeat, EquipmentCatalog.Get(card.Kind).Slot, card.Id, card.Kind, null));
            AddLog("EquipmentChanged", $"{_players[f.OwnerSeat].Name} 发动窃听，将【{card.DisplayName}】置入自己的装备区。", f.OwnerSeat);
        }
        ContinueOrdinaryQieting(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting)]
    private void ReturnOrdinaryQietingDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.OrdinaryQieting is not { Paid: true, Stage: OrdinaryQietingStage.Draw, Option: "draw" } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 || ProgramEventHistory<OrdinaryQietingDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Ordinary Qieting lost its one native draw and typed return.");
        ReplaceRuntimeTop(f = f with { OrdinaryQieting = r with { Stage = OrdinaryQietingStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new OrdinaryQietingDrawReturnedEvent(f.Id, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueOrdinaryQieting(f.Id);
    }
    private bool DrainOrdinaryQieting(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueOrdinaryQieting(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrdinaryQieting: { } r } f || f.Id != id) return;
        AssertOrdinaryQieting(f);
        if (r.Stage == OrdinaryQietingStage.Choice)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishOrdinaryQieting(f); return; }
            if (_pendingDecision is null) PublishOrdinaryQieting(f); return;
        }
        if (r.Stage == OrdinaryQietingStage.Draw)
        {
            if (f.NativeDraw is not null) return;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishOrdinaryQieting(f); return; }
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(OrdinaryQietingDrawReason)); return;
        }
        if (r.Stage is OrdinaryQietingStage.MovementChildren or OrdinaryQietingStage.DrawChildren)
        {
            if (DrainOrdinaryQieting(f)) return;
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            FinishOrdinaryQieting(f); return;
        }
        throw new InvalidOperationException("Ordinary Qieting returned outside its issued option.");
    }
    private void FinishOrdinaryQieting(ProgramSkillFrame f)
    {
        var paid = f.OrdinaryQieting?.Paid == true;
        AdvanceEventRulesAndQueueFact(new OrdinaryQietingCompletedEvent(f.Id, paid));
        ReplaceRuntimeTop(f = f with { OrdinaryQieting = null, PendingMovementContinuation = null }); FinishProgramSkill(f, paid);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27596)]
    private bool ResumeOrdinaryQieting(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrdinaryQieting: not null } f || f.Id != id) return false; ContinueOrdinaryQieting(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27596)]
    private bool ReturnOrdinaryQietingMovement(ProgramSkillFrame f) { if (f.OrdinaryQieting is null) return false; ContinueOrdinaryQieting(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting)]
    private PromptChoice ChooseOrdinaryQietingAi(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderByDescending(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat]) : 5d)
        .ThenBy(c => c.Cards.FirstOrDefault()).First();
}
