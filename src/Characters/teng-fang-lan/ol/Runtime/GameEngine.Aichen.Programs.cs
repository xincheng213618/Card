namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 哀尘 removals are permanent; the removed set is the owner's event history.
    private HashSet<string> AichenRemovedOptions(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramAichenRemovedEvent>()
            .Where(fact => fact.OwnerSeat == ownerSeat)
            .Select(fact => fact.RemovedOptionId)
            .ToHashSet(StringComparer.Ordinal);

    // 哀尘: the locked dying-entry removal; with at most one option left the
    // trigger resolves silently.
    private SkillProgramStepOutcome AichenProgramRemoveOption(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var removed = AichenRemovedOptions(active.OwnerSeat);
        var remaining = LuochongOptionIds.Where(optionId => !removed.Contains(optionId)).ToArray();
        if (remaining.Length <= 1) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = remaining.Select(optionId => new PromptChoice(
                new ChoiceId($"aichen-remove.frame-{frame.Id}.{optionId}"),
                $"移除“落宠”的选项：{LuochongOptionDisplayName(optionId)}。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "aichen-remove",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["option"] = optionId
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】“落宠”选项数大于1，请选择移除其中一项。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 移除选项", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("aichen-remove")]
    private void ResolveAichenRemoveChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Aichen choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.AichenRemoveOption } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Aichen choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("option") is not { } option || !IsLuochongOptionId(option) ||
            AichenRemovedOptions(active.OwnerSeat).Contains(option))
            throw new InvalidOperationException("The Aichen removal option is no longer available.");
        ClearPendingDecision();
        var remainingCount = LuochongOptionIds.Length - AichenRemovedOptions(active.OwnerSeat).Count - 1;
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，移除“落宠”的选项：{LuochongOptionDisplayName(option)}。",
            active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramAichenRemovedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, option, remainingCount));
        AdvanceRuntimeProgram(active.Id);
    }
}
