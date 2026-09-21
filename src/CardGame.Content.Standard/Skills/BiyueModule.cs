using CardGame.Core;

namespace CardGame.Content.Standard.Skills;

public sealed class BiyueModule : IPhaseSkillModule
{
    public string SkillId => "classic:biyue";
    public int Revision => 1;
    public PhaseSkillWindow Window => PhaseSkillWindow.TurnEnding;

    public SkillActivationPlan? CreatePlan(PhaseSkillContext context)
    {
        if (!context.IsClassicIdentityMode) return null;
        PromptChoice Choice(bool use) => new(
            new ChoiceId(use ? "biyue.use" : "biyue.skip"),
            use ? "发动【闭月】，摸一张牌。" : "不发动【闭月】。", [], [],
            new Dictionary<string, string> { ["action"] = use ? "biyue-use" : "biyue-skip" });
        return new SkillActivationPlan(
            new(SkillId, "闭月", "闭月 · 决定是否摸一张牌", "结束阶段，可以发动闭月摸一张牌，也可以跳过并结束回合。"),
            "结束阶段：是否发动【闭月】摸一张牌？",
            Choice(true), Choice(false), [new DrawSkillCards(1, CardMoveReasons.BiyueDraw, LogDraw: true)],
            LegacyDecisionKind: DecisionKind.Biyue);
    }
}
