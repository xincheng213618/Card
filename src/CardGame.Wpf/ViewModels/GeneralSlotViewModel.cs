using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

/// <summary>One national general slot, projected only from the player's permitted view.</summary>
public sealed record GeneralSlotViewModel(string SlotLabel, string GeneralId, string Name, string SkillName,
    string SkillDescription, bool IsRevealed, bool IsKnown, bool IsSkillEnabled, string SkillStateText)
{
    public string StateText => IsRevealed ? "明置" : "暗置";
    public string SlotState => $"{SlotLabel} · {StateText}";
    public string DetailText => $"{SlotLabel}将 · {Name} · {StateText}\n{SkillDescription}\n{SkillStateText}";
    public bool HasPortrait => GeneralArt.HasPortrait(GeneralId);
    public Brush PortraitBrush => GeneralArt.GetPortrait(GeneralId);
    public Brush StateBrush => IsRevealed ? new SolidColorBrush(Color.FromRgb(158, 197, 161)) : new SolidColorBrush(Color.FromRgb(184, 165, 134));

    public static GeneralSlotViewModel FromPlayer(PlayerSnapshot player, bool secondary, int rulesVersion)
    {
        var revealed = secondary ? player.IsSecondaryGeneralPublic : player.IsGeneralPublic;
        // Guard visibility even when a host supplies a trusted/debug snapshot.
        var permitted = player.IsHuman || revealed;
        var id = permitted ? (secondary ? player.SecondaryGeneralId : player.GeneralId) ?? string.Empty : string.Empty;
        var known = id.Length > 0;
        var name = known ? (secondary ? player.SecondaryGeneralName : player.GeneralName)! : player.IsHuman ? "待选" : "暗将";
        var fallbackSkill = new GeneralSkillDefinition(
            known ? (secondary ? player.SecondarySkill : player.Skill) ?? SkillKind.None : SkillKind.None,
            known ? (secondary ? player.SecondarySkillName : player.SkillName) ?? "无" : "未知技能",
            known ? (secondary ? player.SecondarySkillDescription : player.SkillDescription) ?? "没有技能。" : "明置后可查看武将与技能。");
        var skills = known
            ? (secondary ? player.SecondarySkills : player.Skills) ?? [fallbackSkill]
            : [fallbackSkill];
        var hasSkill = known && skills.Any(skill => skill.Kind != SkillKind.None || skill.Name != "无");
        var skillName = string.Join(" / ", skills.Select(skill => skill.Name));
        var description = string.Join("\n", skills.Select(skill => $"{skill.Name}：{skill.Description}"));
        var enabled = hasSkill && (rulesVersion >= 7 ? revealed : !secondary);
        var state = !known ? "武将尚未公开" : !hasSkill ? "此将没有技能" : rulesVersion < 7
            ? secondary ? "旧规则：副将技能未启用" : "旧规则：主将技能生效"
            : enabled ? "技能已启用" : "暗置中，技能未启用";
        return new(secondary ? "副" : "主", id, name, skillName, description, revealed, known, enabled, state);
    }
}
