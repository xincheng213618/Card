using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Presentation hints group exact published choices; each card still commits through
    // its native command and continuation. Never infer new payments or recipients in UI.
    private IReadOnlyList<PromptChoice> PresentCardDistribution(ProgramSkillFrame frame,
        IReadOnlyList<PromptChoice> choices, string stage, int maximum = int.MaxValue,
        Func<PromptChoice, string>? destination = null, Func<PromptChoice, string>? label = null, int? scopeInstruction = null)
    {
        var scope = $"{frame.Id}:{scopeInstruction ?? frame.InstructionIndex}:{stage}";
        return Array.AsReadOnly(choices.Select(choice =>
        {
            var parameters = new Dictionary<string, string>(choice.Parameters)
            {
                ["distribution-scope"] = scope,
                ["distribution-limit"] = maximum.ToString(CultureInfo.InvariantCulture)
            };
            if (choice.Cards.Count == 1 && (destination is not null || choice.Targets.Count == 1))
            {
                if (stage == "public-pile") parameters["native-original-public-pile"] = "true";
                parameters["distribution-target"] = destination?.Invoke(choice) ?? "seat:" + choice.Targets[0];
                parameters["distribution-label"] = label?.Invoke(choice) ?? "交给 " + _players[choice.Targets[0]].Name;
            }
            return choice with { Parameters = parameters };
        }).ToArray());
    }
    private IReadOnlyList<PromptChoice> PresentBoundCardDistribution(ProgramSkillFrame frame,
        IReadOnlyList<PromptChoice> choices, string sourceBind, SkillProgramTargetKind targetKind)
    {
        var instructions = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions;
        bool SameGift(SkillProgramEffect e) => e.Op == SkillProgramEffectOp.GiveBoundCard && e.SourceBind == sourceBind &&
            e.TargetKind == targetKind && e.Condition.Kind == SkillProgramConditionKind.Always;
        var cursor = frame.InstructionIndex - 1;
        var first = cursor;
        var end = cursor + 1;
        if (SameGift(instructions[cursor]))
        {
            while (first > 0 && SameGift(instructions[first - 1])) first--;
            while (end < instructions.Count && SameGift(instructions[end])) end++;
        }
        return PresentCardDistribution(frame, choices, "bound:" + sourceBind, end - cursor, scopeInstruction: first);
    }

}
