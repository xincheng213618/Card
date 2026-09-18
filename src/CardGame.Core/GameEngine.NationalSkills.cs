namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<IPassiveSkill> EnabledPassiveSkills(PlayerRuntime player)
    {
        // Older checkpoints retain their original primary-only behavior.
        if (!IsNationalWarMode)
        {
            foreach (var skill in SupportsMultiSkillGenerals
                         ? player.General.Skills
                         : [new GeneralSkillDefinition(
                             player.General.Skill,
                             player.General.SkillName,
                             player.General.SkillDescription)])
            {
                yield return SkillRegistry.Get(skill.Kind);
            }
            yield break;
        }
        if (_rulesVersion < 7)
        {
            yield return SkillRegistry.Get(player.General.Skill);
            yield break;
        }

        var emitted = new HashSet<SkillKind>();
        if (player.GeneralSelected && player.GeneralRevealed)
        {
            foreach (var skill in SupportsMultiSkillGenerals
                         ? player.General.SkillKinds
                         : [player.General.Skill])
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
        if (player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed && player.SecondaryGeneral is { } secondary)
        {
            foreach (var skill in SupportsMultiSkillGenerals
                         ? secondary.SkillKinds
                         : [secondary.Skill])
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
    }

    private IPassiveSkill PassiveRules(PlayerRuntime player)
    {
        var skills = EnabledPassiveSkills(player).ToArray();
        return skills.Length == 1 ? skills[0] : new CompositePassiveRules(skills);
    }

    // Compose only rule queries. Trigger collection keeps each skill's identity
    // so two damage skills can enter and resume their own ordered windows.
    private sealed class CompositePassiveRules(IReadOnlyList<IPassiveSkill> skills) : IPassiveSkill
    {
        public SkillKind Kind => SkillKind.None;
        public string Name => skills.Count == 0 ? "无" : string.Join(" / ", skills.Select(skill => skill.Name));
        public int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => skills.Aggregate(currentCount, (value, skill) => skill.ModifyDrawCount(owner, value));
        public int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => skills.Aggregate(currentLimit, (value, skill) => skill.ModifySlashLimit(owner, value));
        public int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) => skills.Aggregate(currentDistance, (value, skill) => skill.ModifyOutgoingDistance(owner, value));
        public bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) => skills.Any(skill => skill.IgnoresTrickDistance(owner, trickKind));
        public bool ProhibitsSlashTarget(PlayerSkillContext owner) => skills.Any(skill => skill.ProhibitsSlashTarget(owner));
        public bool CanUseAsSlash(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsSlash(owner, card));
        public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
            skills.Any(skill => skill.CanUseAsResponse(owner, card, requiredCardKind));
        public bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsDyingRescue(owner, card));
    }
}
