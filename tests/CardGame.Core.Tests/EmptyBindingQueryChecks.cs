using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class EmptyBindingQueryChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Noise = "fixture:empty-query-noise", Capability = "fixture:empty-query-capability";
    private const string LordSkill = "fixture:empty-query-lord", Policy = "fixture:empty-query-policy";

    public static void EmptySlotsPreserveExactQueriesAndIssuedObligations()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = Create();
        var owner = Players(game)[0];
        Grant(owner, Noise, "noise-z"); Grant(owner, Noise, "noise-a");
        var collect = Method("CollectProgramTriggerCandidates").CreateDelegate<Func<CharacterState, SkillProgramTriggerWindow, int, IReadOnlyList<ProgramTriggerCandidate>>>(game);
        var publicStates = Method("GetHandComparisonPublicRuleStates").CreateDelegate<Func<CharacterState, string, IReadOnlyList<ProgramPublicRuleStateSnapshot>?>>(game);
        var hasPolicy = Method("HasCardPolicy").CreateDelegate<Func<CharacterState, SkillProgramCardPolicyKind, CardKind?, bool>>(game);
        var effectiveSuit = Method("GetProgramEffectiveSuit").CreateDelegate<Func<CharacterState, Card, Suit>>(game);
        var policyCard = new Card(91234, CardKind.Slash, Suit.Spade, 7);
        var library = Method("EffectiveGeneralLibrary").CreateDelegate<Func<CharacterState, object?>>(game);
        var sync = Method("SynchronizeLordSkillProjections").CreateDelegate<Action>(game);

        var before = Observe(game);
        NoAllocation(() => collect(owner, SkillProgramTriggerWindow.BeforeDamageApplied, 7), "empty trigger window");
        NoAllocation(() => collect(owner, SkillProgramTriggerWindow.PlayEnding, 7), "empty Play-ending window without issued debt");
        NoAllocation(() => publicStates(owner, Noise), "owned program without hand-comparison operations");
        NoAllocation(() => hasPolicy(owner, SkillProgramCardPolicyKind.RewriteSuit, null), "empty card-policy slot");
        NoAllocation(() => effectiveSuit(owner, policyCard), "empty suit-rewrite slot");
        NoAllocation(() => library(owner), "empty private library");
        NoAllocation(sync, "catalog capability without any owner capability or retained projection");
        Require(before == Observe(game), "Empty-slot reads/synchronization must preserve all player JSON, frames, events, commands, grants and movements.");
        VerifyCandidates(SkillProgramTriggerWindow.TurnEnding);
        owner.SkillGrants.RemoveGrant("noise-z"); VerifyCandidates(SkillProgramTriggerWindow.TurnEnding);
        owner.SkillGrants.SetEnabled("noise-a", false); VerifyCandidates(SkillProgramTriggerWindow.TurnEnding);
        owner.SkillGrants.SetEnabled("noise-a", true); VerifyCandidates(SkillProgramTriggerWindow.TurnEnding);

        var policyGrant = Grant(owner, Policy, "conditional-policy");
        owner.Hp = owner.MaxHp; VerifyPolicy(false);
        owner.Hp--; VerifyPolicy(true);
        owner.SkillGrants.SetEnabled(policyGrant.GrantId, false); VerifyPolicy(false);
        owner.SkillGrants.SetEnabled(policyGrant.GrantId, true); VerifyPolicy(true);
        owner.SkillGrants.RemoveGrant(policyGrant.GrantId); VerifyPolicy(false);

        var compare = Grant(owner, "classic:zhongjian", "compare-original");
        var state = Grant(owner, "classic:caishi", "state-original");
        Pending(game).AddRange([
            new ProgramPersistentHandLimitChangedEvent(0, compare.SkillId, compare.SkillInstanceId, "comparison", -2),
            new ProgramHandComparisonResolvedEvent(0, compare.SkillId, compare.SkillInstanceId, "comparison", 1, 1, 1, [], false, true),
            new ProgramPersistentHandLimitChangedEvent(0, state.SkillId, state.SkillInstanceId, "hand-limit", 3),
            new ProgramSelfCardTargetsProhibitedEvent(0, state.SkillId, state.SkillInstanceId, 1, 0)
        ]);
        before = Observe(game);
        Require(JsonSerializer.Serialize(publicStates(owner, compare.SkillId)) == JsonSerializer.Serialize(new ProgramPublicRuleStateSnapshot[]
        {
            new(compare.SkillInstanceId, "compare", ProgramPublicRuleStateKind.ActivationLimit, 2, 0, SkillUsageScope.Phase),
            new(compare.SkillInstanceId, "comparison", ProgramPublicRuleStateKind.PersistentHandLimit, -2)
        }), "Activation limits and persistent values retain their exact ordered JSON and original instance history.");
        Require(JsonSerializer.Serialize(publicStates(owner, state.SkillId)) == JsonSerializer.Serialize(new ProgramPublicRuleStateSnapshot[]
        {
            new(state.SkillInstanceId, "hand-limit", ProgramPublicRuleStateKind.PersistentHandLimit, 3),
            new(state.SkillInstanceId, "self-target", ProgramPublicRuleStateKind.SelfTargetProhibition, 1, null, SkillUsageScope.Turn)
        }) && before == Observe(game), "Trigger-only hand-limit/self-target effects remain public with exact values and pure reads.");
        owner.SkillGrants.RemoveGrant(compare.GrantId);
        Require(publicStates(owner, compare.SkillId) is null, "A lost comparison grant does not expose its old instance as currently owned.");
        var later = Grant(owner, compare.SkillId, "compare-later");
        Require(JsonSerializer.Serialize(publicStates(owner, later.SkillId)) == JsonSerializer.Serialize(new ProgramPublicRuleStateSnapshot[]
        {
            new(later.SkillInstanceId, "compare", ProgramPublicRuleStateKind.ActivationLimit, 1, 0, SkillUsageScope.Phase),
            new(later.SkillInstanceId, "comparison", ProgramPublicRuleStateKind.PersistentHandLimit, 0)
        }), "A new same-skill instance must not inherit another instance's public comparison facts.");

        // Explicit host-issued history audits the read boundary, not accepted-command replay.
        // Real paid Lihun source-loss/recovery is also covered by PhaseHandSeizureChecks.
        var debtSource = Grant(owner, "ol:lihun", "original-debt-source");
        var debtProgram = registry.GetSkill(debtSource.SkillId).Program!;
        var activation = debtProgram.Activations.Single();
        var continuation = debtProgram.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }]);
        var issued = new PhaseHandSeizureIssuedEvent(500, new(debtSource.SkillId, activation.Id, 0, debtSource.SkillInstanceId),
            debtProgram.GameplayHash, continuation.Id, 1, 1, 1, 2, false, 0, 2);
        Pending(game).Add(issued); VerifyCandidates(SkillProgramTriggerWindow.PlayEnding);
        owner.SkillGrants.RemoveGrant(debtSource.GrantId);
        VerifyCandidates(SkillProgramTriggerWindow.PlayEnding);
        var expectedDebt = new ProgramTriggerCandidate(0, debtSource.SkillId, continuation.Id, debtSource.SkillInstanceId, debtProgram.GameplayHash, continuation.Priority, 7);
        Require(JsonSerializer.Serialize(collect(owner, SkillProgramTriggerWindow.PlayEnding, 7)) == JsonSerializer.Serialize(new[] { expectedDebt }),
            "An empty current binding slot retains the exact already-issued original source debt once.");
        CommitPending(game); VerifyCandidates(SkillProgramTriggerWindow.PlayEnding);
        Require(collect(owner, SkillProgramTriggerWindow.PlayEnding, 7).SequenceEqual([expectedDebt]), "Committed issuance retains the original debt after source loss.");
        Pending(game).Add(new PhaseHandDebtSettledEvent(500, 501, 0, 1, "returned", 2, 2, 2, 4));
        VerifyCandidates(SkillProgramTriggerWindow.PlayEnding);
        Require(collect(owner, SkillProgramTriggerWindow.PlayEnding, 7).Count == 0, "A pending settlement closes its original source-less obligation exactly once.");
        LordProjectionBoundaries();

        GameEngine Create()
        {
            var value = GameEngine.CreateStandard(new GameOptions { Seed = 1, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:standard-5", UseInteractiveSetup = true }, registry);
            foreach (var player in Players(value))
            {
                foreach (var grant in player.SkillGrants.Grants.ToArray()) player.SkillGrants.RemoveGrant(grant.GrantId);
            }
            Set(value, "_started", true);
            Set(value, "_turnNumber", 1); Set(value, "_currentSeat", 0); Set(value, "_phase", TurnPhase.Play); Set(value, "_cardUseDebitPhaseInstanceId", 1);
            return value;
        }
        void VerifyCandidates(SkillProgramTriggerWindow window)
        {
            var original = Shard(game, owner).GetInstanceTriggers(window)
                .OrderByDescending(binding => binding.Trigger.Priority).ThenBy(binding => binding.SkillId, StringComparer.Ordinal)
                .ThenBy(binding => binding.SkillInstanceId, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.ChoiceGroup ?? binding.Trigger.Id, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.Id, StringComparer.Ordinal)
                .Select(binding => new ProgramTriggerCandidate(0, binding.SkillId, binding.Trigger.Id, binding.SkillInstanceId, binding.Program.GameplayHash, binding.Trigger.Priority, 7))
                .Concat((IEnumerable<ProgramTriggerCandidate>)Method("IssuedPhaseHandDebtCandidates").Invoke(game, [owner, window, 7])!)
                .DistinctBy(candidate => (candidate.SkillId, candidate.BindingId,
                    Trigger(candidate).NamedUsageGroup is not null || Trigger(candidate).DynamicUsageLimit is not null ||
                    Trigger(candidate).Effects.Any(effect => effect.Op == SkillProgramEffectOp.DiscardHandToNamedTurnCount) ? "" : candidate.SkillInstanceId)).ToArray();
            var stateBefore = Observe(game);
            Require(JsonSerializer.Serialize(original) == JsonSerializer.Serialize(collect(owner, window, 7)) && stateBefore == Observe(game),
                "The empty-slot guard must preserve the original ordered/deduplicated candidate JSON and pure query contract.");
        }
        SkillProgramTrigger Trigger(ProgramTriggerCandidate candidate) => ProgramInstructionResolver.Default.FindTrigger(registry.GetSkill(candidate.SkillId).Program!, candidate.BindingId)!;
        void VerifyPolicy(bool expected)
        {
            var original = (IEnumerable<(IndexedSkillProgramInstance Source, SkillProgramCardPolicy Policy)>)Method("CardPolicies").Invoke(game,
                [owner, SkillProgramCardPolicyKind.RewriteSuit, null, null, null, null])!;
            var stateBefore = Observe(game);
            Require(hasPolicy(owner, SkillProgramCardPolicyKind.RewriteSuit, null) == original.Any() && original.Any() == expected && stateBefore == Observe(game),
                "Policy presence equals the original deferred Any query across live HP conditions, disable, removal and regrant.");
            var originalSuit = policyCard.Suit;
            foreach (var item in original)
                if (item.Policy.InputSuit == originalSuit) originalSuit = item.Policy.OutputSuit!.Value;
            Require(effectiveSuit(owner, policyCard) == originalSuit &&
                    originalSuit == (expected ? Suit.Heart : Suit.Spade) && stateBefore == Observe(game),
                "Suit rewriting preserves the original policy fold across live conditions and binding revisions.");
        }
        void LordProjectionBoundaries()
        {
            var value = Create(); var lord = Players(value)[0]; var recipient = Players(value)[1];
            var source = Grant(lord, LordSkill, "lord-source"); var capability = Grant(recipient, Capability, "capability-source");
            var synchronize = Method("SynchronizeLordSkillProjections").CreateDelegate<Action>(value);
            synchronize();
            var projected = recipient.SkillGrants.Grants.Single(grant => grant.LordProjection is not null);
            var id = $"lord-projection:1:{capability.GrantId.Length}:{capability.GrantId}:{capability.SkillInstanceId.Length}:{capability.SkillInstanceId}:0:{source.GrantId.Length}:{source.GrantId}:{source.SkillInstanceId.Length}:{source.SkillInstanceId}:{source.SkillId}";
            var expected = new SkillGrant(id, LordSkill, id, "lord-projection:" + capability.SourceId,
                LordProjection: new(0, source.GrantId, source.SkillInstanceId, capability.GrantId, capability.SkillInstanceId));
            Require(JsonSerializer.Serialize(projected) == JsonSerializer.Serialize(expected), "Projection retains the exact original source/capability identity JSON.");
            recipient.SkillGrants.SetEnabled(capability.GrantId, false); synchronize();
            Require(recipient.SkillGrants.Grants.Any(grant => grant.GrantId == id) && !Shard(value, recipient).HasSkill(LordSkill), "Disabled capability retains its relation while losing current qualification.");
            recipient.SkillGrants.SetEnabled(capability.GrantId, true); recipient.SkillGrants.SetEnabled(id, false); synchronize();
            Require(!recipient.SkillGrants.Grants.Single(grant => grant.GrantId == id).IsEnabled, "Synchronization preserves a projection's independent local disable.");
            recipient.SkillGrants.SetEnabled(id, true); lord.SkillGrants.SetEnabled(source.GrantId, false); synchronize();
            Require(recipient.SkillGrants.Grants.Any(grant => grant.GrantId == id) && !Shard(value, recipient).HasSkill(LordSkill), "A disabled Lord source keeps its relation but cannot qualify the derived skill.");
            lord.SkillGrants.SetEnabled(source.GrantId, true); synchronize();
            Require(Shard(value, recipient).HasSkill(LordSkill), "The same retained source relationship qualifies again after explicit enable.");
            recipient.SkillGrants.RemoveGrant(capability.GrantId); synchronize();
            Require(recipient.SkillGrants.Grants.All(grant => grant.LordProjection is null) && Pending(value).OfType<LordSkillProjectionChangedEvent>().Count(fact => !fact.Added) == 1,
                "Loss of the last capability still removes its stale retained projection with one rule fact.");
            var originalRole = recipient.Role;
            var roleProperty = typeof(CharacterState).GetProperty(nameof(CharacterState.Role))!;
            roleProperty.SetValue(recipient, Role.Lord);
            var rejectedDuplicateLord = false;
            try { synchronize(); }
            catch (InvalidOperationException) { rejectedDuplicateLord = true; }
            finally { roleProperty.SetValue(recipient, originalRole); }
            Require(rejectedDuplicateLord, "Projection synchronization must retain the unique living Lord invariant even with no recipient capability.");
        }
    }

    private static void NoAllocation(Action query, string label)
    {
        for (var i = 0; i < 32; i++) query();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++) query();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(bytes == 0, $"Warm {label} must return without per-query allocations; allocated {bytes} bytes.");
    }
    private static SkillGrant Grant(CharacterState owner, string skill, string instance)
    {
        var grant = new SkillGrant(instance, skill, instance, "acquired:empty-slot-audit"); owner.SkillGrants.Grant(grant); return grant;
    }
    private static MethodInfo Method(string name) => typeof(GameEngine).GetMethod(name, Flags)!;
    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static void Set(GameEngine game, string name, object value) => typeof(GameEngine).GetField(name, Flags)!.SetValue(game, value);
    private static IReadOnlyList<CharacterState> Players(GameEngine game) => Field<IReadOnlyList<CharacterState>>(game, "_players");
    private static SkillBindingShard Shard(GameEngine game, CharacterState owner) => (SkillBindingShard)Method("GetSkillBindingShard").Invoke(game, [owner])!;
    private static List<IGameEvent> Pending(GameEngine game) => Field<List<IGameEvent>>(game, "_pendingEvents");
    private static void CommitPending(GameEngine game)
    {
        var events = Field<List<EventEnvelope>>(game, "_events");
        foreach (var fact in Pending(game)) events.Add(new(new EventId(events.Count + 1), null, events.Count + 1, game.Revision, "empty-query-audit", fact));
        Pending(game).Clear();
    }
    private static string Observe(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Views = Enumerable.Range(0, Players(game).Count).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = ProgramCompositionEntryChecks.Events(game),
        Pending = Pending(game).Select(fact => JsonSerializer.Serialize(fact, fact.GetType())).ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), game.CardMovements,
        Grants = Players(game).Select(player => new { player.SkillGrants.Revision, player.SkillGrants.Grants }).ToArray()
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("empty-binding-query", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var type in new[] { "Fame2017XinXianyingContent", "OrdinarySpDiaoChanContent" })
                typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard." + type)!
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Noise}}}","revision":1,"activations":[{"id":"noise","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]}],"triggers":[
                {"id":"low","window":"turnEnding","subject":"owner","priority":1,"optional":true,"effects":[{"op":"recover","target":"owner","amount":1}]},
                {"id":"high","window":"turnEnding","subject":"owner","priority":9,"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{{Capability}}}","revision":1,"lordSkillProjection":true},
              {"id":"{{{Policy}}}","revision":1,"cardPolicies":[{"id":"wounded","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart","condition":{"kind":"wounded"}}]}]}
            """;
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { Noise, Capability, Policy }.ToDictionary(id => id, _ => new { name = "Empty slot", description = "Exact read boundaries" }) });
            foreach (var pair in SkillProgramCatalog.Load(rules, presentation).Programs) builder.AddSkill(new(pair.Key, pair.Key, "Exact empty-slot checks") { Program = pair.Value });
            builder.AddSkill(new(LordSkill, "Host Lord source", "Projection identity") { Tags = SkillTag.Lord });
        }
    }
}
