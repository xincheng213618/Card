namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>
    /// Evaluates ownership of skills printed on a general card. Skills acquired
    /// after game start use their own future grant source and deliberately do
    /// not pass through this printed-skill restriction.
    /// </summary>
    private bool CanOwnPrintedSkill(PlayerRuntime player, SkillTag tags) =>
        !SupportsStructuredSkillOwnership ||
        !tags.HasFlag(SkillTag.Lord) ||
        player.Role == Role.Lord;

    private IEnumerable<GeneralSkillDefinition> OwnedPrintedSkills(
        PlayerRuntime player,
        GeneralDefinition general) =>
        general.Skills.Where(skill => CanOwnPrintedSkill(player, skill.Tags));

    private GeneralSkillDefinition ToRuntimeSkillDefinition(string skillId)
    {
        var skill = _contentRegistry?.GetSkill(skillId) ??
            throw new InvalidOperationException(
                $"Runtime skill '{skillId}' is unavailable in the active content registry.");
        return new GeneralSkillDefinition(
            skill.LegacyKind ?? SkillKind.None,
            skill.Name,
            skill.Description)
        {
            ContentId = skill.Id,
            Tags = skill.Tags,
            ExecutionForms = skill.ExecutionForms
        };
    }

    private IEnumerable<GeneralSkillDefinition> OwnedRuntimeSkills(
        PlayerRuntime player,
        GeneralDefinition general,
        bool includeAcquired)
    {
        foreach (var skill in OwnedPrintedSkills(player, general)) yield return skill;
        if (!includeAcquired || !SupportsRuntimeSkillAcquisition) yield break;
        foreach (var skillId in player.AcquiredSkillIds)
            yield return ToRuntimeSkillDefinition(skillId);
    }

    private IEnumerable<GeneralSkillDefinition> RuntimePassiveSkillDefinitions(
        PlayerRuntime player,
        GeneralDefinition general)
    {
        var definitions = SupportsMultiSkillGenerals
            ? general.Skills
            :
            [
                new GeneralSkillDefinition(
                    general.Skill,
                    general.SkillName,
                    general.SkillDescription)
                {
                    ContentId = general.SkillContentId,
                    Tags = general.SkillTags,
                    ExecutionForms = general.SkillExecutionForms
                }
            ];
        foreach (var skill in definitions.Where(skill => CanOwnPrintedSkill(player, skill.Tags)))
            yield return skill;
        if (!SupportsRuntimeSkillAcquisition) yield break;
        foreach (var skillId in player.AcquiredSkillIds)
            yield return ToRuntimeSkillDefinition(skillId);
    }

    private IReadOnlyList<string> EnabledPrintedContentSkillIds(PlayerRuntime player)
    {
        if (_contentRegistry is null) return [];

        var ids = new List<string>();
        void AddGeneral(GeneralDefinition general)
        {
            if (!_contentRegistry.Generals.TryGetValue(general.Id, out var definition)) return;
            ids.AddRange(definition.SkillIds.Where(id =>
                CanOwnPrintedSkill(player, _contentRegistry.GetSkill(id).Tags)));
        }

        if (!IsNationalWarMode || player.GeneralSelected && player.GeneralRevealed)
            AddGeneral(player.General);
        if (IsNationalWarMode && player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed &&
            player.SecondaryGeneral is { } secondary)
            AddGeneral(secondary);
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<string> EnabledContentSkillIds(PlayerRuntime player)
    {
        var ids = EnabledPrintedContentSkillIds(player).ToList();
        if (SupportsRuntimeSkillAcquisition) ids.AddRange(player.AcquiredSkillIds);
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private bool HasRuntimeSkill(PlayerRuntime player, string skillId) =>
        EnabledContentSkillIds(player).Contains(skillId, StringComparer.Ordinal);

    private bool HasRuntimeSkill(PlayerRuntime player, SkillKind skill) =>
        EnabledContentSkillIds(player).Any(id =>
            _contentRegistry?.GetSkill(id).LegacyKind == skill);

    private IEnumerable<IPassiveSkill> EnabledPassiveSkills(PlayerRuntime player)
    {
        // Older checkpoints retain their original primary-only behavior.
        if (!IsNationalWarMode)
        {
            var emittedKinds = new HashSet<SkillKind>();
            foreach (var skill in RuntimePassiveSkillDefinitions(player, player.General))
            {
                if (skill.Kind != SkillKind.None &&
                    emittedKinds.Add(skill.Kind) &&
                    (skill.Kind != SkillKind.Yicong || UsesFormalGongsunZan))
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
            foreach (var skill in RuntimePassiveSkillDefinitions(player, player.General)
                         .Select(definition => definition.Kind))
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
        if (player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed && player.SecondaryGeneral is { } secondary)
        {
            foreach (var skill in RuntimePassiveSkillDefinitions(player, secondary)
                         .Select(definition => definition.Kind))
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
    }

    private IPassiveSkill PassiveRules(PlayerRuntime player)
    {
        var programs = EnabledSkillPrograms(player);
        var skills = programs.Count == 0
            ? EnabledPassiveSkills(player).ToArray()
            : EnabledPassiveSkills(player).Append(new SkillProgramRules(programs,
                GetHand(player).Select(card => card.Id).ToHashSet())).ToArray();
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
        public int ModifyIncomingDistance(PlayerSkillContext owner, int currentDistance) => skills.Aggregate(currentDistance, (value, skill) => skill.ModifyIncomingDistance(owner, value));
        public bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) => skills.Any(skill => skill.IgnoresTrickDistance(owner, trickKind));
        public bool ProhibitsSlashTarget(PlayerSkillContext owner) => skills.Any(skill => skill.ProhibitsSlashTarget(owner));
        public bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) => skills.Any(skill => skill.ProhibitsCardTarget(owner, cardKind));
        public bool CanUseAsSlash(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsSlash(owner, card));
        public bool CanUseAsDismantlement(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsDismantlement(owner, card));
        public bool CanUseAsSupplyShortage(PlayerSkillContext owner, Card card) =>
            skills.Any(skill => skill.CanUseAsSupplyShortage(owner, card));
        public bool CanUseAsIndulgence(PlayerSkillContext owner, Card card) =>
            skills.Any(skill => skill.CanUseAsIndulgence(owner, card));
        public int ModifySupplyShortageDistanceLimit(PlayerSkillContext owner, int currentLimit) =>
            skills.Aggregate(currentLimit, (value, skill) => skill.ModifySupplyShortageDistanceLimit(owner, value));
        public bool CanSkipDiscardPhase(PlayerSkillContext owner, bool usedOrPlayedSlashDuringPlayPhase) =>
            skills.Any(skill => skill.CanSkipDiscardPhase(owner, usedOrPlayedSlashDuringPlayPhase));
        public bool CanReplaceDrawPhase(PlayerSkillContext owner) => skills.Any(skill => skill.CanReplaceDrawPhase(owner));
        public bool CanReduceDrawPhase(PlayerSkillContext owner) => skills.Any(skill => skill.CanReduceDrawPhase(owner));
        public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
            skills.Any(skill => skill.CanUseAsResponse(owner, card, requiredCardKind));
        public int ModifyRequiredResponseCount(ResponseCountSkillContext context, int currentCount) =>
            skills.Aggregate(currentCount, (value, skill) => skill.ModifyRequiredResponseCount(context, value));
        public bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsDyingRescue(owner, card));
    }
}
