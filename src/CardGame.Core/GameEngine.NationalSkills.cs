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

    private PromptChoice WithNationalHealthPreview(PromptChoice choice, CharacterState player, GeneralDefinition candidate)
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

    /// <summary>
    /// Evaluates ownership of skills printed on a general card. Skills acquired
    /// after game start use their own future grant source and deliberately do
    /// not pass through this printed-skill restriction.
    /// </summary>
    private bool CanOwnPrintedSkill(CharacterState player, SkillTag tags) =>
        !SupportsStructuredSkillOwnership ||
        !tags.HasFlag(SkillTag.Lord) ||
        player.Role == Role.Lord;

    private IEnumerable<GeneralSkillDefinition> OwnedPrintedSkills(
        CharacterState player,
        GeneralDefinition general) =>
        general.Skills.Where(skill => CanOwnPrintedSkill(player, skill.Tags) &&
            IsEnabledTemplateSkill(player, general, skill.ContentId));

    private static bool IsEnabledTemplateSkill(CharacterState player, GeneralDefinition general, string? skillId)
    {
        if (skillId is null) return true; // Legacy demo skills have no content binding.
        var source = general.Id == player.General.Id
            ? CharacterState.PrimarySkillSource : CharacterState.SecondarySkillSource;
        return player.SkillGrants.Grants.Any(grant =>
            grant.SourceId == source && grant.SkillId == skillId && grant.IsEnabled);
    }

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
            ExecutionForms = skill.ExecutionForms,
            ActionForms = skill.ActionForms
        };
    }

    private IEnumerable<GeneralSkillDefinition> OwnedRuntimeSkills(
        CharacterState player,
        GeneralDefinition general,
        bool includeAcquired)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in OwnedPrintedSkills(player, general))
        {
            if (skill.ContentId is null || emitted.Add(skill.ContentId)) yield return skill;
        }
        if (!includeAcquired || !SupportsRuntimeSkillAcquisition) yield break;
        foreach (var skillId in EnabledNonTemplateSkillGrants(player).Select(grant => grant.SkillId))
        {
            if (!emitted.Add(skillId)) continue;
            yield return ToRuntimeSkillDefinition(skillId);
        }
    }

    private IEnumerable<GeneralSkillDefinition> RuntimePassiveSkillDefinitions(
        CharacterState player,
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
                    ExecutionForms = general.SkillExecutionForms,
                    ActionForms = general.SkillActionForms
                }
            ];
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in definitions.Where(skill => CanOwnPrintedSkill(player, skill.Tags) &&
                     IsEnabledTemplateSkill(player, general, skill.ContentId)))
        {
            if (skill.ContentId is null || emitted.Add(skill.ContentId)) yield return skill;
        }
        if (!SupportsRuntimeSkillAcquisition) yield break;
        foreach (var skillId in EnabledNonTemplateSkillGrants(player).Select(grant => grant.SkillId))
        {
            if (!emitted.Add(skillId)) continue;
            yield return ToRuntimeSkillDefinition(skillId);
        }
    }

    private SkillBindingShard? GetSkillBindingShard(CharacterState player) =>
        _skillBindingIndex?.GetShard(player);

    private IReadOnlyList<string> EnabledPrintedContentSkillIds(CharacterState player)
    {
        if (_contentRegistry is null) return [];
        return EnabledRuntimeSkillGrants(player)
            .Where(grant => grant.SourceId is CharacterState.PrimarySkillSource or
                CharacterState.SecondarySkillSource)
            .Select(grant => grant.SkillId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<string> EnabledContentSkillIds(CharacterState player)
    {
        var ids = EnabledPrintedContentSkillIds(player).ToList();
        if (SupportsRuntimeSkillAcquisition)
            ids.AddRange(EnabledNonTemplateSkillGrants(player).Select(grant => grant.SkillId));
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<SkillGrant> EnabledNonTemplateSkillGrants(CharacterState player) =>
        EnabledRuntimeSkillGrants(player).Where(grant =>
            grant.SourceId is not CharacterState.PrimarySkillSource and not CharacterState.SecondarySkillSource);

    private bool IsEnabledRuntimeSkillGrant(CharacterState player, SkillGrant grant)
    {
        if (!grant.IsEnabled) return false;
        if (grant.SourceId == CharacterState.PrimarySkillSource)
        {
            if (IsNationalWarMode && (!player.GeneralSelected || !player.GeneralRevealed)) return false;
            return _contentRegistry is null ||
                CanOwnPrintedSkill(player, _contentRegistry.GetSkill(grant.SkillId).Tags);
        }
        if (grant.SourceId == CharacterState.SecondarySkillSource)
        {
            if (!IsNationalWarMode || player.SecondaryGeneral is null ||
                !player.SecondaryGeneralSelected || !player.SecondaryGeneralRevealed)
                return false;
            return _contentRegistry is null ||
                CanOwnPrintedSkill(player, _contentRegistry.GetSkill(grant.SkillId).Tags);
        }
        return SupportsRuntimeSkillAcquisition;
    }

    private IEnumerable<SkillGrant> EnabledRuntimeSkillGrants(CharacterState player) =>
        GetSkillBindingShard(player)?.ActiveGrants ??
        player.SkillGrants.Grants.Where(grant => IsEnabledRuntimeSkillGrant(player, grant)).ToArray();

    private bool HasRuntimeSkillInstance(CharacterState player, string skillId, string skillInstanceId) =>
        GetSkillBindingShard(player)?.HasInstance(skillId, skillInstanceId) ??
        EnabledRuntimeSkillGrants(player).Any(grant =>
            grant.SkillId == skillId && grant.SkillInstanceId == skillInstanceId);

    private string GetRuntimeSkillInstanceId(CharacterState player, string skillId) =>
        EnabledRuntimeSkillGrants(player)
            .Where(grant => grant.SkillId == skillId)
            .OrderBy(grant => grant.SkillInstanceId, StringComparer.Ordinal)
            .Select(grant => grant.SkillInstanceId)
            .FirstOrDefault() ??
        throw new InvalidOperationException($"Player {player.Seat} does not own enabled skill '{skillId}'.");

    private bool HasRuntimeSkill(CharacterState player, string skillId) =>
        GetSkillBindingShard(player)?.HasSkill(skillId) ??
        EnabledContentSkillIds(player).Contains(skillId, StringComparer.Ordinal);

    private bool HasLegacyRuntimeSkill(CharacterState player, string skillId) =>
        GetSkillBindingShard(player)?.Definitions.GetValueOrDefault(skillId) is
        {
            Program: null,
            PhaseSkill: null,
            PindianResultSkill: null
        };

    private bool HasRuntimeSkill(CharacterState player, SkillKind skill) =>
        _contentRegistry is null
            ? player.General.HasSkill(skill)
            : GetSkillBindingShard(player)!.Definitions.Values.Any(definition => definition.LegacyKind == skill);

    private ContentSkillDefinition? GetEnabledContentSkill(CharacterState player, SkillKind skill) =>
        _contentRegistry is null
            ? null
            : GetSkillBindingShard(player)!.Definitions.Values
                .OrderBy(definition => definition.Id, StringComparer.Ordinal)
                .FirstOrDefault(definition => definition.LegacyKind == skill);

    private IEnumerable<IPassiveSkill> EnabledPassiveSkills(CharacterState player)
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
                         .Select(definition => definition.Kind)
                         .Where(kind => kind != SkillKind.None))
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
        if (player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed && player.SecondaryGeneral is { } secondary)
        {
            foreach (var skill in RuntimePassiveSkillDefinitions(player, secondary)
                         .Select(definition => definition.Kind)
                         .Where(kind => kind != SkillKind.None))
            {
                if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
            }
        }
        // Runtime grants are independent of either hidden template slot. A
        // dynamically granted passive remains effective even while both
        // generals are concealed; only printed grants follow reveal state.
        foreach (var skill in EnabledNonTemplateSkillGrants(player)
                     .Select(grant => ToRuntimeSkillDefinition(grant.SkillId).Kind)
                     .Where(kind => kind != SkillKind.None))
        {
            if (emitted.Add(skill)) yield return SkillRegistry.Get(skill);
        }
    }

    private IPassiveSkill PassiveRules(CharacterState player)
    {
        var programs = EnabledPassiveRulePrograms(player);
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
        public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
            skills.Any(skill => skill.CanUseAsResponse(owner, card, requiredCardKind));
        public int ModifyRequiredResponseCount(ResponseCountSkillContext context, int currentCount) =>
            skills.Aggregate(currentCount, (value, skill) => skill.ModifyRequiredResponseCount(context, value));
        public bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => skills.Any(skill => skill.CanUseAsDyingRescue(owner, card));
    }
}
