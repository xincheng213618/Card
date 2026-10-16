using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FuRongCostReason = "skill-program.fu-rong.xiaosi.discard";
    private CardConversionSource FuRongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool FuRongSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private bool FuRongLegalCost(int seat, Card c, CardLocation from) => !c.IsGeneralWeapon &&
        from.OwnerSeat == seat && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        MatchesSkillProgramCardCategory(AdvancedEffectiveHandKind(_players[seat], c), SkillProgramCardCategory.Basic) &&
        !IsSelfHandCategoryDiscardForbidden(seat, c, from, OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(seat, c, from, OwnedCardMoveIntent.Discard);
    private bool FuRongHasCost(int seat) => GetHand(_players[seat]).Concat(GetEquipment(seat))
        .Any(c => FuRongLegalCost(seat, c, _cardZones.GetLocation(c.Id)));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 26310, SkillProgramEffectOp.DiscardBasicPairAndReuse)]
    private bool AllowsFuRongActivation(CharacterState actor) => FuRongHasCost(actor.Seat);

    private SkillProgramStepOutcome BeginFuRongPair(ProgramSkillFrame supplied, int target)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var p = _contentRegistry.GetSkill(f.SkillId).Program!;
        FuRongComposition.ValidateProgram(f.SkillId, p);
        var a = ProgramInstructionResolver.Default.Resolve(f, p).Activation;
        if (a is null || f.FuRongPair is not null || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var selected] || selected != target || target == f.OwnerSeat ||
            !IsValidPlayerSeat(target) || !_players[target].IsAlive || GetHand(_players[target]).Count == 0 ||
            !FuRongSourceCurrent(f) || !FuRongHasCost(f.OwnerSeat) || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play ||
            _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, a.UsageGroup)) != 1)
            throw new InvalidOperationException("Xiaosi requires its exact unpaid phase activation, source and living nonempty-hand target.");
        var r = new ProgramFuRongPairReceipt { Source = FuRongSource(f), GameplayHash = f.GameplayHash,
            UsageGroup = a.UsageGroup, InstructionIndex = f.InstructionIndex, TargetSeat = target, TurnNumber = _turnNumber,
            PhaseInstanceId = _cardUseDebitPhaseInstanceId, Stage = FuRongPairStage.OwnerChoice };
        ReplaceRuntimeTop(f = f with { FuRongPair = r });
        AdvanceEventRulesAndQueueFact(new FuRongPairStartedEvent(f.Id, r.Source, r.GameplayHash, r.UsageGroup, target, _turnNumber, r.PhaseInstanceId));
        PublishFuRongPayment(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private Dictionary<string, string> FuRongArgs(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "fu-rong-pair", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
    private IReadOnlyList<PromptChoice> FuRongPaymentChoices(ProgramSkillFrame f)
    {
        var r = f.FuRongPair!; var payer = r.Stage == FuRongPairStage.OwnerChoice ? f.OwnerSeat : r.TargetSeat;
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, payer, payer, [CardZoneKind.Hand, CardZoneKind.Equipment],
            OwnedCardMoveIntent.Discard, canSelect: (zone, card) => FuRongLegalCost(payer, card, new(zone, payer)))
            .Select(c => new PromptChoice(new($"fu-rong-cost.{f.Id}.{payer}.{c.Cards.Single()}"), c.Description, c.Cards, [], FuRongArgs(f, "cost"))).ToArray());
    }
    private void PublishFuRongPayment(ProgramSkillFrame f)
    {
        var r = f.FuRongPair!; var payer = r.Stage == FuRongPairStage.OwnerChoice ? f.OwnerSeat : r.TargetSeat;
        var choices = FuRongPaymentChoices(f);
        if (choices.Count == 0) throw new InvalidOperationException("Xiaosi cannot offer an empty mandatory basic payment.");
        PublishParticipantHandChoice(f, payer, choices, "选择一张可弃置的基本牌。", payer);
    }
    [ProgramAnswer("fu-rong-pair")]
    private void AnswerFuRongPair(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Xiaosi lost its choice parent.");
        AssertFuRongPair(f); var r = f.FuRongPair!;
        var expected = r.Stage is FuRongPairStage.OwnerChoice or FuRongPairStage.TargetChoice ? FuRongPaymentChoices(f) :
            r.Stage == FuRongPairStage.UseChoice ? FuRongUseChoices(f) : throw new InvalidOperationException("Xiaosi is not awaiting a choice.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
            prompt.PlayerSeat != (r.Stage == FuRongPairStage.TargetChoice ? r.TargetSeat : f.OwnerSeat) ||
            !expected.Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets) &&
                c.Parameters.OrderBy(x => x.Key).SequenceEqual(selected.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Xiaosi requires the exact published chooser, cards, targets and frame parameters.");
        if (r.Stage == FuRongPairStage.UseChoice) { AnswerFuRongUse(f, selected); return; }
        if (r.Stage == FuRongPairStage.OwnerChoice && (!FuRongSourceCurrent(f) || !_players[r.TargetSeat].IsAlive))
        { ClearPendingDecision(); FinishFuRongPair(f); return; }
        var payer = prompt.PlayerSeat;
        var from = selected.Cards is [var selectedId] ? _cardZones.GetLocation(selectedId) : default;
        if (selected.Cards is not [var cardId] || !_players[payer].IsAlive ||
            !FuRongLegalCost(payer, GetAdvancedCard(cardId), from))
            throw new InvalidOperationException("Xiaosi cannot discard a forbidden or substituted physical card.");
        var card = GetAdvancedCard(cardId); var owner = r.Stage == FuRongPairStage.OwnerChoice;
        var paid = new FuRongPayment(payer, cardId, card.Kind, AdvancedEffectiveHandKind(_players[payer], card), from, 0);
        ClearPendingDecision();
        ReplaceRuntimeTop(f with { FuRongPair = r with { Stage = owner ? FuRongPairStage.OwnerChildren : FuRongPairStage.TargetChildren,
            OwnerPayment = owner ? paid : r.OwnerPayment, TargetPayment = owner ? r.TargetPayment : paid },
            PendingMovementContinuation = new(payer, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, payer, card, from, new(FuRongCostReason), beforeFact: move =>
        {
            var active = GetActiveProgramFrame(f.Id); var receipt = active.FuRongPair!; var actual = paid with { Sequence = move.Sequence };
            ReplaceRuntimeTop(active with { FuRongPair = receipt with { OwnerPayment = owner ? actual : receipt.OwnerPayment,
                TargetPayment = owner ? receipt.TargetPayment : actual } });
            AdvanceEventRulesAndQueueFact(new FuRongPairPaidEvent(f.Id, owner, actual));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainFuRongChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26310)]
    private bool ResumeFuRongPair(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.FuRongPair is not { } r) return false;
        AssertFuRongPair(f);
        if (r.Stage is FuRongPairStage.OwnerChoice or FuRongPairStage.TargetChoice)
        { if (_pendingDecision is null) PublishFuRongPayment(f); return true; }
        if (r.Stage == FuRongPairStage.NativeUse) throw new InvalidOperationException("Xiaosi's issued use must return through its exact native receipt.");
        if (r.Stage is FuRongPairStage.OwnerChildren or FuRongPairStage.TargetChildren or FuRongPairStage.DrawChildren or FuRongPairStage.NativeChildren)
        {
            if (DrainFuRongChildren(f)) return true;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.FuRongPair!;
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishFuRongPair(f); return true; }
        if (r.Stage == FuRongPairStage.OwnerChildren)
        {
            if (_players[r.TargetSeat].IsAlive && FuRongHasCost(r.TargetSeat))
            {
                ReplaceRuntimeTop(f = f with { FuRongPair = r with { Stage = FuRongPairStage.TargetChoice } });
                PublishFuRongPayment(f); return true;
            }
            ReplaceRuntimeTop(f = f with { FuRongPair = r with { TargetUnable = true, DrawIssued = true, Stage = FuRongPairStage.DrawChildren,
                DrawBefore = _movementSequence, DrawAfter = _movementSequence },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new FuRongTargetUnableEvent(f.Id, r.TargetSeat));
            var count = DrawCards(_players[f.OwnerSeat], 1, true, new("skill-program.fu-rong.xiaosi.compensation")).Count;
            var active = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(active with { FuRongPair = active.FuRongPair! with { ActualDraw = count, DrawAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new FuRongCompensationDrawEvent(f.Id, f.OwnerSeat, count)); AdvanceRuntimeProgram(f.Id); return true;
        }
        if (r.Stage is FuRongPairStage.TargetChildren or FuRongPairStage.DrawChildren or FuRongPairStage.NativeChildren)
        {
            ReplaceRuntimeTop(f = f with { FuRongPair = r with { Stage = FuRongPairStage.UseChoice,
                SelectedUseCardId = null, ReservedUseId = null, NativeReturn = null, NativeCompleted = false } });
        }
        if (f.FuRongPair!.Stage == FuRongPairStage.UseChoice)
        {
            if (FuRongUseChoices(f).Count == 1) { FinishFuRongPair(f); return true; }
            if (_pendingDecision is null) PublishFuRongUse(f); return true;
        }
        throw new InvalidOperationException("Xiaosi reached an unknown parent continuation.");
    }
    private void FinishFuRongPair(ProgramSkillFrame f)
    {
        var r = f.FuRongPair!;
        ReplaceRuntimeTop(f = f with { FuRongPair = r with { Stage = FuRongPairStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new FuRongPairCompletedEvent(f.Id, r.OwnerPayment is not null, r.TargetPayment is not null, r.UsedCardIds.Count));
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26310)]
    private bool ReturnFuRongMovement(ProgramSkillFrame f)
    {
        if (f.FuRongPair is not { Stage: FuRongPairStage.OwnerChildren or FuRongPairStage.TargetChildren or FuRongPairStage.DrawChildren or FuRongPairStage.NativeChildren }) return false;
        AssertFuRongPair(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26310)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26310)]
    private bool ContinuePaidFuRongPair(ProgramSkillFrame f) => f.FuRongPair?.OwnerPayment is not null;
    [ProgramCapability]
    private sealed class FuRongProgramCapability(GameEngine engine) : IFuRongProgramHost
    { public SkillProgramStepOutcome DiscardBasicPairAndReuse(ProgramSkillFrame f, int target) => engine.BeginFuRongPair(f, target); }
    [ProgramAiChoice(SkillProgramEffectOp.DiscardBasicPairAndReuse)]
    private PromptChoice SelectAiFuRongPair(PendingDecision prompt, ProgramSkillFrame f) =>
        f.FuRongPair!.Stage == FuRongPairStage.UseChoice
            ? prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "use") ?? prompt.Choices.Last()
            : prompt.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[prompt.PlayerSeat])).ThenBy(c => c.Cards.Single()).First();
}
