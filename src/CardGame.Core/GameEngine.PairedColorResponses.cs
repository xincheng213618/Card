using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PairedColorDiscardReason = "skill-program.paired-color-response.discard";
    private const string PairedColorObtainReason = "skill-program.paired-color-response.obtain";
    private long PairedColorSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;

    private PairedColorResponseLinkedEvent? BuildPairedColorResponseLink(ProgramCardTriggerWindowFrame window)
    {
        if (window.ResponseCompletion is not { } completion || !ValidResponseCompletionParent(window) ||
            window.Action is not { Type: CardActionType.Response } response) return null;
        var rootId = _resolutionStack.SingleOrDefault(f => f.Id == window.ParentFrameId) is NullificationWindowFrame counter
            ? counter.ParentFrameId : window.ParentFrameId;
        if (LifecycleCardUse(rootId) is not { Action: { Type: CardActionType.Use } original } use ||
            response.ParentActionId != original.ActionId || !HasExactAcceptedActualHandGainUse(use, original)) return null;
        var history = CompleteProgramEventHistory().ToArray();
        var acceptedIndex = Array.FindIndex(history, e => e is CardActionAcceptedEvent a && a.Action.ActionId == response.ActionId);
        if (acceptedIndex < 0) return null;
        var paired = original; var pairedSeat = original.ActorSeat;
        // Nullification is itself a true Use. Its directly answered counterspell
        // belongs to this exact native chain, rather than every response sharing
        // the original trick's ParentActionId. Duel Slash remains a mere Response
        // and therefore always answers the original Duel Use.
        if (completion.OriginalContinuation == ProgramCardContinuation.NullificationResponse)
        {
            var previous = history.Take(acceptedIndex).OfType<CardResponseCompletedEvent>().LastOrDefault(e =>
                e.ParentFrameId == window.ParentFrameId && e.OriginalContinuation == ProgramCardContinuation.NullificationResponse);
            if (previous is not null)
            {
                var prior = history.Take(acceptedIndex).OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == previous.ActionId).ToArray();
                if (prior is not [var accepted] || !IsCompletedResponseUse(accepted.Action, ProgramCompletedResponseKind.Nullification) ||
                    accepted.Action.ParentActionId != original.ActionId || previous.ActorSeat != accepted.Action.ProviderSeat ||
                    previous.NativeActorSeat != accepted.Action.ActorSeat) return null;
                paired = accepted.Action; pairedSeat = previous.ActorSeat;
            }
        }
        return new(window.Id, response.ActionId, window.ParentFrameId, rootId, original.ActionId,
            completion.OriginalContinuation, completion.CompletionActorSeat, response.ActorSeat,
            paired.ActionId, pairedSeat, response.EffectiveKind, paired.EffectiveKind,
            response.EffectiveIsRed, paired.EffectiveIsRed);
    }

    private void CapturePairedColorResponseLink(ProgramCardTriggerWindowFrame window)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferPairedColorCardDisposition)) return;
        if (CompleteProgramEventHistory().OfType<PairedColorResponseLinkedEvent>().Any(e => e.ResponseActionId == window.Action.ActionId))
            throw new InvalidOperationException("A native response cannot replace its frozen direct color pair.");
        if (BuildPairedColorResponseLink(window) is { } pair) AdvanceEventRulesAndQueueFact(pair);
    }

    private PairedColorResponseLinkedEvent? ExactPairedColorResponseLink(ProgramCardTriggerWindowFrame window)
    {
        var facts = CompleteProgramEventHistory().OfType<PairedColorResponseLinkedEvent>().Where(e => e.ResponseWindowFrameId == window.Id).ToArray();
        return facts is [var fact] && BuildPairedColorResponseLink(window) == fact ? fact : null;
    }

    private bool TryGetPairedColorResponse(ProgramSkillWindowContext context, out ProgramCardTriggerWindowFrame window,
        out PairedColorResponseLinkedEvent pair, out int counterpart)
    {
        window = null!; pair = null!; counterpart = -1;
        if (context is not { Window: SkillProgramTriggerWindow.CardResponseCompleted, CardUse: { } identity } ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame actual ||
            actual.ResponseCompletion is not { CostsDrained: true } || !ValidResponseCompletionWindow(actual) ||
            identity.CardActionId != actual.Action.ActionId || identity.ParentCardUseFrameId != actual.ParentFrameId ||
            ExactPairedColorResponseLink(actual) is not { } link || link.ResponseIsRed is not { } responseRed ||
            link.PairedIsRed is not { } pairedRed || responseRed != pairedRed || link.ResponseSeat == link.PairedSeat ||
            !IsValidPlayerSeat(link.ResponseSeat) || !IsValidPlayerSeat(link.PairedSeat) ||
            context.OwnerSeat != link.ResponseSeat && context.OwnerSeat != link.PairedSeat) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == actual.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != actual.ParentFrameId) return false;
        window = actual; pair = link; counterpart = context.OwnerSeat == link.ResponseSeat ? link.PairedSeat : link.ResponseSeat; return true;
    }

    private bool CanOfferPairedColorDisposition(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferPairedColorCardDisposition)) return true;
        if (!TryGetPairedColorResponse(context, out _, out var pair, out var counterpart) ||
            candidate.OwnerSeat != context.OwnerSeat || !_players[counterpart].IsAlive ||
            CompleteProgramEventHistory().OfType<PairedColorDispositionStartedEvent>().Any(e => e.ResponseActionId == pair.ResponseActionId &&
                e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId && e.StateId == trigger.Effects[0].StateId)) return false;
        var obtain = HasDamageCounterpartAcquisition(candidate.OwnerSeat, candidate.SkillId, trigger.Effects[0].StateId!, counterpart);
        return PairedColorLegalCards(candidate.OwnerSeat, counterpart, obtain).Any();
    }

    private IEnumerable<Card> PairedColorLegalCards(int owner, int counterpart, bool obtain) =>
        new[] { CardLocation.Hand(counterpart), CardLocation.Equipment(counterpart) }.SelectMany(from => _cardZones.CardsAt(from)
            .Where(c => !IsForeignEquipmentDiscardPrevented(owner, c, from, obtain ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Discard)));

    private bool ExactPairedColorDispositionParent(ProgramSkillFrame f, out ProgramCardTriggerWindowFrame window,
        out PairedColorResponseLinkedEvent pair, out int counterpart)
    {
        window = null!; pair = null!; counterpart = -1;
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context || !TryGetPairedColorResponse(context, out window, out pair, out counterpart) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), context, window)) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == window.Id && window.Activated &&
            _contentRegistry.GetSkill(f.SkillId).Program is { } program && program.GameplayHash == f.GameplayHash &&
            ProgramInstructionResolver.Default.FindTrigger(program, f.TriggerId) is { } trigger &&
            trigger.Effects is [{ Op: SkillProgramEffectOp.OfferPairedColorCardDisposition }];
    }

    private SkillProgramStepOutcome OfferPairedColorCardDisposition(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.PairedColorDisposition is not null || f.PendingMovementContinuation is not null ||
            !ExactPairedColorDispositionParent(f, out _, out var pair, out var counterpart))
            throw new InvalidOperationException("Paired-color disposition lost its exact completed native response and observer.");
        var trigger = GetProgramTrigger(f); PairedColorResponseContract.ValidateTrigger(f.SkillId, trigger);
        if (trigger.Effects[0].StateId != stateId) throw new InvalidOperationException("Paired-color disposition changed its declared state.");
        if (!CanOfferPairedColorDisposition(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), trigger, f.WindowContext!))
            return SkillProgramStepOutcome.Continue;
        var obtain = HasDamageCounterpartAcquisition(f.OwnerSeat, f.SkillId, stateId, counterpart);
        var receipt = new ProgramPairedColorDispositionReceipt(1, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, stateId, pair, counterpart, obtain);
        ReplaceRuntimeTop(f = f with { PairedColorDisposition = receipt });
        AdvanceEventRulesAndQueueFact(new PairedColorDispositionStartedEvent(f.Id, receipt.Source, f.GameplayHash, stateId,
            pair.ResponseActionId, pair.PairedActionId, counterpart, obtain));
        PublishPairedColorDisposition(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> PairedColorDispositionChoices(ProgramSkillFrame f)
    {
        var r = f.PairedColorDisposition!;
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.CounterpartSeat,
                [CardZoneKind.Hand, CardZoneKind.Equipment], r.Obtain ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Discard)
            .Select(c => FreezeDirectedDistanceChoice(c with { Parameters = new Dictionary<string, string>(c.Parameters)
                { ["program-action"] = "paired-color-disposition" } })).ToArray());
    }

    private void PublishPairedColorDisposition(ProgramSkillFrame f)
    {
        var r = f.PairedColorDisposition!;
        PublishParticipantHandChoice(f, f.OwnerSeat, PairedColorDispositionChoices(f),
            r.Obtain ? "获得本次同色响应对方的一张手牌或装备。" : "弃置本次同色响应对方的一张手牌或装备。", r.CounterpartSeat);
    }

    private void ResolvePairedColorDisposition(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The paired-color chooser lost its owning frame.");
        AssertPairedColorDisposition(f);
        if (f.PairedColorDisposition is not { Stage: PairedColorDispositionStage.ChoosingCard } r ||
            _pendingDecision is not { } prompt || !IsPairedColorDispositionChoice(f, prompt) ||
            !PairedColorDispositionChoices(f).Any(c => SameNameHandChoicesEqual(c, selected)) ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("Paired-color disposition requires its current exact owner-only published card slot.");
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.CounterpartSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ClearPendingDecision(); CompletePairedColorDisposition(f, false); return; }
        var from = new CardLocation(zone, r.CounterpartSeat); var pool = _cardZones.CardsAt(from);
        if (slot < 0 || slot >= pool.Count || IsForeignEquipmentDiscardPrevented(f.OwnerSeat, pool[slot], from,
                r.Obtain ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("The selected paired-color card is no longer payable.");
        var card = pool[slot]; var to = zone == CardZoneKind.Equipment && card.IsGeneralWeapon ? CardLocation.OutsideGame :
            r.Obtain ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile;
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { PairedColorDisposition = r with { Stage = PairedColorDispositionStage.MovementChildren,
            PaidCardId = card.Id, PaidKind = card.Kind, PaidFrom = from, PaidGeneralWeapon = card.IsGeneralWeapon,
            SequenceBefore = PairedColorSequence, SequenceAfter = PairedColorSequence }, PendingMovementContinuation = new(r.CounterpartSeat, 0, null) });
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        MoveProgramCardsFromMultipleSources([card.Id], to, new(r.Obtain ? PairedColorObtainReason : PairedColorDiscardReason), (batch, records) =>
        {
            if (records is not [var paid] || paid.CardId != card.Id || paid.CardKind != card.Kind || paid.From != from || paid.To != to)
                throw new InvalidOperationException("Paired-color disposition changed its original real card payment.");
            var current = GetActiveProgramFrame(f.Id); var receipt = current.PairedColorDisposition!;
            ReplaceRuntimeTop(current with { PairedColorDisposition = receipt with { SequenceAfter = paid.Sequence, BatchId = batch } });
            AdvanceEventRulesAndQueueFact(new PairedColorDispositionPaidEvent(f.Id, card.Id, card.Kind, from, to, card.IsGeneralWeapon,
                receipt.SequenceBefore, paid.Sequence, batch));
        });
        AdvanceRuntimeProgram(f.Id);
    }

    private bool DrainPairedColorDispositionChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private bool ResumePairedColorDisposition(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PairedColorDisposition: { } r } f || f.Id != frameId) return false;
        AssertPairedColorDisposition(f);
        if (r.Stage == PairedColorDispositionStage.ChoosingCard)
        {
            if (_pendingDecision is not null) return true;
            if (!_players[f.OwnerSeat].IsAlive || !_players[r.CounterpartSeat].IsAlive ||
                !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || PairedColorDispositionChoices(f).Count == 0)
            { CompletePairedColorDisposition(f, false); return true; }
            PublishPairedColorDisposition(f); return true;
        }
        if (DrainPairedColorDispositionChildren(f)) return true;
        CompletePairedColorDisposition(f, true); return true;
    }

    private void CompletePairedColorDisposition(ProgramSkillFrame f, bool paid)
    {
        var r = f.PairedColorDisposition!;
        AdvanceEventRulesAndQueueFact(new PairedColorDispositionCompletedEvent(f.Id, r.Pair.ResponseActionId, r.CounterpartSeat, r.Obtain, paid));
        ReplaceRuntimeTop(f = f with { PairedColorDisposition = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, paid);
    }
    private bool ReturnPairedColorDispositionMovement(ProgramSkillFrame f)
    {
        if (f.PairedColorDisposition is not { Stage: PairedColorDispositionStage.MovementChildren }) return false;
        AssertPairedColorDisposition(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsPairedColorDispositionMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.OfferPairedColorCardDisposition && f.PairedColorDisposition is
            { Stage: PairedColorDispositionStage.MovementChildren } r && f.PendingMovementContinuation == pending &&
        pending.SubjectSeat == r.CounterpartSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        ExactPairedColorDispositionParent(f, out _, out _, out var counterpart) && counterpart == r.CounterpartSeat;
    private bool CanContinuePairedColorDisposition(ProgramSkillFrame f) =>
        f.PairedColorDisposition is { Stage: PairedColorDispositionStage.MovementChildren } && ValidPairedColorDispositionReceipt(f);

    private PromptChoice SelectAiPairedColorDisposition(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderByDescending(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment) && c.Cards.Count == 1
            ? GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.PairedColorDisposition!.CounterpartSeat]) : 3)
        .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IPairedColorResponseHost
    {
        public SkillProgramStepOutcome OfferPairedColorCardDisposition(ProgramSkillFrame frame, string stateId) => engine.OfferPairedColorCardDisposition(frame, stateId);
        public bool CanContinuePairedColorDisposition(ProgramSkillFrame frame) => engine.CanContinuePairedColorDisposition(frame);
    }
}
