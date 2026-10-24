namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SequentialTrickTargets
{
    internal static bool UsesSequentialTrickTargets(LegalActionKind kind) => kind is
        LegalActionKind.DrawTwo or LegalActionKind.Dismantlement or LegalActionKind.Snatch or LegalActionKind.MantianGuohai or
        LegalActionKind.QizhengXiangsheng or LegalActionKind.WumiaoFlood or LegalActionKind.FireAttack or LegalActionKind.Duel or LegalActionKind.UnexpectedAssault or LegalActionKind.AllianceFeast or
        LegalActionKind.SowBeans or LegalActionKind.TransplantWood or LegalActionKind.ReturnSoul or
        LegalActionKind.StarShift or LegalActionKind.HandRedistribution or
        LegalActionKind.SameBoat or LegalActionKind.StriveUpstream or LegalActionKind.AgainstCurrent or
        LegalActionKind.StoreGrain or LegalActionKind.Reinforcements or LegalActionKind.UnitedEffort;
}
