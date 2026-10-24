using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class ShiHeQiHost(GameEngine engine) : IShiHeQiHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginShiHq(f, op); }
    private SkillProgramStepOutcome BeginShiHq(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShiHeQi is not null || f.InstructionIndex != 1 || f.SkillId != ShiHqQizhou || f.WindowContext is not { } w ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            GetProgramTrigger(f).Effects is not [{ } e] || e.Op != op ||
            op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark && (f.TriggerId != "field-entry-mark" || w.MovementBatch is not { } batch || !batch.Movements.Any(m => ShiHqEntersField(m, f.OwnerSeat))) ||
            op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw && (f.TriggerId != "draw-extra-yingji" || w.Window != SkillProgramTriggerWindow.DrawPhaseStarting || _currentSeat != f.OwnerSeat) ||
            op == SkillProgramEffectOp.ResolveMobileShiHeQiYingjiCleanup && !IsIssuedShiHqCleanupCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex), w))
            throw new InvalidOperationException("Qizhou requires its original actual field entry, draw phase or issued whole-turn cleanup.");
        var stage = op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark ? ShiHqStage.Target : op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw ? ShiHqStage.Drawing : ShiHqStage.CleanupChildren;
        var r = new ShiHqReceipt(ShiHqSource(f), f.GameplayHash, _turnNumber, _currentSeat, w.ParentFrameId, op, stage) { Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { ShiHeQi = r });
        AdvanceEventRulesAndQueueFact(new ShiHqStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId, op));
        if (stage == ShiHqStage.Target) { PublishShiHq(f); return SkillProgramStepOutcome.AwaitChoice; }
        if (stage == ShiHqStage.Drawing) { BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(ShiHqDrawReason)); return SkillProgramStepOutcome.AwaitChild; }
        var materials = _players.SelectMany(p => GetHand(p).Where(c => HasShiHqYingji(c.Id)).Select(c => new ShiHqMaterial(c.Id, c.Kind, CardLocation.Hand(p.Seat)))).ToArray();
        var marks = materials.ToDictionary(m => m.CardId, m => ShiHqMark(m.CardId)!);
        ReplaceRuntimeTop(f = f with { ShiHeQi = r with { Materials = materials }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // This is the marked-card cleanup rule, not a logical discard or cost.
        if (materials.Length > 0) MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(), CardLocation.DiscardPile,
            new(ShiHqCleanupReason), disclosure: CardMovementDisclosure.FaceUp);
        var paid = _cardMovements.Where(m => m.Sequence > r.Before && materials.Any(c => c.CardId == m.CardId && c.From == m.From)).ToArray();
        foreach (var movement in paid)
        {
            var material = materials.Single(m => m.CardId == movement.CardId);
            AdvanceEventRulesAndQueueFact(new ShiHqCleanupMaterialEvent(f.Id, material, movement.Sequence));
            AdvanceEventRulesAndQueueFact(new ShiHqYingjiClearedEvent(marks[material.CardId].MarkId, f.Id, material.CardId, movement.Sequence));
        }
        var paidBatch = _pendingCardsMovedBatches.SingleOrDefault(b => b.ParentFrameId == f.Id && b.ParentBatchId is null && paid.Any(m => b.Movements.Contains(m)))?.Id;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShiHeQi = GetActiveProgramFrame(f.Id).ShiHeQi! with { After = _movementSequence, BatchId = paidBatch } });
        AdvanceEventRulesAndQueueFact(new ShiHqCleanupIssuedEvent(f.Id, r.Turn, r.TurnOwner, materials.Length, r.Before, _movementSequence));
        ContinueShiHq(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ShiHqChoices(ProgramSkillFrame f)
    {
        var r = f.ShiHeQi!;
        if (r.Stage == ShiHqStage.Target) return Array.AsReadOnly(_players.Where(p => p.IsAlive && GetHand(p).Count > 0).Select(p =>
            new PromptChoice(new($"shi-he-qi.{f.Id}.target.{p.Seat}"), $"将 {p.Name} 的一张手牌标记为应机牌", [], [p.Seat],
                new Dictionary<string, string> { ["program-action"] = "mobile-shi-he-qi", ["branch"] = "target" })).ToArray());
        if (r.Stage != ShiHqStage.Card || !_players[r.Target].IsAlive) return [];
        return Array.AsReadOnly(r.Materials.Select((m, i) => (m, i)).Where(x => _cardZones.GetLocation(x.m.CardId) == x.m.From).Select(x =>
            new PromptChoice(new($"shi-he-qi.{f.Id}.slot.{x.i}"), r.Target == f.OwnerSeat ? $"标记 {GetHand(_players[r.Target]).Single(c => c.Id == x.m.CardId).DisplayName}" : $"标记第 {x.i + 1} 张手牌",
                r.Target == f.OwnerSeat ? [x.m.CardId] : [], [r.Target], new Dictionary<string, string> { ["program-action"] = "mobile-shi-he-qi", ["branch"] = "card", ["slot"] = x.i.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private void PublishShiHq(ProgramSkillFrame f)
    {
        var choices = ShiHqChoices(f); if (choices.Count == 0) { FinishShiHq(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.ShiHeQi!.Stage == ShiHqStage.Target ? "绮胄：选择一名角色。" : "绮胄：选择其一张手牌标记为应机牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mobile-shi-he-qi")]
    private void AnswerShiHq(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHeQi: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiHqChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Qizhou changed its current private target or opaque hand-slot selection.");
        RequireShiHq(f); ClearPendingDecision();
        if (r.Stage == ShiHqStage.Target)
        {
            var target = choice.Targets.Single(); var cards = GetHand(_players[target]).Select(c => new ShiHqMaterial(c.Id, c.Kind, CardLocation.Hand(target))).ToArray();
            ReplaceRuntimeTop(f = f with { ShiHeQi = r with { Stage = ShiHqStage.Card, Target = target, Materials = cards } });
            PublishShiHq(f); AdvanceRulesAndPublishState(); return;
        }
        var material = r.Materials[int.Parse(choice.Parameters["slot"], CultureInfo.InvariantCulture)];
        var reward = GetProgramBooleanState(f, "next-mark-draw");
        // Consume the previous obligation before this successful invocation can
        // sign its next one. A selected Slash therefore cannot reward itself.
        SetProgramBooleanState(f, "next-mark-draw", false);
        MarkShiHqEntity(f, material.CardId, r.Target, ShiHqMarkOrigin.SelectedHand);
        var next = IsSlashCard(material.Kind);
        if (next) SetProgramBooleanState(f, "next-mark-draw", true);
        AdvanceEventRulesAndQueueFact(new ShiHqSelectedEvent(f.Id, r.Target, material.CardId, reward, next));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { Step = ResolutionFrameStep.ResolvingEffect, ShiHeQi = r with { Stage = reward ? ShiHqStage.Drawing : ShiHqStage.Complete,
            Reward = reward, Materials = [material], Before = _movementSequence, After = _movementSequence } });
        if (reward) BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(ShiHqDrawReason)); else FinishShiHq(f);
        AdvanceRulesAndPublishState();
    }
    private void MarkShiHqEntity(ProgramSkillFrame f, int id, int holder, ShiHqMarkOrigin origin)
    {
        if (_cardZones.GetLocation(id) != CardLocation.Hand(holder)) throw new InvalidOperationException("Yingji marking requires the exact actual hand entity.");
        // Re-marking the same physical card does not create a second cleanup.
        if (!HasShiHqYingji(id)) AdvanceEventRulesAndQueueFact(new ShiHqYingjiMarkedEvent(++_resolutionSequence, f.Id, ShiHqSource(f), f.GameplayHash, id, holder, origin));
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark, SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw)]
    private void ReturnShiHqDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.ShiHeQi is not { Stage: ShiHqStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 2 ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark && !r.Reward)
            throw new InvalidOperationException("Qizhou lost its one original two-card native draw return.");
        foreach (var id in outcome.ActualDrawIds)
        {
            if (!_cardMovements.Any(m => m.CardId == id && m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ShiHqDrawReason))
                throw new InvalidOperationException("Yingji can mark only this exact native draw's actual obtained entities.");
            MarkShiHqEntity(f, id, f.OwnerSeat, r.Reward ? ShiHqMarkOrigin.NextMarkReward : ShiHqMarkOrigin.ExtraDraw);
        }
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShiHeQi = r with { Stage = ShiHqStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiHqDrawPaidEvent(f.Id, outcome.ActualDrawIds.Count, outcome.Replacement is not null, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueShiHq(f.Id);
    }
    private void ContinueShiHq(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHeQi: { } r } f || f.Id != id || _pendingDecision is not null) return;
        RequireShiHq(f);
        if (r.Stage is ShiHqStage.Target or ShiHqStage.Card) { PublishShiHq(f); return; }
        if (r.Stage == ShiHqStage.Drawing) return;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        FinishShiHq(GetActiveProgramFrame(id));
    }
    private void FinishShiHq(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { ShiHeQi = f.ShiHeQi! with { Stage = ShiHqStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new ShiHqCompletedEvent(f.Id, f.ShiHeQi!.Operation)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 49000)]
    private bool ResumeShiHq(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHeQi: not null } f || f.Id != id) return false; ContinueShiHq(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49000)]
    private bool ReturnShiHqMovement(ProgramSkillFrame f) { if (f.ShiHeQi is null) return false; ContinueShiHq(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49000)]
    private bool KeepPaidShiHq(ProgramSkillFrame f) => f.ShiHeQi is { Stage: ShiHqStage.Drawing or ShiHqStage.DrawChildren or ShiHqStage.CleanupChildren or ShiHqStage.Complete } ||
        f.SkillId == ShiHqQizhou && f.ShiHeQi is null && f.WindowContext is { } w && IsIssuedShiHqCleanupCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex), w);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49000)]
    private bool ShiHqMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.ShiHeQi is { } r &&
        effect?.Op == r.Operation && r.Stage is ShiHqStage.DrawChildren or ShiHqStage.CleanupChildren && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark)]
    private PromptChoice ChooseShiHqAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ?? p.Choices.First();
    private void RequireShiHq(ProgramSkillFrame f)
    {
        var r = f.ShiHeQi ?? throw new InvalidOperationException("Qizhou lost its owning receipt.");
        if (r.Source != ShiHqSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash ||
            f.InstructionIndex != 1 || r.Turn != _turnNumber || r.TurnOwner != _currentSeat || r.ParentId != f.WindowContext?.ParentFrameId || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count ||
            ProgramEventHistory<ShiHqStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Hash == r.Hash && e.Turn == r.Turn && e.TurnOwner == r.TurnOwner && e.ParentId == r.ParentId && e.Operation == r.Operation) != 1)
            throw new InvalidOperationException("Qizhou changed its original source, actual turn, native instruction or paid cursor.");
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49000)]
    private void AssertShiHq(ProgramSkillFrame f) { if (f.ShiHeQi is not null) RequireShiHq(f); }
}
