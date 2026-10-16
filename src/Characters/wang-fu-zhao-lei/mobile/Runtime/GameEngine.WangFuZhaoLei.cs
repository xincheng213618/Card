namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource XunyiSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private bool TracksXunyi => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SettleXunyiDamagePoint) == true;
    private IEnumerable<XunyiRelationGrant> XunyiRelations() => CompleteProgramEventHistory().OfType<XunyiRelationChangedEvent>()
        .GroupBy(e => (e.Relation.Source.OwnerSeat, e.Relation.Source.SkillId, e.Relation.Source.SkillInstanceId)).Select(g => g.Last().Relation);
    private XunyiRelationGrant? XunyiRelation(int owner, string skill, string instance) => XunyiRelations()
        .SingleOrDefault(g => g.Source.OwnerSeat == owner && g.Source.SkillId == skill && g.Source.SkillInstanceId == instance);
    private XunyiDamageReceipt? XunyiQualification(int owner, string skill, string instance, long damage) =>
        CompleteProgramEventHistory().OfType<XunyiDamageQualifiedEvent>().Select(e => e.Receipt).SingleOrDefault(r =>
            r.DamageFrameId == damage && r.Relation.Source.OwnerSeat == owner && r.Relation.Source.SkillId == skill && r.Relation.Source.SkillInstanceId == instance);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26320)]
    private void CaptureXunyiActualDamage(IGameEvent payload)
    {
        if (!TracksXunyi || payload is not DamageAppliedEvent { Amount: > 0 } applied || CurrentDamageAttempt is not { } attack ||
            !attack.DamageWasApplied || attack.TargetSeat != applied.TargetSeat || attack.DamageAmount != applied.Amount) return;
        var damage = _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.ParentFrameId == attack.ResolutionId &&
            d.TargetSeat == applied.TargetSeat && d.Amount == applied.Amount) ?? throw new InvalidOperationException("Xunyi requires the actual native damage frame.");
        var source = attack.IsSourceLess || attack.IsDelayedJudgmentDamage ? (int?)null : applied.SourceSeat;
        foreach (var relation in XunyiRelations().Where(g => _players[g.Source.OwnerSeat].IsAlive).ToArray())
        {
            var owner = relation.Source.OwnerSeat;
            var other = relation.HolderSeat;
            if (!_players[other].IsAlive || !HasRuntimeSkillInstance(_players[owner], relation.Source.SkillId, relation.Source.SkillInstanceId)) continue;
            int? discard = applied.TargetSeat == owner && source != other ? other : applied.TargetSeat == other && source != owner ? owner : null;
            int? draw = source == owner && applied.TargetSeat != other ? other : source == other && applied.TargetSeat != owner ? owner : null;
            if (discard is null && draw is null) continue;
            if (XunyiQualification(owner, relation.Source.SkillId, relation.Source.SkillInstanceId, damage.Id) is not null)
                throw new InvalidOperationException("Xunyi cannot qualify the same actual damage twice.");
            AdvanceEventRulesAndQueueFact(new XunyiDamageQualifiedEvent(new(relation, damage.Id, attack.ResolutionId,
                source, applied.TargetSeat, applied.Amount, discard, draw)));
        }
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26320)]
    private bool CanOfferXunyi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ArmXunyiRelation))
            return context.Window == SkillProgramTriggerWindow.GameStarting && XunyiRelation(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId) is null &&
                _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.MoveXunyiAfterPartnerDeath))
            return context.Window == SkillProgramTriggerWindow.CharacterDied && context.TargetSeat is { } victim && !_players[victim].IsAlive &&
                XunyiRelation(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId)?.HolderSeat == victim &&
                _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat);
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.SettleXunyiDamagePoint)) return true;
        return context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.DamageFrameId is { } damage &&
            XunyiQualification(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, damage) is { } receipt &&
            receipt.ActualAmount == context.Amount && receipt.ActualTargetSeat == context.TargetSeat &&
            !CompleteProgramEventHistory().OfType<XunyiDamageIssuedEvent>().Any(e => e.Receipt == receipt);
    }

    private SkillProgramStepOutcome ExecuteXunyi(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (operation != SkillProgramEffectOp.SettleXunyiDamagePoint)
        {
            ChangeXunyiRelation(frame, operation == SkillProgramEffectOp.MoveXunyiAfterPartnerDeath);
            return SkillProgramStepOutcome.Continue;
        }
        if (frame.Xunyi is not null || frame.InstructionIndex != 1 || frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: { } damage } context ||
            !CanOfferXunyi(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0), GetProgramTrigger(frame), context))
            throw new InvalidOperationException("Xunyi requires its exact unissued after-damage candidate.");
        var receipt = XunyiQualification(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, damage)!;
        ReplaceRuntimeTop(frame = frame with { Xunyi = new(receipt, frame.InstructionIndex, 0, XunyiStage.DiscardChoice) });
        AdvanceEventRulesAndQueueFact(new XunyiDamageIssuedEvent(frame.Id, receipt, frame.InstructionIndex));
        BeginNextXunyiPoint(frame);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ChangeXunyiRelation(ProgramSkillFrame frame, bool transfer)
    {
        if (frame.SelectedTargetSeats is not [var target] || target == frame.OwnerSeat || !_players[target].IsAlive ||
            frame.WindowContext is not { } context || frame.InstructionIndex != 2 ||
            !CanOfferXunyi(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0), GetProgramTrigger(frame), context))
            throw new InvalidOperationException("Xunyi assignment requires its exact native selection and relation.");
        var old = XunyiRelation(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId);
        if (transfer != (old is not null)) throw new InvalidOperationException("Xunyi cannot invent an initial or transferred relation.");
        var relation = old is null ? new XunyiRelationGrant(frame.Id, XunyiSource(frame), frame.GameplayHash, target, context.ParentFrameId) : old with { FrameId = frame.Id, HolderSeat = target };
        AdvanceEventRulesAndQueueFact(new XunyiRelationChangedEvent(relation, old?.HolderSeat));
        if (old is not null) SynchronizeXunyiMarker(frame, old.HolderSeat);
        SynchronizeXunyiMarker(frame, target);
    }

    private void SynchronizeXunyiMarker(ProgramSkillFrame frame, int holder)
    {
        var player = _players[holder];
        var marker = PlayerMarkerKind.XunyiYi;
        var before = player.Markers.GetValueOrDefault(marker);
        foreach (var key in player.MarkerSourceCounts.Keys.Where(k => k.Marker == marker).ToArray()) player.MarkerSourceCounts.Remove(key);
        var counts = XunyiRelations().Where(g => g.HolderSeat == holder).GroupBy(g => g.Source.OwnerSeat).ToArray();
        foreach (var count in counts) player.MarkerSourceCounts[(marker, count.Key)] = count.Count();
        var total = counts.Sum(c => c.Count());
        if (total == 0) player.Markers.Remove(marker); else player.Markers[marker] = total;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame.Id, holder, marker, total - before, total, frame.OwnerSeat, "skill-program.xunyi.marker"));
    }

    private static int XunyiBranches(XunyiDamageReceipt receipt) => (receipt.DiscardRecipientSeat is null ? 0 : 1) + (receipt.DrawRecipientSeat is null ? 0 : 1);
    private static int XunyiStepCount(XunyiDamageReceipt receipt) => checked(receipt.ActualAmount * XunyiBranches(receipt));
    private static (int Seat, bool Draw) XunyiStep(XunyiDamagePending pending) =>
        pending.Receipt.DrawRecipientSeat is { } draw && (pending.Receipt.DiscardRecipientSeat is null || pending.StepIndex % XunyiBranches(pending.Receipt) == 1)
            ? (draw, true) : (pending.Receipt.DiscardRecipientSeat!.Value, false);
    private IReadOnlyList<PromptChoice> XunyiChoices(ProgramSkillFrame frame)
    {
        var seat = XunyiStep(frame.Xunyi!).Seat;
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(frame.Id, seat, seat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
            canSelect: (zone, card) => !IsSelfHandCategoryDiscardForbidden(seat, card, new(zone, seat), OwnedCardMoveIntent.Discard))
            .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "xunyi-discard" } }).ToArray());
    }
    private void BeginNextXunyiPoint(ProgramSkillFrame frame)
    {
        RequireXunyi(frame);
        var pending = frame.Xunyi!;
        if (pending.StepIndex >= XunyiStepCount(pending.Receipt))
        {
            AdvanceEventRulesAndQueueFact(new XunyiDamageCompletedEvent(frame.Id, pending.Receipt.DamageFrameId, pending.StepIndex));
            ReplaceRuntimeTop(frame = frame with { Xunyi = null, PendingMovementContinuation = null });
            FinishProgramSkill(frame, true);
            return;
        }
        var (seat, draw) = XunyiStep(pending);
        if (draw || !_players[seat].IsAlive || XunyiChoices(frame).Count == 0)
        {
            PayXunyiPoint(frame, null, null);
            return;
        }
        ReplaceRuntimeTop(frame = frame with { Xunyi = pending with { Stage = XunyiStage.DiscardChoice }, Step = ResolutionFrameStep.AwaitingResponse });
        var choices = XunyiChoices(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, "殉义：弃置一张牌。", choices.SelectMany(c => c.Cards).ToArray(), [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.SettleXunyiDamagePoint)]
    private PromptChoice XunyiAiChoice(ProgramSkillFrame frame) => XunyiChoices(frame).First();
    [ProgramAnswer("xunyi-discard")]
    private void ResolveXunyiDiscard(PromptChoice choice)
    {
        var frame = (ProgramSkillFrame)_resolutionStack.Last();
        RequireXunyi(frame);
        var seat = XunyiStep(frame.Xunyi!).Seat;
        if (frame.Xunyi!.Stage != XunyiStage.DiscardChoice || XunyiStep(frame.Xunyi).Draw || _pendingDecision?.PlayerSeat != seat ||
            !AssistedChoicesEqual([choice], XunyiChoices(frame).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Xunyi discard lost its actual recipient authority.");
        var card = GetAdvancedCard(choice.Cards.Single());
        var from = _cardZones.GetLocation(card.Id);
        ClearPendingDecision();
        PayXunyiPoint(frame, card, from);
    }
    private void PayXunyiPoint(ProgramSkillFrame frame, Card? card, CardLocation? from)
    {
        var pending = frame.Xunyi!;
        var (seat, draw) = XunyiStep(pending);
        var before = _movementSequence;
        pending = pending with { Stage = XunyiStage.Children, Before = before, After = before, Count = 0, CardId = card?.Id, From = from };
        ReplaceRuntimeTop(frame = frame with { Xunyi = pending, PendingMovementContinuation = new(seat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        var count = 0;
        if (_players[seat].IsAlive)
        {
            if (draw) count = DrawCards(_players[seat], 1, true, new("skill-program.xunyi.draw")).Count;
            else if (card is not null && from is not null)
            {
                MoveCard(card, from.Value, CardLocation.DiscardPile, new("skill-program.xunyi.discard"));
                count = 1;
            }
        }
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { Xunyi = pending with { After = _movementSequence, Count = count } });
        AdvanceEventRulesAndQueueFact(new XunyiPointPaidEvent(frame.Id, pending.Receipt.DamageFrameId, pending.StepIndex, seat, draw,
            before, _movementSequence, count, card?.Id, from));
        AdvanceRuntimeProgram(frame.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26320)]
    private bool ResumeXunyi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xunyi: { Stage: XunyiStage.Children } pending } frame || frame.Id != id) return false;
        RequireXunyi(frame);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        ReplaceRuntimeTop(frame = frame with { Xunyi = pending with { StepIndex = pending.StepIndex + 1, Stage = XunyiStage.DiscardChoice, Before = 0, After = 0, Count = 0, CardId = null, From = null }, PendingMovementContinuation = null });
        BeginNextXunyiPoint(frame);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26320)]
    private bool ReturnXunyiMovement(ProgramSkillFrame frame) => frame.Xunyi is not null && ResumeXunyi(frame.Id);
    [ProgramCapability]
    private sealed class WangFuZhaoLeiCapability(GameEngine engine) : IWangFuZhaoLeiProgramHost
    { public SkillProgramStepOutcome ExecuteXunyi(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.ExecuteXunyi(frame, operation); }
}
