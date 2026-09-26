using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class SkillSelectionPreferences
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<Role, double>> Weights =
        new Dictionary<string, IReadOnlyDictionary<Role, double>>(StringComparer.Ordinal)
        {
            ["classic:kongcheng"] = Roles(25, 34, 25, 25),
            ["standard:kongcheng"] = Roles(25, 34, 25, 25),
            ["classic:guicai"] = Roles(41, 41, 36, 36),
            ["standard:guicai"] = Roles(41, 41, 36, 36),
            ["classic:ganglie"] = Roles(39, 39, 35, 35),
            ["standard:ganglie"] = Roles(39, 39, 35, 35),
            ["standard:yuanhu"] = Roles(38, 38, 33, 33),
            ["classic:qicai"] = Roles(33, 33, 37, 37),
            ["standard:qicai"] = Roles(33, 33, 37, 37)
        };

    internal static IReadOnlyDictionary<Role, double>? For(string skillId) =>
        Weights.GetValueOrDefault(skillId);

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

    private static IReadOnlyDictionary<Role, double> Roles(double lord, double loyalist,
        double rebel, double renegade) => new Dictionary<Role, double>
        {
            [Role.Lord] = lord, [Role.Loyalist] = loyalist,
            [Role.Rebel] = rebel, [Role.Renegade] = renegade
        };
}
