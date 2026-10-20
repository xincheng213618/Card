namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yang_yi_ol_YangYiPrograms
{
    // 仅指定唯一目标的普通锦囊牌：the card's targeting is exactly one other
    // character, so instant single-target tricks qualify and every other
    // ordinary trick (self/none/one-or-two/multi-target) stays outside.
    internal static bool IsJuanxiaSingleTargetTrick(CardKind kind) => kind is
        CardKind.Duel or CardKind.Snatch or CardKind.Dismantlement or CardKind.FireAttack;

    internal static IReadOnlyList<string> AppendJuanxiaKind(IReadOnlyList<string> kinds, CardKind kind) =>
        Array.AsReadOnly(kinds.Append(kind.ToString()).ToArray());
}
