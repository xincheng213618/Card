namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<IPassiveSkill> EnabledPassiveSkills(PlayerRuntime player)
    {
        // Older checkpoints retain their original primary-only behavior.
        if (!IsNationalWarMode || _rulesVersion < 7)
        {
            yield return SkillRegistry.Get(player.General.Skill);
            yield break;
        }
        if (player.GeneralSelected && player.GeneralRevealed)
            yield return SkillRegistry.Get(player.General.Skill);
        if (player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed && player.SecondaryGeneral is { } secondary &&
            (!player.GeneralRevealed || secondary.Skill != player.General.Skill))
            yield return SkillRegistry.Get(secondary.Skill);
    }

    private IPassiveSkill PassiveRules(PlayerRuntime player) => !IsNationalWarMode || _rulesVersion < 7
        ? SkillRegistry.Get(player.General.Skill)
        : new NationalPassiveRules(EnabledPassiveSkills(player).ToArray());

    // Compose only rule queries. Trigger collection keeps each skill's identity
    // so two damage skills can enter and resume their own ordered windows.
    private sealed class NationalPassiveRules(IReadOnlyList<IPassiveSkill> skills) : IPassiveSkill
    {
        public SkillKind Kind => SkillKind.None;
        public string Name => skills.Count == 0 ? "无" : string.Join(" / ", skills.Select(skill => skill.Name));
        public int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => skills.Aggregate(currentCount, (value, skill) => skill.ModifyDrawCount(owner, value));
        public int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => skills.Aggregate(currentLimit, (value, skill) => skill.ModifySlashLimit(owner, value));
        public int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) => skills.Aggregate(currentDistance, (value, skill) => skill.ModifyOutgoingDistance(owner, value));
        public bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) => skills.Any(skill => skill.IgnoresTrickDistance(owner, trickKind));
        public bool ProhibitsSlashTarget(PlayerSkillContext owner) => skills.Any(skill => skill.ProhibitsSlashTarget(owner));
        public bool CanUseAsSlash(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsSlash(owner, card));
        public bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsDyingRescue(owner, card));
    }
}
