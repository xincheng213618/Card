using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksDamageTargetBudgetTargets => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AddDamageMarkerBudgetTargets);
    private static bool DamageBudgetCard(CardActionContext a) => IsSlashCard(a.EffectiveKind) ||
        IsOrdinaryTrick(a.EffectiveKind) && a.EffectiveIsRed == false;
    private CardConversionSource BudgetSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool CanOfferDamageTargetBudget(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects is not [var e]) return true;
        if (e.Op == SkillProgramEffectOp.GainDamageOccurrenceTargetBudget)
            return context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, OccurrenceIndex: 0 } &&
                (context.SourceSeat == c.OwnerSeat || context.TargetSeat == c.OwnerSeat);
        if (e.Op != SkillProgramEffectOp.AddDamageMarkerBudgetTargets) return true;
        return _players[c.OwnerSeat].Markers.GetValueOrDefault(e.Marker!.Value) > 0 &&
            TryGetDesignatedExtraTargetUse(c.OwnerSeat, context, out var use, out var window) && DamageBudgetCard(use.Action!) &&
            DesignatedExtraTargetCandidateMatches(c, context, window) && use.DamageTargetBudget is null;
    }
    private SkillProgramStepOutcome ApplyDamageTargetBudget(SkillProgramEffect e, ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (e.Op == SkillProgramEffectOp.GainDamageOccurrenceTargetBudget)
        {
            var c = f.WindowContext ?? throw new InvalidOperationException("A damage budget lost its native occurrence.");
            var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
            if (f.InstructionIndex != 1 || c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.OccurrenceIndex != 0 || c.Amount <= 0 ||
                c.SourceSeat != f.OwnerSeat && c.TargetSeat != f.OwnerSeat || index < 1 ||
                _resolutionStack[index - 1] is not DamageTriggerWindowFrame w || w.Id != c.ParentFrameId || w.ParentFrameId != c.DamageFrameId || w.TriggerWindow != c.Window ||
                w.TargetSeat != c.TargetSeat || (GetDamageTriggerAttack(w).IsSourceLess ? c.SourceSeat is not null : w.SourceSeat != c.SourceSeat) ||
                w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) ||
                _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == w.ParentFrameId) is not { } damage || damage.Amount != c.Amount || damage.TargetSeat != c.TargetSeat ||
                CompleteProgramEventHistory().OfType<DamageTargetBudgetGainedEvent>().Any(x => x.DamageWindowFrameId == w.Id && x.Source == BudgetSource(f)))
                throw new InvalidOperationException("A damage budget requires one exact applied damage OR-participant occurrence.");
            MutateParticipantMarker(f, f.OwnerSeat, e.Marker!.Value, 1);
            AdvanceEventRulesAndQueueFact(new DamageTargetBudgetGainedEvent(f.Id, w.Id, BudgetSource(f), e.Marker.Value,
                c.SourceSeat, c.TargetSeat!.Value, c.Amount, _players[f.OwnerSeat].Markers.GetValueOrDefault(e.Marker.Value)));
            return SkillProgramStepOutcome.Continue;
        }
        var use = RequireDamageBudgetUse(f, out var parent);
        if (use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0)
        {
            var targets = Array.AsReadOnly(new[] { use.SourceSeat });
            var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
            use = RequireDamageBudgetUse(f, out parent);
        }
        var r = new DamageTargetBudgetReceipt { ProgramFrameId = f.Id, WindowFrameId = parent.Id, CardUseFrameId = use.Id,
            ActionId = use.Action!.ActionId, Source = BudgetSource(f), GameplayHash = f.GameplayHash, Marker = e.Marker!.Value,
            FrozenBudget = _players[f.OwnerSeat].Markers.GetValueOrDefault(e.Marker.Value), OriginalTargets = use.TargetSeats };
        r = r with { Choices = BuildDamageBudgetChoices(f, use, r) };
        ReplaceRuntimeTop(f = f with { DamageTargetBudget = r });
        UpdateLifecycleCardUse(use.Id, u => u with { DamageTargetBudget = r with { Choices = [] } });
        AdvanceEventRulesAndQueueFact(new DamageTargetBudgetOfferedEvent(f.Id, r));
        return ExposeDamageBudgetChoice(f);
    }
    private CardUseFrame RequireDamageBudgetUse(ProgramSkillFrame f, out ProgramCardTriggerWindowFrame window)
    {
        if (f.TriggerId is not { } trigger || f.InstructionIndex != 1 || f.WindowContext is not { } c ||
            !TryGetDesignatedExtraTargetUse(f.OwnerSeat, c, out var use, out window) || !DamageBudgetCard(use.Action!) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, trigger, f.SkillInstanceId, f.GameplayHash, 0), c, window))
            throw new InvalidOperationException("A marker target budget lost its actual actor, native Use or exact designation candidate.");
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("A marker target budget requires its immediate typed designation parent.");
        return use;
    }
    private IReadOnlyList<PromptChoice> BuildDamageBudgetChoices(ProgramSkillFrame f, CardUseFrame use, DamageTargetBudgetReceipt r)
    {
        var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        Dictionary<string,string> Params(string branch) => new() { ["program-action"] = "damage-target-budget", ["branch"] = branch,
            ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["action-id"] = r.ActionId.ToString(CultureInfo.InvariantCulture),
            ["selection-index"] = (r.AddedTargets.Count / pairSize).ToString(CultureInfo.InvariantCulture) };
        var choices = new List<PromptChoice>();
        if (r.AddedTargets.Count / pairSize < r.FrozenBudget)
            foreach (var target in _players.Where(p => IsSlashCard(use.CardKind) ? CanBeRecipientCategorySlashTarget(use, p) : CanBeUniqueLeaderTrickTarget(use, p)).OrderBy(p => p.Seat))
            {
                if (pairSize == 2)
                    foreach (var victim in _players.Where(v => IsLegalBorrowedSwordSlashTarget(target, v)).OrderBy(p => p.Seat))
                        choices.Add(new(new($"damage-budget.{f.Id}.{r.AddedTargets.Count}.add.{target.Seat}.{victim.Seat}"),
                            $"追加：{target.Name} 对 {victim.Name} 使用杀", [], [target.Seat, victim.Seat], Params("add")));
                else choices.Add(new(new($"damage-budget.{f.Id}.{r.AddedTargets.Count}.add.{target.Seat}"), $"追加目标：{target.Name}", [], [target.Seat], Params("add")));
            }
        choices.Add(new(new($"damage-budget.{f.Id}.{r.AddedTargets.Count}.done"), "完成选择", [], [], Params("done")));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private SkillProgramStepOutcome ExposeDamageBudgetChoice(ProgramSkillFrame f)
    {
        var r = f.DamageTargetBudget!; var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"可额外指定至多 {r.FrozenBudget} 个目标；已追加 {r.AddedTargets.Count / (LifecycleCardUse(r.CardUseFrameId)!.CardKind == CardKind.BorrowedSword ? 2 : 1)} 个。", [],
            r.Choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = f.OwnerSeat, Choices = r.Choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState(); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveDamageBudgetChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No owning budget choice.");
        AssertDamageTargetBudgetReceipt(f);
        var use = RequireDamageBudgetUse(f, out _); var r = f.DamageTargetBudget!;
        if (!AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !BuildDamageBudgetChoices(f, use, r).Any(c => AssistedChoicesEqual([choice], [c])))
            throw new InvalidOperationException("A budget answer changed its current legal frozen selection.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "add")
        {
            var targets = Array.AsReadOnly(use.TargetSeats.Concat(choice.Targets).ToArray());
            var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, TargetsAdjusted = true,
                Enhancements = use.Enhancements | CurrentCardEnhancement.ExtraTarget }, action);
            r = r with { AddedTargets = r.AddedTargets.Concat(choice.Targets).ToArray() };
            use = LifecycleCardUse(use.Id)!;
            r = r with { Choices = BuildDamageBudgetChoices(f, use, r) };
            ReplaceRuntimeTop(f = f with { DamageTargetBudget = r });
            UpdateLifecycleCardUse(use.Id, u => u with { DamageTargetBudget = r with { Choices = [] } });
            ExposeDamageBudgetChoice(f); return;
        }
        r = r with { Completed = true, Choices = [] };
        UpdateLifecycleCardUse(use.Id, u => u with { DamageTargetBudget = r });
        AdvanceEventRulesAndQueueFact(new DamageTargetBudgetResolvedEvent(f.Id, r, use.TargetSeats));
        foreach (var target in r.AddedTargets.Where((_, index) => use.CardKind != CardKind.BorrowedSword || index % 2 == 0))
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, target));
        RefreshCurrentUseAddedTargetCandidates(use, r.AddedTargets.Where((_, index) => use.CardKind != CardKind.BorrowedSword || index % 2 == 0).ToArray());
        ReplaceRuntimeTop(f with { DamageTargetBudget = null }); AdvanceRuntimeProgram(f.Id);
    }
    private void SettleDamageMarkerBudgetTargets(long id)
    {
        var use = LifecycleCardUse(id);
        if (use?.DamageTargetBudget is not { Completed: true, Settled: false } r) return;
        if (use.Action is not { } action || action.ActionId != r.ActionId || !IsDamageBudgetResolvedFact(use,
                CompleteProgramEventHistory().OfType<DamageTargetBudgetResolvedEvent>().Single(x => x.ProgramFrameId == r.ProgramFrameId), r.OriginalTargets, r.Source.OwnerSeat))
            throw new InvalidOperationException("A final designation budget lost its issued completed actual Use.");
        var count = action.EffectiveDesignatedTargetSeats.Distinct().Count();
        var currentMarkers = _players[r.Source.OwnerSeat].Markers.GetValueOrDefault(r.Marker);
        // Selection entitlement is frozen when it is offered. The text's later
        // "half the marks" observes the actual marks at the final designation,
        // including intervening genuine damage or a nested actual card Use.
        var removed = _players[r.Source.OwnerSeat].IsAlive && count < currentMarkers ? currentMarkers / 2 + currentMarkers % 2 : 0;
        // This is an issued designation effect. Losing the skill after choosing
        // does not reissue it or abandon its final-use debit.
        if (removed > 0)
            PayProgramMarkerCost(_players[r.Source.OwnerSeat], new(r.Marker, removed), r.Source.SkillId, r.Source.BindingId);
        UpdateLifecycleCardUse(id, u => u with { DamageTargetBudget = r with { Settled = true } });
        AdvanceEventRulesAndQueueFact(new DamageTargetBudgetSettledEvent(id, action.ActionId, r.Source, r.Marker, r.FrozenBudget, currentMarkers,
            count, removed, _players[r.Source.OwnerSeat].Markers.GetValueOrDefault(r.Marker)));
    }
    private void AssertDamageTargetBudgetReceipt(ProgramSkillFrame f)
    {
        if (f.DamageTargetBudget is not { } r) return;
        var use = RequireDamageBudgetUse(f, out var w); var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        var offered = CompleteProgramEventHistory().OfType<DamageTargetBudgetOfferedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.ProgramFrameId != f.Id || r.WindowFrameId != w.Id || r.CardUseFrameId != use.Id || r.ActionId != use.Action!.ActionId ||
            r.Source != BudgetSource(f) || r.GameplayHash != f.GameplayHash || r.FrozenBudget <= 0 || r.Completed || r.Settled ||
            r.AddedTargets.Count % pairSize != 0 || r.AddedTargets.Count / pairSize > r.FrozenBudget ||
            !use.TargetSeats.SequenceEqual(r.OriginalTargets.Concat(r.AddedTargets)) || use.DamageTargetBudget is not { } issued ||
            issued.Source != r.Source || issued.FrozenBudget != r.FrozenBudget || !issued.AddedTargets.SequenceEqual(r.AddedTargets) ||
            offered is not [var initial] || initial.Receipt.Source != r.Source || initial.Receipt.FrozenBudget != r.FrozenBudget ||
            !initial.Receipt.OriginalTargets.SequenceEqual(r.OriginalTargets) ||
            !AssistedChoicesEqual(r.Choices, BuildDamageBudgetChoices(f, use, r)) || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } p ||
            p.PlayerSeat != f.OwnerSeat || p.SourceSeat != f.OwnerSeat || p.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(p.Choices, r.Choices))
            throw new InvalidOperationException("A damage budget lost its owning frozen X, paid actual Use, ordered pairs or published choice.");
    }
    private PromptChoice SelectAiDamageBudgetChoice(PendingDecision p, ProgramSkillFrame f)
    {
        var use = RequireDamageBudgetUse(f, out _);
        foreach (var c in p.Choices.Where(c => c.Parameters["branch"] == "add"))
        {
            var target = _players[c.Targets[use.CardKind == CardKind.BorrowedSword ? 1 : 0]];
            var beneficial = use.CardKind is CardKind.DrawTwo or CardKind.FiveGrains or CardKind.PeachGarden ||
                use.CardKind == CardKind.IronChain && target.IsChained;
            if (beneficial == AreProgramDistributionAllies(_players[f.OwnerSeat], target))
                return c;
        }
        return p.Choices.Single(c => c.Parameters["branch"] == "done");
    }
    private bool IsDamageBudgetResolvedFact(CardUseFrame use, DamageTargetBudgetResolvedEvent fact, IReadOnlyList<int> before, int? currentProofActor = null)
    {
        var r = fact.Receipt; var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        if (use.Action is not { } a || fact.ProgramFrameId != r.ProgramFrameId || r.CardUseFrameId != use.Id || r.ActionId != a.ActionId ||
            r.Source.OwnerSeat != (currentProofActor ?? a.ActorSeat) || !r.Completed || r.FrozenBudget <= 0 || r.AddedTargets.Count % pairSize != 0 ||
            r.AddedTargets.Count / pairSize > r.FrozenBudget || !r.OriginalTargets.SequenceEqual(before) ||
            !fact.ResultTargets.SequenceEqual(before.Concat(r.AddedTargets)) ||
            r.OriginalTargets.Any(s => !IsValidPlayerSeat(s)) || r.AddedTargets.Any(s => !IsValidPlayerSeat(s)) ||
            r.AddedTargets.Where((_, i) => i % pairSize == 0).Distinct().Count() != r.AddedTargets.Count / pairSize ||
            r.AddedTargets.Where((_, i) => i % pairSize == 0).Any(s => r.OriginalTargets.Where((_, i) => i % pairSize == 0).Contains(s)) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } program || program.GameplayHash != r.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == r.Source.BindingId)?.Effects is not [{ Op: SkillProgramEffectOp.AddDamageMarkerBudgetTargets } e] || e.Marker != r.Marker)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<DamageTargetBudgetResolvedEvent>().Count(x => x.ProgramFrameId == r.ProgramFrameId) == 1 &&
            history.OfType<DamageTargetBudgetOfferedEvent>().Count(x => x.ProgramFrameId == r.ProgramFrameId && x.Receipt.Source == r.Source &&
                x.Receipt.FrozenBudget == r.FrozenBudget && x.Receipt.OriginalTargets.SequenceEqual(before)) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(x => x.FrameId == r.ProgramFrameId && x.OwnerSeat == r.Source.OwnerSeat &&
                x.SkillId == r.Source.SkillId && x.BindingId == r.Source.BindingId && x.SkillInstanceId == r.Source.SkillInstanceId &&
                x.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1;
    }
    private bool HasDamageTargetBudgetTail(CardUseFrame use) => use.DamageTargetBudget is { Completed: true, AddedTargets.Count: > 0 } r &&
        CompleteProgramEventHistory().OfType<DamageTargetBudgetResolvedEvent>().Any(x => x.ProgramFrameId == r.ProgramFrameId &&
            IsDamageBudgetResolvedFact(use, x, r.OriginalTargets, r.Source.OwnerSeat));
    private sealed partial class ProgramSkillHost : IDamageTargetBudgetHost
    { public SkillProgramStepOutcome ApplyDamageTargetBudget(SkillProgramEffect e, ProgramSkillFrame f) => engine.ApplyDamageTargetBudget(e, f); }
}
