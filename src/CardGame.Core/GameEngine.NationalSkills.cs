using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int NationalMaxHp(GeneralDefinition primary, GeneralDefinition secondary) =>
        ((primary.BaseHp + secondary.BaseHp) / 2 );

    private void InitializeNationalHealth()
    {
        if (!IsNationalWarMode) return;
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
        if (!IsNationalWarMode) return choice;
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
        !tags.HasFlag(SkillTag.Lord) ||
        player.Role == Role.Lord;

    private IEnumerable<GeneralSkillDefinition> OwnedPrintedSkills(
        CharacterState player,
        GeneralDefinition general) =>
        general.Skills.Where(skill => CanOwnPrintedSkill(player, skill.Tags) &&
            IsEnabledTemplateSkill(player, general, skill.ContentId));

    private static bool IsEnabledTemplateSkill(CharacterState player, GeneralDefinition general, string? skillId)
    {
        if (skillId is null) return false;
        var source = general.Id == player.General.Id
            ? CharacterState.PrimarySkillSource : CharacterState.SecondarySkillSource;
        return player.SkillGrants.Grants.Any(grant =>
            grant.SourceId == source && grant.SkillId == skillId && grant.IsEnabled);
    }

    private GeneralSkillDefinition ToRuntimeSkillDefinition(string skillId)
    {
        var skill = _contentRegistry.GetSkill(skillId);
        return ToRuntimeSkillDefinition(skill);
    }

    private static GeneralSkillDefinition ToRuntimeSkillDefinition(ContentSkillDefinition skill) =>
        new GeneralSkillDefinition(
            skill.LegacyKind ?? SkillKind.None,
            skill.Name,
            skill.Description)
        {
            ContentId = skill.Id,
            Tags = skill.Tags,
            ExecutionForms = skill.ExecutionForms,
            ActionForms = skill.ActionForms,
            ViewAsOpportunities = skill.Program?.ViewAs
                .Where(rule => rule.Condition.Kind == SkillProgramConditionKind.Always &&
                               rule.InputCount == 1 &&
                               rule.SourceZones.Contains(CardZoneKind.Hand) &&
                               rule.InputCategories.Count == 0)
                .Select(rule => new SkillViewAsOpportunity(
                    rule.OutputKind, rule.InputKinds, rule.InputSuits,
                    rule.ForPlay, rule.ForResponse))
                .ToArray()
        };

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
        if (!includeAcquired) yield break;
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
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in general.Skills.Where(skill => CanOwnPrintedSkill(player, skill.Tags) &&
                     IsEnabledTemplateSkill(player, general, skill.ContentId)))
        {
            if (skill.ContentId is null || emitted.Add(skill.ContentId)) yield return skill;
        }
        foreach (var skillId in EnabledNonTemplateSkillGrants(player).Select(grant => grant.SkillId))
        {
            if (!emitted.Add(skillId)) continue;
            yield return ToRuntimeSkillDefinition(skillId);
        }
    }

    private SkillBindingShard GetSkillBindingShard(CharacterState player) =>
        _skillBindingIndex.GetShard(player);

    private IReadOnlyList<string> EnabledPrintedContentSkillIds(CharacterState player)
    {
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
        ids.AddRange(EnabledNonTemplateSkillGrants(player).Select(grant => grant.SkillId));
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<SkillGrant> EnabledNonTemplateSkillGrants(CharacterState player) =>
        EnabledRuntimeSkillGrants(player).Where(grant =>
            grant.SourceId is not CharacterState.PrimarySkillSource and not CharacterState.SecondarySkillSource);

    private IEnumerable<SkillGrant> EnabledRuntimeSkillGrants(CharacterState player) =>
        GetSkillBindingShard(player).ActiveGrants;

    private bool HasRuntimeSkillInstance(CharacterState player, string skillId, string skillInstanceId) =>
        GetSkillBindingShard(player).HasInstance(skillId, skillInstanceId);

    private string GetRuntimeSkillInstanceId(CharacterState player, string skillId) =>
        EnabledRuntimeSkillGrants(player)
            .Where(grant => grant.SkillId == skillId)
            .OrderBy(grant => grant.SkillInstanceId, StringComparer.Ordinal)
            .Select(grant => grant.SkillInstanceId)
            .FirstOrDefault() ??
        throw new InvalidOperationException($"Player {player.Seat} does not own enabled skill '{skillId}'.");

    private bool HasRuntimeSkill(CharacterState player, string skillId) =>
        GetSkillBindingShard(player).HasSkill(skillId);

    private bool HasRuntimeSkill(CharacterState player, SkillKind skill) =>
        GetSkillBindingShard(player).Definitions.Values.Any(definition => definition.LegacyKind == skill);

    private ContentSkillDefinition? GetEnabledContentSkill(CharacterState player, SkillKind skill) =>
        GetSkillBindingShard(player).Definitions.Values
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .FirstOrDefault(definition => definition.LegacyKind == skill);

    private IEnumerable<SkillRuleDefinition> EnabledSkillRules(CharacterState player)
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

    private IEnumerable<ICardUseSkillRule> CardUseRules(CharacterState player) =>
        EnabledSkillRules(player).Select(skill => skill.CardUse)
            .Where(rule => rule is not null).Cast<ICardUseSkillRule>();

    private string EnabledSkillNames(CharacterState player)
    {
        var names = EnabledSkillRules(player).Select(skill => skill.Name).ToList();
        if (EnabledPassiveRulePrograms(player).Count != 0) names.Add("Configured skills");
        return names.Count == 0 ? "无" : string.Join(" / ", names);
    }
}
