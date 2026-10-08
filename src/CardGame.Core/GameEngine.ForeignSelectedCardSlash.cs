using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ForeignSelectedCardSlashDrawReason = "skill-program.foreign-selected-card-slash.outcome-draw";
    private static CardConversionSource SelectedForeignSlashSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, frame.ActivationId!, frame.OwnerSeat, frame.SkillInstanceId);
    private bool SelectedForeignSlashEnded => _winner != Winner.None || _status == EngineStatus.Completed;

    private bool CanUseSelectedForeignSlashMaterial(int issuer, int actor, Card card, CardLocation from) =>
        IsValidPlayerSeat(issuer) && IsValidPlayerSeat(actor) && issuer != actor && _players[issuer].IsAlive && _players[actor].IsAlive &&
        from.OwnerSeat == actor && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment &&
        _cardZones.GetLocation(card.Id) == from && !IsTurnPhysicalUseForbidden(actor, [card.Id]) &&
        (from.Zone != CardZoneKind.Hand || !IsTurnHandCardRestricted(_players[actor], card)) &&
        !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
        CanUseSlashTarget(_players[actor], _players[issuer], card, effectiveKind: CardKind.Slash, ignoreDistance: true,
            physicalCardIds: [card.Id]) &&
        CanSpendSlashUse(_players[actor], _players[issuer], false, CardKind.Slash, card,
            excludedEquipmentId: from.Zone == CardZoneKind.Equipment ? card.Id : null, physicalCardIds: [card.Id]);

    private bool CanStartSelectedForeignCardSlash(CharacterState owner, int actorSeat) =>
        !SelectedForeignSlashEnded && _phase == TurnPhase.Play && owner.Seat == _currentSeat && owner.IsAlive &&
        IsValidPlayerSeat(actorSeat) && actorSeat != owner.Seat && _players[actorSeat].IsAlive &&
        new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }.Any(zone =>
            _cardZones.CardsAt(new(zone, actorSeat)).Any(card => CanUseSelectedForeignSlashMaterial(owner.Seat, actorSeat, card, new(zone, actorSeat))));

    private SkillProgramStepOutcome BeginSelectedForeignCardSlash(ProgramSkillFrame supplied, int actorSeat)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.ForeignSelectedCardSlash is not null || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 || !frame.SelectedTargetSeats.SequenceEqual([actorSeat]) ||
            frame.PendingMovementContinuation is not null || _cardUseDebitPhaseInstanceId <= 0 ||
            !IsSelectedForeignSlashActivation(frame))
            throw new InvalidOperationException("A selected foreign-card Slash requires its own zero-card activation and one exact selected actor.");
        if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !CanStartSelectedForeignCardSlash(_players[frame.OwnerSeat], actorSeat)) return SkillProgramStepOutcome.Continue;
        var receipt = new ForeignSelectedCardSlashReceipt(frame.InstructionIndex, SelectedForeignSlashSource(frame), frame.GameplayHash,
            _turnNumber, _cardUseDebitPhaseInstanceId, actorSeat, ForeignSelectedCardSlashStage.ChoosingCard);
        ReplaceRuntimeTop(frame = frame with { ForeignSelectedCardSlash = receipt });
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            receipt.ActualTurnNumber, receipt.PhaseInstanceId, actorSeat));
        PublishSelectedForeignCardSlash(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private bool IsSelectedForeignSlashActivation(ProgramSkillFrame frame)
    {
        if (frame.ActivationId is null || frame.TriggerId is not null || frame.WindowContext is not null ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        return plan.Activation is { MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
            TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null,
            MarkerCost: null, ContinueAfterOwnerDeath: false, Condition.Kind: SkillProgramConditionKind.Always } &&
            plan.Instructions is [{ Op: SkillProgramEffectOp.UseSelectedForeignCardAsSlash,
                Target: SkillProgramEffectTarget.SelectedTarget, Condition.Kind: SkillProgramConditionKind.Always }];
    }

    private IReadOnlyList<PromptChoice> SelectedForeignCardSlashChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.ForeignSelectedCardSlash ?? throw new InvalidOperationException("The selected foreign-card Slash lost its receipt.");
        if (receipt.Stage != ForeignSelectedCardSlashStage.ChoosingCard) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, receipt.ActorSeat,
            [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment], OwnedCardMoveIntent.Transfer,
            canSelect: (zone, card) => CanUseSelectedForeignSlashMaterial(frame.OwnerSeat, receipt.ActorSeat, card, new(zone, receipt.ActorSeat)))
            .Select(choice => choice with { Parameters = new Dictionary<string, string>(choice.Parameters)
                { ["program-action"] = "foreign-selected-slash" } }).ToArray());
    }

    private void PublishSelectedForeignCardSlash(ProgramSkillFrame frame)
    {
        var choices = SelectedForeignCardSlashChoices(frame);
        if (choices.Count == 0) { FinishSelectedForeignCardSlash(frame); return; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "选择该角色区域内的一张牌，令其当无距离限制的【杀】对你使用。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.ForeignSelectedCardSlash!.ActorSeat,
            Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private void ResolveSelectedForeignCardSlashChoice(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || !ValidSelectedForeignCardSlash(frame) ||
            frame.ForeignSelectedCardSlash is not { Stage: ForeignSelectedCardSlashStage.ChoosingCard } receipt ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([selected], SelectedForeignCardSlashChoices(frame).Where(choice => choice.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The selected foreign-card Slash changed its owning chooser or published opaque entity slot.");
        if (!Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), CultureInfo.InvariantCulture, out var slot))
            throw new InvalidOperationException("The selected foreign-card Slash lost its exact source region.");
        var from = new CardLocation(zone, receipt.ActorSeat);
        var cards = _cardZones.CardsAt(from);
        if (slot < 0 || slot >= cards.Count) throw new InvalidOperationException("The selected foreign-card Slash slot no longer exists.");
        var card = cards[slot];
        if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !CanUseSelectedForeignSlashMaterial(frame.OwnerSeat, receipt.ActorSeat, card, from))
            throw new InvalidOperationException("The selected foreign-card Slash is no longer a legal actual Use.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame = frame with { ForeignSelectedCardSlash = receipt with
        { Stage = ForeignSelectedCardSlashStage.IssuingUse, CardId = card.Id, PrintedKind = card.Kind, From = from,
            GeneralWeapon = card.IsGeneralWeapon, PaymentBefore = _movementSequence } });
        ResolveSlashCore(_players[receipt.ActorSeat], _players[frame.OwnerSeat], card, CardKind.Slash, receipt.ActorSeat,
            physicalCards: [card], countsTowardSlashLimit: true, conversionSource: receipt.Source,
            programSkillCardUseFrameId: frame.Id, physicalSourceLocation: from);
    }

    private bool IsSelectedForeignCardSlashSource(long? parentId, int actor, int target, Card card, CardKind kind,
        int physicalOwner, CardLocation from, IReadOnlyList<Card>? materials, bool countsTowardLimit) =>
        parentId is { } id && _resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == id &&
        frame.ForeignSelectedCardSlash is { Stage: ForeignSelectedCardSlashStage.IssuingUse } receipt &&
        receipt.InstructionIndex == frame.InstructionIndex && receipt.Source == SelectedForeignSlashSource(frame) &&
        receipt.GameplayHash == frame.GameplayHash && IsSelectedForeignSlashActivation(frame) &&
        actor == receipt.ActorSeat && physicalOwner == actor && target == frame.OwnerSeat && kind == CardKind.Slash &&
        countsTowardLimit && materials is [var material] && material == card && card.Id == receipt.CardId &&
        card.Kind == receipt.PrintedKind && from == receipt.From && card.IsGeneralWeapon == receipt.GeneralWeapon &&
        frame.SelectedTargetSeats.SequenceEqual([actor]) && CanUseSelectedForeignSlashMaterial(target, actor, card, from);

    private void BindSelectedForeignCardSlashUse(long useId, long? parentId)
    {
        if (parentId is not { } id || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is not { ForeignSelectedCardSlash: { } receipt } frame) return;
        if (receipt.Stage != ForeignSelectedCardSlashStage.IssuingUse || receipt.SlashReturn is not null ||
            LifecycleCardUse(useId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActorSeat != receipt.ActorSeat || action.ProviderSeat != receipt.ActorSeat || action.RequesterSeat is not null ||
            action.ResponderSeat is not null || action.OpponentSeat is not null || action.EffectiveKind != CardKind.Slash ||
            !action.TargetSeats.SequenceEqual([frame.OwnerSeat]) || action.PhysicalCards is not [var cost] ||
            cost.CardId != receipt.CardId || cost.CardKind != receipt.PrintedKind || cost.From != receipt.From ||
            !action.ConversionChain.SequenceEqual([receipt.Source]) || use.CardId != receipt.CardId ||
            !use.PhysicalCardIds!.SequenceEqual([cost.CardId]))
            throw new InvalidOperationException("The selected foreign-card Slash lost its original native actor, provider or HEJ material.");
        var returned = new ForeignSelectedCardSlashReturn(frame.Id, frame.InstructionIndex, receipt.Source, receipt.GameplayHash,
            receipt.ActualTurnNumber, receipt.PhaseInstanceId, receipt.ActorSeat, frame.OwnerSeat,
            cost.CardId, cost.CardKind, cost.From, receipt.GeneralWeapon, useId, action.ActionId);
        ReplaceRuntimeFrame(frame.Id, frame with { ForeignSelectedCardSlash = receipt with
        { Stage = ForeignSelectedCardSlashStage.UseIssued, SlashReturn = returned } });
        UpdateLifecycleCardUse(useId, current => current with { ForeignSelectedCardSlashReturn = returned });
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashIssuedEvent(returned));
    }

    private void CompleteSelectedForeignCardSlashPayment(long useId)
    {
        if (LifecycleCardUse(useId)?.ForeignSelectedCardSlashReturn is not { } returned) return;
        var frame = _resolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == returned.ProgramFrameId);
        var receipt = frame.ForeignSelectedCardSlash!;
        if (receipt.Stage != ForeignSelectedCardSlashStage.UseIssued || receipt.SlashReturn != returned || receipt.PaymentBatchId is not null)
            throw new InvalidOperationException("The selected foreign-card Slash cannot pay its material twice.");
        var paid = _cardMovements.Where(m => m.Sequence > receipt.PaymentBefore && m.CardId == returned.CardId &&
            m.From == returned.From && m.To == (returned.GeneralWeapon ? CardLocation.OutsideGame : CardLocation.Processing) && m.Reason == CardMoveReasons.Use).ToArray();
        if (paid is not [var movement]) throw new InvalidOperationException("The selected foreign-card Slash lost its exact actual material payment.");
        var batch = _pendingCardsMovedBatches.Single(b => b.ParentFrameId == useId && b.Movements.Contains(movement));
        ReplaceRuntimeFrame(frame.Id, frame with { ForeignSelectedCardSlash = receipt with
        { PaymentAfter = _movementSequence, PaymentBatchId = batch.Id } });
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashPaidEvent(frame.Id, useId, returned.CardId, returned.PrintedKind,
            returned.From, movement.To, receipt.PaymentBefore, _movementSequence, batch.Id));
    }

    private void CompleteSelectedForeignCardSlash(AttackCompletionReceipt completion)
    {
        var returned = completion.ForeignSelectedCardSlashReturn ?? throw new InvalidOperationException("The selected foreign-card Slash lost its typed completion.");
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != returned.ProgramFrameId ||
            completion.ProgramFrameId != frame.Id || completion.ResolutionId != returned.CardUseFrameId ||
            frame.ForeignSelectedCardSlash is not { Stage: ForeignSelectedCardSlashStage.UseIssued } receipt || receipt.SlashReturn != returned ||
            !ValidSelectedForeignCardSlash(frame) || LifecycleCardUse(returned.CardUseFrameId) is not null ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == returned.CardUseFrameId) != 1)
            throw new InvalidOperationException("The selected foreign-card Slash lost its retired whole Use or once-paid issuer return.");
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashResolvedEvent(frame.Id, returned.CardUseFrameId, returned.ActionId, receipt.DamagedIssuer));
        ReplaceRuntimeTop(frame with { ForeignSelectedCardSlash = receipt with { Stage = ForeignSelectedCardSlashStage.SettlementChildren },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool ResumeSelectedForeignCardSlash(ProgramSkillFrame frame)
    {
        if (frame.ForeignSelectedCardSlash is not { Stage: ForeignSelectedCardSlashStage.SettlementChildren or ForeignSelectedCardSlashStage.DrawChildren } receipt) return false;
        if (!ValidSelectedForeignCardSlash(frame) || _resolutionStack.LastOrDefault()?.Id != frame.Id)
            throw new InvalidOperationException("The selected foreign-card Slash resumed outside its own paid tail.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return true;
        frame = GetActiveProgramFrame(frame.Id); receipt = frame.ForeignSelectedCardSlash!;
        if (receipt.Stage == ForeignSelectedCardSlashStage.DrawChildren || !_players[frame.OwnerSeat].IsAlive || SelectedForeignSlashEnded)
        { FinishSelectedForeignCardSlash(frame); return true; }
        var requested = receipt.DamagedIssuer ? 2 : 1;
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { ForeignSelectedCardSlash = receipt with
            { Stage = ForeignSelectedCardSlashStage.DrawChildren, RequestedDraw = requested, DrawBefore = before, DrawAfter = before } });
        var actual = DrawCards(_players[frame.OwnerSeat], requested, true, new(ForeignSelectedCardSlashDrawReason)).Count;
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { ForeignSelectedCardSlash = frame.ForeignSelectedCardSlash! with
            { ActualDraw = actual, DrawAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashDrawIssuedEvent(frame.Id, receipt.SlashReturn!.CardUseFrameId,
            requested, actual, before, _movementSequence));
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private bool ResumeSelectedForeignCardSlash(long id) => _resolutionStack.LastOrDefault() is ProgramSkillFrame frame &&
        frame.Id == id && ResumeSelectedForeignCardSlash(frame);

    private bool ReturnSelectedForeignCardSlashMovement(ProgramSkillFrame frame)
    {
        if (frame.ForeignSelectedCardSlash is not { Stage: ForeignSelectedCardSlashStage.SettlementChildren or ForeignSelectedCardSlashStage.DrawChildren }) return false;
        if (!ValidSelectedForeignCardSlash(frame)) throw new InvalidOperationException("The selected foreign-card Slash movement returned to a different invoice.");
        AdvanceRuntimeProgram(frame.Id); return true;
    }

    private void FinishSelectedForeignCardSlash(ProgramSkillFrame frame)
    {
        AdvanceEventRulesAndQueueFact(new ForeignSelectedCardSlashFinishedEvent(frame.Id, frame.ForeignSelectedCardSlash?.SlashReturn is not null));
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { ForeignSelectedCardSlash = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
    }

    private PromptChoice SelectAiSelectedForeignCardSlashChoice(PendingDecision decision, ProgramSkillFrame frame) =>
        decision.Choices.OrderByDescending(choice => choice.Cards.Count == 0 ? 0 : CardCatalog.Get(GetAttackCard(choice.Cards.Single()).Kind).HandKeepValue)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First(); // Foreign Hand choices remain opaque and equally scored.

    private sealed partial class ProgramSkillHost : ISelectedForeignCardSlashHost
    {
        public SkillProgramStepOutcome UseSelectedForeignCardAsSlash(ProgramSkillFrame frame, int actorSeat) => engine.BeginSelectedForeignCardSlash(frame, actorSeat);
        public bool CanContinueSelectedForeignCardSlash(ProgramSkillFrame frame) => engine.CanContinueSelectedForeignCardSlash(frame);
    }
}
