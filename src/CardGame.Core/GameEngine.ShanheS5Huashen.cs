namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ShanheS5HuashenCapability(GameEngine engine) : IShanheS5HuashenHost
    {
        public SkillProgramStepOutcome ChooseOrExchangeShanheS5Avatar(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.ChooseOrExchangeShanheS5Avatar(effect, frame);
    }

    private SkillProgramStepOutcome ChooseOrExchangeShanheS5Avatar(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var source = ResolvePrivateGeneralLibrarySource(frame, effect);
        if (source is null || !LibrarySourceQualified(source) || _privateGeneralLibraries.GetValueOrDefault(source) is not { Initialized: true } library)
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { ShanheS5AvatarChoice = new(source, library.Revision, ShanheS5AvatarChoiceStage.Mode) });
        PublishShanheS5AvatarChoice(frame); return SkillProgramStepOutcome.AwaitChoice;
    }

    private PrivateGeneralLibrary? ShanheS5AvatarChoiceLibrary(ProgramSkillFrame frame) =>
        frame.ShanheS5AvatarChoice is { } draft && draft.Source.OwnerSeat == frame.OwnerSeat &&
        draft.Source.CapabilitySkillId == frame.SkillId && draft.Source.CapabilitySkillInstanceId == frame.SkillInstanceId &&
        LibrarySourceQualified(draft.Source) && _privateGeneralLibraries.GetValueOrDefault(draft.Source) is { } library &&
        library.Revision == draft.LibraryRevision ? library : null;

    private IReadOnlyList<PromptChoice> ShanheS5AvatarChoices(ProgramSkillFrame frame)
    {
        if (frame.ShanheS5AvatarChoice is not { } draft || ShanheS5AvatarChoiceLibrary(frame) is not { } library) return [];
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        PromptChoice Choice(string action, string label, string? id = null) => new(
            new($"shanhe-avatar.{frame.Id}.{draft.Stage}.{action}.{id}"), label, [], [],
            new Dictionary<string, string> { ["program-action"] = "shanhe-s5-avatar", ["frame-id"] = frame.Id.ToString(),
                ["action"] = action, ["general-id"] = id ?? "" });
        var choices = new List<PromptChoice>();
        if (draft.Stage == ShanheS5AvatarChoiceStage.Mode)
        {
            if (library.GeneralIds.Any(id => PrivateGeneralDeclarableSkills(id, effect.GeneralLibraryPolicy!).Count > 0))
                choices.Add(Choice("replace", "替换展示的化身及技能"));
            choices.Add(Choice("exchange", "交换至多两张未展示的化身"));
            choices.Add(Choice("finish", "保留当前化身"));
        }
        else
        {
            var maximum = Math.Min(2, PrivateGeneralCandidates().Count());
            foreach (var id in library.GeneralIds.Where(id => id != library.RevealedGeneralId).Order(StringComparer.Ordinal))
            {
                var selected = draft.SelectedGeneralIds.Contains(id, StringComparer.Ordinal);
                if (selected || draft.SelectedGeneralIds.Count < maximum)
                    choices.Add(Choice(selected ? "unselect" : "select", (selected ? "取消移去【" : "移去【") + _contentRegistry.Generals[id].Name + "】", id));
            }
            choices.Add(Choice("finish", $"确认交换{draft.SelectedGeneralIds.Count}张化身"));
            choices.Add(Choice("back", "返回选择方式"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShanheS5AvatarChoice(ProgramSkillFrame frame)
    {
        var choices = ShanheS5AvatarChoices(frame);
        if (choices.Count == 0) { FinishShanheS5AvatarChoice(frame); return; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, skill.Description, [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("shanhe-s5-avatar")]
    private void ResolveShanheS5AvatarChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("S5 avatar selection lost its program frame.");
        if (choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() || !ShanheS5AvatarChoices(frame).Any(c => c.Id == choice.Id))
            throw new InvalidOperationException("Stale S5 avatar selection.");
        var draft = frame.ShanheS5AvatarChoice!; var library = ShanheS5AvatarChoiceLibrary(frame)!;
        ClearPendingDecision(); var action = choice.Parameters["action"];
        if (draft.Stage == ShanheS5AvatarChoiceStage.Mode && action == "replace")
        {
            ReplaceRuntimeTop(frame = frame with { ShanheS5AvatarChoice = null });
            var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex).Effect;
            if (ExecutePrivateGeneralLibrary(effect, frame) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if (action is "select" or "unselect")
        {
            var id = choice.Parameters["general-id"];
            ReplaceRuntimeTop(frame = frame with { ShanheS5AvatarChoice = draft with
            { SelectedGeneralIds = action == "select" ? [.. draft.SelectedGeneralIds, id] : draft.SelectedGeneralIds.Where(value => value != id).ToArray() } });
            PublishShanheS5AvatarChoice(frame); return;
        }
        if (action is "exchange" or "back")
        {
            ReplaceRuntimeTop(frame = frame with { ShanheS5AvatarChoice = draft with
            { Stage = action == "exchange" ? ShanheS5AvatarChoiceStage.Exchange : ShanheS5AvatarChoiceStage.Mode, SelectedGeneralIds = [] } });
            PublishShanheS5AvatarChoice(frame); return;
        }
        if (action != "finish") throw new InvalidOperationException("Invalid S5 avatar action.");
        if (draft.Stage == ShanheS5AvatarChoiceStage.Exchange && draft.SelectedGeneralIds.Count > 0)
        {
            var selected = draft.SelectedGeneralIds;
            if (selected.Count > 2 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Count ||
                selected.Any(id => id == library.RevealedGeneralId || !library.GeneralIds.Contains(id, StringComparer.Ordinal)) ||
                PrivateGeneralCandidates().Take(selected.Count).Count() != selected.Count)
                throw new InvalidOperationException("S5 avatar exchange lost its exact replaceable private cards.");
            var remaining = library.GeneralIds.Where(id => !selected.Contains(id, StringComparer.Ordinal)).ToArray();
            library = library with { GeneralIds = Array.AsReadOnly(remaining), Revision = library.Revision + 1 };
            _privateGeneralLibraries[draft.Source] = library; _privateGeneralLibraryRevision++;
            var drawn = DrawPrivateGeneralAvatars(library, selected.Count, selected);
            AdvanceEventRulesAndQueueFact(new ShanheS5AvatarsExchangedEvent(frame.Id, draft.Source, selected.Count, drawn.GeneralIds.Count - remaining.Length));
        }
        FinishShanheS5AvatarChoice(frame);
    }

    private void FinishShanheS5AvatarChoice(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { ShanheS5AvatarChoice = null }); AdvanceRuntimeProgram(frame.Id); }

    [ProgramContinuation(ProgramContinuationKind.Resume, 59500)]
    private bool ResumeShanheS5AvatarChoice(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShanheS5AvatarChoice: { } } frame || frame.Id != id) return false;
        if (_winner != Winner.None || ShanheS5AvatarChoiceLibrary(frame) is null) { FinishShanheS5AvatarChoice(frame); return true; }
        if (_pendingDecision is null) PublishShanheS5AvatarChoice(frame); return true;
    }
}
