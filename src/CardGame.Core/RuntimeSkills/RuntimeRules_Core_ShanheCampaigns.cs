using CardGame.Core.Campaigns;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ShanheCampaigns
{
    internal static GameOptions FreezeShanheBattleOptions(GameOptions options)
    {
        if (options.ShanheBattle is not { } setup) return options;
        return options with { ShanheBattle = ShanheBattleSetup.Freeze(setup) };
    }
}
