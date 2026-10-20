namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WeifuDiscardReason = global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardTopPlacement.WeifuDiscardReason, WeifuDrawReason = "skill-program.qian-zhao.weifu.draw";
    private const string WeifuJudgmentReason = "weifu", WeifuJudgmentBind = "weifu-final-result";
    private static CardConversionSource QianZhaoSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_qian_zhao_ol_Weifu.QianZhaoSource(f);
    private SkillProgramStepOutcome BeginWeifu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:weifu" || f.InstructionIndex != 1 || f.TriggerId is not null || f.WindowContext is not null || f.Weifu is not null ||
            f.SelectedCardIds is not [var id] || f.SelectedTargetSeats.Count != 0 || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Weifu requires its exact one-card native Play entry.");
        var card = GetAdvancedCard(id); var from = _cardZones.GetLocation(id);
        if (from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || card.IsGeneralWeapon ||
            IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard) ||
            IsForeignEquipmentDiscardPrevented(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard) ||
            from.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, card))
            throw new InvalidOperationException("Weifu cannot replace its exact legal HE discard.");
        var r = new WeifuReceipt(QianZhaoSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _turnProgression.OwnerSeat,
            id, from, GetProgramCardCategory(card.Kind), _movementSequence, _movementSequence);
        ReplaceRuntimeTop(f = f with { Weifu = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new(WeifuDiscardReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Weifu = f.Weifu! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WeifuPaidEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwnerSeat, id, from, r.CostCategory, r.Before, _movementSequence));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainQianZhaoChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26620)]
    private bool ResumeWeifu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Weifu: { } r } f || f.Id != id) return false;
        AssertQianZhao(f);
        if (r.Stage == WeifuStage.Judging) return true;
        if (DrainQianZhaoChildren(id)) return true;
        f = GetActiveProgramFrame(id); r = f.Weifu!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishQianZhao(f); return true; }
        if (r.Stage == WeifuStage.PaymentChildren)
        {
            var judgment = _resolutionSequence + 1;
            ReplaceRuntimeTop(f = f with { Weifu = r with { Stage = WeifuStage.Judging, JudgmentId = judgment, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = null });
            _ = StartProgramJudgment(f, f.OwnerSeat, WeifuJudgmentReason, WeifuJudgmentBind, SkillProgramCardSetVisibility.Public, f.OwnerSeat);
            return true;
        }
        if (r.Stage == WeifuStage.JudgmentChildren)
        {
            if (r.ResultCategory is null || r.JudgmentId is null) throw new InvalidOperationException("Weifu lost its finalized native judgment.");
            ReplaceRuntimeTop(f = f with { Weifu = r with { Issued = true, Stage = WeifuStage.DrawChildren, Before = _movementSequence, After = _movementSequence },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (r.ResultCategory is SkillProgramCardCategory.Basic or SkillProgramCardCategory.Trick)
                AdvanceEventRulesAndQueueFact(new WeifuGrantedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwnerSeat, r.ResultCategory.Value, r.JudgmentId.Value));
            var drawn = r.CostCategory == r.ResultCategory ? DrawCards(_players[f.OwnerSeat], 1, true, new(WeifuDrawReason)).Count : 0;
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { Weifu = f.Weifu! with { After = _movementSequence, DrawCount = drawn } });
            AdvanceEventRulesAndQueueFact(new WeifuDrawPaidEvent(id, drawn));
            AdvanceRuntimeProgram(id); return true;
        }
        FinishQianZhao(f); return true;
    }
    private bool MatchesWeifuJudgmentInstruction(ProgramSkillFrame f, int target, string reason, string bind, SkillProgramCardSetVisibility visibility) =>
        f.Weifu is { Stage: WeifuStage.Judging, JudgmentId: not null } r && f.SkillId == "ol:weifu" && f.TriggerId is null && f.WindowContext is null &&
        r.Source == QianZhaoSource(f) && r.GameplayHash == f.GameplayHash && r.InstructionIndex == f.InstructionIndex && f.InstructionIndex == 1 &&
        target == f.OwnerSeat && reason == WeifuJudgmentReason && bind == WeifuJudgmentBind && visibility == SkillProgramCardSetVisibility.Public &&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash &&
        GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.ResolveWeifuJudgment;
    private bool IsWeifuJudgment(JudgmentFrame j, ProgramSkillFrame f) => j.Continuation == JudgmentContinuationKind.ProgramSkill &&
        f.Weifu?.JudgmentId == j.Id && j.ParentFrameId == f.Id && j.SourceSeat == f.OwnerSeat && j.ParentAttackId is null &&
        MatchesWeifuJudgmentInstruction(f, j.TargetSeat, j.Reason, j.ProgramResultBind ?? "", j.ProgramResultVisibility ?? SkillProgramCardSetVisibility.Private);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26620)]
    private void FreezeWeifuJudgmentResult(IGameEvent payload)
    {
        if (payload is not JudgmentResolvedEvent { CardId: { } card, CardKind: { } kind } e ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == e.ParentResolutionId) is not { Weifu: { } r } f) return;
        if (ActiveJudgment is not { } judgment || !IsWeifuJudgment(judgment, f) || judgment.Id != e.ResolutionId || r.ResultKind is not null)
            throw new InvalidOperationException("Weifu cannot freeze a different or duplicate judgment result.");
        var category = GetProgramCardCategory(kind);
        ReplaceRuntimeFrame(f.Id, f with { Weifu = r with { ResultCardId = card, ResultKind = kind, ResultCategory = category } });
        AdvanceEventRulesAndQueueFact(new WeifuJudgedEvent(f.Id, judgment.Id, card, kind, category));
    }
    private bool TryReturnWeifuJudgment(JudgmentFrame j)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Weifu: { } r } f || f.Id != j.ParentFrameId) return false;
        if (!IsWeifuJudgment(j, f) || r.ResultCardId is not { } id || r.ResultKind is null ||
            !ProgramEventHistory<WeifuJudgedEvent>().Any(e => e.FrameId == f.Id && e.JudgmentId == j.Id && e.CardId == id && e.Kind == r.ResultKind))
            throw new InvalidOperationException("Weifu lost its exact paid native judgment return.");
        ReplaceRuntimeTop(f = f with { Weifu = r with { Stage = WeifuStage.JudgmentChildren, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (_cardZones.GetLocation(id) == CardLocation.Processing)
            MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.JudgmentFinish);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Weifu = f.Weifu! with { After = _movementSequence } });
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private void FinishQianZhao(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new QianZhaoCompletedEvent(f.Id, f.SkillId));
        ReplaceRuntimeTop(f = f with { Weifu = null, WeifuTargets = null, Kuansai = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26620)]
    private bool ReturnQianZhaoMovement(ProgramSkillFrame f) => f.Weifu is not null ? ResumeWeifu(f.Id) : f.Kuansai is not null && ResumeKuansai(f.Id);
    [ProgramCapability]
    private sealed class QianZhaoCapability(GameEngine e) : IQianZhaoHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveWeifuJudgment => e.BeginWeifu(f),
            SkillProgramEffectOp.OfferWeifuCategoryTargets => e.BeginWeifuTargets(f),
            SkillProgramEffectOp.ResolveKuansaiTargetChoice => e.BeginKuansai(f),
            _ => throw new InvalidOperationException("Unknown Qian Zhao native operation.")
        };
    }
}
