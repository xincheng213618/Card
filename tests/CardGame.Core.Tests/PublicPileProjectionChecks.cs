using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PublicPileProjectionChecks
{
    private const string SourceSkill = "classic:bizhuan", Consumer = "classic:tongbo";
    private const string Extra = "fixture:pile-projection-extra", Cash = "fixture:pile-projection-cash";
    private const string Plain = "fixture:pile-projection-plain";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void EmptyAndLiveSourcesPreserveOriginalQueriesAndFreeze()
    {
        // Sources, grants and physical zones are explicitly assembled to audit
        // the query host boundary. Accepted-command replay remains covered by
        // the existing public-pile behavior checks, not claimed by this fixture.
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:standard-5", UseInteractiveSetup = true
        }, registry);
        var players = Field<IReadOnlyList<CharacterState>>(game, "_players");
        var ledger = Field<Dictionary<(int Owner, string Skill, string Instance), PublicPersistentPileSource>>(game, "_publicPersistentPiles");
        var zones = Field<CardZoneStore>(game, "_cardZones");
        var single = Method("SinglePublicPileSource").CreateDelegate<Func<int, PublicPersistentPileSource?>>(game);
        var piles = Method("CreatePublicPersistentPileSnapshots").CreateDelegate<Func<int, IReadOnlyList<PublicPersistentPileSnapshot>?>>(game);
        var count = Method("PublicPileProgramCount").CreateDelegate<Func<int, string, string, int>>(game);
        var scalar = typeof(GameEngine).GetMethod("PublicPileCards", Flags, null, [typeof(int)], null)!
            .CreateDelegate<Func<int, IReadOnlyList<Card>>>(game);
        var project = Method("ToSnapshot").CreateDelegate<Func<Card, CardSnapshot>>(game);
        var referenced = Method("ReferencedPublicPileSources")
            .CreateDelegate<Func<int, string, string?, IReadOnlyList<PublicPersistentPileSource>>>(game);
        var nextId = 1_100_000;
        Check();
        Require(piles(0) is null && single(0) is null && scalar(0).Count == 0,
            "An empty actual ledger preserves absent scalar source and multi-pile views.");

        var foreign = Add(1, SourceSkill, "foreign", null, 2);
        Check();
        Require(count(0, Consumer, "missing") == 0 && piles(0) is null,
            "A foreign owner's source does not defeat the owner-empty count or view path.");
        var z = Add(0, SourceSkill, "instance-z", null, 2);
        Grant(SourceSkill, z.SkillInstanceId, "acquired:beta");
        Check();
        Require(single(0) == z && piles(0) is null && scalar(0).Count == 2,
            "Exactly one own source retains the legacy scalar view, without a multi-source list.");
        var a = Add(0, SourceSkill, "instance-a", "named-a", 1);
        var sourceGrant = Grant(SourceSkill, a.SkillInstanceId, "acquired:alpha");
        var consumerGrant = Grant(Consumer, "consumer", "acquired:alpha");
        var extra = Add(0, Extra, "extra", "empty-extra", 0);
        var cash = Add(0, Cash, "cash", "cash", 4);
        var other = Add(1, Extra, "other", "other", 1);
        Check();
        var captured = piles(0)!;
        var capturedJson = JsonSerializer.Serialize(captured);
        Require(single(0) is null && scalar(0).Count == 0 &&
                captured.Select(p => p.SourceSkillInstanceId).SequenceEqual(new[] { "instance-z", "instance-a", "extra", "cash" }) &&
                captured.Select(p => p.Count).SequenceEqual(new[] { 2, 1, 0, 4 }) && captured[2].Name is null,
            "Multiple same-skill and unrelated sources preserve insertion order, empty sources, names and scalar ambiguity.");
        Require(count(0, Consumer, "consumer") == 1 && count(0, Consumer, "instance-z") == 2 &&
                count(0, Consumer, "missing") == 2 && count(0, Cash, "cash") == 4 && count(0, Cash, "missing") == 0,
            "Consumer source association, direct instance precedence, maximum fallback and exact cash-out source remain distinct.");
        RejectReadOnly(captured, captured[0]);
        RejectReadOnly(captured[0].Cards, captured[0].Cards[0]);
        RejectReadOnly(captured[2].Cards, captured[0].Cards[0]);
        VerifyPlayerFreeze(captured);

        players[0].SkillGrants.SetEnabled(sourceGrant.GrantId, false); Check();
        Require(count(0, Consumer, "consumer") == 1,
            "The original source-association query remains source-aware without adding an enabled-only restriction.");
        players[0].SkillGrants.RemoveGrant(consumerGrant.GrantId); Check();
        Require(count(0, Consumer, "consumer") == 2, "Removing the association falls back to the original maximum.");
        players[0].SkillGrants.Grant(consumerGrant); Check();
        players[0].SkillGrants.SetEnabled(sourceGrant.GrantId, true); Check();
        ledger.Remove(Key(z)); Check();
        ledger.Add(Key(z), z); Check();
        Require(piles(0)!.Select(p => p.SourceSkillInstanceId)
                    .SequenceEqual(OriginalPiles(0)!.Select(p => p.SourceSkillInstanceId)),
            "Remove/reinsert preserves the dictionary's actual source enumeration order.");
        Fill(extra, 3); Check();
        var moved = zones.CardsAt(a.Location).Single();
        zones.Move(moved.Id, a.Location, CardLocation.Hand(0)); Check();
        Require(count(0, Consumer, "consumer") == 0 && piles(0)!.Single(p => p.SourceSkillInstanceId == "extra").Count == 3 &&
                JsonSerializer.Serialize(captured) == capturedJson,
            "New material and emptied sources update the next query while an older projected view stays detached.");
        ledger.Clear(); Check();
        Require(piles(0) is null && single(0) is null && count(0, Cash, "cash") == 0,
            "Clearing the actual source ledger restores the empty behavior even when old physical zones still contain cards.");

        void Check()
        {
            var revision = game.Revision;
            var records = (game.Events.Count, game.AcceptedCommands.Count, game.CardMovements.Count);
            for (var seat = 0; seat < players.Count; seat++)
            {
                Equal(OriginalSingle(seat), single(seat));
                Equal(OriginalPiles(seat), piles(seat));
                var originalCards = OriginalSingle(seat) is { } source ? zones.CardsAt(source.Location) : Array.Empty<Card>();
                Equal(originalCards, scalar(seat));
                foreach (var skill in new[] { SourceSkill, Consumer, Extra, Cash, Plain, "fixture:missing-pile-skill" })
                    foreach (var instance in new[] { "instance-z", "instance-a", "consumer", "extra", "cash", "missing" })
                        Equal(Outcome(() => OriginalCount(seat, skill, instance)), Outcome(() => count(seat, skill, instance)));
            }
            Require(revision == game.Revision && records == (game.Events.Count, game.AcceptedCommands.Count, game.CardMovements.Count),
                "Source projection and count queries must not publish rules, events, commands or card movements.");
        }
        PublicPersistentPileSource? OriginalSingle(int seat) =>
            ledger.Values.Where(s => s.OwnerSeat == seat).Take(2).ToArray() is [var source] ? source : null;
        IReadOnlyList<PublicPersistentPileSnapshot>? OriginalPiles(int seat)
        {
            var sources = ledger.Values.Where(s => s.OwnerSeat == seat).ToArray();
            if (sources.Length < 2) return null;
            return Array.AsReadOnly(sources.Select(s => new PublicPersistentPileSnapshot(seat, s.SkillId, s.SkillInstanceId,
                registry.GetSkill(s.SkillId).ProgramPresentation?.AuthorityName,
                Array.AsReadOnly(zones.CardsAt(s.Location).Select(project).ToArray()), zones.CardsAt(s.Location).Count, s.Location)).ToArray());
        }
        int OriginalCount(int seat, string skill, string instance)
        {
            var program = registry.GetSkill(skill).Program!;
            if (program.Triggers.SelectMany(t => t.Effects).Any(e => PublicPileCashOutContract.IsOperation(e.Op)))
                return ledger.GetValueOrDefault((seat, skill, instance)) is { } exact ? zones.CardsAt(exact.Location).Count : 0;
            var skills = program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects))
                .Where(e => e.Op is SkillProgramEffectOp.ExchangePublicPile or SkillProgramEffectOp.DistributePublicPileIfAllSuits or
                    SkillProgramEffectOp.RemovePublicPileAfterAttackDamage or SkillProgramEffectOp.ResolvePreparationPublicPile or
                    SkillProgramEffectOp.ExchangePublicPileHand or SkillProgramEffectOp.ObtainPublicPileCard or SkillProgramEffectOp.PublicPileColorDamage or
                    SkillProgramEffectOp.RewardDiscardedActionColor or SkillProgramEffectOp.ResolveFirstGameDomainCrossing)
                .SelectMany(e => e.SkillIds).Distinct(StringComparer.Ordinal).ToArray();
            if (skills.Length == 0) skills = [skill];
            return skills.SelectMany(id => referenced(seat, id, instance)).Select(s => zones.CardsAt(s.Location).Count).DefaultIfEmpty().Max();
        }
        PublicPersistentPileSource Add(int seat, string skill, string instance, string? pileId, int cards)
        {
            var source = new PublicPersistentPileSource(seat, skill, instance, 4, pileId);
            zones.EnsurePublicPersistentPile(source.Location); ledger.Add(Key(source), source); Fill(source, cards); return source;
        }
        void Fill(PublicPersistentPileSource source, int amount)
        {
            for (var i = 0; i < amount; i++)
            {
                var card = new Card(nextId++, i % 2 == 0 ? CardKind.Duel : CardKind.Slash, Suit.Heart, i + 1);
                zones.AddGeneratedCard(card); zones.Move(card.Id, CardLocation.OutsideGame, source.Location);
            }
        }
        SkillGrant Grant(string skill, string instance, string source)
        {
            var grant = new SkillGrant("projection-" + instance, skill, instance, source);
            players[0].SkillGrants.Grant(grant); return grant;
        }
        void VerifyPlayerFreeze(IReadOnlyList<PublicPersistentPileSnapshot> values)
        {
            var player = game.CreateSnapshot(0).Players[0] with { PublicPersistentPiles = values };
            var frozen = (PlayerSnapshot)typeof(GameEngine).GetMethod("FreezePlayer", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [player])!;
            var json = JsonSerializer.Serialize(frozen);
            RejectReadOnly(frozen.PublicPersistentPiles!, values[0]);
            RejectReadOnly(frozen.PublicPersistentPiles![0].Cards, values[0].Cards[0]);
            Require(JsonSerializer.Serialize(frozen) == json, "Observer lists reject mutation of both the pile collection and its cards.");
        }
    }

    private static (int, string, string) Key(PublicPersistentPileSource source) => (source.OwnerSeat, source.SkillId, source.SkillInstanceId);
    private static object Outcome(Func<int> query)
    {
        try { return new { Value = query(), Exception = (string?)null }; }
        catch (Exception error) { return new { Value = 0, Exception = error.GetType().FullName }; }
    }
    private static void Equal<T>(T expected, T actual) => Require(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(actual),
        "The full query JSON or exception type must match the original expression.");
    private static MethodInfo Method(string name) => typeof(GameEngine).GetMethod(name, Flags)!;
    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static void RejectReadOnly<T>(IReadOnlyList<T> values, T replacement)
    {
        Require(values is ReadOnlyCollection<T> && values is IList<T> { IsReadOnly: true }, "The original read-only wrapper remains present.");
        try
        {
            if (values.Count == 0) ((IList<T>)values).Add(replacement); else ((IList<T>)values)[0] = replacement;
        }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A public-pile projected list accepted mutation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("public-pile-projection", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            // Reuse the retained Cai Yong program rather than reconstructing its
            // exchange contract, plus one small real cash-out program.
            var catalog = SkillProgramCatalog.Load(Read("classic-cai-yong.rules.json"), Read("classic-cai-yong.presentation.json"));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "Shared pile projection") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Cash}}}","revision":1,"triggers":[{"id":"cash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                    "condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"publicPersistentPile"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0}},
                    "effects":[{"op":"cashOutPublicPile","target":"owner","drawMultiplier":2}]}]},
                  {"id":"{{{Extra}}}","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                    "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """;
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Cash, Extra }.ToDictionary(id => id, id => new { name = id, description = "Shared pile query fixture" })
            });
            var additions = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new(Cash, Cash, "Exact cash-out source") { Program = additions.Programs[Cash], ProgramPresentation = additions.Presentations[Cash] });
            builder.AddSkill(new(Extra, Extra, "No presentation name") { Program = additions.Programs[Extra] });
            builder.AddSkill(new(Plain, Plain, "No program"));
        }
        private static string Read(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
    }
}
