namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AdjustProgramNormalDrawByPlannedCount(ProgramSkillFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame active || active.Id != frame.Id ||
            frame.TriggerId is null || _contentRegistry.GetSkill(frame.SkillId).Program is not { } program ||
            program.GameplayHash != frame.GameplayHash || frame.InstructionIndex < 1 ||
            ProgramInstructionResolver.Default.Resolve(frame, program).Instructions.ElementAtOrDefault(frame.InstructionIndex - 1) is not
                { Op: SkillProgramEffectOp.AdjustNormalDraw, NumberExpression: SkillProgramNumberExpression.PlannedNormalDrawCount })
            throw new InvalidOperationException("A planned normal-draw adjustment requires its exact active instruction.");
        // A replacement owns all normal draws. A zero remaining budget also
        // stays zero; neither case issues a second payment or revives the plan.
        var count = GetProgramPlannedNormalDrawCount(frame, allowReplacedNoOp: true);
        if (count > 0) AdjustProgramNormalDraw(frame, count);
    }

    private int GetDiscardQuotaMultiplier(CharacterState owner) => ConvertRuleValue(RuleQueryService.Evaluate(
        SkillRuleQuery.DiscardQuotaMultiplier, new RuleQueryBounds(1, 20),
        [new RuleQueryBaseTerm("phase:discard:default-quota-multiplier", 1)],
        CollectNumericRuleContributions(owner, SkillRuleQuery.DiscardQuotaMultiplier)));
}
