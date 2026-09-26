using CardGame.Core;

internal static class MatchSkillBindingIndexChecks
{
    public static void ReadPathsReuseShardsAndPartitionNumericBuckets()
    {
        var fixture = Fixture();
        var definitions = fixture.Definitions.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        for (var i = 0; i < 128; i++)
            definitions[$"fixture:unused-{i}"] = new($"fixture:unused-{i}", "Unused", "Fixture");
        var resolves = 0;
        var index = new MatchSkillBindingIndex(id => { resolves++; return definitions[id]; }, false);
        var players = Enumerable.Range(0, 8).Select(seat => Player(seat, Role.Rebel)).ToArray();
        for (var seat = 0; seat < players.Length; seat++)
            players[seat].SkillGrants.Grant(Grant($"g:{seat}", seat % 2 == 0 ? fixture.RichId : fixture.PlainId, $"i:{seat}"));

        var shards = players.Select(index.GetShard).ToArray();
        Require(index.CachedSeatCount == 8 && index.TotalRebuildCount == 8 && resolves == 8,
            "Initial indexing must resolve only each seat's bound definition.");
        for (var i = 0; i < players.Length; i++)
            Require(ReferenceEquals(shards[i], index.GetShard(players[i])) && index.GetRebuildCount(i) == 1,
                "Repeated reads must return the same shard without rebuilding.");
        Require(resolves == 8, "Unrelated registry definitions must not be resolved on a cached read path.");

        var rich = shards[0];
        Require(rich.GetNumericModifiers(SkillRuleQuery.AttackRange).Count == 1 &&
                rich.GetNumericModifiers(SkillRuleQuery.AttackRange).Single().Modifier.Query == SkillRuleQuery.AttackRange &&
                rich.GetNumericModifiers(SkillRuleQuery.DrawCount).Count == 1 &&
                rich.GetNumericModifiers(SkillRuleQuery.DrawCount).Single().Modifier.Query == SkillRuleQuery.DrawCount,
            "Numeric buckets must contain only modifiers for their requested query.");
        Require(rich.GetNumericModifiers(SkillRuleQuery.IncomingDistance).Count == 0 &&
                rich.GetInstanceTriggers(SkillProgramTriggerWindow.AfterDamageApplied).Count == 0 &&
                rich.GetUniqueTriggers(SkillProgramTriggerWindow.AfterDamageApplied).Count == 0,
            "Empty query and trigger buckets must remain empty.");
    }

    public static void SourcesDeduplicateInstancesAndUniqueProgramBuckets()
    {
        var fixture = Fixture();
        var index = Index(fixture);
        var player = Player(0, Role.Rebel);
        player.SkillGrants.Grant(Grant("source:a", fixture.RichId, "shared"));
        player.SkillGrants.Grant(Grant("source:b", fixture.RichId, "shared"));
        player.SkillGrants.Grant(Grant("source:c", fixture.RichId, "independent"));
        var shard = index.GetShard(player);

        Require(shard.ProgramInstances.Count == 2 && shard.Programs.Count == 1 &&
                shard.GetNumericModifiers(SkillRuleQuery.AttackRange).Count == 2 &&
                shard.GetInstanceTriggers(SkillProgramTriggerWindow.PlayEnding).Count == 2 &&
                shard.GetUniqueTriggers(SkillProgramTriggerWindow.PlayEnding).Count == 1,
            "Same-instance sources must dedupe while independent instances remain instance-scoped.");
        var unique = shard.GetUniqueTriggers(SkillProgramTriggerWindow.PlayEnding).Single();
        Require(unique.SkillId == fixture.RichId &&
                shard.HasInstance(unique.SkillId, unique.SkillInstanceId) &&
                shard.ProgramInstances.Any(instance =>
                    instance.SkillId == unique.SkillId &&
                    instance.SkillInstanceId == unique.SkillInstanceId),
            "A deduplicated trigger must retain an active grant identity accepted by the shared executor.");
        Require(shard.ActivationPrograms.Count == 1 && shard.ViewAsPrograms.Count == 1 &&
                shard.PassiveRulePrograms.Count == 1,
            "Program, activation, view-as and unique trigger projections must fold by SkillId.");
    }

    public static void LordTemplateGateDoesNotSuppressIndependentSources()
    {
        var fixture = Fixture();
        var index = Index(fixture);
        var player = Player(0, Role.Rebel);
        player.SkillGrants.Grant(Grant("template:lord", fixture.LordId, "template-lord", CharacterState.PrimarySkillSource));
        player.SkillGrants.Grant(Grant("runtime:lord", fixture.LordId, "runtime-lord", "acquired:test"));
        player.SkillGrants.Grant(Grant("runtime:plain", fixture.PlainId, "runtime-plain", "acquired:test"));
        var withRuntime = index.GetShard(player);
        Require(withRuntime.HasSkill(fixture.LordId) && withRuntime.HasInstance(fixture.LordId, "runtime-lord") &&
                withRuntime.HasSkill(fixture.PlainId),
            "A non-Lord must retain an independent Lord-skill source and unrelated valid skills.");
        player.SkillGrants.RemoveGrant("runtime:lord");
        var withoutRuntime = index.GetShard(player);
        Require(!withoutRuntime.HasSkill(fixture.LordId) && !withoutRuntime.HasInstance(fixture.LordId, "template-lord") &&
                withoutRuntime.HasSkill(fixture.PlainId),
            "Removing the independent source must not expose the gated Lord template or remove another skill.");
    }

