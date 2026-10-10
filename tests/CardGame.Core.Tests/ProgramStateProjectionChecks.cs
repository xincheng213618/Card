using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramStateProjectionChecks
{
    private const string Skill = "fixture:program-state-projection", Plain = "fixture:program-state-plain";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void EmptyAndLivePoliciesMatchOriginalSnapshotJson()
    {
        // These explicitly assembled grants, ledgers and turn fields audit the
        // projection host boundary; they do not claim accepted-command replay.
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:standard-5", UseInteractiveSetup = true
        }, registry);
        var players = Field<IReadOnlyList<CharacterState>>(game, "_players");
        foreach (var player in players)
            foreach (var grant in player.SkillGrants.Grants.ToArray()) player.SkillGrants.RemoveGrant(grant.GrantId);
        var owner = players[0];
        var usages = Field<SkillRuntimeStateStore>(game, "_skillRuntimeState");
        var directed = Field<List<DirectedTurnCardPolicy>>(game, "_directedTurnCardPolicies");
        var effects = Field<TurnCardUseEffectStore>(game, "_turnCardUseEffects");
        var prohibitions = (List<TurnCardActionProhibition>)effects.ActionProhibitions;
        var project = Method("CreateProgramAwareSkillStateSnapshot").CreateDelegate<Func<CharacterState, string, SkillRuntimeStateSnapshot>>(game);
        var acquired = Method("IsRuntimeAcquiredSkill").CreateDelegate<Func<CharacterState, string, bool>>(game);
        var hasInstance = Method("HasRuntimeSkillInstance").CreateDelegate<Func<CharacterState, string, string, bool>>(game);
        var shard = Method("GetSkillBindingShard").CreateDelegate<Func<CharacterState, SkillBindingShard>>(game);
        var publicRules = Method("GetHandComparisonPublicRuleStates").CreateDelegate<Func<CharacterState, string, IReadOnlyList<ProgramPublicRuleStateSnapshot>?>>(game);
        var boolean = typeof(GameEngine).GetMethod("GetProgramBooleanState", Flags, null,
            [typeof(int), typeof(string), typeof(string), typeof(string)], null)!
            .CreateDelegate<Func<int, string, string, string, bool>>(game);
        Set("_turnNumber", 1); Set("_currentSeat", 0);

        var empty = Check(owner, Skill);
        Require(empty.BooleanStates is { Count: 0 } && empty.DirectedPolicies is { Count: 0 } &&
                empty.ActionProhibitions is { Count: 0 } && empty.PublicRuleStates is null && !empty.IsAcquired,
            "An unowned unused program must preserve three non-null empty lists and absent public rule state.");
        RejectReadOnly(empty.BooleanStates!, new("intruder", "state", false, "intruder"));
        RejectReadOnly(empty.DirectedPolicies!, new(1, 1, 0, 1, 0, Source(0, Skill, "intruder"), 0, 1,
            Array.AsReadOnly(new[] { CardKind.Slash }), DirectedTurnCardPolicyEffect.IgnoreDistance));
        RejectReadOnly(empty.ActionProhibitions!, new(1, 1, 0, 1, 0, Source(0, Skill, "intruder"),
            Array.AsReadOnly(new[] { CardKind.Slash }), Array.AsReadOnly(new[] { CardActionType.Use })));
        Require(empty.BooleanStates is ReadOnlyCollection<ProgramBooleanStateSnapshot> &&
                empty.DirectedPolicies is ReadOnlyCollection<DirectedTurnCardPolicy> &&
                empty.ActionProhibitions is ReadOnlyCollection<TurnCardActionProhibition>,
            "Empty paths must retain the original read-only collection wrapper types.");

        var z = Grant(owner, Skill, "instance-z");
        var a = Grant(owner, Skill, "instance-a");
        Grant(owner, Plain, "plain");
        Grant(players[1], Skill, "instance-z");
        usages.TryConsumeUsage(0, Skill, "z", SkillUsageScope.Turn, 1);
        usages.TryConsumeUsage(0, Skill, "a", SkillUsageScope.Game, 2);
        usages.RegisterConversionSkill(0, Skill, SkillPolarity.Yin);
        var booleans = Check(owner, Skill);
        Require(booleans.IsAcquired && booleans.Polarity == SkillPolarity.Yin &&
                booleans.BooleanStates!.Select(state => (state.SkillInstanceId, state.StateId)).SequenceEqual(new[]
                { (a.SkillInstanceId, "z"), (a.SkillInstanceId, "a"), (z.SkillInstanceId, "z"), (z.SkillInstanceId, "a") }) &&
                booleans.BooleanStates.All(state => state.StateId != "private") &&
                booleans.BooleanStates[0].Text == "z：否" && booleans.BooleanStates[1].Text == "named true",
            "Public boolean values retain instance/definition order, fallback/named text and private-state exclusion.");
        Check(owner, Plain);

        var sourceZ = Source(0, Skill, z.SkillInstanceId);
        var sourceA = Source(0, Skill, a.SkillInstanceId);
        CardUseEffectSource[] sources =
        [sourceZ, Source(1, Skill, z.SkillInstanceId), Source(0, Plain, "plain"),
            Source(0, Skill, "missing-instance"), sourceA, sourceZ, sourceZ];
        for (var index = 0; index < sources.Length; index++)
        {
            var turn = index == 5 ? 2 : 1;
            var seat = index == 6 ? 2 : 0;
            directed.Add(new(90 - index, turn, seat, 100 + index, index, sources[index], 0, 1,
                Array.AsReadOnly(new[] { CardKind.Duel, CardKind.Slash }), DirectedTurnCardPolicyEffect.IgnoreDistance));
            prohibitions.Add(new(70 - index, turn, seat, 200 + index, index, sources[index],
                Array.AsReadOnly(new[] { CardKind.Slash }), Array.AsReadOnly(new[] { CardActionType.Use, CardActionType.Response }))
                { Suits = index % 2 == 0 ? Array.AsReadOnly(new[] { Suit.Heart, Suit.Spade }) : null });
        }
        var captured = Check(owner, Skill);
        var capturedJson = JsonSerializer.Serialize(captured);
        Require(captured.DirectedPolicies!.Select(policy => policy.GrantSequence).SequenceEqual(new long[] { 90, 86 }) &&
                captured.ActionProhibitions!.Select(policy => policy.GrantSequence).SequenceEqual(new long[] { 70, 66 }),
            "Live ledgers retain insertion order while excluding foreign owners, skills, instances, turns and turn seats.");
        Check(players[1], Skill); Check(owner, Plain);
        RejectReadOnly(captured.DirectedPolicies!, captured.DirectedPolicies![0]);
        RejectReadOnly(captured.ActionProhibitions!, captured.ActionProhibitions![0]);
        VerifyPlayerFreeze(captured);

        owner.SkillGrants.SetEnabled(z.GrantId, false);
        Require(Check(owner, Skill).DirectedPolicies!.Count == 1, "Disabling a source immediately hides only its policies and states.");
        owner.SkillGrants.SetEnabled(z.GrantId, true); Check(owner, Skill);
        owner.SkillGrants.RemoveGrant(z.GrantId);
        Require(Check(owner, Skill).ActionProhibitions!.Count == 1, "Removing an instance must not retain its issued policies as currently qualified.");
        owner.SkillGrants.Grant(z); Check(owner, Skill);
        owner.SkillGrants.RemoveGrant(z.GrantId);
        var later = Grant(owner, Skill, "instance-later");
        Require(Check(owner, Skill).DirectedPolicies!.Count == 1,
            "A regranted same-skill instance must not qualify the previous instance's ledgers.");
        owner.SkillGrants.RemoveGrant(later.GrantId); owner.SkillGrants.Grant(z);
        var keyType = typeof(GameEngine).GetNestedType("ProgramBooleanStateKey", BindingFlags.NonPublic)!;
        Field<IDictionary>(game, "_programBooleanStates")[Activator.CreateInstance(keyType, [0, Skill, z.SkillInstanceId, "z"])!] = true;
        usages.ToggleConversionState(0, Skill);
        Require(Check(owner, Skill).BooleanStates!.Single(state => state.SkillInstanceId == z.SkillInstanceId && state.StateId == "z").Value,
            "The next projection reads the current instance's boolean value.");
        Set("_turnNumber", 2); Check(owner, Skill);
        Set("_turnNumber", 1); Set("_currentSeat", 2); Check(owner, Skill);
        Set("_currentSeat", 0); Check(owner, Skill);
        usages.ResetTurn(); Check(owner, Skill);
        Require(JsonSerializer.Serialize(captured) == capturedJson,
            "Later qualification, boolean, polarity and usage changes must not rewrite an already projected snapshot.");

        directed.Clear(); Check(owner, Skill);
        prohibitions.Clear(); var cleared = Check(owner, Skill);
        Require(cleared.DirectedPolicies is ReadOnlyCollection<DirectedTurnCardPolicy> { Count: 0 } &&
                cleared.ActionProhibitions is ReadOnlyCollection<TurnCardActionProhibition> { Count: 0 },
            "Clearing each actual ledger independently returns the same non-null read-only empty output.");
        owner.SkillGrants.RemoveGrant(z.GrantId); owner.SkillGrants.RemoveGrant(a.GrantId);
        Check(owner, Skill); Check(owner, Plain);

        SkillRuntimeStateSnapshot Check(CharacterState player, string skill)
        {
            var original = Original(player, skill);
            var actual = project(player, skill);
            Require(JsonSerializer.Serialize(original) == JsonSerializer.Serialize(actual),
                "The entire skill-state JSON must equal the prior expression across every live-state transition.");
            return actual;
        }

        SkillRuntimeStateSnapshot Original(CharacterState player, string skill)
        {
            var snapshot = usages.CreateSnapshot(player.Seat, skill, acquired(player, skill));
            var states = new List<ProgramBooleanStateSnapshot>();
            foreach (var instance in shard(player).ProgramInstances)
            {
                if (instance.SkillId != skill) continue;
                foreach (var definition in instance.Program.BooleanStates)
                {
                    if (definition.Visibility != SkillProgramStateVisibility.Public) continue;
                    var value = boolean(player.Seat, skill, instance.SkillInstanceId, definition.Id);
                    var presentation = instance.Definition.ProgramPresentation?.BooleanStates.GetValueOrDefault(definition.Id);
                    states.Add(new(instance.SkillInstanceId, definition.Id, value,
                        presentation is null ? $"{definition.Id}：{(value ? "是" : "否")}" :
                        value ? presentation.TrueText : presentation.FalseText));
                }
            }
            bool Current(CardUseEffectSource source) => source.OwnerSeat == player.Seat && source.SkillId == skill &&
                hasInstance(player, skill, source.SkillInstanceId);
            return snapshot with
            {
                PublicRuleStates = publicRules(player, skill), BooleanStates = Array.AsReadOnly(states.ToArray()),
                DirectedPolicies = Array.AsReadOnly(directed.Where(item => item.TurnNumber == Field<int>(game, "_turnNumber") &&
                    item.TurnSeat == Field<int>(game, "_currentSeat") && Current(item.Source)).ToArray()),
                ActionProhibitions = Array.AsReadOnly(prohibitions.Where(item => item.TurnNumber == Field<int>(game, "_turnNumber") &&
                    item.TurnSeat == Field<int>(game, "_currentSeat") && Current(item.Source)).ToArray())
            };
        }

        void VerifyPlayerFreeze(SkillRuntimeStateSnapshot snapshot)
        {
            var player = game.CreateSnapshot(0).Players[0] with { SkillRuntimeStates = [snapshot] };
            var frozen = (PlayerSnapshot)Method("FreezePlayer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [player])!;
            var value = frozen.SkillRuntimeStates!.Single();
            var json = JsonSerializer.Serialize(frozen);
            RejectReadOnly(frozen.SkillRuntimeStates, snapshot);
            RejectReadOnly(value.BooleanStates!, value.BooleanStates![0]);
            RejectReadOnly(value.DirectedPolicies!, value.DirectedPolicies![0]);
            RejectReadOnly(value.DirectedPolicies![0].CardKinds, CardKind.Dodge);
            RejectReadOnly(value.ActionProhibitions!, value.ActionProhibitions![0]);
            RejectReadOnly(value.ActionProhibitions![0].CardKinds, CardKind.Dodge);
            RejectReadOnly(value.ActionProhibitions![0].ActionTypes, CardActionType.Use);
            RejectReadOnly(value.ActionProhibitions![0].Suits!, Suit.Club);
            Require(JsonSerializer.Serialize(frozen) == json, "Frozen observer lists and nested policy collections must reject replacement.");
        }

        void Set(string name, int value) => typeof(GameEngine).GetField(name, Flags)!.SetValue(game, value);
    }

    private static CardUseEffectSource Source(int owner, string skill, string instance) => new(skill, "fixture", owner, instance);
    private static SkillGrant Grant(CharacterState owner, string skill, string instance)
    {
        var grant = new SkillGrant(instance, skill, instance, "acquired:projection-audit");
        owner.SkillGrants.Grant(grant); return grant;
    }
    private static MethodInfo Method(string name, BindingFlags flags = Flags) => typeof(GameEngine).GetMethod(name, flags)!;
    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static void RejectReadOnly<T>(IReadOnlyList<T> values, T replacement)
    {
        Require(values is IList<T> { IsReadOnly: true }, "The original immutable collection boundary must remain present.");
        try
        {
            if (values.Count == 0) ((IList<T>)values).Add(replacement);
            else ((IList<T>)values)[0] = replacement;
        }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A projected collection accepted mutation.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("program-state-projection", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { new
                {
                    id = Skill, revision = 1,
                    states = new[]
                    {
                        new { id = "z", initialValue = false, visibility = "public", resetScope = "game", reacquirePolicy = "preserveUntilGameEnd" },
                        new { id = "private", initialValue = true, visibility = "private", resetScope = "game", reacquirePolicy = "preserveUntilGameEnd" },
                        new { id = "a", initialValue = true, visibility = "public", resetScope = "game", reacquirePolicy = "preserveUntilGameEnd" }
                    },
                    activations = new[] { new
                    {
                        id = "set", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                        targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "setBooleanState", target = "owner", stateId = "z", value = true } }
                    } }
                } }
            });
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Skill] = new
                {
                    name = "Projection state", description = "Shared live snapshot fixture",
                    booleanStates = new Dictionary<string, object> { ["a"] = new { trueText = "named true", falseText = "named false" } }
                } }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new(Skill, "Projection state", "Shared live snapshot fixture")
                { Program = catalog.Programs[Skill], ProgramPresentation = catalog.Presentations[Skill] });
            builder.AddSkill(new(Plain, "Plain projection", "No runtime program"));
        }
    }
}
