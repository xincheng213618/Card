using CardGame.Core.Campaigns;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasShanhePrintedLordQualifications => _options.ShanheBattle is { } setup &&
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member => member.QualifiedPrintedLordSkillIds is { Count: > 0 });

    private static void ValidateShanhePrintedLordQualifications(ShanheCombatantDefinition member,
        ContentGeneralDefinition general, ContentRegistry registry)
    {
        if (member.QualifiedPrintedLordSkillIds is not { } requested) return;
        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Count || requested.Any(id =>
                string.IsNullOrWhiteSpace(id) || !general.SkillIds.Contains(id, StringComparer.Ordinal) ||
                !registry.Skills.TryGetValue(id, out var skill) || !skill.Tags.HasFlag(SkillTag.Lord) ||
                skill.ImplementationStatus != SkillImplementationStatus.Complete || skill.Program is null))
            throw new ArgumentException("Scenario Lord qualification requires distinct complete Lord skills printed on the original primary general.");
    }

    // Qualification is a property of this immutable encounter recipe and its
    // exact original printed instance. It never changes a role, creates a grant,
    // or exempts that grant from ordinary reveal, suppression or loss rules.
    private bool IsShanhePrintedLordGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (_options.ShanheBattle is not { } setup || owner.Seat <= 0 ||
            owner.Seat > setup.AllyRoster.Count + setup.EnemyRoster.Count ||
            grant.SourceId != CharacterState.PrimarySkillSource || grant.LordProjection is not null ||
            grant.GeneralLibraryProjection is not null ||
            grant.GrantId != $"{CharacterState.PrimarySkillSource}:{grant.SkillId}" || grant.SkillInstanceId != grant.GrantId)
            return false;
        var member = owner.Seat <= setup.AllyRoster.Count ? setup.AllyRoster[owner.Seat - 1] :
            setup.EnemyRoster[owner.Seat - setup.AllyRoster.Count - 1];
        return owner.General.Id == member.GeneralId &&
            member.QualifiedPrintedLordSkillIds?.Contains(grant.SkillId, StringComparer.Ordinal) == true &&
            owner.General.Skills.Any(skill => skill.ContentId == grant.SkillId && skill.Tags.HasFlag(SkillTag.Lord));
    }
}
