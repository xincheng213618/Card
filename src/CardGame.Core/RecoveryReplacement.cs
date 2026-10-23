namespace CardGame.Core;

public enum RecoveryAttemptProducer
{
    Program, ProgramMaximum, SilverLion, CardUse, GroupCard, VirtualBasic,
    DyingBoundAlcohol, DyingVirtualAlcohol, Hengye,
    SkillInvocation = 38700
}

public sealed record RecoveryAttemptCompletion(RecoveryAttemptProducer Producer,
    int InstructionIndex = 0, int? CardId = null, CardKind? CardKind = null,
    CardMoveReason? MoveReason = null, long? ProgramFrameId = null,
    long? DyingFrameId = null, string? SkillId = null, int? SkillOwnerSeat = null,
    IReadOnlyList<ProgramRecoveryPolicySource>? Policies = null);

public sealed record RecoveryReplacementCandidate(int OwnerSeat, string SkillId,
    string SkillInstanceId, string PolicyId, int RecoveryAmount, int ProviderDrawCount);

/// <summary>A produced recovery belongs to its exact live rules frame, before any HP delta.</summary>
public sealed partial record RecoveryAttempt(long Id, int SourceSeat, int TargetSeat, int Amount,
    int? HpBefore, int ActualTurnNumber, int ActualTurnOwnerSeat,
    IReadOnlyList<RecoveryReplacementCandidate> Candidates, RecoveryAttemptCompletion Completion)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoDualHealth? HappyQiaoBefore { get; init; }
}

public enum RecoveryReplacementStage { Choosing, RecoveryApplied, RewardApplied }

public sealed record RecoveryReplacementReturn(PostEventContinuation Continuation,
    long? ResumeFrameId = null, int? CardId = null, CardKind? CardKind = null);

public sealed record RecoveryRewardMovementReceipt(long SequenceBefore, long SequenceAfter)
{
    private IReadOnlyList<CardMovementBatchContext> _batches = Array.AsReadOnly(Array.Empty<CardMovementBatchContext>());
    public IReadOnlyList<CardMovementBatchContext> Batches
    {
        get => _batches;
        init => _batches = Array.AsReadOnly(value.Select(batch => batch with
        {
            Movements = Array.AsReadOnly(batch.Movements.ToArray()),
            SourceCounts = Array.AsReadOnly(batch.SourceCounts.ToArray()),
            DestinationCounts = batch.DestinationCounts is null ? null : Array.AsReadOnly(batch.DestinationCounts.ToArray())
        }).ToArray());
    }
}

public sealed record PreparedRecoveryApplication(int SourceSeat, int TargetSeat, int RequestedAmount, int? CandidateIndex);

public sealed partial record RecoveryReplacementFrame(long Id, long ParentFrameId, RecoveryAttempt Attempt,
    RecoveryReplacementReturn Return, RecoveryReplacementStage Stage = RecoveryReplacementStage.Choosing,
    int? CandidateIndex = null, int OriginalRecovered = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.RecoveryReplacement, Step)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryRewardMovementReceipt? RewardMovementReceipt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PreparedRecoveryApplication? PendingApplication { get; init; }
}

public sealed record RecoveryReplacementChosenEvent(long RecoveryFrameId, int RecoveringSeat,
    int BeneficiarySeat, string SkillId, string SkillInstanceId, string PolicyId,
    int ReplacedAmount, int RecoveryAmount) : IGameEvent;

public enum RecoveryPaidCardUseKind { Trick, Simple, CommittedSlash, SlashTarget, AttackDamage, EquipmentUse, DodgeCompletion = 6 }

public sealed partial record RecoveryPaidCardUseContinuation(RecoveryPaidCardUseKind Kind, int SourceSeat,
    int? CardId = null, IReadOnlyList<int>? TargetSeats = null, LegalActionKind? ActionKind = null,
    int? TargetCardId = null, CardKind? RequiredCardKind = null, CardKind? PlayedCardKind = null,
    ProgramSimpleCardContinuation? Simple = null, int? ReplacedCardId = null, EquipmentSlot? EquipmentSlot = null);

public sealed partial class GameEngine
{
    private bool HasRecoveryReplacementCapability => _contentRegistry.ProgramDependencies
        .HasCardPolicy(SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery);

    private bool TryQueueHengyeRecovery(CharacterState player) => UsesFormalMouLuMeng &&
        player.IsAlive && HasRuntimeSkill(player, HengyeSkillId) && GetHengyeGrowth(player) >= 3 &&
        player.Hp < player.MaxHp && TryQueueRecoveryReplacement(0, player.Seat, player.Seat, 1,
            new(RecoveryAttemptProducer.Hengye));

