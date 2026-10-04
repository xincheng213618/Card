using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ConditionalDuelOwnerReason = "skill-program.conditional-discard-duel.owner-cost";
    private const string ConditionalDuelTargetReason = "skill-program.conditional-discard-duel.target-cost";
    private bool ConditionalDuelSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private bool CanStartConditionalDiscardDuel(CharacterState owner) => GetHand(owner).Concat(GetEquipment(owner))
        .Any(c => IsSlashCard(c.Kind) && !c.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(owner.Seat,
            c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard));
    private bool ConditionalDuelParentMatches(ProgramSkillFrame f)
    {
        if (f.ConditionalDiscardDuel is not { } d || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != d.InstructionIndex || d.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats is not [var target] || target != d.TargetSeat || target == f.OwnerSeat || !IsValidPlayerSeat(target) ||
            d.TurnNumber != _turnNumber || d.TurnOwnerSeat != _currentSeat || d.TurnOwnerSeat != f.OwnerSeat ||
            _phase != TurnPhase.Play || d.Source != new CardConversionSource(f.SkillId, f.ActivationId!, f.OwnerSeat, f.SkillInstanceId) ||
            d.GameplayHash != f.GameplayHash || string.IsNullOrWhiteSpace(f.SkillInstanceId) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, p);
        return plan.Instructions is [{ Op: SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel }] &&
            plan.Activation is { MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.OtherLivingInAttackRange, UsesPerTurn: null, UsesPerPhase: null, UsesPerGame: null } &&
            CompleteProgramEventHistory().OfType<ConditionalDiscardDuelStartedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Source == d.Source && e.GameplayHash == d.GameplayHash && e.TurnNumber == d.TurnNumber &&
                e.TurnOwnerSeat == d.TurnOwnerSeat && e.TargetSeat == d.TargetSeat) == 1;
    }
    private SkillProgramStepOutcome BeginConditionalDiscardDuel(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ConditionalDiscardDuel is not null || f.SelectedTargetSeats is not [var target] ||
            !ConditionalDuelSourceCurrent(f) || !_players[target].IsAlive || !IsWithinAttackRange(f.OwnerSeat, target))
            return SkillProgramStepOutcome.Continue;
        var source = new CardConversionSource(f.SkillId, f.ActivationId!, f.OwnerSeat, f.SkillInstanceId);
        ReplaceRuntimeTop(f = f with { ConditionalDiscardDuel = new(f.InstructionIndex, source, f.GameplayHash,
            _turnNumber, _currentSeat, target, ConditionalDiscardDuelStage.OwnerChoice) });
        AdvanceEventRulesAndQueueFact(new ConditionalDiscardDuelStartedEvent(f.Id, source, f.GameplayHash, _turnNumber, _currentSeat, target));
        if (!ConditionalDuelParentMatches(f)) throw new InvalidOperationException("Conditional Duel lost its exact activation.");
        PublishConditionalDuelPayment(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ConditionalDuelChoices(ProgramSkillFrame f)
    {
        var d = f.ConditionalDiscardDuel!; var ownerCost = d.Stage == ConditionalDiscardDuelStage.OwnerChoice;
        var payer = ownerCost ? f.OwnerSeat : d.TargetSeat;
        var choices = BuildOwnedCardPaymentChoices(f.Id, payer, payer, [CardZoneKind.Hand, CardZoneKind.Equipment],
            OwnedCardMoveIntent.Discard, canSelect: (_, c) => !ownerCost || IsSlashCard(c.Kind) && !c.IsGeneralWeapon)
            .Select(c => { var args = c.Parameters.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                args["program-action"] = "conditional-discard-duel"; args["cost-side"] = ownerCost ? "owner" : "target";
                return new PromptChoice(c.Id, c.Description, c.Cards, c.Targets, args); }).ToList();
        if (ownerCost) choices.Add(new(new($"conditional-discard-duel.{f.Id}.cancel"), "不支付，取消发动。", [], [],
            new Dictionary<string, string> { ["program-action"] = "conditional-discard-duel", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["cost-side"] = "cancel" }));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishConditionalDuelPayment(ProgramSkillFrame f)
    {
        var d = f.ConditionalDiscardDuel!;
        if (!ConditionalDuelSourceCurrent(f) || !_players[d.TargetSeat].IsAlive) { FinishConditionalDiscardDuel(f, false); return; }
        var payer = d.Stage == ConditionalDiscardDuelStage.OwnerChoice ? f.OwnerSeat : d.TargetSeat;
        var choices = ConditionalDuelChoices(f);
        if (choices.Count == 0) { FinishConditionalDiscardDuel(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, payer,
            payer == f.OwnerSeat ? "请选择实际手牌或装备中的一张杀弃置。" : "请选择自己的一张手牌或装备弃置。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = payer, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 实际弃牌", skill.Description) };
        _status = _players[payer].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveConditionalDuelPayment(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !ConditionalDuelParentMatches(f) ||
            f.ConditionalDiscardDuel is not { Stage: ConditionalDiscardDuelStage.OwnerChoice or ConditionalDiscardDuelStage.TargetChoice } d ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != (d.Stage == ConditionalDiscardDuelStage.OwnerChoice ? f.OwnerSeat : d.TargetSeat) ||
            !ConditionalDuelChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets)) ||
            selected.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Conditional Duel payment must be its exact published private choice.");
        if (!ConditionalDuelSourceCurrent(f) || !_players[d.TargetSeat].IsAlive)
        { ClearPendingDecision(); FinishConditionalDiscardDuel(f, false); return; }
        if (selected.Parameters.GetValueOrDefault("cost-side") == "cancel")
        { ClearPendingDecision(); FinishConditionalDiscardDuel(f, false); return; }
        var payer = prompt.PlayerSeat; var ownerCost = d.Stage == ConditionalDiscardDuelStage.OwnerChoice;
        if (!Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("Conditional Duel lost its own payment zone and slot.");
        var from = new CardLocation(zone, payer); var cards = _cardZones.CardsAt(from);
        if (slot < 0 || slot >= cards.Count || selected.Cards is not [var id] || cards[slot].Id != id ||
            ownerCost && (!IsSlashCard(cards[slot].Kind) || cards[slot].IsGeneralWeapon) ||
            IsForeignEquipmentDiscardPrevented(payer, cards[slot], from, OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("Conditional Duel cannot pay an invalid or substituted entity.");
        var card = cards[slot]; var before = _movementSequence;
        var paid = new ConditionalDiscardDuelPayment(payer, card.Id, card.Kind, from, before, before);
        ClearPendingDecision();
        ReplaceRuntimeTop(f with { ConditionalDiscardDuel = d with { Stage = ownerCost ? ConditionalDiscardDuelStage.OwnerChildren : ConditionalDiscardDuelStage.TargetChildren,
            OwnerPayment = ownerCost ? paid : d.OwnerPayment, TargetPayment = ownerCost ? null : paid },
            PendingMovementContinuation = new(payer, 0, null) });
        MoveCard(card, from, CardLocation.DiscardPile, new(ownerCost ? ConditionalDuelOwnerReason : ConditionalDuelTargetReason));
        f = GetActiveProgramFrame(f.Id); // Preserve queued SilverLion recovery/replacement fields on this actual owner.
        paid = paid with { SequenceAfter = _movementSequence };
        ReplaceRuntimeTop(f = f with { ConditionalDiscardDuel = f.ConditionalDiscardDuel! with {
            OwnerPayment = ownerCost ? paid : f.ConditionalDiscardDuel.OwnerPayment, TargetPayment = ownerCost ? null : paid } });
        AdvanceEventRulesAndQueueFact(new ConditionalDiscardDuelPaidEvent(f.Id, payer, ownerCost, paid.SequenceBefore, paid.SequenceAfter));
        ContinueConditionalDuelPaymentChildren(f);
    }
    private void ContinueConditionalDuelPaymentChildren(ProgramSkillFrame f)
    {
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ReturnConditionalDuelMovement(ProgramSkillFrame f)
    {
        if (f.ConditionalDiscardDuel is not { Stage: ConditionalDiscardDuelStage.OwnerChildren or ConditionalDiscardDuelStage.TargetChildren }) return false;
        if (f.PendingMovementContinuation is null || !ValidConditionalDuelPayments(f)) throw new InvalidOperationException("Conditional Duel movement lost its real cost.");
        ContinueConditionalDuelPaymentChildren(f); return true;
    }
    private bool ResumeConditionalDiscardDuel(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.ConditionalDiscardDuel is not { } d) return false;
        if (!ConditionalDuelParentMatches(f) || !ValidConditionalDuelPayments(f)) throw new InvalidOperationException("Conditional Duel lost its frozen owner, costs or instruction.");
        if (d.Stage is ConditionalDiscardDuelStage.OwnerChoice or ConditionalDiscardDuelStage.TargetChoice)
        { if (_pendingDecision is null) PublishConditionalDuelPayment(f); return true; }
        if (d.Stage == ConditionalDiscardDuelStage.DuelIssued) throw new InvalidOperationException("Issued Conditional Duel requires its exact typed use return.");
        if (f.PendingMovementContinuation is not null) { ContinueConditionalDuelPaymentChildren(f); return true; }
        if (!ConditionalDuelSourceCurrent(f) || !_players[d.TargetSeat].IsAlive) { FinishConditionalDiscardDuel(f, false); return true; }
        if (d.Stage == ConditionalDiscardDuelStage.OwnerChildren)
        {
            ReplaceRuntimeTop(f = f with { ConditionalDiscardDuel = d with { Stage = ConditionalDiscardDuelStage.TargetChoice } });
            PublishConditionalDuelPayment(f); return true;
        }
        // This comparison happens only after every real child of the target's actual discard has returned.
        if (d.TargetPayment is not { } paid || IsSlashCard(paid.PrintedKind) || _players[d.TargetSeat].Hp < _players[f.OwnerSeat].Hp ||
            !CanIssueSelectedActorDuel(d.TargetSeat, f.OwnerSeat)) { FinishConditionalDiscardDuel(f, false); return true; }
        var useId = _resolutionSequence + 1;
        var origin = new ConditionalDiscardDuelOrigin(f.Id, d.InstructionIndex, useId, d.Source, d.GameplayHash, d.TurnNumber,
            d.TurnOwnerSeat, f.OwnerSeat, d.TargetSeat, _players[f.OwnerSeat].Hp, _players[d.TargetSeat].Hp);
        ReplaceRuntimeTop(f with { ConditionalDiscardDuel = d with { Stage = ConditionalDiscardDuelStage.DuelIssued,
            CardUseFrameId = useId, OwnerHpAtIssue = origin.OwnerHpAtIssue, TargetHpAtIssue = origin.TargetHpAtIssue } });
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var issued = BeginCardUse(card, f.OwnerSeat, [d.TargetSeat], CardKind.Duel, physicalCardIds: [], conditionalDiscardDuelOrigin: origin);
        if (issued != useId) throw new InvalidOperationException("Conditional Duel must retain its reserved owning Use.");
        AdvanceEventRulesAndQueueFact(new ConditionalDiscardDuelIssuedEvent(origin));
        BeginJizhiOrNullificationWindow(useId, card, f.OwnerSeat, [d.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return true;
    }
    private void FinishConditionalDiscardDuel(ProgramSkillFrame f, bool issued)
    {
        AdvanceEventRulesAndQueueFact(new ConditionalDiscardDuelFinishedEvent(f.Id, issued));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ConditionalDiscardDuel = null, PendingMovementContinuation = null });
        if (issued) FinishProgramSkill(f, true);
        else if (ConditionalDuelSourceCurrent(f)) AdvanceRuntimeProgram(f.Id);
        else CancelProgramBindingAndCleanup(f, "已付弃牌保留，未发行决斗取消。");
    }
    private PromptChoice SelectAiConditionalDuel(PendingDecision decision, ProgramSkillFrame f)
    {
        var choices = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("cost-side") != "cancel").ToArray();
        if (choices.Length == 0) return decision.Choices[0];
        // Every named card belongs to this exact chooser. No opponent's hidden identity is inspected.
        var d = f.ConditionalDiscardDuel!;
        return choices.OrderBy(c => d.Stage == ConditionalDiscardDuelStage.TargetChoice &&
            _players[d.TargetSeat].Hp >= _players[f.OwnerSeat].Hp && c.Cards is [var id] && IsSlashCard(GetAttackCard(id).Kind) ? -100 : 0)
            .ThenBy(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment) ? 1 : 0)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    private sealed partial class ProgramSkillHost : IConditionalDiscardDuelProgramHost
    { public SkillProgramStepOutcome DiscardSlashThenOtherCardAndUseDuel(ProgramSkillFrame frame) => engine.BeginConditionalDiscardDuel(frame); }
}
