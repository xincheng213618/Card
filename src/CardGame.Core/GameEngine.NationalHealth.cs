using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int NationalMaxHp(GeneralDefinition primary, GeneralDefinition secondary) =>
        _rulesVersion >= 8 ? (primary.BaseHp + secondary.BaseHp) / 2 : 4;

    private void InitializeNationalHealth()
    {
        if (!IsNationalWarMode || _rulesVersion < 8) return;
        foreach (var player in _players)
        {
            if (!player.GeneralSelected || !player.SecondaryGeneralSelected || player.SecondaryGeneral is null)
                throw new InvalidOperationException("Both national generals must be selected before initializing health.");
            player.MaxHp = NationalMaxHp(player.General, player.SecondaryGeneral);
            player.Hp = player.MaxHp;
        }
    }

    private PromptChoice WithNationalHealthPreview(PromptChoice choice, PlayerRuntime player, GeneralDefinition candidate)
    {
        if (!IsNationalWarMode || _rulesVersion < 8) return choice;
        var parameters = new Dictionary<string, string>(choice.Parameters);
        parameters["base-hp"] = candidate.BaseHp.ToString(CultureInfo.InvariantCulture);
        var health = $"基础体力 {candidate.BaseHp}";
        if (player.GeneralSelected)
        {
            var maxHp = NationalMaxHp(player.General, candidate);
            parameters["primary-base-hp"] = player.General.BaseHp.ToString(CultureInfo.InvariantCulture);
            parameters["combined-max-hp"] = maxHp.ToString(CultureInfo.InvariantCulture);
            health += $" · 组合上限 {maxHp}（{player.General.BaseHp}+{candidate.BaseHp} 平均向下取整）";
        }
        parameters["health-preview"] = health;
        return choice with { Description = choice.Description + " · " + health, Parameters = parameters };
    }
}