    private bool TryPauseRecoveryPaidCardUse(long useId, RecoveryPaidCardUseContinuation continuation)
    {
        var use = LifecycleCardUse(useId);
        if (use is null) return false;
        continuation = FreezeNativePublicPileEquipmentChildren(use, continuation);
        var observesPayment = ObservesPaidCardUseChildren(use, continuation);
        var paymentOwner = HasNativePublicPileEquipmentTail(use, continuation)
            ? use with { RecoveryPaidContinuation = continuation } : use;
        if (use.PendingRecoveryAttempts is not { Count: > 0 } &&
            !((observesPayment || continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion) &&
              _pendingHpChanges.Any(change => change.ParentFrameId == useId)) &&
            !(observesPayment && _pendingCardsMovedBatches.Any(batch => IsCardUsePaymentBatch(paymentOwner, batch))))
            return false;
        if (_resolutionStack.LastOrDefault()?.Id != useId || use.RecoveryPaidContinuation is not null)
            throw new InvalidOperationException("Paid card use recovery lost its exact fresh parent boundary.");
        continuation = continuation with
        {
            TargetSeats = continuation.TargetSeats is { } targets ? Array.AsReadOnly(targets.ToArray()) : null,
            Simple = continuation.Simple is { } simple ? simple with
            { RecoveryPolicySources = simple.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null } : null
        };
        ReplaceRuntimeTop(use with { RecoveryPaidContinuation = continuation, Step = ResolutionFrameStep.ResolvingEffect });
        if (TryBeginQueuedRecoveryReplacement(useId, PostEventContinuation.RecoveryPaidCardUse)) return true;
        if (observesPayment || continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion)
        {
            if (TryBeginHpChangedProgramWindow(useId, PostEventContinuation.RecoveryPaidCardUse)) return true;
            if (observesPayment && TryBeginCardsMovedProgramWindow(useId)) return true;
            // No observer qualified. Keep the already paid response on its ordinary tail.
            var current = LifecycleCardUse(useId) ?? throw new InvalidOperationException("The paid response lost its card-use parent.");
            ReplaceRuntimeTop(current with { RecoveryPaidContinuation = null, Step = use.Step });
            return false;
        }
        return false;
    }

