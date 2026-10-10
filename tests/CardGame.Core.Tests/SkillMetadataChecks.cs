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

    public static void RuntimeUsageAndReset()
    {
        var state = new SkillRuntimeStateStore();
        Require(state.TryConsumeUsage(2, "fixture:skill", "game", SkillUsageScope.Game, 1) &&
                !state.TryConsumeUsage(2, "fixture:skill", "game", SkillUsageScope.Game, 1),
            "Game-scoped usage must enforce its explicit limit.");
        Require(state.TryConsumeUsage(2, "fixture:skill", "turn", SkillUsageScope.Turn, 2) &&
                state.TryConsumeUsage(2, "fixture:skill", "round", SkillUsageScope.Round, 1) &&
                state.TryConsumeUsage(2, "fixture:skill", "phase", SkillUsageScope.Phase, 1) &&
                state.TryConsumeUsage(2, "fixture:skill", "event-41", SkillUsageScope.Event, 1) &&
                !state.TryConsumeUsage(2, "fixture:skill", "event-41", SkillUsageScope.Event, 1) &&
                state.TryConsumeUsage(3, "fixture:off-turn", "phase", SkillUsageScope.Phase, 1),
            "Event, phase, turn and round scopes must record independently for every owner.");

        Require(state.ClearUsage(2, "fixture:skill", "event-41", SkillUsageScope.Event) &&
                state.GetUsage(2, "fixture:skill", "event-41", SkillUsageScope.Event) == 0 &&
                !state.ClearUsage(2, "fixture:skill", "event-41", SkillUsageScope.Event),
            "Closing one event must remove only its exact composite usage window.");
        state.TryConsumeUsage(2, "fixture:skill", "event-42", SkillUsageScope.Event, 1);

        state.ResetPhase();
        Require(state.GetUsage(2, "fixture:skill", "phase", SkillUsageScope.Phase) == 0 &&
                state.GetUsage(2, "fixture:skill", "event-42", SkillUsageScope.Event) == 0 &&
                state.GetUsage(3, "fixture:off-turn", "phase", SkillUsageScope.Phase) == 0 &&
                state.GetUsage(2, "fixture:skill", "turn", SkillUsageScope.Turn) == 1 &&
                state.GetUsage(2, "fixture:skill", "round", SkillUsageScope.Round) == 1 &&
                state.GetUsage(2, "fixture:skill", "game", SkillUsageScope.Game) == 1,
            "A phase boundary must expire every owner's phase record and preserve longer scopes.");

        state.TryConsumeUsage(2, "fixture:skill", "phase", SkillUsageScope.Phase, 1);
        state.TryConsumeUsage(3, "fixture:off-turn", "turn", SkillUsageScope.Turn, 1);
        state.ResetTurn();
        Require(state.GetUsage(2, "fixture:skill", "phase", SkillUsageScope.Phase) == 0 &&
                state.GetUsage(2, "fixture:skill", "turn", SkillUsageScope.Turn) == 0 &&
                state.GetUsage(3, "fixture:off-turn", "turn", SkillUsageScope.Turn) == 0 &&
                state.GetUsage(2, "fixture:skill", "round", SkillUsageScope.Round) == 1 &&
                state.GetUsage(2, "fixture:skill", "game", SkillUsageScope.Game) == 1,
            "A turn boundary must refresh off-turn owners and preserve round and game records.");

        state.ResetRound();
        Require(state.GetUsage(2, "fixture:skill", "round", SkillUsageScope.Round) == 0 &&
                state.GetUsage(2, "fixture:skill", "game", SkillUsageScope.Game) == 1,
            "A round boundary must preserve a game-scoped limited-skill record.");

        state.RegisterConversionSkill(2, "fixture:skill", SkillPolarity.Yin);
        Require(state.GetConversionState(2, "fixture:skill") == SkillPolarity.Yin &&
                state.ToggleConversionState(2, "fixture:skill") == SkillPolarity.Yang,
            "A conversion skill must execute from and then switch away from its registered side.");
        state.ResetSkill(2, "fixture:skill");
        Require(state.GetConversionState(2, "fixture:skill") == SkillPolarity.Yin &&
                state.GetUsage(2, "fixture:skill", "game", SkillUsageScope.Game) == 0,
            "Skill reset must restore the registered initial side and clear every usage scope.");
        SnapshotOrderingAndIsolation();
    }

    private static void SnapshotOrderingAndIsolation()
    {
        const string skill = "fixture:snapshot-skill";
        var state = new SkillRuntimeStateStore();
        var empty = state.CreateSnapshot(2, skill, false);
        Require(empty.SkillId == skill && !empty.IsAcquired && empty.Polarity is null && empty.Usages.Count == 0,
            "An unused skill snapshot retains its identity and absent conversion state.");
        RequireThrows<NotSupportedException>(() => ((IList<SkillUsageStateSnapshot>)empty.Usages)
            .Add(new("intruder", SkillUsageScope.Game, 1)));
        RequireThrows<ArgumentOutOfRangeException>(() => state.CreateSnapshot(-1, skill, false));
        RequireThrows<ArgumentException>(() => state.CreateSnapshot(2, " ", false));

        var records = new[]
        {
            new SkillUsageStateSnapshot("same", SkillUsageScope.Event, 1),
            new SkillUsageStateSnapshot("z", SkillUsageScope.Phase, 1),
            new SkillUsageStateSnapshot("same", SkillUsageScope.Game, 2),
            new SkillUsageStateSnapshot("a", SkillUsageScope.Phase, 1),
            new SkillUsageStateSnapshot("A", SkillUsageScope.Phase, 1),
            new SkillUsageStateSnapshot("same", SkillUsageScope.Turn, 1),
            new SkillUsageStateSnapshot("same", SkillUsageScope.Round, 1)
        };
        foreach (var record in records)
            for (var i = 0; i < record.Count; i++)
                Require(state.TryConsumeUsage(2, skill, record.UsageId, record.Scope, record.Count),
                    "The snapshot fixture must record each actual use.");
        state.TryConsumeUsage(3, skill, "foreign-owner", SkillUsageScope.Game, 1);
        state.TryConsumeUsage(2, "fixture:other-skill", "foreign-skill", SkillUsageScope.Game, 1);
        state.RegisterConversionSkill(2, skill, SkillPolarity.Yin);
        var captured = state.CreateSnapshot(2, skill, true);
        var expected = records.OrderBy(record => record.Scope)
            .ThenBy(record => record.UsageId, StringComparer.Ordinal).ToArray();
        Require(captured.IsAcquired && captured.Polarity == SkillPolarity.Yin &&
                captured.Usages.SequenceEqual(expected) && empty.Usages.Count == 0,
            "Usage snapshots preserve scope/ordinal ordering and exclude other owners and skills.");
        RequireThrows<NotSupportedException>(() => ((IList<SkillUsageStateSnapshot>)captured.Usages)[0] =
            new("intruder", SkillUsageScope.Game, 1));

        state.ClearUsage(2, skill, "same", SkillUsageScope.Event);
        state.ResetPhase();
        state.ToggleConversionState(2, skill);
        var after = state.CreateSnapshot(2, skill, false);
        Require(!after.IsAcquired && after.Polarity == SkillPolarity.Yang &&
                after.Usages.SequenceEqual(expected.Where(record => (int)record.Scope < (int)SkillUsageScope.Phase)) &&
                captured.Usages.SequenceEqual(expected) && captured.Polarity == SkillPolarity.Yin,
            "Fresh snapshots see resets and polarity changes while exposed snapshots stay frozen.");
        state.ResetSkill(2, skill);
        Require(state.CreateSnapshot(2, skill, false) is { Polarity: SkillPolarity.Yin, Usages.Count: 0 } &&
                state.CreateSnapshot(3, skill, false).Usages.Count == 1 &&
                state.CreateSnapshot(2, "fixture:other-skill", false).Usages.Count == 1,
            "Skill reset clears only its own snapshot records and restores its registered polarity.");
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

    private sealed class NiepanLedgerFixture : IGameContentPackage
    {
        public const string ModeId = "identity:classic-niepan-ledger-5";
        public const string DyingSkillId = "fixture:lose-hp";
        private const string DeckId = "fixture:niepan-ledger-deck";
        private static readonly string[] GeneralIds = Enumerable.Range(0, 5)
            .Select(index => $"fixture:niepan-ledger-{index}")
            .ToArray();

        public PackageManifest Manifest { get; } = new(
            "fixture-niepan-ledger",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 68, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(DyingRules, DyingPresentation);
            builder.AddSkill(new ContentSkillDefinition(DyingSkillId, "失去体力", "令自己失去五点体力。")
            {
                Program = catalog.Programs[DyingSkillId],
                ExecutionForms = SkillExecutionForm.Trigger
            });
            foreach (var id in GeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "涅槃账本测试",
                    "pang_tong",
                    "classic:niepan",
                    "shu",
                    BaseHp: 1,
                    AdditionalSkillIds: [DyingSkillId]));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "涅槃账本牌堆",
                InitialHandSize: 0,
                DrawPerTurn: 0,
                [new ContentDeckCardCount("standard:slash", 30)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "涅槃账本",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 1,
                GeneralPoolIds: GeneralIds));
        }

        private const string DyingRules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:lose-hp","revision":1,
            "modifiers":[],"viewAs":[],"activations":[{"id":"invoke","minCards":0,"maxCards":0,
            "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{"op":"loseHp","target":"owner","amount":5}]}]}]}
            """;

        private const string DyingPresentation = """
            {"schemaVersion":3,"skills":{"fixture:lose-hp":{"name":"失去体力",
            "description":"令自己失去五点体力。"}}}
            """;
    }
}