    public static void OnlyBindingStampChangesRebuildOneSeatAndOldShardsStayFrozen()
    {
        var fixture = Fixture();
        var index = new MatchSkillBindingIndex(id => fixture.Definitions[id], true);
        var first = Player(0, Role.Rebel); var second = Player(1, Role.Rebel);
        first.SkillGrants.Grant(Grant("p", fixture.RichId, "p", CharacterState.PrimarySkillSource));
        second.SkillGrants.Grant(Grant("s", fixture.PlainId, "s", "acquired:test"));
        first.GeneralRevealed = false;
        var hidden = index.GetShard(first); var other = index.GetShard(second);
        Require(hidden.ActiveGrants.Count == 0, "Hidden national template must not enter the shard.");

        first.Hp = 1; first.MaxHp = 7; first.RoleRevealed = !first.RoleRevealed; first.FactionRevealed = !first.FactionRevealed;
        Require(ReferenceEquals(hidden, index.GetShard(first)) && ReferenceEquals(other, index.GetShard(second)),
            "HP, max HP, role/faction reveal and other external facts must not invalidate bindings.");
        first.GeneralRevealed = true;
        var revealed = index.GetShard(first);
        Require(!ReferenceEquals(hidden, revealed) && revealed.HasSkill(fixture.RichId) &&
                index.GetRebuildCount(0) == 2 && index.GetRebuildCount(1) == 1 && hidden.ActiveGrants.Count == 0,
            "Visibility must rebuild only its seat and must not mutate the old shard.");
        Require(first.SkillGrants.SetEnabled("p", false), "The fixture grant must disable once.");
        var disabled = index.GetShard(first);
        Require(disabled.ActiveGrants.Count == 0 && index.GetRebuildCount(0) == 3 &&
                index.GetRebuildCount(1) == 1 && ReferenceEquals(other, index.GetShard(second)) &&
                revealed.HasSkill(fixture.RichId),
            "A grant revision must rebuild only its seat and keep earlier shards immutable.");
        Require(first.SkillGrants.SetEnabled("p", true), "The fixture grant must re-enable once.");
        var reenabled = index.GetShard(first);
        Require(reenabled.HasSkill(fixture.RichId) && index.GetRebuildCount(0) == 4 &&
                ReferenceEquals(other, index.GetShard(second)),
            "Re-enabling a grant must refresh only the affected seat.");
        Require(!first.SkillGrants.RemoveGrant("missing") && !first.SkillGrants.SetEnabled("p", true) &&
                ReferenceEquals(reenabled, index.GetShard(first)),
            "No-op ownership mutations and reads must retain the rebuilt shard.");
    }

    public static void MatchIndexesDoNotShareCaches()
    {
        var fixture = Fixture(); var player = Player(0, Role.Rebel);
        player.SkillGrants.Grant(Grant("g", fixture.RichId, "i"));
        var first = Index(fixture); var second = Index(fixture);
        var a = first.GetShard(player); var b = second.GetShard(player);
        Require(!ReferenceEquals(a, b) && first.TotalRebuildCount == 1 && second.TotalRebuildCount == 1,
            "Match-local indexes must not share shards or rebuild counters.");
    }

    private static MatchSkillBindingIndex Index(FixtureData fixture) => new(
        id => fixture.Definitions[id], false);

    private static FixtureData Fixture()
    {
        const string rich = "fixture:index-rich", plain = "fixture:index-plain", lord = "fixture:index-lord";
        var catalog = SkillProgramCatalog.Load("""
            {"schemaVersion":61,"skills":[
              {"id":"fixture:index-rich","revision":1,"minimumRulesVersion":170,
               "modifiers":[
                 {"id":"attack","query":"attackRange","operation":"add","value":1,"priority":0},
                 {"id":"draw","query":"drawCount","operation":"add","value":1,"priority":0}],
               "viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],
               "activations":[{"id":"invoke","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                 "targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":1}]}],
               "triggers":[{"id":"ending","window":"playEnding","subject":"owner","optional":true,"priority":0,
                 "effects":[{"op":"draw","target":"owner","amount":1}]}],"contributions":[],"cardIdentities":[]}]}
            """, """
            {"schemaVersion":3,"skills":{
              "fixture:index-rich":{"name":"Rich","description":"Fixture"}}}
            """);
        var definitions = new Dictionary<string, ContentSkillDefinition>(StringComparer.Ordinal)
        {
            [rich] = new(rich, "Rich", "Fixture") { Program = catalog.Programs[rich] },
            [plain] = new(plain, "Plain", "Fixture"),
            [lord] = new(lord, "Lord", "Fixture") { Tags = SkillTag.Lord }
        };
        return new(rich, plain, lord, definitions);
    }

    private static SkillGrant Grant(string grantId, string skillId, string instanceId, string source = "acquired:test") =>
        new(grantId, skillId, instanceId, source);

    private static CharacterState Player(int seat, Role role) => new()
    {
        Seat = seat, Name = $"P{seat}", IsHuman = seat == 0, Role = role, RoleRevealed = true,
        FactionRevealed = true, General = new GeneralDefinition($"fixture:g{seat}", $"G{seat}", "supporter",
            []), GeneralSelected = true, GeneralRevealed = true, MaxHp = 4, Hp = 4
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record FixtureData(string RichId, string PlainId, string LordId,
        IReadOnlyDictionary<string, ContentSkillDefinition> Definitions);
}


