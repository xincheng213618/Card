using CardGame.Core;

internal static class CharacterSkillSetChecks
{
    public static void GrantsRetainSourcesAndStableOwnership()
    {
        var skills = new CharacterSkillSet();
        var first = new SkillGrant("default", "fixture:skill", "instance:default", "template:primary");
        var second = new SkillGrant("equipment", "fixture:skill", "instance:equipment", "equipment:one");
        Require(skills.Grant(first) && skills.Grant(second) && skills.Revision == 2 &&
                skills.EffectiveSkillIds.SequenceEqual(["fixture:skill"]),
            "Two sources must retain separate grants and one effective skill identity.");
        var frozen = skills.Grants;
        Require(!skills.Grant(second) && skills.Revision == 2, "Identical grant retries must be idempotent.");
        Require(skills.RemoveGrant("default") && skills.EffectiveSkillIds.SequenceEqual(["fixture:skill"]) &&
                frozen.Count == 2 && skills.Revision == 3,
            "Removing one source must retain the other and must not mutate old snapshots.");
        Require(skills.SetEnabled("equipment", false) && skills.EffectiveSkillIds.Count == 0 &&
                !skills.SetEnabled("equipment", false) && skills.Revision == 4,
            "Disabled grants must remain owned without repeatedly invalidating the revision.");
        Require(skills.SetEnabled("equipment", true) && skills.Grants.Single() == second &&
                skills.Revision == 5, "Re-enabling must preserve source and instance identity.");
        Require(!skills.RemoveGrant("missing") && skills.Revision == 5,
            "Removing a missing grant must not change state.");
        Require(new CharacterSkillSet().EffectiveSkillIds.Count == 0,
            "Character skill sets must not share mutable registration state.");
    }

    public static void InvalidGrantsAndConflictsLeaveStateUnchanged()
    {
        var skills = new CharacterSkillSet();
        var grant = new SkillGrant("grant", "fixture:skill", "instance", "source");
        skills.Grant(grant);
        Throws<InvalidOperationException>(() => skills.Grant(grant with { SkillId = "fixture:other" }));
        Throws<ArgumentException>(() => skills.Grant(grant with { GrantId = "new", SkillId = "invalid" }));
        Throws<ArgumentException>(() => skills.Grant(grant with { GrantId = "new", SourceId = " " }));
        Throws<KeyNotFoundException>(() => skills.SetEnabled("unknown", false));
        Require(skills.Revision == 1 && skills.Grants.SequenceEqual([grant]),
            "Rejected grant mutations must preserve the complete previous state.");
        Throws<NotSupportedException>(() => ((ICollection<SkillGrant>)skills.Grants).Clear());
        skills.Grant(new SkillGrant("a", "fixture:alpha", "another", "source"));
        Require(skills.Grants[0].GrantId == "a" &&
                skills.EffectiveSkillIds.SequenceEqual(["fixture:alpha", "fixture:skill"]),
            "Grant snapshots and effective skill identities must have deterministic ordering.");
    }

    public static void CharacterTemplatesSupplyDefaultsWithoutOwningCurrentState()
    {
        var template = Template("first", "fixture:first");
        var first = Character(template);
        var second = Character(template);
        Require(first.SkillGrants.EffectiveSkillIds.SequenceEqual(["fixture:first"]),
            "Creating a character must bind its template's default skill.");
        first.SkillGrants.Grant(new SkillGrant("acquired:first", "fixture:first", "acquired-instance", "acquired:test"));
        first.SkillGrants.RemoveGrant($"{CharacterState.PrimarySkillSource}:fixture:first");
        Require(first.SkillGrants.EffectiveSkillIds.SequenceEqual(["fixture:first"]),
            "A skill acquired from another source must survive removal of the default grant.");
        first.General = Template("next", "fixture:next");
        first.GenderOverride = GeneralGender.Female;
        first.MaxHp = 6;
        first.Hp = 2;
        Require(first.SkillGrants.EffectiveSkillIds.SequenceEqual(["fixture:first", "fixture:next"]) &&
                first.AcquiredSkillIds.SequenceEqual(["fixture:first"]) &&
                first.Gender == GeneralGender.Female && first.General.Gender == GeneralGender.Male,
            "Replacing defaults must retain acquired skills and current attributes must be separate from the template.");
        Require(second.Hp == 4 && second.MaxHp == 4 && second.Gender == GeneralGender.Male &&
                second.SkillGrants.EffectiveSkillIds.SequenceEqual(["fixture:first"]) && template.BaseHp == 4,
            "Characters sharing a template must have independent runtime state.");
        first.GenderOverride = null;
        Require(first.Gender == first.General.Gender, "Removing an override must reveal the template default.");
        var previousTemplate = first.General;
        var previousGrants = first.SkillGrants.Grants;
        Throws<ArgumentException>(() => first.General = Template("invalid", "not-namespaced"));
        Require(first.General == previousTemplate && first.SkillGrants.Grants.SequenceEqual(previousGrants),
            "An invalid template replacement must preserve the template and all current skill sources.");
    }

    private static GeneralDefinition Template(string name, string skillId) =>
        new($"fixture:{name}", name, name,
            [new GeneralSkillDefinition("Skill", "Test") { ContentId = skillId }]);

    private static CharacterState Character(GeneralDefinition template) => new()
    {
        Seat = 0, Name = "Fixture", IsHuman = true, Role = Role.Rebel,
        RoleRevealed = true, General = template, GeneralSelected = true,
        GeneralRevealed = true, MaxHp = 4, Hp = 4
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
