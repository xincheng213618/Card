using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource ZhangHuanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 46300)]
    [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 46300)]
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 46300)]
    private bool ZhangHuanTargetWindows(ProgramTargetCardWindowQuery q) => _players.Any(p => p.IsAlive &&
        GetSkillBindingShard(p).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized).Any(b => b.SkillId == AnniversaryZhangHuanComposition.Yiju));
    [ProgramCapability] private sealed class AnniversaryZhangHuanHost(GameEngine engine) : IAnniversaryZhangHuanHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => engine.BeginZhangHuan(f); }

    // A completed logical discard distinguishes a real discard from UseFinished,
    // judgment cleanup, arbitrary movement and equipment destruction outside the game.
    private IReadOnlyList<AnniversaryZhangHuanDiscard> ZhangHuanDiscards()
    {
        var invocations = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(i => i.TurnNumber == _turnNumber && i.ActualTurnOwnerSeat == _turnProgression.OwnerSeat).ToDictionary(i => i.InvocationId);
        return Array.AsReadOnly(ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>()
            .Where(m => m.To == CardLocation.DiscardPile && invocations.TryGetValue(m.InvocationId, out var i) && m.CompletionSequence > i.SequenceBefore && m.CompletionSequence <= i.SequenceAfter &&
                _cardMovements.Any(move => move.Sequence == m.CompletionSequence && move.CardId == m.CardId && move.To == m.To && move.TurnNumber == i.TurnNumber &&
                    (move.From == m.From || GetProgramDiscardSource(move) == m.From) && (move.Reason == i.Reason || move.Reason.Value == i.Reason.Value + "-finished" || move.Reason.Value == i.Reason.Value + "-discard")))
            .DistinctBy(m => m.CompletionSequence).OrderBy(m => m.CompletionSequence)
            .Select(m => new AnniversaryZhangHuanDiscard(m.InvocationId, m.CardId, GetAdvancedCard(m.CardId).Kind, m.From, invocations[m.InvocationId].ExecutorSeat, m.CompletionSequence)).ToArray());
    }
    private IReadOnlyList<AnniversaryZhangHuanDiscard> ZhangHuanAvailable(ProgramSkillFrame f) => f.AnniversaryZhangHuan is not { } r ? [] :
        Array.AsReadOnly(ZhangHuanDiscards().GroupBy(d => d.CardId).Select(group => group.Last()).Where(d => !r.Used.Contains(d.CardId) &&
            _cardZones.Contains(CardLocation.DiscardPile, d.CardId) && _cardMovements.LastOrDefault(m => m.CardId == d.CardId)?.Sequence == d.Sequence).ToArray());
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 46300, SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo)]
    private bool CanOfferZhangHuan(ProgramTriggerLegalityContext q) => ZhangHuanDiscards().Count > 0;
    private SkillProgramStepOutcome BeginZhangHuan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryZhangHuan is not null || f.InstructionIndex != 1 || f.SkillId != AnniversaryZhangHuanComposition.Shuguo || !ZhangHuanParent(f))
            throw new InvalidOperationException("Shuguo requires its unpaid original actual-turn ending candidate.");
        var r = new AnniversaryZhangHuanReceipt(ZhangHuanSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat, f.WindowContext!.ParentFrameId, AnniversaryZhangHuanStage.Choice)
        { Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId));
        ContinueZhangHuan(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool IsAnniversaryZhangHuanExactCardQuery(CharacterState actor, int? selected)
    {
        if (selected is not > 0 || !actor.IsAlive || !_cardZones.Contains(CardLocation.DiscardPile, selected.Value)) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryZhangHuan: { Stage: AnniversaryZhangHuanStage.Choice or AnniversaryZhangHuanStage.IssuingUse } r } f)
            return f.OwnerSeat == actor.Seat && ValidZhangHuan(f) && ZhangHuanAvailable(f).Any(d => d.CardId == selected) &&
                (r.Stage == AnniversaryZhangHuanStage.Choice || r.Native?.Discard.CardId == selected);
        return _resolutionStack.LastOrDefault() is CardUseFrame { AnniversaryZhangHuanUse: { } n } use && n.Actor == actor.Seat && n.Discard.CardId == selected && IsZhangHuanNative(use);
    }
    private Card? AnniversaryZhangHuanQueryOriginalCard(CharacterState actor, int? id) => IsAnniversaryZhangHuanExactCardQuery(actor, id) ? GetAdvancedCard(id!.Value) : null;
    private IReadOnlyList<LegalAction> ZhangHuanActions(ProgramSkillFrame f)
    {
        if (!ValidZhangHuan(f) || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || f.AnniversaryZhangHuan!.Stop) return [];
        var actor = _players[f.OwnerSeat];
        return Array.AsReadOnly(ZhangHuanAvailable(f).SelectMany(d => BuildLegalActions(actor, false, d.CardId).Where(a => a.CardId == d.CardId &&
            a.ConversionSource is null && a.AdditionalConversionSources is not { Count: > 0 } && a.MinCardCount <= 1 && a.MaxCardCount <= 1 &&
            a.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral) &&
            ((a.PlayedCardKind ?? d.Kind) == d.Kind || d.Kind == CardKind.Slash && a.PlayedCardKind == CardKind.FireSlash && HasZhuqueFan(actor))))
            .Select(AnniversaryZhangHuanNative.Freeze).ToArray());
    }
    private IReadOnlyList<PromptChoice> ZhangHuanChoices(ProgramSkillFrame f) => Array.AsReadOnly(ZhangHuanActions(f).Select((a, i) => new PromptChoice(new($"shuguo.{f.Id}.{f.AnniversaryZhangHuan!.Used.Count}.use.{i}"), a.Description,
        [a.CardId!.Value], a.TargetSeats, new Dictionary<string, string> { ["program-action"] = "anniversary-shuguo", ["index"] = i.ToString(CultureInfo.InvariantCulture) }))
        .Append(new(new($"shuguo.{f.Id}.{f.AnniversaryZhangHuan!.Used.Count}.stop"), "停止使用，结算未使用牌的摸牌", [], [], new Dictionary<string, string> { ["program-action"] = "anniversary-shuguo", ["index"] = "stop" })).ToArray());
    private void PublishZhangHuan(ProgramSkillFrame f)
    {
        var choices = ZhangHuanChoices(f); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "戍国：依次使用本回合真正弃置的牌；使用他人弃置的牌后停止。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, "戍国", "戍国", _contentRegistry.GetSkill(f.SkillId).Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo)] private PromptChoice ZhangHuanAi(PendingDecision p) => p.Choices.First();
    [ProgramAnswer("anniversary-shuguo")] private void AnswerZhangHuan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangHuan: { Stage: AnniversaryZhangHuanStage.Choice } r } f || !ValidZhangHuan(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ZhangHuanChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shuguo rejects a stale discarded entity, prior sequence or native target choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["index"] == "stop") { DrawZhangHuan(f); return; }
        var a = ZhangHuanActions(f)[int.Parse(choice.Parameters["index"], CultureInfo.InvariantCulture)];
        var d = ZhangHuanAvailable(f).Single(d => d.CardId == a.CardId);
        var n = new AnniversaryZhangHuanNative(f.Id, f.InstructionIndex, r.Source, r.Hash, r.Turn, f.OwnerSeat, d, a, 0, 0);
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r with { Stage = AnniversaryZhangHuanStage.IssuingUse, Native = n, Returned = false, Before = _movementSequence, After = _movementSequence, NativeBatches = [] } });
        ExecuteAction(_players[f.OwnerSeat], a);
    }
    private bool DrainZhangHuan(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 46300)] private bool ContinueZhangHuan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangHuan: { } r } f || f.Id != id) return false;
        RequireZhangHuan(f);
        if (r.Stage is AnniversaryZhangHuanStage.Drawing or AnniversaryZhangHuanStage.IssuingUse or AnniversaryZhangHuanStage.NativeUse) return true;
        if (DrainZhangHuan(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryZhangHuan!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == AnniversaryZhangHuanStage.DrawChildren) { FinishZhangHuan(f); return true; }
        if (r.Stage == AnniversaryZhangHuanStage.NativeTail)
        {
            ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r = r with { Stage = AnniversaryZhangHuanStage.Choice, Native = null, Returned = false, NativeBatches = [] } });
        }
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishZhangHuan(f); return true; }
        if (r.Stop || ZhangHuanActions(f).Count == 0) DrawZhangHuan(f); else PublishZhangHuan(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 46300)] private bool ReturnZhangHuanMovement(ProgramSkillFrame f) => f.AnniversaryZhangHuan is not null && ContinueZhangHuan(f.Id);
    private void DrawZhangHuan(ProgramSkillFrame f)
    {
        var r = f.AnniversaryZhangHuan!;
        if (r.Stage != AnniversaryZhangHuanStage.Choice || ProgramEventHistory<AnniversaryZhangHuanDrawIssuedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Shuguo must settle its unused actual-turn discards once after all real uses and children.");
        var discards = ZhangHuanDiscards().Count;
        var maximum = _players.Where(p => p.IsAlive).All(p => p.Seat == f.OwnerSeat || GetHand(p).Count < GetHand(_players[f.OwnerSeat]).Count);
        var count = maximum || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ? 0 : Math.Min(5, Math.Max(0, discards - r.Used.Count));
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r with { Stage = count == 0 ? AnniversaryZhangHuanStage.Complete : AnniversaryZhangHuanStage.Drawing, TotalDiscards = discards, RequestedDraw = count } });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanDrawIssuedEvent(f.Id, discards, r.Used.Count, maximum, count));
        if (count == 0) FinishZhangHuan(f); else BeginOperationProgramDraw(f, f.OwnerSeat, count, new("skill.anniversary.shuguo.draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo)] private void ReturnZhangHuanDraw(ProgramSkillFrame supplied, NativeDrawOutcome d)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryZhangHuan!;
        if (!ValidZhangHuan(f) || r.Stage != AnniversaryZhangHuanStage.Drawing || r.Draw is not null || d.RecipientSeat != f.OwnerSeat || d.RequestedCount != r.RequestedDraw)
            throw new InvalidOperationException("Shuguo lost its original capped native draw obligation.");
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r with { Stage = AnniversaryZhangHuanStage.DrawChildren, Draw = d, Before = d.SequenceBefore, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanDrawReturnedEvent(f.Id, d.RequestedCount, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter)); ContinueZhangHuan(f.Id);
    }
    private void FinishZhangHuan(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
}
