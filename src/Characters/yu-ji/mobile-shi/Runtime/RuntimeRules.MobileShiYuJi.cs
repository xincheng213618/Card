namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Mobile_Shi_Yu_Ji
{
    // 火杀/雷杀与普通杀共用"杀"牌名；武翊的牌名记录沿用同一归并。
    internal static CardKind NormalizedName(CardKind kind) =>
        kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
}
