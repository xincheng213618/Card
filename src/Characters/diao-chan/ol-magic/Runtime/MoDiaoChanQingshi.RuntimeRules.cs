namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_diao_chan_ol_magic_MoDiaoChanQingshi
{
    internal static bool IsMoDcSingleDamageKind(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or
        CardKind.FireAttack or CardKind.UnexpectedAssault or CardKind.Lightning or CardKind.AgainstCurrent;
}
