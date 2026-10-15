using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IPhaseStartCardControlHost
    {
        public SkillProgramStepOutcome GrantOtherPlayUseRestriction(ProgramSkillFrame frame, string skillId) =>
            engine.BeginPhaseStartCardControl(frame, PhaseStartCardControlKind.OtherPlayRestriction, skillId);
        public SkillProgramStepOutcome ChooseStartingEquipmentControl(ProgramSkillFrame frame) =>
            engine.BeginPhaseStartCardControl(frame, PhaseStartCardControlKind.StartingEquipment, null);
    }
    private static string PhaseStartControlReason(PhaseStartCardControlKind kind) =>
        kind == PhaseStartCardControlKind.OtherPlayRestriction ? "skill-program.other-play-restriction.cost" : "skill-program.starting-equipment.move";
    private CardConversionSource PhaseStartControlSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private Card[] OtherPlayRestrictionCosts(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).Where(card =>
        !IsSelfHandCategoryDiscardForbidden(owner, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(owner, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private bool HasPhaseStartControlBoundary(int owner, ProgramSkillWindowContext? context, PhaseStartCardControlKind kind) =>
        context is { Window: SkillProgramTriggerWindow.PlayPhaseStarting, SourceSeat: { } actor, TargetSeat: { } target } &&
        actor == target && actor == _currentSeat && context.OwnerSeat == owner && _phase == TurnPhase.Play &&
        _players[owner].IsAlive && _players[actor].IsAlive &&
        (kind == PhaseStartCardControlKind.OtherPlayRestriction ? actor != owner : actor == owner) &&
        _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().Any(p => p.Id == context.ParentFrameId && p.OwnerSeat == actor);
    private bool CanOfferPhaseStartCardControl(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is [{ Op: SkillProgramEffectOp.GrantOtherPlayUseRestriction }])
            return HasPhaseStartControlBoundary(candidate.OwnerSeat, context, PhaseStartCardControlKind.OtherPlayRestriction) &&
                IsWithinAttackRange(_currentSeat, candidate.OwnerSeat) && OtherPlayRestrictionCosts(candidate.OwnerSeat).Length > 0;
        if (trigger.Effects is [{ Op: SkillProgramEffectOp.ChooseStartingEquipmentControl }])
            return HasPhaseStartControlBoundary(candidate.OwnerSeat, context, PhaseStartCardControlKind.StartingEquipment) &&
                StartingEquipmentControlChoices(candidate.OwnerSeat).Any();
        return true;
    }
    private IEnumerable<(Card Card, int Seat, bool Obtain)> StartingEquipmentControlChoices(int owner)
    {
        foreach (var target in _players.Where(p => p.IsAlive))
        foreach (var card in GetEquipment(target))
        {
            if (target.Seat != owner && !IsForeignEquipmentDiscardPrevented(owner, card,
                    CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard)) yield return (card, target.Seat, false);
            if (EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Armor) yield return (card, target.Seat, true);
        }
    }
    private SkillProgramStepOutcome BeginPhaseStartCardControl(ProgramSkillFrame supplied, PhaseStartCardControlKind kind, string? grantedSkill)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.PhaseStartCardControl is not null || f.InstructionIndex != 1 || !HasPhaseStartControlBoundary(f.OwnerSeat, f.WindowContext, kind) ||
            !CanOfferPhaseStartCardControl(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), f.WindowContext!))
            throw new InvalidOperationException("Phase-starting card control requires its exact eligible owner and actual boundary.");
        var r = new PhaseStartCardControlReceipt(PhaseStartControlSource(f), f.GameplayHash, f.Id, f.InstructionIndex,
            f.WindowContext!.ParentFrameId, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId, kind,
            PhaseStartCardControlStage.Choosing, grantedSkill);
        ReplaceRuntimeTop(f = f with { PhaseStartCardControl = r });
        AdvanceEventRulesAndQueueFact(new PhaseStartCardControlIssuedEvent(r));
        PublishPhaseStartCardControlChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> PhaseStartCardControlChoices(ProgramSkillFrame f)
    {
        var r = f.PhaseStartCardControl!;
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, int? card = null, int? seat = null) => new(
            new($"phase-start-control.{f.Id}.{branch}.{card ?? -1}.{seat ?? -1}"), label,
            card is { } id ? [id] : [], seat is { } target ? [target] : [],
            new Dictionary<string, string> { ["program-action"] = "phase-start-card-control", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage != PhaseStartCardControlStage.Choosing) return [];
        if (r.Kind == PhaseStartCardControlKind.OtherPlayRestriction)
            choices.AddRange(OtherPlayRestrictionCosts(f.OwnerSeat).Select(card => Choice("pay", $"弃置【{card.DisplayName}】", card.Id, f.OwnerSeat)));
        else
            choices.AddRange(StartingEquipmentControlChoices(f.OwnerSeat).Select(option => Choice(option.Obtain ? "obtain" : "discard",
                $"{(option.Obtain ? "获得" : "弃置")} {_players[option.Seat].Name}的【{option.Card.DisplayName}】", option.Card.Id, option.Seat)));
        choices.Add(Choice("decline", "不执行"));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishPhaseStartCardControlChoice(ProgramSkillFrame f)
    {
        var choices = PhaseStartCardControlChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()),
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = f.PhaseStartCardControl!.Kind == PhaseStartCardControlKind.OtherPlayRestriction,
            Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("phase-start-card-control")]
    private void ResolvePhaseStartCardControlChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No phase-starting control frame.");
        if (!HasExactPhaseStartCardControl(f) || f.PhaseStartCardControl!.Stage != PhaseStartCardControlStage.Choosing ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !AssistedChoicesEqual([selected], PhaseStartCardControlChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Phase-starting control lost its exact unpaid choice and source.");
        var r = f.PhaseStartCardControl; var branch = selected.Parameters["branch"]; ClearPendingDecision();
        if (branch == "decline") { CompletePhaseStartCardControl(f); return; }
        var card = GetAttackCard(selected.Cards.Single()); var from = _cardZones.GetLocation(card.Id);
        var obtain = r.Kind == PhaseStartCardControlKind.StartingEquipment && branch == "obtain";
        var to = obtain ? CardLocation.Hand(f.OwnerSeat) : from.Zone == CardZoneKind.Equipment && card.IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile;
        var payment = new PhaseStartCardControlPayment(card.Id, card.Kind, EffectiveSuit(_players[from.OwnerSeat!.Value], card), from, to,
            _movementSequence, _movementSequence);
        ReplaceRuntimeTop(f = f with { PhaseStartCardControl = r with { Stage = PhaseStartCardControlStage.MovementChildren,
            Payment = payment, ArmorAcquired = obtain }, PendingMovementContinuation = new(from.OwnerSeat.Value, 0, null) });
        MoveProgramCardsFromMultipleSources([card.Id], to, new(PhaseStartControlReason(r.Kind)), (_, moves) =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { PhaseStartCardControl = current.PhaseStartCardControl! with
                { Payment = payment with { SequenceAfter = moves.Max(m => m.Sequence) } } });
            AdvanceEventRulesAndQueueFact(new PhaseStartCardControlPaidEvent(GetActiveProgramFrame(f.Id).PhaseStartCardControl!));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool HasExactPhaseStartCardControl(ProgramSkillFrame f)
    {
        if (f.PhaseStartCardControl is not { } r || r.ProgramFrameId != f.Id || r.InstructionIndex != 1 || f.InstructionIndex != 1 ||
            r.Source != PhaseStartControlSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } context || context.ParentFrameId != r.PhaseParentFrameId ||
            context.OwnerSeat != f.OwnerSeat || context.SourceSeat != r.ActualTurnOwnerSeat || context.TargetSeat != r.ActualTurnOwnerSeat ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId || _phase != TurnPhase.Play ||
            _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(p => p.Id == r.PhaseParentFrameId) is not { } parent ||
            parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate ||
            !MountObserverCandidateMatches(f, candidate) ||
            GetProgramTrigger(f).Effects is not [var effect] ||
            effect.Op != (r.Kind == PhaseStartCardControlKind.OtherPlayRestriction ? SkillProgramEffectOp.GrantOtherPlayUseRestriction : SkillProgramEffectOp.ChooseStartingEquipmentControl) ||
            (r.Kind == PhaseStartCardControlKind.OtherPlayRestriction ? effect.SkillIds is not [var granted] || granted != r.GrantedSkillId : r.GrantedSkillId is not null) ||
            CompleteProgramEventHistory().OfType<PhaseStartCardControlIssuedEvent>().Count(e => e.Receipt.ProgramFrameId == f.Id &&
                e.Receipt.Source == r.Source && e.Receipt.PhaseParentFrameId == r.PhaseParentFrameId && e.Receipt.Kind == r.Kind) != 1) return false;
        if (r.Payment is not { } p) return r.Stage == PhaseStartCardControlStage.Choosing && !r.ArmorAcquired && f.PendingMovementContinuation is null;
        return r.Stage is PhaseStartCardControlStage.MovementChildren or PhaseStartCardControlStage.GrantChildren &&
            p.SequenceAfter > p.SequenceBefore && p.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            (r.Kind == PhaseStartCardControlKind.OtherPlayRestriction ? p.From.OwnerSeat == f.OwnerSeat && !r.ArmorAcquired &&
                p.To == (p.Kind == CardKind.GeneralWeapon && p.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile) :
                p.From.Zone == CardZoneKind.Equipment && (r.ArmorAcquired ? p.To == CardLocation.Hand(f.OwnerSeat) && EquipmentCatalog.Get(p.Kind).Slot == EquipmentSlot.Armor :
                    p.From.OwnerSeat != f.OwnerSeat && p.To == (p.Kind == CardKind.GeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile))) &&
            _cardMovements.Count(m => m.CardId == p.CardId && m.CardKind == p.Kind && m.From == p.From && m.To == p.To &&
                m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.Reason.Value == PhaseStartControlReason(r.Kind)) == 1 &&
            CompleteProgramEventHistory().OfType<PhaseStartCardControlPaidEvent>().Count(e => e.Receipt.ProgramFrameId == f.Id &&
                e.Receipt.Source == r.Source && e.Receipt.Payment == p && e.Receipt.ArmorAcquired == r.ArmorAcquired) == 1;
    }
    private bool CanContinuePhaseStartCardControl(ProgramSkillFrame f) => f.PhaseStartCardControl is { Payment: not null } &&
        _winner == Winner.None && HasExactPhaseStartCardControl(f);
    private bool DrainPhaseStartCardControlChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 20600)]
    private bool ResumePhaseStartCardControl(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PhaseStartCardControl: { } r } f || f.Id != id) return false;
        if (!HasExactPhaseStartCardControl(f)) throw new InvalidOperationException("Phase-starting control lost its exact issuance or once-paid movement.");
        if (r.Stage == PhaseStartCardControlStage.Choosing) { if (_pendingDecision is null) PublishPhaseStartCardControlChoice(f); return true; }
        if (DrainPhaseStartCardControlChildren(f)) return true;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[r.ActualTurnOwnerSeat].IsAlive ||
            r.Kind == PhaseStartCardControlKind.StartingEquipment && !_players[f.OwnerSeat].IsAlive)
        { CompletePhaseStartCardControl(f); return true; }
        if (r.Stage == PhaseStartCardControlStage.MovementChildren)
        {
            ReplaceRuntimeTop(f = f with { PhaseStartCardControl = r with { Stage = PhaseStartCardControlStage.GrantChildren } });
            if (r.Kind == PhaseStartCardControlKind.OtherPlayRestriction) GrantOtherPlayRestriction(f);
            else if (r.ArmorAcquired) GrantStartingEquipmentSlashProhibition(f);
            AdvanceRuntimeProgram(id); return true;
        }
        CompletePhaseStartCardControl(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20600)]
    private bool ReturnPhaseStartCardControlMovement(ProgramSkillFrame frame)
    { if (frame.PhaseStartCardControl is null) return false; AdvanceRuntimeProgram(frame.Id); return true; }
    private void CompletePhaseStartCardControl(ProgramSkillFrame f)
    {
        var r = f.PhaseStartCardControl!;
        if (r.Kind == PhaseStartCardControlKind.StartingEquipment && r.Payment is { } p)
            AdvanceEventRulesAndQueueFact(new StartingEquipmentControlCompletedEvent(f.Id, r.PhaseParentFrameId, r.Source,
                r.ActualTurnNumber, r.ActualTurnOwnerSeat, p.CardId, p.From, p.To, r.ArmorAcquired, p.SequenceBefore, p.SequenceAfter));
        ReplaceRuntimeTop(f = f with { PhaseStartCardControl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(
        SkillProgramEffectOp.GrantOtherPlayUseRestriction,
        SkillProgramEffectOp.ChooseStartingEquipmentControl)]
    private PromptChoice SelectAiPhaseStartCardControl(PendingDecision decision, ProgramSkillFrame f) =>
        f.PhaseStartCardControl!.Kind == PhaseStartCardControlKind.OtherPlayRestriction
            ? decision.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.OwnerSeat])).ThenBy(c => c.Cards[0]).FirstOrDefault() ?? decision.Choices[^1]
            : decision.Choices.FirstOrDefault(c => c.Parameters["branch"] == "obtain") ?? decision.Choices[0];
}
