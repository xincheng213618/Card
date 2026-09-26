using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramChoiceAiChecks
{
    public static void PublicStatePredictsExactlyOneOption()
    {
        var effects = StandardContentRegistry.CreateWithClassicGenerals().GetSkill("classic:jujian")
            .Program!.Triggers.Single().Effects.Select(effect => effect.ToExecutionEffect()).ToArray();
        var owner = new PlayerSkillContext(0, 3, 3, 2, TurnPhase.Finished);
        var target = new PlayerSkillContext(1, 4, 4, 2, TurnPhase.Finished);
        ProgramAiEstimate Estimate(PlayerSkillContext chooser) => ProgramCompositionAi.Estimate(effects, owner,
            publicContext: new ProgramAiPublicContext(2, SelectedTarget: chooser));
        var draw = Estimate(target);
        Require(draw.Hint.TargetDraw == 2 && draw.Hint.TargetRecovery == 0 &&
                draw.Hint.TargetValueAdjustment == 0 && draw.Score == -8,
            "A healthy face-up target gains only two cards; the owner pays exactly one card.");
        var recover = Estimate(target with { Hp = 1 });
        Require(recover.Hint.TargetDraw == 0 && recover.Hint.TargetRecovery == 1,
            "A one-HP chooser must favor ordinary recovery instead of adding all alternative effects.");
        var restore = Estimate(target with { IsFaceDown = true, IsChained = true });
        Require(restore.Hint.TargetDraw == 0 && restore.Hint.TargetRecovery == 0 &&
                restore.Hint.TargetValueAdjustment == 28 && restore.Score == -8,
            "Restoration is a target benefit, not an owner bonus or an attack against that target.");
        var chainedOnly = Estimate(target with { IsChained = true });
        Require(chainedOnly.Hint.TargetDraw == 2 && chainedOnly.Hint.TargetValueAdjustment == 0,
            "The bounded public estimate may prefer two cards to removing only chaining.");
        var committed = ProgramCompositionAi.Estimate(effects, owner, publicContext:
            new ProgramAiPublicContext(2, SelectedTarget: target with { Hp = 1 },
                ChoiceResult: name => name == "benefit" ? "draw" : null));
        Require(committed.Hint.TargetDraw == 2 && committed.Hint.TargetRecovery == 0,
            "Later AI decisions must preserve the earlier committed choice instead of predicting a replacement.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
