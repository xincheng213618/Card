namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_meng_da_ol_MengDa
{
    internal static bool GoudePrivateChoice(ProgramSkillFrame f)=>f.Goude!.Stage==GoudeStage.DiscardCard;
}
