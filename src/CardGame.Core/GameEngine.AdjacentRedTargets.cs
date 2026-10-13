using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAdjacentRedTargetCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.JoinAdjacentRedCardTarget);
    private bool TryGetAdjacentRedTargetUse(int owner, ProgramSkillWindowContext context, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } || !IsValidPlayerSeat(owner) || !_players[owner].IsAlive ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } w ||
            w.ParentFrameId != identity.ParentCardUseFrameId || w.CompletedResponseReturn is not null ||
            w.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard) ||
            LifecycleCardUse(w.ParentFrameId) is not { Action: { Type: CardActionType.Use } a } u || a.ActionId != identity.CardActionId ||
            w.Action.ActionId != a.ActionId || w.Action.ActorSeat != a.ActorSeat || u.SourceSeat != a.ActorSeat || a.EffectiveKind != u.CardKind ||
            !w.Action.TargetSeats.SequenceEqual(u.TargetSeats) || !a.TargetSeats.SequenceEqual(u.TargetSeats) ||
            GetProgramCardCategory(u.CardKind) != SkillProgramCardCategory.Basic && !IsOrdinaryTrick(u.CardKind)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == w.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != u.Id ||
            u.CardKind == CardKind.BorrowedSword && (u.TargetSeats.Count < 2 || u.TargetSeats.Count % 2 != 0 ||
                u.TargetSeats.Where((_, i) => i % 2 == 0).Distinct().Count() != u.TargetSeats.Count / 2)) return false;
        // This is the actual accepted Use, including a Peach/Alcohol rescue Use.
        // A Dodge response has no such finalized card-use continuation.
        if (a.PhysicalCards.Count > 0 && a.PhysicalCards.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Processing &&
                !IsCurrentUsePhysicalCardClaim(u.Id, c.CardId) && !IsExchangedCardClaim(a.ActionId, c.CardId))) return false;
        use = u; window = w; return true;
    }
    private IEnumerable<int> AdjacentRedOriginalPrimaryTargets(CardUseFrame use) => use.CardKind == CardKind.BorrowedSword
        ? use.TargetSeats.Where((_, i) => i % 2 == 0)
        : use.TargetSeats.Count == 0 && use.CardKind == CardKind.DrawTwo ? [use.SourceSeat] : use.TargetSeats;
    private bool AdjacentRedTargetEligible(CardUseFrame use, int owner)
    {
        var a = use.Action!; var target = _players[owner];
        if (!target.IsAlive || _winner != Winner.None || a.ActorSeat == owner || use.TargetSeats.Contains(owner) ||
            !(a.EffectiveIsRed ?? a.EffectiveSuit is Suit.Heart or Suit.Diamond) ||
            !AdjacentRedOriginalPrimaryTargets(use).Any(s => s != owner && _players[s].IsAlive && GetCombatDistance(s, owner) == 1) ||
            HasTurnCardTargetRestriction(a.ActorSeat, SkillProgramCardTargetRestriction.SelfOnly) ||
            IsDirectedCardTargetProhibited(a.ActorSeat, owner, use.CardKind) ||
            IsCardTargetProhibited(target, use.CardKind, a.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(a)) ||
            HasBeneficiarySuitShield(a.ActorSeat, owner, a.EffectiveSuit)) return false;
        // Mandatory target addition ignores use distance and permits a full-HP
        // Peach target (its native recovery may be zero). Public card conditions
        // and prohibitions still apply; BorrowedSword always needs a whole pair.
        return use.CardKind switch
        {
            CardKind.FireAttack => GetHand(target).Count > 0,
            CardKind.Snatch or CardKind.Dismantlement => HasTargetCard(target),
            CardKind.BorrowedSword => GetWeapon(target) is not null && _players.Any(p => IsLegalBorrowedSwordSlashTarget(target, p)),
            CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains =>
                !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, use.CardKind),
            _ => IsSlashCard(use.CardKind) || use.CardKind is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.Duel or CardKind.IronChain
        };
    }
    private CardUseFrame RequireAdjacentRedParent(ProgramSkillFrame f, out ProgramCardTriggerWindowFrame window)
    {
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.JoinAdjacentRedCardTarget }] ||
            !TryGetAdjacentRedTargetUse(f.OwnerSeat, context, out var use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), context, window))
            throw new InvalidOperationException("Adjacent red targeting lost its exact observer and real native Use.");
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("Adjacent red targeting requires its immediate finalized native parent.");
        return use;
    }
    private SkillProgramStepOutcome BeginAdjacentRedTarget(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireAdjacentRedParent(f, out _);
        if (f.AdjacentRedTarget is not null || CompleteProgramEventHistory().OfType<AdjacentRedTargetAddedEvent>().Any(e =>
                e.CardUseFrameId == use.Id && e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId))
            throw new InvalidOperationException("Adjacent red targeting cannot add one observer twice.");
        if (!AdjacentRedTargetEligible(use, f.OwnerSeat)) return SkillProgramStepOutcome.Continue;
        if (use.CardKind != CardKind.BorrowedSword) { ApplyAdjacentRedTarget(f, [f.OwnerSeat]); return SkillProgramStepOutcome.Continue; }
        var choices = AdjacentBorrowedSwordChoices(f, use);
        ReplaceRuntimeTop(f = f with { AdjacentRedTarget = new() { CardUseFrameId = use.Id, ActionId = use.Action!.ActionId,
            Source = DamageAcquisitionSource(f), GameplayHash = f.GameplayHash, OriginalTargets = use.TargetSeats, Choices = choices } });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "寄篱：成为借刀杀人的额外目标，请选择你使用杀的目标。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState(); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> AdjacentBorrowedSwordChoices(ProgramSkillFrame f, CardUseFrame use) => Array.AsReadOnly(
        _players.Where(p => IsLegalBorrowedSwordSlashTarget(_players[f.OwnerSeat], p)).OrderBy(p => p.Seat).Select(p => PromptChoiceProjection.Freeze(
            new PromptChoice(new($"adjacent-red.{f.Id}.victim-{p.Seat}"), $"额外令你对 {p.Name} 使用杀", [], [f.OwnerSeat, p.Seat],
                new Dictionary<string, string> { ["program-action"] = "adjacent-red-target", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                    ["card-use-id"] = use.Id.ToString(CultureInfo.InvariantCulture), ["action-id"] = use.Action!.ActionId.ToString(CultureInfo.InvariantCulture) }))).ToArray());
    private void ResolveAdjacentRedTargetChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Adjacent red choice lost its owner.");
        AssertAdjacentRedTargetDraft(f); var draft = f.AdjacentRedTarget!;
        if (!AssistedChoicesEqual([selected], draft.Choices.Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Adjacent red choice must retain its whole published BorrowedSword pair.");
        ClearPendingDecision(); ApplyAdjacentRedTarget(f, selected.Targets);
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { AdjacentRedTarget = null }); AdvanceRuntimeProgram(f.Id);
    }
    private void ApplyAdjacentRedTarget(ProgramSkillFrame f, IReadOnlyList<int> added)
    {
        var use = RequireAdjacentRedParent(f, out var window);
        if (!AdjacentRedTargetEligible(use, f.OwnerSeat) || added[0] != f.OwnerSeat ||
            (use.CardKind == CardKind.BorrowedSword ? added.Count != 2 || !IsLegalBorrowedSwordSlashTarget(_players[f.OwnerSeat], _players[added[1]]) : added.Count != 1))
            throw new InvalidOperationException("Adjacent red addition lost its current legal target or complete pair.");
        var original = use.TargetSeats.Count == 0 && use.CardKind == CardKind.DrawTwo ? Array.AsReadOnly(new[] { use.SourceSeat }) : use.TargetSeats;
        var targets = Array.AsReadOnly(original.Concat(added).ToArray()); var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
        var simple = use.AdjustedSimpleContinuation;
        if (window.Continuation == ProgramCardContinuation.FinalizedSimpleCard)
        {
            var current = window.SimpleContinuation ?? throw new InvalidOperationException("Adjacent basic targeting lost its actual simple effect.");
            if (current.Effect is not (SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol))
                throw new InvalidOperationException("Adjacent basic targeting supports a native Peach/Alcohol effect.");
            simple ??= current with { RecoveryPolicySources = current.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null };
        }
        var updated = use with { Action = action, TargetSeats = targets, TargetsAdjusted = true, AdjustedSimpleContinuation = simple,
            Enhancements = use.Enhancements | CurrentCardEnhancement.ExtraTarget };
        UpdateProgramRoleCardUse(updated, action);
        AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, f.OwnerSeat));
        AdvanceEventRulesAndQueueFact(new AdjacentRedTargetAddedEvent(f.Id, use.Id, action.ActionId, action.ActorSeat, DamageAcquisitionSource(f), f.GameplayHash, original, added, targets));
        RefreshCurrentUseAddedTargetCandidates(updated, [f.OwnerSeat]);
    }
    private void AssertAdjacentRedTargetDraft(ProgramSkillFrame f)
    {
        if (f.AdjacentRedTarget is not { } d) return;
        var use = RequireAdjacentRedParent(f, out _);
        if (use.CardKind != CardKind.BorrowedSword || d.CardUseFrameId != use.Id || d.ActionId != use.Action!.ActionId || d.Source != DamageAcquisitionSource(f) ||
            d.GameplayHash != f.GameplayHash || !d.OriginalTargets.SequenceEqual(use.TargetSeats) || !AdjacentRedTargetEligible(use, f.OwnerSeat) ||
            !AssistedChoicesEqual(d.Choices, AdjacentBorrowedSwordChoices(f, use)) || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } p || p.PlayerSeat != f.OwnerSeat || p.SourceSeat != f.OwnerSeat ||
            p.TargetSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, d.Choices))
            throw new InvalidOperationException("Adjacent red targeting lost its mandatory native pair choice.");
    }
    private bool IsAdjacentRedTargetFact(CardUseFrame use, AdjacentRedTargetAddedEvent fact, IReadOnlyList<int> original, int? currentProofActor = null)
    {
        var history = CompleteProgramEventHistory().ToArray(); var owner = fact.Source.OwnerSeat;
        return fact.CardUseFrameId == use.Id && fact.ActionId == use.Action?.ActionId && fact.ActorSeat == (currentProofActor ?? use.Action!.ActorSeat) && fact.Source.OwnerSeat != fact.ActorSeat &&
            fact.AddedTargets.Count == (use.CardKind == CardKind.BorrowedSword ? 2 : 1) && fact.AddedTargets[0] == owner &&
            !original.Contains(owner) && fact.AddedTargets.All(IsValidPlayerSeat) && fact.OriginalTargets.SequenceEqual(original) &&
            fact.ResultTargets.SequenceEqual(original.Concat(fact.AddedTargets)) &&
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is { } program && program.GameplayHash == fact.GameplayHash &&
            program.Triggers.SingleOrDefault(t => t.Id == fact.Source.BindingId)?.Effects is [{ Op: SkillProgramEffectOp.JoinAdjacentRedCardTarget }] &&
            history.OfType<AdjacentRedTargetAddedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == owner && e.SkillId == fact.Source.SkillId &&
                e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1 &&
            history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id &&
                e.OwnerSeat == owner && e.SkillId == fact.Source.SkillId && e.TargetSeat == owner) == 1;
    }
    private bool HasAdjacentRedTargetTail(CardUseFrame use) => CompleteProgramEventHistory().OfType<AdjacentRedTargetAddedEvent>().Any(e =>
        IsAdjacentRedTargetFact(use, e, e.OriginalTargets, e.ActorSeat) && use.Action!.TargetSeats.Take(e.ResultTargets.Count).SequenceEqual(e.ResultTargets) &&
        use.TargetSeats.Count == use.Action.TargetSeats.Count);
}
