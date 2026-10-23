using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class Ol2016ShenZhaoYunChecks
{
    private const string Hero = "ol:shen-zhao-yun-2016", Longhun = "ol:longhun-2016", Driver = "fixture:old-longhun-driver",
        Quiet = "fixture:old-longhun-quiet", Counter = "fixture:old-longhun-counter", Watch = "fixture:old-longhun-watch",
        Peer = "fixture:old-longhun-peer", Deck = "fixture:old-longhun-deck";

    public static void LiveHpCostsNativeEquipmentUseResponseAndColdRecovery()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(0).MaxHp == 3 && s.V(0).HandCount == 8,
            "The real opening Draw adds one lost HP to the ordinary two-card draw.");
        var diamond = s.V(0).Hand.Single(c => c.Suit == Suit.Diamond).Id;
        var horse = s.V(0).Equipment.Single(c => c.Kind == CardKind.OffensiveHorse).Id;
        var weapon = s.V(0).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        var action = s.G.GetHumanLegalActions().Single(a => a.SkillId == Longhun && a.ProgramActivationId == "diamond-use");
        s.Need(action.MinCardCount == 2 && action.MaxCardCount == 2 && action.SelectableCardIds!.Contains(horse),
            "At two HP, the legal action requires exactly two current same-suit hand/equipment entities.");
        var before = GameCheckpointJson.Serialize(s.G.CreateCheckpoint());
        var rejected = s.G.Submit(new UseProgramSkillCommand(0, Longhun, "diamond-use", [diamond], [1], s.G.Revision, s.P!.PromptId));
        s.Need(!rejected.Accepted && before == GameCheckpointJson.Serialize(s.G.CreateCheckpoint()),
            "A one-material command cannot bypass the live two-HP payment requirement.");
        var targetHp = s.V(1).Hp;
        s.Use(Longhun, "diamond-use", [diamond, horse], [1]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Action?.ConversionChain.Any(c => c.SkillId == Longhun) == true);
        s.Need(use.Action is { EffectiveKind: CardKind.FireSlash, ActorSeat: 0, ProviderSeat: 0 } actual &&
            actual.PhysicalCards.Count == 2 && actual.PhysicalCards.Any(c => c.CardId == horse && c.From == CardLocation.Equipment(0)) &&
            actual.PhysicalCards.Any(c => c.CardId == diamond && c.From == CardLocation.Hand(0)),
            "The original native Fire Slash retains both real payment origins before its committed-use child.");
        s.Cold(); s.Skip(); s.Play();
        // The first attack finishes before the supported non-card damage counter starts a separate native Slash.
        s.Use(Driver, "poke", [], [3]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.P!.Choices.Any(c => Conversion(c) && c.Cards.Count == 2 && c.Cards.Contains(weapon)) &&
            !s.P.Choices.Any(c => Conversion(c) && c.Cards.Count == 1),
            "The real retaliation response at two HP offers a two-club conversion including equipped Crossbow, with no one-card shortcut.");
        var response = s.P.Choices.First(c => Conversion(c) && c.Cards.Count == 2 && c.Cards.Contains(weapon));
        var firstDodge = response.Cards.ToArray(); s.Answer(c => c.Id == response.Id); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(1).Hp == targetHp - 1 &&
            s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 1 && e.Nature == DamageNature.Fire) &&
            new[] { diamond, horse }.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1) &&
            firstDodge.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1),
            "Both native conversions pay each selected entity once; original Longhun has no later-version two-red bonus.");

        var hearts = s.V(0).Hand.Where(c => c.Suit == Suit.Heart).Take(2).Select(c => c.Id).ToArray();
        s.Use(Longhun, "heart-use", hearts, []); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.V(0).Hp == 3 && s.E<HpScaledCardUsePaidEvent>().Last() is { OwnerHp: 2, MaterialCount: 2 },
            "Two hearts pay the live two-HP cost and recover exactly one, freezing that cost before the recovery changes HP.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(hearts.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
            "Returning the genuine recovery child after cold restore never demands a third material or repeats payment.");

        s.Use(Driver, "lose-two", [], []); s.Play(); s.Use(Driver, "poke", [], [3]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.V(0).Hp == 1 && s.P!.Choices.Any(c => Conversion(c) && c.Cards.Count == 1) &&
            !s.P.Choices.Any(c => Conversion(c) && c.Cards.Count == 2),
            "A later real response follows the new one-HP cost rather than a cached two-material family.");
        s.Answer(c => Conversion(c) && c.Cards.Count == 1); s.Play();
        var peach = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Peach && a.ConversionSource is { SkillId: Longhun, BindingId: "heart" });
        s.PlayCard(peach); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Skip(); s.Play();
        s.Need(s.V(0).Hp == 2 && s.E<HpScaledCardUsePaidEvent>().Last() is { OwnerHp: 1, MaterialCount: 1 } &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action.Type == CardActionType.Response && e.Action.ProviderSeat == 0 &&
                e.Action.ConversionChain.Any(c => c.SkillId == Longhun) && e.Action.PhysicalCards.Count == 1),
            "At one HP, ordinary native one-card Peach and Dodge conversions resume without special rewards or stale payment counts.");
        s.Replay();
    }

    private static bool Conversion(PromptChoice c) => c.Parameters.GetValueOrDefault("conversion-skill-id") == Longhun;
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-2016-shen-zhao-yun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"lose-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Counter}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],
                "triggers":[{"id":"counter","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"fire-use","isSkillInvocation":false,"window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["fireSlash"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"recovery","isSkillInvocation":false,"window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, Counter, Watch }.ToDictionary(id => id, id => new { name = id, description = "原版龙魂实时数量与真实原生续接" }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实回击目标", "", Quiet, "wei", 20));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "OL原版龙魂小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string skill, string binding, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, skill, binding, cards, targets, G.Revision, P!.PromptId));
        public void PlayCard(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 160; i++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None, "The fixed short real-turn fixture must reach its next native boundary.");
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected old Longhun prompt " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Old Longhun did not reach its bounded native occurrence " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold restoration preserves the original native use, frozen paid count and its child."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(state == State(), "Command replay preserves dynamic counts, original material origins and once-only native payments."); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Longhun command."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { Turn = G.State.TurnNumber, Prompt = P })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "ol-2016-longhun", Hero, 2, 1, 0, 0, 0, [Driver, Watch],
                [new(CardKind.Dodge, Suit.Diamond, 2), new(CardKind.Slash, Suit.Heart, 3), new(CardKind.Slash, Suit.Heart, 4),
                    new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Slash, Suit.Club, 6)],
                [new(CardKind.OffensiveHorse, Suit.Diamond, 5), new(CardKind.Crossbow, Suit.Club, 1)],
                [new(Peer) { StartingHand = [] }, new(Peer, ExtraSkillIds: [Counter]) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('e', 64)) { DefaultGodFactionId = "shu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
