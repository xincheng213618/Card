namespace CardGame.Core;

public enum RecoveryAttemptProducer
{
    Program, ProgramMaximum, SilverLion, CardUse, GroupCard, VirtualBasic,
    DyingBoundAlcohol, DyingVirtualAlcohol, Hengye
}

public sealed record RecoveryAttemptCompletion(RecoveryAttemptProducer Producer,
    int InstructionIndex = 0, int? CardId = null, CardKind? CardKind = null,
    CardMoveReason? MoveReason = null, long? ProgramFrameId = null,
    long? DyingFrameId = null, string? SkillId = null, int? SkillOwnerSeat = null,
    IReadOnlyList<ProgramRecoveryPolicySource>? Policies = null);

public sealed record RecoveryReplacementCandidate(int OwnerSeat, string SkillId,
    string SkillInstanceId, string PolicyId, int RecoveryAmount, int ProviderDrawCount);

/// <summary>A produced recovery belongs to its exact live rules frame, before any HP delta.</summary>
public sealed record RecoveryAttempt(long Id, int SourceSeat, int TargetSeat, int Amount,
    int HpBefore, int ActualTurnNumber, int ActualTurnOwnerSeat,
    IReadOnlyList<RecoveryReplacementCandidate> Candidates, RecoveryAttemptCompletion Completion);

public enum RecoveryReplacementStage { Choosing, RecoveryApplied, RewardApplied }

public sealed record RecoveryReplacementReturn(PostEventContinuation Continuation,
    long? ResumeFrameId = null, int? CardId = null, CardKind? CardKind = null);

public sealed record RecoveryReplacementFrame(long Id, long ParentFrameId, RecoveryAttempt Attempt,
    RecoveryReplacementReturn Return, RecoveryReplacementStage Stage = RecoveryReplacementStage.Choosing,
    int? CandidateIndex = null, int OriginalRecovered = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.RecoveryReplacement, Step);

public sealed record RecoveryReplacementChosenEvent(long RecoveryFrameId, int RecoveringSeat,
    int BeneficiarySeat, string SkillId, string SkillInstanceId, string PolicyId,
    int ReplacedAmount, int RecoveryAmount) : IGameEvent;

public enum RecoveryPaidCardUseKind { Trick, Simple, CommittedSlash, SlashTarget, AttackDamage, EquipmentUse, DodgeCompletion = 6 }

public sealed record RecoveryPaidCardUseContinuation(RecoveryPaidCardUseKind Kind, int SourceSeat,
    int? CardId = null, IReadOnlyList<int>? TargetSeats = null, LegalActionKind? ActionKind = null,
    int? TargetCardId = null, CardKind? RequiredCardKind = null, CardKind? PlayedCardKind = null,
    ProgramSimpleCardContinuation? Simple = null, int? ReplacedCardId = null, EquipmentSlot? EquipmentSlot = null);

public sealed partial class GameEngine
{
    private bool? _hasRecoveryReplacementCapability;
    private bool HasRecoveryReplacementCapability => _hasRecoveryReplacementCapability ??=
        _contentRegistry.Skills.Values.Any(s => s.Program?.CardPolicies.Any(p =>
            p.Kind == SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery) == true);

    private bool TryQueueHengyeRecovery(CharacterState player) => UsesFormalMouLuMeng &&
        player.IsAlive && HasRuntimeSkill(player, HengyeSkillId) && GetHengyeGrowth(player) >= 3 &&
        player.Hp < player.MaxHp && TryQueueRecoveryReplacement(0, player.Seat, player.Seat, 1,
            new(RecoveryAttemptProducer.Hengye));

