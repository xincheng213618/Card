using CardGame.Core;

internal static class MatchSkillBindingIndexChecks
{
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

    private static MatchSkillBindingIndex Index(FixtureData fixture) => new(
        id => fixture.Definitions[id], false);

    private static FixtureData Fixture()
    {
        const string rich = "fixture:index-rich", plain = "fixture:index-plain", lord = "fixture:index-lord";
        var catalog = SkillProgramCatalog.Load("""
            {"schemaVersion":62,"skills":[
              {"id":"fixture:index-rich","revision":1,"minimumRulesVersion": 171,
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


