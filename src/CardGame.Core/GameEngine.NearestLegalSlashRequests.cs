namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome RequestLegalNearestSlashes(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        // An assisted faction child leaves exactly one result on this same
        // frame. Consume it before advancing the already committed actor cursor.
        if (active.NearestLegalSlashRequest is { AwaitingFaction: true } draft)
        {
            var answer = active.ChoiceBindings.SingleOrDefault(binding => binding.Name == draft.ResultBind) ??
                throw new InvalidOperationException("A nearest faction child returned without its typed result.");
            if (answer.OptionId is not ("declined" or "used-slash"))
                throw new InvalidOperationException("A nearest faction child returned an unknown result.");
            if (!IsNearestFactionChoiceResult(active, answer)) throw new InvalidOperationException("A nearest faction result lost its committed producer.");
            ReplaceRuntimeTop(active with { NearestLegalSlashRequest = null,
                ChoiceBindings = Array.AsReadOnly(active.ChoiceBindings.Where(binding => binding.Name != draft.ResultBind).ToArray()) });
            active = GetActiveProgramFrame(frame.Id);
            if (answer.OptionId == "declined" && _players[draft.ActorSeat].IsAlive)
            {
                // The executor already consumed the previous reexecute flag
                // before dispatching this returned faction result. Re-arm only
                // the next participant: the stored cursor already paid this one.
                ReplaceRuntimeTop(active with { ReexecuteParticipantInstruction = true });
                return new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, draft.ActorSeat, effect.Amount);
            }
        }
        return ExecuteParticipantReserve(effect, active);
    }

    private SkillProgramStepOutcome RequestLegalSlashByNearestActor(ProgramSkillFrame frame, int actorSeat, int hpAmount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.NearestLegalSlashRequest is not null || active.AssistedSlashRequest is not null || actorSeat == active.OwnerSeat)
            throw new InvalidOperationException("A nearest legal Slash requires one clean other actor.");
        if (!_players[active.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId))
        { CancelProgramBindingAndCleanup(active, "技能来源已失效，剩余结算取消。"); return SkillProgramStepOutcome.AwaitChild; }
        var nearest = GetNearestLivingCharacterSeats(actorSeat);
        active = active with
        {
            SelectedTargetSeats = Array.AsReadOnly(new[] { actorSeat }),
            NearestLegalSlashRequest = new(actorSeat, Array.AsReadOnly(nearest), $"nearest-{active.InstructionIndex}-{actorSeat}")
        };
        ReplaceRuntimeTop(active);
        var choices = NearestLegalSlashChoices(active);
        if (choices.All(choice => choice.Parameters["request-option"] == "decline"))
        {
            ReplaceRuntimeTop(active with { NearestLegalSlashRequest = null });
            return new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, actorSeat, hpAmount);
        }
        var skill = _contentRegistry.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, actorSeat,
            "对距离最小的另一名角色使用一张【杀】，或失去1点体力。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), nearest, active.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = actorSeat,
            SkillPrompt = new(active.SkillId, skill.Name, skill.Name, skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[actorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> NearestLegalSlashChoices(ProgramSkillFrame frame)
    {
        var draft = frame.NearestLegalSlashRequest ?? throw new InvalidOperationException("Nearest Slash draft disappeared.");
        if (draft.AwaitingFaction) return [];
        var choices = new List<PromptChoice>();
        foreach (var target in draft.NearestSeats)
        {
            // Pure payment enumeration reuses the existing conversion, Zhangba,
            // range, restriction and faction rules. This view is never stored.
            var view = frame with { AssistedSlashRequest = new(draft.ActorSeat, target, ActorChoosesTarget: true) };
            foreach (var choice in NearestPhysicalSlashChoices(view).Where(choice => choice.Parameters["request-option"] != "decline"))
            {
                var parameters = choice.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
                parameters["program-action"] = "nearest-legal-slash";
                choices.Add(choice with
                {
                    Id = new ChoiceId($"nearest.{frame.Id}.{draft.ActorSeat}.{target}.{choice.Id.Value}"),
                    Description = choice.Description + $" → {_players[target].Name}", Parameters = parameters
                });
            }
        }
        choices.Add(new(new ChoiceId($"nearest.{frame.Id}.{draft.ActorSeat}.decline"), "失去1点体力。", [], [],
            new Dictionary<string, string> { ["program-action"] = "nearest-legal-slash", ["request-option"] = "decline", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        return choices;
    }

    private IReadOnlyList<PromptChoice> NearestPhysicalSlashChoices(ProgramSkillFrame view)
    {
        var draft = view.AssistedSlashRequest!; var actor = _players[draft.ActorSeat]; var target = draft.TargetSeat!.Value;
        bool ExactSingleSourceLegal(PromptChoice choice)
        {
            if (choice.Parameters["request-option"] != "use" || choice.Cards.Count != 1) return true;
            var card = GetPlayableCards(actor).Concat(GetEquipment(actor)).Single(item => item.Id == choice.Cards[0]);
            TryReadConversionSource(choice.Parameters, out var conversion);
            var kind = Enum.Parse<CardKind>(choice.Parameters["effective-kind"]);
            return NearestSlashPaymentHasRange(actor, target, [card], conversion, kind) && CanUseSlashTarget(actor, _players[target], card, conversion, kind) &&
                !IsTurnHandCardRestricted(actor, card) && !IsTurnPhysicalUseForbidden(actor.Seat, choice.Cards);
        }
        var choices = AssistedPhysicalSlashChoices(view).Where(ExactSingleSourceLegal).ToList();
        // Historical assisted consumers perform a native range precheck before
        // creating variants. Only this new opt-in path adds variants whose exact
        // identity/view-as source supplies its own distance exemption.
        foreach (var card in GetSlashUseCards(actor))
        {
            var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault(item => IsSlashCard(item.Identity.OutputKind));
            var baseKind = identity?.Identity.OutputKind ?? (IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash);
            foreach (var kind in GetSlashUseKinds(actor, baseKind))
            {
                var parameters = new Dictionary<string, string> { ["program-action"] = "assisted-physical-slash",
                    ["frame-id"] = view.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["request-option"] = "use", ["effective-kind"] = kind.ToString() };
                choices.AddRange(CreateConversionChoiceVariants(actor, card, kind, false,
                    $"assisted-slash.{view.Id}.card-{card.Id}.{kind}", $"使用【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [target], parameters).Where(ExactSingleSourceLegal));
            }
        }
        return Array.AsReadOnly(choices.DistinctBy(choice => choice.Id).ToArray());
    }

    private bool IsNearestFactionChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding)
    {
        if (frame.NearestLegalSlashRequest is not { AwaitingFaction: true, TargetSeat: { } target } draft || binding.Name != draft.ResultBind)
            return false;
        if (!IsNearestLegalFactionOrigin(frame, draft.ActorSeat, target, binding.Name) || binding.ChooserSeat != draft.ActorSeat ||
            binding.OptionId is not ("declined" or "used-slash") ||
            !_events.Select(item => item.Payload).Concat(_pendingEvents).OfType<ProgramOptionChosenEvent>().Any(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.BindingId == GetProgramBindingId(frame) &&
                fact.OwnerSeat == frame.OwnerSeat && fact.ResultBind == binding.Name && fact.ChooserSeat == binding.ChooserSeat && fact.OptionId == binding.OptionId))
            throw new InvalidOperationException("A nearest faction result changed its exact owning instruction, actor or committed fact.");
        return true;
    }

    private void ResolveNearestLegalSlashChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Nearest legal Slash parent disappeared.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var draft = frame.NearestLegalSlashRequest ?? throw new InvalidOperationException("Nearest legal Slash draft disappeared.");
        if (effect.Op != SkillProgramEffectOp.RequestLegalSlashByNearest || draft.AwaitingFaction ||
            _pendingDecision?.PlayerSeat != draft.ActorSeat || !draft.NearestSeats.SequenceEqual(GetNearestLivingCharacterSeats(draft.ActorSeat)))
            throw new InvalidOperationException("Nearest legal Slash lost its exact instruction, actor or distance frontier.");
        var canonical = NearestLegalSlashChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ??
            throw new InvalidOperationException("Nearest legal Slash payment or target is no longer available.");
        if (!AssistedChoicesEqual([canonical], [selected])) throw new InvalidOperationException("Nearest legal Slash answer changed its frozen payload.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.ActorSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "受令角色或技能来源已失效。"); return; }
        if (canonical.Parameters["request-option"] == "decline")
        {
            ReplaceRuntimeTop(frame with { NearestLegalSlashRequest = null });
            if (new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, draft.ActorSeat, effect.Amount) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(frame.Id);
            return;
        }
        var target = canonical.Targets.Single();
        if (canonical.Parameters["request-option"] == "faction")
        {
            ReplaceRuntimeTop(frame with { NearestLegalSlashRequest = draft with { TargetSeat = target, AwaitingFaction = true } });
            BeginAssistedProgramFactionSlashRequest(GetActiveProgramFrame(frame.Id), draft.ActorSeat, target, draft.ResultBind);
            return;
        }
        var actor = _players[draft.ActorSeat];
        var (cards, kind, conversion) = ReadAssistedSlashPayment(actor, canonical);
        if (!draft.NearestSeats.Contains(target) || !NearestSlashPaymentHasRange(actor, target, cards, conversion, kind) ||
            !CanUseSlashTarget(actor, _players[target], cards[0], conversion, kind, noEffectiveRank: cards.Count > 1,
                specificEffectiveRank: canonical.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString() ? ZhangbaSpecificSlashRank(actor, cards) : null, physicalCardIds:cards.Select(c=>c.Id).ToArray()))
            throw new InvalidOperationException("Nearest legal Slash target or material became illegal.");
        ReplaceRuntimeTop(frame with { NearestLegalSlashRequest = null });
        ResolveSlashCore(actor, _players[target], cards[0], kind, actor.Seat, physicalCards: cards, countsTowardSlashLimit: false,
            usesZhuqueFan: kind == CardKind.FireSlash && cards[0].Kind == CardKind.Slash && HasZhuqueFan(actor),
            conversionSource: conversion, programSkillCardUseFrameId: frame.Id);
    }

    // Paying a weapon cannot borrow the range of that discarded weapon. A
    // separately qualified distance exemption survives its payment, however.
    // Historical assisted consumers retain their existing range predicate.
    private bool NearestSlashPaymentHasRange(CharacterState actor, int target, IReadOnlyList<Card> cards,
        CardConversionSource? conversion, CardKind kind) => AssistedSlashPaymentHasRange(actor, target, cards) ||
        (HasProvenanceUseDistance(actor, cards.Select(c=>c.Id).ToArray()) || HasGrantedPhaseEntityDistance(actor, cards.Select(c=>c.Id).ToArray())) || IgnoresProgramSlashDistance(actor, conversion) ||
        IgnoresSpGuanYuWushengDistance(actor, cards[0]) ||
        HasSlashUseDistanceBySuit(actor, kind, EffectiveSuit(actor, cards[0])) ||
        HasTurnRedSlashPolicy(actor.Seat, kind, EffectiveSuit(actor, cards[0])) ||
        HasPhaseSuitAllowance(actor, cards[0]) || HasCardDistanceExemption(actor, _players[target], kind) ||
        HasUnlimitedTurnRuleModifier(actor.Seat, SkillRuleQuery.SlashDistanceLimit);

    private bool IsNearestLegalFactionOrigin(ProgramSkillFrame frame, int actor, int target, string? bind) =>
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect.Op == SkillProgramEffectOp.RequestLegalSlashByNearest &&
        frame.NearestLegalSlashRequest is { AwaitingFaction: true } draft && draft.ActorSeat == actor &&
        draft.TargetSeat == target && draft.ResultBind == bind && draft.NearestSeats.Contains(target) &&
        frame.SelectedTargetSeats.SequenceEqual([actor]) && actor != frame.OwnerSeat;

    private bool ValidateNearestLegalFactionParent(FactionCardRequestHandle pending, ProgramSkillFrame frame)
    {
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.RequestLegalSlashByNearest) return false;
        var answer = frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == pending.AssistedResultBind);
        if (pending.TargetSeat is not { } target || !IsNearestLegalFactionOrigin(frame, pending.OwnerSeat, target, pending.AssistedResultBind) ||
            pending.PolicySource is null || (pending.ActiveAttack is null ? answer is not null : answer?.OptionId != "used-slash"))
            throw new InvalidOperationException("Nearest faction child changed its frozen parent, actor, target or paid result.");
        return true;
    }

    private int[] UnlimitedVirtualSlashTargets(ProgramSkillFrame frame) => _players.Where(target =>
        _players[frame.OwnerSeat].IsAlive && target.IsAlive && target.Seat != frame.OwnerSeat &&
        CanSpendSlashUse(_players[frame.OwnerSeat], target, ignoresCount: true, CardKind.Slash) &&
        !IsSlashProhibited(target)).Select(target => target.Seat).ToArray();

    private IReadOnlyList<PromptChoice> UnlimitedVirtualSlashChoices(ProgramSkillFrame frame)
    {
        Dictionary<string, string> Parameters(string option) => new()
        { ["program-action"] = "unlimited-virtual-slash", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["offer-option"] = option };
        return UnlimitedVirtualSlashTargets(frame).Select(target => new PromptChoice(new ChoiceId($"unlimited-slash.{frame.Id}.{target}"),
                $"视为对 {_players[target].Name} 使用【杀】", [], [target], Parameters("use")))
            .Append(new(new ChoiceId($"unlimited-slash.{frame.Id}.decline"), "不使用【杀】。", [], [], Parameters("decline"))).ToArray();
    }

    private SkillProgramStepOutcome OfferUnlimitedVirtualSlash(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.NearestLegalSlashRequest is not null ||
            _currentSeat != active.OwnerSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("An unlimited Slash offer requires a completed current active sequence.");
        // The participant selection belonged to the previous instruction.
        // This optional use selects its own target only when answered.
        active = active with { SelectedTargetSeats = Array.AsReadOnly(Array.Empty<int>()) };
        ReplaceRuntimeTop(active);
        if (UnlimitedVirtualSlashTargets(active).Length == 0) return SkillProgramStepOutcome.Continue;
        var skill = _contentRegistry.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, active.OwnerSeat, "你可以视为使用一张无距离限制的【杀】。", [], UnlimitedVirtualSlashTargets(active), active.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = false, Choices = Array.AsReadOnly(UnlimitedVirtualSlashChoices(active).ToArray()),
            SkillPrompt = new(active.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveUnlimitedVirtualSlashChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Unlimited Slash parent disappeared.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var canonical = UnlimitedVirtualSlashChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ??
            throw new InvalidOperationException("Unlimited Slash target became unavailable.");
        if (effect.Op != SkillProgramEffectOp.OfferUnlimitedVirtualSlash || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([selected], [canonical])) throw new InvalidOperationException("Unlimited Slash answer lost its exact frame or target.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "虚拟杀的技能来源已失效。"); return; }
        var used = canonical.Parameters["offer-option"] == "use";
        AdvanceEventRulesAndQueueFact(new ProgramUnlimitedSlashChoiceEvent(frame.Id, frame.SkillId, frame.SkillInstanceId, frame.OwnerSeat,
            used ? canonical.Targets.Single() : null, used));
        if (!used) { AdvanceRuntimeProgram(frame.Id); return; }
        ReplaceRuntimeTop(frame with { SelectedTargetSeats = Array.AsReadOnly(canonical.Targets.ToArray()) });
        if (BeginProgramVirtualCardUse(GetActiveProgramFrame(frame.Id), canonical.Targets.Single(), CardKind.Slash, ignoreDistance: true) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
    }

    private PromptChoice SelectAiNearestLegalSlash(PendingDecision decision) => decision.Choices
        .Select(choice => (Choice: choice, Value: choice.Parameters["request-option"] == "decline" ? -22d :
            _aiBrains[decision.PlayerSeat].ScoreProgramTarget(CreateSnapshot(decision.PlayerSeat), choice.Targets.Single(), new(0, 0, 0, 0, 0, 1, false, false))))
        .OrderByDescending(item => item.Value).ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).First().Choice;

    private PromptChoice SelectAiUnlimitedSlash(PendingDecision decision) => decision.Choices
        .Select(choice => (Choice: choice, Value: choice.Parameters["offer-option"] == "decline" ? 0d :
            _aiBrains[decision.PlayerSeat].ScoreProgramTarget(CreateSnapshot(decision.PlayerSeat), choice.Targets.Single(), new(0, 0, 0, 0, 0, 1, false, false))))
        .OrderByDescending(item => item.Value).ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).First().Choice;

    private void AssertNearestLegalSlashState(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.NearestLegalSlashRequest is { } draft)
        {
            if (paused.Op != SkillProgramEffectOp.RequestLegalSlashByNearest || !frame.ReexecuteParticipantInstruction ||
                !IsValidPlayerSeat(draft.ActorSeat) || draft.ActorSeat == frame.OwnerSeat ||
                !frame.SelectedTargetSeats.SequenceEqual([draft.ActorSeat]) || draft.NearestSeats.Distinct().Count() != draft.NearestSeats.Count ||
                draft.NearestSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == draft.ActorSeat) ||
                draft.AwaitingFaction != (draft.TargetSeat is not null) || draft.TargetSeat is { } target && !draft.NearestSeats.Contains(target))
                throw new InvalidOperationException("Nearest legal Slash owning draft lost its exact cursor or frozen actors.");
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && !draft.AwaitingFaction &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != draft.ActorSeat ||
                 !AssistedChoicesEqual(decision.Choices, NearestLegalSlashChoices(frame))))
                throw new InvalidOperationException("Nearest legal Slash prompt changed its actual material choices.");
        }
        if (paused.Op == SkillProgramEffectOp.OfferUnlimitedVirtualSlash && ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } offer || offer.PlayerSeat != frame.OwnerSeat ||
             !AssistedChoicesEqual(offer.Choices, UnlimitedVirtualSlashChoices(frame))))
            throw new InvalidOperationException("Unlimited Slash offer changed its exact live prompt.");
    }

    private bool IsValidNearestSlashProgramSelection(ProgramSkillFrame frame, SkillProgramEffect effect,
        IReadOnlyList<SkillProgramEffect> instructions)
    {
        var ownerIndex = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        var child = ownerIndex >= 0 && ownerIndex + 1 < _resolutionStack.Count ? _resolutionStack[ownerIndex + 1] : null;
        if (effect.Op == SkillProgramEffectOp.RequestLegalSlashByNearest)
        {
            var instructionIndex = Enumerable.Range(0, Math.Min(frame.InstructionIndex, instructions.Count))
                .Single(index => ReferenceEquals(instructions[index], effect));
            var cursor = frame.NumberBindings.SingleOrDefault(binding => binding.Name == "participant-cursor-" + instructionIndex)?.Value ?? 0;
            if (cursor < 1 || cursor >= _playerCount || !frame.ReexecuteParticipantInstruction ||
                frame.SelectedTargetSeats is not [var actor] || actor != (frame.OwnerSeat + cursor) % _playerCount)
                return false;
            if (frame.NearestLegalSlashRequest is { } draft) return draft.ActorSeat == actor;
            return child switch
            {
                CardUseFrame use => use.SourceSeat == actor && use.CardAttack?.ProgramSkillCardUseFrameId == frame.Id &&
                    use.Action is { Type: CardActionType.Use } action && action.ActorSeat == actor && IsSlashCard(use.CardKind),
                DyingFrame dying => dying.ParentFrameId == frame.Id && dying.Continuation == DyingContinuationKind.ProgramSkill && dying.VictimSeat == actor,
                HpChangedTriggerWindowFrame hp => hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.Program &&
                    hp.Change.ParentFrameId == frame.Id && hp.Change.TargetSeat == actor,
                _ => false
            };
        }
        if (effect.Op != SkillProgramEffectOp.OfferUnlimitedVirtualSlash || frame.TriggerId is not null) return false;
        if (frame.SelectedTargetSeats.Count == 0)
            return ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger } offer && offer.PlayerSeat == frame.OwnerSeat &&
                AssistedChoicesEqual(offer.Choices, UnlimitedVirtualSlashChoices(frame));
        return frame.SelectedTargetSeats is [var target] && IsValidPlayerSeat(target) && target != frame.OwnerSeat &&
            _events.Select(item => item.Payload).Concat(_pendingEvents).OfType<ProgramUnlimitedSlashChoiceEvent>().Any(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.SkillInstanceId == frame.SkillInstanceId &&
                fact.OwnerSeat == frame.OwnerSeat && fact.Used && fact.TargetSeat == target) &&
            child is CardUseFrame { CardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } virtualUse &&
            virtualUse.SourceSeat == frame.OwnerSeat && virtualUse.CardAttack?.ProgramSkillCardUseFrameId == frame.Id &&
            virtualUse.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0 } virtualAction &&
            virtualAction.ActorSeat == frame.OwnerSeat &&
            (virtualAction.TargetSeats.SequenceEqual([target]) ||
                TryGetOriginalTargetVirtualSlashReturn(virtualUse, frame, out _));
    }

    private sealed partial class ProgramSkillHost : INearestLegalSlashProgramHost
    {
        public SkillProgramStepOutcome RequestLegalNearestSlashes(SkillProgramEffect effect, ProgramSkillFrame frame) => engine.RequestLegalNearestSlashes(effect, frame);
        public SkillProgramStepOutcome OfferUnlimitedVirtualSlash(ProgramSkillFrame frame) => engine.OfferUnlimitedVirtualSlash(frame);
    }
}
