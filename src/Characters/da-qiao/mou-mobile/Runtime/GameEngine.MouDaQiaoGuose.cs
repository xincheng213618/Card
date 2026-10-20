namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouDaQiao => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouDaQiaoGuose);
    private int MouDaQiaoGuoseLimit => _modeDefinition.ModeKind == ContentModeKind.Identity ? 4 : 2;
    private static CardConversionSource MouDaQiaoSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_da_qiao_mou_mobile_MouDaQiaoGuose.MouDaQiaoSource(f);
    private int MouDaQiaoGuoseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouDaQiaoGuoseReservedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Phase == phase);
    [ProgramPhaseUsedProjection(80)]
    private int MouDaQiaoProjectedPhaseUses(int owner, string skill, int fallback) => skill == MouDaQiaoComposition.Guose && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p ? MouDaQiaoGuoseUses(owner, p) : fallback;
    private IEnumerable<Card> MouDaQiaoRemovable(int target) => GetJudgment(_players[target]).Where(c => !IsZixiQinJudgmentCard(c.Id) && GetJudgmentEffectiveCardKind(c) == CardKind.Indulgence);
    private bool MouDaQiaoGuoseTarget(int owner, int? card, int target, string branch) => IsValidPlayerSeat(target) && _players[target].IsAlive &&
        (branch == "remove-delayed" ? MouDaQiaoRemovable(target).Any() : card is { } id && FindOwnedPlayableCard(_players[owner], id) is { } physical && DiamondUseTargetLegal(owner, physical, target, MouDaQiaoComposition.Guose));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 31300, SkillProgramEffectOp.ResolveMouDaQiaoGuose, SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)]
    private bool CanMouDaQiaoGuose(ProgramActivationLegalityContext c) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == c.OwnerSeat &&
        _currentSeat == c.OwnerSeat && _phase == TurnPhase.Play && MouDaQiaoGuoseUses(c.OwnerSeat, p) < MouDaQiaoGuoseLimit;
    private SkillProgramStepOutcome BeginMouDaQiaoGuose(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouDaQiaoGuose is not null) { ContinueMouDaQiaoGuose(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouDaQiaoComposition.Guose || f.TriggerId is not null || f.InstructionIndex != 1 || f.SelectedTargetSeats is not [var target] ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } p || p.ActorSeat != f.OwnerSeat ||
            MouDaQiaoGuoseUses(f.OwnerSeat, p) >= MouDaQiaoGuoseLimit || !MouDaQiaoGuoseTarget(f.OwnerSeat, f.SelectedCardIds.SingleOrDefault(), target, f.ActivationId))
            throw new InvalidOperationException("Guose requires one current legal native branch within its shared actual-Play budget.");
        var source = MouDaQiaoSource(f);
        var r = new MouDaQiaoGuoseReceipt(source, f.GameplayHash, p, f.ActivationId, target, MouDaQiaoGuoseStage.Selecting);
        ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoGuoseReservedEvent(f.Id, source, f.GameplayHash, p, r.Branch, target, MouDaQiaoGuoseLimit));
        if (r.Branch == "remove-delayed")
        { PublishParticipantHandChoice(f, f.OwnerSeat, MouDaQiaoRemovalChoices(f), "选择要弃置的场上【乐不思蜀】。", target); return SkillProgramStepOutcome.AwaitChild; }
        var card = EntityAtCurrentLocation(f.SelectedCardIds.Single()); var from = _cardZones.GetLocation(card.Id); var expected = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r with { Stage = MouDaQiaoGuoseStage.Using, CardId = card.Id, From = from, UseId = expected, Before = _movementSequence } });
        var id = BeginCardUse(card, f.OwnerSeat, [target], CardKind.Indulgence, conversionSource: source);
        if (id != expected) throw new InvalidOperationException("Guose lost its exact newly issued native delayed Use.");
        MoveCard(card, from, CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        f = _resolutionStack.OfType<ProgramSkillFrame>().Single(parent => parent.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f with { MouDaQiaoGuose = f.MouDaQiaoGuose! with { Paid = true, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoGuosePaidEvent(f.Id, r.Branch, card.Id, from, CardLocation.Processing, _movementSequence));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, [target], LegalActionKind.Indulgence, playedCardKind: CardKind.Indulgence);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> MouDaQiaoRemovalChoices(ProgramSkillFrame f) => Array.AsReadOnly(MouDaQiaoRemovable(f.MouDaQiaoGuose!.TargetSeat).Select(card =>
        new PromptChoice(new($"mou-da-qiao.guose.{f.Id}.{card.Id}"), "弃置【乐不思蜀】", [card.Id], [f.MouDaQiaoGuose.TargetSeat],
            new Dictionary<string, string> { ["program-action"] = "mou-da-qiao-guose", ["frame-id"] = f.Id.ToString() })).ToArray());
    [ProgramAnswer("mou-da-qiao-guose")]
    private void AnswerMouDaQiaoGuose(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDaQiaoGuose: { Stage: MouDaQiaoGuoseStage.Selecting } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], MouDaQiaoRemovalChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Guose accepts only its current visible native judgment removal.");
        var id = choice.Cards.Single(); var from = CardLocation.Judgment(r.TargetSeat); var before = _movementSequence;
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r with { Stage = MouDaQiaoGuoseStage.MovementChildren, CardId = id, From = from, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new("skill-program.mou-mobile:guose.remove"));
        var moved = _cardMovements.Single(m => m.CardId == id && m.Sequence > before && m.From == from && m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = f.MouDaQiaoGuose! with { Paid = true, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoGuosePaidEvent(f.Id, r.Branch, id, from, moved.To, moved.Sequence)); ContinueMouDaQiaoGuose(f.Id);
    }
    [NativeUseReturn(31300)]
    private void ReturnMouDaQiaoGuoseUse(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDaQiaoGuose: { Stage: MouDaQiaoGuoseStage.Using } r } f || r.UseId != useId) return;
        if (!ProgramEventHistory<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId && e.CardId == r.CardId && e.CardKind == CardKind.Indulgence)) throw new InvalidOperationException("Guose returned from a different native Use.");
        ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r with { Stage = MouDaQiaoGuoseStage.MovementChildren, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueMouDaQiaoGuose(f.Id);
    }
    private bool DrainMouDaQiao(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMouDaQiaoGuose(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDaQiaoGuose: { } r } f || f.Id != id) return;
        if (r.Stage is MouDaQiaoGuoseStage.Using or MouDaQiaoGuoseStage.Drawing or MouDaQiaoGuoseStage.Selecting) return;
        if (DrainMouDaQiao(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouDaQiaoGuose!;
        if (r.Stage == MouDaQiaoGuoseStage.MovementChildren && r.Paid && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        { ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r with { Stage = MouDaQiaoGuoseStage.Drawing }, PendingMovementContinuation = null }); BeginOperationProgramDraw(f, f.OwnerSeat, 1, new("skill-program.mou-mobile:guose.draw")); return; }
        AdvanceEventRulesAndQueueFact(new MouDaQiaoGuoseCompletedEvent(f.Id, r.Paid)); ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouDaQiaoGuose, SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)]
    private void ReturnMouDaQiaoGuoseDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouDaQiaoGuose is not { Stage: MouDaQiaoGuoseStage.Drawing } r || o.RequestedCount != 1 || o.RecipientSeat != f.OwnerSeat) throw new InvalidOperationException("Guose lost its exact native Draw1 return.");
        ReplaceRuntimeTop(f = f with { MouDaQiaoGuose = r with { Stage = MouDaQiaoGuoseStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoGuoseDrawReturnedEvent(f.Id, o.AttemptFrameId, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count)); ContinueMouDaQiaoGuose(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDaQiaoGuose, SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)]
    private PromptChoice AiMouDaQiaoGuose(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramCapability]
    private sealed class MouDaQiaoCapability(GameEngine g) : IMouDaQiaoHost, IMouDaQiaoQuery
    {
        public int Limit() => g.MouDaQiaoGuoseLimit;
        public bool Allows(int owner, int? card, int target, string branch) => g.MouDaQiaoGuoseTarget(owner, card, target, branch);
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        { SkillProgramEffectOp.ResolveMouDaQiaoGuose or SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove => g.BeginMouDaQiaoGuose(f), SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart => g.ResolveMouDaQiaoHeart(f), _ => g.BeginMouDaQiaoLiuliPhase(f) };
    }
}
