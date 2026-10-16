using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillMetadataChecks
{
    public static void TagsNormalizeAndFingerprint()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();

        Require(current.Skills["classic:niepan"] is
        {
            Tags: SkillTag.Limited,
            ExecutionForms: SkillExecutionForm.Trigger
        } &&
                current.Skills["classic:xueyi"] is
                {
                    Tags: SkillTag.Lord | SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State
                } &&
                current.Skills["classic:huangtian"] is
                {
                    Tags: SkillTag.Lord,
                    ExecutionForms: SkillExecutionForm.Trigger
                } &&
                current.Skills["classic:wushen"] is
                {
                    Tags: SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State
                },
            "The current classic skills must publish structured metadata.");

        var plain = ContentRegistry.Build(new MetadataFixture(SkillTag.None, SkillExecutionForm.None));
        var tagged = ContentRegistry.Build(new MetadataFixture(SkillTag.Limited, SkillExecutionForm.Trigger));
        Require(plain.ContentHash != tagged.ContentHash,
            "Structured tags and execution forms must participate in content drift detection.");
        var active = ContentRegistry.Build(new MetadataFixture(
            SkillTag.None,
            SkillExecutionForm.None,
            SkillActionForm.Active));
        Require(active.Skills[MetadataFixture.SkillId].ActionForms == SkillActionForm.Active &&
                plain.ContentHash != active.ContentHash,
            "Structured active action forms must participate in content drift detection independently.");

        var awakening = ContentRegistry.Build(new MetadataFixture(
            SkillTag.Awakening,
            SkillExecutionForm.Trigger));
        Require(awakening.Skills[MetadataFixture.SkillId].Tags ==
                (SkillTag.Awakening | SkillTag.Locked | SkillTag.Limited),
            "Awakening metadata must normalize its documented Locked and Limited tags.");

        var localizedOnly = ContentRegistry.Build(new LocalizedTextFixture());
        Require(localizedOnly.Skills[LocalizedTextFixture.SkillId].Tags == SkillTag.None,
            "A localized description containing tag words must not create structured metadata.");

        RequireThrows<InvalidOperationException>(() =>
            ContentRegistry.Build(new MetadataFixture((SkillTag)(1 << 12), SkillExecutionForm.State)));
        RequireThrows<InvalidOperationException>(() =>
            ContentRegistry.Build(new MetadataFixture(SkillTag.Locked, (SkillExecutionForm)(1 << 12))));
        RequireThrows<InvalidOperationException>(() =>
            ContentRegistry.Build(new MetadataFixture(
                SkillTag.None,
                SkillExecutionForm.None,
                (SkillActionForm)(1 << 12))));
    }

    public static void ClassicLockedStateMetadataIsVersioned()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var general in registry.Generals.Values.Where(general => general.Id.StartsWith("classic:", StringComparison.Ordinal)))
        foreach (var skillId in general.SkillIds)
        {
            var skill = registry.GetSkill(skillId);
            Require(skill.Program is not null && skill.Program.RuntimeVersion == "skill-program-v62",
                $"{general.Id} / {skillId} must be backed by the current compiled program.");
        }
    }

    public static void ClassicSharedLockedSkillsReceiveDistinctIdentities()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var general in registry.Generals.Values.Where(general => general.Id.StartsWith("classic:", StringComparison.Ordinal)))
        foreach (var skillId in general.SkillIds)
        {
            var skill = registry.GetSkill(skillId);
            Require(skill.Program is not null && skill.Program.RuntimeVersion == "skill-program-v62",
                $"{general.Id} / {skillId} must be backed by the current compiled program.");
        }
    }

    public static void ClassicOptionalTriggerMetadataIsVersioned()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var general in registry.Generals.Values.Where(general => general.Id.StartsWith("classic:", StringComparison.Ordinal)))
        foreach (var skillId in general.SkillIds)
        {
            var skill = registry.GetSkill(skillId);
            Require(skill.Program is not null && skill.Program.RuntimeVersion == "skill-program-v62",
                $"{general.Id} / {skillId} must be backed by the current compiled program.");
        }
    }

    public static void ClassicContinuousCardConversionMetadataIsVersioned()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var general in registry.Generals.Values.Where(general => general.Id.StartsWith("classic:", StringComparison.Ordinal)))
        foreach (var skillId in general.SkillIds)
        {
            var skill = registry.GetSkill(skillId);
            Require(skill.Program is not null && skill.Program.RuntimeVersion == "skill-program-v62",
                $"{general.Id} / {skillId} must be backed by the current compiled program.");
        }
    }

    public static void ClassicRemainingSharedSkillsReceiveDistinctIdentities()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var general in registry.Generals.Values.Where(general => general.Id.StartsWith("classic:", StringComparison.Ordinal)))
        foreach (var skillId in general.SkillIds)
        {
            var skill = registry.GetSkill(skillId);
            Require(skill.Program is not null && skill.Program.RuntimeVersion == "skill-program-v62",
                $"{general.Id} / {skillId} must be backed by the current compiled program.");
        }
    }


    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }


    private sealed class MetadataFixture(
        SkillTag tags,
        SkillExecutionForm executionForms,
        SkillActionForm actionForms = SkillActionForm.None) : IGameContentPackage
    {
        public const string SkillId = "fixture:skill";
        public PackageManifest Manifest { get; } = new("fixture-metadata", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder) =>
            builder.AddSkill(new ContentSkillDefinition(
                SkillId,
                "Fixture",
                "Presentation is not executable metadata.")
            {
                Tags = tags,
                ExecutionForms = executionForms,
                ActionForms = actionForms
            });
    }

    private sealed class LocalizedTextFixture : IGameContentPackage
    {
        public const string SkillId = "fixture:localized-only";
        public PackageManifest Manifest { get; } = new("fixture-localized", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder) =>
            builder.AddSkill(new ContentSkillDefinition(
                SkillId,
                "Only text",
                "主公技，锁定技，限定技，觉醒技，转换技。"));
    }
}
