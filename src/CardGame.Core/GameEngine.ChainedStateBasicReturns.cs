namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ExecuteChainedStateBasicPaid(ChainedStateBasicFrame frame)
    {
        var p = frame.Payment; var actor = _players[p.Source.OwnerSeat];
        if (p.Intent == ChainedStateBasicIntent.OwnSlashDodge) { ResolveChainedStateBasicDodge(p); return; }
        if (p.Intent == ChainedStateBasicIntent.Play) { BeginChainedStateBasicSlash(actor, frame.TargetSeats, p, null, true, null); return; }
        if (p.Intent == ChainedStateBasicIntent.BorrowedSword)
        {
            var borrowed = ActiveBorrowedSword!; PopResponseWindow(borrowed.ResolutionId);
            SetCardUseStep(borrowed.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            borrowed.AwaitingSlashChoice = false; borrowed.SlashCardId = 0; borrowed.EffectiveSlashKind = p.EffectiveKind;
            BeginChainedStateBasicSlash(actor, frame.TargetSeats, p, borrowed, true, null); return;
        }
        if (p.Intent == ChainedStateBasicIntent.Qinglong)
        {
            var original = ActiveQinglongCrescentBlade!.Attack; ActiveQinglongCrescentBlade = null;
            SetCardUseStep(original.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(original.ResolutionId, actor.Seat, p.TargetSeat!.Value,
                Used: true, SlashCardIds: [], EffectiveSlashKind: p.EffectiveKind));
            CompleteAttack(original); SuspendContinuationForQinglongFollowup(original.ResolutionId);
            BeginChainedStateBasicSlash(actor, frame.TargetSeats, p, null, false, null); return;
        }
        var parent = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A paid chained-state Slash lost its request parent.");
        if (!ChainedStateBasicProgramActor(parent, p)) throw new InvalidOperationException("A paid chained-state Slash changed its original actor or instruction.");
        var effect = ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!).GetPausedInstruction(parent.InstructionIndex).Effect;
        if (p.Intent == ChainedStateBasicIntent.ProgramSlash)
            CommitProgramChoiceResult(parent.Id, effect.ResultBind!, RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption, actor.Seat, "解除连环后视为使用【杀】。");
        else if (p.Intent == ChainedStateBasicIntent.AssistedSlash)
        { ReplaceRuntimeTop(parent with { AssistedSlashRequest = null }); CommitProgramChoiceResult(parent.Id, effect.ResultBind!, "used-slash", actor.Seat, "解除连环后视为使用【杀】。"); }
        else if (p.Intent == ChainedStateBasicIntent.NearestLegalSlash) ReplaceRuntimeTop(parent with { NearestLegalSlashRequest = null });
        else if (p.Intent != ChainedStateBasicIntent.ProgramNearestSlash) throw new InvalidOperationException("A paid chained-state Slash has an unknown return.");
        BeginChainedStateBasicSlash(actor, frame.TargetSeats, p, null, false, parent.Id);
    }
    private void BeginChainedStateBasicSlash(CharacterState actor, IReadOnlyList<int> targets, ChainedStateBasicPayment payment,
        BorrowedSwordHandle? borrowed, bool countsTowardLimit, long? programParent)
    {
        if (!IsSlashCard(payment.EffectiveKind) || targets.Count == 0) throw new InvalidOperationException("A chained-state Slash requires its accepted actual targets.");
        var kind = payment.EffectiveKind;
        var id = BeginCardUse(new(0, kind, Suit.None, 0), actor.Seat, targets, kind,
            ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], kind), physicalCardIds: [], conversionSource: payment.Source, chainedStatePayment: payment);
        var actualTargets = LifecycleCardUse(id)!.TargetSeats;
        var attack = new CardAttackHandle(this, id, actor.Seat, actualTargets[0], card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: kind,
            ignoresArmor: HasCardArmorBypass(actor, _players[actualTargets[0]], kind), programSkillCardUseFrameId: programParent);
        if (borrowed is not null) borrowed.ActiveAttack = attack;
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        if (countsTowardLimit && _phase == TurnPhase.Play && actor.Seat == _currentSeat && LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, payment.Source))
            RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, actualTargets[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[actualTargets[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
            actualTargets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private CardActionContext PrepareChainedStateBasicAction(CardActionContext action, ChainedStateBasicPayment? payment) => payment is null ? action :
        CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards,
            action.ConversionChain, action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: false));
    private void IssueChainedStateBasicUse(CardUseFrame use, ChainedStateBasicPayment? payment)
    {
        if (payment is not { } p) return;
        if (!ValidChainedStateBasicPayment(p) || p.Intent == ChainedStateBasicIntent.OwnSlashDodge ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } action ||
            use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || action.ActorSeat != p.Source.OwnerSeat || action.ProviderSeat != action.ActorSeat ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.EffectiveKind != p.EffectiveKind || use.CardKind != p.EffectiveKind || action.ConversionChain is not [var source] || source != p.Source ||
            action.TargetSeats.Count == 0 || action.TargetSeats.Distinct().Count() != action.TargetSeats.Count || !action.TargetSeats.SequenceEqual(use.TargetSeats))
            throw new InvalidOperationException("A chained-state Use lost its exact paid source, neutral zero material or native targets.");
        var receipt = new ChainedStateBasicUseReceipt(p, use.Id, action.ActionId);
        ReplaceRuntimeFrame(use.Id, use with { ChainedStateBasicUse = receipt }); EmitChainedStateBasicIssued(receipt);
    }
    private void EmitChainedStateBasicIssued(ChainedStateBasicUseReceipt receipt)
    {
        if (CompleteProgramEventHistory().OfType<ChainedStateBasicIssuedEvent>().Any(e => e.Receipt.Payment.PaymentFrameId == receipt.Payment.PaymentFrameId))
            throw new InvalidOperationException("One chained-state payment cannot issue two basic Uses.");
        AdvanceEventRulesAndQueueFact(new ChainedStateBasicIssuedEvent(receipt));
    }
    private bool IsChainedStateBasicIssued(ChainedStateBasicUseReceipt receipt) => ValidChainedStateBasicPayment(receipt.Payment) &&
        CompleteProgramEventHistory().OfType<ChainedStateBasicIssuedEvent>().Where(e => e.Receipt.Payment.PaymentFrameId == receipt.Payment.PaymentFrameId).ToArray() is [var fact] && fact.Receipt == receipt;
    private bool IsIssuedChainedStateBasicUse(CardUseFrame use)
    {
        if (use.ChainedStateBasicUse is not { } r || r.OwnerFrameId != use.Id || !IsChainedStateBasicIssued(r) || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use } current) return false;
        var action = use.CurrentSlashFirePolicy is { } fire ? fire.OriginalAction : current;
        if (use.CurrentSlashFirePolicy is not null) AssertCurrentSlashFirePolicy(use);
        var p = r.Payment;
        return action.ActionId == r.CardActionId && action.EffectiveKind == p.EffectiveKind && IsSlashCard(p.EffectiveKind) &&
            (use.CardKind == p.EffectiveKind || use.CurrentSlashFirePolicy is not null && IsSlashCard(use.CardKind)) &&
            action.ProviderSeat == p.Source.OwnerSeat && action.RequesterSeat is null && action.ResponderSeat is null && action.OpponentSeat is null &&
            action.PhysicalCards.Count == 0 && action.ConversionChain is [var source] && source == p.Source && action.EffectiveSuit == Suit.None &&
            action.EffectiveRank == 0 && action.EffectiveIsRed == false && current.TargetSeats.SequenceEqual(use.TargetSeats) &&
            (action.ActorSeat == p.Source.OwnerSeat && use.SourceSeat == action.ActorSeat || MatchesDeclaredActualDamageUse(use, action.ActorSeat, p.Source.OwnerSeat));
    }
    private bool IsChainedStateBasicUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedChainedStateBasicUse(use);
    private Card ChainedStateBasicRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedChainedStateBasicUse(use)
        ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("A chained-state logical card lost its unique paid receipt.");
    private Card ReadChainedStateBasicUseAppearance(long id, CardAppearanceReference appearance) => appearance.Id == 0 && IsChainedStateBasicUse(id) &&
        LifecycleCardUse(id)!.CardKind == appearance.Kind && appearance.Suit == Suit.None && appearance.Rank == 0
        ? ChainedStateBasicRepresentation(id) : ReadDrawFundedDistinctBasicUseAppearance(id, appearance);
    private bool SkipChainedStateBasicFinishedMovement(long id, Card card)
    {
        if (card.Id != 0 || !IsChainedStateBasicUse(id)) return false;
        if (card.Kind != LifecycleCardUse(id)!.CardKind) throw new InvalidOperationException("A chained-state logical completion changed its card name.");
        return true;
    }
    private bool FinishChainedStateBasicAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsChainedStateBasicUse(attack.ResolutionId) || !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats,
            ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    private void ResolveChainedStateBasicDodge(ChainedStateBasicPayment p)
    {
        var actor = _players[p.Source.OwnerSeat]; var attack = ActiveCardAttack ?? throw new InvalidOperationException("A chained-state Dodge lost its original Slash.");
        var use = LifecycleCardUse(attack.ResolutionId)!;
        if (!ValidChainedStateBasicPayment(p) || p.Intent != ChainedStateBasicIntent.OwnSlashDodge || p.ParentFrameId != use.Id ||
            p.ParentActionId != use.Action?.ActionId || p.Cursor != attack.SuccessfulDodgeResponses || actor.Seat != attack.TargetSeat ||
            ActiveFactionDefense is not null || ActiveGroupCard is not null || !IsProgramResponseCardUse(actor, CardKind.Dodge))
            throw new InvalidOperationException("A paid chained-state Dodge changed its original native Slash-defense direction.");
        PopResponseWindow(attack.ResolutionId); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, use.Action?.ActionId, CardActionType.Response,
            actor.Seat, actor.Seat, null, actor.Seat, attack.SourceSeat, CardKind.Dodge, [], [], [p.Source], effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: false));
        var receipt = new ChainedStateBasicUseReceipt(p, use.Id, action.ActionId);
        ReplaceRuntimeFrame(use.Id, LifecycleCardUse(use.Id)! with { ChainedStateBasicResponse = receipt }); EmitChainedStateBasicIssued(receipt);
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Dodge); RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, attack.SourceSeat, CardKind.Dodge));
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        if (TryBeginCommittedResponseUsePrograms(attack, action, ProgramCardContinuation.Dodge)) return;
        if (TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.Dodge)) return;
        if (HasResponseUseCompletionObserver(action, ProgramCardContinuation.Dodge) || HasCardResponseCompletedObserver(action, ProgramCardContinuation.Dodge))
            ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.Dodge);
        else CompleteSuccessfulDodgeResponse(attack);
    }
    private CardActionContext RequireChainedStateBasicResponseAction(CardUseFrame use)
    {
        var receipt = use.ChainedStateBasicResponse ?? throw new InvalidOperationException("A paid chained-state Dodge lost its owning receipt.");
        var p = receipt.Payment;
        if (CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == receipt.CardActionId).ToArray() is not [var accepted] ||
            !IsChainedStateBasicIssued(receipt) || receipt.OwnerFrameId != use.Id || p.ParentFrameId != use.Id || p.Intent != ChainedStateBasicIntent.OwnSlashDodge ||
            use.CardAttack is not { } attack || !IsSlashCard(use.CardKind) || attack.TargetSeat != p.Source.OwnerSeat || attack.SuccessfulDodgeResponses != p.Cursor || p.TargetSeat != attack.SourceSeat ||
            accepted.Action is not { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } action ||
            action.ParentActionId != use.Action?.ActionId || action.ParentActionId != p.ParentActionId || action.ActorSeat != p.Source.OwnerSeat || action.ProviderSeat != action.ActorSeat ||
            action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null || action.OpponentSeat != attack.SourceSeat ||
            action.ConversionChain is not [var source] || source != p.Source || action.TargetSeats.Count != 0)
            throw new InvalidOperationException("A chained-state Dodge changed its unique accepted action, native owner or paid response cursor.");
        return action;
    }
    private void ClearChainedStateBasicDodgeResponse(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { ChainedStateBasicResponse: not null } use) return;
        var action = RequireChainedStateBasicResponseAction(use);
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == action.ActionId) ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null)
            throw new InvalidOperationException("A chained-state Dodge cannot return before its exact native children.");
        EmitChainedStateBasicReturned(use.ChainedStateBasicResponse!); ReplaceRuntimeFrame(use.Id, use with { ChainedStateBasicResponse = null });
    }
    private bool ChainedStateBasicProgramActor(ProgramSkillFrame parent, ChainedStateBasicPayment p)
    {
        if (parent.Id != p.ParentFrameId || parent.Id != p.RequestFrameId || parent.InstructionIndex != p.Cursor || parent.InstructionIndex < 1 ||
            _contentRegistry.Skills.GetValueOrDefault(parent.SkillId)?.Program is not { } definition || definition.GameplayHash != parent.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, definition);
        if (parent.InstructionIndex > plan.Instructions.Count) return false;
        var op = plan.GetPausedInstruction(parent.InstructionIndex).Effect.Op;
        if (p.Intent == ChainedStateBasicIntent.ProgramNearestSlash)
        {
            var cursors = parent.NumberBindings.Where(b => b.Name == "participant-cursor-" + (parent.InstructionIndex - 1)).ToArray();
            return op == SkillProgramEffectOp.RequestSlashByNearest && parent.ReexecuteParticipantInstruction && cursors is [var cursor] &&
                cursor.Value >= 1 && cursor.Value < _playerCount && p.Source.OwnerSeat == (parent.OwnerSeat + cursor.Value) % _playerCount;
        }
        return parent.SelectedTargetSeats.SequenceEqual([p.Source.OwnerSeat]) && (p.Intent switch
        {
            ChainedStateBasicIntent.ProgramSlash => op == SkillProgramEffectOp.RequestSlashByTarget,
            ChainedStateBasicIntent.AssistedSlash => op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget,
            ChainedStateBasicIntent.NearestLegalSlash => op == SkillProgramEffectOp.RequestLegalSlashByNearest,
            _ => false
        });
    }
    private bool IsChainedStateBasicProgramSelection(ProgramSkillFrame parent, SkillProgramEffect effect, ChainedStateBasicFrame paid)
    {
        var p = paid.Payment;
        if (!ValidChainedStateBasicPayment(p) || !ChainedStateBasicProgramActor(parent, p) || paid.OriginalDecision.PlayerSeat != p.Source.OwnerSeat ||
            paid.OriginalDecision.TargetSeat != p.Source.OwnerSeat || paid.OriginalDecision.PromptId != p.OriginalPromptId || paid.OriginalDecision.Revision != p.OriginalRevision ||
            ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!).GetPausedInstruction(parent.InstructionIndex).Effect != effect) return false;
        if (p.Intent != ChainedStateBasicIntent.ProgramNearestSlash) return true;
        var native = ChainedStateBasicNativeDecision(DrawFundedDistinctBasicNativeDecision(RequestedDeckBasicNativeDecision(paid.OriginalDecision)));
        return native.Kind == DecisionKind.ProgramTrigger && native.Choices.Count > 0 && native.Choices.All(c =>
            c.Parameters.GetValueOrDefault("frame-id") == parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
            c.Parameters.GetValueOrDefault("seat") == p.Source.OwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
            native.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash-nearest-decline");
    }
    private void AssertChainedStateBasicUseCompletion(CardUseFrame use)
    {
        if (use.ChainedStateBasicUse is null && use.ChainedStateBasicResponse is null) return;
        if (use.ChainedStateBasicUse is not { } r || use.ChainedStateBasicResponse is not null || !IsIssuedChainedStateBasicUse(use) ||
            use.Step != ResolutionFrameStep.Completed || _resolutionStack.LastOrDefault()?.Id != use.Id || use.PendingRecoveryAttempts is { Count: > 0 } ||
            use.RecoveryPaidContinuation is not null || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.ParentFrameId == use.Id))
            throw new InvalidOperationException("A chained-state Use cannot finish before its original native children return.");
        if (r.Payment.Intent is ChainedStateBasicIntent.ProgramSlash or ChainedStateBasicIntent.ProgramNearestSlash or ChainedStateBasicIntent.AssistedSlash or ChainedStateBasicIntent.NearestLegalSlash &&
            (_resolutionStack.SingleOrDefault(f => f.Id == r.Payment.ParentFrameId) is not ProgramSkillFrame parent || !ChainedStateBasicProgramActor(parent, r.Payment) ||
             use.CardAttack?.ProgramSkillCardUseFrameId != parent.Id))
            throw new InvalidOperationException("A chained-state Slash lost its exact original program return.");
    }
    private void ReturnChainedStateBasicUse(CardUseFrame completed)
    {
        if (completed.ChainedStateBasicUse is not { } receipt) return;
        if (_resolutionStack.Any(f => f.Id == completed.Id) || !IsChainedStateBasicIssued(receipt)) throw new InvalidOperationException("A chained-state return must follow native owner completion.");
        EmitChainedStateBasicReturned(receipt);
    }
    private void EmitChainedStateBasicReturned(ChainedStateBasicUseReceipt r)
    {
        if (CompleteProgramEventHistory().OfType<ChainedStateBasicReturnedEvent>().Any(e => e.Receipt.Payment.PaymentFrameId == r.Payment.PaymentFrameId))
            throw new InvalidOperationException("A chained-state Use cannot return twice.");
        AdvanceEventRulesAndQueueFact(new ChainedStateBasicReturnedEvent(r));
    }
    private bool IsChainedStateBasicAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !IsIssuedChainedStateBasicUse(use) || attack.Card is not null ||
            attack.PhysicalCards.Count != 0 || attack.EffectiveCardKind != use.CardKind || attack.SourceSeat != use.SourceSeat) return false;
        var p = use.ChainedStateBasicUse!.Payment; int[] parentIds = [];
        if (p.Intent == ChainedStateBasicIntent.BorrowedSword)
        {
            if (ActiveBorrowedSword is not { ActiveAttack: { } child } b || !SameAttackOwner(child, attack) || b.ResolutionId != p.ParentFrameId ||
                b.WeaponOwnerSeat != p.Source.OwnerSeat || b.SlashTargetSeat != p.TargetSeat || use.Action?.ParentActionId != LifecycleCardUse(b.ResolutionId)?.Action?.ActionId) return false;
            parentIds = GetCardUsePhysicalCards(b.ResolutionId).Select(c => c.Id).ToArray();
        }
        else if (p.Intent == ChainedStateBasicIntent.Qinglong)
        {
            if (ActiveQinglongFollowup is not { } q || q.OuterResolutionId != p.ParentFrameId ||
                CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().LastOrDefault(e => e.ResolutionId == p.ParentFrameId) is not
                    { Used: true, SlashCardIds.Count: 0 } fact || fact.SourceSeat != p.Source.OwnerSeat || fact.TargetSeat != p.TargetSeat || fact.EffectiveSlashKind != p.EffectiveKind) return false;
            parentIds = q.NextAttack?.PhysicalCards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing).Select(c => c.Id).ToArray() ?? [];
        }
        else if (p.Intent is ChainedStateBasicIntent.ProgramSlash or ChainedStateBasicIntent.ProgramNearestSlash or ChainedStateBasicIntent.AssistedSlash or ChainedStateBasicIntent.NearestLegalSlash)
        {
            if (attack.ProgramSkillCardUseFrameId != p.ParentFrameId || _resolutionStack.SingleOrDefault(f => f.Id == p.ParentFrameId) is not ProgramSkillFrame parent || !ChainedStateBasicProgramActor(parent, p)) return false;
            parentIds = _resolutionStack.Take(_resolutionStack.FindIndex(f => f.Id == parent.Id)).OfType<CardUseFrame>().SelectMany(f => f.PhysicalCardIds ?? [])
                .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().ToArray();
        }
        else if (p.Intent != ChainedStateBasicIntent.Play || attack.ProgramSkillCardUseFrameId is not null) return false;
        return parentIds.All(id => processing.Any(c => c.Id == id)) && processing.Select(c => c.Id).Order().SequenceEqual(parentIds.Order());
    }

    private bool ChainedStateBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) => child is ChainedStateBasicFrame paid &&
        paid.Payment.RequestFrameId == parent.Id && paid.Payment.ParentFrameId is { } owner && _resolutionStack.Any(f => f.Id == owner) &&
        paid.OriginalDecision.PlayerSeat == paid.Payment.Source.OwnerSeat && paid.OriginalDecision.PromptId == paid.Payment.OriginalPromptId &&
        paid.OriginalDecision.Revision == paid.Payment.OriginalRevision && ValidChainedStateBasicPayment(paid.Payment);

    // The native Qinglong suspension may retain the first Slash's precise
    // completion candidate below this family's accepted follow-up Use.
    private bool IsChainedStateQinglongCompletedWindowRide(ProgramCardTriggerWindowFrame window)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (index < 1 || index + 1 >= _resolutionStack.Count || _resolutionStack[index - 1] is not CardUseFrame original ||
            window.Continuation != ProgramCardContinuation.CompletedSlash || window.ParentFrameId != original.Id || window.AttackOwnerFrameId != original.Id ||
            original.Step != ResolutionFrameStep.Completed || original.Action is not { Type: CardActionType.Use } action || window.Action.ActionId != action.ActionId ||
            !IsSlashCard(original.CardKind) || original.CardAttack is not { } attack || attack.SuccessfulDodgeResponses != attack.RequiredDodgeResponses ||
            original.Continuations.QinglongFollowup is not { Active: true, Decision: { } saved } suspension ||
            suspension.NextAttackOwnerId != original.Id || suspension.FangtianOwnerId is not null || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var bound = _resolutionStack[index + 1] as ProgramSkillFrame;
        var next = index + (bound is null ? 1 : 2);
        if (next >= _resolutionStack.Count || _resolutionStack[next] is not CardUseFrame followup || window.Activated != (bound is not null) ||
            !IsIssuedChainedStateBasicUse(followup) || followup.ChainedStateBasicUse!.Payment is not { Intent: ChainedStateBasicIntent.Qinglong, Cursor: 0 } p ||
            p.ParentFrameId != original.Id || p.RequestFrameId != original.Id || p.ParentActionId != action.ActionId || p.Source.OwnerSeat != original.SourceSeat ||
            p.TargetSeat != attack.TargetSeat || followup.SourceSeat != original.SourceSeat || followup.Action!.ParentActionId != action.ActionId ||
            !followup.TargetSeats.SequenceEqual([attack.TargetSeat]) || !followup.Action.TargetSeats.SequenceEqual(followup.TargetSeats) || !IsSlashCard(followup.CardKind)) return false;
        var candidate = window.Candidates[window.CandidateIndex]; var context = candidate.FrozenContext;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } cardUse } || context.ParentFrameId != window.Id ||
            cardUse.ParentCardUseFrameId != original.Id || cardUse.CardActionId != action.ActionId || CreateCardActionProgramContext(window, candidate) != context ||
            saved.Kind != DecisionKind.ProgramTrigger || !saved.IsPrivate || saved.SkillPrompt?.SkillId != candidate.SkillId || saved.Choices.Count == 0) return false;
        if (bound is null)
        {
            if (!GetProgramTrigger(ToSharedCandidate(candidate)).Optional || saved.PlayerSeat != (context.OptionalChooserSeat ?? candidate.OwnerSeat) ||
                saved.SourceSeat != (context.OptionalChooserSeat is not null ? candidate.OwnerSeat : context.SourceSeat) ||
                saved.TargetSeat != (context.OptionalChooserSeat ?? context.TargetSeat ?? candidate.OwnerSeat) || saved.Choices.Count != 2 ||
                !saved.Choices.Select(c => c.Parameters.GetValueOrDefault("program-action")).Order().SequenceEqual(["activate", "skip"]) ||
                saved.Choices.Any(c => c.Cards.Count != 0 || c.Targets.Count != 0 || c.Parameters.GetValueOrDefault("skill-id") != candidate.SkillId ||
                    c.Parameters.GetValueOrDefault("binding-id") != candidate.TriggerId || c.Parameters.GetValueOrDefault("skill-instance-id") != candidate.SkillInstanceId)) return false;
        }
        else
        {
            if (bound.WindowContext != context || bound.OwnerSeat != candidate.OwnerSeat || bound.SkillId != candidate.SkillId ||
                bound.SkillInstanceId != candidate.SkillInstanceId || bound.TriggerId != candidate.TriggerId || bound.GameplayHash != candidate.GameplayHash || bound.InstructionIndex < 1) return false;
            var effect = ProgramInstructionResolver.Default.Resolve(bound, _contentRegistry.GetSkill(bound.SkillId).Program!).GetPausedInstruction(bound.InstructionIndex).Effect;
            if (effect.Op != SkillProgramEffectOp.ChooseOption || bound.ChoiceBindings.Any(b => b.Name == effect.ResultBind) ||
                saved.PlayerSeat != (effect.ChooserRef is { } chooser ? ResolveProgramParticipant(bound, chooser) : ResolveProgramEffectTarget(bound, effect.Target)) ||
                saved.SourceSeat != bound.OwnerSeat || saved.TargetSeat != saved.PlayerSeat || saved.Choices.Select(c => c.Id).Distinct().Count() != saved.Choices.Count ||
                saved.Choices.Any(c => c.Cards.Count != 0 || c.Targets.Count != 0 || c.Parameters.GetValueOrDefault("program-action") != "choose-option" ||
                    c.Parameters.GetValueOrDefault("frame-id") != bound.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    c.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind || !effect.Options.Any(o => o.Id == c.Parameters.GetValueOrDefault("option-id") &&
                        c.Id.Value == $"program-option.frame-{bound.Id}.{effect.ResultBind}.{o.Id}"))) return false;
        }
        return CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().LastOrDefault(e => e.ResolutionId == original.Id) is
            { Used: true, SlashCardIds.Count: 0 } issued && issued.SourceSeat == p.Source.OwnerSeat && issued.TargetSeat == p.TargetSeat && issued.EffectiveSlashKind == p.EffectiveKind &&
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == followup.Id).ToArray() is [var declared] &&
            declared.SourceSeat == p.Source.OwnerSeat && declared.CardId == 0 && declared.CardKind == p.EffectiveKind;
    }
    // Only this family's already-paid state subtree may carry the normalized
    // legacy virtual Alcohol's direct HP return. No legacy matcher is widened.
    private bool ChainedStateBasicLegacyAlcoholRide(ChainedStateBasicFrame paid, int dyingIndex, DyingFrame dying)
    {
        var paidIndex = _resolutionStack.FindIndex(frame => frame.Id == paid.Id);
        if (paidIndex < 0 || paidIndex + 1 >= dyingIndex || dyingIndex + 3 >= _resolutionStack.Count ||
            !ValidChainedStateBasicPayment(paid.Payment) || !ChainedStateBasicStateChild(paid, _resolutionStack[paidIndex + 1]) ||
            _resolutionStack[dyingIndex].Id != dying.Id || ActiveDying?.FrameId != dying.Id ||
            dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            _resolutionStack[dyingIndex - 1] is not ProgramSkillFrame losing || losing.Id != dying.ParentFrameId ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame program ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != dying.VictimSeat || context.SourceSeat is not null ||
            context.DamageFrameId is not null || context.OwnerSeat != program.OwnerSeat || context.OccurrenceIndex != 0 ||
            program.OwnerSeat != dying.VictimSeat || program.OwnerSeat != dying.ResponderSeat ||
            string.IsNullOrWhiteSpace(program.TriggerId) || string.IsNullOrWhiteSpace(program.SkillInstanceId) ||
            program.ActivationId != program.TriggerId || program.InstructionIndex < 1 ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash ||
            definition.Triggers.SingleOrDefault(trigger => trigger.Id == program.TriggerId)?.Window != context.Window) return false;
        for (var index = paidIndex + 2; index <= dyingIndex; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (!(ChainedStateBasicStructuralEdge(parent, child) || HalfHandPaidDamageObserverEdge(index) || DamageFrameRidesOn(child, parent) || DamageObserverRidesOn(child, parent) ||
                RecoveryReplacementFrameRidesOn(child, parent) || ParticipantHandDyingRide(child, parent) ||
                parent is DyingFrame entry && ParticipantHandDyingEntryRide(child, parent, entry) ||
                index >= 2 && _resolutionStack[index - 2] is DyingFrame originalEntry && ParticipantHandDyingEntryRide(child, parent, originalEntry))) return false;
        }
        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(program.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.UseVirtualDyingAlcohol, Target: SkillProgramEffectTarget.Owner } ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame
                { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: not null,
                    LegacyDyingAlcoholReturn: { } returned, Step: ResolutionFrameStep.ResolvingEffect } use ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            use.DyingResponse is not null || use.VirtualBasicReturn is not null ||
            returned.ProgramFrameId != program.Id || returned.InstructionIndex != program.InstructionIndex ||
            returned.ProducerSource != new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId) ||
            !IsExactLegacyActualUseCompletion(use) ||
            _resolutionStack[dyingIndex + 3] is not HpChangedTriggerWindowFrame hp || hp.Id != hp.Change.Id ||
            hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id || hp.Continuation != PostEventContinuation.CardUse ||
            hp.CardId != 0 || hp.CardKind != CardKind.Alcohol || hp.Change.Kind != HpChangeKind.Recovery ||
            hp.Change.SourceSeat != dying.VictimSeat || hp.Change.TargetSeat != dying.VictimSeat ||
            hp.Change.Amount != 1 || hp.Change.HpBefore > 0 || hp.Change.HpAfter != hp.Change.HpBefore + 1 ||
            hp.Candidates.Count == 0 || hp.Candidates.Count != hp.Contexts.Count ||
            hp.CandidateIndex < 0 || hp.CandidateIndex > hp.Candidates.Count) return false;

        var history = CompleteProgramEventHistory().ToArray();
        // A paid rescue keeps its accepted source identity even if a child later
        // disables that skill. Re-enumerating live eligibility would revoke it.
        if (history.OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == program.Id &&
                fact.SkillId == program.SkillId && fact.BindingId == program.TriggerId && fact.SkillInstanceId == program.SkillInstanceId &&
                fact.OwnerSeat == program.OwnerSeat && fact.Window == context.Window) != 1 ||
            history.OfType<PlayerDyingEvent>().Count(fact => fact.ResolutionId == dying.Id &&
                fact.VictimSeat == dying.VictimSeat && fact.KillerSeat is null) != 1 ||
            history.OfType<CardUseDeclaredEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardId == 0 &&
                fact.CardKind == CardKind.Alcohol && fact.SourceSeat == dying.VictimSeat) != 1 ||
            history.OfType<TargetsConfirmedEvent>().Count(fact => fact.ResolutionId == use.Id &&
                fact.TargetSeats.SequenceEqual([dying.VictimSeat])) != 1 ||
            history.OfType<ProgramDyingRescueEvent>().Count(fact => fact.DyingFrameId == dying.Id && fact.SkillId == program.SkillId &&
                fact.OwnerSeat == program.OwnerSeat && fact.VictimSeat == dying.VictimSeat && fact.CardId == 0 &&
                fact.RecoveredHp == 1 && fact.VictimHp == hp.Change.HpAfter) != 1) return false;

        for (var index = 0; index < hp.Candidates.Count; index++)
        {
            var candidate = hp.Candidates[index]; var hpContext = hp.Contexts[index];
            if (candidate.OwnerSeat != dying.VictimSeat || hpContext.OwnerSeat != candidate.OwnerSeat ||
                hpContext.ParentFrameId != hp.Id || hpContext.HpChange != hp.Change ||
                hpContext.SourceSeat != hp.Change.SourceSeat || hpContext.TargetSeat != hp.Change.TargetSeat ||
                hpContext.Amount != hp.Change.Amount || hpContext.OccurrenceIndex != candidate.OccurrenceIndex ||
                hpContext.Window is not (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged) ||
                GetProgramTrigger(candidate).Window != hpContext.Window) return false;
        }
        if (dyingIndex + 4 == _resolutionStack.Count) return true;
        return dyingIndex + 5 == _resolutionStack.Count && hp.CandidateIndex < hp.Candidates.Count &&
            _resolutionStack[dyingIndex + 4] is ProgramSkillFrame observer &&
            MountObserverCandidateMatches(observer, hp.Candidates[hp.CandidateIndex]) &&
            observer.WindowContext == hp.Contexts[hp.CandidateIndex];
    }

    private bool ChainedStateBasicStateChild(ChainedStateBasicFrame paid, ResolutionFrame child) => child is ProgramLifecycleTriggerWindowFrame state &&
        paid.ActiveChildFrameId == state.Id && paid.Payment.CharacterStateChangeId == state.Id && state.ResumeProgramFrameId == paid.Id &&
        state.CharacterStateContinuation == CharacterStateContinuation.ChainedStateBasic && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
        state.Window == SkillProgramTriggerWindow.CharacterEnteredChain && state.OwnerSeat == paid.Payment.Source.OwnerSeat && state.ResumeCardId is null && state.ResumeCardKind is null;
    private bool ChainedStateBasicStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ChainedStateBasicFrame paid) return ValidChainedStateBasicPayment(paid.Payment) && ChainedStateBasicStateChild(paid, child);
        if (parent is ProgramLifecycleTriggerWindowFrame { CharacterStateContinuation: CharacterStateContinuation.ChainedStateBasic } state && child is ProgramSkillFrame observer)
            return state.CandidateIndex >= 0 && state.CandidateIndex < state.Candidates.Count && MountObserverCandidateMatches(observer, state.Candidates[state.CandidateIndex]) &&
                observer.WindowContext is { Window: SkillProgramTriggerWindow.CharacterEnteredChain } context && context.ParentFrameId == state.Id &&
                context.OwnerSeat == observer.OwnerSeat && context.TargetSeat == state.OwnerSeat &&
                _resolutionStack.OfType<ChainedStateBasicFrame>().Any(root => ChainedStateBasicStateChild(root, state) && ValidChainedStateBasicPayment(root.Payment));
        if (parent is ProgramSkillFrame program && child is CardUseFrame { ChainedStateBasicUse: { } receipt } use)
            return IsIssuedChainedStateBasicUse(use) && ChainedStateBasicProgramActor(program, receipt.Payment) && use.CardAttack?.ProgramSkillCardUseFrameId == program.Id;
        return ChainedStateBasicFrameRidesOn(child, parent);
    }
    private bool ChainedStateBasicPaidSuffix(int rootIndex)
    {
        if (_resolutionStack[rootIndex] is not ChainedStateBasicFrame paid || !ValidChainedStateBasicPayment(paid.Payment)) return false;
        if (rootIndex == _resolutionStack.Count - 1) return paid.ActiveChildFrameId is null;
        if (!ChainedStateBasicStateChild(paid, _resolutionStack[rootIndex + 1])) return false;
        for (var dyingIndex = rootIndex + 2; dyingIndex < _resolutionStack.Count; dyingIndex++)
            if (_resolutionStack[dyingIndex] is DyingFrame dying && ChainedStateBasicLegacyAlcoholRide(paid, dyingIndex, dying)) return true;
        for (var i = rootIndex + 2; i < _resolutionStack.Count; i++)
        {
            var child = _resolutionStack[i]; var parent = _resolutionStack[i - 1];
            if (parent is DyingFrame dying && (IsPaidHandRepaymentRescueRide(i - 1, dying) || IsPaidHandRepaymentProgramAlcoholRide(i - 1, dying) ||
                PolicyCounterspellVirtualAlcoholRide(i - 1, dying) || PaidObserverDamageVirtualAlcoholRide(i - 1, dying) ||
                TieredRoundZeroDyingRescueRide(i - 1, dying) || DrawFundedDistinctBasicDyingRescueRide(i - 1, dying))) return true;
            if (ChainedStateBasicStructuralEdge(parent, child) || HalfHandPaidDamageObserverEdge(i) || DamageFrameRidesOn(child, parent) ||
                DamageObserverRidesOn(child, parent) || RecoveryReplacementFrameRidesOn(child, parent) || ParticipantHandDyingRide(child, parent) ||
                parent is DyingFrame entry && ParticipantHandDyingEntryRide(child, parent, entry) ||
                i >= 2 && _resolutionStack[i - 2] is DyingFrame prior && ParticipantHandDyingEntryRide(child, parent, prior)) continue;
            return false;
        }
        return true;
    }
    private ChainedStateBasicFrame? ChainedStateBasicObserverRoot(long? requestedId = null)
    {
        for (var i = 0; i < _resolutionStack.Count; i++)
            if (_resolutionStack[i] is ChainedStateBasicFrame paid && paid.Stage == ChainedStateBasicStage.StateChildren &&
                (requestedId is null || paid.Payment.ParentFrameId == requestedId || _resolutionStack.Skip(i + 1).Any(f => f.Id == requestedId)) && ChainedStateBasicPaidSuffix(i)) return paid;
        return null;
    }
    private bool HasChainedStateBasicObserver(long id) => ChainedStateBasicObserverRoot(id) is not null;
    private bool HasChainedStateBasicDamageObserver(long id) => ChainedStateBasicObserverRoot(id) is not null;
    private bool IsChainedStateBasicProgramDying() => ActiveDying is { } dying && ChainedStateBasicObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsChainedStateBasicNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            ChainedStateBasicObserverRoot() is null || observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CharacterEnteredChain or
                SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private PendingDecision? ChainedStateBasicInvariantDecision(PendingDecision? native) => _resolutionStack.LastOrDefault() is ChainedStateBasicFrame paid
        ? ChainedStateBasicNativeDecision(DrawFundedDistinctBasicNativeDecision(RequestedDeckBasicNativeDecision(paid.OriginalDecision)))
        : native is { } prompt ? ChainedStateBasicNativeDecision(prompt) : null;
    private ResolutionFrame? ChainedStateBasicInvariantTop(ResolutionFrame? native) => _resolutionStack.LastOrDefault() is ChainedStateBasicFrame paid
        ? _resolutionStack.SingleOrDefault(f => f.Id == paid.Payment.RequestFrameId) : native;
    private void AssertChainedStateBasicFramesAndUses()
    {
        foreach (var paid in _resolutionStack.OfType<ChainedStateBasicFrame>())
        {
            var p = paid.Payment; var i = _resolutionStack.FindIndex(f => f.Id == paid.Id);
            if (paid.Stage != ChainedStateBasicStage.StateChildren || p.PaymentFrameId != paid.Id || !ValidChainedStateBasicPayment(p) ||
                paid.OriginalDecision.PlayerSeat != p.Source.OwnerSeat || paid.OriginalDecision.PromptId != p.OriginalPromptId || paid.OriginalDecision.Revision != p.OriginalRevision ||
                paid.TargetSeats.Count == 0 || paid.TargetSeats.Distinct().Count() != paid.TargetSeats.Count || paid.TargetSeats.Any(t => !IsValidPlayerSeat(t)) ||
                (p.Intent == ChainedStateBasicIntent.Play ? i != 0 : i <= 0 || !ChainedStateBasicFrameRidesOn(paid, _resolutionStack[i - 1])) ||
                !ChainedStateBasicPaidSuffix(i) || CompleteProgramEventHistory().OfType<ChainedStateBasicIssuedEvent>().Any(e => e.Receipt.Payment.PaymentFrameId == p.PaymentFrameId))
                throw new InvalidOperationException("A chained-state payment lost its frozen original need, real cost or exact child subtree.");
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.ChainedStateBasicUse is not null && !IsIssuedChainedStateBasicUse(use)) throw new InvalidOperationException("A chained-state Slash lost its unique paid native issuance.");
            if (use.ChainedStateBasicResponse is not null) RequireChainedStateBasicResponseAction(use);
            if (use.Action?.ConversionChain.Any(s => ViewAsRule(s)?.ChainedStateCost.HasValue == true) == true && use.ChainedStateBasicUse is null)
                throw new InvalidOperationException("A chained-state Use cannot disguise a deleted receipt as a legacy zero card.");
        }
        foreach (var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Where(w => w.Action.Type == CardActionType.Response &&
                     w.Action.ConversionChain.Any(s => ViewAsRule(s)?.ChainedStateCost.HasValue == true)))
            if (LifecycleCardUse(window.ParentFrameId) is not { ChainedStateBasicResponse: not null } owner ||
                !TieredRoundActionsStructurallyMatch(RequireChainedStateBasicResponseAction(owner), window.Action))
                throw new InvalidOperationException("A chained-state Dodge child lost its exact accepted response owner.");
    }
}
