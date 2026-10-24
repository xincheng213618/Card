using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class XiahouBaChecks
{
    private const string Hero = "mobile:shi-xiahou-ba",
        Quiet = "fixture:shi-xiahou-ba-quiet", Keeper = "fixture:shi-xiahou-ba-keeper",
        Deck = "fixture:shi-xiahou-ba-deck";

    public static void LibbingCyclesOptionsAndResetsOnDamageUse()
    {
        var opening = new[]
        {
            Sword(0), Sword(1), Sword(2), Sword(3), Slash(0),
        };
        var s = Scene.Start("libbing", 4, [Keeper], NoEnemies(), opening); s.Play();
        s.Equip(opening[0]);
        s.Reach(() => s.E<ShiXbLibbingAdvancedEvent>().Length >= 1);
        s.Need(s.E<ShiXbLibbingAdvancedEvent>() is [{ Option: 1, Count1: 1, Count2: 0, Count3: 0 }] &&
            s.G.GetAttackRange(0) == 3,
            "The first non-damage use executes option 1 and raises the range of the equipped weapon by one.");
        s.Equip(opening[1]);
        s.Reach(() => s.E<ShiXbLibbingAdvancedEvent>().Length >= 2);
        s.Need(s.E<ShiXbLibbingAdvancedEvent>() is [{ Option: 1 }, { Option: 2, Count2: 1 }] &&
            s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-libing.draw") == 2,
            "The second non-damage use executes option 2 and immediately draws X=1+1=2 cards.");
        s.Equip(opening[2]);
        s.Reach(() => s.E<ShiXbLibbingAdvancedEvent>().Length >= 3);
        s.Need(s.E<ShiXbLibbingAdvancedEvent>().Last().Option == 3 && s.E<ShiXbLibbingAdvancedEvent>().Last().Count3 == 1,
            "The third non-damage use executes option 3 and arms one pending damage boost.");
        s.Slash(1);
        s.Reach(() => s.E<ShiXbLibbingResetEvent>().Length >= 1);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 2) &&
            s.E<ShiXbLibbingResetEvent>().Length == 1,
            "The next damage card deals +1 damage and the completed damage use resets the skill.");
        s.Equip(opening[3]);
        s.Reach(() => s.E<ShiXbLibbingAdvancedEvent>().Length >= 4);
        s.Need(s.E<ShiXbLibbingAdvancedEvent>().Last() is { Option: 1, Count1: 1 } && s.G.GetAttackRange(0) == 3,
            "After the reset the cycle restarts at option 1 with the earlier range bonus cleared.");
        s.Cold(); s.Replay();
    }

    public static void PoxiUsesDiscardedSlashesWithoutDistanceOrCount()
    {
        var opening = Enumerable.Range(0, 7).Select(i => Slash(i)).ToArray();
        var s = Scene.Start("poxi", 4, [], NoEnemies(), opening); s.Play();
        s.Slash(1);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1) &&
            !s.E<ShiXbPoxiNativeIssuedEvent>().Any(),
            "The play-phase Slash stays an ordinary counted use with no poxi involvement.");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
        var discard = s.V(0).Hand.Where(c => c.Kind == CardKind.Slash).Take(2).Select(c => c.Id).ToArray();
        s.Need(discard.Length == 2, "The bounded opening hand must hold two discardable Slashes.");
        s.Discard(discard);
        for (var round = 0; round < 2; round++)
        {
            var target = round == 0 ? 2 : 3;
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } &&
                s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use" &&
                    c.Parameters.GetValueOrDefault("target") == target.ToString()));
            var issued = s.E<ShiXbPoxiNativeIssuedEvent>().Length;
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "use" &&
                c.Parameters.GetValueOrDefault("target") == target.ToString());
            s.Reach(() => s.E<ShiXbPoxiNativeIssuedEvent>().Length >= issued + 1);
            s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == target && e.Amount == 1) &&
                s.E<CardUsedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == target &&
                    e.CardId == discard[round]),
                $"Poxi turns discarded Slash #{round + 1} into a real unlimited-distance use that hits seat {target}.");
        }
        s.Need(s.E<ShiXbPoxiNativeIssuedEvent>().Select(e => e.CardId).SequenceEqual(discard.OrderBy(id => id)),
            "Both discarded Slashes were used beyond the one-per-turn limit in discard order.");
        s.Cold(); s.Replay();
    }

    public static void PoxiGatesResponsesByHandCountAndIgnoresNonSlashDiscards()
    {
        var opening = Enumerable.Range(0, 6).Select(i => Slash(i)).Append(Dodge()).ToArray();
        var s = Scene.Start("poxi-gate", 4, [],
            new[] { new Enemy(8, Dodges: 2), new Enemy(8, Dodges: 7), new Enemy(8), new Enemy(8) }, opening);
        s.Play(); s.End();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
        var slashIds = s.V(0).Hand.Where(c => c.Kind == CardKind.Slash).Take(2).Select(c => c.Id).ToArray();
        var dodgeId = s.V(0).Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Need(slashIds.Length == 2, "The bounded opening hand must hold two discardable Slashes.");
        s.Discard(new[] { dodgeId }.Concat(slashIds).Order().ToArray());
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use"));
        var issued = s.E<ShiXbPoxiNativeIssuedEvent>().Length;
        s.Answer(c => c.Parameters.GetValueOrDefault("target") == "1");
        s.Reach(() => s.E<ShiXbPoxiNativeIssuedEvent>().Length >= issued + 1);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 1) &&
            s.V(1).HandCount == 2,
            "The small-hand target may not respond to the poxi Slash and takes its damage.");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use"));
        issued = s.E<ShiXbPoxiNativeIssuedEvent>().Length;
        s.Answer(c => c.Parameters.GetValueOrDefault("target") == "2");
        s.Reach(() => s.E<ShiXbPoxiNativeIssuedEvent>().Length >= issued + 1);
        s.Reach(() => s.P is not { PlayerSeat: 2, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash });
        s.Need(!s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2) && s.V(2).HandCount == 6,
            "The larger-hand target answers the poxi Slash with its Dodge and stays unhurt.");
        s.Need(s.E<ShiXbPoxiNativeIssuedEvent>().Length == 2,
            "Exactly the two discarded Slashes were converted; the Dodge discard triggered nothing.");
        s.Cold(); s.Replay();
    }

    private sealed record Enemy(int Hp, int Dodges = 0);

    private static ShanheStartingCard Slash(int i) => new(CardKind.Slash, i % 2 == 0 ? Suit.Heart : Suit.Club, 4 + i % 9);
    private static ShanheStartingCard Dodge() => new(CardKind.Dodge, Suit.Heart, 7);
    private static ShanheStartingCard Sword(int i) => new(CardKind.QinggangSword, Suit.Spade, 6 + i);
    private static Enemy[] NoEnemies() => [new Enemy(8), new Enemy(8), new Enemy(8), new Enemy(8)];
    private const string Peer = "fixture:shi-xiahou-ba-peer";

    private sealed class Scene(GameEngine game, ContentRegistry registry, ShanheStartingCard[] opening)
    {
        public GameEngine G { get; private set; } = game;
        public ShanheStartingCard[] Opening { get; } = opening;
        public PendingDecision? P => Enumerable.Range(0, 5).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();

        public void Equip(ShanheStartingCard card)
        {
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Use(IdOf(card));
        }
        public int IdOf(ShanheStartingCard card) => V(0).Hand
            .Single(c => (int)c.Kind == (int)card.Kind && c.Rank == card.Rank).Id;
        public void Use(int cardId)
        {
            var actions = G.GetHumanLegalActions();
            var a = actions.FirstOrDefault(x => x.CardId == cardId);
            Need(a is not null, $"Card {cardId} has no legal action. Available: " +
                JsonSerializer.Serialize(actions.Select(x => new { x.Kind, x.CardId, x.PlayedCardKind, x.EquipmentKind }).Take(30)));
            Accept0(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void Discard(int[] cardIds)
        {
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
            Accept0(new DiscardCardsCommand(0, cardIds, P!.PromptId, G.Revision));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected prompt choice must be published.");
            Accept0(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept0(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Slash(int target)
        {
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Accept0(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 1600; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.PlayerSeat == 0 && p.Kind == DecisionKind.DiscardCards) throw new InvalidOperationException("An unhandled discard boundary requires an explicit choice. " + Dump());
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or
                        DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.Kind != DecisionKind.PlayCard &&
                        p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
                    { Answer(c => c.Parameters.GetValueOrDefault("action") == "end-play"); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Xiahou Ba boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain its participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Xiahou Ba did not reach the expected bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact phase identity and ledger facts."); }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the observed flow exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Xiahou Ba command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 5).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 5).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Advanced = E<ShiXbLibbingAdvancedEvent>().Select(e => new { e.Option, e.Count1, e.Count2, e.Count3 }),
            Reset = E<ShiXbLibbingResetEvent>().Select(e => new { e.UsedKind }),
            Poxi = E<ShiXbPoxiNativeIssuedEvent>().Select(e => new { e.CardId, e.TargetSeat }),
            Returned = E<ShiXbPoxiNativeReturnedEvent>().Select(e => e.UseId),
            Damage = E<DamageAppliedEvent>().Select(e => new { e.TargetSeat, e.Amount }),
            Moves = G.CardMovements.TakeLast(40).Select(m => new { m.CardId, From = m.From.Zone, FromSeat = m.From.OwnerSeat, To = m.To.Zone, ToSeat = m.To.OwnerSeat, Reason = m.Reason.Value }),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId, int heroHp, string[] heroExtras, Enemy[] enemies, ShanheStartingCard[] opening)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheCombatantDefinition Combatant(int seat, Enemy enemy) => new(Peer, Hp: enemy.Hp, ExtraSkillIds: [Quiet])
            { StartingHand = Enumerable.Range(0, enemy.Dodges).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 7 + i)).ToArray() };
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, heroHp, heroHp - 4, 0, 0, 0, heroExtras, opening, [],
                enemies.Select((enemy, index) => Combatant(index + 1, enemy)).ToArray(), [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 5, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry, opening); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-xiahou-ba", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keeper}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0},
                {"id":"many","query":"slashLimit","operation":"add","value":10,"priority":0}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [Keeper] = new { name = Keeper, description = "保持手牌并无限制出杀。" },
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 8));
            b.AddCard(new("fixture:shi-xiahou-ba-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddCard(new("fixture:shi-xiahou-ba-dodge", "闪", "基本牌", "同体闪实体", CardKind.Dodge));
            b.AddCard(new("fixture:shi-xiahou-ba-sword", "青釭剑", "装备牌", "同体青釭剑实体", CardKind.QinggangSword));
            b.AddDeck(new(Deck, "固定真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 160)
                .Select(i => i % 7 == 3
                    ? new ContentDeckPhysicalCard("fixture:shi-xiahou-ba-dodge", Suit.Heart, 7)
                    : new ContentDeckPhysicalCard("fixture:shi-xiahou-ba-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势夏侯霸实际检查", 5, 5, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 4 }));
        }
    }
}
