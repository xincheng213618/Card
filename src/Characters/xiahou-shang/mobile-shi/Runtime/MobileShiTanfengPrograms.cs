namespace CardGame.Core;

public sealed record MobileShiTanfengTargetSelectedEvent(long ProgramFrameId, string SkillInstanceId,
    string TargetRole, int TargetSeat, string Mode) : IGameEvent;

internal interface IMobileShiTanfengHost
{
    SkillProgramStepOutcome SelectTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// Only the two independent target selections need an adapter. The preselected
// options, native card payments, damage and phase debts remain ordinary nodes.
internal sealed class ResolveMobileShiXiahouShangTanfengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiXiahouShangTanfeng;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiTanfengHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "stateId", "condition");
        var role = r.RequiredIdentifier("stateId");
        var condition = r.Condition();
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            r.RequiredIdentifier("sourceBind") != "tanfeng-mode" || role is not ("initial-target" or "fire-target") ||
            role == "initial-target" && (condition.Kind != SkillProgramConditionKind.Any || condition.Children.Count != 4 ||
                !condition.Children.Select(item => item.OptionId).Order(StringComparer.Ordinal).SequenceEqual(new[] { "both", "cancel", "discard", "fire" }) ||
                condition.Children.Any(item => item.Kind != SkillProgramConditionKind.ChoiceIs || item.SourceBind != "tanfeng-mode")) ||
            role == "fire-target" && (condition.Kind != SkillProgramConditionKind.ChoiceIs ||
                condition.SourceBind != "tanfeng-mode" || condition.OptionId != "both"))
            throw new InvalidOperationException(r.Path + ": Tanfeng targets require the original named four-option choice and its exact second-target branch.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, condition, sourceBind: "tanfeng-mode", stateId: role);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.StateId == "initial-target"
            ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),
                new RequireChoiceOptions("tanfeng-mode", ["discard", "fire", "both", "cancel"]), new SelectSingleTarget()]
            : [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),
                new RequireChoiceOptions("tanfeng-mode", ["discard", "fire", "both", "cancel"]), new ReadSelectedTarget(), new ReplaceSingleTarget()];
}

