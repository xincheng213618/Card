namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ActualEndedEquipmentMoveReason = "program.actual-ended-turn.equipment";
    private const string ActualEndedEquipmentDrawReason = "program.actual-ended-turn.draw";
    private bool TracksActualForeignUseTargets => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn);
    private bool IsActualEndedEquipmentTrigger(SkillProgramTrigger trigger) => trigger.Effects.Any(e =>
        e.Op == SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn);

    private void ObserveActualForeignUseTargets(IGameEvent payload)
    {
        if (!TracksActualForeignUseTargets || payload is not TargetsConfirmedEvent targets) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == targets.ResolutionId);
        if (use is null || !IsValidPlayerSeat(use.SourceSeat) || !targets.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id &&
                e.SourceSeat == use.SourceSeat && e.CardId == use.CardId && e.CardKind == use.CardKind)) return;
        var action = use.Action;
        if (action is not null && (action.Type != CardActionType.Use || action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind)) return;
        long? legacy = null;
        if (action is null)
        {
            if (use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || !IsActualTurnLegacyVirtualUse(use)) return;
            var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame producer) return;
            legacy = producer.Id;
        }
        foreach (var seat in targets.TargetSeats.Distinct().Where(IsValidPlayerSeat))
        {
            if (CompleteProgramEventHistory().OfType<ActualTurnForeignUseTargetEvent>().Any(e => e.TurnNumber == _turnNumber &&
                e.TurnOwnerSeat == _currentSeat && e.CardUseFrameId == use.Id && e.ActorSeat == use.SourceSeat && e.TargetSeat == seat)) continue;
            AdvanceEventRulesAndQueueFact(new ActualTurnForeignUseTargetEvent(_turnNumber, _currentSeat, use.Id,
                action?.ActionId, use.SourceSeat, action?.ProviderSeat ?? use.SourceSeat, use.CardKind, seat, legacy));
        }
    }
    private ActualEndedTurnEquipmentContext? CaptureActualEndedEquipmentContext(DeferredTurnEndFrame parent)
    {
        if (!IsActualAfterTurnEndedParent(parent)) return null;
        var history = CompleteProgramEventHistory().ToArray();
        var end = Array.FindLastIndex(history, e => e is TurnEndedEvent ended && ended.TurnNumber == parent.TurnNumber && ended.ActorSeat == parent.OwnerSeat);
        if (end < 0) return null;
        var start = Array.FindLastIndex(history, end, e => e is TurnStartedEvent begun && begun.TurnNumber == parent.TurnNumber && begun.ActorSeat == parent.OwnerSeat);
        if (start < 0 || history.Skip(start + 1).Take(end - start - 1).Any(e => e is TurnStartedEvent or TurnEndedEvent)) return null;
        var actual = history.Skip(start + 1).Take(end - start - 1).ToArray();
        var damaged = actual.OfType<DamageAppliedEvent>().Any(e => !e.SourceLess && e.Amount > 0 &&
            e.SourceSeat == parent.OwnerSeat && e.TargetSeat != parent.OwnerSeat && IsValidPlayerSeat(e.TargetSeat));
        var used = actual.OfType<ActualTurnForeignUseTargetEvent>().Any(e => e.TurnNumber == parent.TurnNumber &&
            e.TurnOwnerSeat == parent.OwnerSeat && e.ActorSeat == parent.OwnerSeat && e.TargetSeat != parent.OwnerSeat);
        return new(parent.TurnNumber, parent.OwnerSeat, damaged, used, !used ? 2 : !damaged ? 1 : 0);
    }
    private bool MatchesActualEndedEquipmentContext(DeferredTurnEndFrame parent, ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context, SkillProgramTrigger trigger, bool requireLivingTurnOwner = true)
    {
        var frozen = context.ActualEndedEquipment;
        var actual = CaptureActualEndedEquipmentContext(parent);
        return IsActualEndedEquipmentTrigger(trigger) && actual is not null && frozen is not null && frozen == actual &&
            frozen.MaximumDistinctOptions > 0 && trigger.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
            trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && context.Window == trigger.Window &&
            context.ParentFrameId == parent.Id && candidate.OwnerSeat != parent.OwnerSeat &&
            (!requireLivingTurnOwner || _players[parent.OwnerSeat].IsAlive) && context.OwnerSeat == candidate.OwnerSeat &&
            context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
            context.OccurrenceIndex == candidate.OccurrenceIndex && AfterTurnEndedCandidate(parent) == candidate;
    }
    private bool MatchesActualEndedEquipmentRoot(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded, ActualEndedEquipment: not null } context ||
            _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } parent ||
            parent.AfterTurnEnded?.CurrentChild is not { } child || child.FrameId != f.Id ||
            !MountObserverCandidateMatches(f, child.Candidate)) return false;
        return MatchesActualEndedEquipmentContext(parent, child.Candidate, context, GetProgramTrigger(child.Candidate), requireLivingTurnOwner: false);
    }
    private SkillProgramStepOutcome RunActualEndedTurnEquipment(ProgramSkillFrame f)
    {
        f = GetActiveProgramFrame(f.Id);
        if (!MatchesActualEndedEquipmentRoot(f)) throw new InvalidOperationException("Equipment options require the original completed actual turn and exact current candidate.");
        if (f.ActualEndedEquipment is null)
        {
            var context = f.WindowContext!;
            ReplaceRuntimeTop(f = f with { ActualEndedEquipment = new(0,
                new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
                context.ParentFrameId, context.OccurrenceIndex, context.ActualEndedEquipment!, ActualEndedTurnEquipmentStage.Offering) });
        }
        if (!IsValidActualEndedEquipment(f)) throw new InvalidOperationException("Equipment options lost their issued actual-turn draw/placement ledger.");
        var r = f.ActualEndedEquipment!;
        if (r.Stage == ActualEndedTurnEquipmentStage.Complete) return SkillProgramStepOutcome.Continue;
        if (_winner != Winner.None || !EquipmentDonationSourceValid(f) || !_players[r.Qualification.TurnOwnerSeat].IsAlive ||
            (r.EquipmentIssued ? 1 : 0) + (r.DrawIssued ? 1 : 0) >= r.Qualification.MaximumDistinctOptions)
        { ReplaceRuntimeTop(f with { ActualEndedEquipment = r with { Stage = ActualEndedTurnEquipmentStage.Complete } }); return SkillProgramStepOutcome.Continue; }
        var choices = ActualEndedEquipmentChoices(f);
        if (choices.Count == 0)
        { ReplaceRuntimeTop(f with { ActualEndedEquipment = r with { Stage = ActualEndedTurnEquipmentStage.Complete } }); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(f = f with { ActualEndedEquipment = r with { Stage = ActualEndedTurnEquipmentStage.Offering }, ReexecuteParticipantInstruction = true });
        PublishEquipmentDonationPrompt(f, f.OwnerSeat, $"原实际回合可选择至多 {r.Qualification.MaximumDistinctOptions} 个不同选项。", choices);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ActualEndedEquipmentChoices(ProgramSkillFrame f)
    {
        var r = f.ActualEndedEquipment!; var choices = new List<PromptChoice>();
        if (!r.DrawIssued) choices.Add(new(new($"actual-ended-equipment.{f.Id}.draw"), "摸一张牌。", [], [],
            EquipmentDonationParameters(f, "actual-ended-equipment", "draw")));
        if (!r.EquipmentIssued)
            foreach (var card in GetEquipment(_players[r.Qualification.TurnOwnerSeat]).Where(c => !c.IsGeneralWeapon &&
                CanPlaceOwnedEquipment(r.Qualification.TurnOwnerSeat, f.OwnerSeat, c, CardLocation.Equipment(r.Qualification.TurnOwnerSeat))).OrderBy(c => c.Id))
                choices.Add(new(new($"actual-ended-equipment.{f.Id}.equipment.{card.Id}"), $"将【{card.DisplayName}】置入自己的装备区。", [card.Id], [f.OwnerSeat],
                    EquipmentDonationParameters(f, "actual-ended-equipment", "equipment")));
        return choices;
    }
    private void ResolveActualEndedEquipmentChoice(ProgramSkillFrame f, PromptChoice choice)
    {
        if (!IsValidActualEndedEquipment(f) || f.ActualEndedEquipment is not { } r || r.Stage != ActualEndedTurnEquipmentStage.Offering ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat) throw new InvalidOperationException("An ended-turn equipment choice lost its exact original turn.");
        choice = ActualEndedEquipmentChoices(f).Single(c => c.Id == choice.Id);
        ClearPendingDecision();
        if (!EquipmentDonationSourceValid(f) || _winner != Winner.None)
        { ReplaceRuntimeTop(f with { ActualEndedEquipment = r with { Stage = ActualEndedTurnEquipmentStage.Complete }, ReexecuteParticipantInstruction = true }); AdvanceRuntimeProgram(f.Id); return; }
        var before = EquipmentDonationSequence; int? id = null, replacedId = null; var replacedGeneral = false; var draw = 0;
        var option = choice.Parameters["option"];
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (option == "draw")
        {
            if (r.DrawIssued || choice.Cards.Count != 0 || choice.Targets.Count != 0) throw new InvalidOperationException("A draw option cannot repeat or select equipment.");
            DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new(ActualEndedEquipmentDrawReason));
            draw = _cardMovements.Count(m => m.Sequence > before && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ActualEndedEquipmentDrawReason);
            r = r with { DrawIssued = true };
        }
        else
        {
            if (option != "equipment" || r.EquipmentIssued || choice.Cards is not [var cardId] || !choice.Targets.SequenceEqual([f.OwnerSeat]))
                throw new InvalidOperationException("An equipment option requires one original turn owner's current actual equipment.");
            var from = CardLocation.Equipment(r.Qualification.TurnOwnerSeat); var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
            if (card.IsGeneralWeapon || !CanPlaceOwnedEquipment(r.Qualification.TurnOwnerSeat, f.OwnerSeat, card, from))
                throw new InvalidOperationException("The current equipment cannot survive actual placement.");
            var slot = EquipmentCatalog.Get(card.Kind).Slot;
            var equipped = GetEquipment(_players[f.OwnerSeat]).Where(c => EquipmentCatalog.Get(c.Kind).Slot == slot).ToArray();
            var replaced = equipped.Length >= _players[f.OwnerSeat].EquipmentSlotCapacity(slot) ? equipped[0] : null;
            replacedId = replaced?.Id; replacedGeneral = replaced?.IsGeneralWeapon == true;
            if (replaced is not null)
            { CopyFirstReplacedWeapon(card, replaced); MoveCard(replaced, CardLocation.Equipment(f.OwnerSeat), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace); }
            MoveCard(card, from, CardLocation.Equipment(f.OwnerSeat), new(ActualEndedEquipmentMoveReason));
            AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id, f.OwnerSeat, slot, card.Id, card.Kind, replaced?.Id));
            id = card.Id; r = r with { EquipmentIssued = true };
        }
        var after = EquipmentDonationSequence;
        r = r with { Stage = ActualEndedTurnEquipmentStage.Moving, LastPayment = new(option, id, before, after, draw, replacedId, replacedGeneral) };
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ActualEndedEquipment = r, ReexecuteParticipantInstruction = true });
        AdvanceEventRulesAndQueueFact(new ActualEndedTurnEquipmentOptionIssuedEvent(f.Id, r.Source, r.GameplayHash,
            r.Qualification.TurnNumber, r.Qualification.TurnOwnerSeat, r.Qualification.MaximumDistinctOptions, option, id, before, after, draw, replacedId, replacedGeneral));
        if (AwaitEquipmentDonationMovements(f.Id) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
    }
    private PromptChoice SelectAiActualEndedEquipment(PendingDecision decision, ProgramSkillFrame f) =>
        decision.Choices.OrderByDescending(c => c.Parameters["option"] == "equipment" ?
            GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat]) : 5d)
            .ThenBy(c => c.Cards.FirstOrDefault()).First();
}
