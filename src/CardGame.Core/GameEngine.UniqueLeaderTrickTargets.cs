using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksUniqueLeaderTrickTargets => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage);

    private void CaptureUniqueLeaderTrickQualification(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!TracksUniqueLeaderTrickTargets || window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            action.Type != CardActionType.Use || !IsOrdinaryTrick(action.EffectiveKind) ||
            action.ResponderSeat is not null || action.OpponentSeat is not null ||
            _resolutionStack.LastOrDefault() is not CardUseFrame use || use.Action?.ActionId != action.ActionId ||
            use.SourceSeat != action.ActorSeat || use.CardKind != action.EffectiveKind ||
            !use.TargetSeats.SequenceEqual(action.TargetSeats)) return;
        var history = CompleteProgramEventHistory().OfType<UniqueLeaderTrickQualificationEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        if (history.Length != 0)
        {
            if (history.Length != 1 || history[0].ActionId != action.ActionId)
                throw new InvalidOperationException("A native trick cannot replace its frozen unique-leader qualification.");
            return;
        }
        var living = _players.Where(p => p.IsAlive).ToArray();
        int? Unique(Func<CharacterState, int> value)
        {
            if (living.Length == 0) return null;
            var maximum = living.Max(value);
            var winners = living.Where(p => value(p) == maximum).ToArray();
            return winners.Length == 1 ? winners[0].Seat : null;
        }
        var primary = action.EffectiveKind == CardKind.BorrowedSword
            ? action.TargetSeats.Where((_, i) => i % 2 == 0).ToArray()
            : action.TargetSeats.Count == 0 && action.EffectiveKind == CardKind.DrawTwo ? [action.ActorSeat] : action.EffectiveDesignatedTargetSeats.ToArray();
        AdvanceEventRulesAndQueueFact(new UniqueLeaderTrickQualificationEvent(use.Id, action.ActionId, action.ActorSeat,
            Unique(p => GetHand(p).Count), Unique(p => p.Hp), primary.Length, primary.Length == 1 ? primary[0] : null));
    }

    private UniqueLeaderTrickQualificationEvent? UniqueLeaderTrickQualification(CardUseFrame use) =>
        CompleteProgramEventHistory().OfType<UniqueLeaderTrickQualificationEvent>().SingleOrDefault(e => e.CardUseFrameId == use.Id && e.ActionId == use.Action?.ActionId);

    private bool TryGetUniqueLeaderTrickUse(ProgramSkillWindowContext context, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        return context.CardUse is { } identity && TryGetDesignatedExtraTargetUse(identity.ActorSeat, context, out use, out window) &&
            window.Continuation == ProgramCardContinuation.FinalizedTrick && IsOrdinaryTrick(use.CardKind) &&
            UniqueLeaderTrickQualification(use) is { } qualification && qualification.ActorSeat == identity.ActorSeat;
    }

    private bool UniqueLeaderTrickEligible(CardUseFrame use, int owner, SkillProgramEffectOp operation)
    {
        var qualification = UniqueLeaderTrickQualification(use);
        return IsValidPlayerSeat(owner) && _players[owner].IsAlive && qualification is not null &&
            (operation == SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition
                ? qualification.UniqueLargestHandSeat == qualification.ActorSeat
                : operation == SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage &&
                  qualification.OriginalPrimaryTargetCount == 1 && qualification.OriginalSinglePrimaryTargetSeat is { } sole &&
                  sole != owner && qualification.UniqueLargestHpSeat == sole);
    }

    private bool CanBeUniqueLeaderTrickTarget(CardUseFrame use, CharacterState target)
    {
        if (use.Action is not { } action || !IsOrdinaryTrick(use.CardKind) || !target.IsAlive ||
            HasTurnCardTargetRestriction(action.ActorSeat, SkillProgramCardTargetRestriction.SelfOnly) && target.Seat != action.ActorSeat) return false;
        if (use.CardKind == CardKind.UnexpectedAssault)
            return !action.EffectiveDesignatedTargetSeats.Contains(target.Seat) && CanBeProgramCardUseRoleTarget(use, target) &&
                !IsSelfTargetForbiddenAction(_players[action.ActorSeat], use.CardKind, [target.Seat]);
        if (!CanBeDistanceFreeDesignatedTarget(use, target)) return false;
        if (use.CardKind != CardKind.Snatch) return true;
        var actor = _players[action.ActorSeat];
        return HasIssuedProvenanceUseDistance(use.Id, actor.Seat) || HasIssuedGrantedPhaseEntityDistance(use.Id, actor.Seat) ||
            HasProvenanceUseDistance(actor, use.PhysicalCardIds) || HasGrantedPhaseEntityDistance(actor, use.PhysicalCardIds) ||
            HasCardDistanceExemption(actor, target, use.CardKind, use.Id) ||
            HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, use.CardKind) || GetCombatDistance(actor.Seat, target.Seat) == 1;
    }

    private IReadOnlyList<IReadOnlyList<int>> UniqueLeaderTrickTargetCandidates(CardUseFrame use, int owner, SkillProgramEffectOp operation)
    {
        if (!UniqueLeaderTrickEligible(use, owner, operation)) return Array.AsReadOnly(Array.Empty<IReadOnlyList<int>>());
        var result = new List<IReadOnlyList<int>>();
        foreach (var target in _players.Where(p => (operation != SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage || p.Seat == owner) &&
                     CanBeUniqueLeaderTrickTarget(use, p)).OrderBy(p => p.Seat))
            if (use.CardKind == CardKind.BorrowedSword)
                foreach (var victim in _players.Where(p => IsLegalBorrowedSwordSlashTarget(target, p)).OrderBy(p => p.Seat))
                    result.Add(Array.AsReadOnly(new[] { target.Seat, victim.Seat }));
            else result.Add(Array.AsReadOnly(new[] { target.Seat }));
        return Array.AsReadOnly(result.ToArray());
    }

    private bool CanOfferUniqueLeaderTrickTarget(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [{ } effect] || !UniqueLeaderTrickTargetContract.IsTargetOperation(effect.Op)) return true;
        return TryGetUniqueLeaderTrickUse(context, out var use, out var window) && DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
            !CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetOfferedEvent>().Any(e => e.CardUseFrameId == use.Id &&
                e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId) &&
            UniqueLeaderTrickTargetCandidates(use, candidate.OwnerSeat, effect.Op).Count > 0;
    }

    private CardUseFrame RequireUniqueLeaderTrickParent(ProgramSkillFrame frame, out ProgramCardTriggerWindowFrame window)
    {
        if (frame.TriggerId is not { } binding || frame.WindowContext is not { } context || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            !TryGetUniqueLeaderTrickUse(context, out var use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(frame.OwnerSeat, frame.SkillId, binding, frame.SkillInstanceId, frame.GameplayHash, 0), context, window) ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == binding)?.Effects is not [{ } effect] ||
            !UniqueLeaderTrickTargetContract.IsTargetOperation(effect.Op) || !UniqueLeaderTrickEligible(use, frame.OwnerSeat, effect.Op))
            throw new InvalidOperationException("A unique-leader trick addition lost its exact native use, frozen qualification, observer or instruction.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat &&
                e.SkillId == frame.SkillId && e.BindingId == binding && e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) != 1)
            throw new InvalidOperationException("A unique-leader trick addition requires its immediate typed designation parent.");
        return use;
    }

    private IReadOnlyList<PromptChoice> BuildUniqueLeaderTrickTargetChoices(ProgramSkillFrame frame, CardUseFrame use, SkillProgramEffectOp operation)
    {
        Dictionary<string, string> Parameters() => new()
        {
            ["program-action"] = "unique-leader-trick-target", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
            ["card-use-id"] = use.Id.ToString(CultureInfo.InvariantCulture), ["action-id"] = use.Action!.ActionId.ToString(CultureInfo.InvariantCulture)
        };
        var choices = UniqueLeaderTrickTargetCandidates(use, frame.OwnerSeat, operation).Select(pair => new PromptChoice(
            new($"unique-leader-trick-target.{frame.Id}.{string.Join(".", pair)}"), pair.Count == 2
                ? $"额外令 {_players[pair[0]].Name} 对 {_players[pair[1]].Name} 使用杀" : $"额外指定 {_players[pair[0]].Name}", [], pair, Parameters())).ToList();
        choices.Add(new(new($"unique-leader-trick-target.{frame.Id}.decline"), "不追加目标", [], [], Parameters()));
        return Array.AsReadOnly(choices.Select(DesignatedExtraTargetDraft.FreezeChoice).ToArray());
    }

    private SkillProgramStepOutcome OfferUniqueLeaderTrickTarget(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var use = RequireUniqueLeaderTrickParent(frame, out _);
        if (frame.UniqueLeaderTrickTargetDraft is not null || CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetOfferedEvent>().Any(e =>
                e.CardUseFrameId == use.Id && e.Source.OwnerSeat == frame.OwnerSeat && e.Source.SkillId == frame.SkillId))
            throw new InvalidOperationException("A unique-leader trick target instruction cannot issue its prompt twice.");
        if (UniqueLeaderTrickTargetCandidates(use, frame.OwnerSeat, operation).Count == 0) return SkillProgramStepOutcome.Continue;
        if (use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0)
        {
            var targets = Array.AsReadOnly(new[] { use.Action!.ActorSeat });
            var action = CloneDesignatedExtraTargetAction(use.Action, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
            use = RequireUniqueLeaderTrickParent(frame, out _);
        }
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
        var choices = BuildUniqueLeaderTrickTargetChoices(frame, use, operation);
        var draft = new UniqueLeaderTrickTargetDraft(use.Id, use.Action!.ActionId, 1, source, frame.GameplayHash, operation)
            { OriginalTargetSeats = use.TargetSeats, Choices = choices };
        ReplaceRuntimeTop(frame = frame with { UniqueLeaderTrickTargetDraft = draft });
        AdvanceEventRulesAndQueueFact(new UniqueLeaderTrickTargetOfferedEvent(frame.Id, use.Id, draft.ActionId, 1, source, frame.GameplayHash,
            operation, draft.OriginalTargetSeats, Array.AsReadOnly(choices.SelectMany(c => c.Targets.Take(1)).Distinct().ToArray())));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "可以为此锦囊牌追加一个合法目标。", [],
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = frame.OwnerSeat, Choices = draft.Choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState(); return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveUniqueLeaderTrickTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A trick target choice lost its owning program.");
        AssertUniqueLeaderTrickTargetDraft(frame);
        var draft = frame.UniqueLeaderTrickTargetDraft ?? throw new InvalidOperationException("The trick target draft is missing.");
        var choice = draft.Choices.SingleOrDefault(c => c.Id == selected.Id);
        if (choice is null || !AssistedChoicesEqual([choice], [selected])) throw new InvalidOperationException("The trick target answer changed its published choice.");
        var use = RequireUniqueLeaderTrickParent(frame, out _);
        var targets = draft.OriginalTargetSeats;
        if (choice.Targets.Count > 0)
        {
            if (!UniqueLeaderTrickTargetCandidates(use, frame.OwnerSeat, draft.Operation).Any(pair => pair.SequenceEqual(choice.Targets)))
                throw new InvalidOperationException("The extra trick target is no longer legal.");
            targets = Array.AsReadOnly(targets.Concat(choice.Targets).ToArray());
            var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, TargetsAdjusted = true,
                Enhancements = use.Enhancements | CurrentCardEnhancement.ExtraTarget }, action);
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, use.Id, choice.Targets[0]));
        }
        AdvanceEventRulesAndQueueFact(new UniqueLeaderTrickTargetResolvedEvent(frame.Id, use.Id, draft.ActionId, 1, draft.Source,
            frame.GameplayHash, draft.Operation, draft.OriginalTargetSeats, choice.Targets, targets));
        if (choice.Targets.Count > 0 && draft.Operation == SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage)
        {
            var benefit = new JoinedTrickDamageBenefit(frame.Id, use.Id, draft.ActionId, draft.Source, frame.GameplayHash);
            var current = LifecycleCardUse(use.Id)!;
            if (current.JoinedTrickDamageBenefits?.Any(b => b.Source.OwnerSeat == frame.OwnerSeat && b.Source.SkillId == frame.SkillId) == true)
                throw new InvalidOperationException("A joined trick cannot duplicate one owner's benefit.");
            UpdateLifecycleCardUse(use.Id, native => native with { JoinedTrickDamageBenefits = (current.JoinedTrickDamageBenefits ?? []).Append(benefit).ToArray() });
            AdvanceEventRulesAndQueueFact(new JoinedTrickDamageBenefitIssuedEvent(benefit));
        }
        ClearPendingDecision(); ReplaceRuntimeTop(frame with { UniqueLeaderTrickTargetDraft = null }); AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertUniqueLeaderTrickTargetDraft(ProgramSkillFrame frame)
    {
        if (frame.UniqueLeaderTrickTargetDraft is null && (frame.TriggerId is null ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == frame.TriggerId)?.Effects
                .Any(e => UniqueLeaderTrickTargetContract.IsTargetOperation(e.Op)) != true)) return;
        var offered = CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetOfferedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        var resolved = CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetResolvedEvent>().Any(e => e.ProgramFrameId == frame.Id);
        if (frame.UniqueLeaderTrickTargetDraft is not { } draft)
        {
            if (offered.Length != 0 && !resolved) throw new InvalidOperationException("An offered unique-leader trick cannot lose its owning draft.");
            return;
        }
        var use = RequireUniqueLeaderTrickParent(frame, out _);
        if (draft.CardUseFrameId != use.Id || draft.ActionId != use.Action!.ActionId || draft.InstructionIndex != 1 ||
            draft.Source != new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId) || draft.GameplayHash != frame.GameplayHash ||
            !draft.OriginalTargetSeats.SequenceEqual(use.TargetSeats) || offered.Length != 1 || resolved ||
            offered[0].CardUseFrameId != use.Id || offered[0].ActionId != draft.ActionId || offered[0].InstructionIndex != 1 ||
            offered[0].Source != draft.Source || offered[0].GameplayHash != draft.GameplayHash || offered[0].Operation != draft.Operation ||
            !offered[0].OriginalTargetSeats.SequenceEqual(draft.OriginalTargetSeats) ||
            !offered[0].CandidateTargetSeats.SequenceEqual(draft.Choices.SelectMany(c => c.Targets.Take(1)).Distinct()) ||
            !AssistedChoicesEqual(draft.Choices, BuildUniqueLeaderTrickTargetChoices(frame, use, draft.Operation)) ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } prompt ||
            prompt.PlayerSeat != frame.OwnerSeat || prompt.SourceSeat != frame.OwnerSeat || prompt.TargetSeat != frame.OwnerSeat ||
            prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.ValidCardIds.Count != 0 || !AssistedChoicesEqual(prompt.Choices, draft.Choices) ||
            !prompt.ValidTargetSeats.SequenceEqual(draft.Choices.SelectMany(c => c.Targets).Distinct()))
            throw new InvalidOperationException("A unique-leader trick lost its exact source, native prefix or published target choice.");
    }

    private PromptChoice SelectAiUniqueLeaderTrickTargetChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var use = RequireUniqueLeaderTrickParent(frame, out _);
        var draft = frame.UniqueLeaderTrickTargetDraft!;
        if (draft.Operation == SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage)
        {
            var beneficial = use.CardKind is CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains ||
                use.CardKind == CardKind.IronChain && _players[frame.OwnerSeat].IsChained;
            // A healthy joining player may trade one possible native damage for two cards.
            // Public HP alone is used; no foreign hand identities inform this choice.
            var affordable = IsCompletedUndamagedDamageUse(use.CardKind) && _players[frame.OwnerSeat].Hp > 1;
            return beneficial || affordable ? decision.Choices.First(c => c.Targets.Count > 0) : decision.Choices.Single(c => c.Targets.Count == 0);
        }
        bool Helpful(int target) => use.CardKind is CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains || use.CardKind == CardKind.IronChain && _players[target].IsChained;
        return decision.Choices.Where(c => c.Targets.Count > 0).FirstOrDefault(c => use.CardKind == CardKind.BorrowedSword
            ? !AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[c.Targets[1]])
            : Helpful(c.Targets[0]) == AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[c.Targets[0]])) ?? decision.Choices.Single(c => c.Targets.Count == 0);
    }

    private bool IsUniqueLeaderTrickTargetFact(CardUseFrame use, UniqueLeaderTrickTargetResolvedEvent fact, IReadOnlyList<int> originalTargets)
    {
        var normalizationOnly = use.CardKind == CardKind.DrawTwo && fact.AddedTargetSeats.Count == 0 &&
            originalTargets is [var self] && self == UniqueLeaderTrickQualification(use)?.ActorSeat;
        if (use.Action is not { Type: CardActionType.Use } action || !IsOrdinaryTrick(use.CardKind) || fact.CardUseFrameId != use.Id ||
            fact.ActionId != action.ActionId || fact.InstructionIndex != 1 || !IsValidPlayerSeat(fact.Source.OwnerSeat) ||
            !UniqueLeaderTrickTargetContract.IsTargetOperation(fact.Operation) || !fact.OriginalTargetSeats.SequenceEqual(originalTargets) ||
            !normalizationOnly && fact.AddedTargetSeats.Count != (use.CardKind == CardKind.BorrowedSword ? 2 : 1) || fact.AddedTargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
            !normalizationOnly && (use.CardKind == CardKind.BorrowedSword ? originalTargets.Where((_, i) => i % 2 == 0) : originalTargets).Contains(fact.AddedTargetSeats[0]) ||
            !fact.ResultTargetSeats.SequenceEqual(originalTargets.Concat(fact.AddedTargetSeats)) ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == fact.Source.BindingId) is not { } trigger || trigger.Effects is not [{ } effect] || effect.Op != fact.Operation)
            return false;
        var qualification = UniqueLeaderTrickQualification(use);
        if (qualification is null || (fact.Operation == SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition
                ? qualification.UniqueLargestHandSeat != qualification.ActorSeat
                : qualification.OriginalPrimaryTargetCount != 1 || qualification.OriginalSinglePrimaryTargetSeat == fact.Source.OwnerSeat ||
                  qualification.OriginalSinglePrimaryTargetSeat != qualification.UniqueLargestHpSeat || !normalizationOnly && fact.AddedTargetSeats[0] != fact.Source.OwnerSeat)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<UniqueLeaderTrickTargetResolvedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId) == 1 &&
            history.OfType<UniqueLeaderTrickTargetOfferedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id &&
                e.ActionId == fact.ActionId && e.Source == fact.Source && e.GameplayHash == fact.GameplayHash && e.Operation == fact.Operation &&
                e.InstructionIndex == 1 && e.OriginalTargetSeats.SequenceEqual(originalTargets) &&
                (normalizationOnly ? e.CandidateTargetSeats.Count > 0 : e.CandidateTargetSeats.Contains(fact.AddedTargetSeats[0]))) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == fact.Source.OwnerSeat &&
                e.SkillId == fact.Source.SkillId && e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1 &&
            (normalizationOnly ? !history.OfType<ProgramCardUseTargetAddedEvent>().Any(e => e.FrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id) :
             history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id &&
                e.OwnerSeat == fact.Source.OwnerSeat && e.SkillId == fact.Source.SkillId && e.TargetSeat == fact.AddedTargetSeats[0]) == 1);
    }

    private bool HasUniqueLeaderTrickTargetTail(CardUseFrame use) => CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetResolvedEvent>().Any(e =>
        e.AddedTargetSeats.Count > 0 && IsUniqueLeaderTrickTargetFact(use, e, e.OriginalTargetSeats) && use.Action!.TargetSeats.Count >= e.ResultTargetSeats.Count &&
        use.Action.TargetSeats.Take(e.ResultTargetSeats.Count).SequenceEqual(e.ResultTargetSeats) && use.TargetSeats.SequenceEqual(use.Action.TargetSeats));

    private sealed partial class ProgramSkillHost : IUniqueLeaderTrickTargetHost
    {
        public SkillProgramStepOutcome OfferUniqueLargestHandTrickTargetAddition(ProgramSkillFrame frame) =>
            engine.OfferUniqueLeaderTrickTarget(frame, SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition);
        public SkillProgramStepOutcome JoinUniqueLargestHpTrickTargetAndDrawAfterDamage(ProgramSkillFrame frame) =>
            engine.OfferUniqueLeaderTrickTarget(frame, SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage);
        public SkillProgramStepOutcome DrawAfterJoinedTrickDamage(ProgramSkillFrame frame) => engine.BeginJoinedTrickDamageReward(frame);
        public bool CanContinueIssuedJoinedTrickDamageReward(ProgramSkillFrame frame) => engine.CanContinueIssuedJoinedTrickDamageReward(frame);
    }
}