internal sealed class MobileShiTanfengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiXiahouShangTanfeng;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiTanfengHost>().SelectTarget(frame, effect);
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class MobileShiTanfengCapability(GameEngine engine) : IMobileShiTanfengHost
    {
        public SkillProgramStepOutcome SelectTarget(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.SelectMobileShiTanfengTarget(frame, effect);
    }

    private string MobileShiTanfengMode(ProgramSkillFrame frame) =>
        frame.ChoiceBindings.Single(binding => binding.Name == "tanfeng-mode").OptionId;

    private SkillProgramStepOutcome SelectMobileShiTanfengTarget(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var mode = MobileShiTanfengMode(frame);
        if (effect.StateId == "initial-target" && mode == "cancel")
        {
            ReplaceRuntimeTop(frame with { SelectedTargetSeats = [] });
            return SkillProgramStepOutcome.Continue;
        }
        if (mode is not ("discard" or "fire" or "both") || effect.StateId == "fire-target" && mode != "both")
            throw new InvalidOperationException("Tanfeng lost its original preselected branch.");
        var stage = effect.StateId == "fire-target" || mode == "fire" ? "火焰伤害" : "弃置牌";
        var choices = _players.Where(player => player.IsAlive).Select(player => new PromptChoice(
            new ChoiceId($"shi-tanfeng.frame-{frame.Id}.{effect.StateId}.seat-{player.Seat}"),
            $"选择 {player.Name}（{stage}）。", [], [player.Seat], new Dictionary<string, string>
            {
                ["program-action"] = "shi-tanfeng-target", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["target-role"] = effect.StateId!
            })).ToArray();
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, $"【探锋】请选择{stage}的目标（可以选择自己）。",
            [], choices.SelectMany(choice => choice.Targets).ToArray(), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), TargetSeat = frame.OwnerSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"探锋 · {stage}目标", skill.Description), Choices = Array.AsReadOnly(choices)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("shi-tanfeng-target")]
    private void ResolveMobileShiTanfengTarget(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Tanfeng target lost its original frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ResolveMobileShiXiahouShangTanfeng || frame.SkillId != "mobile:shi-tanfeng" ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("target-role") != effect.StateId || selected.Cards.Count != 0 ||
            selected.Targets is not [var target] || !IsValidPlayerSeat(target) || !_players[target].IsAlive ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            MobileShiTanfengMode(frame) is not ("discard" or "fire" or "both") ||
            effect.StateId == "fire-target" && MobileShiTanfengMode(frame) != "both")
            throw new InvalidOperationException("Tanfeng target changed its issued branch, live participant or original frame.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { target }) });
        AdvanceEventRulesAndQueueFact(new MobileShiTanfengTargetSelectedEvent(frame.Id, frame.SkillInstanceId,
            effect.StateId!, target, MobileShiTanfengMode(frame)));
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool HasExactMobileShiTanfengParticipantSelection(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "mobile:shi-tanfeng" || frame.TriggerId != "preselect-options" || frame.ActivationId != frame.TriggerId ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats is not [var target] || !IsValidPlayerSeat(target) ||
            frame.InstructionIndex is < 3 or > 9 || frame.OwnerSeat != _currentSeat ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            context.OwnerSeat != frame.OwnerSeat || context.SourceSeat != frame.OwnerSeat || context.TargetSeat != frame.OwnerSeat)
            return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.OwnerSeat != frame.OwnerSeat || parent.Window != context.Window ||
            parent.Continuation != ProgramLifecycleContinuation.NormalTurnStart || parent.CandidateIndex < 0 ||
            parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] is not { } candidate ||
            candidate.SkillId != frame.SkillId || candidate.BindingId != frame.TriggerId || candidate.SkillInstanceId != frame.SkillInstanceId ||
            candidate.OwnerSeat != frame.OwnerSeat || candidate.GameplayHash != frame.GameplayHash || candidate.OccurrenceIndex != context.OccurrenceIndex)
            return false;
        var mode = frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == "tanfeng-mode");
        if (mode is null || mode.ChooserSeat != frame.OwnerSeat || mode.OptionId is not ("discard" or "fire" or "both")) return false;
        var selections = ProgramEventHistory<MobileShiTanfengTargetSelectedEvent>().Where(item => item.ProgramFrameId == frame.Id).ToArray();
        return selections.Length is >= 1 and <= 2 && selections[0].TargetRole == "initial-target" &&
            selections.All(item => item.SkillInstanceId == frame.SkillInstanceId && item.Mode == mode.OptionId && IsValidPlayerSeat(item.TargetSeat)) &&
            selections[^1].TargetSeat == target && (selections.Length == 1 ||
                mode.OptionId == "both" && selections[1].TargetRole == "fire-target" && frame.InstructionIndex >= 8);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47200)]
    private void AssertMobileShiTanfengTarget(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "mobile:shi-tanfeng") return;
        if (frame.SelectedTargetSeats.Count > 0 && !HasExactMobileShiTanfengParticipantSelection(frame))
            throw new InvalidOperationException("Tanfeng lost its issued participant, original preselection or actual preparation parent.");
        var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (paused.Op != SkillProgramEffectOp.ResolveMobileShiXiahouShangTanfeng || _resolutionStack.LastOrDefault()?.Id != frame.Id) return;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.Choices.Count != _players.Count(player => player.IsAlive) ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") != "shi-tanfeng-target" ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("target-role") != paused.StateId || choice.Cards.Count != 0 ||
                choice.Targets is not [var target] || !IsValidPlayerSeat(target) || !_players[target].IsAlive) ||
            prompt.Choices.Select(choice => choice.Targets[0]).Distinct().Count() != prompt.Choices.Count)
            throw new InvalidOperationException("Tanfeng lost its exact original target prompt.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiXiahouShangTanfeng)]
    private PromptChoice SelectMobileShiTanfengAiTarget(PendingDecision decision) =>
        SelectAiProgramTargets(decision, SkillProgramTargetAiOrder.HostileThenHandCount);
}
