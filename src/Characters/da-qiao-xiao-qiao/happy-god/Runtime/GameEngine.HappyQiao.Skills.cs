using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyQiaoAction = "happy-qiao-skills";
    private static CardConversionSource HappyQiaoSource(ProgramSkillFrame frame) => new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    [ProgramCapability] private sealed class HappyQiaoHost(GameEngine engine) : IHappyQiaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginHappyQiaoSkill(frame, operation); }

    [ProgramPipelineHook(ProgramPipelinePhase.HealthChangeSubject, 42400)]
    private bool HappyQiaoHealthSubject(int target) => _players.Any(p => p.IsAlive && p.HappyQiaoHealth is not null && HasRuntimeSkill(p, HappyQiaoComposition.Tongxin));
    private CardUseFrame? HappyQiaoCausingUse(HpChangeContext change)
    {
        if (change.ParentFrameId is not { } id || change.HpBefore == change.HpAfter) return null;
        if (change.Kind == HpChangeKind.Damage)
        {
            var damageOwner = _resolutionStack.SingleOrDefault(f => f.Id == id) is DamageFrame damage ? damage.ParentFrameId : id;
            var attack = LastDamageSourceOriginalAttack(damageOwner);
            return attack is CardAttackHandle ? LifecycleCardUse(attack.ResolutionId) : null;
        }
        if (_resolutionStack.SingleOrDefault(f => f.Id == id) is RecoveryReplacementFrame recovery)
            return recovery.Attempt.Completion.Producer is RecoveryAttemptProducer.CardUse or RecoveryAttemptProducer.GroupCard
                ? LifecycleCardUse(recovery.ParentFrameId) : null;
        return LifecycleCardUse(id);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureHealthChange, 42400)]
    private void CaptureHappyQiaoRedHealth(HpChangeContext change)
    {
        if (!HappyQiaoHealthSubject(change.TargetSeat) || HappyQiaoCausingUse(change) is not { Action: { } action } use ||
            !(action.Type == CardActionType.Use || action.Type == CardActionType.Response && action.EffectiveKind is CardKind.Dodge or CardKind.Nullification) ||
            !(action.EffectiveIsRed ?? (action.EffectiveSuit is Suit.Heart or Suit.Diamond)) ||
            !IsValidPlayerSeat(action.ActorSeat) || !_players[action.ActorSeat].IsAlive || !HasHappyQiaoHealth(action.ActorSeat)) return;
        foreach (var c in CollectProgramTriggerCandidates(_players[action.ActorSeat], SkillProgramTriggerWindow.AfterHealthChanged).Where(c => c.SkillId == HappyQiaoComposition.Tongxin))
            AdvanceEventRulesAndQueueFact(new HappyQiaoRedHealthQualifiedEvent(change.Id, c.OwnerSeat, c.SkillInstanceId, use.Id, action.ActionId, change.TargetSeat, change.Kind));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeHealthChangeCandidates, 42400)]
    private IReadOnlyList<ProgramTriggerCandidate> HappyQiaoHealthCandidates(HpChangeContext change, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        var peers = _players.Where(p => p.IsAlive && HasHappyQiaoHealth(p.Seat)).SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.AfterHealthChanged))
            .Where(c => c.SkillId == HappyQiaoComposition.Tongxin && HappyQiaoRedQualification(c.OwnerSeat, c.SkillInstanceId, change));
        return Array.AsReadOnly(ordinary.Where(c => c.SkillId != HappyQiaoComposition.Tongxin).Concat(peers)
            .DistinctBy(c => (c.OwnerSeat, c.SkillId, c.BindingId, c.SkillInstanceId)).ToArray());
    }
    private bool HappyQiaoRedQualification(int owner, string instance, HpChangeContext change) => ProgramEventHistory<HappyQiaoRedHealthQualifiedEvent>()
        .Count(e => e.ChangeId == change.Id && e.Owner == owner && e.Instance == instance && e.Target == change.TargetSeat && e.Kind == change.Kind) == 1;
    private bool IsHappyQiaoForeignHealthCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId == HappyQiaoComposition.Tongxin && candidate.BindingId == "pool-health" &&
        context is { Window: SkillProgramTriggerWindow.AfterHealthChanged, HpChange: { } change } &&
        context.OwnerSeat == candidate.OwnerSeat && context.TargetSeat == change.TargetSeat && context.ParentFrameId == change.Id &&
        HasHappyQiaoHealth(candidate.OwnerSeat) && HappyQiaoRedQualification(candidate.OwnerSeat, candidate.SkillInstanceId, change) &&
        _resolutionStack.OfType<HpChangedTriggerWindowFrame>().LastOrDefault() is { } window && window.Id == context.ParentFrameId && window.Change == change &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && window.Candidates[window.CandidateIndex] == candidate;
    private bool HappyQiaoShutuChange(int owner, HpChangeContext change) => HasHappyQiaoHealth(owner) && change.TargetSeat == owner && change.HpBefore != change.HpAfter &&
        (change is { Kind: HpChangeKind.Recovery, HappyQiaoPool: HappyQiaoPool.DaQiao } && change.HpAfter > change.HpBefore ||
         change is { Kind: HpChangeKind.Damage, HappyQiaoPool: HappyQiaoPool.XiaoQiao } && change.HpAfter < change.HpBefore);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 42400, SkillProgramEffectOp.ResolveHappyQiaoShutu, SkillProgramEffectOp.ResolveHappyQiaoTongxin)]
    private bool HappyQiaoSkillQualified(ProgramTriggerLegalityContext gate) => gate.WindowContext?.HpChange is { } change &&
        (gate.SkillId == HappyQiaoComposition.Shutu ? HappyQiaoShutuChange(gate.OwnerSeat, change) :
         gate.SkillId == HappyQiaoComposition.Tongxin && HappyQiaoRedQualification(gate.OwnerSeat, gate.Candidate.SkillInstanceId, change));

    private SkillProgramStepOutcome BeginHappyQiaoSkill(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.HappyQiao is not null || frame.InstructionIndex != 1 || frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHealthChanged, HpChange: { } change } context ||
            !HappyQiaoOriginalHealthCandidate(frame) || !HasHappyQiaoHealth(frame.OwnerSeat) ||
            !(operation == SkillProgramEffectOp.ResolveHappyQiaoShutu && frame.SkillId == HappyQiaoComposition.Shutu && HappyQiaoShutuChange(frame.OwnerSeat, change) ||
              operation == SkillProgramEffectOp.ResolveHappyQiaoTongxin && frame.SkillId == HappyQiaoComposition.Tongxin && HappyQiaoRedQualification(frame.OwnerSeat, frame.SkillInstanceId, change)))
            throw new InvalidOperationException("Happy Qiao requires her original actual selected-pool HP change or her exact red native Use.");
        var stage = operation == SkillProgramEffectOp.ResolveHappyQiaoTongxin ? HappyQiaoStage.HealChoice :
            change.Kind == HpChangeKind.Recovery ? HappyQiaoStage.SuitChoice : HappyQiaoStage.UseChoice;
        var receipt = new HappyQiaoReceipt(HappyQiaoSource(frame), frame.GameplayHash, operation, context.ParentFrameId, _turnNumber, change, stage)
        { Target = frame.OwnerSeat, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt });
        AdvanceEventRulesAndQueueFact(new HappyQiaoStartedEvent(frame.Id, receipt.Source, receipt.Hash, operation, receipt.Parent, receipt.Turn, change.Id));
        ContinueHappyQiao(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool HappyQiaoOriginalHealthCandidate(ProgramSkillFrame frame)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1] is HpChangedTriggerWindowFrame hp && hp.Id == frame.WindowContext?.ParentFrameId &&
            hp.Change == frame.WindowContext.HpChange && hp.CandidateIndex < hp.Candidates.Count && MountObserverCandidateMatches(frame, hp.Candidates[hp.CandidateIndex]);
    }
    private bool HappyQiaoEqualHealable(int owner) => _players[owner].HappyQiaoHealth is { } h && h.DaQiaoHp == h.XiaoQiaoHp && h.AnyWounded;
    private bool HappyQiaoEqualRedHand(int owner)
    {
        var hand = GetHand(_players[owner]);
        return hand.Count(c => EffectiveSuit(_players[owner], c) == Suit.Heart) == hand.Count(c => EffectiveSuit(_players[owner], c) == Suit.Diamond);
    }
    private IReadOnlyList<PromptChoice> HappyQiaoDiscardChoices(ProgramSkillFrame frame) => BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, frame.OwnerSeat,
        [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard, canSelect: (_, card) => EffectiveSuit(_players[frame.OwnerSeat], card) is Suit.Heart or Suit.Diamond);
    private PromptChoice HappyQiaoChoice(ProgramSkillFrame frame, string branch, string label, string value = "", IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        new(new($"happy-qiao.{frame.Id}.{frame.HappyQiao!.Stage}.{branch}.{value}"), label, cards ?? [], targets ?? [],
            new Dictionary<string, string> { ["program-action"] = HappyQiaoAction, ["branch"] = branch, ["value"] = value });
    private IReadOnlyList<PromptChoice> HappyQiaoChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyQiao!; var choices = new List<PromptChoice>();
        if (receipt.Stage == HappyQiaoStage.SuitChoice)
            choices.AddRange(new[] { ("HH", "取得两张红桃"), ("HD", "取得红桃和方片各一张"), ("DD", "取得两张方片") }.Select(x => HappyQiaoChoice(frame, "suits", x.Item2, x.Item1)));
        if (receipt.Stage == HappyQiaoStage.UseChoice)
            choices.AddRange(HappyQiaoUsePlans(frame).Select((plan, i) => HappyQiaoChoice(frame, "use", plan.Action.Description, i.ToString(CultureInfo.InvariantCulture), plan.Cards, plan.Targets)));
        if (receipt.Stage == HappyQiaoStage.HealChoice && HappyQiaoEqualHealable(frame.OwnerSeat)) choices.Add(HappyQiaoChoice(frame, "heal", "回复1点体力"));
        if (receipt.Stage == HappyQiaoStage.DiscardChoice && HappyQiaoEqualRedHand(frame.OwnerSeat))
            choices.AddRange(HappyQiaoDiscardChoices(frame).Select(c => c with { Id = new($"happy-qiao-cost.{frame.Id}.{c.Id.Value}"), Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = HappyQiaoAction, ["branch"] = "discard" } }));
        if (receipt.Stage == HappyQiaoStage.TargetChoice) choices.AddRange(_players.Where(p => p.IsAlive).Select(p => HappyQiaoChoice(frame, "target", "对" + p.Name + "造成1点伤害", p.Seat.ToString(CultureInfo.InvariantCulture), targets: [p.Seat])));
        if (receipt.Stage is HappyQiaoStage.UseChoice or HappyQiaoStage.HealChoice or HappyQiaoStage.DiscardChoice) choices.Add(HappyQiaoChoice(frame, "decline", "不发动此项效果"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PresentHappyQiao(ProgramSkillFrame frame)
    {
        var choices = HappyQiaoChoices(frame);
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(frame, frame.OwnerSeat, choices, frame.SkillId == HappyQiaoComposition.Shutu ? "殊途：请选择。" : "同心：依次选择。", frame.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(HappyQiaoAction)] private void AnswerHappyQiaoSkill(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyQiao: { } receipt } frame || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !HappyQiaoChoices(frame).Any(c => AssistedChoicesEqual([c], [answer]))) throw new InvalidOperationException("Happy Qiao requires her exact original live private choice.");
        RequireHappyQiaoSkill(frame); ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = answer.Parameters["branch"];
        AdvanceEventRulesAndQueueFact(new HappyQiaoBranchEvent(frame.Id, receipt.Stage, branch));
        if (receipt.Stage == HappyQiaoStage.HealChoice)
        {
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = branch == "heal" ? HappyQiaoStage.HealChildren : HappyQiaoStage.DiscardChoice } });
            ContinueHappyQiao(frame.Id); return;
        }
        if (branch == "decline") { FinishHappyQiaoSkill(frame); return; }
        if (receipt.Stage == HappyQiaoStage.SuitChoice)
        {
            var suits = answer.Parameters["value"].Select(ch => ch == 'H' ? Suit.Heart : Suit.Diamond).ToArray();
            var remaining = _cardZones.CardsAt(CardLocation.DrawPile).ToList(); var gained = new List<int>();
            foreach (var suit in suits) if (remaining.FirstOrDefault(c => c.Suit == suit) is { } card) { gained.Add(card.Id); remaining.Remove(card); }
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.GainChildren, Suits = suits, Gained = gained, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            if (gained.Count > 0) MoveProgramCardsFromMultipleSources(gained, CardLocation.Hand(frame.OwnerSeat), new("skill.happy.shutu.chosen-red"), (_, _) => RecordHappyQiaoMovement(frame.Id));
            ContinueHappyQiao(frame.Id); return;
        }
        if (receipt.Stage == HappyQiaoStage.UseChoice)
        {
            var plan = HappyQiaoUsePlans(frame)[int.Parse(answer.Parameters["value"], CultureInfo.InvariantCulture)];
            var native = new HappyQiaoNativeUse(frame.Id, frame.InstructionIndex, receipt.Source, receipt.Hash, receipt.Turn, plan, 0, 0)
            { Costs = plan.Cards.Select(id => { var card = GetAdvancedCard(id); return new CardActionCost(id, card.Kind, _cardZones.GetLocation(id)); }).ToArray() };
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.IssuingUse, Native = native } }); IssueHappyQiaoShutuUse(frame); return;
        }
        if (receipt.Stage == HappyQiaoStage.DiscardChoice)
        {
            var location = new CardLocation(Enum.Parse<CardZoneKind>(answer.Parameters["source-zone"]), frame.OwnerSeat);
            var card = _cardZones.CardsAt(location)[int.Parse(answer.Parameters["slot-index"], CultureInfo.InvariantCulture)];
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.TargetChoice, CardId = card.Id, CostFrom = location } }); PresentHappyQiao(frame); return;
        }
        if (receipt.Stage == HappyQiaoStage.TargetChoice)
        {
            if (!HappyQiaoEqualRedHand(frame.OwnerSeat) || _cardZones.GetLocation(receipt.CardId) != receipt.CostFrom ||
                !HappyQiaoDiscardChoices(frame).Any(c => c.Cards.Contains(receipt.CardId))) throw new InvalidOperationException("Tongxin's live red hand equality or actual red discard material expired.");
            ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.DiscardChildren, Target = answer.Targets.Single(), Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            ContinueHappyQiao(frame.Id); return;
        }
        throw new InvalidOperationException("Happy Qiao cannot repeat a completed native payment.");
    }
    private void RecordHappyQiaoMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId); var receipt = frame.HappyQiao!;
        ReplaceRuntimeTop(frame with { HappyQiao = receipt with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new HappyQiaoMovementEvent(frameId, receipt.Stage, receipt.Before, _movementSequence, _cardMovements.Count(m => m.Sequence > receipt.Before && m.Sequence <= _movementSequence && receipt.Gained.Contains(m.CardId) && m.To == CardLocation.Hand(frame.OwnerSeat))));
    }
    private bool DrainHappyQiaoSkill(ProgramSkillFrame frame)
    {
        var movement = frame.HappyQiao!.Stage is HappyQiaoStage.GainChildren or HappyQiaoStage.DiscardChildren;
        return TryBeginQueuedRecoveryReplacement(frame.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 42400)] private bool ContinueHappyQiao(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyQiao: not null } current || current.Id != frameId) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { HappyQiao: { } receipt } frame && frame.Id == frameId)
        {
            RequireHappyQiaoSkill(frame);
            if (receipt.Stage is HappyQiaoStage.IssuingUse or HappyQiaoStage.Declaration or HappyQiaoStage.NativeUse) return true;
            if (frame.AttackAttempt is not null) return true;
            if (receipt.Stage == HappyQiaoStage.HealChildren && !receipt.HealIssued)
            {
                if (TryBeginSkillInvocation(frame.Id, frame.OwnerSeat, frame.SkillId, "tongxin:" + frame.Id, frame.SkillInstanceId)) return true;
                frame = GetActiveProgramFrame(frameId); receipt = frame.HappyQiao!;
                if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None) { FinishHappyQiaoSkill(frame); return true; }
                ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { HealIssued = true } });
                new ProgramSkillHost(this).Recover(frame.Id, frame.OwnerSeat, frame.OwnerSeat, 1, null, null);
                continue;
            }
            if (receipt.Stage == HappyQiaoStage.DiscardChildren && receipt.CostBatch is null)
            {
                if (TryBeginSkillInvocation(frame.Id, frame.OwnerSeat, frame.SkillId, "tongxin:" + frame.Id, frame.SkillInstanceId)) return true;
                frame = GetActiveProgramFrame(frameId); receipt = frame.HappyQiao!;
                if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None || _cardZones.GetLocation(receipt.CardId) != receipt.CostFrom ||
                    !HappyQiaoDiscardChoices(frame).Any(c => c.Cards.Contains(receipt.CardId))) { FinishHappyQiaoSkill(frame); return true; }
                ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Before = _movementSequence, After = _movementSequence } });
                MoveProgramLogicalDiscard(frame, frame.OwnerSeat, [receipt.CardId], new("skill.happy.tongxin.red-cost"), (batch, moves) =>
                {
                    var current = GetActiveProgramFrame(frame.Id); var paid = moves.Single();
                    ReplaceRuntimeTop(current with { HappyQiao = current.HappyQiao! with { After = paid.Sequence, CostBatch = batch } });
                    AdvanceEventRulesAndQueueFact(new HappyQiaoCostPaidEvent(frame.Id, batch, paid.CardId, paid.From, paid.To, paid.Sequence));
                });
                continue;
            }
            if (DrainHappyQiaoSkill(frame)) return true;
            frame = GetActiveProgramFrame(frameId); receipt = frame.HappyQiao!;
            if (frame.PendingMovementContinuation is not null) ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
            if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None) { FinishHappyQiaoSkill(frame); return true; }
            if (receipt.Stage == HappyQiaoStage.HealChildren) { ReplaceRuntimeTop(frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.DiscardChoice } }); continue; }
            if (receipt.Stage == HappyQiaoStage.HealChoice && !HappyQiaoEqualHealable(frame.OwnerSeat)) { ReplaceRuntimeTop(frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.DiscardChoice } }); continue; }
            if (receipt.Stage == HappyQiaoStage.DiscardChoice && (!HappyQiaoEqualRedHand(frame.OwnerSeat) || HappyQiaoDiscardChoices(frame).Count == 0)) { FinishHappyQiaoSkill(frame); return true; }
            if (receipt.Stage is HappyQiaoStage.GainChildren or HappyQiaoStage.ReturnedUse or HappyQiaoStage.DamageChildren) { FinishHappyQiaoSkill(frame); return true; }
            if (receipt.Stage == HappyQiaoStage.DiscardChildren)
            {
                if (receipt.CostBatch is null || !ProgramEventHistory<HappyQiaoCostPaidEvent>().Any(e => e.FrameId == frame.Id && e.BatchId == receipt.CostBatch && e.CardId == receipt.CardId && e.Sequence == receipt.After))
                    throw new InvalidOperationException("Tongxin cannot damage before its actual red logical discard completes.");
                if (!_players[receipt.Target].IsAlive) { FinishHappyQiaoSkill(frame); return true; }
                ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.DamageChildren } });
                new ProgramSkillHost(this).Damage(frame, receipt.Target, 1); return true;
            }
            PresentHappyQiao(frame); return true;
        }
        return true;
    }
    private void FinishHappyQiaoSkill(ProgramSkillFrame frame)
    { AdvanceEventRulesAndQueueFact(new HappyQiaoCompletedEvent(frame.Id)); ReplaceRuntimeTop(frame = frame with { HappyQiao = null, PendingMovementContinuation = null }); FinishProgramSkill(frame, true); }
    [ProgramContinuation(ProgramContinuationKind.Movement, 42400)] private bool ReturnHappyQiaoMovement(ProgramSkillFrame frame) => frame.HappyQiao is not null && ContinueHappyQiao(frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 42400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42400)]
    private bool RetainHappyQiaoPaid(ProgramSkillFrame frame) => frame.HappyQiao is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyQiaoShutu)] private PromptChoice HappyQiaoShutuAi(PendingDecision pending, ProgramSkillFrame frame) => pending.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyQiaoTongxin)] private PromptChoice HappyQiaoTongxinAi(PendingDecision pending, ProgramSkillFrame frame) => pending.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "decline") ?? pending.Choices.First();
}
