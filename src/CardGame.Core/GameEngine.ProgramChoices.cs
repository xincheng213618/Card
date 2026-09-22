namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A named program choice cannot be answered twice.");
        var chooser = _players[chooserSeat];
        var context = CreateSkillContext(chooser);
        var choices = options.Where(option => option.Condition.Evaluate(context)).Select(option =>
            new PromptChoice(new ChoiceId($"program-option.frame-{frame.Id}.{resultBind}.{option.Id}"),
                option.Label, [], [], new Dictionary<string, string>
                {
                    ["program-action"] = "choose-option",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = resultBind,
                    ["option-id"] = option.Id
                })).ToArray();
        if (!chooser.IsAlive || choices.Length == 0)
        {
            CancelProgramBindingAndCleanup(active, "没有可用的效果选项，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, chooserSeat,
            $"【{skill.Name}】请选择一项。", [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = chooserSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 选择效果", skill.Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = chooser.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramOptionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The named choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var chooserSeat = ResolveProgramEffectTarget(frame, effect.Target);
        if (effect.Op != SkillProgramEffectOp.ChooseOption ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat || selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
            frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind))
            throw new InvalidOperationException("The choice does not match its suspended instruction.");
        var option = effect.Options.SingleOrDefault(item => item.Id == selected.Parameters.GetValueOrDefault("option-id")) ??
            throw new InvalidOperationException("The selected program option is unavailable.");
        ClearPendingDecision();
        if (!_players[chooserSeat].IsAlive || !option.Condition.Evaluate(CreateSkillContext(_players[chooserSeat])) ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(frame, "效果选项或技能实例已失效，技能剩余步骤取消。");
            return;
        }
        _resolutionStack[^1] = frame with
        {
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(effect.ResultBind!, option.Id, chooserSeat)).ToArray())
        };
        QueueGameEvent(new ProgramOptionChosenEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
            frame.OwnerSeat, effect.ResultBind!, option.Id, chooserSeat));
        ContinueProgramSkill(frame.Id);
    }

    private static int ResolveProgramEffectTarget(ProgramSkillFrame frame, SkillProgramEffectTarget target) => target switch
    {
        SkillProgramEffectTarget.Owner => frame.OwnerSeat,
        SkillProgramEffectTarget.SelectedTarget => frame.SelectedTargetSeats.Single(),
        SkillProgramEffectTarget.Actor => frame.WindowContext?.CardUse?.ActorSeat ??
            throw new InvalidOperationException("A program option lost its card-action actor."),
        _ => throw new InvalidOperationException("Unsupported program option chooser.")
    };

    private PromptChoice SelectAiProgramOption(PendingDecision decision, ProgramSkillFrame frame)
    {
        var program = _contentRegistry!.GetSkill(frame.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        var chooser = _players[decision.PlayerSeat];
        var host = new ProgramSkillHost(this);
        return decision.Choices.Select(choice =>
        {
            var answered = frame with { ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(choice.Parameters["result-bind"], choice.Parameters["option-id"], chooser.Seat)).ToArray()) };
            var score = 0d;
            foreach (var effect in plan.Instructions.Skip(frame.InstructionIndex))
            {
                if (!host.EvaluateCondition(answered, effect.Condition, CreateSkillContext(_players[frame.OwnerSeat])) ||
                    ResolveProgramEffectTarget(answered, effect.Target) != chooser.Seat) continue;
                score += effect.Op switch
                {
                    SkillProgramEffectOp.Draw => effect.Amount * 8d,
                    SkillProgramEffectOp.Recover => Math.Min(effect.Amount, chooser.MaxHp - chooser.Hp) * (chooser.Hp <= 1 ? 100d : 18d),
                    SkillProgramEffectOp.SetFaceState when effect.FaceDown != chooser.IsFaceDown => effect.FaceDown == false ? 24d : -24d,
                    SkillProgramEffectOp.SetChainedState when effect.Chained != chooser.IsChained => effect.Chained == false ? 4d : -4d,
                    SkillProgramEffectOp.LoseHp or SkillProgramEffectOp.Damage => -effect.Amount * 22d,
                    _ => 0d
                };
            }
            return (Choice: choice, Score: score);
        }).OrderByDescending(item => item.Score).ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).First().Choice;
    }
}