    private void ContinueRecoveryPaidCardUse(long useId)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Paid recovery lost its card-use producer.");
        var continuation = use.RecoveryPaidContinuation ?? throw new InvalidOperationException("Paid recovery lost its exact continuation.");
        if (_resolutionStack.LastOrDefault()?.Id != useId)
            throw new InvalidOperationException("Paid card use cannot advance before its recovery children return.");
        var observesPayment = ObservesPaidCardUseChildren(use, continuation);
        if ((observesPayment || continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion) &&
            TryBeginHpChangedProgramWindow(useId, PostEventContinuation.RecoveryPaidCardUse)) return;
        if (use.PendingRecoveryAttempts is { Count: > 0 })
            throw new InvalidOperationException("Paid card use cannot advance before its recovery children return.");
        if (observesPayment && TryBeginCardsMovedProgramWindow(useId)) return;
        ReplaceRuntimeTop(use with { RecoveryPaidContinuation = null });
        if (_winner != Winner.None)
        {
            if (ActiveCardAttack is { } completedAttack && completedAttack.ResolutionId == useId) CompleteAttack(completedAttack);
            else FinishCardUse(useId, GetTrickRepresentation(useId, continuation.CardId ?? use.CardId), continuation.PlayedCardKind ?? use.CardKind);
            return;
        }
        if (continuation.Kind is RecoveryPaidCardUseKind.Trick or RecoveryPaidCardUseKind.Simple or RecoveryPaidCardUseKind.EquipmentUse)
        {
            var card = GetTrickRepresentation(useId, continuation.CardId!.Value, requireProcessing: continuation.Kind != RecoveryPaidCardUseKind.Simple);
            if (!_players[continuation.SourceSeat].IsAlive)
            { FinishCardUse(useId, card, continuation.PlayedCardKind ?? use.CardKind); return; }
            if (continuation.Kind == RecoveryPaidCardUseKind.Trick)
                BeginJizhiOrNullificationWindow(useId, card, continuation.SourceSeat, continuation.TargetSeats!,
                    continuation.ActionKind!.Value, continuation.TargetCardId, continuation.RequiredCardKind, continuation.PlayedCardKind);
            else if (continuation.Kind == RecoveryPaidCardUseKind.Simple) BeginSimpleCardUse(useId, continuation.Simple!);
            else CompletePaidEquipmentEntry(_players[continuation.SourceSeat], card, useId,
                continuation.EquipmentSlot!.Value, continuation.ReplacedCardId);
            return;
        }
        var attack = ActiveCardAttack;
        if (attack is null || attack.ResolutionId != useId)
            throw new InvalidOperationException("A paid recovery continuation lost its actual active attack.");
        switch (continuation.Kind)
        {
            case RecoveryPaidCardUseKind.CommittedSlash:
                var action = LifecycleCardUse(useId)!.Action;
                if (action is not null && TryMarkProgramUseCommitted(useId) &&
                    TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted,
                        action.TargetSeats, ProgramCardContinuation.CommittedSlash)) return;
                BeginSlashTargetResolution(attack); break;
            case RecoveryPaidCardUseKind.SlashTarget: BeginSlashTargetResolution(attack); break;
            case RecoveryPaidCardUseKind.AttackDamage:
                if (!ApplyAttackDamage(attack)) CompleteAttack(attack); break;
            case RecoveryPaidCardUseKind.DodgeCompletion: CompleteSuccessfulDodgeResponse(attack); break;
            default: throw new InvalidOperationException("Paid card use recovery has an unsupported continuation.");
        }
    }

    private bool HasRecoveryPaidCardUseObserver(long useId) => LifecycleCardUse(useId)?.RecoveryPaidContinuation is not null &&
        (_resolutionStack.OfType<RecoveryReplacementFrame>().Any(frame => frame.ParentFrameId == useId &&
            frame.Return.ResumeFrameId == useId && frame.Return.Continuation == PostEventContinuation.RecoveryPaidCardUse) ||
         _resolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(frame => frame.Change.ParentFrameId == useId &&
            frame.ResumeFrameId == useId && frame.Continuation == PostEventContinuation.RecoveryPaidCardUse &&
            (LifecycleCardUse(useId)?.RecoveryPaidContinuation?.Kind is RecoveryPaidCardUseKind.DodgeCompletion or RecoveryPaidCardUseKind.CommittedSlash ||
             LifecycleCardUse(useId) is { RecoveryPaidContinuation: { } continuation } use && CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.HpObserverTail, use, continuation))) ||
         _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(frame =>
            LifecycleCardUse(useId) is { } owner && IsPaidCardUseMovementReturn(owner, frame)));

    private void CompletePaidEquipmentEntry(CharacterState source, Card equipment, long useId,
        EquipmentSlot slot, int? replacedCardId)
    {
        if (EquipmentCatalog.Get(equipment.Kind).Slot != slot || _cardZones.GetLocation(equipment.Id) != CardLocation.Processing)
            throw new InvalidOperationException("A paid equipment entry lost its frozen slot or processing entity.");
        // The replacement payment is complete. A child may occupy/abolish the slot;
        // that cancels this incoming entry rather than charging a second replacement.
        if (!source.IsAlive || GetEquipment(source).Count(c => EquipmentCatalog.Get(c.Kind).Slot == slot) >= source.EquipmentSlotCapacity(slot))
        {
            MoveCard(equipment, CardLocation.Processing, CardLocation.DiscardPile, new("equipment.paid-entry-cancelled"));
            FinishCardUse(useId, equipment, equipment.Kind); return;
        }
        MoveCard(equipment, CardLocation.Processing, CardLocation.Equipment(source.Seat), CardMoveReasons.EquipmentEnter);
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(useId, source.Seat, slot, equipment.Id, equipment.Kind, replacedCardId));
        AddLog("EquipmentChanged", $"{source.Name} 装备【{equipment.DisplayName}】。", source.Seat);
        BeginSimpleCardUse(useId, new(equipment.Id, SimpleCardUseEffect.Equipment));
    }

    private IReadOnlyList<RecoveryReplacementCandidate> RecoveryReplacementCandidates(int seat)
    {
        var target = _players[seat];
        if (!HasRecoveryReplacementCapability || !target.IsAlive || !target.HasHpValue || _currentSeat != seat ||
            _turnNumber <= 0 || !HappyQiaoAnyWounded(seat) || _winner != Winner.None) return [];
        var faction = GetEffectiveFactionId(target);
        // Before a pool is selected this is a candidate union, not a resolved
        // comparison. The actual selected-pool comparison is checked below.
        var thresholds = target.HappyQiaoHealth is { } dual ? new[] { dual.DaQiaoHp, dual.XiaoQiaoHp } : new[] { target.Hp };
        return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.HasHpValue && p.Seat != seat && thresholds.Any(h =>
                p.HappyQiaoHealth is { } other ? other.DaQiaoHp <= h || other.XiaoQiaoHp <= h : p.Hp <= h))
            .OrderBy(p => RelativeTableOrder(seat, p.Seat))
            .SelectMany(owner => CardPolicies(owner, SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery)
                .Where(item => item.Policy.FactionId == faction)
                .Select(item => new RecoveryReplacementCandidate(owner.Seat, item.Source.SkillId,
                    item.Source.SkillInstanceId, item.Policy.Id, item.Policy.Value, item.Policy.ProviderDrawCount)))
            .GroupBy(c => (c.OwnerSeat, c.SkillId, c.PolicyId)).Select(g => g.First()).ToArray());
    }

    private bool TryQueueRecoveryReplacement(long parentFrameId, int sourceSeat, int targetSeat,
        int amount, RecoveryAttemptCompletion completion, HappyQiaoRecoverToOrigin? happyQiaoRecoverToOrigin = null)
    {
        if (amount <= 0 || !_players[targetSeat].IsAlive || !_players[targetSeat].HasHpValue || !HappyQiaoAnyWounded(targetSeat)) return false;
        var qiao = _players[targetSeat].HappyQiaoHealth;
        if (qiao is null && !HasRecoveryReplacementCapability && !HasRecoveryAmountSupport(targetSeat) && !HasOwnRecoveryAmountSupport(targetSeat)) return false;
        var parent = parentFrameId > 0 ? _resolutionStack.Single(f => f.Id == parentFrameId) : null;
        var candidates = RecoveryReplacementCandidates(targetSeat);
        if (qiao is null && candidates.Count == 0 && !HasRecoveryAmountSupport(targetSeat) && !HasOwnRecoveryAmountSupport(targetSeat) &&
            parent?.PendingRecoveryAttempts is not { Count: > 0 }) return false;
        var request = new RecoveryAttempt(++_resolutionSequence, sourceSeat, targetSeat, amount,
            qiao is null ? _players[targetSeat].Hp : null, _turnNumber, _currentSeat, candidates, completion with
            { Policies = completion.Policies is { } policies ? Array.AsReadOnly(policies.ToArray()) : null })
        { HappyQiaoBefore = qiao, HappyQiaoRecoverToOrigin = happyQiaoRecoverToOrigin };
        if (parent is null)
        {
            PushRuntimeFrame(new RecoveryReplacementFrame(request.Id, 0, request,
                new(PostEventContinuation.RecoveryProducer)));
        }
        else
        {
            ReplaceRuntimeFrame(parent.Id, parent with { PendingRecoveryAttempts =
                Array.AsReadOnly((parent.PendingRecoveryAttempts ?? []).Append(request).ToArray()) });
        }
        RecordProgramRecoveryRequest(parent, request);
        return true;
    }

    private bool TryBeginQueuedRecoveryReplacement(long? parentFrameId, PostEventContinuation continuation,
        int? cardId = null, CardKind? cardKind = null)
    {
        if (_pendingDecision is not null || parentFrameId is null ||
            _resolutionStack.LastOrDefault() is not { } parent || parent.Id != parentFrameId ||
            parent.PendingRecoveryAttempts is not { Count: > 0 } requests) return false;
        var request = requests[0];
        ReplaceRuntimeTop(parent with { PendingRecoveryAttempts = requests.Count == 1 ? null :
            Array.AsReadOnly(requests.Skip(1).ToArray()) });
        if (continuation == PostEventContinuation.DrawFundedDistinctBasic)
            BeginDrawFundedDistinctBasicChild(parent.Id, request.Id);
        PushRuntimeFrame(new RecoveryReplacementFrame(request.Id, parent.Id, request,
            new(continuation, parent.Id, cardId, cardKind)));
        AdvanceRuntimeFrame(request.Id);
        return true;
    }

    private bool IsRecoveryReplacementCandidateCurrent(RecoveryReplacementFrame frame, RecoveryReplacementCandidate c)
    {
        var attempt = frame.Attempt;
        if (_winner != Winner.None || !IsValidPlayerSeat(c.OwnerSeat) ||
            !_players[c.OwnerSeat].IsAlive || !_players[attempt.TargetSeat].IsAlive ||
            !_players[c.OwnerSeat].HasHpValue || !_players[attempt.TargetSeat].HasHpValue ||
            attempt.ActualTurnNumber != _turnNumber || attempt.ActualTurnOwnerSeat != _currentSeat ||
            attempt.TargetSeat != _currentSeat || _players[c.OwnerSeat].Hp > HappyQiaoRecoveryHpBefore(frame)) return false;
        return CardPolicies(_players[c.OwnerSeat], SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery)
            .Any(item => item.Source.SkillId == c.SkillId && item.Source.SkillInstanceId == c.SkillInstanceId &&
                item.Policy.Id == c.PolicyId && item.Policy.FactionId == GetEffectiveFactionId(_players[attempt.TargetSeat]));
    }

    private IReadOnlyList<PromptChoice> RecoveryReplacementChoices(RecoveryReplacementFrame frame)
    {
        var choices = frame.Attempt.Candidates.Select((c, i) => (c, i))
            .Where(item => IsRecoveryReplacementCandidateCurrent(frame, item.c))
            .Select(item => new PromptChoice(new($"recovery.{frame.Id}.redirect.{item.i}"),
                $"发动【{_contentRegistry.GetSkill(item.c.SkillId).Name}】：令 {_players[item.c.OwnerSeat].Name} 回复{item.c.RecoveryAmount}点体力，然后你摸{item.c.ProviderDrawCount}张牌",
                [], [item.c.OwnerSeat], new Dictionary<string,string>
                { ["recovery-action"] = "redirect", ["candidate-index"] = item.i.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
            .Append(new(new($"recovery.{frame.Id}.keep"), $"按原效果回复{frame.Attempt.Amount}点体力", [], [],
                new Dictionary<string,string> { ["recovery-action"] = "keep" })).ToArray();
        return Array.AsReadOnly(choices);
    }

    [NativeFrameRoute(typeof(RecoveryReplacementFrame), 330)]
    private void ContinueRecoveryReplacement()
    {
        if (_resolutionStack.LastOrDefault() is not RecoveryReplacementFrame frame) return;
        if (frame.Stage == RecoveryReplacementStage.Choosing)
        {
            if (TryBeginHappyQiaoRecoveryRequestChoice(frame)) return;
            if (frame.PendingApplication is { } pending)
            {
                ApplyRecoveryReplacement(frame, pending.CandidateIndex);
                return;
            }
            var choices = RecoveryReplacementChoices(frame);
            if (choices.Count == 1 || !_players[frame.Attempt.TargetSeat].IsAlive || _winner != Winner.None)
            {
                ApplyRecoveryReplacement(frame, null);
                return;
            }
            var first = frame.Attempt.Candidates.First(c => IsRecoveryReplacementCandidateCurrent(frame, c));
            var skill = _contentRegistry.GetSkill(first.SkillId);
            ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
            _pendingDecision = new(DecisionKind.RecoveryReplacement, frame.Attempt.TargetSeat,
                $"【{skill.Name}】：是否改为令其他角色回复体力，并由你摸牌？", [], [], first.OwnerSeat)
            { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
                SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
            _status = _players[frame.Attempt.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }
        if (frame.Stage == RecoveryReplacementStage.RecoveryApplied)
        {
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.RecoveryReplacement)) return;
            ReplaceRuntimeTop(frame with { Stage = RecoveryReplacementStage.RewardApplied });
            var sequenceBefore = _movementSequence;
            if (frame.CandidateIndex is { } index && _winner == Winner.None && _players[frame.Attempt.TargetSeat].IsAlive)
                DrawCards(_players[frame.Attempt.TargetSeat], frame.Attempt.Candidates[index].ProviderDrawCount, true,
                    new CardMoveReason("skill-program.recovery-replacement.reward"));
            frame = (RecoveryReplacementFrame)_resolutionStack.Last();
            var receipt = new RecoveryRewardMovementReceipt(sequenceBefore, _movementSequence)
            {
                Batches = _pendingCardsMovedBatches.Where(batch => batch.ParentFrameId == frame.Id &&
                    batch.Movements.Count > 0 && batch.Movements.All(move =>
                        move.Sequence > sequenceBefore && move.Sequence <= _movementSequence)).ToArray()
            };
            ReplaceRuntimeTop(frame with { RewardMovementReceipt = receipt });
            frame = (RecoveryReplacementFrame)_resolutionStack.Last();
        }
        if (TryBeginCardsMovedProgramWindow(frame.Id)) return;
        PopResolutionFrame(frame.Id, ResolutionFrameKind.RecoveryReplacement);
        ResumeRecoveryReplacementProducer(frame);
    }

    private void ApplyRecoveryReplacement(RecoveryReplacementFrame frame, int? candidateIndex)
    {
        var attempt = frame.Attempt;
        if (frame.PendingApplication is null)
        {
            var selected = candidateIndex is { } selectedIndex ? attempt.Candidates[selectedIndex] : null;
            if (selected is not null && !IsRecoveryReplacementCandidateCurrent(frame, selected)) selected = null;
            candidateIndex = selected is null ? null : candidateIndex;
            ReplaceRuntimeTop(frame with
            {
                PendingApplication = new(selected is null ? attempt.SourceSeat : attempt.TargetSeat,
                    selected?.OwnerSeat ?? attempt.TargetSeat, selected?.RecoveryAmount ?? attempt.Amount, candidateIndex),
                Step = ResolutionFrameStep.ResolvingEffect
            });
        }
        if (RuntimeHooks.TryAdvance(ProgramRuntimePhase.BeforeRecoveryApply)) return;
        frame = (RecoveryReplacementFrame)_resolutionStack.Last();
        var application = frame.PendingApplication ??
            throw new InvalidOperationException("Recovery lost its prepared recipient and amount.");
        candidateIndex = application.CandidateIndex;
        var candidate = candidateIndex is { } index ? attempt.Candidates[index] : null;
        var recipient = _players[application.TargetSeat];
        var source = application.SourceSeat;
        if (TryBeginHappyQiaoRecoveryChoice(frame, application)) return;
        // No child or cost is opened between the live passive decision and this
        // single HP payment. Existing paid Cao Song support has already returned.
        var bonus = PrepareOwnRecoveryAmountBonus(application);
        var requested = bonus?.ModifiedRequestedAmount ?? application.RequestedAmount;
        var amount = recipient.IsAlive && recipient.HasHpValue && _winner == Winner.None
            ? Math.Min(requested, Math.Max(0, recipient.MaxHp - recipient.Hp)) : 0;
        ReplaceRuntimeTop(frame with { CandidateIndex = candidateIndex, Stage = RecoveryReplacementStage.RecoveryApplied,
            OriginalRecovered = candidate is null ? amount : 0, PendingApplication = null, AmountBonus = bonus, Step = ResolutionFrameStep.ResolvingEffect });
        if (candidate is not null)
            AdvanceEventRulesAndQueueFact(new RecoveryReplacementChosenEvent(frame.Id, attempt.TargetSeat, candidate.OwnerSeat,
                candidate.SkillId, candidate.SkillInstanceId, candidate.PolicyId, attempt.Amount, candidate.RecoveryAmount));
        if (amount > 0)
        {
            var child = BeginRecovery(frame.Id, source, recipient.Seat, amount);
            if (bonus is not null)
            {
                bonus = bonus with { AppliedRecoveryFrameId = child };
                var owning = (RecoveryReplacementFrame)_resolutionStack.Single(f => f.Id == frame.Id);
                ReplaceRuntimeFrame(frame.Id, owning with { AmountBonus = bonus });
            }
            recipient.Hp += amount;
            if (HasHappyQiaoHealth(recipient.Seat))
                CompleteHappyQiaoHealthPayment(frame.Id, recipient.Seat, HpChangeKind.Recovery, frame.HappyQiaoHealth!.Before.Hp(frame.HappyQiaoHealth.Pool!.Value), recipient.Hp);
            if (bonus is not null)
                AdvanceEventRulesAndQueueFact(new RecoveryAmountBonusAppliedEvent(frame.Id, bonus));
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(source, recipient.Seat, amount, recipient.Hp));
            PopResolutionFrame(child, ResolutionFrameKind.Recovery);
        }
        else ReleaseHappyQiaoHealthSelection(frame.Id);
        ApplyRecoveryAttemptProducerFacts(frame.Id, attempt, candidate is null ? amount : 0);
        ContinueRecoveryReplacement();
    }

    private CommandResult SubmitRecoveryReplacementAnswer(PromptChoice selected) => Accept(() =>
    {
        var frame = (RecoveryReplacementFrame)_resolutionStack.Last();
        var published = _pendingDecision!.Choices.Single(c => c.Id == selected.Id);
        int? index = published.Parameters.GetValueOrDefault("recovery-action") == "redirect"
            ? int.Parse(published.Parameters["candidate-index"], System.Globalization.CultureInfo.InvariantCulture) : null;
        ClearPendingDecision();
        ApplyRecoveryReplacement(frame, index);
        AdvanceRulesAndPublishState();
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    });

    private void ResolveAiRecoveryReplacement()
    {
        // A deterministic identity-role preference keeps this optional choice free of RNG consumption.
        var frame = (RecoveryReplacementFrame)_resolutionStack.Last();
        var choice = _pendingDecision!.Choices.Last();
        var actor = _players[frame.Attempt.TargetSeat];
        if (actor.Role == Role.Loyalist && _players[frame.Attempt.TargetSeat].Hp > 1)
            choice = _pendingDecision.Choices.FirstOrDefault(c => c.Targets.Count == 1 &&
                _players[c.Targets[0]].Role == Role.Lord && _players[c.Targets[0]].Hp < _players[c.Targets[0]].MaxHp) ?? choice;
        ClearPendingDecision();
        int? index = choice.Parameters.GetValueOrDefault("recovery-action") == "redirect"
            ? int.Parse(choice.Parameters["candidate-index"], System.Globalization.CultureInfo.InvariantCulture) : null;
        ApplyRecoveryReplacement(frame, index);
        AdvanceRulesAndPublishState();
    }

    private void ApplyRecoveryAttemptProducerFacts(long childId, RecoveryAttempt attempt, int recovered)
    {
        var completion = attempt.Completion;
        var parent = _resolutionStack.SingleOrDefault(f => f.Id == ((RecoveryReplacementFrame)_resolutionStack.Last()).ParentFrameId);
        if (completion.Producer == RecoveryAttemptProducer.ProgramMaximum && parent is ProgramSkillFrame maximum)
            ReplaceRuntimeFrame(parent.Id, maximum with { RecoveryReceipt = new(childId, completion.InstructionIndex, attempt.TargetSeat, recovered) });
        if (completion.Producer == RecoveryAttemptProducer.Program && parent is ProgramSkillFrame program &&
            program.WindowContext?.JudgmentReplacement is { } judgment)
            ReplaceRuntimeFrame(parent.Id, program with { WindowContext = program.WindowContext with
                { JudgmentReplacement = judgment with { RecoveredHp = judgment.RecoveredHp + recovered } } });
        if (completion.Producer == RecoveryAttemptProducer.SilverLion)
            AdvanceEventRulesAndQueueFact(new SilverLionRemovedRecoveryEvent(childId, attempt.TargetSeat, completion.MoveReason!.Value, recovered, _players[attempt.TargetSeat].Hp));
        if (completion.Producer == RecoveryAttemptProducer.CardUse && recovered > 0)
            foreach (var policy in completion.Policies ?? [])
                AdvanceEventRulesAndQueueFact(new ProgramRecoveryPolicyAppliedEvent(((RecoveryReplacementFrame)_resolutionStack.Last()).ParentFrameId,
                    attempt.TargetSeat, attempt.SourceSeat, completion.CardId!.Value, attempt.Amount, policy.SkillId, policy.PolicyId));
        if (recovered > 0) AddLog("Recovered", $"{_players[attempt.TargetSeat].Name} 回复{recovered}点体力，至 {_players[attempt.TargetSeat].Hp}/{_players[attempt.TargetSeat].MaxHp}。", attempt.SourceSeat, attempt.TargetSeat);
    }

    private void ResumeRecoveryReplacementProducer(RecoveryReplacementFrame completed)
    {
        if (ReturnCardResponseCompletionRecovery(completed)) return;
        if (ReturnCardSupplyCompletionRecovery(completed)) return;
        if (completed.Return.Continuation == PostEventContinuation.DrawFundedDistinctBasic)
        { ResumeDrawFundedDistinctBasicRecovery(completed); return; }
        var attempt = completed.Attempt;
        var continuation = completed.Return;
        if (continuation.Continuation == PostEventContinuation.RecoveryProducer)
        {
            CompleteDeferredRecoveryProducer(attempt, completed.OriginalRecovered, completed.ParentFrameId);
            return;
        }
        if (TryBeginQueuedRecoveryReplacement(continuation.ResumeFrameId, continuation.Continuation, continuation.CardId, continuation.CardKind)) return;
        if (TryBeginHpChangedProgramWindow(continuation.ResumeFrameId, continuation.Continuation, continuation.CardId, continuation.CardKind)) return;
        switch (continuation.Continuation)
        {
            case PostEventContinuation.Program: AdvanceRuntimeProgram(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.SkillInvocation:
                if (_resolutionStack.LastOrDefault() is not SkillInvocationFrame invocation || !IsSkillInvocationHealthChild(invocation, completed))
                    throw new InvalidOperationException("A skill invocation lost its exact recovery producer return.");
                AdvanceRuntimeFrame(invocation.Id); break;
            case PostEventContinuation.KemingEcho:
                if (_resolutionStack.LastOrDefault() is not KemingSkillEchoFrame echo || echo.Id != continuation.ResumeFrameId ||
                    !KemingSkillEchoFrameRidesOn(completed, echo))
                    throw new InvalidOperationException("Keming echo lost its exact recovery producer return.");
                AdvanceRuntimeFrame(echo.Id); break;
            case PostEventContinuation.FriendDuelResponse: ContinueFriendDuelResponse(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.AwaitedProgramMovement:
                if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.CardUse:
                var card = GetTrickRepresentation(continuation.ResumeFrameId!.Value, continuation.CardId!.Value);
                FinishCardUse(continuation.ResumeFrameId!.Value, card, continuation.CardKind); break;
            case PostEventContinuation.VirtualBasicCardUse: FinishVirtualBasicUse(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.GroupRecovery: CompleteGroupRecoveryTarget(); break;
            case PostEventContinuation.RecoveryReplacement: AdvanceRuntimeFrame(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.AmountSupport:
                if (_resolutionStack.LastOrDefault() is not (RecoveryReplacementFrame or BeforeDamageProgramWindowFrame))
                    throw new InvalidOperationException("Amount support lost its typed parent frame.");
                AdvanceRuntimeFrame(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.RecoveryPaidCardUse: ContinueRecoveryPaidCardUse(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.CounterspellPayment: ContinuePolicyCounterspellPayment(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.SoleIncomingRangeDraw: ContinueSoleIncomingRangeDraw(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.NativeCardEffectDraw: ContinueNativeDrawTwo(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.WenheCardEffect: ContinueWenheCardEffect(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.GuanduCardEffect: ContinueGuanduCardEffect(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.WumiaoFlood: ContinueWumiaoFlood(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.DragonBoatCardEffect: ContinueDragonBoatCardEffect(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.ColorFireAttackPayment: ContinueColorFireAttackPayment(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.EquipmentRecast: ContinueEquipmentRecast(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.DrawPhaseObligation: AdvanceRuntimeFrame(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.FactionRequestCost: ContinuePaidFactionRequestCost(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.Boundary: TryBeginCardsMovedProgramWindow(); break;
            default: throw new InvalidOperationException("A recovery attempt lost its typed producer continuation.");
        }
    }

    private void CompleteDeferredRecoveryProducer(RecoveryAttempt attempt, int recovered, long parentId)
    {
        var c = attempt.Completion;
        if (c.Producer == RecoveryAttemptProducer.Hengye)
        { ContinueTurnAfterHengye(_players[attempt.TargetSeat]); return; }
        if (c.Producer == RecoveryAttemptProducer.SilverLion)
        { if (!TryBeginHpChangedProgramWindow()) TryBeginCardsMovedProgramWindow(); return; }
        if (c.Producer is not (RecoveryAttemptProducer.DyingBoundAlcohol or RecoveryAttemptProducer.DyingVirtualAlcohol))
            throw new InvalidOperationException("A root recovery attempt lost its producer.");
        if (c.Producer == RecoveryAttemptProducer.DyingBoundAlcohol)
        {
            var use = LifecycleCardUse(parentId) ?? throw new InvalidOperationException("Bound Alcohol recovery lost its native use.");
            if (use.BoundDyingAlcoholUse is not { } token || token.ProgramFrameId != c.ProgramFrameId ||
                token.DyingFrameId != c.DyingFrameId || token.Source.SkillId != c.SkillId || token.Source.OwnerSeat != c.SkillOwnerSeat ||
                token.CardId != c.CardId || token.VictimSeat != attempt.TargetSeat || c.CardKind != CardKind.Alcohol || c.MoveReason != token.Reason ||
                attempt.SourceSeat != token.VictimSeat)
                throw new InvalidOperationException("Bound Alcohol recovery returned outside its exact typed source/use.");
            RecordBoundDyingAlcoholPayment(parentId, recovered, attempt.Id);
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == c.CardId);
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, c.MoveReason!.Value);
            FinishCardUse(parentId, card, CardKind.Alcohol);
            return; // The owning use returns the paused program after all completion children.
        }
        AdvanceEventRulesAndQueueFact(new ProgramDyingRescueEvent(c.DyingFrameId!.Value, c.SkillId!, c.SkillOwnerSeat!.Value,
            attempt.TargetSeat, c.CardId ?? 0, recovered, _players[attempt.TargetSeat].Hp));
        if (FinishActualTurnLegacyDyingAlcohol(parentId, c.ProgramFrameId!.Value)) return;
        else PopFinishedCardUse(parentId);
        AdvanceRuntimeProgram(c.ProgramFrameId!.Value);
    }

    private void AssertRecoveryReplacementInvariants()
    {
        AssertRecoveryAmountBonusReceipts();
        AssertPaidFactionRequestCostRecoveries();
        foreach (var movement in _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Where(frame => frame.ResumePaidCardUseFrameId is not null))
            if (_resolutionStack.SingleOrDefault(frame => frame.Id == movement.ResumePaidCardUseFrameId) is not CardUseFrame owner ||
                !IsPaidCardUseMovementReturn(owner, movement))
                throw new InvalidOperationException("A card-use payment lost its exact physical batch and typed return.");
        foreach (var parent in _resolutionStack)
            if (parent.PendingRecoveryAttempts is { } attempts &&
                (attempts.Count == 0 || attempts.Select(a => a.Id).Distinct().Count() != attempts.Count ||
                 attempts is not System.Collections.IList { IsReadOnly: true }))
                throw new InvalidOperationException("Recovery attempts must remain a frozen, nonempty owning-frame queue.");
        foreach (var movement in _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Where(m => m.ResumeRecoveryReplacementFrameId is not null))
            if (_resolutionStack.OfType<RecoveryReplacementFrame>().SingleOrDefault(f => f.Id == movement.ResumeRecoveryReplacementFrameId) is not { Stage: RecoveryReplacementStage.RewardApplied } owner ||
                movement.Batch.ParentFrameId != owner.Id || movement.ResumeProgramFrameId is not null || movement.ResumeDeclarationFrameId is not null)
                throw new InvalidOperationException("A recovery reward movement lost its exact completed-reward producer.");
        foreach (var frame in _resolutionStack.OfType<RecoveryReplacementFrame>())
        {
            if (frame.Return.Continuation == PostEventContinuation.CardSupplyCompletion &&
                (_resolutionStack.SingleOrDefault(parent => parent.Id == frame.ParentFrameId) is not ProgramCardTriggerWindowFrame supply ||
                 !IsCardSupplyCompletionHealthChild(supply, frame)))
                throw new InvalidOperationException("A supplied card lost its exact recovery child.");
            if (frame.Return.Continuation == PostEventContinuation.ResponseCompletion &&
                (_resolutionStack.SingleOrDefault(parent => parent.Id == frame.ParentFrameId) is not ProgramCardTriggerWindowFrame response ||
                 !IsResponseCompletionHealthChild(response, frame)))
                throw new InvalidOperationException("A response payment lost its exact recovery child.");
            if (frame.Return.Continuation == PostEventContinuation.DrawFundedDistinctBasic &&
                !IsDrawFundedDistinctBasicRecoveryParent(_resolutionStack.SingleOrDefault(parent => parent.Id == frame.ParentFrameId), frame))
                throw new InvalidOperationException("A draw-funded payment lost its exact recovery child.");
            if (frame.Id != frame.Attempt.Id || frame.Attempt.Amount <= 0 ||
                frame.CandidateIndex is { } index && (index < 0 || index >= frame.Attempt.Candidates.Count) ||
                frame.ParentFrameId > 0 && !_resolutionStack.Any(p => p.Id == frame.ParentFrameId) ||
                frame.ParentFrameId != (frame.Return.ResumeFrameId ?? frame.ParentFrameId))
                throw new InvalidOperationException("A recovery replacement lost its exact owning attempt or typed parent.");
            if (frame.PendingApplication is { } application)
            {
                var candidate = application.CandidateIndex is { } preparedIndex && preparedIndex >= 0 &&
                    preparedIndex < frame.Attempt.Candidates.Count ? frame.Attempt.Candidates[preparedIndex] : null;
                var baseAmount = candidate?.RecoveryAmount ?? frame.Attempt.Amount;
                if (frame.Stage != RecoveryReplacementStage.Choosing || frame.CandidateIndex is not null ||
                    frame.OriginalRecovered != 0 || application.CandidateIndex is not null && candidate is null ||
                    application.SourceSeat != (candidate is null ? frame.Attempt.SourceSeat : frame.Attempt.TargetSeat) ||
                    application.TargetSeat != (candidate?.OwnerSeat ?? frame.Attempt.TargetSeat) ||
                    application.RequestedAmount < baseAmount ||
                    application.RequestedAmount > SupportedRecoveryAmount(frame, baseAmount))
                    throw new InvalidOperationException("Recovery lost its frozen application or supported amount receipt.");
            }
            if (_resolutionStack.LastOrDefault()?.Id == frame.Id && frame.Step == ResolutionFrameStep.AwaitingResponse &&
                (_pendingDecision is not { Kind: DecisionKind.RecoveryReplacement } p || p.PlayerSeat != frame.Attempt.TargetSeat))
                throw new InvalidOperationException("Recovery replacement choice lost its recovering player.");
        }
    }
}
