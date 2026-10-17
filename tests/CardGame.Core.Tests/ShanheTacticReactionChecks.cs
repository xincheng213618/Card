using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheTacticReactionChecks
{
    private const string Force = "fixture:shanhe-reaction-force";
    private const string Quiet = "fixture:shanhe-reaction-quiet";
    private const string Fanci = "shanhe:fanci";
    private const string Shixue = "shanhe:shixue-1";
    private const string Xueren = "shanhe:xueren";
    private const string Player = "fixture:shanhe-reaction-player";
    private const string Enemy = "fixture:shanhe-reaction-enemy";

    public static void FirstActualDamageConsumesBeforeNestedEnemyCountersAndColdChildren()
    {
        var setup = Setup([Fanci], [new(Enemy, ExtraSkillIds: [Fanci]), new(Enemy), new(Enemy)]);
        var registry = Registry(setup);
        var game = Create(setup, registry);
        StartAndPlay(game);
        var firstEvent = game.Events.Count;
        Use(game, "hurt");
        Settle(game, registry);
        var recent = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        var quota = Array.FindIndex(recent, e => e is SkillUsageConsumedEvent { SkillId: Fanci, SkillOwnerSeat: 0 });
        var retaliation = Array.FindIndex(recent, e => e is DamageAppliedEvent { SourceSeat: 0, TargetSeat: 1 });
        Require(quota >= 0 && retaliation > quota &&
            recent.OfType<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0) == 1 &&
            recent.OfType<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 1) == 1,
            "The first actual damage must reserve each participant's turn quota before nested counter-damage can re-enter its trigger.");
        Require(game.CreateSnapshot(0).Players[0].Hp == 6 && game.CreateSnapshot(0).Players.Skip(1).All(p => p.Hp == 3),
            "The original self-damage, one enemy counter and the full enemy target set must all use real damage without looping.");
        Cold(game, registry);
        var before = game.CreateSnapshot(0).Players.Skip(1).Select(p => p.Hp).ToArray();
        Use(game, "hurt");
        Settle(game, registry);
        Require(game.CreateSnapshot(0).Players.Skip(1).Select(p => p.Hp).SequenceEqual(before) &&
            game.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0) == 1,
            "A second hit in the same actual turn must not reissue the already-consumed counter, including after cold replay.");
        Cold(game, registry);
        var oldTurn = game.State.TurnNumber;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 240 && !(game.State.TurnNumber > oldTurn && game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.State.TurnNumber > oldTurn && game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The fixture must reach the next actual player turn.");
        Use(game, "hurt"); Settle(game, registry);
        Require(game.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0) == 2,
            "A later actual turn restores the first-damage counter without waiting for another round.");
        Cold(game, registry);

        var lethalSetup = Setup([Fanci], [new(Enemy, Hp: 1), new(Enemy)]);
        var lethalRegistry = Registry(lethalSetup);
        var lethal = Create(lethalSetup, lethalRegistry);
        StartAndPlay(lethal);
        firstEvent = lethal.Events.Count;
        Use(lethal, "hurt");
        Settle(lethal, lethalRegistry);
        recent = lethal.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        Require(Array.FindIndex(recent, e => e is PlayerDiedEvent { VictimSeat: 1 }) is var death && death >= 0 &&
            Array.FindIndex(recent, e => e is DamageAppliedEvent { SourceSeat: 0, TargetSeat: 2 }) > death &&
            lethal.CreateSnapshot(0).Players[2].Hp == 3,
            "Each counter target must finish its real damage, dying and death child before advancing to the next frozen enemy.");
        Cold(lethal, lethalRegistry);
    }

    public static void EmptyEnemySetConsumesTheFirstDamageQuotaAndColdReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(identity: true));
        var game = GameEngine.CreateStandard(new()
        {
            PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, Seed = 37,
            ModeId = "fixture:shanhe-reaction-identity", UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 8
        }, registry);
        StartAndPlay(game);
        Use(game, "hurt");
        Settle(game, registry);
        Require(game.Events.Select(e => e.Payload).OfType<PublicTeamTargetsBoundEvent>().Any(e => e.OwnerSeat == 0 && e.Count == 0) &&
            game.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0) == 1,
            "The first actual damage must consume its quota even when the public enemy set is empty.");
        Cold(game, registry);
        var damageCount = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count();
        Use(game, "hurt");
        Settle(game, registry);
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count() == damageCount + 1 &&
            game.Events.Select(e => e.Payload).OfType<PublicTeamTargetsBoundEvent>().Count(e => e.OwnerSeat == 0) == 1,
            "An empty first counter remains spent during the same turn and produces no fabricated damage.");
        Cold(game, registry);
    }

    public static void RecoveryDrawUsesOneActualEventAndEmptyMaximumRestartsEachBattle()
    {
        foreach (var growth in new[] { 1, 2 })
        {
            var tactic = $"shanhe:zengshou-{growth}";
            var setup = Setup([Shixue, tactic], [new(Enemy)]);
            var registry = Registry(setup);
            var game = Create(setup, registry);
            StartAndPlay(game);
            Require(game.CreateSnapshot(0).Players[0] is { Hp: 8 } player && player.MaxHp == 8 + growth &&
                !game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Shixue),
                "Battle initialization adds only empty maximum health and must not fabricate a recovery trigger.");
            var beforeHand = game.CreateSnapshot(0).Players[0].HandCount;
            Use(game, "wound-two"); Settle(game, registry);
            Use(game, "heal-two"); Settle(game, registry);
            Require(game.CreateSnapshot(0).Players[0].Hp == 8 && game.CreateSnapshot(0).Players[0].HandCount == beforeHand + 1 &&
                game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == Shixue) == 1,
                "Recovering two real health points in one event draws exactly one card, not one per point.");
            Cold(game, registry);
            // Fill the empty growth, then try the same recovery at full health.
            Use(game, "heal-two"); Settle(game, registry);
            var fullHand = game.CreateSnapshot(0).Players[0].HandCount;
            var recoveries = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == Shixue);
            Use(game, "heal-two"); Settle(game, registry);
            Require(game.CreateSnapshot(0).Players[0].HandCount == fullHand &&
                game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == Shixue) == recoveries,
                "A zero-point recovery at full health cannot draw a card.");
            Cold(game, registry);
            var next = Create(setup with { EncounterOrdinal = 2 }, Registry(setup with { EncounterOrdinal = 2 }));
            StartAndPlay(next);
            Require(next.CreateSnapshot(0).Players[0] is { Hp: 8 } initialized && initialized.MaxHp == 8 + growth,
                "A new battle initializes its empty maximum once rather than carrying or accumulating the previous battle's maximum.");
        }
    }

    public static void RandomRecoveryDamageFreezesItsRecipientAndComposesWithNativeRecovery()
    {
        var setup = Setup([Xueren, Shixue], [new(Enemy), new(Enemy), new(Enemy)]) with
        { StartingCards = [new(CardKind.Peach, Suit.Heart, 3)] };
        var registry = Registry(setup);
        var game = Create(setup, registry);
        StartAndPlay(game);
        var beforeHand = game.CreateSnapshot(0).Players[0].HandCount;
        Use(game, "wound-two"); Settle(game, registry);
        var before = game.Events.Count;
        Use(game, "heal-two"); Settle(game, registry);
        var recent = game.Events.Skip(before).Select(e => e.Payload).ToArray();
        var selected = recent.OfType<PublicTeamTargetsBoundEvent>().Single(e => e.SelectionOp == SkillProgramEffectOp.SelectRandomPublicTeamTarget);
        var damage = recent.OfType<DamageAppliedEvent>().Single();
        Require(selected.Count == 1 && selected.TargetSeatsKey == damage.TargetSeat.ToString() &&
            damage.SourceSeat == 0 && damage.TargetSeat is >= 1 and <= 3 && damage.Amount == 1 &&
            game.CreateSnapshot(0).Players[0].HandCount == beforeHand + 1 &&
            game.CreateSnapshot(0).Players.Skip(1).Sum(player => player.Hp) == 11,
            "A two-point real recovery must draw once and select exactly one public enemy for one native damage instance.");
        Cold(game, registry);
        before = game.Events.Count;
        Use(game, "heal-two"); Settle(game, registry);
        Require(!game.Events.Skip(before).Any(e => e.Payload is PublicTeamTargetsBoundEvent or DamageAppliedEvent),
            "A recovery attempt at full health must neither reroll a recipient nor manufacture damage.");
        Use(game, "wound-two"); Settle(game, registry);
        var peach = game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Kind == CardKind.Peach).Id;
        var action = game.GetHumanLegalActions().Single(action => action.CardId == peach);
        before = game.Events.Count;
        Accept(game, new PlayCardCommand(0, peach, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        Settle(game, registry);
        recent = game.Events.Skip(before).Select(e => e.Payload).ToArray();
        Require(recent.OfType<DamageAppliedEvent>().Single() is { SourceSeat: 0, Amount: 1 } &&
            recent.OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == Shixue) == 1 &&
            game.CardMovements.Count(move => move.CardId == peach && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Count(move => move.CardId == peach && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
            "An ordinary real Peach must finish one paid card use while its recovery starts one blood-blade damage and one draw.");
        Cold(game, registry);

        // The real classic locked skill recovers for each nearby damage point.
        // Its recovery children can retrigger the tactic, then stop at full HP.
        var chainedSetup = Setup([Xueren, Shixue, "shanhe:jinshen"], [new(Enemy), new(Enemy)]) with
        { ExtraSkillIds = ["classic:kuanggu"] };
        var chainedRegistry = Registry(chainedSetup);
        var chained = Create(chainedSetup, chainedRegistry);
        StartAndPlay(chained);
        Use(chained, "wound-two"); Settle(chained, chainedRegistry);
        beforeHand = chained.CreateSnapshot(0).Players[0].HandCount;
        before = chained.Events.Count;
        Use(chained, "heal-one"); Settle(chained, chainedRegistry);
        recent = chained.Events.Skip(before).Select(e => e.Payload).ToArray();
        Require(chained.CreateSnapshot(0).Players[0].Hp == 8 &&
            chained.CreateSnapshot(0).Players[0].HandCount == beforeHand + 2 &&
            recent.OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 0) == 2 &&
            recent.OfType<PublicTeamTargetsBoundEvent>().Count(e => e.SelectionOp == SkillProgramEffectOp.SelectRandomPublicTeamTarget) == 2 &&
            recent.OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == "classic:kuanggu"),
            "Blood blade, native Kuanggu and recovery draw must compose through real recovery children and terminate when health becomes full.");
        Cold(chained, chainedRegistry);

        var lethalSetup = Setup([Xueren], [new(Enemy, Hp: 1)]);
        var lethalRegistry = Registry(lethalSetup);
        var lethal = Create(lethalSetup, lethalRegistry);
        StartAndPlay(lethal);
        Use(lethal, "wound-two"); Settle(lethal, lethalRegistry);
        Use(lethal, "heal-one");
        for (var step = 0; step < 100 && lethal.State.Status != EngineStatus.Completed; step++)
        {
            Cold(lethal, lethalRegistry);
            Accept(lethal, new AdvanceOneStepCommand(lethal.Revision));
        }
        Require(lethal.State.Status == EngineStatus.Completed && !lethal.CreateSnapshot(0).Players[1].IsAlive &&
            lethal.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 1 } &&
            lethal.Events.Select(e => e.Payload).OfType<PlayerDiedEvent>().Single().VictimSeat == 1,
            "A frozen random recipient can die through its real rescue/death children and finish the last-enemy battle exactly once.");
        Cold(lethal, lethalRegistry);

        var hiddenRegistry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(identity: true));
        var hidden = GameEngine.CreateStandard(new()
        {
            PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, Seed = 37,
            ModeId = "fixture:shanhe-reaction-identity", UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 8
        }, hiddenRegistry);
        StartAndPlay(hidden);
        Use(hidden, "wound-two"); Settle(hidden, hiddenRegistry);
        before = hidden.Events.Count;
        Use(hidden, "heal-one"); Settle(hidden, hiddenRegistry);
        recent = hidden.Events.Skip(before).Select(e => e.Payload).ToArray();
        Require(recent.OfType<PublicTeamTargetsBoundEvent>().Single() is
            { SelectionOp: SkillProgramEffectOp.SelectRandomPublicTeamTarget, Count: 0, TargetSeatsKey: "" } &&
            !recent.OfType<DamageAppliedEvent>().Any(),
            "A real recovery with no public enemy must complete an empty selection without probing hidden identity or fabricating damage.");
        Cold(hidden, hiddenRegistry);
    }

    private static ShanheBattleSetup Setup(IReadOnlyList<string> tactics, IReadOnlyList<ShanheCombatantDefinition> enemies) =>
        ShanheBattleSetup.Freeze(new(37, 1, "fixture:reactions", Player, 8, 4, 0, 0, 0, [], [], [], enemies, [], "fixture-reactions")
        { TacticSkillIds = tactics });
    private static ContentRegistry Registry(ShanheBattleSetup setup) => ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(setup));
    private static GameEngine Create(ShanheBattleSetup setup, ContentRegistry registry) => GameEngine.CreateStandard(new()
    {
        Seed = setup.BattleSeed, PlayerCount = 1 + setup.EnemyRoster.Count, HumanSeat = 0, HumanRole = null,
        HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
        UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 8
    }, registry);
    private static void StartAndPlay(GameEngine game)
    {
        Accept(game, new StartGameCommand());
        for (var i = 0; i < 80 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; i++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The bounded fixture must reach the player's real play phase.");
    }
    private static void Use(GameEngine game, string id) => Accept(game, new UseProgramSkillCommand(0, Force, id, [], [], game.Revision, game.PendingDecision!.PromptId));
    private static void Settle(GameEngine game, ContentRegistry registry)
    {
        for (var i = 0; i < 160 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; i++)
        {
            if (game.ResolutionStack.Any(f => f is ProgramSkillFrame { SkillId: Fanci or Shixue or Xueren or "classic:kuanggu" })) Cold(game, registry);
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "Reaction children must return to the same player's actual play prompt.");
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, game.State.Players.Count).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Cold replay must preserve reaction quotas, exact child frames, health and native card movements.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "Reaction fixture command rejected.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class FixturePackage(ShanheBattleSetup? setup = null, bool identity = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-reactions", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string name)
            {
                using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + name) ?? throw new InvalidOperationException("Missing real reaction bundle.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            var real = SkillProgramCatalog.Load(Read("shanhe-reactions.rules.json"), Read("shanhe-reactions.presentation.json"));
            foreach (var entry in real.Programs) builder.AddSkill(new(entry.Key, real.Presentations[entry.Key].Name, real.Presentations[entry.Key].Description) { Program = entry.Value, Tags = SkillTag.Locked });
            var passives = SkillProgramCatalog.Load(Read("shanhe-passives.rules.json"), Read("shanhe-passives.presentation.json"));
            builder.AddSkill(new("shanhe:jinshen", "近身术", passives.Presentations["shanhe:jinshen"].Description)
            { Program = passives.Programs["shanhe:jinshen"], Tags = SkillTag.Locked });
            using var nativeRules = JsonDocument.Parse(Read("passive-damage-cutover.rules.json"));
            var nativeRecovery = nativeRules.RootElement.GetProperty("skills").EnumerateArray()
                .Single(skill => skill.GetProperty("id").GetString() == "classic:kuanggu");
            using var nativePresentation = JsonDocument.Parse(Read("passive-damage-cutover.presentation.json"));
            var nativeCatalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { nativeRecovery } }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, JsonElement>
                    { ["classic:kuanggu"] = nativePresentation.RootElement.GetProperty("skills").GetProperty("classic:kuanggu") } }));
            builder.AddSkill(new("classic:kuanggu", "狂骨", nativeCatalog.Presentations["classic:kuanggu"].Description)
            { Program = nativeCatalog.Programs["classic:kuanggu"], Tags = SkillTag.Locked });
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new object[]
                {
                    new { id = Force, revision = 1, activations = new object[]
                    {
                        new { id = "hurt", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "damage", target = "owner", amount = 1, nature = "normal" } } },
                        new { id = "wound-two", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "loseHp", target = "owner", amount = 2 } } },
                        new { id = "heal-two", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "recover", target = "owner", amount = 2 } } },
                        new { id = "heal-one", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "recover", target = "owner", amount = 1 } } }
                    } },
                    new { id = Quiet, revision = 1, modifiers = new[] { new { id = "quiet", query = "attackRange", operation = "set", value = 1, priority = 0 } } }
                }
            }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Force, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Bounded reaction fixture" }) }));
            foreach (var entry in fixture.Programs) builder.AddSkill(new(entry.Key, entry.Key, "fixture") { Program = entry.Value });
            builder.AddDeck(new("fixture:shanhe-reaction-deck", "闪牌堆", 4, 2, [new("standard:dodge", 80)]));
            if (identity)
            {
                var ids = Enumerable.Range(0, 4).Select(i => $"fixture:reaction-identity-{i}").ToArray();
                foreach (var id in ids) builder.AddGeneral(new(id, id, "", Force, "shu", BaseHp: 8, AdditionalSkillIds: [Fanci, Xueren]));
                builder.AddMode(new("fixture:shanhe-reaction-identity", "无公开阵营空集", 4, 4,
                    new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                    "fixture:shanhe-reaction-deck", GeneralPoolIds: ids));
                return;
            }
            builder.AddGeneral(new(Player, "行者", "", Force, "shu"));
            builder.AddGeneral(new(Enemy, "守军", "", Quiet, "qun"));
            var count = 1 + setup!.EnemyRoster.Count;
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "反应战法", count, count, new Dictionary<string, int>(),
                "fixture:shanhe-reaction-deck", GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = setup.EnemyRoster.Count }));
        }
    }
}
