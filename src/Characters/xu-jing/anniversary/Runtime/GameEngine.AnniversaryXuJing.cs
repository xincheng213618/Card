namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource AnniversaryXuJingSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasAnniversaryXuJing => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia);
    private AnniversaryXuJingMarkedEvent? AnniversaryXuJingMark(int owner, string? instance = null) => ProgramEventHistory<AnniversaryXuJingMarkedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner && (instance is null || e.Source.SkillInstanceId == instance));
    private AnniversaryXuJingCaixiaIssuedEvent? AnniversaryXuJingDebt(int owner) => ProgramEventHistory<AnniversaryXuJingCaixiaIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner);
    private int AnniversaryXuJingRemaining(int owner) => AnniversaryXuJingDebt(owner) is not { } debt ? 0 : Math.Max(0, debt.Count - ProgramEventHistory<AnniversaryXuJingCaixiaUseEvent>().Count(e => e.DebtFrameId == debt.FrameId));
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 43100)]
    private void RecordAnniversaryXuJingActualUse(CurrentTurnCardUseKindsRecordedEvent fact)
    {
        if (!HasAnniversaryXuJing || AnniversaryXuJingDebt(fact.ActorSeat) is not { } debt || AnniversaryXuJingRemaining(fact.ActorSeat) == 0 ||
            ProgramEventHistory<AnniversaryXuJingCaixiaUseEvent>().Any(e => e.DebtFrameId == debt.FrameId && e.OriginFrameId == fact.OriginFrameId && e.ActionId == fact.CardActionId && e.InstructionIndex == fact.ProgramInstructionIndex)) return;
        AdvanceEventRulesAndQueueFact(new AnniversaryXuJingCaixiaUseEvent(debt.FrameId, fact.ActorSeat, fact.OriginFrameId, fact.CardActionId, fact.ProgramInstructionIndex));
    }
    private int[] AnniversaryXuJingDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate) => AnniversaryXuJingMark(candidate.OwnerSeat, candidate.SkillInstanceId) is not { } mark ? [] : batch.Movements
        .Select((movement, index) => (movement, index)).Where(x => x.movement.CardId == mark.CardId && x.movement.To == CardLocation.DiscardPile && x.movement.From != x.movement.To).Select(x => x.index).ToArray();
    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 43100)]
    private int[]? SelectAnniversaryXuJingDiscards(CardMovementBatchContext batch, ProgramTriggerCandidate candidate, ProgramInstructionFeatures features) =>
        features.HasOperation(SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu) ? AnniversaryXuJingDiscardIndexes(batch, candidate) : null;
    private IDamageAttempt? AnniversaryXuJingDamage(ProgramSkillWindowContext context) => _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId && w.ParentFrameId == context.DamageFrameId) is { } window ? GetDamageTriggerAttack(window) : null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43100, SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu, SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia)]
    private bool CanOfferAnniversaryXuJing(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == AnniversaryXuJingComposition.Caixia) return AnniversaryXuJingRemaining(candidate.OwnerSeat) == 0 && context.Amount > 0 &&
            (context.SourceSeat == candidate.OwnerSeat || context.TargetSeat == candidate.OwnerSeat);
        if (trigger.Id == "opening-slash") return AnniversaryXuJingMark(candidate.OwnerSeat, candidate.SkillInstanceId) is null;
        if (AnniversaryXuJingMark(candidate.OwnerSeat, candidate.SkillInstanceId) is not { } mark) return false;
        if (trigger.Id == "marked-slash-discard") return context.MovementBatch is { } batch && context.MovementIndex is { } index && AnniversaryXuJingDiscardIndexes(batch, candidate).Contains(index) &&
            _cardZones.GetLocation(mark.CardId) == CardLocation.DiscardPile && AnniversaryXuJingRecipients(candidate.OwnerSeat, mark.CardId).Length > 0;
        return AnniversaryXuJingDamage(context) is { IsSourceLess: false, DamageWasApplied: true, EffectiveCardKind: { } kind } damage && IsSlashCard(kind) && damage.PhysicalCards.Any(c => c.Id == mark.CardId);
    }
    private int[] AnniversaryXuJingRecipients(int owner, int card) => _players.Where(p => p.IsAlive && !ProgramEventHistory<AnniversaryXuJingGiftIssuedEvent>().Any(e =>
        e.Source.OwnerSeat == owner && e.CardId == card && e.TurnNumber == _turnNumber && e.TurnOwner == _currentSeat && e.TargetSeat == p.Seat)).OrderBy(p => RelativeTableOrder(owner, p.Seat)).Select(p => p.Seat).ToArray();
    [ProgramCapability] private sealed class AnniversaryXuJingHost(GameEngine engine) : IAnniversaryXuJingHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginAnniversaryXuJing(frame, operation); }
    private SkillProgramStepOutcome BeginAnniversaryXuJing(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryXuJing is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context) throw new InvalidOperationException("Xu Jing requires his original unissued trigger instruction.");
        var mode = operation == SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia ? "caixia" : context.Window switch
        { SkillProgramTriggerWindow.GameStarting => "opening", SkillProgramTriggerWindow.DiscardPileReceived => "discard", _ => "damage" };
        var card = mode == "opening" ? 0 : AnniversaryXuJingMark(f.OwnerSeat, f.SkillInstanceId)?.CardId ?? 0;
        var user = mode == "damage" ? AnniversaryXuJingDamage(context)?.CardUserSeat ?? throw new InvalidOperationException("Shangyu requires its actual damaging Slash user.") : f.OwnerSeat;
        var r = new AnniversaryXuJingReceipt(AnniversaryXuJingSource(f), f.GameplayHash, context.ParentFrameId, mode,
            mode == "caixia" ? AnniversaryXuJingStage.CountChoice : mode == "damage" ? AnniversaryXuJingStage.DrawReady : AnniversaryXuJingStage.GiftChoice, card, user, context.DamageFrameId ?? 0);
        if (mode == "opening")
        {
            var candidates = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => IsSlashCard(c.Kind)).ToArray();
            if (candidates.Length > 0) r = r with { CardId = candidates[_random.Next(candidates.Length)].Id, Stage = AnniversaryXuJingStage.MovementChildren };
        }
        ReplaceRuntimeTop(f = f with { AnniversaryXuJing = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryXuJingStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, mode, r.CardId, user, r.DamageId));
        if (mode == "opening")
        {
            if (r.CardId == 0) { FinishProgramSkill(f, true); return SkillProgramStepOutcome.AwaitChild; }
            AdvanceEventRulesAndQueueFact(new AnniversaryXuJingMarkedEvent(f.Id, r.Source, r.CardId));
            MoveAnniversaryXuJingEntity(f, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat));
        }
        ContinueAnniversaryXuJing(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private static CardMoveReason AnniversaryXuJingMoveReason => new("skill.anniversary.shangyu.entity");
    private static CardMoveReason AnniversaryXuJingDrawReason => new("skill.anniversary.xujing.draw");
    private void MoveAnniversaryXuJingEntity(ProgramSkillFrame supplied, CardLocation source, CardLocation destination)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryXuJing!;
        if (_cardZones.GetLocation(r.CardId) != source) throw new InvalidOperationException("Shangyu cannot replace its original marked entity or current source.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryXuJing = r with { Stage = AnniversaryXuJingStage.MovementChildren, MovementSource = source, MovementDestination = destination, Before = before, After = before } });
        if (source != destination) MoveProgramCardsFromMultipleSources([r.CardId], destination, AnniversaryXuJingMoveReason, (_, _) =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { AnniversaryXuJing = current.AnniversaryXuJing! with { After = _movementSequence } });
        });
        f = GetActiveProgramFrame(f.Id); r = f.AnniversaryXuJing!;
        AdvanceEventRulesAndQueueFact(new AnniversaryXuJingMovedEvent(f.Id, r.CardId, source, destination, before, r.After));
        _ = AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat);
    }
    private IReadOnlyList<PromptChoice> AnniversaryXuJingChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryXuJing!;
        if (r.Stage == AnniversaryXuJingStage.CountChoice) return Enumerable.Range(1, Math.Min(5, _players.Count)).Select(n => new PromptChoice(new($"anniversary-xujing.{f.Id}.draw.{n}"), $"摸{n}张牌，累计使用{n}张牌前不能再次发动才瑕", [], [],
            new Dictionary<string, string> { ["program-action"] = "anniversary-xujing", ["count"] = n.ToString() })).ToArray();
        if (r.Stage != AnniversaryXuJingStage.GiftChoice || _cardZones.GetLocation(r.CardId) != (r.Mode == "opening" ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile)) return [];
        return AnniversaryXuJingRecipients(f.OwnerSeat, r.CardId).Select(seat => new PromptChoice(new($"anniversary-xujing.{f.Id}.gift.{seat}"), $"将赏誉标记的【杀】交给{_players[seat].Name}", [], [seat],
            new Dictionary<string, string> { ["program-action"] = "anniversary-xujing", ["item"] = "gift" })).ToArray();
    }
    private void PublishAnniversaryXuJing(ProgramSkillFrame f)
    {
        var choices = AnniversaryXuJingChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive) { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.AnniversaryXuJing!.Stage == AnniversaryXuJingStage.GiftChoice ? "赏誉：选择本回合未指定过的角色。" : "才瑕：选择此次摸牌数量。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-xujing")] private void AnswerAnniversaryXuJing(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryXuJing: { } r } f || !AnniversaryXuJingChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Xu Jing rejects a stale gift recipient or draw count.");
        RequireAnniversaryXuJing(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == AnniversaryXuJingStage.CountChoice)
        {
            var count = int.Parse(choice.Parameters["count"]);
            if (AnniversaryXuJingRemaining(f.OwnerSeat) != 0) throw new InvalidOperationException("Caixia is still waiting for actual card uses.");
            AdvanceEventRulesAndQueueFact(new AnniversaryXuJingCaixiaIssuedEvent(f.Id, r.Source, count));
            ReplaceRuntimeTop(f = f with { AnniversaryXuJing = r with { Count = count, Stage = AnniversaryXuJingStage.DrawReady } });
        }
        else
        {
            var target = choice.Targets.Single();
            AdvanceEventRulesAndQueueFact(new AnniversaryXuJingGiftIssuedEvent(f.Id, r.Source, r.CardId, target, _turnNumber, _currentSeat));
            ReplaceRuntimeTop(f = f with { AnniversaryXuJing = r with { TargetSeat = target } });
            MoveAnniversaryXuJingEntity(f, r.Mode == "opening" ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile, CardLocation.Hand(target));
        }
        ContinueAnniversaryXuJing(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 43100)] private bool ContinueAnniversaryXuJing(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryXuJing: not null } root || root.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryXuJing: { } r } f && f.Id == id)
        {
            RequireAnniversaryXuJing(f);
            if (r.Stage is AnniversaryXuJingStage.CountChoice or AnniversaryXuJingStage.GiftChoice) { PublishAnniversaryXuJing(f); return true; }
            if (r.Stage == AnniversaryXuJingStage.Drawing) return true;
            if (r.Stage is AnniversaryXuJingStage.MovementChildren or AnniversaryXuJingStage.DrawChildren)
            {
                if (DrainAnniversaryXuJing(f)) return true;
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.AnniversaryXuJing!;
                if (r.Stage == AnniversaryXuJingStage.MovementChildren && r.Mode == "opening" && r.TargetSeat < 0)
                { ReplaceRuntimeTop(f with { AnniversaryXuJing = r with { Stage = AnniversaryXuJingStage.GiftChoice } }); continue; }
                if (r.Stage == AnniversaryXuJingStage.DrawChildren && r.Mode == "damage" && r.RewardIndex < 2)
                { ReplaceRuntimeTop(f with { AnniversaryXuJing = r with { Stage = AnniversaryXuJingStage.DrawReady } }); continue; }
                FinishProgramSkill(f, true); return true;
            }
            var recipient = r.Mode == "caixia" || r.RewardIndex == 0 ? f.OwnerSeat : r.UserSeat;
            var count = r.Mode == "caixia" ? r.Count : 1;
            if (!_players[recipient].IsAlive || _winner != Winner.None)
            {
                if (r.Mode == "damage" && r.RewardIndex == 0 && _players[r.UserSeat].IsAlive)
                { ReplaceRuntimeTop(f with { AnniversaryXuJing = r with { RewardIndex = 1 } }); continue; }
                FinishProgramSkill(f, true); return true;
            }
            ReplaceRuntimeTop(f = f with { AnniversaryXuJing = r with { TargetSeat = recipient, Count = count, RewardIndex = r.RewardIndex + 1, Stage = AnniversaryXuJingStage.Drawing } });
            BeginOperationProgramDraw(f, recipient, count, AnniversaryXuJingDrawReason); return true;
        }
        return true;
    }
    private bool DrainAnniversaryXuJing(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.AnniversaryXuJing!.Stage == AnniversaryXuJingStage.MovementChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.AnniversaryXuJing!.Stage == AnniversaryXuJingStage.MovementChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu, SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia)]
    private void ReturnAnniversaryXuJingDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.AnniversaryXuJing is not { Stage: AnniversaryXuJingStage.Drawing } r || outcome.RecipientSeat != r.TargetSeat || outcome.RequestedCount != r.Count || outcome.SequenceAfter != _movementSequence || outcome.AttemptFrameId is { } attempt && r.Draws.Any(d => d.AttemptFrameId == attempt)) throw new InvalidOperationException("Xu Jing lost his original once-issued native draw return.");
        var ordinal = r.RewardIndex;
        ReplaceRuntimeTop(f with { AnniversaryXuJing = r with { Stage = AnniversaryXuJingStage.DrawChildren, Draws = r.Draws.Append(outcome).ToArray(), Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryXuJingDrawReturnedEvent(f.Id, ordinal, outcome.RecipientSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueAnniversaryXuJing(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43100)] private bool ReturnAnniversaryXuJingMovement(ProgramSkillFrame f) => f.AnniversaryXuJing is not null && ContinueAnniversaryXuJing(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu)] private PromptChoice ChooseAnniversaryXuJingGift(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia)] private PromptChoice ChooseAnniversaryXuJingCount(PendingDecision p, ProgramSkillFrame f) => p.Choices[^1];
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43100)] private bool RetainAnniversaryXuJingIssued(ProgramSkillFrame f) => f.AnniversaryXuJing is not null;
}
