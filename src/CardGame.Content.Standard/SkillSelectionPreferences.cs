using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class SkillSelectionPreferences
{
    internal static SkillRevealWeights? RevealFor(string skillId) => skillId.Split(':').Last() switch
    {
        "paoxiao" => new(RepeatedSlash: 42),
        "kongcheng" => new(EmptyHand: 56),
        "jianxiong" or "feedback" or "yiji" or "jieming" or "yuanhu" or "ganglie" => new(Wounded: 31),
        "mashu" or "qicai" => new(Base: 20),
        "yingzi" => new(Base: 18),
        "guicai" or "jijiu" => new(Base: 16),
        _ => null
    };
}