    private bool TryPauseRecoveryPaidCardUse(long useId, RecoveryPaidCardUseContinuation continuation)
    {
        var use = LifecycleCardUse(useId);
        if (use is null || use.PendingRecoveryAttempts is not { Count: > 0 } &&
            !(continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion &&
              _pendingHpChanges.Any(change => change.ParentFrameId == useId))) return false;
        if (_resolutionStack.LastOrDefault()?.Id != useId || use.RecoveryPaidContinuation is not null)
            throw new InvalidOperationException("Paid card use recovery lost its exact fresh parent boundary.");
        continuation = continuation with
        {
            TargetSeats = continuation.TargetSeats is { } targets ? Array.AsReadOnly(targets.ToArray()) : null,
            Simple = continuation.Simple is { } simple ? simple with
            { RecoveryPolicySources = simple.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null } : null
        };
        ReplaceRuntimeTop(use with { RecoveryPaidContinuation = continuation, Step = ResolutionFrameStep.ResolvingEffect });
        if (continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion)
        {
            if (TryBeginHpChangedProgramWindow(useId, PostEventContinuation.RecoveryPaidCardUse)) return true;
            // No observer qualified. Keep the already paid response on its ordinary tail.
            var current = LifecycleCardUse(useId) ?? throw new InvalidOperationException("The paid response lost its card-use parent.");
            ReplaceRuntimeTop(current with { RecoveryPaidContinuation = null, Step = use.Step });
            return false;
        }
        return TryBeginQueuedRecoveryReplacement(useId, PostEventContinuation.RecoveryPaidCardUse);
    }

