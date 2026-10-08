using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardConversionSource DesignatedExtraTargetSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    private bool TryGetDesignatedExtraTargetUse(int owner, ProgramSkillWindowContext context,
        out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            !IsValidPlayerSeat(owner) || !_players[owner].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } foundWindow ||
            foundWindow.ParentFrameId != identity.ParentCardUseFrameId || foundWindow.CompletedResponseReturn is not null ||
            foundWindow.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard) ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } foundUse ||
            action.ActionId != identity.CardActionId || action.ActorSeat != owner || foundUse.SourceSeat != owner ||
            action.EffectiveKind != foundUse.CardKind || foundWindow.Action.ActionId != action.ActionId ||
            foundWindow.Action.ActorSeat != owner || !foundWindow.Action.TargetSeats.SequenceEqual(foundUse.TargetSeats) ||
            !action.TargetSeats.SequenceEqual(foundUse.TargetSeats) || foundUse.DyingResponse is not null ||
            action.ResponderSeat is not null || action.OpponentSeat is not null ||
            GetProgramCardCategory(foundUse.CardKind) != SkillProgramCardCategory.Basic && !IsOrdinaryTrick(foundUse.CardKind) ||
            foundUse.TargetSeats.Any(seat => !IsValidPlayerSeat(seat))) return false;
        var windowIndex = _resolutionStack.FindIndex(f => f.Id == foundWindow.Id);
        if (windowIndex < 1 || _resolutionStack[windowIndex - 1].Id != foundUse.Id) return false;
        if (action.PhysicalCards.Count > 0)
        {
            if (IsForeignPublicPileSlashUse(foundUse.Id))
            {
                // This native producer already paid two exact Authority entities
                // to Discard; its existing paid-virtual target loop owns the tail.
                if (foundUse.PhysicalCardIds is not { Count: 0 } || action.PhysicalCards.Any(c =>
                    _cardZones.GetLocation(c.CardId) != CardLocation.DiscardPile)) return false;
            }
            else if (!action.PhysicalCards.Select(c => c.CardId).SequenceEqual(foundUse.PhysicalCardIds ?? [foundUse.CardId]) ||
                action.PhysicalCards.Where(c => !IsCurrentUsePhysicalCardClaim(foundUse.Id, c.CardId) &&
                    !IsExchangedCardClaim(action.ActionId, c.CardId)).Any(c => IsProgramAlternativeCost(action, c.CardId)
                        ? _cardZones.GetLocation(c.CardId) == CardLocation.Processing
                        : _cardZones.GetLocation(c.CardId) != CardLocation.Processing)) return false;
        }
        else if (foundUse.CardId != 0 || foundUse.PhysicalCardIds is not { Count: 0 } ||
            !IsTieredRoundZeroUse(foundUse.Id) && !IsProgramVirtualOrdinaryTrickUse(foundUse.Id) &&
            foundUse.VirtualBasicReturn is null && foundUse.CardAttack?.ProgramSkillCardUseFrameId is null &&
            foundUse.AdjustedSlashReturn is null && !IsIssuedZeroEntityDuel(foundUse.Id)) return false;
        if (foundUse.CardKind == CardKind.BorrowedSword &&
            (foundUse.TargetSeats.Count < 2 || foundUse.TargetSeats.Count % 2 != 0 ||
             foundUse.TargetSeats.Where((_, i) => i % 2 == 0).Distinct().Count() != foundUse.TargetSeats.Count / 2)) return false;
        if (foundUse.TargetSeats.Count == 0 && foundUse.CardKind != CardKind.DrawTwo) return false;
        use = foundUse; window = foundWindow; return true;
    }

    private bool DesignatedExtraTargetCandidateMatches(ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context, ProgramCardTriggerWindowFrame window)
    {
        if (window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var current = window.Candidates[window.CandidateIndex];
        return current.OwnerSeat == candidate.OwnerSeat && current.SkillId == candidate.SkillId &&
            current.TriggerId == candidate.BindingId && current.SkillInstanceId == candidate.SkillInstanceId &&
            current.GameplayHash == candidate.GameplayHash && CreateCardActionProgramContext(window, current) == context;
    }

    private bool HasDiscardedDesignatedCardName(CardKind kind) => _cardZones.CardsAt(CardLocation.DiscardPile)
        .Any(card => ProgramBasicCardName(card.Kind) == ProgramBasicCardName(kind));

    private bool WasDesignatedExtraTargetOffered(CardUseFrame use, CardConversionSource source) =>
        CompleteProgramEventHistory().OfType<DesignatedExtraTargetOfferedEvent>().Any(e =>
            e.CardUseFrameId == use.Id && e.ActionId == use.Action!.ActionId && e.Source == source);

    private bool CanOfferDesignatedExtraTarget(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget)) return true;
        return TryGetDesignatedExtraTargetUse(candidate.OwnerSeat, context, out var use, out var window) &&
            DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
            !HasDiscardedDesignatedCardName(use.CardKind) &&
            !WasDesignatedExtraTargetOffered(use, new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId)) &&
            DesignatedExtraTargetCandidates(use).Count > 0;
    }

    private CardUseFrame RequireDesignatedExtraTargetParent(ProgramSkillFrame frame,
        out ProgramCardTriggerWindowFrame window)
    {
        if (frame.TriggerId is not { } binding || frame.WindowContext is not { } context ||
            !TryGetDesignatedExtraTargetUse(frame.OwnerSeat, context, out var use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(frame.OwnerSeat, frame.SkillId, binding,
                frame.SkillInstanceId, frame.GameplayHash, 0), context, window) ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            frame.InstructionIndex != 1 || ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(1).Effect.Op !=
                SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget)
            throw new InvalidOperationException("A designated extra target lost its exact actor, candidate, instruction or owning use.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("A designated extra target requires its immediate typed designation parent.");
        return use;
    }

    private bool CanBeDistanceFreeDesignatedTarget(CardUseFrame use, CharacterState target)
    {
        var action = use.Action!; var actor = _players[action.ActorSeat]; var kind = use.CardKind;
        var originalMainTargets = kind == CardKind.BorrowedSword
            ? use.TargetSeats.Where((_, i) => i % 2 == 0) : action.EffectiveDesignatedTargetSeats;
        if (!target.IsAlive || originalMainTargets.Contains(target.Seat) ||
            kind == CardKind.DrawTwo && use.TargetSeats.Count == 0 && target.Seat == actor.Seat ||
            IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) ||
            IsCardTargetProhibited(target, kind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) ||
            HasBeneficiarySuitShield(actor.Seat, target.Seat, action.EffectiveSuit) ||
            IsSelfTargetForbiddenAction(actor, kind, [target.Seat]) ||
            target.Seat == actor.Seat && action.ConversionChain.Any(source => ViewAsRule(source)?.ExcludeOwnerEffects == true)) return false;
        if (IsSlashCard(kind))
            return CanUseSlashTarget(actor, target, new(-1, kind, action.EffectiveSuit ?? Suit.None, action.EffectiveRank ?? 0),
                action.ConversionChain.FirstOrDefault(), kind, ignoreDistance: true, existingUseFrameId: use.Id,
                specificEffectiveRank: action.EffectiveRank, physicalCardIds: action.PhysicalCards.Select(c => c.CardId).ToArray());
        return kind switch
        {
            CardKind.Peach => target.Hp < target.MaxHp,
            CardKind.Alcohol or CardKind.DrawTwo or CardKind.IronChain => true,
            CardKind.Duel => target.Seat != actor.Seat,
            CardKind.FireAttack => GetHand(target).Count > 0,
            CardKind.Dismantlement or CardKind.Snatch => target.Seat != actor.Seat && HasTargetCard(target),
            CardKind.BorrowedSword => target.Seat != actor.Seat && GetWeapon(target) is not null &&
                _players.Any(victim => IsLegalBorrowedSwordSlashTarget(target, victim)),
            CardKind.BarbarianAssault or CardKind.ArrowBarrage => target.Seat != actor.Seat &&
                !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind),
            CardKind.PeachGarden or CardKind.FiveGrains => !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind),
            _ => false
        };
    }

    private IReadOnlyList<IReadOnlyList<int>> DesignatedExtraTargetCandidates(CardUseFrame use)
    {
        var candidates = new List<IReadOnlyList<int>>();
        foreach (var target in _players.Where(p => CanBeDistanceFreeDesignatedTarget(use, p)).OrderBy(p => p.Seat))
            if (use.CardKind == CardKind.BorrowedSword)
                foreach (var victim in _players.Where(v => IsLegalBorrowedSwordSlashTarget(target, v)).OrderBy(v => v.Seat))
                    candidates.Add(Array.AsReadOnly(new[] { target.Seat, victim.Seat }));
            else candidates.Add(Array.AsReadOnly(new[] { target.Seat }));
        return Array.AsReadOnly(candidates.ToArray());
    }

    private IReadOnlyList<PromptChoice> BuildDesignatedExtraTargetChoices(ProgramSkillFrame frame, CardUseFrame use)
    {
        Dictionary<string, string> Parameters() => new()
        {
            ["program-action"] = "designated-extra-target", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
            ["card-use-id"] = use.Id.ToString(CultureInfo.InvariantCulture), ["action-id"] = use.Action!.ActionId.ToString(CultureInfo.InvariantCulture)
        };
        var choices = DesignatedExtraTargetCandidates(use).Select(pair => new PromptChoice(
            new($"designated-extra-target.{frame.Id}.{string.Join(".", pair)}"), pair.Count == 2
                ? $"额外令 {_players[pair[0]].Name} 对 {_players[pair[1]].Name} 使用杀"
                : $"额外指定 {_players[pair[0]].Name}", [], pair, Parameters())).ToList();
        choices.Add(new(new($"designated-extra-target.{frame.Id}.decline"), "不追加目标", [], [], Parameters()));
        return Array.AsReadOnly(choices.Select(DesignatedExtraTargetDraft.FreezeChoice).ToArray());
    }

    private SkillProgramStepOutcome AddOneDistanceFreeCurrentUseTarget(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var use = RequireDesignatedExtraTargetParent(frame, out _);
        if (frame.DesignatedExtraTargetDraft is not null)
            throw new InvalidOperationException("A designated extra target instruction was offered twice.");
        var source = DesignatedExtraTargetSource(frame);
        if (WasDesignatedExtraTargetOffered(use, source))
            throw new InvalidOperationException("A designated extra target lost its already issued choice receipt.");
        if (HasDiscardedDesignatedCardName(use.CardKind) || DesignatedExtraTargetCandidates(use).Count == 0)
            return SkillProgramStepOutcome.Continue;
        // DrawTwo's original self target is implicit in the ordinary host input.
        // Normalize only this new opt-in producer, preserving its action identity.
        if (use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0)
        {
            var targets = Array.AsReadOnly(new[] { use.SourceSeat }); var previous = use.Action!;
            var action = CloneDesignatedExtraTargetAction(previous, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
            use = RequireDesignatedExtraTargetParent(frame, out _);
        }
        var choices = BuildDesignatedExtraTargetChoices(frame, use);
        var draft = new DesignatedExtraTargetDraft(use.Id, use.Action!.ActionId, frame.InstructionIndex, source, frame.GameplayHash)
            { OriginalTargetSeats = use.TargetSeats, Choices = choices };
        ReplaceRuntimeTop(frame = frame with { DesignatedExtraTargetDraft = draft });
        AdvanceEventRulesAndQueueFact(new DesignatedExtraTargetOfferedEvent(frame.Id, use.Id, draft.ActionId, frame.InstructionIndex,
            source, frame.GameplayHash, draft.OriginalTargetSeats,
            Array.AsReadOnly(choices.SelectMany(c => c.Targets.Take(1)).Distinct().ToArray())));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "可为此牌额外指定一个目标，无距离限制。", [],
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = frame.OwnerSeat, Choices = draft.Choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private CardActionContext CloneDesignatedExtraTargetAction(CardActionContext action, IReadOnlyList<int> targets) =>
        new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, targets,
            action.PhysicalCards, action.ConversionChain,
            action.EffectiveKind == CardKind.BorrowedSword ? targets.Where((_, i) => i % 2 == 0).ToArray() : targets,
            action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);

    private void ResolveDesignatedExtraTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("A designated extra target lost its owning program.");
        var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        AssertDesignatedExtraTargetDraft(frame, paused);
        var draft = frame.DesignatedExtraTargetDraft!;
        var choice = draft.Choices.SingleOrDefault(c => c.Id == selected.Id);
        if (choice is null || !AssistedChoicesEqual([choice], [selected]))
            throw new InvalidOperationException("A designated extra target answer changed its frozen public choice.");
        var use = RequireDesignatedExtraTargetParent(frame, out var window);
        var targets = draft.OriginalTargetSeats;
        if (choice.Targets.Count > 0)
        {
            targets = Array.AsReadOnly(draft.OriginalTargetSeats.Concat(choice.Targets).ToArray());
            var previous = use.Action!; var action = CloneDesignatedExtraTargetAction(previous, targets);
            var simple = use.AdjustedSimpleContinuation;
            if (window.Continuation == ProgramCardContinuation.FinalizedSimpleCard)
            {
                var continuation = window.SimpleContinuation ?? throw new InvalidOperationException("A basic extra target lost its native simple continuation.");
                if (continuation.Effect is not (SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol))
                    throw new InvalidOperationException("A basic extra target requires its real recovery or Alcohol effect.");
                if (simple is not null && (simple.CardId != continuation.CardId || simple.Effect != continuation.Effect ||
                    simple.RecoveryAmount != continuation.RecoveryAmount || !(simple.RecoveryPolicySources ?? []).SequenceEqual(continuation.RecoveryPolicySources ?? [])))
                    throw new InvalidOperationException("A basic extra target cannot replace an existing native continuation.");
                simple ??= continuation with { RecoveryPolicySources = continuation.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null };
            }
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, AdjustedSimpleContinuation = simple,
                TargetsAdjusted = true, Enhancements = use.Enhancements | CurrentCardEnhancement.ExtraTarget }, action);
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, use.Id, choice.Targets[0]));
        }
        AdvanceEventRulesAndQueueFact(new DesignatedExtraTargetResolvedEvent(frame.Id, use.Id, draft.ActionId, frame.InstructionIndex,
            draft.Source, frame.GameplayHash, draft.OriginalTargetSeats, choice.Targets, targets));
        ClearPendingDecision(); ReplaceRuntimeTop(frame with { DesignatedExtraTargetDraft = null }); AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertDesignatedExtraTargetDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        var offered = CompleteProgramEventHistory().OfType<DesignatedExtraTargetOfferedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        var resolved = CompleteProgramEventHistory().OfType<DesignatedExtraTargetResolvedEvent>().Any(e => e.ProgramFrameId == frame.Id);
        if (frame.DesignatedExtraTargetDraft is not { } draft)
        {
            if (offered.Length > 0 && !resolved)
                throw new InvalidOperationException("A designated extra target lost its already issued choice receipt.");
            return;
        }
        var use = RequireDesignatedExtraTargetParent(frame, out _);
        if (paused.Op != SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget || draft.CardUseFrameId != use.Id ||
            draft.ActionId != use.Action!.ActionId || draft.InstructionIndex != frame.InstructionIndex ||
            draft.Source != DesignatedExtraTargetSource(frame) || draft.GameplayHash != frame.GameplayHash ||
            !draft.OriginalTargetSeats.SequenceEqual(use.TargetSeats) || offered.Length != 1 || resolved ||
            offered[0].CardUseFrameId != use.Id || offered[0].ActionId != draft.ActionId || offered[0].Source != draft.Source ||
            offered[0].GameplayHash != draft.GameplayHash || offered[0].InstructionIndex != draft.InstructionIndex ||
            !offered[0].OriginalTargetSeats.SequenceEqual(draft.OriginalTargetSeats) ||
            !offered[0].CandidateTargetSeats.SequenceEqual(draft.Choices.SelectMany(c => c.Targets.Take(1)).Distinct()) ||
            HasDiscardedDesignatedCardName(use.CardKind) || !AssistedChoicesEqual(draft.Choices, BuildDesignatedExtraTargetChoices(frame, use)) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            prompt.SourceSeat != frame.OwnerSeat || prompt.TargetSeat != frame.OwnerSeat || prompt.SkillPrompt?.SkillId != frame.SkillId ||
            prompt.ValidCardIds.Count != 0 || !AssistedChoicesEqual(prompt.Choices, draft.Choices) ||
            !prompt.ValidTargetSeats.SequenceEqual(draft.Choices.SelectMany(c => c.Targets).Distinct()))
            throw new InvalidOperationException("A designated extra target lost its exact source, prefix, native parent or public prompt.");
    }

    private PromptChoice SelectAiDesignatedExtraTargetChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var use = RequireDesignatedExtraTargetParent(frame, out _);
        bool Helpful(int target) => use.CardKind is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains ||
            use.CardKind == CardKind.IronChain && _players[target].IsChained;
        return decision.Choices.Where(c => c.Targets.Count > 0).FirstOrDefault(c => use.CardKind == CardKind.BorrowedSword
            ? !AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[c.Targets[1]])
            : Helpful(c.Targets[0]) == AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[c.Targets[0]]))
            ?? decision.Choices.Single(c => c.Targets.Count == 0);
    }

    private bool IsDesignatedExtraTargetFact(CardUseFrame use, DesignatedExtraTargetResolvedEvent fact,
        IReadOnlyList<int> originalTargets)
    {
        if (use.Action is not { Type: CardActionType.Use } action || fact.CardUseFrameId != use.Id || fact.ActionId != action.ActionId ||
            fact.Source.OwnerSeat != action.ActorSeat || fact.InstructionIndex != 1 ||
            !fact.OriginalTargetSeats.SequenceEqual(originalTargets) || fact.OriginalTargetSeats.Count == 0 ||
            fact.AddedTargetSeats.Count != (use.CardKind == CardKind.BorrowedSword ? 2 : 1) ||
            fact.AddedTargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
            (use.CardKind == CardKind.BorrowedSword ? originalTargets.Where((_, i) => i % 2 == 0) : originalTargets).Contains(fact.AddedTargetSeats[0]) ||
            !fact.ResultTargetSeats.SequenceEqual(originalTargets.Concat(fact.AddedTargetSeats)) ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == fact.Source.BindingId) is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized } trigger ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget }]) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<DesignatedExtraTargetResolvedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId) == 1 &&
            history.OfType<DesignatedExtraTargetOfferedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId &&
                e.CardUseFrameId == use.Id && e.ActionId == fact.ActionId && e.InstructionIndex == fact.InstructionIndex &&
                e.Source == fact.Source && e.GameplayHash == fact.GameplayHash && e.OriginalTargetSeats.SequenceEqual(originalTargets) &&
                e.CandidateTargetSeats.Contains(fact.AddedTargetSeats[0])) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == action.ActorSeat &&
                e.SkillId == fact.Source.SkillId && e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.Window == trigger.Window) == 1 &&
            history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id &&
                e.OwnerSeat == action.ActorSeat && e.SkillId == fact.Source.SkillId && e.TargetSeat == fact.AddedTargetSeats[0]) == 1;
    }

    private bool HasDesignatedExtraTargetTail(CardUseFrame use) =>
        CompleteProgramEventHistory().OfType<DesignatedExtraTargetResolvedEvent>().Any(e =>
            IsDesignatedExtraTargetFact(use, e, e.OriginalTargetSeats) && use.Action!.TargetSeats.Count >= e.ResultTargetSeats.Count &&
            use.Action.TargetSeats.Take(e.ResultTargetSeats.Count).SequenceEqual(e.ResultTargetSeats) &&
            use.TargetSeats.Count == use.Action.TargetSeats.Count);

    private void RecordDesignatedExtraTargetRedirect(ProgramSkillFrame redirector, long useId, int originalSeat, int newSeat)
    {
        if (LifecycleCardUse(useId) is not { Action: { } action } use || !IsSlashCard(use.CardKind) || !(HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use))) return;
        var source = new CardConversionSource(redirector.SkillId, GetProgramBindingId(redirector), redirector.OwnerSeat, redirector.SkillInstanceId);
        var program = _contentRegistry.GetSkill(redirector.SkillId).Program!;
        var effect = ProgramInstructionResolver.Default.Resolve(redirector, program).GetPausedInstruction(redirector.InstructionIndex).Effect;
        if (redirector.GameplayHash != program.GameplayHash || effect.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
            newSeat != ResolveProgramEffectTarget(redirector, effect.Target) ||
            redirector.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } windowContext ||
            context.ParentCardUseFrameId != useId || context.CardActionId != action.ActionId || context.ActorSeat != use.SourceSeat ||
            windowContext.SourceSeat != use.SourceSeat || windowContext.TargetSeat != originalSeat || redirector.OwnerSeat != originalSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == windowContext.ParentFrameId) is not { } window ||
            window.ParentFrameId != useId || window.Action.ActionId != action.ActionId || window.Continuation != ProgramCardContinuation.SlashTargetRedirecting ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(redirector, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != newSeat ||
            CompleteProgramEventHistory().OfType<DesignatedExtraTargetRedirectedEvent>().Any(e => e.ProgramFrameId == redirector.Id &&
                e.CardUseFrameId == useId && e.InstructionIndex == redirector.InstructionIndex))
            throw new InvalidOperationException("A designated extra target redirect lost its exact native producer and target cursor.");
        AdvanceEventRulesAndQueueFact(new DesignatedExtraTargetRedirectedEvent(redirector.Id, useId, action.ActionId, source,
            redirector.GameplayHash, redirector.InstructionIndex, use.TargetIndex, originalSeat, newSeat));
    }

    private bool IsDesignatedExtraTargetRedirectFact(CardUseFrame use, DesignatedExtraTargetRedirectedEvent fact,
        IReadOnlyList<int> actualTargets)
    {
        if (!(HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use)) || fact.CardUseFrameId != use.Id || fact.ActionId != use.Action?.ActionId ||
            fact.Source.OwnerSeat != fact.OriginalTargetSeat || fact.TargetIndex < 0 || fact.TargetIndex >= actualTargets.Count ||
            actualTargets[fact.TargetIndex] != fact.OriginalTargetSeat || !IsValidPlayerSeat(fact.NewTargetSeat) ||
            fact.NewTargetSeat == use.SourceSeat || fact.NewTargetSeat == fact.OriginalTargetSeat ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            ProgramInstructionResolver.Default.Find(program, ProgramInstructionSourceKind.Trigger, fact.Source.BindingId) is not { } plan ||
            plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || fact.InstructionIndex < 1 ||
            fact.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(fact.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<DesignatedExtraTargetRedirectedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId &&
            e.CardUseFrameId == use.Id && e.InstructionIndex == fact.InstructionIndex) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == fact.Source.OwnerSeat &&
                e.SkillId == fact.Source.SkillId && e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) == 1;
    }

    private bool TryGetDesignatedExtraTargetVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent,
        out IReadOnlyList<int> targets)
    {
        targets = [];
        if (!IsSlashCard(use.CardKind) || !(HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use)) || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || use.CardAttack is not { CardId: null, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat || action.PhysicalCards.Count != 0 ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 || parent.InstructionIndex < 1 ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != parent.Id) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var declared = history.OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
        var declaredUse = history.OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
        var accepted = history.OfType<CardActionAcceptedEvent>().Select(e => e.Action).FirstOrDefault(a => a.ActionId == action.ActionId);
        if (declared is not { TargetSeats.Count: 1 } || declaredUse is not { CardId: 0 } || declaredUse.SourceSeat != use.SourceSeat ||
            declaredUse.CardKind != use.CardKind || accepted is not { Type: CardActionType.Use, TargetSeats.Count: 1, PhysicalCards.Count: 0 } ||
            accepted.ActorSeat != action.ActorSeat || accepted.ProviderSeat != action.ProviderSeat || accepted.EffectiveKind != use.CardKind ||
            !accepted.ConversionChain.SequenceEqual(action.ConversionChain)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(parent, program).GetPausedInstruction(parent.InstructionIndex).Effect;
        var issuer = new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId);
        var exactProducer = effect.Op switch
        {
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == use.SourceSeat && action.ConversionChain.SequenceEqual([issuer]) &&
                (effect.TargetReference is null ? parent.SelectedTargetSeats is [var selected] && selected == declared.TargetSeats[0]
                    : effect.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == declared.TargetSeats[0]),
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats is [var actor] && actor == use.SourceSeat && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash => parent.OwnerSeat == use.SourceSeat &&
                parent.SelectedTargetSeats is [var selected] && selected == declared.TargetSeats[0] && action.ConversionChain.Count == 0 &&
                effect.UseCardActionWindows && effect.OutputKind == CardKind.Slash && effect.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
            _ => false
        };
        if (!exactProducer) return false;
        targets = Array.AsReadOnly(new[] { use.TargetSeats[0] }); return true;
    }

    // This producer's zero-material Peach/Alcohol return still belongs to its
    // original program. Only its real card-use target cursor is extended.
    private bool ContinueDesignatedVirtualBasicEffect(long id, ProgramSimpleCardContinuation continuation)
    {
        var use = LifecycleCardUse(id);
        if (use is not { VirtualBasicReturn: not null, AdjustedSimpleContinuation: not null } || !(HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use))) return false;
        if (continuation.CardId != 0 || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.CardKind is not (CardKind.Peach or CardKind.Alcohol) || use.VirtualBasicEffectApplied == true ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count ||
            use.AdjustedSimpleContinuation.Effect != continuation.Effect)
            throw new InvalidOperationException("A designated virtual basic effect lost its real zero-material continuation.");
        var actor = _players[use.SourceSeat]; var target = _players[use.TargetSeats[use.TargetIndex]];
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (use.CardKind == CardKind.Alcohol && actor.IsAlive)
            actor.UsedPlayPhaseAlcoholThisTurn = true;
        if (actor.IsAlive && target.IsAlive && _winner == Winner.None && !IsCardEffectIneffective(id, target.Seat))
        {
            if (use.CardKind == CardKind.Peach)
            {
                if (!TryQueueRecoveryReplacement(id, actor.Seat, target.Seat, continuation.RecoveryAmount, new(RecoveryAttemptProducer.VirtualBasic)))
                {
                    var recovery = BeginRecovery(id, actor.Seat, target.Seat, continuation.RecoveryAmount);
                    try
                    {
                        target.Hp = Math.Min(target.MaxHp, target.Hp + continuation.RecoveryAmount);
                        AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(actor.Seat, target.Seat, continuation.RecoveryAmount, target.Hp));
                    }
                    finally { PopResolutionFrame(recovery, ResolutionFrameKind.Recovery); }
                }
            }
            else
            {
                target.HasAlcoholEffect = true;
                AdvanceEventRulesAndQueueFact(new AlcoholAppliedEvent(id, target.Seat, 1));
            }
        }
        UpdateLifecycleCardUse(id, frame => frame with { VirtualBasicEffectApplied = true });
        FinishVirtualBasicUse(id);
        return true;
    }

    private bool TryContinueDesignatedVirtualBasicUse(long id)
    {
        var use = LifecycleCardUse(id);
        if (use is not { VirtualBasicReturn: not null, AdjustedSimpleContinuation: { } continuation } ||
            !(HasDesignatedExtraTargetTail(use) || HasRecipientCategorySlashTargetTail(use))) return false;
        var next = use.TargetIndex + 1;
        while (next < use.TargetSeats.Count && !_players[use.TargetSeats[next]].IsAlive) next++;
        if (_winner != Winner.None || next >= use.TargetSeats.Count)
        {
            UpdateLifecycleCardUse(id, frame => frame with { AdjustedSimpleContinuation = null });
            return false;
        }
        UpdateLifecycleCardUse(id, frame => frame with { TargetIndex = next, VirtualBasicEffectApplied = false });
        ContinueSimpleCardUse(id, continuation);
        return true;
    }

    private sealed partial class ProgramSkillHost : IDesignatedExtraTargetProgramHost
    {
        public SkillProgramStepOutcome AddOneDistanceFreeCurrentUseTarget(ProgramSkillFrame frame) =>
            engine.AddOneDistanceFreeCurrentUseTarget(frame);
    }
}
