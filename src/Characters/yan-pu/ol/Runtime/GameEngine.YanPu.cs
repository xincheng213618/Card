using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string YanPuGiftReason = "skill-program.yan-pu.huantu.gift";
    [ProgramCapability]
    private sealed class YanPuCapability(GameEngine engine) : IYanPuHost
    { public SkillProgramStepOutcome ExecuteYanPu(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginYanPu(f, op); }
    private SkillProgramStepOutcome BeginYanPu(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.YanPu is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context)
            throw new InvalidOperationException("YanPu requires a new exact actual-boundary instruction.");
        var c = op == SkillProgramEffectOp.ResolveHuantuEnding ? YanPuCurrentEndingFrameCandidate(f) ?? throw new InvalidOperationException("Huantu Ending lost its exact current candidate.") :
            new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        YanPuHuantuPromise? promise = null; YanPuDyingExitReturn? exited = null;
        var stage = op == SkillProgramEffectOp.GiveCardSkipDrawForEnding ? YanPuStage.GiftChoice : op == SkillProgramEffectOp.ResolveHuantuEnding ? YanPuStage.EndingChoice : YanPuStage.ExitDrawChildren;
        if (op == SkillProgramEffectOp.GiveCardSkipDrawForEnding && !IsYanPuDrawContext(c, context) ||
            op == SkillProgramEffectOp.ResolveHuantuEnding && (promise = CurrentYanPuEnding(c, context)) is null ||
            op == SkillProgramEffectOp.DrawDyingExitRoundDistance && !IsYanPuDyingExitContext(c, context))
            throw new InvalidOperationException("YanPu lost its actual Draw, issued Ending or survived Dying parent.");
        if (op == SkillProgramEffectOp.DrawDyingExitRoundDistance)
            exited = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId).YanPuDyingExit!;
        var r = new YanPuReceipt { Source = YanPuSource(f), GameplayHash = f.GameplayHash, InstructionIndex = f.InstructionIndex,
            WindowId = context.ParentFrameId, Round = _roundNumber, Turn = _turnNumber, TurnOwner = _currentSeat,
            Target = promise?.RecipientSeat ?? context.TargetSeat!.Value, Stage = stage, Promise = promise, Exited = exited };
        ReplaceRuntimeTop(f = f with { YanPu = r });
        AdvanceEventRulesAndQueueFact(new YanPuStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Round, r.Turn, r.TurnOwner, r.Target, stage));
        if (promise is not null)
        {
            if (YanPuPromiseConsumed(promise.Id)) throw new InvalidOperationException("An issued Huantu Ending cannot be consumed twice.");
            AdvanceEventRulesAndQueueFact(new YanPuHuantuConsumedEvent(promise.Id, context.ParentFrameId, f.Id));
        }
        if (exited is not null) { IssueYanPuDraw(f, r.Target, 3, YanPuStage.ExitDrawChildren); return SkillProgramStepOutcome.AwaitChild; }
        PublishYanPuChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> YanPuChoices(ProgramSkillFrame f)
    {
        var r = f.YanPu!;
        PromptChoice Choice(string option, string label, IReadOnlyList<int>? ids = null) => new(new($"yanpu.{f.Id}.{r.Stage}.{option}.{string.Join('.', ids ?? [])}"), label,
            ids ?? [], [], new Dictionary<string,string> { ["program-action"] = "yan-pu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
        if (r.Stage == YanPuStage.GiftChoice)
        {
            var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext!.OccurrenceIndex);
            if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !IsYanPuDrawContext(c, f.WindowContext)) return [];
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer)
                .Select(c => Choice("gift", $"交给{_players[r.Target].Name}【{GetAdvancedCard(c.Cards.Single()).DisplayName}】并跳过此次摸牌阶段", c.Cards)).ToArray());
        }
        if (r.Stage == YanPuStage.EndingChoice)
            return [Choice("recover-draw", "令其回复1点体力并摸两张牌"), Choice("draw-give", "你摸三张牌并交给其两张手牌")];
        if (r.Stage == YanPuStage.EndingGiftChoice)
            return Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Where(c => !r.SelectedCards.Contains(c.Id)).Select(c =>
                Choice("ending-card", $"交给{_players[r.Target].Name}【{c.DisplayName}】（{r.SelectedCards.Count + 1}/{r.RequiredGift}）", [c.Id])).ToArray());
        return [];
    }
    private void PublishYanPuChoice(ProgramSkillFrame f)
    {
        var choices = YanPuChoices(f);
        if (choices.Count == 0) { CompleteYanPu(f, f.YanPu!.Promise is not null); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.YanPu!.Stage == YanPuStage.GiftChoice ? "缓图：交给其一张牌。" : "缓图：结算本次实际回合的收益。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("yan-pu")]
    private void AnswerYanPu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YanPu: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], YanPuChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("YanPu lost its exact private chooser or current owned physical payment.");
        AssertYanPu(f); ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Stage != YanPuStage.EndingChoice && !_players[r.Target].IsAlive)
        { CompleteYanPu(f, r.Promise is not null); return; }
        var option = choice.Parameters["option"];
        if (option == "gift")
        {
            var id = choice.Cards.Single(); var from = _cardZones.GetLocation(id); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { YanPu = r with { Stage = YanPuStage.GiftChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(r.Target), new(YanPuGiftReason), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,r.Target,[id],_movementSequence,(_, movements) =>
            {
                var root = GetActiveProgramFrame(f.Id); var move = movements.Single(); var paid = root.YanPu!;
                var promise = new YanPuHuantuPromise(root.Id, paid.Source, paid.GameplayHash, paid.WindowId, paid.Round, paid.Turn, paid.TurnOwner, paid.Target, id, from, move.Sequence);
                var parent = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(p => p.Id == paid.WindowId);
                if (parent.ActualPhaseSubstitution is not null || parent.OwnerSeat != paid.Target || parent.Window != SkillProgramTriggerWindow.DrawPhaseStarting)
                    throw new InvalidOperationException("A paid Huantu gift cannot replace another actual phase substitution.");
                ReplaceRuntimeFrame(parent.Id, parent with { ActualPhaseSubstitution = new(parent.Id, root.Id, paid.Target, root.SkillId, root.SkillInstanceId, paid.Turn, SkillProgramTurnPhase.Draw) });
                ReplaceRuntimeTop(root with { YanPu = paid with { Promise = promise } });
                AdvanceEventRulesAndQueueFact(new YanPuHuantuIssuedEvent(promise));
                AdvanceEventRulesAndQueueFact(new ProgramTurnPhaseSubstitutedEvent(root.Id, root.SkillId, root.TriggerId!, root.SkillInstanceId, paid.Target, paid.Turn, SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramTurnPhase.Draw));
                CommitYanPuMovement(root.Id, new(0, root.OwnerSeat, YanPuGiftReason, before, move.Sequence, 1, id, from, CardLocation.Hand(paid.Target)));
            }));
            // Equipment-removal recovery can have queued native children after the payment callback.
            ContinueYanPu(f.Id); return;
        }
        if (option == "recover-draw")
        {
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { YanPu = r with { Stage = YanPuStage.RecoveryChildren }, PendingMovementContinuation = new(r.Target, 0, null) });
            if (_players[r.Target].IsAlive) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.Target, 1, null, null);
            CommitYanPuMovement(f.Id, new(r.Movements.Count, r.Target, "skill-program.yan-pu.huantu.recover", before, _movementSequence, 0));
            ContinueYanPu(f.Id); return;
        }
        if (option == "draw-give") { IssueYanPuDraw(f, f.OwnerSeat, 3, YanPuStage.OwnerDrawChildren); return; }
        if (option != "ending-card") throw new InvalidOperationException("Unknown YanPu instruction choice.");
        var selected = r.SelectedCards.Append(choice.Cards.Single()).ToArray();
        ReplaceRuntimeTop(f = f with { YanPu = r with { SelectedCards = selected } });
        if (selected.Length < r.RequiredGift) { PublishYanPuChoice(f); return; }
        var sequence = _movementSequence;
        ReplaceRuntimeTop(f = f with { YanPu = f.YanPu! with { Stage = YanPuStage.EndingGiftChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(selected, CardLocation.Hand(r.Target), new("skill-program.yan-pu.huantu.ending-gift"), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,r.Target,selected,_movementSequence,(_, movements) =>
            CommitYanPuMovement(f.Id, new(r.Movements.Count, f.OwnerSeat, "skill-program.yan-pu.huantu.ending-gift", sequence, movements.Max(m => m.Sequence), movements.Count))));
        ContinueYanPu(f.Id);
    }
    private void CommitYanPuMovement(long id, YanPuMovement movement)
    {
        var f = GetActiveProgramFrame(id); var r = f.YanPu!;
        ReplaceRuntimeTop(f with { YanPu = r with { Movements = r.Movements.Append(movement).ToArray() } });
        AdvanceEventRulesAndQueueFact(new YanPuMovementIssuedEvent(id, movement));
    }
    private void IssueYanPuDraw(ProgramSkillFrame f, int subject, int count, YanPuStage stage)
    {
        var r = f.YanPu!; var before = _movementSequence; var reason = stage == YanPuStage.ExitDrawChildren ? "skill-program.yan-pu.bihuo.draw" : "skill-program.yan-pu.huantu.draw";
        ReplaceRuntimeTop(f = f with { YanPu = r with { Stage = stage }, PendingMovementContinuation = new(subject, 0, null) });
        var actual = _players[subject].IsAlive && _winner == Winner.None ? DrawCards(_players[subject], count, true, new(reason)).Count : 0;
        CommitYanPuMovement(f.Id, new(r.Movements.Count, subject, reason, before, _movementSequence, actual));
        ContinueYanPu(f.Id);
    }
    private bool DrainYanPuChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueYanPu(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { YanPu: { } r } f && f.Id == id)
        {
            AssertYanPu(f);
            if (r.Stage is YanPuStage.GiftChoice or YanPuStage.EndingChoice or YanPuStage.EndingGiftChoice)
            {
                if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Stage != YanPuStage.EndingChoice && !_players[r.Target].IsAlive)
                { CompleteYanPu(f, r.Promise is not null); return; }
                if (_pendingDecision is null) PublishYanPuChoice(f); return;
            }
            if (DrainYanPuChildren(f)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.YanPu!;
            if (_winner != Winner.None) { CompleteYanPu(f, true); return; }
            switch (r.Stage)
            {
                case YanPuStage.GiftChildren: CompleteYanPu(f, true); return;
                case YanPuStage.RecoveryChildren: IssueYanPuDraw(f, r.Target, 2, YanPuStage.RecipientDrawChildren); return;
                case YanPuStage.RecipientDrawChildren:
                case YanPuStage.EndingGiftChildren: CompleteYanPu(f, true); return;
                case YanPuStage.OwnerDrawChildren:
                    var n = Math.Min(2, GetHand(_players[f.OwnerSeat]).Count);
                    if (n == 0 || !_players[f.OwnerSeat].IsAlive || !_players[r.Target].IsAlive) { CompleteYanPu(f, true); return; }
                    ReplaceRuntimeTop(f = f with { YanPu = r with { Stage = YanPuStage.EndingGiftChoice, RequiredGift = n } }); PublishYanPuChoice(f); return;
                case YanPuStage.ExitDrawChildren:
                    if (_players[r.Target].IsAlive)
                        AdvanceEventRulesAndQueueFact(new YanPuRoundDistanceIssuedEvent(f.Id, r.Source, r.GameplayHash, r.Round, r.Turn, r.Exited!.DyingFrameId, r.Target));
                    CompleteYanPu(f, true); return;
                default: throw new InvalidOperationException("Unknown YanPu paid continuation.");
            }
        }
    }
    private void CompleteYanPu(ProgramSkillFrame f, bool applied)
    {
        AdvanceEventRulesAndQueueFact(new YanPuCompletedEvent(f.Id, applied));
        ReplaceRuntimeTop(f = f with { YanPu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26410)]
    private bool ResumeYanPu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YanPu: not null } f || f.Id != id) return false; ContinueYanPu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26410)]
    private bool ReturnYanPuMovement(ProgramSkillFrame f)
    { if (f.YanPu is null) return false; ContinueYanPu(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 660)]
    private bool IsYanPuAwaitedMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.YanPu is { Stage: not (YanPuStage.GiftChoice or YanPuStage.EndingChoice or YanPuStage.EndingGiftChoice), Movements.Count: > 0 } r &&
        (e is null || YanPuComposition.IsOperation(e.Op)) && p.BeforeCount == 0 && p.CoverageResultBind is null && p.SubjectSeat == r.Movements[^1].SubjectSeat;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26410)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26410)]
    private bool ContinuePaidYanPu(ProgramSkillFrame f)
    {
        if (f.YanPu is { } r) return (r.Promise is not null || r.Exited is not null) &&
            ProgramEventHistory<YanPuStartedEvent>().Any(e => e.ProgramFrameId == f.Id && e.Source == r.Source);
        // An issued Ending must also be allowed to enter its first instruction
        // after the original skill instance disappeared. Its authority is the
        // exact unconsumed gift item, not the absent source or a parallel debt.
        if (f.TriggerId is null || f.InstructionIndex is < 0 or > 1 || !_players[f.OwnerSeat].IsAlive || f.WindowContext is not { } context) return false;
        return YanPuCurrentEndingFrameCandidate(f) is { } c && CurrentYanPuEnding(c, context) is { } p && !YanPuPromiseConsumed(p.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.GiveCardSkipDrawForEnding, SkillProgramEffectOp.ResolveHuantuEnding, SkillProgramEffectOp.DrawDyingExitRoundDistance)]
    private PromptChoice ChooseAiYanPu(PendingDecision d, ProgramSkillFrame f) =>
        d.Choices.FirstOrDefault(c => c.Parameters["option"] == (_players[f.YanPu!.Target].IsAlive ? "recover-draw" : "draw-give")) ??
        d.Choices.OrderBy(c => c.Cards.Count == 0 ? 0 : GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
}