    private void ContinueRecoveryPaidCardUse(long useId)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Paid recovery lost its card-use producer.");
        var continuation = use.RecoveryPaidContinuation ?? throw new InvalidOperationException("Paid recovery lost its exact continuation.");
        if (_resolutionStack.LastOrDefault()?.Id != useId)
            throw new InvalidOperationException("Paid card use cannot advance before its recovery children return.");
        if (continuation.Kind == RecoveryPaidCardUseKind.DodgeCompletion &&
            TryBeginHpChangedProgramWindow(useId, PostEventContinuation.RecoveryPaidCardUse)) return;
        if (use.PendingRecoveryAttempts is { Count: > 0 })
            throw new InvalidOperationException("Paid card use cannot advance before its recovery children return.");
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
            LifecycleCardUse(useId)?.RecoveryPaidContinuation?.Kind == RecoveryPaidCardUseKind.DodgeCompletion));

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
        if (!HasRecoveryReplacementCapability || !target.IsAlive || _currentSeat != seat ||
            _turnNumber <= 0 || target.Hp >= target.MaxHp || _winner != Winner.None) return [];
        var faction = GetEffectiveFactionId(target);
        return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != seat && p.Hp <= target.Hp)
            .OrderBy(p => (p.Seat - seat + _players.Count) % _players.Count)
            .SelectMany(owner => CardPolicies(owner, SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery)
                .Where(item => item.Policy.FactionId == faction)
                .Select(item => new RecoveryReplacementCandidate(owner.Seat, item.Source.SkillId,
                    item.Source.SkillInstanceId, item.Policy.Id, item.Policy.Value, item.Policy.ProviderDrawCount)))
            .GroupBy(c => (c.OwnerSeat, c.SkillId, c.PolicyId)).Select(g => g.First()).ToArray());
    }

    private bool TryQueueRecoveryReplacement(long parentFrameId, int sourceSeat, int targetSeat,
        int amount, RecoveryAttemptCompletion completion)
    {
        if (!HasRecoveryReplacementCapability || amount <= 0 || !_players[targetSeat].IsAlive ||
            _players[targetSeat].Hp >= _players[targetSeat].MaxHp) return false;
        var parent = parentFrameId > 0 ? _resolutionStack.Single(f => f.Id == parentFrameId) : null;
        var candidates = RecoveryReplacementCandidates(targetSeat);
        if (candidates.Count == 0 && parent?.PendingRecoveryAttempts is not { Count: > 0 }) return false;
        var request = new RecoveryAttempt(++_resolutionSequence, sourceSeat, targetSeat, amount,
            _players[targetSeat].Hp, _turnNumber, _currentSeat, candidates, completion with
            { Policies = completion.Policies is { } policies ? Array.AsReadOnly(policies.ToArray()) : null });
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
            attempt.ActualTurnNumber != _turnNumber || attempt.ActualTurnOwnerSeat != _currentSeat ||
            attempt.TargetSeat != _currentSeat || _players[c.OwnerSeat].Hp > attempt.HpBefore) return false;
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

    private void ContinueRecoveryReplacement()
    {
        if (_resolutionStack.LastOrDefault() is not RecoveryReplacementFrame frame) return;
        if (frame.Stage == RecoveryReplacementStage.Choosing)
        {
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
            if (frame.CandidateIndex is { } index && _winner == Winner.None && _players[frame.Attempt.TargetSeat].IsAlive)
                DrawCards(_players[frame.Attempt.TargetSeat], frame.Attempt.Candidates[index].ProviderDrawCount, true,
                    new CardMoveReason("skill-program.recovery-replacement.reward"));
            frame = (RecoveryReplacementFrame)_resolutionStack.Last();
        }
        if (TryBeginCardsMovedProgramWindow(frame.Id)) return;
        PopResolutionFrame(frame.Id, ResolutionFrameKind.RecoveryReplacement);
        ResumeRecoveryReplacementProducer(frame);
    }

    private void ApplyRecoveryReplacement(RecoveryReplacementFrame frame, int? candidateIndex)
    {
        var attempt = frame.Attempt;
        var candidate = candidateIndex is { } index ? attempt.Candidates[index] : null;
        if (candidate is not null && !IsRecoveryReplacementCandidateCurrent(frame, candidate)) candidate = null;
        candidateIndex = candidate is null ? null : candidateIndex;
        var recipient = _players[candidate?.OwnerSeat ?? attempt.TargetSeat];
        var source = candidate is null ? attempt.SourceSeat : attempt.TargetSeat;
        var amount = recipient.IsAlive && _winner == Winner.None
            ? Math.Min(candidate?.RecoveryAmount ?? attempt.Amount, Math.Max(0, recipient.MaxHp - recipient.Hp)) : 0;
        ReplaceRuntimeTop(frame with { CandidateIndex = candidateIndex, Stage = RecoveryReplacementStage.RecoveryApplied,
            OriginalRecovered = candidate is null ? amount : 0, Step = ResolutionFrameStep.ResolvingEffect });
        if (candidate is not null)
            AdvanceEventRulesAndQueueFact(new RecoveryReplacementChosenEvent(frame.Id, attempt.TargetSeat, candidate.OwnerSeat,
                candidate.SkillId, candidate.SkillInstanceId, candidate.PolicyId, attempt.Amount, candidate.RecoveryAmount));
        if (amount > 0)
        {
            var child = BeginRecovery(frame.Id, source, recipient.Seat, amount);
            recipient.Hp += amount;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(source, recipient.Seat, amount, recipient.Hp));
            PopResolutionFrame(child, ResolutionFrameKind.Recovery);
        }
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
            case PostEventContinuation.AwaitedProgramMovement:
                if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.CardUse:
                var card = GetTrickRepresentation(continuation.ResumeFrameId!.Value, continuation.CardId!.Value);
                FinishCardUse(continuation.ResumeFrameId!.Value, card, continuation.CardKind); break;
            case PostEventContinuation.VirtualBasicCardUse: FinishVirtualBasicUse(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.GroupRecovery: CompleteGroupRecoveryTarget(); break;
            case PostEventContinuation.RecoveryReplacement: AdvanceRuntimeFrame(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.RecoveryPaidCardUse: ContinueRecoveryPaidCardUse(continuation.ResumeFrameId!.Value); break;
            case PostEventContinuation.CounterspellPayment: ContinuePolicyCounterspellPayment(continuation.ResumeFrameId!.Value); break;
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
        AdvanceEventRulesAndQueueFact(new ProgramDyingRescueEvent(c.DyingFrameId!.Value, c.SkillId!, c.SkillOwnerSeat!.Value,
            attempt.TargetSeat, c.CardId ?? 0, recovered, _players[attempt.TargetSeat].Hp));
        if (c.Producer == RecoveryAttemptProducer.DyingBoundAlcohol)
        {
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == c.CardId);
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, c.MoveReason!.Value);
            FinishCardUse(parentId, card, CardKind.Alcohol);
        }
        else if (FinishActualTurnLegacyDyingAlcohol(parentId, c.ProgramFrameId!.Value)) return;
        else PopFinishedCardUse(parentId);
        AdvanceRuntimeProgram(c.ProgramFrameId!.Value);
    }

    private void AssertRecoveryReplacementInvariants()
    {
        AssertPaidFactionRequestCostRecoveries();
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
            if (_resolutionStack.LastOrDefault()?.Id == frame.Id && frame.Step == ResolutionFrameStep.AwaitingResponse &&
                (_pendingDecision is not { Kind: DecisionKind.RecoveryReplacement } p || p.PlayerSeat != frame.Attempt.TargetSeat))
                throw new InvalidOperationException("Recovery replacement choice lost its recovering player.");
        }
    }
}
