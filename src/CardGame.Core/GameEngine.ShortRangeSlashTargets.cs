using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasShortRangeSlashCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferShortRangeSlashTarget);
    private int FreezeShortRangeSlashResponseRequirement(long attackId, int source, int target, CardKind kind, int ordinary)
    {
        var state = GetCardAttackState(attackId);
        if (state.ShortRangeResponseRequirement is { } issued && issued.ActorSeat == source && issued.TargetSeat == target)
            return issued.RequiredDodgeResponses;
        if (!IsSlashCard(kind) || !CardPolicies(_players[source], SkillProgramCardPolicyKind.MinimumSlashResponseAtDistanceOne,
            kind, CardKind.Dodge).Any()) return ordinary;
        // Freeze at this native target's first response window, after final targets and redirection.
        UpdateCardAttackState(attackId, current => current! with { ShortRangeResponseRequirement = new(source, target, ordinary) });
        return ordinary;
    }
    private CardConversionSource ShortRangeSlashSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private CardUseFrame? ShortRangeSlashUse(int owner, ProgramSkillWindowContext context)
    {
        if (context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && context.CardUse is { } identity &&
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } window &&
            window.ParentFrameId == identity.ParentCardUseFrameId && GetCardActionWindow(window) == context.Window &&
            LifecycleCardUse(identity.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
            action.ActionId == identity.CardActionId && window.Action.ActionId == action.ActionId &&
            action.ActorSeat == owner && use.SourceSeat == owner && action.EffectiveKind == use.CardKind && IsSlashCard(use.CardKind)) return use;
        if (context.Window == SkillProgramTriggerWindow.OtherActualUseTargeted && context.ActualUseTarget is { ActionId: null } legacy &&
            legacy.ActorSeat == owner && MatchesActualUseTarget(legacy) &&
            _resolutionStack.OfType<ActualUseTargetWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } parent &&
            parent.ParentFrameId == legacy.CardUseFrameId && LifecycleCardUse(legacy.CardUseFrameId) is { Action: null } old &&
            old.SourceSeat == owner && IsSlashCard(old.CardKind)) return old;
        return null;
    }
    private bool ShortRangeSlashWasOffered(long useId) => CompleteProgramEventHistory().OfType<ShortRangeSlashTargetResolvedEvent>()
        .Any(e => e.CardUseFrameId == useId);
    private bool CanRunShortRangeSlash(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferShortRangeSlashTarget)) return true;
        return ShortRangeSlashUse(candidate.OwnerSeat, context) is { } use && !ShortRangeSlashWasOffered(use.Id) &&
            ShortRangeSlashTargets(use).Length > 0;
    }
    private int[] ShortRangeSlashTargets(CardUseFrame use)
    {
        if (_winner != Winner.None || !_players[use.SourceSeat].IsAlive || !IsSlashCard(use.CardKind)) return [];
        var actor = _players[use.SourceSeat];
        return _players.Where(target => target.IsAlive && target.Seat != actor.Seat && !use.TargetSeats.Contains(target.Seat) &&
            GetCombatDistance(actor.Seat, target.Seat) == 1 && (use.Action is not null
                ? CanBeProgramCardUseRoleTarget(use, target)
                : !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, use.CardKind) &&
                    !IsCardTargetProhibited(target, use.CardKind, Suit.None, null) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) &&
                    !IsSlashProhibited(target) && IsWithinSpecificSlashRange(actor, target, use.CardKind, null, use.Id)))
            .OrderBy(target => (target.Seat - actor.Seat + _playerCount) % _playerCount).Select(target => target.Seat).ToArray();
    }
    private SkillProgramStepOutcome OfferShortRangeSlashTarget(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var use = frame.WindowContext is { } context ? ShortRangeSlashUse(frame.OwnerSeat, context) : null;
        if (use is null || frame.InstructionIndex != 1 || GetProgramTrigger(frame).Effects is not [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }])
            throw new InvalidOperationException("A short-range target offer requires its exact final-use instruction.");
        if (ShortRangeSlashWasOffered(use.Id) || ShortRangeSlashTargets(use).Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { ShortRangeSlashTargetDraft = new(use.Id, use.Action?.ActionId, frame.InstructionIndex) });
        PublishShortRangeSlashTarget(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShortRangeSlashChoices(ProgramSkillFrame frame)
    {
        var use = ShortRangeSlashUse(frame.OwnerSeat, frame.WindowContext!) ?? throw new InvalidOperationException("The short-range choice lost its true owning use.");
        Dictionary<string, string> Parameters() => new() { ["program-action"] = "short-range-slash-target", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) };
        var choices = ShortRangeSlashTargets(use).Select(target => new PromptChoice(new($"short-range.{frame.Id}.{target}"),
            $"额外指定距离为1的 {_players[target].Name}", [], [target], Parameters())).ToList();
        choices.Add(new(new($"short-range.{frame.Id}.decline"), "不追加目标", [], [], Parameters()));
        return Array.AsReadOnly(choices.Select(FreezeDirectedDistanceChoice).ToArray());
    }
    private void PublishShortRangeSlashTarget(ProgramSkillFrame frame)
    {
        var choices = ShortRangeSlashChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "可额外指定一名距离为1的角色为杀的目标。", [],
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = frame.OwnerSeat, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveShortRangeSlashTarget(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The short-range chooser lost its owning program.");
        AssertShortRangeSlashDraft(frame);
        var choices = ShortRangeSlashChoices(frame);
        if (!AssistedChoicesEqual([selected], [choices.Single(c => c.Id == selected.Id)])) throw new InvalidOperationException("Short-range selection changed its exact target.");
        var use = ShortRangeSlashUse(frame.OwnerSeat, frame.WindowContext!)!;
        var source = ShortRangeSlashSource(frame); ShortRangeSlashTargetReceipt? receipt = null;
        if (selected.Targets is [var target] && _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            receipt = new(frame.Id, source, frame.GameplayHash, use.Id, use.Action?.ActionId, use.SourceSeat, target, use.TargetSeats);
            var targets = Array.AsReadOnly(use.TargetSeats.Append(target).ToArray());
            var updated = use with { TargetSeats = targets, ShortRangeSlashTarget = receipt,
                Enhancements = use.Enhancements | CurrentCardEnhancement.ExtraTarget };
            if (use.Action is { } action)
            {
                var newAction = CloneRoleAction(action, action.ActorSeat, targets);
                UpdateProgramRoleCardUse(updated with { Action = newAction }, newAction);
                SyncIssuedTieredRoundZeroTrickTargetWindows(use.Id, action);
            }
            else UpdateLifecycleCardUse(use.Id, _ => updated);
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, use.Id, target));
        }
        AdvanceEventRulesAndQueueFact(new ShortRangeSlashTargetResolvedEvent(use.Id, use.Action?.ActionId, source, receipt is not null, receipt));
        ClearPendingDecision(); ReplaceRuntimeTop(frame with { ShortRangeSlashTargetDraft = null }); AdvanceRuntimeProgram(frame.Id);
    }
    private void AssertShortRangeSlashDraft(ProgramSkillFrame frame)
    {
        if (frame.ShortRangeSlashTargetDraft is not { } draft) return;
        var use = frame.WindowContext is { } context ? ShortRangeSlashUse(frame.OwnerSeat, context) : null;
        if (use is null || use.Id != draft.CardUseFrameId || use.Action?.ActionId != draft.ActionId || frame.InstructionIndex != draft.InstructionIndex ||
            frame.InstructionIndex != 1 || GetProgramTrigger(frame).Effects is not [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }] ||
            ShortRangeSlashWasOffered(use.Id) || _resolutionStack.LastOrDefault()?.Id == frame.Id &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
                    !AssistedChoicesEqual(prompt.Choices, ShortRangeSlashChoices(frame))))
            throw new InvalidOperationException("A short-range target draft lost its source, actual use or frozen target choices.");
    }
    private PromptChoice SelectAiShortRangeSlash(PendingDecision decision, ProgramSkillFrame frame)
    {
        var view = CreateSnapshot(decision.PlayerSeat); var brain = _aiBrains[decision.PlayerSeat];
        var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
        var best = decision.Choices.Where(c => c.Targets.Count == 1).Select(c => (Choice: c, Score: brain.ScoreProgramTarget(view, c.Targets[0], hint)))
            .OrderByDescending(item => item.Score).ThenBy(item => item.Choice.Targets[0]).FirstOrDefault();
        return best.Choice is not null && best.Score > 0 ? best.Choice : decision.Choices.Single(c => c.Targets.Count == 0);
    }
    // Prove just this issuance at its exact ordered checkpoint. Later mature
    // additions/redirects are reconstructed by the caller and verified in full.
    private bool IsShortRangeSlashTargetFact(CardUseFrame use, ShortRangeSlashTargetResolvedEvent fact,
        IReadOnlyList<int> originalTargets)
    {
        if (!fact.Added || fact.Receipt is not { } r || use.ShortRangeSlashTarget is not { } issued ||
            fact.CardUseFrameId != use.Id || r.CardUseFrameId != use.Id || fact.ActionId != r.ActionId || fact.Source != r.Source ||
            issued.ProgramFrameId != r.ProgramFrameId || issued.Source != r.Source || issued.GameplayHash != r.GameplayHash ||
            issued.CardUseFrameId != r.CardUseFrameId || issued.ActionId != r.ActionId || issued.ActorSeat != r.ActorSeat ||
            issued.AddedTargetSeat != r.AddedTargetSeat || !issued.OriginalTargets.SequenceEqual(r.OriginalTargets) ||
            r.ActorSeat != use.SourceSeat || r.Source.OwnerSeat != r.ActorSeat || !IsSlashCard(use.CardKind) ||
            r.OriginalTargets.Count == 0 || !r.OriginalTargets.SequenceEqual(originalTargets) ||
            (r.OriginalTargets.Distinct().Count() != r.OriginalTargets.Count && !TryGetShortRangeFinalizedPrefix(use, r, out _)) ||
            r.OriginalTargets.Contains(r.AddedTargetSeat) ||
            !IsValidPlayerSeat(r.AddedTargetSeat) || r.AddedTargetSeat == r.ActorSeat ||
            (r.ActionId != use.Action?.ActionId && !(r.ActionId is null && use.Action is not null &&
                IsNormalizedLegacyCompletedCategoryUse(use, r.ActorSeat, use.CardKind, use.TargetSeats))) ||
            use.Action is { } action && (action.Type != CardActionType.Use || action.ActorSeat != r.ActorSeat || action.EffectiveKind != use.CardKind) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } program || program.GameplayHash != r.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == r.Source.BindingId) is not { } trigger ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }] ||
            trigger.Window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.OtherActualUseTargeted)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<ShortRangeSlashTargetResolvedEvent>().Count(e => e.CardUseFrameId == use.Id) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == r.ProgramFrameId && e.OwnerSeat == r.ActorSeat &&
                e.SkillId == r.Source.SkillId && e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId && e.Window == trigger.Window) == 1 &&
            history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == r.ProgramFrameId && e.SkillId == r.Source.SkillId &&
                e.OwnerSeat == r.ActorSeat && e.CardUseFrameId == use.Id && e.TargetSeat == r.AddedTargetSeat) == 1;
    }
    // Native preparation accepts the complete actual sequence after the first
    // redirects, before this finalized-target binding starts. That sequence may
    // legitimately repeat a seat; it is not a repeated user designation.
    private bool TryGetShortRangeFinalizedPrefix(CardUseFrame use, ShortRangeSlashTargetReceipt receipt,
        out CardActionContext finalized)
    {
        finalized = null!;
        if (!use.ProgramUseAccepted || receipt.ActionId is null || receipt.CardUseFrameId != use.Id ||
            use.Action is not { Type: CardActionType.Use } current || current.ActionId != receipt.ActionId ||
            current.ActorSeat != receipt.ActorSeat || current.ActorSeat != use.SourceSeat || current.EffectiveKind != use.CardKind)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        var bindings = history.Select((fact, index) => (fact, index)).Where(item => item.fact is ProgramBindingStartedEvent started &&
            started.FrameId == receipt.ProgramFrameId && started.SkillId == receipt.Source.SkillId && started.BindingId == receipt.Source.BindingId &&
            started.OwnerSeat == receipt.ActorSeat && started.SkillInstanceId == receipt.Source.SkillInstanceId &&
            started.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized).ToArray();
        if (bindings is not [var binding]) return false;
        var issuances = history.Select((fact, index) => (fact, index)).Where(item => item.fact is ShortRangeSlashTargetResolvedEvent resolved &&
            resolved.Added && resolved.CardUseFrameId == use.Id && resolved.ActionId == receipt.ActionId && resolved.Source == receipt.Source &&
            resolved.Receipt is { } issued && issued.ProgramFrameId == receipt.ProgramFrameId && issued.CardUseFrameId == use.Id &&
            issued.ActionId == receipt.ActionId && issued.GameplayHash == receipt.GameplayHash && issued.ActorSeat == receipt.ActorSeat &&
            issued.AddedTargetSeat == receipt.AddedTargetSeat && issued.OriginalTargets.SequenceEqual(receipt.OriginalTargets)).ToArray();
        var companions = history.Select((fact, index) => (fact, index)).Where(item => item.fact is ProgramCardUseTargetAddedEvent added &&
            added.FrameId == receipt.ProgramFrameId && added.SkillId == receipt.Source.SkillId && added.OwnerSeat == receipt.ActorSeat &&
            added.CardUseFrameId == use.Id && added.TargetSeat == receipt.AddedTargetSeat).ToArray();
        if (issuances is not [var issuance] || companions is not [var companion] ||
            companion.index <= binding.index || issuance.index <= companion.index) return false;
        var accepted = history.Take(binding.index).OfType<CardActionAcceptedEvent>()
            .LastOrDefault(e => e.Action.ActionId == current.ActionId)?.Action;
        if (accepted is null || accepted.Type != CardActionType.Use || accepted.ActorSeat != current.ActorSeat ||
            accepted.ProviderSeat != current.ProviderSeat || accepted.EffectiveKind != current.EffectiveKind ||
            accepted.ParentActionId != current.ParentActionId || accepted.RequesterSeat != current.RequesterSeat ||
            accepted.ResponderSeat != current.ResponderSeat || accepted.OpponentSeat != current.OpponentSeat ||
            accepted.EffectiveSuit != current.EffectiveSuit || accepted.EffectiveRank != current.EffectiveRank ||
            accepted.EffectiveIsRed != current.EffectiveIsRed || !accepted.PhysicalCards.SequenceEqual(current.PhysicalCards) ||
            !accepted.ConversionChain.SequenceEqual(current.ConversionChain) || !accepted.TargetSeats.SequenceEqual(receipt.OriginalTargets))
            return false;
        finalized = accepted; return true;
    }
    private void RecordShortRangeSlashRedirect(ProgramSkillFrame redirector, long useId, int originalSeat, int newSeat)
    {
        // Converted fire Slashes already have their mature dedicated redirect fact.
        if (LifecycleCardUse(useId) is not { ShortRangeSlashTarget: not null, CurrentSlashFirePolicy: null, Action: { } action } use) return;
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
            CompleteProgramEventHistory().OfType<ProgramActualSlashTargetRedirectedEvent>().Any(e => e.ProgramFrameId == redirector.Id &&
                e.CardUseFrameId == useId && e.InstructionIndex == redirector.InstructionIndex))
            throw new InvalidOperationException("A short-range native redirect lost its exact program, paused instruction or actual target cursor.");
        AdvanceEventRulesAndQueueFact(new ProgramActualSlashTargetRedirectedEvent(redirector.Id, useId, action.ActionId, source,
            redirector.GameplayHash, redirector.InstructionIndex, use.TargetIndex, originalSeat, newSeat));
    }
    private bool IsShortRangeSlashRedirectFact(CardUseFrame use, ProgramActualSlashTargetRedirectedEvent fact, IReadOnlyList<int> actualTargets)
    {
        if (use.ShortRangeSlashTarget is null || use.CurrentSlashFirePolicy is not null || fact.CardUseFrameId != use.Id ||
            fact.ActionId is null || fact.ActionId != use.Action?.ActionId || fact.Source.OwnerSeat != fact.OriginalTargetSeat ||
            fact.TargetIndex < 0 || fact.TargetIndex >= actualTargets.Count || actualTargets[fact.TargetIndex] != fact.OriginalTargetSeat ||
            !IsValidPlayerSeat(fact.NewTargetSeat) || fact.NewTargetSeat == use.SourceSeat || fact.NewTargetSeat == fact.OriginalTargetSeat ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            ProgramInstructionResolver.Default.Find(program, ProgramInstructionSourceKind.Trigger, fact.Source.BindingId) is not { } plan ||
            plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || fact.InstructionIndex < 1 || fact.InstructionIndex > plan.Instructions.Count ||
            plan.GetPausedInstruction(fact.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<ProgramActualSlashTargetRedirectedEvent>().Count(e => e == fact) == 1 &&
            history.OfType<ProgramActualSlashTargetRedirectedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId &&
                e.CardUseFrameId == use.Id && e.InstructionIndex == fact.InstructionIndex) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.SkillId == fact.Source.SkillId &&
                e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId && e.OwnerSeat == fact.Source.OwnerSeat &&
                e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) == 1;
    }
    private bool HasShortRangeSlashTail(CardUseFrame use)
    {
        if (use.ShortRangeSlashTarget is not { } receipt || receipt.CardUseFrameId != use.Id || receipt.ActorSeat != use.SourceSeat ||
            (receipt.ActionId != use.Action?.ActionId && !(receipt.ActionId is null && use.Action is not null &&
                IsNormalizedLegacyCompletedCategoryUse(use, receipt.ActorSeat, use.CardKind, use.TargetSeats))) ||
            !IsSlashCard(use.CardKind) || receipt.OriginalTargets.Count == 0 ||
            receipt.OriginalTargets.Contains(receipt.AddedTargetSeat) ||
            receipt.Source.OwnerSeat != receipt.ActorSeat || (use.Enhancements & CurrentCardEnhancement.ExtraTarget) == 0 ||
            use.TargetSeats.Count != receipt.OriginalTargets.Count + 1 ||
            use.Action is { } action && (action.Type != CardActionType.Use || action.ActorSeat != receipt.ActorSeat ||
                action.EffectiveKind != use.CardKind) ||
            _contentRegistry.GetSkill(receipt.Source.SkillId).Program is not { } program || program.GameplayHash != receipt.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == receipt.Source.BindingId)?.Effects is not [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }]) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var resolved = Array.FindIndex(history, e => e is ShortRangeSlashTargetResolvedEvent fact && fact.Receipt is { } frozen &&
            frozen.ProgramFrameId == receipt.ProgramFrameId && frozen.Source == receipt.Source && frozen.GameplayHash == receipt.GameplayHash &&
            frozen.CardUseFrameId == receipt.CardUseFrameId && frozen.ActionId == receipt.ActionId && frozen.ActorSeat == receipt.ActorSeat &&
            frozen.AddedTargetSeat == receipt.AddedTargetSeat && frozen.OriginalTargets.SequenceEqual(receipt.OriginalTargets) && fact.Added &&
            fact.CardUseFrameId == use.Id && fact.ActionId == receipt.ActionId && fact.Source == receipt.Source);
        if (resolved < 0 || history[resolved] is not ShortRangeSlashTargetResolvedEvent resolvedFact ||
            !IsShortRangeSlashTargetFact(use, resolvedFact, receipt.OriginalTargets) ||
            history.OfType<ShortRangeSlashTargetResolvedEvent>().Count(e => e.CardUseFrameId == use.Id) != 1 ||
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == receipt.ProgramFrameId && e.OwnerSeat == receipt.ActorSeat &&
                e.SkillId == receipt.Source.SkillId && e.BindingId == receipt.Source.BindingId && e.SkillInstanceId == receipt.Source.SkillInstanceId) != 1) return false;
        var designated = receipt.OriginalTargets.Append(receipt.AddedTargetSeat).ToArray();
        var prefix = designated.ToArray();
        foreach (var fact in history.Skip(resolved + 1))
        {
            if (fact is ProgramActualSlashTargetRedirectedEvent redirected && redirected.CardUseFrameId == use.Id)
            {
                if (!IsShortRangeSlashRedirectFact(use, redirected, prefix)) return false;
                prefix[redirected.TargetIndex] = redirected.NewTargetSeat;
            }
            else if (fact is ProgramCurrentSlashFireRedirectedEvent fire && fire.CardUseFrameId == use.Id && fire.ActionId == use.Action?.ActionId &&
                fire.TargetIndex >= 0 && fire.TargetIndex < prefix.Length && prefix[fire.TargetIndex] == fire.OriginalTargetSeat)
                prefix[fire.TargetIndex] = fire.NewTargetSeat;
        }
        // Native redirect changes actual seats, while Action keeps the designation.
        // The exact legacy normalizer is issued only at completion with actual seats.
        return use.TargetSeats.SequenceEqual(prefix) && (use.Action is null ||
            receipt.ActionId is null && IsNormalizedLegacyCompletedCategoryUse(use, receipt.ActorSeat, use.CardKind, prefix) ||
            use.Action.TargetSeats.SequenceEqual(designated));
    }
    private bool TryGetShortRangeVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> target)
    {
        target = [];
        if (!HasShortRangeSlashTail(use) || use.ShortRangeSlashTarget is not { OriginalTargets.Count: 1 } receipt ||
            use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.CardAttack is not { CardId: null, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || parent.InstructionIndex < 1 || use.Action is not { Type: CardActionType.Use } action ||
            action.ProviderSeat != receipt.ActorSeat || action.PhysicalCards.Count != 0 ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame exact || exact.Id != parent.Id) return false;
        var paused = ProgramInstructionResolver.Default.Resolve(parent, program).GetPausedInstruction(parent.InstructionIndex).Effect;
        if (receipt.ActionId is null && IsNormalizedLegacyCompletedCategoryUse(use, receipt.ActorSeat, use.CardKind, use.TargetSeats))
        {
            var normalized = CompleteProgramEventHistory().OfType<CompletedCategoryVirtualUseNormalizedEvent>().Single(e => e.CardUseFrameId == use.Id);
            var original = CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
            var issuedUse = CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
            var legacyIssuer = new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId);
            if (normalized.ProgramParentFrameId != parent.Id || normalized.ProducerSource != legacyIssuer || normalized.ProducerGameplayHash != parent.GameplayHash ||
                normalized.ProducerInstructionIndex != parent.InstructionIndex || parent.OwnerSeat != receipt.ActorSeat ||
                paused.Op != SkillProgramEffectOp.UseVirtualCard || paused.UseCardActionWindows || !IsSlashCard(paused.OutputKind ?? CardKind.Dodge) ||
                original is not { TargetSeats.Count: 1 } || !receipt.OriginalTargets.SequenceEqual(original.TargetSeats) ||
                parent.SelectedTargetSeats is not [var legacySelected] || legacySelected != original.TargetSeats[0] ||
                issuedUse is not { CardId: 0 } || issuedUse.SourceSeat != receipt.ActorSeat || issuedUse.CardKind != paused.OutputKind)
                return false;
            target = Array.AsReadOnly(original.TargetSeats.ToArray()); return true;
        }
        if (!TryGetShortRangeFinalizedPrefix(use, receipt, out var accepted)) return false;
        var declared = CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
        var declaredUse = CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id);
        if (accepted is null || accepted.Type != CardActionType.Use || accepted.ActorSeat != receipt.ActorSeat || accepted.ProviderSeat != receipt.ActorSeat ||
            accepted.EffectiveKind != action.EffectiveKind || accepted.EffectiveSuit != Suit.None || accepted.EffectiveRank != 0 ||
            accepted.RequesterSeat is not null || accepted.ResponderSeat is not null || accepted.OpponentSeat is not null ||
            accepted.PhysicalCards.Count != 0 || accepted.TargetSeats.Count != 1 || declared is not { TargetSeats.Count: 1 } ||
            !accepted.TargetSeats.SequenceEqual(receipt.OriginalTargets) ||
            declaredUse is not { CardId: 0 } || declaredUse.SourceSeat != receipt.ActorSeat || declaredUse.CardKind != action.EffectiveKind ||
            !accepted.ConversionChain.SequenceEqual(action.ConversionChain)) return false;
        var issuer = new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId);
        var exactProducer = paused.Op switch
        {
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == action.ActorSeat && action.ConversionChain.SequenceEqual([issuer]) &&
                (paused.TargetReference is null ? parent.SelectedTargetSeats is [var selected] && selected == declared.TargetSeats[0]
                    : paused.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == declared.TargetSeats[0]),
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats is [var actor] && actor == action.ActorSeat && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash => parent.OwnerSeat == action.ActorSeat &&
                parent.SelectedTargetSeats is [var selected] && selected == declared.TargetSeats[0] && action.ConversionChain.Count == 0 &&
                paused.UseCardActionWindows && paused.OutputKind == CardKind.Slash && paused.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
            _ => false
        };
        if (!exactProducer) return false;
        target = Array.AsReadOnly(declared.TargetSeats.ToArray()); return true;
    }
    private void AppendShortRangeLegacyCandidate(CardUseFrame use, long windowId,
        List<(ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)> entries)
    {
        if (!HasShortRangeSlashCapability || use.Action is not null || !IsSlashCard(use.CardKind) || ShortRangeSlashWasOffered(use.Id) ||
            !_players[use.SourceSeat].IsAlive || use.TargetSeats.Count == 0 || ShortRangeSlashTargets(use).Length == 0 ||
            FreezeActualUseTarget(use, use.TargetSeats[0]) is not { ActionId: null } identity) return;
        foreach (var candidate in CollectEligibleProgramTriggerCandidates(_players[use.SourceSeat], SkillProgramTriggerWindow.OtherActualUseTargeted,
            CaptureProgramTriggerFacts(_players[use.SourceSeat])).Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }]))
            entries.Add((candidate, new(SkillProgramTriggerWindow.OtherActualUseTargeted, windowId, candidate.OwnerSeat,
                SourceSeat: use.SourceSeat, TargetSeat: identity.TargetSeat, OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureProgramTriggerFacts(_players[use.SourceSeat])) { ActualUseTarget = identity }));
    }
    private bool IsShortRangeLegacyCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.OtherActualUseTargeted ||
            GetProgramTrigger(candidate).Effects is not [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }]) return false;
        return ShortRangeSlashUse(candidate.OwnerSeat, context) is not null &&
            _resolutionStack.OfType<ActualUseTargetWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } window &&
            window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
            window.Candidates[window.CandidateIndex] == candidate && window.Contexts[window.CandidateIndex] == context;
    }
    private sealed partial class ProgramSkillHost : IDirectedDistanceDebtHost
    {
        public SkillProgramStepOutcome OfferShortRangeSlashTarget(ProgramSkillFrame frame) => engine.OfferShortRangeSlashTarget(frame);
        public void GrantFixedDistanceOneTurnPolicy(ProgramSkillFrame frame, int target) => engine.GrantFixedDistanceOneTurnPolicy(frame, target);
        public SkillProgramStepOutcome SettleFixedDistanceOneEndingDebt(ProgramSkillFrame frame) => engine.SettleFixedDistanceOneEndingDebt(frame);
        public bool CanContinueIssuedFixedDistanceEnding(ProgramSkillFrame frame) => engine.CanContinueIssuedFixedDistanceEnding(frame);
    }
}
