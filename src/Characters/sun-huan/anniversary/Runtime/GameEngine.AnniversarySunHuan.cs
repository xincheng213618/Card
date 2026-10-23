using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource SunHuanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardMoveReason SunHuanDrawReason => new("skill.anniversary.niji.draw");
    private static CardMoveReason SunHuanDiscardReason => new("skill.anniversary.niji.discard");
    private bool HasAnniversarySunHuan => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw);
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 45400)] private bool SunHuanTargetWindow(ProgramTargetCardWindowQuery q) => HasAnniversarySunHuan;
    [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 45400)] [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 45400)]
    private bool SunHuanImplicitTarget(ProgramTargetCardWindowQuery q) => HasAnniversarySunHuan;
    [ProgramCapability] private sealed class AnniversarySunHuanHost(GameEngine engine) : IAnniversarySunHuanHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginSunHuan(f, op); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 45400, SkillProgramEffectOp.ResolveAnniversarySunHuanNijiEnding)]
    private bool CanOfferSunHuanEnding(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => SunHuanDraws(c.OwnerSeat, c.SkillInstanceId).Any(d => d.Outcome.ActualDrawIds.Any(id => _cardZones.Contains(CardLocation.Hand(c.OwnerSeat), id)));
    private IEnumerable<AnniversarySunHuanDraw> SunHuanDraws(int seat, string instance) => _players[seat].AnniversaryNijiDraws.Where(d => d.Turn == _turnNumber && d.TurnOwner == _currentSeat && d.Source.SkillInstanceId == instance);
    private IReadOnlyList<int> SunHuanHand(ProgramSkillFrame f) => Array.AsReadOnly(SunHuanDraws(f.OwnerSeat, f.SkillInstanceId).SelectMany(d => d.Outcome.ActualDrawIds).Distinct().Where(id => _cardZones.Contains(CardLocation.Hand(f.OwnerSeat), id)).ToArray());
    private SkillProgramStepOutcome BeginSunHuan(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversarySunHuan is not null || f.InstructionIndex != 1 || f.SkillId != AnniversarySunHuanComposition.Skill || f.WindowContext is not { } w ||
            w.Window != (op == SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.TurnEnding))
            throw new InvalidOperationException("Niji requires its accepted actual card target or actual ending.");
        var draw = op == SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw;
        var r = new AnniversarySunHuanReceipt(SunHuanSource(f), f.GameplayHash, _turnNumber, _currentSeat, w.ParentFrameId, op, draw ? AnniversarySunHuanStage.Drawing : AnniversarySunHuanStage.Choice);
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r });
        AdvanceEventRulesAndQueueFact(new AnniversarySunHuanStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId, op));
        if (draw) return BeginOperationProgramDraw(f, f.OwnerSeat, 1, SunHuanDrawReason);
        if (!_players[f.OwnerSeat].IsAlive || SunHuanHand(f).Count == 0) { FinishSunHuan(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishSunHuan(f); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw)] private void ReturnSunHuanDraw(ProgramSkillFrame supplied, NativeDrawOutcome d)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversarySunHuan!;
        if (r.Stage != AnniversarySunHuanStage.Drawing || r.Draw is not null || d.RecipientSeat != f.OwnerSeat || d.RequestedCount != 1 || d.SequenceAfter != _movementSequence || r.Turn != _turnNumber)
            throw new InvalidOperationException("Niji must retain its exact once-returned native draw.");
        var owner = _players[f.OwnerSeat];
        // Retain only the current actual turn, independent of subsequent Hand exits/re-entry.
        owner.AnniversaryNijiDraws = owner.AnniversaryNijiDraws.Where(x => x.Turn == r.Turn).Append(new(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, d)).ToArray();
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.DrawChildren, Draw = d, Before = d.SequenceBefore, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversarySunHuanDrawReturnedEvent(f.Id, f.OwnerSeat, r.Turn, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter, d.AttemptFrameId));
        ContinueSunHuan(f.Id);
    }
    private bool IsAnniversarySunHuanExactCardQuery(CharacterState actor, int? selectedPhysicalCardId)
    {
        if (IsSunHuanDeclarationQuery(actor, selectedPhysicalCardId)) return true;
        return selectedPhysicalCardId is { } id && id > 0 && actor.IsAlive && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversarySunHuan: { Stage: AnniversarySunHuanStage.Choice or AnniversarySunHuanStage.IssuingUse } } f &&
            f.OwnerSeat == actor.Seat && ValidSunHuan(f) && SunHuanHand(f).Contains(id);
    }
    private IReadOnlyList<LegalAction> SunHuanActions(ProgramSkillFrame f) => !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ? [] : Array.AsReadOnly(SunHuanHand(f).SelectMany(id =>
        BuildLegalActions(_players[f.OwnerSeat], includeProgramActions: false, selectedPhysicalCardId: id).Where(a => a.CardId == id && a.MinCardCount <= 1 && a.MaxCardCount <= 1 &&
            (a.ConversionSource is null || ViewAsRule(a.ConversionSource) is not { } rule || rule.InputCount == 1) &&
            a.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral))).Select(FreezeSunHuanAction).ToArray());
    private static LegalAction FreezeSunHuanAction(LegalAction a) => a with
    { TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()), SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()), SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()), SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.ToArray())).ToArray()) };
    private IReadOnlyList<PromptChoice> SunHuanChoices(ProgramSkillFrame f) => Array.AsReadOnly(SunHuanActions(f).Select((a, i) => new PromptChoice(new($"niji.{f.Id}.use.{i}"), a.Description,
        [a.CardId!.Value], a.TargetSeats, new Dictionary<string, string> { ["program-action"] = "anniversary-niji", ["index"] = i.ToString(CultureInfo.InvariantCulture) }))
        .Append(new(new($"niji.{f.Id}.skip"), "不使用牌，弃置仍在手中的本回合逆击牌", [], [], new Dictionary<string, string> { ["program-action"] = "anniversary-niji", ["index"] = "skip" })).ToArray());
    private void PublishSunHuan(ProgramSkillFrame f)
    {
        var choices = SunHuanChoices(f); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "逆击：可以先使用本回合逆击摸到的一张手牌，然后弃置其余对应手牌。", SunHuanHand(f), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, "逆击", "逆击", _contentRegistry.GetSkill(f.SkillId).Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversarySunHuanNijiEnding)] private PromptChoice ChooseSunHuan(PendingDecision p) => p.Choices.First();
    [ProgramAnswer("anniversary-niji")] private void AnswerSunHuan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunHuan: { Stage: AnniversarySunHuanStage.Choice } r } f || !ValidSunHuan(f) || !SunHuanChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Niji rejects stale drawn material or illegal native targets.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["index"] == "skip") { DiscardSunHuan(f); return; }
        var a = SunHuanActions(f)[int.Parse(choice.Parameters["index"], CultureInfo.InvariantCulture)];
        var n = new AnniversarySunHuanNative(f.Id, f.InstructionIndex, r.Source, r.Hash, r.Turn, f.OwnerSeat, a.CardId!.Value, a, 0, 0);
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.IssuingUse, Native = n, Before = _movementSequence, After = _movementSequence } });
        ExecuteAction(_players[f.OwnerSeat], a);
    }
    private bool DrainSunHuan(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 45400)] private bool ContinueSunHuan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunHuan: { } r } f || f.Id != id) return false;
        RequireSunHuan(f);
        if (r.Stage is AnniversarySunHuanStage.Drawing or AnniversarySunHuanStage.IssuingUse or AnniversarySunHuanStage.NativeUse) return true;
        if (DrainSunHuan(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversarySunHuan!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == AnniversarySunHuanStage.Choice) { if (_players[f.OwnerSeat].IsAlive) PublishSunHuan(f); else DiscardSunHuan(f); return true; }
        if (r.Stage == AnniversarySunHuanStage.NativeTail) { DiscardSunHuan(f); return true; }
        FinishSunHuan(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 45400)] private bool ReturnSunHuanMovement(ProgramSkillFrame f) => f.AnniversarySunHuan is not null && ContinueSunHuan(f.Id);
    private void DiscardSunHuan(ProgramSkillFrame f)
    {
        var r = f.AnniversarySunHuan!; var ids = _players[f.OwnerSeat].IsAlive ? SunHuanHand(f).Where(id => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, GetAdvancedCard(id), CardLocation.Hand(f.OwnerSeat), OwnedCardMoveIntent.Discard)).ToArray() : [];
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = r with { Stage = AnniversarySunHuanStage.DiscardChildren, Cards = ids, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, SunHuanDiscardReason);
        var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeFrame(f.Id, current with { AnniversarySunHuan = current.AnniversarySunHuan! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new AnniversarySunHuanDiscardedEvent(f.Id, ids.Length, before, _movementSequence));
        ContinueSunHuan(f.Id);
    }
    private void FinishSunHuan(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { AnniversarySunHuan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
}
