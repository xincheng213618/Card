using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasTargetedOrdinaryTrickAdjustment => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AdjustTargetedOrdinaryTrick);
    private bool TryGetTargetedOrdinaryTrickUse(ProgramSkillWindowContext context, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { } action } current || !IsOrdinaryTrick(current.CardKind)) return false;
        // Earlier role changes preserve the same actual action but can change its
        // user. Legality follows that current user while the issued source stays frozen.
        var live = context with { CardUse = identity with { ActorSeat = action.ActorSeat } };
        return TryGetDesignatedExtraTargetUse(action.ActorSeat, live, out use, out window) && window.Continuation == ProgramCardContinuation.FinalizedTrick;
    }
    private bool TargetedTrickEffectIsEffective(CardUseFrame use, int seat) => _players[seat].IsAlive &&
        use.IneffectiveTargetSeats?.Contains(seat) != true &&
        _turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, seat, SkillRuleQuery.CardEffectImmunity, use.CardKind).Count == 0;
    private int[] TargetedTrickPrimaryTargets(CardUseFrame use) => use.CardKind == CardKind.BorrowedSword
        ? use.TargetSeats.Where((_, index) => index % 2 == 0).ToArray()
        : use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0 ? [use.SourceSeat] : use.TargetSeats.ToArray();
    private IReadOnlyList<PromptChoice> TargetedTrickChoices(long frameId, CardUseFrame use)
    {
        Dictionary<string, string> Parameters(string branch) => new() { ["program-action"] = "targeted-ordinary-trick", ["branch"] = branch,
            ["frame-id"] = frameId.ToString(CultureInfo.InvariantCulture), ["card-use-id"] = use.Id.ToString(CultureInfo.InvariantCulture),
            ["action-id"] = use.Action!.ActionId.ToString(CultureInfo.InvariantCulture) };
        var choices = new List<PromptChoice>(); var primary = TargetedTrickPrimaryTargets(use);
        foreach (var target in _players.Where(p => !primary.Contains(p.Seat) && CanBeUniqueLeaderTrickTarget(use, p)).OrderBy(p => p.Seat))
        {
            if (use.CardKind == CardKind.BorrowedSword)
                foreach (var victim in _players.Where(v => IsLegalBorrowedSwordSlashTarget(target, v)).OrderBy(v => v.Seat))
                    choices.Add(new(new($"targeted-trick.{frameId}.add.{target.Seat}.{victim.Seat}"), $"增加目标：{target.Name} 对 {victim.Name} 使用杀", [], [target.Seat, victim.Seat], Parameters("add")));
            else choices.Add(new(new($"targeted-trick.{frameId}.add.{target.Seat}"), $"增加目标：{target.Name}", [], [target.Seat], Parameters("add")));
        }
        var effective = primary.Where(s => TargetedTrickEffectIsEffective(use, s)).ToArray();
        if (effective.Length > 1)
            foreach (var seat in effective)
                choices.Add(new(new($"targeted-trick.{frameId}.ineffective.{seat}"), $"令此牌对 {_players[seat].Name} 无效", [], [seat], Parameters("ineffective")));
        choices.Add(new(new($"targeted-trick.{frameId}.decline"), "不改变效果", [], [], Parameters("decline")));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private bool CanOfferTargetedOrdinaryTrick(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustTargetedOrdinaryTrick)) return true;
        return TryGetTargetedOrdinaryTrickUse(context, out var use, out var window) &&
            DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
            TargetedTrickPrimaryTargets(use).Contains(candidate.OwnerSeat) && _players[candidate.OwnerSeat].IsAlive &&
            !CompleteProgramEventHistory().OfType<TargetedOrdinaryTrickOfferedEvent>().Any(e => e.Receipt.CardUseFrameId == use.Id &&
                e.Receipt.Source.OwnerSeat == candidate.OwnerSeat && e.Receipt.Source.SkillId == candidate.SkillId && e.Receipt.Source.BindingId == candidate.BindingId) &&
            TargetedTrickChoices(0, use).Any(c => c.Parameters["branch"] != "decline");
    }
    private CardUseFrame RequireTargetedOrdinaryTrickParent(ProgramSkillFrame f, out ProgramCardTriggerWindowFrame window)
    {
        if (f.TriggerId is not { } binding || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context || !TryGetTargetedOrdinaryTrickUse(context, out var use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, binding, f.SkillInstanceId, f.GameplayHash, 0), context, window) ||
            !TargetedTrickPrimaryTargets(use).Contains(f.OwnerSeat) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.AdjustTargetedOrdinaryTrick)
            throw new InvalidOperationException("A targeted trick adjustment lost its exact source, owning Use or target candidate.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("A targeted trick adjustment requires its immediate typed designation parent.");
        return use;
    }
    private SkillProgramStepOutcome BeginTargetedOrdinaryTrick(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireTargetedOrdinaryTrickParent(f, out var window);
        if (f.TargetedOrdinaryTrick is not null) throw new InvalidOperationException("A targeted trick adjustment was already issued.");
        var r = new TargetedOrdinaryTrickReceipt { WindowFrameId = window.Id, CardUseFrameId = use.Id, ActionId = use.Action!.ActionId,
            Source = TargetedTrickHandHpSource(f), GameplayHash = f.GameplayHash, InstructionIndex = f.InstructionIndex,
            OriginalTargets = use.TargetSeats, Choices = TargetedTrickChoices(f.Id, use) };
        ReplaceRuntimeTop(f = f with { TargetedOrdinaryTrick = r });
        AdvanceEventRulesAndQueueFact(new TargetedOrdinaryTrickOfferedEvent(f.Id, r));
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "增加一个普通锦囊目标或令其对一个目标无效（保留至少一个有效目标）。", [],
            r.Choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = r.Choices, TargetSeat = f.OwnerSeat, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    [ProgramAnswer("targeted-ordinary-trick")]
    private void ResolveTargetedOrdinaryTrickChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No owning targeted trick frame.");
        var use = RequireTargetedOrdinaryTrickParent(f, out _); var r = f.TargetedOrdinaryTrick ?? throw new InvalidOperationException("No issued target receipt.");
        AssertTargetedOrdinaryTrickReceipt(f);
        if (!AssistedChoicesEqual([selected], r.Choices.Where(c => c.Id == selected.Id).ToArray()) ||
            !AssistedChoicesEqual([selected], TargetedTrickChoices(f.Id, use).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("A targeted trick answer lost its current legal participant or last-effective-target protection.");
        var branch = selected.Parameters["branch"]; var before = use.TargetSeats;
        IReadOnlyList<int> added = branch == "add" ? selected.Targets : [];
        int? ineffective = branch == "ineffective" ? selected.Targets.Single() : null;
        if (ineffective is { } seat) MarkCardEffectIneffective(use.Id, seat);
        if (added.Count > 0)
        {
            var result = Array.AsReadOnly(before.Concat(added).ToArray());
            var action = CloneDesignatedExtraTargetAction(use.Action!, result);
            UpdateProgramRoleCardUse(use with { TargetSeats = result, Action = action, TargetsAdjusted = true }, action);
        }
        use = LifecycleCardUse(use.Id)!;
        AdvanceEventRulesAndQueueFact(new TargetedOrdinaryTrickResolvedEvent(f.Id, use.Id, use.Action!.ActionId,
            r.Source, r.GameplayHash, before, added, ineffective, use.TargetSeats));
        if (ineffective is { } canceled) AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, use.SourceSeat, canceled, use.CardKind, CardEffectSkipReason.SkillNullified));
        if (added.Count > 0) RefreshCurrentUseAddedTargetCandidates(use, [added[0]]);
        ClearPendingDecision(); ReplaceRuntimeTop(f with { TargetedOrdinaryTrick = null }); AdvanceRuntimeProgram(f.Id);
    }
    private void AssertTargetedOrdinaryTrickReceipt(ProgramSkillFrame f)
    {
        if (f.TargetedOrdinaryTrick is not { } r) return;
        var use = RequireTargetedOrdinaryTrickParent(f, out var window);
        var offered = CompleteProgramEventHistory().OfType<TargetedOrdinaryTrickOfferedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.WindowFrameId != window.Id || r.CardUseFrameId != use.Id || r.ActionId != use.Action!.ActionId || r.Source != TargetedTrickHandHpSource(f) ||
            r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 || !r.OriginalTargets.SequenceEqual(use.TargetSeats) ||
            offered is not [var fact] || fact.Receipt.Source != r.Source || fact.Receipt.ActionId != r.ActionId ||
            !fact.Receipt.OriginalTargets.SequenceEqual(r.OriginalTargets) || !AssistedChoicesEqual(fact.Receipt.Choices, r.Choices) ||
            !AssistedChoicesEqual(r.Choices, TargetedTrickChoices(f.Id, use)))
            throw new InvalidOperationException("An issued targeted-trick receipt lost its frozen actual action or current legal choices.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.AdjustTargetedOrdinaryTrick)]
    private PromptChoice SelectAiTargetedOrdinaryTrick(PendingDecision p, ProgramSkillFrame f)
    {
        var use = RequireTargetedOrdinaryTrickParent(f, out _);
        return p.Choices.OrderByDescending(c => TargetedTrickPublicChoiceScore(f.OwnerSeat, use, c)).First();
    }
    private bool IsTargetedOrdinaryTrickResolvedFact(CardUseFrame use, TargetedOrdinaryTrickResolvedEvent fact, IReadOnlyList<int> before)
    {
        var pair = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        if (fact.CardUseFrameId != use.Id || fact.ActionId != use.Action?.ActionId || !IsOrdinaryTrick(use.CardKind) ||
            !fact.BeforeTargets.SequenceEqual(before) || fact.AddedTargets.Count is not 0 && fact.AddedTargets.Count != pair ||
            fact.AddedTargets.Count > 0 && fact.IneffectiveTarget is not null ||
            !fact.ResultTargets.SequenceEqual(before.Concat(fact.AddedTargets)) ||
            !ActualHandGainMutationBinding(fact.ProgramFrameId, fact.Source.SkillId, fact.Source.OwnerSeat, SkillProgramTriggerWindow.CardUseTargetsFinalized,
                SkillProgramEffectOp.AdjustTargetedOrdinaryTrick) || _contentRegistry.GetSkill(fact.Source.SkillId).Program?.GameplayHash != fact.GameplayHash) return false;
        var original = before.Where((_, index) => index % pair == 0).ToArray();
        if (!original.Contains(fact.Source.OwnerSeat) || fact.AddedTargets.Count > 0 && original.Contains(fact.AddedTargets[0]) ||
            fact.IneffectiveTarget is { } target && (!original.Contains(target) || use.IneffectiveTargetSeats?.Contains(target) != true)) return false;
        return CompleteProgramEventHistory().OfType<TargetedOrdinaryTrickOfferedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId &&
            e.Receipt.CardUseFrameId == use.Id && e.Receipt.ActionId == fact.ActionId && e.Receipt.Source == fact.Source &&
            e.Receipt.GameplayHash == fact.GameplayHash && e.Receipt.OriginalTargets.SequenceEqual(before) &&
            e.Receipt.Choices.Any(c => c.Parameters["branch"] == (fact.AddedTargets.Count > 0 ? "add" : fact.IneffectiveTarget is not null ? "ineffective" : "decline") &&
                c.Targets.SequenceEqual(fact.AddedTargets.Count > 0 ? fact.AddedTargets : fact.IneffectiveTarget is { } canceled ? [canceled] : []))) == 1;
    }
    private bool HasTargetedOrdinaryTrickTail(CardUseFrame use) => CompleteProgramEventHistory().OfType<TargetedOrdinaryTrickResolvedEvent>().Any(e =>
        e.AddedTargets.Count > 0 && IsTargetedOrdinaryTrickResolvedFact(use, e, e.BeforeTargets) && use.TargetSeats.Count >= e.ResultTargets.Count &&
        use.TargetSeats.Take(e.ResultTargets.Count).SequenceEqual(e.ResultTargets));
}
