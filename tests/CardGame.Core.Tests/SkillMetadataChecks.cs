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
    }

    public static void ClassicLockedStateMetadataIsVersioned()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        string[] skillIds =
        [
            "classic:kuanggu",
            "classic:wushuang",
            "classic:paoxiao",
            "classic:qianxun",
            "classic:bazhen",
            "classic:hongyan",
            "classic:buqu",
            "classic:yaowu",
            "classic:yicong",
            "classic:huoshou",
            "classic:juxiang",
            "classic:yizhong",
            "classic:wuyan"
        ];

        foreach (var skillId in skillIds)
        {
            Require(migrated.Skills[skillId] is
                {
                    Tags: SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State
                }, $"Current classic content did not classify {skillId} as an explicit locked state skill.");
            Require(previous.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                }, $"Package 1.70.0 unexpectedly gained the metadata for {skillId}.");
        }

        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 71, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 70, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The locked-state migration must be isolated to package 1.71.0 and participate in content drift detection.");
    }

    public static void ClassicSharedLockedSkillsReceiveDistinctIdentities()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        var expected = new Dictionary<string, SkillKind?>
        {
            ["classic:kongcheng"] = SkillKind.Kongcheng,
            ["classic:mashu"] = null,
            ["classic:qicai"] = SkillKind.Qicai
        };

        foreach (var (skillId, kind) in expected)
        {
            Require(current.Skills[skillId] is
                {
                    Tags: SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State
                } definition && definition.LegacyKind == kind &&
                (skillId != "classic:mashu" ||
                 definition.Program is { RuntimeVersion: "skill-program-v59", MinimumRulesVersion: 169 }),
                $"Current classic content did not give {skillId} its locked-state identity.");
            Require(!previous.Skills.ContainsKey(skillId),
                $"Package 1.71.0 unexpectedly contains {skillId}.");
        }

        Require(current.Generals["classic:zhuge-liang"].SkillIds
                    .SequenceEqual(["classic:guanxing", "classic:kongcheng"]) &&
                current.Generals["classic:huang-yueying"].SkillIds
                    .SequenceEqual(["classic:jizhi", "classic:qicai"]) &&
                current.Generals["classic:ma-chao"].SkillIds
                    .SequenceEqual(["classic:tieqi", "classic:mashu"]) &&
                current.Generals["classic:pang-de"].SkillIds
                    .SequenceEqual(["classic:mashu", "classic:mengjin"]),
            "Current classic generals did not switch every shared skill reference to the distinct classic ids.");
        Require(previous.Generals["classic:zhuge-liang"].SkillIds
                    .SequenceEqual(["classic:guanxing", "standard:kongcheng"]) &&
                previous.Generals["classic:huang-yueying"].SkillIds
                    .SequenceEqual(["classic:jizhi", "standard:qicai"]) &&
                previous.Generals["classic:ma-chao"].SkillIds
                    .SequenceEqual(["classic:tieqi", "standard:mashu"]) &&
                previous.Generals["classic:pang-de"].SkillIds
                    .SequenceEqual(["standard:mashu", "classic:mengjin"]),
            "Package 1.71.0 no longer preserves its shared standard skill references.");
        Require(current.Skills["standard:kongcheng"].Tags == SkillTag.None &&
                current.Skills["standard:mashu"] is
                {
                    LegacyKind: null,
                    Tags: SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State,
                    Program.RuntimeVersion: "skill-program-v59"
                } &&
                current.Skills["standard:qicai"].Tags == SkillTag.None,
            "The classic identity migration and current rule-query package must retain independent metadata.");
    }

    public static void ClassicOptionalTriggerMetadataIsVersioned()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        string[] triggerSkillIds =
        [
            "classic:feedback",
            "classic:tiandu",
            "classic:guanxing",
            "classic:keji",
            "classic:tuxi",
            "classic:luoyi",
            "classic:luoshen",
            "classic:jizhi",
            "classic:tieqi",
            "classic:liegong",
            "classic:liuli",
            "classic:biyue",
            "classic:xiaoji",
            "classic:lianying",
            "classic:mengjin",
            "classic:jushou",
            "classic:tianxiang",
            "classic:shensu",
            "classic:guidao",
            "classic:leiji",
            "boundary:guidao",
            "boundary:leiji",
            "classic:yinghun",
            "classic:zaiqi",
            "classic:lieren",
            "classic:jujian",
            "sp:chongzhen"
        ];

        foreach (var skillId in triggerSkillIds)
        {
            Require(migrated.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.Trigger
                }, $"Current classic content did not classify {skillId} as an optional trigger skill.");
            Require(previous.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                }, $"Package 1.72.0 unexpectedly gained the trigger metadata for {skillId}.");
        }

        string[] deferredSkillIds =
        [
            "classic:shuangxiong",
            "classic:tianyi",
            "classic:longdan",
            "sp:longdan",
            "classic:fanjian"
        ];
        foreach (var skillId in deferredSkillIds)
        {
            Require(migrated.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                }, $"The optional-trigger migration incorrectly flattened deferred skill {skillId}.");
        }

        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 73, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 72, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The optional-trigger migration must be isolated to package 1.73.0 and fingerprinted.");
    }

    public static void ClassicContinuousCardConversionMetadataIsVersioned()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        string[] stateSkillIds =
        [
            "classic:qixi",
            "classic:duanliang",
            "classic:qingguo",
            "classic:longdan",
            "classic:wusheng",
            "classic:guose",
            "classic:huoji",
            "classic:kanpo",
            "classic:lianhuan",
            "classic:luanji",
            "sp:longdan"
        ];

        foreach (var skillId in stateSkillIds)
        {
            Require(migrated.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State
                }, $"Current classic content did not classify {skillId} as a continuous state skill.");
            Require(previous.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                }, $"Package 1.73.0 unexpectedly gained the state metadata for {skillId}.");
        }

        Require(migrated.Skills["classic:shuangxiong"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                } &&
                migrated.Skills["classic:tianyi"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None
                },
            "The continuous-conversion migration must not flatten Shuangxiong or Tianyi's compound execution forms.");
        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 74, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 73, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The continuous-conversion migration must be isolated to package 1.74.0 and fingerprinted.");
    }

    public static void ClassicPureActiveActionMetadataIsVersioned()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        string[] activeSkillIds =
        [
            "classic:fanjian",
            "classic:qiangxi",
            "classic:lijian",
            "classic:jieyin"
        ];

        foreach (var skillId in activeSkillIds)
        {
            Require(migrated.Skills[skillId] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None,
                    ActionForms: SkillActionForm.Active
                } definition &&
                    definition.LegacyKind is { } kind &&
                    ActiveActionCatalog.Find(kind) is not null,
                $"Current classic content did not classify {skillId} as a backed active action.");
            Require(previous.Skills[skillId].ActionForms == SkillActionForm.None,
                $"Package 1.74.0 unexpectedly gained the action metadata for {skillId}.");
        }

        Require(migrated.Skills["classic:jijiang"] is
                {
                    Tags: SkillTag.Lord,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                migrated.Skills["classic:luanji"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State,
                    ActionForms: SkillActionForm.None
                } &&
                migrated.Skills["classic:tianyi"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None,
                    ActionForms: SkillActionForm.None
                },
            "The pure-active migration must leave Jijiang, Luanji and Tianyi for compound classification.");
        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 75, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 74, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The pure-active migration must be isolated to package 1.75.0 and fingerprinted.");
    }

    public static void ClassicCompoundSkillMetadataIsVersioned()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();

        Require(migrated.Skills["classic:jijiang"] is
                {
                    Tags: SkillTag.Lord,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.Active,
                    LegacyKind: SkillKind.Jijiang
                } &&
                ActiveActionCatalog.Find(SkillKind.Jijiang) is not null,
            "Current Jijiang metadata did not preserve its Lord trigger and active action parts.");
        Require(migrated.Skills["classic:luanji"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State,
                    ActionForms: SkillActionForm.Active,
                    LegacyKind: SkillKind.Luanji
                } &&
                ActiveActionCatalog.Find(SkillKind.Luanji) is not null,
            "Current Luanji metadata did not combine its continuous conversion and active action parts.");
        Require(migrated.Skills["classic:tianyi"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State,
                    ActionForms: SkillActionForm.Active,
                    LegacyKind: SkillKind.Tianyi
                } &&
                ActiveActionCatalog.Find(SkillKind.Tianyi) is null,
            "Tianyi must not retain a second legacy active executor after its Program migration.");
        Require(migrated.Skills["classic:shuangxiong"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State | SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None,
                    LegacyKind: SkillKind.Shuangxiong
                } &&
                ActiveActionCatalog.Find(SkillKind.Shuangxiong) is null,
            "Current Shuangxiong metadata did not combine its draw trigger and turn-state parts.");

        Require(previous.Skills["classic:jijiang"] is
                {
                    Tags: SkillTag.Lord,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                previous.Skills["classic:luanji"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State,
                    ActionForms: SkillActionForm.None
                } &&
                previous.Skills["classic:tianyi"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None,
                    ActionForms: SkillActionForm.None
                } &&
                previous.Skills["classic:shuangxiong"] is
                {
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.None,
                    ActionForms: SkillActionForm.None
                },
            "Package 1.75.0 unexpectedly gained compound skill metadata.");
        Require(migrated.Skills["classic:fanjian"].ActionForms == SkillActionForm.Active &&
                migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 76, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 75, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The compound migration must preserve pure actions and be isolated to package 1.76.0.");
    }

    public static void ClassicSharedActiveSkillsReceiveDistinctIdentities()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        var stable = StandardContentRegistry.CreateWithActiveSkills();
        (string ClassicId, string StandardId, SkillKind Kind)[] skills =
        [
            ("classic:rende", "standard:rende", SkillKind.Rende),
            ("classic:zhiheng", "standard:zhiheng", SkillKind.Zhiheng),
            ("classic:qingnang", "standard:qingnang", SkillKind.Qingnang),
            ("classic:kujin", "standard:kujin", SkillKind.Kujin)
        ];

        foreach (var (classicId, standardId, kind) in skills)
        {
            Require(migrated.Skills[classicId] is
                    {
                        Tags: SkillTag.None,
                        ExecutionForms: SkillExecutionForm.None,
                        ActionForms: SkillActionForm.Active,
                        LegacyKind: var projectedKind
                    } &&
                    projectedKind == kind &&
                    ActiveActionCatalog.Find(kind) is not null,
                $"Current classic content did not register {classicId} as a backed active action.");
            Require(!previous.Skills.ContainsKey(classicId) &&
                    previous.Skills[standardId].ActionForms == SkillActionForm.None,
                $"Package 1.76.0 unexpectedly gained the distinct identity {classicId}.");
            Require(stable.Skills[standardId].ActionForms == SkillActionForm.None &&
                    !stable.Skills.ContainsKey(classicId),
                $"The stable active-skill package was mutated while migrating {classicId}.");
        }

        Require(migrated.Generals["classic:liu-bei"].SkillIds
                    .SequenceEqual(["classic:rende", "classic:jijiang"]) &&
                migrated.Generals["classic:sun-quan"].SkillIds
                    .SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]) &&
                migrated.Generals["classic:hua-tuo"].SkillIds
                    .SequenceEqual(["classic:qingnang", "standard:jijiu"]) &&
                migrated.Generals["classic:huang-gai"].SkillIds
                    .SequenceEqual(["classic:kujin"]),
            "Current classic generals did not switch to all four distinct active-skill identities.");
        Require(previous.Generals["classic:liu-bei"].SkillIds
                    .SequenceEqual(["standard:rende", "classic:jijiang"]) &&
                previous.Generals["classic:sun-quan"].SkillIds
                    .SequenceEqual(["standard:zhiheng", "classic:jiuyuan"]) &&
                previous.Generals["classic:hua-tuo"].SkillIds
                    .SequenceEqual(["standard:qingnang", "standard:jijiu"]) &&
                previous.Generals["classic:huang-gai"].SkillIds
                    .SequenceEqual(["standard:kujin"]),
            "Package 1.76.0 did not preserve the historical shared active-skill references.");
        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 77, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 76, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The shared active-skill identity migration must be isolated to package 1.77.0.");
    }

    public static void ClassicRemainingSharedSkillsReceiveDistinctIdentities()
    {
        var migrated = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals();
        var stable = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage());
        (string ClassicId, string StandardId, SkillKind Kind, SkillExecutionForm Form)[] skills =
        [
            ("classic:guicai", "standard:guicai", SkillKind.Guicai, SkillExecutionForm.Trigger),
            ("classic:ganglie", "standard:ganglie", SkillKind.Ganglie, SkillExecutionForm.Trigger),
            ("classic:jijiu", "standard:jijiu", SkillKind.Jijiu, SkillExecutionForm.State),
            ("classic:yiji", "standard:yiji", SkillKind.Yiji, SkillExecutionForm.Trigger),
            ("classic:yingzi", "standard:yingzi", SkillKind.Yingzi, SkillExecutionForm.Trigger),
            ("classic:jianxiong", "standard:jianxiong", SkillKind.Jianxiong, SkillExecutionForm.Trigger),
            ("classic:jieming", "standard:jieming", SkillKind.Jieming, SkillExecutionForm.Trigger)
        ];

        foreach (var (classicId, standardId, kind, form) in skills)
        {
            Require(migrated.Skills[classicId] is
                    {
                        Tags: SkillTag.None,
                        ExecutionForms: var projectedForm,
                        ActionForms: SkillActionForm.None,
                        LegacyKind: var projectedKind
                    } &&
                    projectedForm == form &&
                    projectedKind == kind &&
                    SkillRegistry.Get(kind).Kind == kind,
                $"Current classic content did not register {classicId} with its backed execution form.");
            Require(!previous.Skills.ContainsKey(classicId) &&
                    previous.Skills[standardId] is
                    {
                        Tags: SkillTag.None,
                        ExecutionForms: SkillExecutionForm.None,
                        ActionForms: SkillActionForm.None
                    },
                $"Package 1.78.0 unexpectedly gained the distinct identity {classicId}.");
            Require(!stable.Skills.ContainsKey(classicId) &&
                    stable.Skills[standardId] is
                    {
                        Tags: SkillTag.None,
                        ExecutionForms: SkillExecutionForm.None,
                        ActionForms: SkillActionForm.None
                    },
                $"A stable standard package was mutated while migrating {classicId}.");
        }

        Require(migrated.Generals["classic:sima-yi"].SkillIds
                    .SequenceEqual(["classic:feedback", "classic:guicai"]) &&
                migrated.Generals["classic:xiahou-dun"].SkillIds
                    .SequenceEqual(["classic:ganglie"]) &&
                migrated.Generals["classic:hua-tuo"].SkillIds
                    .SequenceEqual(["classic:qingnang", "classic:jijiu"]) &&
                migrated.Generals["classic:guo-jia"].SkillIds
                    .SequenceEqual(["classic:tiandu", "classic:yiji"]) &&
                migrated.Generals["classic:zhou-yu"].SkillIds
                    .SequenceEqual(["classic:yingzi", "classic:fanjian"]) &&
                migrated.Generals["classic:cao-cao"].SkillIds
                    .SequenceEqual(["classic:jianxiong", "classic:hujia"]) &&
                migrated.Generals["classic:xun-yu"].SkillIds
                    .SequenceEqual(["classic:quhu", "classic:jieming"]),
            "Current classic generals did not switch to all seven distinct shared-skill identities.");
        Require(previous.Generals["classic:sima-yi"].SkillIds
                    .SequenceEqual(["classic:feedback", "standard:guicai"]) &&
                previous.Generals["classic:xiahou-dun"].SkillIds
                    .SequenceEqual(["standard:ganglie"]) &&
                previous.Generals["classic:hua-tuo"].SkillIds
                    .SequenceEqual(["classic:qingnang", "standard:jijiu"]) &&
                previous.Generals["classic:guo-jia"].SkillIds
                    .SequenceEqual(["classic:tiandu", "standard:yiji"]) &&
                previous.Generals["classic:zhou-yu"].SkillIds
                    .SequenceEqual(["standard:yingzi", "classic:fanjian"]) &&
                previous.Generals["classic:cao-cao"].SkillIds
                    .SequenceEqual(["standard:jianxiong", "classic:hujia"]) &&
                previous.Generals["classic:xun-yu"].SkillIds
                    .SequenceEqual(["classic:quhu", "standard:jieming"]),
            "Package 1.78.0 did not preserve the historical shared-skill references.");
        Require(migrated.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 79, 0)) &&
                previous.Packages.Any(package =>
                    package.Id == "standard-classic-generals" && package.Version == new Version(1, 78, 0)) &&
                migrated.ContentHash != previous.ContentHash,
            "The remaining shared-skill identity migration must be isolated to package 1.79.0.");
    }

    public static void StructuredNiepanUsageReplays()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new NiepanLedgerFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 5,
            ModeId = NiepanLedgerFixture.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Niepan ledger fixture failed to start.");
        for (var step = 0; step < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Niepan ledger fixture failed to reach play.");

        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill &&
            item.ProgramSkillId == NiepanLedgerFixture.DyingSkillId);
        Require(game.Submit(new UseProgramSkillCommand(
                0,
                action.ProgramSkillId!,
                action.ProgramActivationId!,
                [],
                [],
                game.Revision,
                game.PendingDecision!.PromptId)).Accepted,
            "The fixture could not enter dying through a replayable command.");
        var dying = game.PendingDecision ?? throw new InvalidOperationException("The fixture did not publish dying rescue.");
        var niepan = dying.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            choice.Parameters.GetValueOrDefault("skill-id") == "classic:niepan");
        Require(game.Submit(new AnswerPromptCommand(0, dying.PromptId, niepan.Id, game.Revision)).Accepted,
            "Structured Niepan was rejected.");
        Require(GetStructuredNiepanUsage(game) == 1,
            "Current program Niepan must consume its instance-scoped game record.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(GetStructuredNiepanUsage(restored) == 1 &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A completed program Niepan use must restore from its accepted command prefix.");

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

    private static int GetStructuredNiepanUsage(GameEngine game)
    {
        var field = typeof(GameEngine).GetField(
            "_skillRuntimeState",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var state = (SkillRuntimeStateStore)field.GetValue(game)!;
        return state.GetUsage(
            0,
            "classic:niepan",
            "activation@template:primary:classic:niepan",
            SkillUsageScope.Game);
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
            {"schemaVersion":59,"skills":[{"id":"fixture:lose-hp","revision":1,
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
