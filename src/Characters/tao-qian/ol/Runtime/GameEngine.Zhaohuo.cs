using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhaohuoAction = "zhaohuo";
    private const string ZhaohuoGiftReason = "skill-program.tao-qian.zhaohuo.give";
    private bool HasZhaohuoCapability => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift) == true;
    private static bool IsZhaohuoTrigger(SkillProgramTrigger trigger) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_tao_qian_ol_Zhaohuo.IsZhaohuoTrigger(trigger);
    private bool ZhaohuoActualTurnOpen => _setupComplete && _turnNumber > 0 &&
        _turnProgression.TurnNumber == _turnNumber &&
        !ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat) &&
        !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _turnProgression.OwnerSeat);
    private static CardConversionSource ZhaohuoSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_tao_qian_ol_Zhaohuo.ZhaohuoSource(f);

    // DamageApplied precedes before-dying/recovery and other after-damage
    // children. Capture once here, including empty-Hand victims and turns in
    // which the current turn owner does not yet own Zhaohuo.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28121)]
    private void CaptureZhaohuoFirstVictim(IGameEvent payload)
    {
        if (!HasZhaohuoCapability || !ZhaohuoActualTurnOpen ||
            payload is not DamageAppliedEvent { Amount: > 0 } applied ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage ||
            CurrentDamageAttempt is not { } attack || damage.ParentFrameId != attack.ResolutionId ||
            damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat ||
            damage.Amount != applied.Amount || attack.SourceSeat != applied.SourceSeat ||
            attack.TargetSeat != applied.TargetSeat || attack.DamageAmount != applied.Amount ||
            attack.IsSourceLess != applied.SourceLess || !IsValidPlayerSeat(applied.TargetSeat)) return;
        if (ProgramEventHistory<ZhaohuoFirstVictimDamageEvent>().Any(e => e.ActualTurn == _turnNumber &&
                e.TurnOwner == _turnProgression.OwnerSeat && e.VictimSeat == applied.TargetSeat)) return;
        var first = new ZhaohuoFirstVictimDamageEvent(damage.Id, attack.ResolutionId, _turnNumber,
            _turnProgression.OwnerSeat, applied.SourceLess ? null : applied.SourceSeat,
            applied.TargetSeat, applied.Amount, GetHand(_players[applied.TargetSeat]).Count);
        AdvanceEventRulesAndQueueFact(first);
        var owner = _players[first.TurnOwner];
        if (!owner.IsAlive || first.HandCount == 0) return;
        var candidate = GetSkillBindingShard(owner).ProgramInstances.SelectMany(i =>
                i.Program.Triggers.Where(IsZhaohuoTrigger).Select(t => new ProgramTriggerCandidate(
                    owner.Seat, i.SkillId, t.Id, i.SkillInstanceId, i.Program.GameplayHash, t.Priority)))
            .Where(c => HasRuntimeSkillInstance(owner, c.SkillId, c.SkillInstanceId))
            .OrderByDescending(c => c.Priority).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        if (candidate is not null)
            AdvanceEventRulesAndQueueFact(new ZhaohuoQualifiedEvent(first.DamageFrameId,
                new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId),
                candidate.GameplayHash, first.ActualTurn, first.TurnOwner));
    }

    private ZhaohuoFirstVictimDamageEvent? ZhaohuoFirst(long damageId) =>
        ProgramEventHistory<ZhaohuoFirstVictimDamageEvent>().SingleOrDefault(e => e.DamageFrameId == damageId);
    private bool ZhaohuoQualified(ProgramTriggerCandidate c, ZhaohuoFirstVictimDamageEvent first) =>
        first.Amount > 0 && first.HandCount > 0 && c.OccurrenceIndex == 0 && c.OwnerSeat == first.TurnOwner &&
        _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program is { } p && p.GameplayHash == c.GameplayHash &&
        p.Triggers.Any(t => t.Id == c.BindingId && IsZhaohuoTrigger(t)) &&
        ProgramEventHistory<ZhaohuoQualifiedEvent>().Count(e => e.DamageFrameId == first.DamageFrameId &&
            e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) &&
            e.GameplayHash == c.GameplayHash && e.ActualTurn == first.ActualTurn && e.TurnOwner == first.TurnOwner) == 1;

    private void AppendZhaohuoDamageCandidates(IDamageAttempt attack, SkillProgramTriggerWindow window,
        List<DamageTriggerCandidate> candidates)
    {
        candidates.RemoveAll(c => !string.IsNullOrEmpty(c.ProgramId) &&
            IsZhaohuoTrigger(GetProgramTrigger(c.ToProgramCandidate())));
        if (!HasZhaohuoCapability || window != SkillProgramTriggerWindow.AfterDamageApplied || !ZhaohuoActualTurnOpen ||
            !_players[_turnProgression.OwnerSeat].IsAlive || !_players[attack.TargetSeat].IsAlive ||
            GetHand(_players[attack.TargetSeat]).Count == 0) return;
        var damage = _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.ParentFrameId == attack.ResolutionId &&
            d.SourceSeat == attack.SourceSeat && d.TargetSeat == attack.TargetSeat && d.Amount == attack.DamageAmount);
        if (damage is null || ZhaohuoFirst(damage.Id) is not { } first || first.HandCount == 0 ||
            first.ActualTurn != _turnNumber || first.TurnOwner != _turnProgression.OwnerSeat) return;
        foreach (var q in ProgramEventHistory<ZhaohuoQualifiedEvent>().Where(q => q.DamageFrameId == damage.Id))
        {
            var c = new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId,
                q.Source.SkillInstanceId, q.GameplayHash, 0);
            if (!ZhaohuoQualified(c, first)) continue;
            var t = GetProgramTrigger(c);
            candidates.Add(new(c.OwnerSeat, $"program:{c.SkillId}:{c.BindingId}:{c.SkillInstanceId}:occurrence-0",
                t.Priority, false, ProgramId: c.SkillId, ProgramTriggerId: c.BindingId,
                SkillInstanceId: c.SkillInstanceId, GameplayHash: c.GameplayHash));
        }
    }

    private ProgramSkillWindowContext CreateZhaohuoDamageContext(DamageTriggerWindowFrame window,
        ProgramTriggerCandidate candidate)
    {
        var first = ZhaohuoFirst(window.ParentFrameId) ??
            throw new InvalidOperationException("Zhaohuo requires the actual producer-frozen damage occurrence.");
        if (!ZhaohuoQualified(candidate, first) || window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
            first.VictimSeat != window.TargetSeat || first.ActualTurn != _turnNumber)
            throw new InvalidOperationException("Zhaohuo cannot replace its original damaged donor.");
        return new(SkillProgramTriggerWindow.AfterDamageApplied, window.Id, candidate.OwnerSeat,
            SourceSeat: first.DamageSourceSeat, TargetSeat: first.VictimSeat, DamageFrameId: first.DamageFrameId,
            Amount: first.Amount, Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with
            { EventTargetDamageInstancesTakenThisTurn = 1, EventTargetHandCount = first.HandCount });
    }

    private bool IsIssuedZhaohuoCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (!HasZhaohuoCapability || !IsValidPlayerSeat(candidate.OwnerSeat) ||
            !IsZhaohuoTrigger(GetProgramTrigger(candidate)) || !ZhaohuoActualTurnOpen ||
            context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.OwnerSeat != candidate.OwnerSeat ||
            context.DamageFrameId is not { } damageId || ZhaohuoFirst(damageId) is not { } first ||
            !ZhaohuoQualified(candidate, first) || first.ActualTurn != _turnNumber ||
            first.TurnOwner != _turnProgression.OwnerSeat || context.SourceSeat != first.DamageSourceSeat ||
            context.TargetSeat != first.VictimSeat || context.Amount != first.Amount ||
            !_players[candidate.OwnerSeat].IsAlive || !_players[first.VictimSeat].IsAlive ||
            GetHand(_players[first.VictimSeat]).Count == 0 ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not DamageTriggerWindowFrame w ||
            w.TriggerWindow != context.Window || w.ParentFrameId != damageId || w.TargetSeat != first.VictimSeat ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex].ToProgramCandidate() != candidate ||
            _resolutionStack.SingleOrDefault(f => f.Id == damageId) is not DamageFrame damage ||
            damage.ParentFrameId != first.AttackId || damage.TargetSeat != first.VictimSeat || damage.Amount != first.Amount)
            return false;
        return !ProgramEventHistory<ZhaohuoStartedEvent>().Any(e => e.DamageFrameId == damageId && e.Source.OwnerSeat == candidate.OwnerSeat);
    }

    [ProgramCapability]
    private sealed class ZhaohuoCapability(GameEngine engine) : IZhaohuoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.ResolveZhaohuoFirstVictimGift(frame); }

    private SkillProgramStepOutcome ResolveZhaohuoFirstVictimGift(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Zhaohuo is not null || f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } c)
            throw new InvalidOperationException("Zhaohuo requires its original unexecuted native damage binding.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        if (!IsIssuedZhaohuoCandidate(candidate, c))
            throw new InvalidOperationException("Zhaohuo lost its first positive damage and original Hand donor.");
        var first = ZhaohuoFirst(c.DamageFrameId!.Value)!;
        var r = new ZhaohuoReceipt(ZhaohuoSource(f), f.GameplayHash, first.DamageFrameId,
            c.ParentFrameId, first.AttackId, first.VictimSeat, first.ActualTurn, first.TurnOwner, ZhaohuoStage.Choosing);
        ReplaceRuntimeTop(f = f with { Zhaohuo = r });
        AdvanceEventRulesAndQueueFact(new ZhaohuoStartedEvent(f.Id, r.DamageFrameId, r.DamageWindowId,
            r.Source, r.GameplayHash, r.VictimSeat, r.ActualTurn, r.TurnOwner));
        PublishZhaohuo(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> ZhaohuoChoices(ProgramSkillFrame f)
    {
        var r = f.Zhaohuo!;
        if (r.Stage != ZhaohuoStage.Choosing || !_players[f.OwnerSeat].IsAlive || !_players[r.VictimSeat].IsAlive) return [];
        return Array.AsReadOnly(GetHand(_players[r.VictimSeat]).Select(card => PromptChoiceProjection.Freeze(new PromptChoice(
            new($"zhaohuo.{f.Id}.give.{card.Id}"), "交给" + _players[f.OwnerSeat].Name + "：" + card.DisplayName,
            [card.Id], [], new Dictionary<string, string> { ["program-action"] = ZhaohuoAction,
                [ZhaohuoAction] = "give", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) }))).ToArray());
    }
    private void PublishZhaohuo(ProgramSkillFrame f)
    {
        AssertZhaohuo(f);
        var choices = ZhaohuoChoices(f);
        if (choices.Count == 0) { FinishZhaohuo(f); return; }
        var r = f.Zhaohuo!; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.VictimSeat, "招祸：选择一张手牌交给" + _players[f.OwnerSeat].Name + "。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { IsPrivate = true, PromptId = CreatePromptId(), Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.VictimSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("zhaohuo")]
    private void AnswerZhaohuo(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zhaohuo: { Stage: ZhaohuoStage.Choosing } r } f ||
            _pendingDecision?.PlayerSeat != r.VictimSeat || !ZhaohuoChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Zhaohuo requires the damaged donor's exact private original Hand choice.");
        AssertZhaohuo(f);
        var card = GetHand(_players[r.VictimSeat]).Single(c => c.Id == choice.Cards.Single());
        ClearPendingDecision();
        r = r with { Paid = true, Stage = ZhaohuoStage.GiftChildren, CardId = card.Id,
            PrintedKind = card.Kind, SequenceBefore = _movementSequence, SequenceAfter = _movementSequence,
            SelfGift = r.VictimSeat == f.OwnerSeat };
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, Zhaohuo = r,
            PendingMovementContinuation = r.SelfGift ? null : new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ZhaohuoGiftCommittedEvent(f.Id, r.DamageFrameId, r.Source,
            r.GameplayHash, r.VictimSeat, card.Id, card.Kind, r.ActualTurn, r.TurnOwner));
        if (r.SelfGift)
        {
            AdvanceEventRulesAndQueueFact(new ZhaohuoSelfGiftCommittedEvent(f.Id, r.DamageFrameId,
                r.Source, r.GameplayHash, card.Id, card.Kind, r.ActualTurn, r.TurnOwner,
                r.SequenceBefore, r.SequenceBefore));
            ContinueZhaohuo(f.Id);
            return;
        }
        MoveCard(card, CardLocation.Hand(r.VictimSeat), CardLocation.Hand(f.OwnerSeat), new(ZhaohuoGiftReason),
            movement => RecordZhaohuoGift(f.Id, movement));
        ContinueZhaohuo(f.Id);
    }
    private void RecordZhaohuoGift(long frameId, CardMovementRecord movement)
    {
        var f = GetActiveProgramFrame(frameId); var r = f.Zhaohuo!;
        if (!r.Paid || r.SelfGift || r.Stage != ZhaohuoStage.GiftChildren || r.PaymentSequence is not null ||
            movement.CardId != r.CardId || movement.CardKind != r.PrintedKind ||
            movement.From != CardLocation.Hand(r.VictimSeat) || movement.To != CardLocation.Hand(f.OwnerSeat) ||
            movement.Reason.Value != ZhaohuoGiftReason || movement.Sequence <= r.SequenceBefore)
            throw new InvalidOperationException("Zhaohuo lost its one original donor Hand payment.");
        ReplaceRuntimeTop(f with { Zhaohuo = r with { PaymentSequence = movement.Sequence, SequenceAfter = movement.Sequence } });
        AdvanceEventRulesAndQueueFact(new ZhaohuoGiftPaidEvent(f.Id, r.DamageFrameId, r.Source, r.GameplayHash,
            r.VictimSeat, movement.CardId, movement.CardKind, r.ActualTurn, r.TurnOwner,
            movement.From, movement.To, movement.Sequence));
    }
    private bool DrainZhaohuo(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id);
    private void ContinueZhaohuo(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zhaohuo: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertZhaohuo(f);
        if (r.Stage == ZhaohuoStage.Choosing) { PublishZhaohuo(f); return; }
        if (DrainZhaohuo(id)) return;
        FinishZhaohuo(GetActiveProgramFrame(id));
    }
    private void FinishZhaohuo(ProgramSkillFrame f)
    {
        AssertZhaohuo(f); var r = f.Zhaohuo!;
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ZhaohuoCompletedEvent(f.Id, r.DamageFrameId, r.Paid));
        ReplaceRuntimeTop(f = f with { Zhaohuo = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28121)]
    private bool ResumeZhaohuo(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zhaohuo: not null } f || f.Id != id || f.InstructionIndex == 0) return false;
        ContinueZhaohuo(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28121)]
    private bool ReturnZhaohuoMovement(ProgramSkillFrame f)
    { if (f.Zhaohuo is null) return false; ContinueZhaohuo(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift)]
    private PromptChoice ChooseZhaohuoAi(PendingDecision prompt, ProgramSkillFrame frame) => prompt.Choices.First();
}
