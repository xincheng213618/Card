using System.Text.Json;
using CardGame.Content.Standard;
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
        Require(shard.GetSingleCardConversions(CardKind.Slash).Count == 2 &&
                shard.GetSingleCardConversions(CardKind.Peach).Count == 0 &&
                shard.GetCardPolicies(SkillProgramCardPolicyKind.IgnoreUseDistance).Count == 2 &&
                shard.GetCardPolicies(SkillProgramCardPolicyKind.IgnoreUseDistanceBeforeDealingDamage).Count == 2,
            "Conversion and policy slots contain only registered instances, including the distance-policy alias.");
        player.SkillGrants.SetEnabled("source:a", false);
        Require(index.GetShard(player).GetSingleCardConversions(CardKind.Slash).Count == 2,
            "Disabling one duplicate source retains the shared instance's slot.");
        player.SkillGrants.SetEnabled("source:b", false);
        Require(index.GetShard(player).GetSingleCardConversions(CardKind.Slash).Count == 1,
            "Disabling the final shared source removes only that instance's slots.");
        player.SkillGrants.RemoveGrant("source:c");
        Require(index.GetShard(player).GetCardPolicies(SkillProgramCardPolicyKind.IgnoreUseDistance).Count == 0 &&
                shard.GetSingleCardConversions(CardKind.Slash).Count == 2,
            "Removing the last source empties the current slots without mutating previously prepared shards.");
        var table = Enumerable.Range(0, 8).Select(seat => Player(seat, Role.Rebel)).ToArray();
        foreach (var participant in table)
        {
            participant.SkillGrants.Grant(Grant($"seat:{participant.Seat}", fixture.RichId, $"instance:{participant.Seat}"));
            Require(index.GetShard(participant).GetSingleCardConversions(CardKind.Slash).Single().Source.SkillInstanceId == $"instance:{participant.Seat}",
                "Eight players retain separate registered conversion slots.");
        }
        table[3].SkillGrants.RemoveGrant("seat:3");
        Require(index.GetShard(table[3]).GetSingleCardConversions(CardKind.Slash).Count == 0 &&
                index.GetShard(table[4]).GetSingleCardConversions(CardKind.Slash).Count == 1,
            "Removing one player's slot does not remove another player's binding.");
        var lord = Player(9, Role.Rebel);
        lord.SkillGrants.Grant(Grant("printed", fixture.LordId, "printed", CharacterState.PrimarySkillSource));
        Require(!index.GetShard(lord).HasSkill(fixture.LordId), "An unqualified printed Lord slot is absent.");
        lord = Player(9, Role.Lord);
        lord.SkillGrants.Grant(Grant("printed", fixture.LordId, "printed", CharacterState.PrimarySkillSource));
        Require(index.GetShard(lord).HasSkill(fixture.LordId), "An updated role input refreshes qualification even with the same grant revision.");
        PreparedTriggerCandidatesRetainOrderAndOwnership();
    }

    private static void PreparedTriggerCandidatesRetainOrderAndOwnership()
    {
        const string first = "fixture:candidate-a", second = "fixture:candidate-b", jinqu = "classic:jinqu";
        var catalog = SkillProgramCatalog.Load("""
            {"schemaVersion":$SCHEMA$,"skills":[
              {"id":"fixture:candidate-a","revision":1,"triggers":[
                {"id":"later","window":"drawPhaseEnded","subject":"owner","optional":true,
                 "effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"fixture:candidate-b","revision":1,"triggers":[
                {"id":"zeta","window":"drawPhaseEnded","subject":"owner","optional":true,"choiceGroup":"a-group",
                 "effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"free","window":"drawPhaseEnded","subject":"owner","optional":true,
                 "effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"named","window":"drawPhaseEnded","subject":"owner","optional":true,"priority":10,
                 "usageScope":"round","usageLimit":1,"namedUsageGroup":"shared",
                 "effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"urgent","window":"drawPhaseEnded","subject":"owner","optional":true,"priority":30,
                 "effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"alpha","window":"drawPhaseEnded","subject":"owner","optional":true,"choiceGroup":"a-group",
                 "effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"dynamic","window":"drawPhaseEnded","subject":"owner","optional":true,"priority":10,
                 "usageScope":"round","dynamicUsageLimit":{"kind":"alivePlayersCapped","cap":3},
                 "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString()),
            JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [first] = new { name = "First", description = "Candidate order" },
                    [second] = new { name = "Second", description = "Candidate order", triggerChoices = new { alpha = "Alpha", zeta = "Zeta" } }
                }
            }));
        var definitions = catalog.Programs.ToDictionary(pair => pair.Key,
            pair => new ContentSkillDefinition(pair.Key, "Candidate", "Candidate order") { Program = pair.Value }, StringComparer.Ordinal);
        var actual = SkillProgramCatalog.Load(Resource("classic-wang-ji.rules.json"), Resource("classic-wang-ji.presentation.json"));
        foreach (var (id, program) in actual.Programs)
            definitions.Add(id, new(id, "Wang Ji", "Actual named-turn-count definition") { Program = program });
        var index = new MatchSkillBindingIndex(id => definitions[id], false);
        var owner = Player(6, Role.Rebel);
        foreach (var skill in new[] { second, first, jinqu })
        foreach (var instance in new[] { "z-instance", "a-instance" })
            owner.SkillGrants.Grant(Grant(skill + ":" + instance, skill, instance));
        var shard = index.GetShard(owner);
        Verify(shard, SkillProgramTriggerWindow.DrawPhaseEnded, 0);
        Verify(shard, SkillProgramTriggerWindow.DrawPhaseEnded, 7);
        Verify(shard, SkillProgramTriggerWindow.TurnEnding, 0);
        var cached = shard.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded);
        Require(cached.Count == 12 && cached.Count(c => c.BindingId == "named") == 1 && cached.Count(c => c.BindingId == "dynamic") == 1 &&
                cached.Where(c => c.BindingId is "named" or "dynamic").All(c => c.SkillInstanceId == "a-instance") &&
                shard.GetTriggerCandidates(SkillProgramTriggerWindow.TurnEnding) is [{ SkillId: jinqu, SkillInstanceId: "a-instance" }],
            "Named, dynamic and actual discard-to-named-turn-count triggers share usage across instances while normal bindings remain instance-scoped.");
        Require(cached[0].BindingId == "urgent" && cached[0].SkillInstanceId == "a-instance" && cached[1].SkillInstanceId == "z-instance" &&
                cached.Skip(4).Select(c => (c.SkillId, c.SkillInstanceId, c.BindingId)).SequenceEqual(new[]
                {
                    (first, "a-instance", "later"), (first, "z-instance", "later"),
                    (second, "a-instance", "alpha"), (second, "a-instance", "zeta"), (second, "a-instance", "free"),
                    (second, "z-instance", "alpha"), (second, "z-instance", "zeta"), (second, "z-instance", "free")
                }), "Priority, ordinal skill/instance order, choice-group order and binding order are unchanged.");
        Require(ReferenceEquals(cached, shard.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded)) &&
                shard.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded, 7).All(c => c.OccurrenceIndex == 7) && cached.All(c => c.OccurrenceIndex == 0),
            "Occurrence zero reuses the immutable list; a repeated damage/HP occurrence copies only its occurrence index.");
        var empty = shard.GetTriggerCandidates(SkillProgramTriggerWindow.PlayEnding);
        Require(ReferenceEquals(empty, shard.GetTriggerCandidates(SkillProgramTriggerWindow.PlayEnding, 7)), "An empty slot also reuses its list for nonzero occurrences.");
        var exposed = (IList<ProgramTriggerCandidate>)cached;
        var rejected = false;
        try { exposed[0] = cached[0] with { OwnerSeat = 0 }; }
        catch (NotSupportedException) { rejected = true; }
        Require(exposed.IsReadOnly && rejected && cached.All(c => c.OwnerSeat == owner.Seat), "Prepared candidate lists cannot be changed by observers.");
        owner.Hp--;
        Require(ReferenceEquals(shard, index.GetShard(owner)), "Ordinary live HP changes do not invalidate definition-only candidate metadata.");
        for (var i = 0; i < 32; i++) _ = index.GetShard(owner).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++) _ = index.GetShard(owner).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded);
        Require(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Warm registered occurrence-zero candidate queries allocate no sorting, projection or candidate objects.");
        owner.SkillGrants.SetEnabled(second + ":a-instance", false);
        var disabled = index.GetShard(owner);
        Verify(disabled, SkillProgramTriggerWindow.DrawPhaseEnded, 0);
        Require(!ReferenceEquals(shard, disabled) && disabled.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded)
                .Where(c => c.SkillId == second).All(c => c.SkillInstanceId == "z-instance") && cached.Count == 12,
            "Losing the first shared-usage instance selects the remaining instance without mutating earlier lists.");
        owner.SkillGrants.SetEnabled(second + ":a-instance", true);
        Verify(index.GetShard(owner), SkillProgramTriggerWindow.DrawPhaseEnded, 0);
        owner.SkillGrants.RemoveGrant(second + ":a-instance");
        owner.SkillGrants.RemoveGrant(second + ":z-instance");
        Require(index.GetShard(owner).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).All(c => c.SkillId != second), "Removing the last source removes its prepared candidates.");
        owner.SkillGrants.Grant(Grant("later-grant", second, "new-instance"));
        Verify(index.GetShard(owner), SkillProgramTriggerWindow.DrawPhaseEnded, 7);
        Require(index.GetShard(owner).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Where(c => c.SkillId == second)
                .All(c => c.SkillInstanceId == "new-instance"), "A new same-skill source uses its actual identity rather than the old cached instance.");
        foreach (var seat in Enumerable.Range(0, 8))
        {
            var participant = Player(seat, Role.Rebel);
            participant.SkillGrants.Grant(Grant("seat-source", first, "seat-instance"));
            Require(index.GetShard(participant).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Single().OwnerSeat == seat,
                "Prepared candidate owner seats belong to each of the eight participants.");
        }
        var hidden = Player(2, Role.Rebel);
        hidden.GeneralRevealed = false;
        hidden.SkillGrants.Grant(Grant("printed-candidate", first, "printed-candidate", CharacterState.PrimarySkillSource));
        var national = new MatchSkillBindingIndex(id => definitions[id], true);
        var hiddenShard = national.GetShard(hidden);
        Require(hiddenShard.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Count == 0, "An unrevealed national-war printed source has no candidates.");
        hidden.GeneralRevealed = true;
        Require(national.GetShard(hidden).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Single().OwnerSeat == 2 &&
                hiddenShard.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Count == 0, "Reveal invalidation exposes the correctly owned immutable candidate.");
        const string suppression = "fixture:candidate-suppression";
        definitions.Add(suppression, new(suppression, "Suppression", "HP-sensitive source qualification")
            { Tags = SkillTag.Locked, SuppressionRule = new(2) });
        var sensitive = new MatchSkillBindingIndex(id => definitions[id], false, _ => true);
        var suppressed = Player(1, Role.Rebel);
        suppressed.SkillGrants.Grant(Grant("ordinary", first, "ordinary"));
        suppressed.SkillGrants.Grant(Grant("suppression", suppression, "suppression"));
        var beforeSuppression = sensitive.GetShard(suppressed);
        suppressed.Hp = 2;
        Require(sensitive.GetShard(suppressed).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Count == 0 &&
                beforeSuppression.GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Count == 1,
            "HP-sensitive qualification invalidates current candidate slots while preserving earlier immutable lists.");
        suppressed.Hp = 3;
        Require(sensitive.GetShard(suppressed).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Single().SkillInstanceId == "ordinary",
            "Ending suppression restores the original still-owned source identity.");
        var lordDefinitions = new Dictionary<string, ContentSkillDefinition>(definitions, StringComparer.Ordinal)
            { [first] = definitions[first] with { Tags = SkillTag.Lord } };
        var roles = new MatchSkillBindingIndex(id => lordDefinitions[id], false);
        var unqualified = Player(0, Role.Rebel);
        unqualified.SkillGrants.Grant(Grant("printed", first, "printed", CharacterState.PrimarySkillSource));
        Require(roles.GetShard(unqualified).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Count == 0,
            "An unqualified printed Lord source contributes no cached candidates.");
        var qualified = Player(0, Role.Lord);
        qualified.SkillGrants.Grant(Grant("printed", first, "printed", CharacterState.PrimarySkillSource));
        Require(roles.GetShard(qualified).GetTriggerCandidates(SkillProgramTriggerWindow.DrawPhaseEnded).Single().OwnerSeat == 0,
            "Role qualification refreshes candidate slots even when the skill-grant revision is unchanged.");

        void Verify(SkillBindingShard value, SkillProgramTriggerWindow window, int occurrence)
        {
            foreach (var binding in value.GetInstanceTriggers(window))
                Require(ReferenceEquals(binding.Trigger, Trigger(binding.SkillId, binding.Trigger.Id)), "The index and execution resolver refer to the same immutable trigger definition.");
            var old = value.GetInstanceTriggers(window)
                .OrderByDescending(b => b.Trigger.Priority).ThenBy(b => b.SkillId, StringComparer.Ordinal)
                .ThenBy(b => b.SkillInstanceId, StringComparer.Ordinal).ThenBy(b => b.Trigger.ChoiceGroup ?? b.Trigger.Id, StringComparer.Ordinal)
                .ThenBy(b => b.Trigger.Id, StringComparer.Ordinal)
                .Select(b => new ProgramTriggerCandidate(owner.Seat, b.SkillId, b.Trigger.Id, b.SkillInstanceId, b.Program.GameplayHash, b.Trigger.Priority, occurrence))
                .DistinctBy(c => (c.SkillId, c.BindingId, Trigger(c.SkillId, c.BindingId).NamedUsageGroup is not null ||
                    Trigger(c.SkillId, c.BindingId).DynamicUsageLimit is not null || Trigger(c.SkillId, c.BindingId).Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardHandToNamedTurnCount) ? "" : c.SkillInstanceId))
                .ToArray();
            Require(JsonSerializer.Serialize(old) == JsonSerializer.Serialize(value.GetTriggerCandidates(window, occurrence)), "Prepared candidates preserve the full ordered JSON of the original resolver-based projection and distinct contract.");
        }
        SkillProgramTrigger Trigger(string skill, string binding) => ProgramInstructionResolver.Default.Resolve(definitions[skill].Program!, ProgramInstructionSourceKind.Trigger, binding).Trigger!;
        static string Resource(string name)
        {
            using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + name)
                ?? throw new InvalidOperationException("Missing actual Wang Ji rules/presentation resource.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }



    private static MatchSkillBindingIndex Index(FixtureData fixture) => new(
        id => fixture.Definitions[id], false);

    private static FixtureData Fixture()
    {
        const string rich = "fixture:index-rich", plain = "fixture:index-plain", lord = "fixture:index-lord";
        var catalog = SkillProgramCatalog.Load("""
            {"schemaVersion":62,"skills":[
              {"id":"fixture:index-rich","revision":1,"minimumRulesVersion": 171,
               "cardPolicies":[{"id":"distance","kind":"ignoreUseDistanceBeforeDealingDamage","cardKinds":["supplyShortage"]}],
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


