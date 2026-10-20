using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouFaZhengGiftReason = "skill-program.mou-mobile:xuanhuo.give", MouFaZhengTakeReason = "skill-program.mou-mobile:xuanhuo.take", MouFaZhengRepayReason = "skill-program.mou-mobile:enyuan.give";
    [ProgramCapability] private sealed class MouFaZhengCapability(GameEngine engine) : IMouFaZhengHost
    { public SkillProgramStepOutcome Start(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.StartMouFaZheng(f, op); }
    private bool CanMouFaZhengMark(int owner, int target) => IsValidPlayerSeat(target) && _players[target].IsAlive && target != owner && !MouFaZhengMarks().Any(m => m.TargetSeat == target);
    private IReadOnlyList<PromptChoice> MouFaZhengPayments(ProgramSkillFrame f, int target) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
        [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer, destination: SkillProgramCardDestination.SelectedTargetHand, destinationSeat: target);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 31800, SkillProgramEffectOp.IssueMouFaZhengXuanhuo)]
    private bool CanStartMouFaZheng(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == owner.Seat &&
        IsRecordedActualDiscardRecoveryPhase(phase) && !ProgramEventHistory<MouFaZhengStartedEvent>().Any(e => e.Op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo && e.Source.OwnerSeat == owner.Seat &&
            ProgramEventHistory<MouFaZhengActivationPhaseEvent>().Any(p => p.FrameId == e.FrameId && p.Phase == phase)) &&
        _players.Any(p => CanMouFaZhengMark(owner.Seat, p.Seat)) && GetHand(owner).Count + GetEquipment(owner).Count > 0;
    [ProgramPhaseUsedProjection(31800)]
    private int MouFaZhengUsed(int owner, string skill, int fallback) => skill == MouFaZhengComposition.Xuanhuo && CurrentActualDiscardRecoveryPhase() is { } phase
        ? ProgramEventHistory<MouFaZhengActivationPhaseEvent>().Count(e => e.OwnerSeat == owner && e.Phase == phase) : fallback;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31800)]
    private bool CanOfferMouFaZheng(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (IsMouFaZhengGainTrigger(t)) return MouFaZhengQualification(c, context) is { } q && MouFaZhengMarks().Any(m => m.MarkId == q.MarkId) &&
            MouFaZhengExtracted(q.MarkId) < 5 && _players[q.TargetSeat].IsAlive && GetHand(_players[q.TargetSeat]).Count > 0;
        return t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouFaZhengEnyuan }] || context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            context.SourceSeat == c.OwnerSeat && MouFaZhengMarks().Any(m => m.Source.OwnerSeat == c.OwnerSeat);
    }
    private SkillProgramStepOutcome StartMouFaZheng(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouFaZheng is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Mou Fa Zheng requires his original empty-input native entry.");
        var parent = f.WindowContext?.ParentFrameId ?? 0L;
        var r = new MouFaZhengReceipt(MouFaZhengSource(f), f.GameplayHash, op, parent);
        if (op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo)
        {
            if (f.SkillId != MouFaZhengComposition.Xuanhuo || f.ActivationId != "give-xuan" || f.TriggerId is not null || !CanStartMouFaZheng(_players[f.OwnerSeat]))
                throw new InvalidOperationException("Xuanhuo requires its unused actual Play activation.");
            r = r with { Stage = MouFaZhengStage.Target, Phase = CurrentActualDiscardRecoveryPhase() };
        }
        else
        {
            if (f.ActivationId != f.TriggerId || f.WindowContext is not { } w || !CanOfferMouFaZheng(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex), GetProgramTrigger(f), w))
                throw new InvalidOperationException("Mou Fa Zheng cannot borrow another gain or Preparation window.");
            r = r with { Accepted = true, Stage = MouFaZhengStage.NextMark };
            if (op == SkillProgramEffectOp.ResolveMouFaZhengEnyuan)
                r = r with { Marks = MouFaZhengMarks().Where(m => m.Source.OwnerSeat == f.OwnerSeat).OrderBy(m => (m.TargetSeat - f.OwnerSeat + _playerCount) % _playerCount).Select(m => m.MarkId).ToArray() };
            else
            {
                var q = MouFaZhengQualification(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex), w)!;
                r = r with { MarkId = q.MarkId, Target = q.TargetSeat };
            }
        }
        ReplaceRuntimeTop(f = f with { MouFaZheng = r });
        AdvanceEventRulesAndQueueFact(new MouFaZhengStartedEvent(f.Id, r.Source, r.GameplayHash, op, parent));
        if (r.Phase is { } phase) AdvanceEventRulesAndQueueFact(new MouFaZhengActivationPhaseEvent(f.Id, f.OwnerSeat, phase));
        for (var n = 0; n < r.Marks.Count; n++) AdvanceEventRulesAndQueueFact(new MouFaZhengRosterEvent(f.Id, n, r.Marks[n]));
        if (op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo) PublishMouFaZheng(f);
        else if (op == SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain)
        {
            var hand = GetHand(_players[r.Target]); var card = hand[_random.Next(hand.Count)];
            PayMouFaZhengMovement(f, card, CardLocation.Hand(r.Target), f.OwnerSeat, "take");
        }
        else ContinueMouFaZheng(f.Id);
        return op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo ? SkillProgramStepOutcome.AwaitChoice : SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> MouFaZhengChoices(ProgramSkillFrame f)
    {
        var r = f.MouFaZheng!;
        if (r.Stage == MouFaZhengStage.Target) return Array.AsReadOnly(_players.Where(p => CanMouFaZhengMark(f.OwnerSeat, p.Seat) && MouFaZhengPayments(f, p.Seat).Count > 0)
            .Select(p => new PromptChoice(new($"mou-fa-zheng.{f.Id}.target.{p.Seat}"), $"交牌并令{p.Name}获得眩", [], [p.Seat],
                new Dictionary<string, string> { ["program-action"] = "mou-fa-zheng", ["branch"] = "target" })).ToArray());
        if (r.Stage is MouFaZhengStage.Gift or MouFaZhengStage.Repay) return Array.AsReadOnly(MouFaZhengPayments(f, r.Target).Select(c => c with {
            Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-fa-zheng", ["branch"] = "card" } }).ToArray());
        return [];
    }
    private void PublishMouFaZheng(ProgramSkillFrame f)
    {
        var choices = MouFaZhengChoices(f);
        if (choices.Count == 0) { if (f.MouFaZheng!.Op == SkillProgramEffectOp.ResolveMouFaZhengEnyuan) { NextMouFaZhengMark(f); return; } FinishMouFaZheng(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.MouFaZheng!.Stage == MouFaZhengStage.Repay ? "恩怨：交给该角色三张牌，不足则交给其全部可交牌。" : "眩惑：选择其他角色并交给其一张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-fa-zheng")]
    private void AnswerMouFaZheng(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouFaZheng: { } r } f || !ValidMouFaZheng(f) ||
            !MouFaZhengChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Mou Fa Zheng lost his exact current HE payment choice.");
        ClearPendingDecision();
        if (r.Stage == MouFaZhengStage.Target)
        { ReplaceRuntimeTop(f = f with { MouFaZheng = r with { Target = choice.Targets.Single(), Stage = MouFaZhengStage.Gift } }); PublishMouFaZheng(f); return; }
        if (r.Stage == MouFaZhengStage.Gift && !CanMouFaZhengMark(f.OwnerSeat, r.Target)) { FinishMouFaZheng(f); return; }
        var from = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), f.OwnerSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        PayMouFaZhengMovement(f, card, from, r.Target, r.Stage == MouFaZhengStage.Gift ? "gift" : "repay");
    }
    private void PayMouFaZhengMovement(ProgramSkillFrame f, Card card, CardLocation from, int target, string purpose)
    {
        var r = f.MouFaZheng!; var reason = purpose == "take" ? MouFaZhengTakeReason : purpose == "gift" ? MouFaZhengGiftReason : MouFaZhengRepayReason;
        ReplaceRuntimeTop(f = f with { MouFaZheng = r with { Accepted = true, Stage = MouFaZhengStage.MovementChildren, MovementPurpose = purpose,
            PaidCardId = card.Id, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(target), new CardMoveReason(reason), move => {
            var current = GetActiveProgramFrame(f.Id); var paid = current.MouFaZheng!;
            ReplaceRuntimeTop(current with { MouFaZheng = paid with { After = move.Sequence, MarkId = purpose == "gift" ? f.Id : paid.MarkId } });
            AdvanceEventRulesAndQueueFact(new MouFaZhengMovementPaidEvent(f.Id, purpose, purpose == "gift" ? f.Id : paid.MarkId, from.OwnerSeat!.Value, target, move.Sequence));
            if (purpose == "gift") AdvanceEventRulesAndQueueFact(new MouFaZhengMarkEvent(f.Id, paid.Source, paid.GameplayHash, target, move.Sequence));
            if (purpose == "take") AdvanceEventRulesAndQueueFact(new MouFaZhengObtainedEvent(f.Id, paid.MarkId, from.OwnerSeat.Value, move.To == CardLocation.Hand(f.OwnerSeat) ? 1 : 0, move.Sequence));
        }, disclosure: from.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouFaZheng = f.MouFaZheng! with { After = _movementSequence } });
        ContinueMouFaZheng(f.Id);
    }
    private void RemoveMouFaZhengMark(ProgramSkillFrame f)
    {
        var r = f.MouFaZheng!;
        if (MouFaZhengMarks().Any(m => m.MarkId == r.MarkId)) AdvanceEventRulesAndQueueFact(new MouFaZhengRemovedEvent(f.Id, r.MarkId, r.Target, MouFaZhengReceived(r.MarkId)));
        SynchronizeMouFaZhengMarks();
    }
    private void NextMouFaZhengMark(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { MouFaZheng = f.MouFaZheng! with { Index = f.MouFaZheng.Index + 1, Stage = MouFaZhengStage.NextMark }, PendingMovementContinuation = null }); ContinueMouFaZheng(f.Id); }
    private void ContinueMouFaZheng(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouFaZheng: { } r } f || f.Id != id) return;
        if (!ValidMouFaZheng(f)) throw new InvalidOperationException("Mou Fa Zheng lost his issued native effect cursor.");
        if (r.Stage is MouFaZhengStage.Target or MouFaZhengStage.Gift or MouFaZhengStage.Repay) { PublishMouFaZheng(f); return; }
        if (DrainMouFaZhengChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouFaZheng!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None) { FinishMouFaZheng(f); return; }
        if (r.Stage == MouFaZhengStage.MovementChildren)
        {
            if (r.MovementPurpose != "repay") { FinishMouFaZheng(f); return; }
            r = r with { Remaining = r.Remaining - 1, Stage = MouFaZhengStage.Repay }; ReplaceRuntimeTop(f = f with { MouFaZheng = r });
            if (r.Remaining > 0 && _players[f.OwnerSeat].IsAlive && _players[r.Target].IsAlive) { PublishMouFaZheng(f); return; }
            NextMouFaZhengMark(f); return;
        }
        if (r.Stage == MouFaZhengStage.LosingHp)
        {
            if (!ProgramEventHistory<IdentityAppearanceCompletedEvent>().Any(done => ProgramEventHistory<IdentityGeneralAppearedEvent>().Any(e => e.AppearanceFrameId == done.AppearanceFrameId &&
                e.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && e.Receipt.ParentFrameId == f.Id && e.Receipt.OwnerSeat == r.Target && e.Receipt.RequestedDeduction == 1)))
                throw new InvalidOperationException("Enyuan requires its native HP-loss return.");
            ReplaceRuntimeTop(f = f with { MouFaZheng = r = r with { Stage = MouFaZhengStage.LossChildren } });
        }
        if (r.Stage == MouFaZhengStage.LossChildren)
        {
            if (_players[f.OwnerSeat].IsAlive)
            {
                ReplaceRuntimeTop(f = f with { MouFaZheng = r with { Stage = MouFaZhengStage.RecoveryChildren, RecoveryHpBefore = _players[f.OwnerSeat].Hp,
                    Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
                f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouFaZheng = f.MouFaZheng! with { After = _movementSequence } });
                ContinueMouFaZheng(id); return;
            }
            RemoveMouFaZhengMark(f); NextMouFaZhengMark(f); return;
        }
        if (r.Stage == MouFaZhengStage.RecoveryChildren)
        {
            AdvanceEventRulesAndQueueFact(new MouFaZhengRecoveryReturnedEvent(f.Id, f.OwnerSeat)); RemoveMouFaZhengMark(f); NextMouFaZhengMark(f); return;
        }
        if (r.Stage != MouFaZhengStage.NextMark) throw new InvalidOperationException("Unexpected Enyuan cursor.");
        if (r.Index >= r.Marks.Count) { FinishMouFaZheng(f); return; }
        var mark = MouFaZhengMarks().SingleOrDefault(m => m.MarkId == r.Marks[r.Index]);
        if (mark is null || !_players[mark.TargetSeat].IsAlive) { NextMouFaZhengMark(f); return; }
        ReplaceRuntimeTop(f = f with { MouFaZheng = r = r with { MarkId = mark.MarkId, Target = mark.TargetSeat } });
        if (MouFaZhengReceived(mark.MarkId) >= 3)
        {
            RemoveMouFaZhengMark(f);
            if (!_players[f.OwnerSeat].IsAlive) { NextMouFaZhengMark(f); return; }
            ReplaceRuntimeTop(f = f with { MouFaZheng = r with { Stage = MouFaZhengStage.Repay, Remaining = 3 } }); PublishMouFaZheng(f); return;
        }
        ReplaceRuntimeTop(f = f with { MouFaZheng = r with { Stage = MouFaZhengStage.LosingHp, LossId = 0, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var result = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, mark.TargetSeat, 1);
        if (result == SkillProgramStepOutcome.Continue && _resolutionStack.LastOrDefault()?.Id == f.Id) ContinueMouFaZheng(id);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 31800)]
    private void ObserveMouFaZhengLoss(ProgramSkillHpLostEvent lost)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouFaZheng: { Stage: MouFaZhengStage.LosingHp } r } f || lost.FrameId != f.Id) return;
        var change = _pendingHpChanges.SingleOrDefault(c => c.ParentFrameId == f.Id && c.Kind == HpChangeKind.Loss && c.TargetSeat == r.Target);
        if (!ValidMouFaZheng(f) || lost.SkillId != f.SkillId || lost.TargetSeat != r.Target || change is null || change.SourceSeat is not null || change.Amount != lost.Amount || change.HpAfter != lost.RemainingHp)
            throw new InvalidOperationException("Enyuan lost the exact native HP loss invoice.");
        ReplaceRuntimeTop(f with { MouFaZheng = r with { Stage = MouFaZhengStage.LossChildren, LossId = change.Id, LossHpBefore = change.HpBefore, LossHpAfter = change.HpAfter } });
        AdvanceEventRulesAndQueueFact(new MouFaZhengLossReturnedEvent(f.Id, r.Target, change.Id, change.Amount, change.HpBefore, change.HpAfter));
    }
    private void FinishMouFaZheng(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new MouFaZhengCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { MouFaZheng = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.IssueMouFaZhengXuanhuo, SkillProgramEffectOp.ResolveMouFaZhengEnyuan)]
    private PromptChoice AiMouFaZheng(PendingDecision p, ProgramSkillFrame f) => f.MouFaZheng!.Stage == MouFaZhengStage.Target ?
        p.Choices.OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]) ? 1 : 0).ThenBy(c => c.Targets[0]).First() :
        p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
}
public sealed record MouFaZhengActivationPhaseEvent(long FrameId, int OwnerSeat, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
