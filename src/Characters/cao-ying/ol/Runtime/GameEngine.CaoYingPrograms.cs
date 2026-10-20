using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private CardConversionSource CaoYingSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private bool CaoYingSourceCurrent(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 330)]
    private bool CanOfferCaoYing(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c)
    {
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage }]) return true;
        var use = c.CardUse is { } action ? LifecycleCardUse(action.ParentCardUseFrameId) : null;
        return use is { Action: { Type: CardActionType.Use } a } && a.ActorSeat == candidate.OwnerSeat &&
            a.ActionId == c.CardUse!.CardActionId && CaoYingDamageKind(a.EffectiveKind) && use.TargetSeats.Any(t => _players[t].IsAlive);
    }
    private SkillProgramStepOutcome BeginCaoYing(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.CaoYing is not null || f.WindowContext is not { } context || !CaoYingSourceCurrent(f)) throw new InvalidOperationException("Cao Ying requires an unpaid current source.");
        if (effect.Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage)
        {
            if (!CanOfferCaoYing(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), GetProgramTrigger(f), context)) throw new InvalidOperationException("Lingren lost its real finalized actor use.");
            var use = LifecycleCardUse(context.CardUse!.ParentCardUseFrameId)!;
            ReplaceRuntimeTop(f = f with { CaoYing = new(f.InstructionIndex, context.ParentFrameId, use.Id, use.Action!.ActionId, _turnNumber, _currentSeat, CaoYingStage.Target) { Source = CaoYingSource(f), GameplayHash = f.GameplayHash } });
        }
        else
        {
            if (context.Window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding) || f.OwnerSeat != _currentSeat) throw new InvalidOperationException("Fujian requires its own preparation or ending.");
            var living = _players.Where(p => p.IsAlive).ToArray(); var maximum = living.Max(p => GetHand(p).Count);
            var uniqueMaximum = living.Count(p => GetHand(p).Count == maximum) == 1;
            var candidates = living.Where(p => p.Seat != f.OwnerSeat && (!uniqueMaximum || GetHand(p).Count != maximum)).ToArray();
            if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
            var target = candidates[_random.Next(candidates.Length)];
            ReplaceRuntimeTop(f = f with { CaoYing = new(f.InstructionIndex, context.ParentFrameId, null, null, _turnNumber, _currentSeat, CaoYingStage.Inspection, target.Seat) { Cards = GetHand(target).Select(ToSnapshot).ToArray(), Source = CaoYingSource(f), GameplayHash = f.GameplayHash } });
            AdvanceEventRulesAndQueueFact(new CaoYingInspectionIssuedEvent(f.Id, f.OwnerSeat, target.Seat, f.CaoYing.Cards.Count));
        }
        PublishCaoYingChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice CaoYingChoice(ProgramSkillFrame f, string branch, string label, IReadOnlyList<int>? targets = null) => new(new($"cao-ying.{f.Id}.{f.CaoYing!.Stage}.{branch}"), label, [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "cao-ying", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> CaoYingChoices(ProgramSkillFrame f)
    {
        var r = f.CaoYing!;
        if (r.Stage == CaoYingStage.Target) return Array.AsReadOnly(LifecycleCardUse(r.UseId!.Value)!.TargetSeats.Where(t => _players[t].IsAlive).Distinct().Select(t => CaoYingChoice(f, $"target-{t}", $"猜测{_players[t].Name}的手牌类型", [t])).ToArray());
        if (r.Stage == CaoYingStage.Inspection) return [CaoYingChoice(f, "done", "观看完毕")];
        if (r.Stage != CaoYingStage.Guess) return [];
        return Array.AsReadOnly(Enumerable.Range(0, 8).Select(mask => CaoYingChoice(f, $"guess-{mask}", $"基本牌：{((mask & 1) != 0 ? "有" : "无")}；锦囊牌：{((mask & 2) != 0 ? "有" : "无")}；装备牌：{((mask & 4) != 0 ? "有" : "无")}")).ToArray());
    }
    private void PublishCaoYingChoice(ProgramSkillFrame f)
    {
        var choices = CaoYingChoices(f); if (choices.Count == 0) { FinishCaoYing(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.CaoYing!.Stage == CaoYingStage.Inspection ? "观看随机角色的全部手牌。" : "请选择目标及三类手牌的有无。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.CaoYing.Target, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("cao-ying")]
    private void ResolveCaoYingChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Cao Ying lost its owning frame."); RequireCaoYing(f); var r = f.CaoYing!;
        if (_pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], CaoYingChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Cao Ying selection changed while suspended.");
        ClearPendingDecision();
        if (!CaoYingSourceCurrent(f)) { FinishCaoYing(f); return; }
        if (r.Stage == CaoYingStage.Inspection) { FinishCaoYing(f); return; }
        if (r.Stage == CaoYingStage.Target)
        {
            var target = choice.Targets.Single(); var hand = GetHand(_players[target]);
            var mask = (hand.Any(c => MatchesSkillProgramCardCategory(c.Kind, SkillProgramCardCategory.Basic)) ? 1 : 0) | (hand.Any(c => MatchesSkillProgramCardCategory(c.Kind, SkillProgramCardCategory.Trick)) ? 2 : 0) | (hand.Any(c => MatchesSkillProgramCardCategory(c.Kind, SkillProgramCardCategory.Equipment)) ? 4 : 0);
            ReplaceRuntimeTop(f = f with { CaoYing = r with { Stage = CaoYingStage.Guess, Target = target, FrozenCategories = mask } }); PublishCaoYingChoice(f); return;
        }
        var guess = int.Parse(choice.Parameters["branch"][6..], CultureInfo.InvariantCulture); var correct = 3 - System.Numerics.BitOperations.PopCount((uint)(guess ^ r.FrozenCategories));
        ConsumeProgramTriggerUsage(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority));
        ReplaceRuntimeTop(f = f with { CaoYing = r with { Guess = guess, Correct = correct, Stage = CaoYingStage.DrawChildren, MovementBefore = _movementSequence, MovementAfter = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceEventRulesAndQueueFact(new CaoYingGuessResolvedEvent(f.Id, CaoYingSource(f), r.TurnNumber, r.TurnOwner, r.UseId!.Value, r.ActionId, r.Target!.Value, correct));
        if (correct >= 2) RefundProgramCardUseDebit(f);
        if (correct >= 3) AcquireRuntimeSkills(_players[f.OwnerSeat], $"cao-ying:{f.OwnerSeat}:{f.SkillInstanceId}:{r.TurnNumber}:{f.Id}", GetProgramTrigger(f).Effects.Single().SkillIds);
        var drawn = correct >= 2 && _players[f.OwnerSeat].IsAlive ? DrawCards(_players[f.OwnerSeat], 2, true, new("skill-program.cao-ying.draw")).Count : 0;
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { CaoYing = f.CaoYing! with { MovementAfter = _movementSequence, DrawCount = drawn } });
        if (!DrainCaoYingChildren(f)) FinishCaoYing(GetActiveProgramFrame(f.Id));
    }
    private bool DrainCaoYingChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void FinishCaoYing(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { CaoYing = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21800)]
    private bool ResumeCaoYing(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CaoYing: { Stage: CaoYingStage.DrawChildren } } f || f.Id != id) return false;
        RequireCaoYing(f); if (!DrainCaoYingChildren(f)) FinishCaoYing(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 21800)]
    private bool ReturnCaoYingMovement(ProgramSkillFrame f)
    { if (f.CaoYing?.Stage != CaoYingStage.DrawChildren) return false; RequireCaoYing(f); if (!DrainCaoYingChildren(f)) FinishCaoYing(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 70)]
    private bool IsCaoYingMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => CanContinueCaoYing(f) && e?.Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage && p.SubjectSeat == f.OwnerSeat && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50)]
    private bool CanContinueCaoYing(ProgramSkillFrame f) => f.CaoYing is { Stage: CaoYingStage.DrawChildren, Correct: >= 0 } && ValidCaoYing(f);
    private bool ValidCaoYing(ProgramSkillFrame f)
    {
        var r = f.CaoYing!;
        return r.Source == CaoYingSource(f) && r.GameplayHash == f.GameplayHash && r.InstructionIndex == f.InstructionIndex &&
            r.WindowId == f.WindowContext?.ParentFrameId && r.UseId == f.WindowContext?.CardUse?.ParentCardUseFrameId && r.ActionId == f.WindowContext?.CardUse?.CardActionId &&
            GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage && r.TurnNumber == _turnNumber && r.TurnOwner == _currentSeat &&
            f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == f.OwnerSeat &&
            LifecycleCardUse(r.UseId!.Value) is { Action: { } action } use && action.ActionId == r.ActionId && action.ActorSeat == f.OwnerSeat && use.TargetSeats.Contains(r.Target!.Value) &&
            r.MovementBefore <= r.MovementAfter && r.MovementAfter <= _movementSequence && r.DrawCount is >= 0 and <= 2 &&
            CompleteProgramEventHistory().OfType<CaoYingGuessResolvedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Source == r.Source && e.TurnNumber == r.TurnNumber && e.TurnOwner == r.TurnOwner && e.UseId == r.UseId && e.ActionId == r.ActionId && e.TargetSeat == r.Target && e.CorrectCount == r.Correct) == 1 &&
            _cardMovements.Count(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter && m.Reason.Value == "skill-program.cao-ying.draw" && m.To == CardLocation.Hand(f.OwnerSeat)) == r.DrawCount;
    }
    private void RequireCaoYing(ProgramSkillFrame f)
    {
        if (f.CaoYing is not { } r || r.Source != CaoYingSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || f.TriggerId is null || r.WindowId != f.WindowContext?.ParentFrameId || r.FrozenCategories is < 0 or > 7 ||
            r.Stage == CaoYingStage.DrawChildren && !ValidCaoYing(f) ||
            (r.Stage == CaoYingStage.DrawChildren ? r.Guess is < 0 or > 7 || r.Correct != 3 - System.Numerics.BitOperations.PopCount((uint)(r.Guess ^ r.FrozenCategories)) : r.Guess != -1 || r.Correct != -1) ||
            (r.Stage == CaoYingStage.Inspection ? r.Target is null || r.Target == f.OwnerSeat || r.UseId is not null : r.UseId != f.WindowContext?.CardUse?.ParentCardUseFrameId || r.ActionId != f.WindowContext?.CardUse?.CardActionId))
            throw new InvalidOperationException("Cao Ying receipt lost its frozen instruction, action or private category decision.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 110)]
    private void AssertRegisteredCaoYing(ProgramSkillFrame frame)
    {
        if (frame.CaoYing is not null) RequireCaoYing(frame);
    }
    private bool CaoYingFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!CanContinueCaoYing(root) || root.CaoYing is not { } r) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return r.Correct == 3 && skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is not CardsMovedTriggerWindowFrame moved) return false;
        return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == "skill-program.cao-ying.draw");
    }
    private ProgramSkillFrame? CaoYingObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.CaoYingFirstChild(root, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 140)]
    private bool HasCaoYingDamageObserver(long id) => CaoYingObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 180)]
    private bool IsCaoYingDying() => ActiveDying is { } dying && CaoYingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 380)]
    private bool AllowsCaoYingNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            CaoYingObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 110)]
    private bool TryAdvanceCaoYingSubtree() => _pendingDecision is null && CaoYingObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramCapability]
    private sealed class CaoYingProgramCapability(GameEngine engine) : ICaoYingProgramHost
    { public SkillProgramStepOutcome BeginCaoYing(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginCaoYing(f, e); }
}
