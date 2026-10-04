using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SlashBenefitDrawReason = "skill-program.slash-target-benefit.draw";
    private const string SlashBenefitDiscardReason = "skill-program.slash-target-benefit.discard";
    private const string SlashBenefitSettlementReason = "skill-program.slash-target-benefit.settlement";
    private long SlashBenefitSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private bool SlashBenefitProgramParentMatches(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { SlashTargetBenefit: { } identity } context || !IsSlashTargetBenefitWindow(context.Window) ||
            _resolutionStack.OfType<SlashTargetBenefitWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != identity.Use.CardUseFrameId || !SlashBenefitWindowMatches(parent) || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
            parent.Contexts[parent.CandidateIndex] != context ||
            identity.Source.OwnerSeat != f.OwnerSeat || identity.Source.SkillId != f.SkillId || identity.Source.SkillInstanceId != f.SkillInstanceId ||
            identity.GameplayHash != f.GameplayHash || !MatchesSlashTargetBenefitUse(identity.Use)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id && f.InstructionIndex == 1;
    }
    private SkillProgramStepOutcome OfferSlashTargetBenefit(ProgramSkillFrame supplied, string settlementBinding) =>
        ResolveSlashTargetBenefit(supplied, false, settlementBinding);
    private SkillProgramStepOutcome SettleDodgeCancelledSlashBenefit(ProgramSkillFrame supplied) =>
        ResolveSlashTargetBenefit(supplied, true, null);
    private bool ResumeSlashTargetBenefit(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId ||
            !IsSlashTargetBenefitWindow(f.WindowContext?.Window ?? default)) return false;
        var settlement = f.WindowContext!.Window == SkillProgramTriggerWindow.SlashDodgeCancelledBenefit;
        // The initial unpaid instruction retains the ordinary executor's source
        // gate. A settlement is authorized by an already paid original receipt.
        if (f.SlashTargetBenefitDraft is null && !settlement) return false;
        var program = _contentRegistry.GetSkill(f.SkillId).Program ?? throw new InvalidOperationException("The original Slash benefit definition is missing.");
        var plan = ProgramInstructionResolver.Default.Resolve(f, program);
        var expected = settlement ? SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit : SkillProgramEffectOp.OfferSlashTargetBenefit;
        if (program.GameplayHash != f.GameplayHash || plan.Instructions.Count != 1 || plan.GetInstruction(0).Effect.Op != expected ||
            plan.GetInstruction(0).Effect.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException("Only the exact standalone issued Slash benefit may resume without a current source instance.");
        if (f.InstructionIndex == 0 && settlement && f.SlashTargetBenefitDraft is null && !f.ReexecuteParticipantInstruction)
        {
            var executing = f with { InstructionIndex = 1, ReexecuteParticipantInstruction = true };
            if (!SlashBenefitProgramParentMatches(executing) || f.WindowContext!.SlashTargetBenefit is not { } identity ||
                SlashTargetBenefitCurrent(identity) is not { Stage: SlashTargetBenefitStage.CancellationQualified } qualified ||
                !ValidSlashTargetBenefitReceipt(qualified)) throw new InvalidOperationException("An unissued settlement cannot bypass skill-source ownership.");
            ReplaceRuntimeTop(f = executing);
        }
        if (!SlashBenefitProgramParentMatches(f)) throw new InvalidOperationException("The issued benefit lost its exact candidate before resumption.");
        if (f.SlashTargetBenefitDraft is { Stage: SlashTargetBenefitDraftStage.Complete })
        { FinishResumedSlashBenefit(f); return true; }
        if (f.SlashTargetBenefitDraft is { Stage: SlashTargetBenefitDraftStage.PaidChildren } && f.PendingMovementContinuation is not null)
        { ContinueSlashBenefitPaymentChildren(f); return true; }
        if (_pendingDecision is not null) return true;
        var outcome = ResolveSlashTargetBenefit(f, settlement, settlement ? null : plan.GetInstruction(0).Effect.StateId);
        if (outcome == SkillProgramStepOutcome.Continue && _resolutionStack.LastOrDefault() is ProgramSkillFrame completed && completed.Id == frameId)
            FinishResumedSlashBenefit(completed);
        return true;
    }
    private void FinishResumedSlashBenefit(ProgramSkillFrame f)
    {
        if (!SlashBenefitProgramParentMatches(f) || f.SlashTargetBenefitDraft is not { Stage: SlashTargetBenefitDraftStage.Complete } d ||
            SlashTargetBenefitCurrent(d.Receipt) is not { } receipt || !ValidSlashTargetBenefitReceipt(receipt))
            throw new InvalidOperationException("An issued benefit cannot return before its original receipt and paid children finish.");
        var paid = d.Settlement || receipt.Stage == SlashTargetBenefitStage.Paid;
        FinishProgramSkill(f, _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
            (paid || HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)));
    }
    private SkillProgramStepOutcome ResolveSlashTargetBenefit(ProgramSkillFrame supplied, bool settlement, string? settlementBinding)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!SlashBenefitProgramParentMatches(f) || f.WindowContext!.SlashTargetBenefit is not { } identity ||
            settlement != (f.WindowContext.Window == SkillProgramTriggerWindow.SlashDodgeCancelledBenefit) ||
            !settlement && settlementBinding != identity.SettlementBinding)
            throw new InvalidOperationException("A Slash benefit requires its exact current scalar candidate.");
        if (f.SlashTargetBenefitDraft is null)
            ReplaceRuntimeTop(f = f with { SlashTargetBenefitDraft = new(identity,
                settlement ? SlashTargetBenefitDraftStage.ChoosingDiscard : SlashTargetBenefitDraftStage.ChoosingBenefit, settlement),
                ReexecuteParticipantInstruction = true });
        else ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = true });
        if (f.SlashTargetBenefitDraft!.Stage == SlashTargetBenefitDraftStage.PaidChildren)
        {
            if (f.PendingMovementContinuation is not null || !ValidSlashBenefitDraftPayment(f))
                throw new InvalidOperationException("A Slash benefit cannot finish before its paid children.");
            return CompleteSlashBenefitOperation(f, true);
        }
        var receipt = SlashTargetBenefitCurrent(identity) ?? throw new InvalidOperationException("The original use lost its benefit receipt.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[identity.Use.TargetSeat].IsAlive ||
            !settlement && (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
                !LifecycleCardUse(identity.Use.CardUseFrameId)!.TargetSeats.Contains(identity.Use.TargetSeat)))
            return CompleteSlashBenefitOperation(f, false);
        if (settlement && (receipt.Stage != SlashTargetBenefitStage.CancellationQualified || !ValidSlashTargetBenefitReceipt(receipt)))
            throw new InvalidOperationException("The settlement requires the original paid and genuinely cancelled target.");
        var choices = SlashBenefitChoices(f);
        if (choices.Count == 0) return CompleteSlashBenefitOperation(f, false);
        var chooser = settlement ? identity.Use.TargetSeat : f.OwnerSeat;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            settlement ? "此杀被闪抵消，选择弃置使用者的一张手牌或装备。" : "选择摸一张牌、弃置此目标一张牌，或不发动。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = settlement ? f.OwnerSeat : identity.Use.TargetSeat,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 同一张杀", skill.Description), Choices = choices };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private Dictionary<string, string> SlashBenefitParameters(ProgramSkillFrame f, string step) => new()
    { ["program-action"] = "slash-target-benefit", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["slash-benefit-step"] = step };
    private IReadOnlyList<PromptChoice> SlashBenefitChoices(ProgramSkillFrame f)
    {
        var d = f.SlashTargetBenefitDraft!;
        if (d.Stage == SlashTargetBenefitDraftStage.ChoosingBenefit)
        {
            var choices = new List<PromptChoice> { new(new($"slash-benefit.{f.Id}.draw"), "摸一张牌", [], [], SlashBenefitParameters(f, "draw")) };
            if (SlashBenefitOwnedChoices(f).Count > 0) choices.Add(new(new($"slash-benefit.{f.Id}.discard"), "弃置此目标一张牌", [], [], SlashBenefitParameters(f, "discard")));
            choices.Add(new(new($"slash-benefit.{f.Id}.decline"), "不发动", [], [], SlashBenefitParameters(f, "decline")));
            return Array.AsReadOnly(choices.ToArray());
        }
        return SlashBenefitOwnedChoices(f);
    }
    private IReadOnlyList<PromptChoice> SlashBenefitOwnedChoices(ProgramSkillFrame f)
    {
        var d = f.SlashTargetBenefitDraft!; var owner = d.Settlement ? f.OwnerSeat : d.Receipt.Use.TargetSeat;
        var chooser = d.Settlement ? d.Receipt.Use.TargetSeat : f.OwnerSeat;
        if (!_players[owner].IsAlive || !_players[chooser].IsAlive) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, chooser, owner, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => { var parameters = c.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                parameters["program-action"] = "slash-target-benefit"; parameters["slash-benefit-step"] = "card";
                return new PromptChoice(c.Id, c.Description, c.Cards, c.Targets, parameters); }).ToArray());
    }
    private void ResolveSlashTargetBenefitChoice(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.SlashTargetBenefitDraft is not { } d ||
            !SlashBenefitProgramParentMatches(f) || _pendingDecision is not { } decision ||
            decision.PlayerSeat != (d.Settlement ? d.Receipt.Use.TargetSeat : f.OwnerSeat) ||
            selected.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            !SlashBenefitChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets)))
            throw new InvalidOperationException("An unpublished Slash benefit choice cannot pay or advance.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[d.Receipt.Use.TargetSeat].IsAlive ||
            !d.Settlement && (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
                !LifecycleCardUse(d.Receipt.Use.CardUseFrameId)!.TargetSeats.Contains(d.Receipt.Use.TargetSeat)))
        { ClearPendingDecision(); CompleteSlashBenefitOperation(f, false); AdvanceRuntimeProgram(f.Id); return; }
        var step = selected.Parameters.GetValueOrDefault("slash-benefit-step");
        ClearPendingDecision();
        if (step == "decline") { CompleteSlashBenefitOperation(f, false); AdvanceRuntimeProgram(f.Id); return; }
        if (step == "discard")
        { ReplaceRuntimeTop(f with { SlashTargetBenefitDraft = d with { Stage = SlashTargetBenefitDraftStage.ChoosingDiscard } }); AdvanceRuntimeProgram(f.Id); return; }
        if (step == "draw") { PaySlashBenefitDraw(f); return; }
        if (step != "card" || !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The Slash benefit payment lost its published opaque slot.");
        var owner = d.Settlement ? f.OwnerSeat : d.Receipt.Use.TargetSeat;
        var cards = _cardZones.CardsAt(new(zone, owner));
        if (slot < 0 || slot >= cards.Count) { CompleteSlashBenefitOperation(f, false); AdvanceRuntimeProgram(f.Id); return; }
        PaySlashBenefitDiscard(f, cards[slot], new(zone, owner));
    }
    private void PaySlashBenefitDraw(ProgramSkillFrame f)
    {
        var before = SlashBenefitSequence; var d = f.SlashTargetBenefitDraft!;
        ReplaceRuntimeTop(f with { SlashTargetBenefitDraft = d with { Stage = SlashTargetBenefitDraftStage.PaidChildren, SequenceBefore = before } });
        DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new(SlashBenefitDrawReason));
        var after = SlashBenefitSequence;
        var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == SlashBenefitDrawReason);
        var paid = SlashTargetBenefitCurrent(d.Receipt)! with { Stage = SlashTargetBenefitStage.Paid, ProducerProgramId = f.Id,
            DrawBenefit = true, SequenceBefore = before, SequenceAfter = after, ActualDrawCount = actual };
        ReplaceSlashTargetBenefit(paid);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { SlashTargetBenefitDraft = f.SlashTargetBenefitDraft! with { Receipt = paid, SequenceAfter = after },
            PendingMovementContinuation = after > before ? new(f.OwnerSeat, 0, null) : null });
        PublishSlashBenefitPaid(f, paid, actual);
        ContinueSlashBenefitPaymentChildren(f);
    }
    private void PaySlashBenefitDiscard(ProgramSkillFrame f, Card card, CardLocation from)
    {
        var d = f.SlashTargetBenefitDraft!; var before = SlashBenefitSequence;
        ReplaceRuntimeTop(f with { SlashTargetBenefitDraft = d with { Stage = SlashTargetBenefitDraftStage.PaidChildren,
            PaidCardId = card.Id, PaidFrom = from, SequenceBefore = before }, PendingMovementContinuation = new(from.OwnerSeat!.Value, 0, null) });
        MoveCard(card, from, CardLocation.DiscardPile, new(d.Settlement ? SlashBenefitSettlementReason : SlashBenefitDiscardReason));
        var after = SlashBenefitSequence;
        f = GetActiveProgramFrame(f.Id); // SilverLion may have queued a replacement on this very frame.
        var paid = SlashTargetBenefitCurrent(d.Receipt)!;
        if (!d.Settlement)
        {
            paid = paid with { Stage = SlashTargetBenefitStage.Paid, ProducerProgramId = f.Id, DrawBenefit = false,
                PaidCardId = card.Id, PaidFrom = from, SequenceBefore = before, SequenceAfter = after };
            ReplaceSlashTargetBenefit(paid); PublishSlashBenefitPaid(f, paid, 1);
        }
        ReplaceRuntimeTop(f = f with { SlashTargetBenefitDraft = f.SlashTargetBenefitDraft! with { Receipt = paid, SequenceAfter = after } });
        ContinueSlashBenefitPaymentChildren(f);
    }
    private void PublishSlashBenefitPaid(ProgramSkillFrame f, SlashTargetBenefitReceipt paid, int actual) =>
        AdvanceEventRulesAndQueueFact(new SlashTargetBenefitPaidEvent(paid.Use.CardUseFrameId, f.Id, paid.OfferWindowId, paid.OfferCandidateIndex,
            paid.Use.ActorSeat, paid.Use.TargetSeat, paid.Source, paid.GameplayHash, paid.DrawBenefit, paid.SequenceBefore, paid.SequenceAfter, actual));
    private void ContinueSlashBenefitPaymentChildren(ProgramSkillFrame f)
    {
        if (f.PendingMovementContinuation is not null && (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id))) return;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
    }
    private SkillProgramStepOutcome CompleteSlashBenefitOperation(ProgramSkillFrame f, bool paid)
    {
        var d = f.SlashTargetBenefitDraft!;
        if (d.Settlement)
        {
            var r = SlashTargetBenefitCurrent(d.Receipt)!;
            ReplaceSlashTargetBenefit(r with { Stage = paid ? SlashTargetBenefitStage.Settled : SlashTargetBenefitStage.Cancelled });
            AdvanceEventRulesAndQueueFact(new SlashTargetBenefitSettledEvent(r.Use.CardUseFrameId, f.Id, r.OfferWindowId, r.OfferCandidateIndex,
                r.Use.ActorSeat, r.Use.TargetSeat, paid, d.SequenceBefore, d.SequenceAfter));
        }
        else if (!paid) ReplaceSlashTargetBenefit(SlashTargetBenefitCurrent(d.Receipt)! with { Stage = SlashTargetBenefitStage.Declined });
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { SlashTargetBenefitDraft = d with { Stage = SlashTargetBenefitDraftStage.Complete }, ReexecuteParticipantInstruction = false });
        return SkillProgramStepOutcome.Continue;
    }
    private bool ReturnSlashBenefitMovement(ProgramSkillFrame f)
    {
        if (f.SlashTargetBenefitDraft is not { Stage: SlashTargetBenefitDraftStage.PaidChildren }) return false;
        if (f.PendingMovementContinuation is null || !ValidSlashBenefitDraftPayment(f)) throw new InvalidOperationException("A benefit movement lost its original paid interval.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); return true;
    }
    private PromptChoice SelectAiSlashTargetBenefit(PendingDecision decision, ProgramSkillFrame f)
    {
        var d = f.SlashTargetBenefitDraft!; var view = CreateSnapshot(decision.PlayerSeat);
        if (d.Stage == SlashTargetBenefitDraftStage.ChoosingBenefit)
        {
            var discard = decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("slash-benefit-step") == "discard");
            var hostility = _aiBrains[decision.PlayerSeat].ScoreProgramTarget(view, d.Receipt.Use.TargetSeat, new(0, 0, 0, 0, 0, 1, false, false));
            return discard is not null && hostility > 1.5 ? discard : decision.Choices.Single(c => c.Parameters.GetValueOrDefault("slash-benefit-step") == "draw");
        }
        var cardOwner = d.Settlement ? f.OwnerSeat : d.Receipt.Use.TargetSeat;
        var discardScore = _aiBrains[decision.PlayerSeat].ScoreProgramTarget(view, cardOwner, new(0, 0, 0, 0, 0, 1, false, false));
        return decision.Choices.OrderBy(c => (discardScore > 0 ? -1 : 1) *
                (c.Cards.Count == 1 ? GetKeepValue(GetAttackCard(c.Cards[0]), _players[cardOwner]) : 5d))
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    private sealed partial class ProgramSkillHost : ISlashTargetBenefitProgramHost
    {
        public SkillProgramStepOutcome OfferSlashTargetBenefit(ProgramSkillFrame f, string binding) => engine.OfferSlashTargetBenefit(f, binding);
        public SkillProgramStepOutcome SettleDodgeCancelledSlashBenefit(ProgramSkillFrame f) => engine.SettleDodgeCancelledSlashBenefit(f);
    }
}
