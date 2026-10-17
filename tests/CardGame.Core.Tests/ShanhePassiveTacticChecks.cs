using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanhePassiveTacticChecks
{
    private const string Driver = "fixture:shanhe-passive-driver", Quiet = "fixture:shanhe-passive-quiet";
    private const string Owner = "fixture:shanhe-passive-owner", Deck = "fixture:shanhe-passive-deck";
    private const string Ruoxi = "shanhe:ruoxi", Jinshen = "shanhe:jinshen";
    private const string Bingfan = "shanhe:bingfan", Lezhuan = "shanhe:lezhuan";

    public static void LowHandDamageRechecksProgramsChainsAndSkillLossCold()
    {
        var s = Scenario.Start("damage");
        s.Need(s.V().Hp == 8 && s.V().HandCount == 1, "The fixed opening starts below the real current HP, without searching seeds.");
        var slash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        s.PlayCard(slash, 1);
        s.Need(s.Events<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 2 },
            "A physical Slash receives the source's enabled low-hand bonus.");
        s.Damage("normal", 1, 2);
        s.Use("stock");
        s.Need(s.V().HandCount == 12 && s.V().HandCount >= s.V().Hp, "A real twelve-card draw crosses the qualification threshold.");
        s.Damage("normal", 1, 1);
        var trimmed = s.V().Hand.Select(c => c.Id).ToArray();
        s.Use("trim", trimmed);
        s.Need(s.V().HandCount == 0 && trimmed.All(id => s.G.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)),
            "Real physical discards restore the low-hand condition instead of altering fixture state directly.");
        s.Damage("normal", 1, 2);
        var beforeLoss = s.Events<DamageAppliedEvent>().Length;
        var hp = s.V(1).Hp;
        s.Use("loss", targets: [1]);
        s.Need(s.V(1).Hp == hp - 1 && s.Events<DamageAppliedEvent>().Length == beforeLoss,
            "Losing HP is exactly one HP and never becomes damage or receives a damage modifier.");
        s.Use("chain", targets: [1]);
        s.Use("chain", targets: [2]);
        s.Cold();
        var beforeFire = s.Events<DamageAppliedEvent>().Length;
        s.Begin("fire", targets: [1]);
        s.Cold();
        s.Play();
        var fire = s.Events<DamageAppliedEvent>().Skip(beforeFire).ToArray();
        s.Need(fire.Length == 2 && fire[0] is { SourceSeat: 0, TargetSeat: 1, Amount: 2, Nature: DamageNature.Fire } &&
            fire[1] is { SourceSeat: 0, TargetSeat: 2, Amount: 3, Nature: DamageNature.Fire } &&
            !s.V(1).IsChained && !s.V(2).IsChained,
            "The original fire damage is two; its real propagated damage starts from two and rechecks the same source's bonus once, becoming three.");
        s.Use("lose-ruoxi");
        s.Need(s.V().Skills?.Any(skill => skill.ContentId == Ruoxi) != true,
            "The native skill-loss operation actually removes the source, rather than merely changing its presentation.");
        s.Damage("normal", 1, 1);
        s.Cold();
    }

    public static void FinalOutgoingDistanceIgnoresHorsesButKeepsReverseAndSlashLimitCold()
    {
        var s = Scenario.Start("distance");
        var horse = s.V().Hand.Single(c => c.Kind == CardKind.DefensiveHorse).Id;
        s.Use("equip", [horse], [2]);
        s.Need(s.V(2).Equipment.Single().Id == horse && s.V().Equipment.Single().Kind == CardKind.DefensiveHorse,
            "Both defensive horses occupy genuine public equipment zones on their respective owners.");
        s.Need(s.G.GetSeatDistance(0, 2) == 2 && s.G.GetCombatDistance(0, 2) == 1 &&
            s.G.GetCombatDistance(2, 0) == 3 && s.G.GetAttackRange(0) == 1,
            "The final outgoing distance is one even after the enemy's defensive horse; the reverse calculation and actual weapon range stay independent.");
        s.Cold();
        var slashes = s.V().Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        s.Need(slashes.Length == 2 && s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash &&
            a.CardId == slashes[0] && a.TargetSeats.SequenceEqual([2])),
            "Nearness genuinely makes the seat-two opponent Slashable with the unmodified one-point attack range.");
        s.PlayCard(slashes[0], 2);
        s.Need(s.V().Hand.Any(c => c.Id == slashes[1]) &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            s.Events<DamageAppliedEvent>().Single() is { TargetSeat: 2, Amount: 1 },
            "Nearness supplies no extra Slash: a second real Slash remains in hand after the first ordinary use consumes the turn limit.");
        s.Use("lose-jinshen");
        s.Need(s.G.GetCombatDistance(0, 2) == 3 && s.G.GetCombatDistance(2, 0) == 3,
            "Losing the effective policy immediately restores normal directional equipment calculations without a stale distance cache.");
        s.Cold();
    }

    public static void DelayedOutcomeInvertsOnceAcrossRealSourcesWithoutRewritingCardsCold()
    {
        foreach (var suit in new[] { Suit.Heart, Suit.Club })
        foreach (var copies in new[] { 0, 2 })
        {
            var s = Scenario.Start("judgment", suit, copies);
            var delayed = s.V().Hand.ToDictionary(c => c.Kind, c => c.Id);
            // The two-copy fixture owns each exact production program natively and
            // through the separate campaign tactic grant, with distinct instance IDs.
            foreach (var kind in new[] { CardKind.Indulgence, CardKind.SupplyShortage })
                s.Use("give", [delayed[kind]], [1]);
            s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
            s.Reach(() => s.Events<DelayedCardPlacedEvent>().Length == 2);
            s.Need(s.Events<DelayedCardPlacedEvent>().All(e => e.SourceSeat == 1 && e.TargetSeat == 0 &&
                delayed[e.CardKind] == e.CardId),
                "The opposing AI really uses both given physical delayed tricks against its sole enemy; no judgment zone is injected.");
            s.Cold();
            s.Reach(() => s.Events<DelayedCardResolvedEvent>().Length == 2);
            foreach (var result in s.Events<DelayedCardResolvedEvent>())
            {
                var printedSafe = result.CardKind == CardKind.Indulgence ? suit == Suit.Heart : suit == Suit.Club;
                var expectedSafe = copies == 0 ? printedSafe : !printedSafe;
                var judgment = s.Events<JudgmentResolvedEvent>().Single(e => e.ResolutionId == result.ResolutionId);
                var diagnostic = JsonSerializer.Serialize(new { PrintedSuit = suit, Copies = copies, ExpectedSafe = expectedSafe,
                    Result = result, Judgment = judgment, Skills = s.V().Skills });
                s.Need(result.TargetSeat == 0 && result.CardId == delayed[result.CardKind],
                    "The delayed completion must retain its original actual target and paid entity. " + diagnostic);
                s.Need(result.JudgmentSucceeded == expectedSafe && judgment.Succeeded == expectedSafe,
                    "The finalized safe-result fact must invert once, including two enabled instances of the same production policy. " + diagnostic);
                s.Need(result.SkippedPlayPhase == (result.CardKind == CardKind.Indulgence && !expectedSafe) &&
                    result.SkippedDrawPhase == (result.CardKind == CardKind.SupplyShortage && !expectedSafe),
                    "The actual delayed phase effect must follow the finalized inverted outcome. " + diagnostic);
                s.Need(judgment.CardId == result.JudgmentCardId && judgment.CardKind == CardKind.Dodge &&
                    judgment.Suit == suit && judgment.Rank == 7 &&
                    s.G.CardMovements.Any(m => m.CardId == result.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) &&
                    s.G.CardMovements.Any(m => m.CardId == result.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Judgment(0)) &&
                    s.G.CardMovements.Any(m => m.CardId == result.CardId && m.From == CardLocation.Judgment(0) && m.To == CardLocation.DiscardPile),
                    "Inversion preserves the judgment's original physical entity, suit and rank, and the actual delayed entity still follows ordinary placement/discard rules.");
            }
            s.Cold();
        }
    }

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
        public void Accept(GameCommand command)
        {
            var result = G.Submit(command);
            Need(result.Accepted, result.Error?.Message ?? "The passive-tactic fixture command was rejected.");
        }
        public void Begin(string activation, int[]? cards = null, int[]? targets = null) => Accept(
            new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string activation, int[]? cards = null, int[]? targets = null) { Begin(activation, cards, targets); Play(); }
        public void Damage(string activation, int target, int amount)
        {
            var before = Events<DamageAppliedEvent>().Length;
            Use(activation, targets: [target]);
            var events = Events<DamageAppliedEvent>().Skip(before).ToArray();
            Need(events.Length == 1 && events[0].SourceSeat == 0 && events[0].TargetSeat == target && events[0].Amount == amount,
                "Each independent genuine program damage must use its source's current hand, HP and active skill ownership.");
        }
        public void PlayCard(int id, int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId == id && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
            Play();
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 240; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                    Accept(new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, G.Revision));
                else if (P is { PlayerSeat: 0, Choices.Count: > 0 } choice)
                    Accept(new AnswerPromptCommand(0, choice.PromptId, choice.Choices[0].Id, G.Revision));
                else if (P is { PlayerSeat: 0 })
                    throw new InvalidOperationException("Unexpected passive-tactic boundary: " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded passive-tactic boundary: " + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.Phase, Prompt = P }));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves source qualifications, distances, private views, original card faces and finalized judgment outcomes.");
        }
        public static Scenario Start(string scene, Suit suit = Suit.Spade, int copies = 0)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene, suit, copies));
            var setup = new ShanheBattleSetup(73, 1, "fixture:passive-" + scene, Owner, scene == "damage" ? 8 : 12,
                0, 0, 0, 0, [], scene switch
                {
                    "damage" => [new(CardKind.Slash, Suit.Spade, 6)],
                    "distance" => [new(CardKind.DefensiveHorse, Suit.Club, 5), new(CardKind.Slash, Suit.Spade, 6), new(CardKind.Slash, Suit.Club, 8)],
                    _ => [new(CardKind.Indulgence, Suit.Diamond, 9), new(CardKind.SupplyShortage, Suit.Spade, 10)]
                }, scene == "distance" ? [new(CardKind.DefensiveHorse, Suit.Heart, 4)] : [],
                Enumerable.Range(1, 3).Select(i => new ShanheCombatantDefinition(Peer(i), MaxHpBonus: 20)).ToArray(), [], new string('b', 64))
            { TacticSkillIds = scene == "judgment" && copies > 0 ? [Bingfan, Lezhuan] : [] };
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var s = new Scenario(game, registry);
            s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private static string Peer(int seat) => "fixture:shanhe-passive-peer-" + seat;

    private sealed class Fixture(string scene, Suit suit, int copies) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-passive-tactics", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            static string Resource(string suffix)
            {
                var assembly = typeof(StandardContentPackage).Assembly;
                var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + suffix, StringComparison.Ordinal));
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Resource("shanhe-passives.rules.json"), Resource("shanhe-passives.presentation.json"));
            foreach (var (id, program) in production.Programs)
                builder.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = program, ProgramPresentation = production.Presentations[id] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"normal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"normal"}]},
                    {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                    {"id":"loss","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                    {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                    {"id":"trim","minCards":1,"maxCards":20,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
                    {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                    {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
                    {"id":"lose-ruoxi","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Ruoxi}}"],"sourceBind":"standard:none"}]},
                    {"id":"lose-jinshen","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Jinshen}}"],"sourceBind":"standard:none"}]}],
                    "modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, _ => new { name = "真实战法小夹具", description = "原生伤害、移动、装备和技能失去" })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string>();
            if (scene == "damage") own.Add(Ruoxi);
            if (scene == "distance") own.Add(Jinshen);
            if (scene == "judgment" && copies == 2) own.AddRange([Bingfan, Lezhuan]);
            builder.AddGeneral(new(Owner, "战法使用者", "supporter", Driver, "qun", 12, own));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new(Peer(seat), "固定敌军", "supporter", "standard:none", "shu", 12,
                    scene == "judgment" && seat > 1 ? [Quiet] : []));
            builder.AddDeck(new(Deck, "同花色同点数真实牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:dodge", suit, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "战法底层机制", 4, 4,
                new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
