using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardZoneKind[] DamageJudgmentPaymentZones = [CardZoneKind.Hand, CardZoneKind.Equipment];
    private const string DamageJudgmentPaymentReason = "skill-program.damage-judgment-suit.payment";
    private const string DamageJudgmentSourceDiscardReason = "skill-program.damage-judgment-suit.source-discard";

    private CardUseFrame? ActualDamageJudgmentUse(ProgramSkillWindowContext context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, SourceSeat: { } source, TargetSeat: { } target } ||
            ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId || damage.ParentFrameId != context.DamageFrameId) return null;
        var attack = GetDamageTriggerAttack(damage);
        if (attack.IsSourceLess || !attack.DamageWasApplied || attack.SourceSeat != source || attack.TargetSeat != target ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) return null;
        return _resolutionStack.OfType<CardUseFrame>().LastOrDefault(use => use.Id == attack.ResolutionId && use.SourceSeat == source &&
            (use.Action is { Type: CardActionType.Use } action && action.ActorSeat == source && action.EffectiveKind == attack.EffectiveCardKind ||
             use.Action is null && use.CardKind == attack.EffectiveCardKind && LegacyDamageJudgmentVirtualProducer(use, target) is not null));
    }

    private long? LegacyDamageJudgmentVirtualProducer(CardUseFrame use, int target)
    {
        if (use.Action is not null || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.TargetSeats is not [var currentTarget] || currentTarget != target || use.CardAttack is not
                { Active: true, IsSourceLess: false, EffectiveCardKind: CardKind.Slash, CardId: null, PhysicalCardIds.Count: 0,
                    ProgramSkillCardUseFrameId: { } producer } attack || attack.SourceSeat != use.SourceSeat || attack.TargetSeat != target) return null;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || parent.Id != producer || parent.OwnerSeat != use.SourceSeat ||
            parent.TriggerId is null || parent.SelectedTargetSeats is not [var selected] || !IsValidPlayerSeat(selected) ||
            !_events.Select(e => e.Payload).Concat(_pendingEvents).OfType<TargetsConfirmedEvent>().Any(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual([selected])) ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash) return null;
        var instructions = ProgramInstructionResolver.Default.Resolve(parent, program).Instructions;
        return parent.InstructionIndex >= 1 && parent.InstructionIndex <= instructions.Count && instructions[parent.InstructionIndex - 1] is
            { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash, UseCardActionWindows: false,
                TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget } ? parent.Id : null;
    }

    private bool DamageJudgmentUseMatches(ProgramDamageJudgmentSuitPayment draft, CardKind? kind) => LifecycleCardUse(draft.CardUseFrameId) is { } use &&
        use.SourceSeat == draft.SourceSeat && (use.Action is { Type: CardActionType.Use } action
            ? draft.LegacyVirtualProducerFrameId is null && action.ActionId == draft.CardActionId && action.ActorSeat == draft.SourceSeat && IsSlashCard(action.EffectiveKind) && action.EffectiveKind == kind
            : kind == CardKind.Slash && draft.CardActionId is null && draft.LegacyVirtualProducerFrameId is { } legacy && LegacyDamageJudgmentVirtualProducer(use, draft.TargetSeat) == legacy);

    private bool CanRunDamageJudgmentSuitPayment(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int owner) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard) ||
        _winner == Winner.None && IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
        context.TargetSeat is { } target && _players[target].IsAlive && ActualDamageJudgmentUse(context) is not null &&
        DamageJudgmentPaymentZones.Any(zone => _cardZones.CardsAt(new(zone, owner)).Count > 0);

    private SkillProgramStepOutcome BeginDamageJudgmentSuitPayment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var context = active.WindowContext ?? throw new InvalidOperationException("Damage judgment lost its original window.");
        var use = ActualDamageJudgmentUse(context);
        if (active.DamageJudgmentSuitPayment is not null || use is null ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DamageTriggerWindowFrame damage || damage.Id != context.ParentFrameId ||
            damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            !MountObserverCandidateMatches(active, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
            !_players[active.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId) ||
            context.TargetSeat is not { } target || !_players[target].IsAlive)
            throw new InvalidOperationException("Damage judgment requires its exact actual Slash candidate and living source.");
        ReplaceRuntimeTop(active with { DamageJudgmentSuitPayment = new(active.InstructionIndex, damage.Id, damage.ParentFrameId,
            use.Id, use.Action?.ActionId, active.OwnerSeat, use.SourceSeat, target, context.OccurrenceIndex,
            _resolutionSequence + 1, effect.JudgmentReason!, effect.ResultBind!,
            LegacyVirtualProducerFrameId: use.Action is null ? LegacyDamageJudgmentVirtualProducer(use, target) : null) });
        return StartProgramJudgment(GetActiveProgramFrame(active.Id), target, effect.JudgmentReason!, effect.ResultBind!,
            SkillProgramCardSetVisibility.Public, use.SourceSeat);
    }

    private bool IsDamageJudgmentInstructionTarget(ProgramSkillFrame frame, int target) =>
        frame.DamageJudgmentSuitPayment is { Stage: ProgramDamageJudgmentPaymentStage.Judging } draft &&
        target == draft.TargetSeat && IsValidDamageJudgmentSuitPayment(frame);

    private void CaptureDamageJudgmentSuitResult(JudgmentFrame judgment, Card card, Suit suit)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == judgment.ParentFrameId) is not
            { DamageJudgmentSuitPayment: { Stage: ProgramDamageJudgmentPaymentStage.Judging } draft } frame) return;
        if (!IsValidDamageJudgmentSuitPayment(frame) || judgment.Id != draft.JudgmentFrameId || judgment.TargetSeat != draft.TargetSeat ||
            judgment.SourceSeat != draft.SourceSeat || judgment.Reason != draft.Reason || judgment.ProgramResultBind != draft.ResultBind ||
            judgment.Continuation != JudgmentContinuationKind.ProgramSkill || draft.JudgmentCardId is not null)
            throw new InvalidOperationException("The finalized damage judgment changed its owning parent or result.");
        ReplaceRuntimeFrame(frame.Id, frame with { DamageJudgmentSuitPayment = draft with
        { JudgmentCardId = card.Id, JudgmentCardKind = card.Kind, JudgmentSuit = suit, JudgmentRank = card.Rank } });
    }

    private bool TryReturnDamageJudgmentSuitPayment(JudgmentFrame judgment)
    {
        if (GetActiveProgramFrame(judgment.ParentFrameId) is not { DamageJudgmentSuitPayment: { } draft } frame) return false;
        if (!IsValidDamageJudgmentSuitPayment(frame) || draft.Stage != ProgramDamageJudgmentPaymentStage.Judging ||
            draft.JudgmentFrameId != judgment.Id ||
            judgment.TargetSeat != draft.TargetSeat || judgment.SourceSeat != draft.SourceSeat || judgment.Reason != draft.Reason)
            throw new InvalidOperationException("A damage judgment returned without its exact frozen result.");
        if (draft.JudgmentCardId is null && judgment.CardId is null)
        {
            SetProgramCardSet(frame.Id, draft.ResultBind, [], SkillProgramCardSetVisibility.Public, []);
            BeginDamageJudgmentCleanup(GetActiveProgramFrame(frame.Id));
            return true;
        }
        if (draft.JudgmentCardId is not { } id || draft.JudgmentSuit is null)
            throw new InvalidOperationException("A successful damage judgment lost its finalized physical result.");
        var held = _cardZones.GetLocation(id) == CardLocation.Processing;
        SetProgramCardSet(frame.Id, draft.ResultBind, held ? [id] : [], SkillProgramCardSetVisibility.Public,
            held ? [CardLocation.Processing] : [], draft.JudgmentSuit);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.ChoosingPayment } });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private IReadOnlyList<PromptChoice> DamageJudgmentPaymentChoices(ProgramSkillFrame frame, bool sourceDiscard)
    {
        var draft = frame.DamageJudgmentSuitPayment!;
        var actor = sourceDiscard ? draft.SourceSeat : draft.OwnerSeat;
        var result = BuildOwnedCardPaymentChoices(frame.Id, actor, actor, DamageJudgmentPaymentZones, OwnedCardMoveIntent.Discard)
            .Select(choice => choice with { Parameters = new Dictionary<string, string>(choice.Parameters)
                { ["program-action"] = "damage-judgment-suit-payment", ["payment-kind"] = sourceDiscard ? "source-discard" : "owner-payment" } }).ToList();
        if (!sourceDiscard) result.Add(new(new ChoiceId($"damage-judgment.{frame.Id}.decline"), "不弃置牌。", [], [],
            new Dictionary<string, string> { ["program-action"] = "damage-judgment-suit-payment", ["payment-kind"] = "decline",
                ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) }));
        return Array.AsReadOnly(result.ToArray());
    }

    private void PublishDamageJudgmentPayment(ProgramSkillFrame frame, bool sourceDiscard)
    {
        var draft = frame.DamageJudgmentSuitPayment!; var actor = sourceDiscard ? draft.SourceSeat : frame.OwnerSeat;
        var choices = DamageJudgmentPaymentChoices(frame, sourceDiscard); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, actor, sourceDiscard ? "请弃置一张牌。" :
            $"判定为{GetSuitDisplayName(draft.JudgmentSuit)}{draft.JudgmentRank}，你可以弃置一张牌执行效果。", choices.SelectMany(c => c.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.TargetSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[actor].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveDamageJudgmentSuitPaymentChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Damage judgment payment lost its owner.");
        var draft = frame.DamageJudgmentSuitPayment ?? throw new InvalidOperationException("Damage judgment payment lost its frozen facts.");
        var sourceDiscard = draft.Stage == ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard;
        if (!IsValidDamageJudgmentSuitPayment(frame) || !sourceDiscard && draft.Stage != ProgramDamageJudgmentPaymentStage.ChoosingPayment ||
            _pendingDecision?.PlayerSeat != (sourceDiscard ? draft.SourceSeat : frame.OwnerSeat) ||
            !DamageJudgmentPaymentChoices(frame, sourceDiscard).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Damage judgment payment changed its published actor, slot or original result.");
        ClearPendingDecision();
        if (!sourceDiscard && (selected.Parameters["payment-kind"] == "decline" || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId)))
        { BeginDamageJudgmentCleanup(frame); return; }
        var actor = sourceDiscard ? draft.SourceSeat : draft.OwnerSeat;
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), actor);
        var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        if (selected.Cards is not [var actual] || actual != card.Id || !_players[actor].IsAlive ||
            from.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(actor, frame.SkillId, frame.SkillInstanceId, card))
            throw new InvalidOperationException("Damage judgment payment lost its exact owned physical entity.");
        var paidSuit = EffectiveSuit(_players[actor], card);
        ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = sourceDiscard
            ? draft with { Stage = ProgramDamageJudgmentPaymentStage.SourceDiscardChildren, SourceDiscardsRemaining = draft.SourceDiscardsRemaining - 1 }
            : draft with { Stage = ProgramDamageJudgmentPaymentStage.PaymentChildren, PaidCardId = card.Id, PaidCardKind = card.Kind,
                PaidFrom = from, PaidSuit = paidSuit, PaidRank = card.Rank }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.DiscardPile, new(sourceDiscard ? DamageJudgmentSourceDiscardReason : DamageJudgmentPaymentReason), movement =>
        {
            var current = GetActiveProgramFrame(frame.Id);
            if (sourceDiscard)
            {
                var state = current.DamageJudgmentSuitPayment!;
                ReplaceRuntimeTop(current with { DamageJudgmentSuitPayment = state.SourceDiscardMovementSequence1 is null
                    ? state with { SourceDiscardMovementSequence1 = movement.Sequence }
                    : state with { SourceDiscardMovementSequence2 = movement.Sequence } });
                return;
            }
            ReplaceRuntimeTop(current with { DamageJudgmentSuitPayment = current.DamageJudgmentSuitPayment! with { PaidMovementSequence = movement.Sequence, PaidTo = movement.To } });
            AdvanceEventRulesAndQueueFact(new ProgramJudgmentSuitPaymentCommittedEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
                frame.SkillInstanceId, frame.OwnerSeat, draft.JudgmentFrameId, draft.JudgmentCardId!.Value, card.Id, movement.Sequence,
                draft.JudgmentSuit == paidSuit, draft.JudgmentRank == card.Rank,
                draft.JudgmentSuit!.Value, draft.JudgmentRank!.Value, paidSuit, card.Rank));
        });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private bool ResumeDamageJudgmentSuitPayment(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.DamageJudgmentSuitPayment is not { } draft) return false;
        if (!IsValidDamageJudgmentSuitPayment(frame)) throw new InvalidOperationException("The damage judgment lost its original candidate or paid ledger.");
        if (frame.PendingMovementContinuation is not null || _pendingDecision is not null) return true;
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.Complete) return false;
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.Judging) return true;
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.ChoosingPayment)
        {
            if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None ||
                !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) || DamageJudgmentPaymentChoices(frame, false).Count == 1)
            { BeginDamageJudgmentCleanup(frame); return true; }
            PublishDamageJudgmentPayment(frame, false); return true;
        }
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.PaymentChildren)
        {
            ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.SuitEffectChildren,
                SourceDiscardsRemaining = draft.JudgmentSuit == Suit.Club ? 2 : 0 } });
            if (_winner == Winner.None)
            {
                var host = new ProgramSkillHost(this);
                if (draft.JudgmentSuit == Suit.Heart && _players[draft.TargetSeat].IsAlive)
                    host.Recover(frame.Id, frame.OwnerSeat, draft.TargetSeat, 1, null, null);
                else if (draft.JudgmentSuit == Suit.Diamond && _players[draft.TargetSeat].IsAlive)
                    DrawProgramCards(frame.Id, draft.TargetSeat, 2, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.damage-judgment-suit.draw"));
                else if (draft.JudgmentSuit == Suit.Spade && _players[draft.SourceSeat].IsAlive)
                    host.TurnOver(frame.Id, frame.OwnerSeat, draft.SourceSeat);
            }
            if (new ProgramSkillHost(this).TryStartPostInstructionWindow(frame.Id)) return true;
            AdvanceRuntimeProgram(frame.Id); return true;
        }
        if (draft.Stage is ProgramDamageJudgmentPaymentStage.SuitEffectChildren or ProgramDamageJudgmentPaymentStage.SourceDiscardChildren)
        {
            if (draft.SourceDiscardsRemaining > 0 && _winner == Winner.None && _players[draft.SourceSeat].IsAlive && DamageJudgmentPaymentChoices(frame, true).Count > 0)
            {
                ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard } });
                PublishDamageJudgmentPayment(GetActiveProgramFrame(frame.Id), true); return true;
            }
            ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.MatchingClaims } });
            AdvanceRuntimeProgram(frame.Id); return true;
        }
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard) return true;
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.ClaimChildren)
        { ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.MatchingClaims } }); AdvanceRuntimeProgram(frame.Id); return true; }
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.MatchingClaims)
        {
            if (_players[frame.OwnerSeat].IsAlive && _winner == Winner.None)
            {
                if (!draft.JudgmentClaimIssued && draft.JudgmentSuit == draft.PaidSuit && draft.JudgmentCardId is { } judge && _cardZones.GetLocation(judge) == CardLocation.Processing)
                { ClaimDamageJudgmentMatchingCard(frame, judge, true); return true; }
                if (!draft.PaymentClaimIssued && draft.JudgmentRank == draft.PaidRank && draft.PaidCardId is { } paid && _cardZones.GetLocation(paid) == CardLocation.DiscardPile)
                { ClaimDamageJudgmentMatchingCard(frame, paid, false); return true; }
            }
            BeginDamageJudgmentCleanup(frame); return true;
        }
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.CleanupChildren)
        { ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.Complete } }); AdvanceRuntimeProgram(frame.Id); return true; }
        throw new InvalidOperationException("Unknown damage judgment stage.");
    }

    private bool ReturnDamageJudgmentSuitPaymentMovement(ProgramSkillFrame frame)
    {
        if (frame.DamageJudgmentSuitPayment is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions.Single();
        if (frame.PendingMovementContinuation is not { } movement || !IsDamageJudgmentSuitPaymentMovement(frame, effect, movement))
            throw new InvalidOperationException("The suit payment movement lost its exact owning cost or cleanup stage.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        // Source loss cannot erase an already paid atomic tail. Before payment
        // the same entry reaches only the original judgment cleanup stage.
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private void ClaimDamageJudgmentMatchingCard(ProgramSkillFrame frame, int id, bool judged)
    {
        var draft = frame.DamageJudgmentSuitPayment!; var from = judged ? CardLocation.Processing : CardLocation.DiscardPile;
        var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
        ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.ClaimChildren,
            JudgmentClaimIssued = draft.JudgmentClaimIssued || judged, PaymentClaimIssued = draft.PaymentClaimIssued || !judged },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(frame.OwnerSeat), new(judged ? "skill-program.damage-judgment-suit.claim-judgment" : "skill-program.damage-judgment-suit.reclaim-payment"), movement =>
        {
            var current = GetActiveProgramFrame(frame.Id); var state = current.DamageJudgmentSuitPayment!;
            ReplaceRuntimeTop(current with { DamageJudgmentSuitPayment = judged
                ? state with { JudgmentClaimMovementSequence = movement.Sequence }
                : state with { PaymentClaimMovementSequence = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new ProgramJudgmentSuitPaymentCardClaimedEvent(frame.Id, frame.SkillId, frame.SkillInstanceId,
                frame.OwnerSeat, draft.JudgmentFrameId, id, movement.Sequence, judged));
        });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private void BeginDamageJudgmentCleanup(ProgramSkillFrame frame)
    {
        var draft = frame.DamageJudgmentSuitPayment!;
        ReplaceRuntimeTop(frame with { DamageJudgmentSuitPayment = draft with { Stage = ProgramDamageJudgmentPaymentStage.CleanupChildren } });
        if (draft.JudgmentCardId is { } id && _cardZones.GetLocation(id) == CardLocation.Processing)
            MoveCard(_cardZones.CardsAt(CardLocation.Processing).Single(c => c.Id == id), CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.JudgmentFinish);
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    private sealed partial class ProgramSkillHost : IDamageJudgmentSuitPaymentHost
    {
        public SkillProgramStepOutcome JudgeDamageTargetThenOfferSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginDamageJudgmentSuitPayment(frame, effect);
    }
}
