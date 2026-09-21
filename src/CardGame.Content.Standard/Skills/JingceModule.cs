using System.Globalization;
using CardGame.Core;

namespace CardGame.Content.Standard.Skills;

public sealed class JingceModule : IPhaseSkillModule
{
    public string SkillId => "classic:jingce";
    public int Revision => 1;
    public PhaseSkillWindow Window => PhaseSkillWindow.PlayEnding;

    public SkillActivationPlan? CreatePlan(PhaseSkillContext context)
    {
        if (!context.IsClassicIdentityMode || context.CardsUsedThisTurn < context.Hp) return null;
        PromptChoice Choice(bool use) => new(
            new ChoiceId($"jingce.{(use ? "use" : "skip")}.turn-{context.TurnNumber}.count-{context.CardsUsedThisTurn}"),
            use ? "发动【精策】，摸两张牌。" : "不发动【精策】。", [], [],
            new Dictionary<string, string>
            {
                ["action"] = use ? "jingce-use" : "jingce-skip",
                ["used-card-count"] = context.CardsUsedThisTurn.ToString(CultureInfo.InvariantCulture),
                ["current-hp"] = context.Hp.ToString(CultureInfo.InvariantCulture)
            });
        return new SkillActivationPlan(
            new(SkillId, "精策", "精策 · 决定是否摸两张牌", "出牌阶段结束时，本回合使用牌数达到当前体力值，可以摸两张牌；也可以跳过。"),
            $"出牌阶段结束：本回合已使用 {context.CardsUsedThisTurn} 张牌，当前体力为 {context.Hp}，是否发动【精策】摸两张牌？",
            Choice(true), Choice(false), [new DrawSkillCards(2, CardMoveReasons.JingceDraw)],
            LegacyDecisionKind: DecisionKind.Jingce);
    }

    public IGameEvent CreateResolvedEvent(PhaseSkillContext context, SkillActivationResult result) =>
        new JingceResolvedEvent(context.OwnerSeat, context.CardsUsedThisTurn, context.Hp, result.Used, result.DrawnCardIds);
}
