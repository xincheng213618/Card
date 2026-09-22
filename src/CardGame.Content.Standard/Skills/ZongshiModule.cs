using CardGame.Core;

namespace CardGame.Content.Standard.Skills;

public sealed class ZongshiModule : IPindianResultModule
{
    public string SkillId => "classic:jianyong-zongshi";
    public int Revision => 1;

    public PindianCardClaimPlan? CreatePlan(PindianResultContext context)
    {
        var result = context.Result;
        if (context.OwnerSeat != result.SourceSeat && context.OwnerSeat != result.OpponentSeat)
        {
            return null;
        }

        var ownerWon = result.WonBy(context.OwnerSeat);
        var cardId = ownerWon
            ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId
            : result.CardOf(context.OwnerSeat);
        if (!context.AvailableCardIds.Contains(cardId))
        {
            return null;
        }

        var resultText = ownerWon
            ? "你已拼点获胜，可以获得两张拼点牌中点数较小的一张。"
            : "你未拼点获胜，可以收回自己的拼点牌。";
        return new PindianCardClaimPlan(
            new SkillPromptPresentation(
                SkillId,
                "纵适",
                "纵适 · 是否获得拼点牌",
                $"{resultText}也可以跳过。"),
            $"{resultText}是否发动【纵适】？",
            cardId);
    }
}
