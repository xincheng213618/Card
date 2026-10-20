using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouJieyueGiftReason = "skill-program.mou-mobile:jieyue.gift";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29301, SkillProgramEffectOp.ResolveMouYuJinJieyue)]
    private bool CanOfferMouJieyue(ProgramTriggerLegalityContext c) => c.SkillId == MouJieyue && MouYuJinComposition.IsJieyue(c.Program) &&
        c.Owner.IsAlive && c.Context.Window == SkillProgramTriggerWindow.TurnEnding && c.Context.SourceSeat == c.OwnerSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Ending } phase && phase.ActorSeat == c.OwnerSeat &&
        _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat);
    private bool MouJieyueRoot(ProgramSkillFrame f)
    {
        if (f.SkillId != MouJieyue || f.TriggerId != "ending-armor-and-gift" || f.ActivationId != f.TriggerId ||
            f.SelectedCardIds.Count != 0 || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } c ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat || c.OccurrenceIndex != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouYuJinComposition.IsJieyue(p) ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate)) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return i > 0 && _resolutionStack[i - 1].Id == parent.Id;
    }
    private bool MouJieyueArmorGranted(ProgramSkillFrame f) => f.MouYuJinJieyue is { } r &&
        ProgramEventHistory<ArmorGrantedEvent>().Where(e => e.ProgramFrameId == f.Id && e.InstructionIndex == 2).ToArray() is [var grant] &&
        grant.PlayerSeat == r.RecipientSeat && grant.Amount == 1 && grant.ArmorAfter == checked(grant.ArmorBefore + 1) &&
        grant.Source == CreateProgramTurnEffectSource(f);
    private bool ValidMouJieyue(ProgramSkillFrame f)
    {
        if (!MouJieyueRoot(f) || f.MouYuJinJieyue is not { } r || r.Source != MouYuJinSource(f) || r.GameplayHash != f.GameplayHash ||
            r.OriginalEndingWindowId != f.WindowContext!.ParentFrameId || !f.SelectedTargetSeats.SequenceEqual([r.RecipientSeat]) ||
            !IsValidPlayerSeat(r.RecipientSeat) || r.RecipientSeat == f.OwnerSeat || r.Phase.ActorSeat != f.OwnerSeat ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Ending || !IsRecordedActualDiscardRecoveryPhase(r.Phase) || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<MouYuJinJieyueReservedEvent>().Count(e => e == new MouYuJinJieyueReservedEvent(f.Id,
                r.Source, r.GameplayHash, r.OriginalEndingWindowId, r.Phase, r.RecipientSeat)) != 1) return false;
        if (r.Stage == MouYuJinJieyueStage.ArmorReserved) return r.GiftCardId is null && r.GiftFrom is null && r.GiftTo is null && r.GiftMovementSequence is null;
        if (!MouJieyueArmorGranted(f)) return false;
        if (r.GiftCardId is { } id)
        {
            if (r.GiftFrom is not { } from || from.OwnerSeat != r.RecipientSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                r.GiftTo is not { } to || to != CardLocation.Hand(f.OwnerSeat) && to != CardLocation.OutsideGame || r.GiftMovementSequence is not { } sequence ||
                ProgramEventHistory<MouYuJinJieyueGiftPaidEvent>().Count(e => e == new MouYuJinJieyueGiftPaidEvent(f.Id, r.RecipientSeat, id, from, to, sequence)) != 1 ||
                _cardMovements.Count(m => m.CardId == id && m.Sequence == sequence && m.From == from && m.To == to && m.Reason.Value == MouJieyueGiftReason) != 1) return false;
        }
        else if (r.GiftFrom is not null || r.GiftTo is not null || r.GiftMovementSequence is not null ||
            r.Stage == MouYuJinJieyueStage.GiftChildren) return false;
        return r.Stage != MouYuJinJieyueStage.Complete || ProgramEventHistory<MouYuJinJieyueCompletedEvent>().Count(e => e.FrameId == f.Id) == 1;
    }
    private SkillProgramStepOutcome ResolveMouJieyue(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouJieyueRoot(f)) throw new InvalidOperationException("Jieyue requires its exact own native Ending boundary and complete grant-first contract.");
        if (f.InstructionIndex == 2)
        {
            if (f.MouYuJinJieyue is not null || f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive ||
                !_players[f.OwnerSeat].IsAlive || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Ending } phase ||
                phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
                throw new InvalidOperationException("Jieyue cannot reserve an absent recipient or artificial Ending.");
            var receipt = new MouYuJinJieyueReceipt(MouYuJinSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, phase, target);
            ReplaceRuntimeTop(f with { MouYuJinJieyue = receipt });
            AdvanceEventRulesAndQueueFact(new MouYuJinJieyueReservedEvent(f.Id, receipt.Source, receipt.GameplayHash,
                receipt.OriginalEndingWindowId, phase, target));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 4 || !ValidMouJieyue(f) || f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.ArmorReserved } r || !MouJieyueArmorGranted(f))
            throw new InvalidOperationException("Jieyue's independent gift must follow the exact committed Armor1 grant once.");
        ReplaceRuntimeTop(f = f with { MouYuJinJieyue = r with { Stage = MouYuJinJieyueStage.GiftChoice } });
        var choices = MouJieyueGiftChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.RecipientSeat].IsAlive || choices.Count == 1)
        { CompleteMouJieyue(f); return SkillProgramStepOutcome.AwaitChild; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.RecipientSeat, "节钺：已获得1点护甲，你可以交给技能拥有者一张手牌或装备牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.RecipientSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.RecipientSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouJieyueGiftChoices(ProgramSkillFrame f)
    {
        var r = f.MouYuJinJieyue!;
        var choices = BuildOwnedCardPaymentChoices(f.Id, r.RecipientSeat, r.RecipientSeat, [CardZoneKind.Hand, CardZoneKind.Equipment],
            OwnedCardMoveIntent.Transfer, destination: SkillProgramCardDestination.OwnerHand).Select(c => c with
            { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-yu-jin-jieyue-gift", ["option-id"] = "give" } });
        return Array.AsReadOnly(choices.Append(new PromptChoice(new($"mou-jieyue.{f.Id}.decline"), "不交给牌", [], [],
            new Dictionary<string, string> { ["program-action"] = "mou-yu-jin-jieyue-gift", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["option-id"] = "decline" })).ToArray());
    }
    [ProgramAnswer("mou-yu-jin-jieyue-gift")]
    private void AnswerMouJieyueGift(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!ValidMouJieyue(f) || f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.GiftChoice } r ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != r.RecipientSeat ||
            !AssistedChoicesEqual([selected], MouJieyueGiftChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Jieyue accepts only the actual recipient's canonical private HE gift or refusal.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("option-id") == "decline" || !_players[f.OwnerSeat].IsAlive || !_players[r.RecipientSeat].IsAlive)
        {
            AdvanceEventRulesAndQueueFact(new MouYuJinJieyueGiftDeclinedEvent(f.Id, r.RecipientSeat));
            CompleteMouJieyue(f); return;
        }
        if (!Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), NumberStyles.None, CultureInfo.InvariantCulture, out var slot))
            throw new InvalidOperationException("Jieyue cannot substitute its frozen native payment zone and slot.");
        var from = new CardLocation(zone, r.RecipientSeat); var cards = _cardZones.CardsAt(from);
        if (slot < 0 || slot >= cards.Count || selected.Cards is not [var id] || cards[slot].Id != id)
            throw new InvalidOperationException("Jieyue's published physical gift has changed.");
        MoveCard(cards[slot], from, CardLocation.Hand(f.OwnerSeat), new(MouJieyueGiftReason), movement =>
        {
            var current = GetActiveProgramFrame(f.Id); var old = current.MouYuJinJieyue!;
            ReplaceRuntimeTop(current with { MouYuJinJieyue = old with { Stage = MouYuJinJieyueStage.GiftChildren,
                GiftCardId = id, GiftFrom = from, GiftTo = movement.To, GiftMovementSequence = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new MouYuJinJieyueGiftPaidEvent(f.Id, r.RecipientSeat, id, from, movement.To, movement.Sequence));
        }, disclosure: zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        ContinueMouJieyue(f.Id);
    }
    private void ContinueMouJieyue(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (!ValidMouJieyue(f) || f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.GiftChildren })
            throw new InvalidOperationException("Jieyue can return only its once-paid actual gift and native children.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        CompleteMouJieyue(GetActiveProgramFrame(id));
    }
    private void CompleteMouJieyue(ProgramSkillFrame f)
    {
        var r = f.MouYuJinJieyue!;
        if (r.Stage == MouYuJinJieyueStage.Complete) throw new InvalidOperationException("Jieyue cannot finish twice.");
        ReplaceRuntimeTop(f with { MouYuJinJieyue = r with { Stage = MouYuJinJieyueStage.Complete } });
        AdvanceEventRulesAndQueueFact(new MouYuJinJieyueCompletedEvent(f.Id));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29301)]
    private bool ReturnMouJieyueMovement(ProgramSkillFrame f)
    {
        if (f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.GiftChildren }) return false;
        ContinueMouJieyue(f.Id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29300)]
    private bool ResumeMouYuJin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouYuJinJieyue is { Stage: MouYuJinJieyueStage.GiftChildren }) { ContinueMouJieyue(id); return true; }
        return f.MouYuJinXiayuan is not null && f.InstructionIndex == 3 && f.PendingMovementContinuation is not null && ReturnMouXiayuanMovement(f);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouYuJinJieyue)]
    private PromptChoice SelectAiMouJieyueGift(PendingDecision p, ProgramSkillFrame f)
    {
        var giver = _players[p.PlayerSeat]; var view = CreateSnapshot(giver.Seat);
        var best = p.Choices.Where(c => c.Parameters.GetValueOrDefault("option-id") == "give").Select(c =>
        {
            var benefit = _aiBrains[giver.Seat].ScoreProgramTarget(view, f.OwnerSeat, new(0, 0, 0, 1, 0, 0, true, false));
            return (Choice: c, Score: benefit - GetKeepValue(GetAdvancedCard(c.Cards.Single()), giver) * 0.35d);
        }).OrderByDescending(c => c.Score).ThenBy(c => c.Choice.Id.Value, StringComparer.Ordinal).FirstOrDefault();
        return best.Choice is not null && best.Score > 0 ? best.Choice : p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "decline");
    }
}
