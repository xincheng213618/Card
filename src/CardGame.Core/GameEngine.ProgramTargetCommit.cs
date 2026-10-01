namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IProgramTargetCommitHost
    {
        public SkillProgramStepOutcome SelectRelativeZoneDemandTarget(SkillProgramEffect effect, ProgramSkillFrame frame) => engine.BeginRelativeZoneDemand(effect, frame);
        public SkillProgramStepOutcome DrawOnFirstProgramTargetEncounter(SkillProgramEffect effect, ProgramSkillFrame frame) => engine.DrawFirstProgramTargetReward(effect, frame);
    }
    private SkillProgramAiHint EstimateProgramTargetReward(CharacterState owner, SkillProgram program,
        SkillProgramActivation activation, SkillProgramAiHint hint)
    {
        if (!activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectRelativeZoneDemandTarget)) return hint;
        var hand = GetHand(owner).Count; var equipment = GetEquipment(owner).Count;
        var possible = _players.Where(target => target.IsAlive && target.Seat != owner.Seat &&
            (GetHand(target).Count > hand - (hand > 0 ? 1 : 0) || GetEquipment(target).Count > equipment - (equipment > 0 ? 1 : 0))).ToArray();
        if (possible.Length == 0) return hint with { ValueAdjustment = hint.ValueAdjustment - 20d };
        var prior = CompleteProgramEventHistory().OfType<ProgramTargetCommittedEvent>().Select(item => item.Declaration)
            .Where(item => item.OwnerSeat == owner.Seat && item.SourceProgramId == program.Id && item.SourceActivationId == activation.Id)
            .Select(item => item.TargetSeat).ToHashSet();
        if (possible.All(target => prior.Contains(target.Seat))) return hint;
        var facts = CaptureProgramTriggerFacts(owner);
        var reward = CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.ProgramTargetCommitted, facts)
            .Where(candidate => EnabledSkillPrograms(owner).Any(item => item.Id == candidate.SkillId))
            .SelectMany(candidate => GetProgramTrigger(candidate).Effects)
            .Where(effect => effect.Op == SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter &&
                effect.SkillIds.Single() == program.Id && effect.StateId == activation.Id).Sum(effect => effect.Amount);
        // Only public relative counts, declaration history and the owner's enabled grants enter this estimate.
        return hint with { ValueAdjustment = hint.ValueAdjustment + reward * 8d };
    }
    private bool ProgramTargetSourceValid(ProgramSkillFrame frame) => _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) &&
        EnabledSkillPrograms(_players[frame.OwnerSeat]).Any(item => item.Id == frame.SkillId);

    private SkillProgramStepOutcome BeginRelativeZoneDemand(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var paid = plan.GetInstruction(frame.InstructionIndex - 2).Effect;
        var captured = GetProgramCardSet(frame, effect.SourceBind!);
        if (frame.TriggerId is not null || frame.InstructionIndex != 3 || paid is not
            { Op: SkillProgramEffectOp.MoveBoundCards, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true } ||
            paid.SourceBind != effect.SourceBind || captured.CardIds.Count != 1 || captured.SourceLocations.Single() is not
            { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: { } costOwner } || costOwner != frame.OwnerSeat ||
            !CompleteProgramEventHistory().SkipWhile(item => item is not ProgramSkillStartedEvent started || started.FrameId != frame.Id).OfType<CardMovedEvent>().Any(move => move.CardId == captured.CardIds[0] &&
                move.From == captured.SourceLocations[0] && move.To == CardLocation.DiscardPile &&
                move.Reason.Value == $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}"))
            throw new InvalidOperationException("A relative-zone declaration requires its real, completed own HE discard parent.");
        if (!ProgramTargetSourceValid(frame))
        { CancelProgramBindingAndCleanup(frame, "目标声明的技能实例已失效。"); return SkillProgramStepOutcome.AwaitChild; }
        var owner = _players[frame.OwnerSeat];
        var candidates = _players.Where(target => target.IsAlive && target.Seat != owner.Seat)
            .SelectMany(target => new[] { "hand", "equipment" }.Where(mode => mode == "hand"
                ? GetHand(target).Count > GetHand(owner).Count : GetEquipment(target).Count > GetEquipment(owner).Count)
                .Select(mode => new ProgramRelativeZoneCandidate(target.Seat, mode))).ToArray();
        if (candidates.Length == 0)
        { FinishProgramSkill(frame, completed: true); return SkillProgramStepOutcome.AwaitChild; }
        frame = frame with { RelativeZoneDemand = new(effect.ResultBind!, Array.AsReadOnly(candidates)) };
        ReplaceRuntimeTop(frame);
        PublishRelativeZoneTarget(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> RelativeZoneChoices(ProgramSkillFrame frame) => frame.RelativeZoneDemand!.Candidates.Select(candidate =>
        new PromptChoice(new($"relative-zone.{frame.Id}.{candidate.Mode}.{candidate.TargetSeat}"),
            candidate.Mode == "hand" ? $"令 {_players[candidate.TargetSeat].Name} 交给你一张手牌或装备牌" : $"令 {_players[candidate.TargetSeat].Name} 弃置一张装备牌",
            [], [candidate.TargetSeat], new Dictionary<string, string> { ["program-action"] = "relative-zone-target", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["mode"] = candidate.Mode })).ToArray();
    private void PublishRelativeZoneTarget(ProgramSkillFrame frame)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "弃牌已结算，选择当前牌数严格大于你的角色及效果。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = frame.OwnerSeat, Choices = RelativeZoneChoices(frame), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveRelativeZoneTarget(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing real relative-zone parent.");
        var draft = frame.RelativeZoneDemand ?? throw new InvalidOperationException("Missing relative-zone selection.");
        if (draft.Commit is not null || !AssistedChoicesEqual([selected], RelativeZoneChoices(frame).Where(choice => choice.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The relative-zone declaration no longer matches its published parent.");
        ClearPendingDecision();
        if (!ProgramTargetSourceValid(frame)) { CancelProgramBindingAndCleanup(frame, "目标声明的技能实例已失效。"); return; }
        var target = selected.Targets.Single(); var mode = selected.Parameters["mode"];
        if (!_players[target].IsAlive) { FinishProgramSkill(frame, completed: false); return; }
        var history = CompleteProgramEventHistory().OfType<ProgramTargetCommittedEvent>().Select(item => item.Declaration)
            .Where(item => item.OwnerSeat == frame.OwnerSeat && item.SourceProgramId == frame.SkillId && item.SourceActivationId == frame.ActivationId).ToArray();
        var commit = new ProgramTargetCommitContext(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, frame.SkillInstanceId,
            target, mode, history.Length + 1, !history.Any(item => item.TargetSeat == target));
        frame = frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { target }), ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new(draft.ResultBind, mode, frame.OwnerSeat)).ToArray()), RelativeZoneDemand = draft with { Commit = commit } };
        ReplaceRuntimeTop(frame);
        // Record before reward eligibility: suppression and later loss/regrant do not erase an encounter.
        AdvanceEventRulesAndQueueFact(new ProgramTargetCommittedEvent(commit));
        var facts = CaptureProgramTriggerFacts(_players[frame.OwnerSeat]);
        var candidates = CollectEligibleProgramTriggerCandidates(_players[frame.OwnerSeat], SkillProgramTriggerWindow.ProgramTargetCommitted, facts);
        if (candidates.Count == 0) { AdvanceRuntimeProgram(frame.Id); return; }
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, frame.OwnerSeat,
            SkillProgramTriggerWindow.ProgramTargetCommitted, candidates, ProgramLifecycleContinuation.ResumeParentProgram, facts)
        { ResumeProgramFrameId = frame.Id, ProgramTarget = commit });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }
    private bool TryResumeProgramTargetCommit(long id)
    {
        var frame = GetActiveProgramFrame(id);
        if (frame.RelativeZoneDemand?.Commit is null) return false;
        if (!ProgramTargetSourceValid(frame)) { CancelProgramBindingAndCleanup(frame, "目标声明后技能实例或拥有者已失效。"); return true; }
        ReplaceRuntimeTop(frame with { RelativeZoneDemand = null });
        return false;
    }
    private bool IsProgramTargetContextValid(ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.ProgramTargetCommitted || context.ProgramTarget is not { } commit ||
            context.OwnerSeat != commit.OwnerSeat || context.SourceSeat != commit.OwnerSeat || context.TargetSeat != commit.TargetSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(item => item.Id == context.ParentFrameId) is not { } window ||
            window.Id != context.ParentFrameId || window.Window != context.Window || window.ProgramTarget != commit ||
            window.Continuation != ProgramLifecycleContinuation.ResumeParentProgram || window.ResumeProgramFrameId != commit.ParentProgramFrameId ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(item => item.Id == commit.ParentProgramFrameId) is not { } parent ||
            parent.SkillId != commit.SourceProgramId || parent.ActivationId != commit.SourceActivationId || parent.SkillInstanceId != commit.SourceSkillInstanceId ||
            parent.OwnerSeat != commit.OwnerSeat || parent.RelativeZoneDemand?.Commit != commit || parent.InstructionIndex != 3 ||
            !parent.SelectedTargetSeats.SequenceEqual([commit.TargetSeat])) return false;
        var declarations = CompleteProgramEventHistory().OfType<ProgramTargetCommittedEvent>().Select(item => item.Declaration)
            .Where(item => item.OwnerSeat == commit.OwnerSeat && item.SourceProgramId == commit.SourceProgramId && item.SourceActivationId == commit.SourceActivationId).ToArray();
        return declarations.Count(item => item == commit) == 1 && declarations.Count(item => item.DeclarationOrdinal < commit.DeclarationOrdinal) == commit.DeclarationOrdinal - 1 &&
            commit.FirstEncounter == !declarations.Where(item => item.DeclarationOrdinal < commit.DeclarationOrdinal).Any(item => item.TargetSeat == commit.TargetSeat);
    }
    private SkillProgramStepOutcome DrawFirstProgramTargetReward(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var context = frame.WindowContext ?? throw new InvalidOperationException("First-target reward requires an actual declaration window.");
        if (!IsProgramTargetContextValid(context)) throw new InvalidOperationException("First-target reward lost its exact real parent and declaration ordinal.");
        var commit = context.ProgramTarget!;
        if (!commit.FirstEncounter || commit.SourceProgramId != effect.SkillIds.Single() || commit.SourceActivationId != effect.StateId ||
            !ProgramTargetSourceValid(frame)) return SkillProgramStepOutcome.Continue;
        DrawProgramCards(frame.Id, frame.OwnerSeat, effect.Amount, null, null, SkillProgramCardSetVisibility.Private, new("program.first-target.reward"));
        return AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
    }
    private void AssertProgramTargetDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.WindowContext?.Window == SkillProgramTriggerWindow.ProgramTargetCommitted && !IsProgramTargetContextValid(frame.WindowContext))
            throw new InvalidOperationException("Program-target trigger lost its exact declaration parent.");
        if (frame.RelativeZoneDemand is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.SelectRelativeZoneDemandTarget || frame.InstructionIndex != 3 || paused.ResultBind != draft.ResultBind ||
            draft.Candidates.Count == 0 || draft.Candidates.Distinct().Count() != draft.Candidates.Count ||
            draft.Candidates.Any(item => !IsValidPlayerSeat(item.TargetSeat) || item.TargetSeat == frame.OwnerSeat || item.Mode is not ("hand" or "equipment")) ||
            draft.Commit is { } commit && (commit.ParentProgramFrameId != frame.Id || commit.OwnerSeat != frame.OwnerSeat ||
                !draft.Candidates.Contains(new(commit.TargetSeat, commit.Mode)) || !frame.SelectedTargetSeats.SequenceEqual([commit.TargetSeat]) ||
                !frame.ChoiceBindings.Contains(new(draft.ResultBind, commit.Mode, frame.OwnerSeat))))
            throw new InvalidOperationException("Relative-zone demand lost its frozen candidates or declared binding.");
        if (draft.Commit is null && ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != frame.OwnerSeat ||
             !AssistedChoicesEqual(decision.Choices, RelativeZoneChoices(frame))))
            throw new InvalidOperationException("Relative-zone demand lost its matching public choice.");
    }
}
