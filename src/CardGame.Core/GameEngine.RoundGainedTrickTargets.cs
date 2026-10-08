using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<int> RoundGainedOriginalTargets(CardUseFrame use) =>
        use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0 ? Array.AsReadOnly(new[] { use.Action!.ActorSeat }) : use.TargetSeats;

    private bool CanOfferRoundGainedTrickTarget(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget) ||
        ExactIssuedRoundGainedUseCandidate(candidate, context) is { } q &&
        TryGetDesignatedExtraTargetUse(candidate.OwnerSeat, context, out var use, out _) &&
        !CompleteProgramEventHistory().OfType<RoundGainedTrickTargetOfferedEvent>().Any(e => e.Qualification.CardUseFrameId == use.Id &&
            e.Qualification.Source.SkillId == candidate.SkillId && e.Qualification.ActorSeat == candidate.OwnerSeat) &&
        q.CardUseFrameId == use.Id && RoundGainedOriginalTargets(use).Count > 0;

    private RoundGainedUseQualification RequireRoundGainedTrickTargetParent(ProgramSkillFrame frame, out CardUseFrame use)
    {
        use = null!;
        if (frame.TriggerId is not { } trigger || frame.ActivationId != trigger || frame.InstructionIndex != 1 || frame.WindowContext is not { } context ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            ExactIssuedRoundGainedUseCandidate(new(frame.OwnerSeat, frame.SkillId, trigger, frame.SkillInstanceId, frame.GameplayHash, 0), context) is not { } q ||
            !TryGetDesignatedExtraTargetUse(frame.OwnerSeat, context, out use, out var window))
            throw new InvalidOperationException("A round-gained trick target instruction lost its exact qualified actual Use.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat &&
                e.SkillId == frame.SkillId && e.BindingId == trigger && e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) != 1)
            throw new InvalidOperationException("A round-gained trick target requires its immediate native designation parent.");
        return q;
    }

    private IReadOnlyList<PromptChoice> BuildRoundGainedTrickTargetChoices(ProgramSkillFrame frame, CardUseFrame use)
    {
        Dictionary<string, string> Parameters(string branch) => new()
        {
            ["program-action"] = "round-gained-trick-target", ["branch"] = branch,
            ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["card-use-id"] = use.Id.ToString(CultureInfo.InvariantCulture),
            ["action-id"] = use.Action!.ActionId.ToString(CultureInfo.InvariantCulture)
        };
        var original = RoundGainedOriginalTargets(use);
        var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        var primary = original.Where((_, index) => index % pairSize == 0).ToHashSet();
        var choices = new List<PromptChoice>();
        foreach (var target in _players.Where(p => !primary.Contains(p.Seat) && CanBeUniqueLeaderTrickTarget(use, p)).OrderBy(p => p.Seat))
        {
            if (pairSize == 2)
            {
                foreach (var victim in _players.Where(p => IsLegalBorrowedSwordSlashTarget(target, p)).OrderBy(p => p.Seat))
                    choices.Add(new(new($"round-gained-trick-target.{frame.Id}.add.{target.Seat}.{victim.Seat}"),
                        $"增加目标：{target.Name} 对 {victim.Name} 使用杀", [], [target.Seat, victim.Seat], Parameters("add")));
            }
            else choices.Add(new(new($"round-gained-trick-target.{frame.Id}.add.{target.Seat}"), $"增加目标：{target.Name}", [], [target.Seat], Parameters("add")));
        }
        for (var index = 0; index < original.Count; index += pairSize)
            choices.Add(new(new($"round-gained-trick-target.{frame.Id}.remove.{original[index]}"), $"减少目标：{_players[original[index]].Name}", [],
                original.Skip(index).Take(pairSize).ToArray(), Parameters("remove")));
        choices.Add(new(new($"round-gained-trick-target.{frame.Id}.decline"), "不改变目标", [], [], Parameters("decline")));
        return Array.AsReadOnly(choices.Select(DesignatedExtraTargetDraft.FreezeChoice).ToArray());
    }

    private SkillProgramStepOutcome OfferRoundGainedTrickTarget(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var q = RequireRoundGainedTrickTargetParent(frame, out var use);
        if (frame.RoundGainedTrickTargetDraft is not null || CompleteProgramEventHistory().OfType<RoundGainedTrickTargetOfferedEvent>().Any(e =>
                e.Qualification.CardUseFrameId == use.Id && e.Qualification.Source.SkillId == frame.SkillId && e.Qualification.ActorSeat == frame.OwnerSeat))
            throw new InvalidOperationException("A round-gained trick cannot offer the same target adjustment twice.");
        var draft = new RoundGainedTrickTargetDraft(use.Id, use.Action!.ActionId, 1, q)
            { OriginalTargetSeats = RoundGainedOriginalTargets(use), Choices = BuildRoundGainedTrickTargetChoices(frame, use) };
        ReplaceRuntimeTop(frame = frame with { RoundGainedTrickTargetDraft = draft });
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
        AdvanceEventRulesAndQueueFact(new RoundGainedTrickTargetOfferedEvent(frame.Id, 1, source, q, draft.OriginalTargetSeats, draft.Choices));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "可以为此普通锦囊增加或减少一个目标。", [],
            Array.AsReadOnly(draft.Choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = frame.OwnerSeat, Choices = draft.Choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState(); return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveRoundGainedTrickTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The round-gained trick lost its owning choice.");
        AssertRoundGainedTrickTargetDraft(frame);
        var draft = frame.RoundGainedTrickTargetDraft!;
        var choice = draft.Choices.SingleOrDefault(c => c.Id == selected.Id);
        if (choice is null || !AssistedChoicesEqual([choice], [selected])) throw new InvalidOperationException("A round-gained target answer changed its published choice.");
        RequireRoundGainedTrickTargetParent(frame, out var use);
        if (!BuildRoundGainedTrickTargetChoices(frame, use).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("The selected ordinary-trick target adjustment is no longer legal.");
        var branch = choice.Parameters["branch"];
        IReadOnlyList<int> added = branch == "add" ? choice.Targets : Array.AsReadOnly(Array.Empty<int>());
        IReadOnlyList<int> removed = branch == "remove" ? choice.Targets : Array.AsReadOnly(Array.Empty<int>());
        var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        var result = draft.OriginalTargetSeats.ToList();
        if (added.Count > 0) result.AddRange(added);
        if (removed.Count > 0)
        {
            var index = Enumerable.Range(0, result.Count / pairSize).Select(i => i * pairSize).Single(i => result[i] == removed[0]);
            result.RemoveRange(index, pairSize);
        }
        var targets = Array.AsReadOnly(result.ToArray());
        var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, TargetsAdjusted = true,
            Enhancements = added.Count > 0 ? use.Enhancements | CurrentCardEnhancement.ExtraTarget : use.Enhancements }, action);
        if (added.Count > 0) AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, use.Id, added[0]));
        AdvanceEventRulesAndQueueFact(new RoundGainedTrickTargetResolvedEvent(frame.Id, 1,
            new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId), draft.Qualification,
            draft.OriginalTargetSeats, added, removed, targets));
        ClearPendingDecision(); ReplaceRuntimeTop(frame with { RoundGainedTrickTargetDraft = null }); AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertRoundGainedTrickTargetDraft(ProgramSkillFrame frame)
    {
        if (frame.RoundGainedTrickTargetDraft is null && (frame.TriggerId is null ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == frame.TriggerId)?.Effects
                .Any(e => e.Op == SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget) != true)) return;
        var offers = CompleteProgramEventHistory().OfType<RoundGainedTrickTargetOfferedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        var resolved = CompleteProgramEventHistory().OfType<RoundGainedTrickTargetResolvedEvent>().Any(e => e.ProgramFrameId == frame.Id);
        if (frame.RoundGainedTrickTargetDraft is not { } draft)
        { if (offers.Length != 0 && !resolved) throw new InvalidOperationException("A round-gained trick cannot lose its published owning draft."); return; }
        var q = RequireRoundGainedTrickTargetParent(frame, out var use);
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
        if (draft.CardUseFrameId != use.Id || draft.ActionId != use.Action!.ActionId || draft.InstructionIndex != 1 || !SameRoundGainedQualification(draft.Qualification, q) ||
            !draft.OriginalTargetSeats.SequenceEqual(RoundGainedOriginalTargets(use)) || offers is not [var offered] || resolved ||
            offered.Source != source || offered.InstructionIndex != 1 || !SameRoundGainedQualification(offered.Qualification, q) ||
            !offered.OriginalTargetSeats.SequenceEqual(draft.OriginalTargetSeats) || !AssistedChoicesEqual(offered.Choices, draft.Choices) ||
            !AssistedChoicesEqual(draft.Choices, BuildRoundGainedTrickTargetChoices(frame, use)) ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } prompt ||
            prompt.PlayerSeat != frame.OwnerSeat || prompt.SourceSeat != frame.OwnerSeat || prompt.TargetSeat != frame.OwnerSeat ||
            prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.ValidCardIds.Count != 0 || !AssistedChoicesEqual(prompt.Choices, draft.Choices) ||
            !prompt.ValidTargetSeats.SequenceEqual(draft.Choices.SelectMany(c => c.Targets).Distinct()))
            throw new InvalidOperationException("A round-gained trick target draft lost its frozen source, original targets or actual public choices.");
    }

    private PromptChoice SelectAiRoundGainedTrickTargetChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        RequireRoundGainedTrickTargetParent(frame, out var use);
        bool Helpful(int target) => use.CardKind is CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains || use.CardKind == CardKind.IronChain && _players[target].IsChained;
        foreach (var choice in decision.Choices.Where(c => c.Parameters["branch"] != "decline"))
        {
            var help = use.CardKind == CardKind.BorrowedSword ? !AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[choice.Targets[1]]) :
                Helpful(choice.Targets[0]) == AreProgramDistributionAllies(_players[frame.OwnerSeat], _players[choice.Targets[0]]);
            if (choice.Parameters["branch"] == "add" ? help : !help) return choice;
        }
        return decision.Choices.Single(c => c.Parameters["branch"] == "decline");
    }

    private bool IsRoundGainedTrickTargetFact(CardUseFrame use, RoundGainedTrickTargetResolvedEvent fact, IReadOnlyList<int> originalTargets)
    {
        var q = fact.Qualification;
        var pairSize = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        if (use.Action is not { Type: CardActionType.Use } action || !IsOrdinaryTrick(use.CardKind) || !ValidRoundGainedQualification(use, q) ||
            q.CardUseFrameId != use.Id || q.ActionId != action.ActionId || fact.InstructionIndex != 1 || fact.Source.OwnerSeat != q.ActorSeat ||
            fact.Source.SkillId != q.Source.SkillId || fact.Source.SkillInstanceId != q.Source.SkillInstanceId ||
            !fact.OriginalTargetSeats.SequenceEqual(originalTargets) || originalTargets.Count % pairSize != 0 ||
            originalTargets.Any(s => !IsValidPlayerSeat(s)) || originalTargets.Where((_, i) => i % pairSize == 0).Distinct().Count() != originalTargets.Count / pairSize ||
            fact.AddedTargetSeats.Count != 0 && fact.RemovedTargetSeats.Count != 0 ||
            fact.AddedTargetSeats.Count != 0 && fact.AddedTargetSeats.Count != pairSize || fact.RemovedTargetSeats.Count != 0 && fact.RemovedTargetSeats.Count != pairSize ||
            fact.AddedTargetSeats.Concat(fact.RemovedTargetSeats).Any(s => !IsValidPlayerSeat(s)) ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != q.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == fact.Source.BindingId)?.Effects is not [{ Op: SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget }]) return false;
        var expected = originalTargets.ToList();
        if (fact.AddedTargetSeats.Count > 0)
        {
            if (originalTargets.Where((_, i) => i % pairSize == 0).Contains(fact.AddedTargetSeats[0]) || pairSize == 2 && fact.AddedTargetSeats[0] == fact.AddedTargetSeats[1]) return false;
            expected.AddRange(fact.AddedTargetSeats);
        }
        if (fact.RemovedTargetSeats.Count > 0)
        {
            var index = Enumerable.Range(0, originalTargets.Count / pairSize).Select(i => i * pairSize).Where(i => originalTargets.Skip(i).Take(pairSize).SequenceEqual(fact.RemovedTargetSeats)).ToArray();
            if (index is not [var offset]) return false;
            expected.RemoveRange(offset, pairSize);
        }
        if (!fact.ResultTargetSeats.SequenceEqual(expected)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var branch = fact.AddedTargetSeats.Count > 0 ? "add" : fact.RemovedTargetSeats.Count > 0 ? "remove" : "decline";
        var chosenTargets = fact.AddedTargetSeats.Count > 0 ? fact.AddedTargetSeats : fact.RemovedTargetSeats;
        return history.OfType<RoundGainedTrickTargetResolvedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId) == 1 &&
            history.OfType<RoundGainedTrickTargetOfferedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId && e.Source == fact.Source && e.InstructionIndex == 1 &&
                SameRoundGainedQualification(e.Qualification, q) && e.OriginalTargetSeats.SequenceEqual(originalTargets) &&
                e.Choices.Count(c => c.Parameters.TryGetValue("branch", out var b) && b == branch && c.Targets.SequenceEqual(chosenTargets)) == 1) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == fact.Source.OwnerSeat && e.SkillId == fact.Source.SkillId &&
                e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1 &&
            history.OfType<ProgramCardUseTargetAddedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.CardUseFrameId == use.Id && e.OwnerSeat == fact.Source.OwnerSeat &&
                e.SkillId == fact.Source.SkillId && (fact.AddedTargetSeats.Count == 0 || e.TargetSeat == fact.AddedTargetSeats[0])) == (fact.AddedTargetSeats.Count > 0 ? 1 : 0);
    }

    private bool HasRoundGainedTrickTargetTail(CardUseFrame use) => CompleteProgramEventHistory().OfType<RoundGainedTrickTargetResolvedEvent>().Any(e =>
        e.AddedTargetSeats.Count > 0 && IsRoundGainedTrickTargetFact(use, e, e.OriginalTargetSeats) && use.Action!.TargetSeats.Count >= e.ResultTargetSeats.Count &&
        use.Action.TargetSeats.Take(e.ResultTargetSeats.Count).SequenceEqual(e.ResultTargetSeats) && use.TargetSeats.SequenceEqual(use.Action.TargetSeats));
    private bool HasRoundGainedTrickTargetCancellation(CardUseFrame use) => use.TargetSeats.Count == 0 && use.Action is { TargetSeats.Count: 0 } &&
        CompleteProgramEventHistory().OfType<RoundGainedTrickTargetResolvedEvent>().Any(e => e.RemovedTargetSeats.Count > 0 && e.ResultTargetSeats.Count == 0 &&
            IsRoundGainedTrickTargetFact(use, e, e.OriginalTargetSeats));

    private sealed partial class ProgramSkillHost : IRoundGainedSourceUseHost
    {
        public SkillProgramStepOutcome AdjustOneRoundGainedOrdinaryTrickTarget(ProgramSkillFrame frame) => engine.OfferRoundGainedTrickTarget(frame);
        public SkillProgramStepOutcome DrawForRoundGainedEquipmentUse(ProgramSkillFrame frame) => engine.BeginRoundGainedEquipmentDraw(frame);
        public bool CanContinueIssuedRoundGainedUse(ProgramSkillFrame frame) => engine.CanContinueIssuedRoundGainedUse(frame);
    }
}
