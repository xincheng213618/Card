using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryZhaoTongZhaoGuangChecks
{
    private const string Hero = "anniversary:zhao-tong-zhao-guang", Yizan = "anniversary:yizan", Longyuan = "anniversary:longyuan",
        Driver = "fixture:ztzg-driver", Quiet = "fixture:ztzg-quiet", Counter = "fixture:ztzg-counter", Watch = "fixture:ztzg-watch",
        Peer = "fixture:ztzg-peer", Deck = "fixture:ztzg-deck";

    public static void PairBasicRequirementNativeWinePlayedSlashAwakeningAndQingrenCold()
    {
        var s = Scene.Start(); s.Play();
        int Hand(CardKind kind, Suit suit, int rank) => s.V(0).Hand.Single(c => c.Kind == kind && c.Suit == suit && c.Rank == rank).Id;
        var rescueBasic = Hand(CardKind.Slash, Suit.Heart, 2); var wineBasic = Hand(CardKind.Dodge, Suit.Club, 3);
        var wineOther = Hand(CardKind.Dismantlement, Suit.Spade, 4); var thunderBasic = Hand(CardKind.Peach, Suit.Heart, 5);
        var thunderOther = Hand(CardKind.DrawTwo, Suit.Diamond, 6); var playedBasic = Hand(CardKind.Slash, Suit.Club, 7);
        var playedOther = Hand(CardKind.Dismantlement, Suit.Heart, 8); var singleDodge = Hand(CardKind.Slash, Suit.Spade, 9);
        var singlePeach = Hand(CardKind.Dodge, Suit.Diamond, 10);
        var nonbasic = new[] { Hand(CardKind.DrawTwo, Suit.Spade, 11), Hand(CardKind.Dismantlement, Suit.Diamond, 12) };
        var weapon = s.V(0).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Need(s.G.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Yizan).All(a =>
                a.SelectableCardIds is not { Count: 2 } ids || !ids.Order().SequenceEqual(nonbasic.Order())),
            "Two nonbasic materials are never an available original Yizan payment.");

        s.Driver("lose-four"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.P!.Choices.Any(c => Conversion(c, "pair-alcohol") && c.Cards.Order().SequenceEqual(new[] { rescueBasic, weapon }.Order())),
            "The actual dying-use menu offers a HE two-material Alcohol containing a real basic card.");
        s.Answer(c => Conversion(c, "pair-alcohol") && c.Cards.Order().SequenceEqual(new[] { rescueBasic, weapon }.Order())); s.Play();
        s.Need(s.V(0).Hp == 1 && s.E<DyingResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedAlcohol) &&
            s.Zone(rescueBasic) == CardLocation.DiscardPile && s.Zone(weapon) == CardLocation.DiscardPile &&
            s.Invocations is [{ NativeUseFrameId: { } rescueUse }] && s.E<CardUseDeclaredEvent>().Any(e => e.ResolutionId == rescueUse && e.CardKind == CardKind.Alcohol && e.SourceSeat == 0) &&
            s.G.CardMovements.Count(m => m.CardId == weapon && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == rescueBasic && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "Native self-rescue recovers one and spends both original HE entities exactly once.");

        s.Pair(CardKind.Alcohol, [wineBasic, wineOther], []); s.Play();
        s.Need(s.V(0).HasAlcoholEffect && new[] { wineBasic, wineOther }.All(id => s.Zone(id) == CardLocation.DiscardPile) && s.Invocations.Length == 2,
            "A real normal Alcohol Use finishes both physical materials and grants the normal wine effect.");
        var targetHp = s.V(3).Hp; s.Pair(CardKind.ThunderSlash, [thunderBasic, thunderOther], [3]); s.Play();
        s.Need(s.V(3).Hp == targetHp - 2 && !s.V(0).HasAlcoholEffect && s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3 && e.Nature == DamageNature.Thunder && e.Amount == 2),
            "The real two-material Thunder Slash consumes the actual wine and retains ordinary elemental damage.");
        s.Driver("duel", [1, 0]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => Conversion(c, "pair-slash") && c.Cards.Order().SequenceEqual(new[] { playedBasic, playedOther }.Order())); s.Play();
        s.Need(s.Invocations.Length == 4 && s.Invocations.Select(e => e.ActionId).Distinct().Count() == 4 &&
            s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash) &&
            s.Actual(s.Invocations.Last().ActionId).Type == CardActionType.Response && s.E<AnniversaryLongyuanAwakenedEvent>().Length == 0 &&
            s.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Yizan && a.MinCardCount == 2),
            "A genuine played Slash counts once; reaching three does not modify the still-active current Play phase early.");

        var before = s.V(0).HandCount; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Qingren?.AnniversaryQingren is { Count: 4, Stage: AnniversaryQingrenStage.Drawing });
        var q = s.Qingren!; s.Need(s.E<AnniversaryQingrenIssuedEvent>().Single().Count == 4, "Own Ending freezes all four actual Use/Played invocations.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<AnniversaryQingrenDrawReturnedEvent>().Single(e => e.FrameId == q.Id) is { Requested: 4, Actual: 4 } &&
            s.V(0).HandCount == before + 6 && s.V(0).Hp == 2 && s.V(0).MaxHp == 4 &&
            s.E<AnniversaryLongyuanAwakenedEvent>().Length == 1 && s.E<SkillAwakenedEvent>().Count(e => e.SkillId == Longyuan) == 1 &&
            s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Yizan || a.MinCardCount != 2) &&
            s.G.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Yizan).All(a => a.CardId is not { } id || !nonbasic.Contains(id)),
            "Cold Qingren draws four once; native Longyuan then draws two, recovers one without losing max HP, and permits only one basic material.");

        var currentTurn = s.G.State.TurnNumber;
        s.Driver("poke", [2]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == Yizan).All(c => c.Cards.Count == 1),
            "The real post-awakening defense offers no stale two-card conversion.");
        s.Answer(c => Conversion(c, "single-dodge") && c.Cards.SequenceEqual([singleDodge])); s.Play();
        s.Native(CardKind.Peach, singlePeach); s.Play();
        var wine = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id; s.Native(CardKind.Alcohol, wine); s.Play();
        var fire = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id; targetHp = s.V(3).Hp;
        s.Native(CardKind.FireSlash, fire, 3); s.Play();
        s.Need(s.V(0).Hp == 3 && s.V(3).Hp == targetHp - 2 && !s.V(0).HasAlcoholEffect &&
            s.Invocations.Where(e => e.ActualTurn == currentTurn).ToArray() is { Length: 4 } current && current.All(e => e.Modified && e.MaterialCount == 1) &&
            s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3 && e.Nature == DamageNature.Fire && e.Amount == 2),
            "Single-basic native Dodge, Peach, Alcohol and Fire Slash use the modified cost and keep ordinary recovery, defense and wine consumption.");
        s.End(); s.Play();
        s.Need(s.E<AnniversaryQingrenIssuedEvent>().Last().ActualTurn == currentTurn && s.E<AnniversaryQingrenIssuedEvent>().Last().Count == 4 &&
            s.E<AnniversaryQingrenDrawReturnedEvent>().Last() is { Requested: 4, Actual: 4 } && s.E<AnniversaryLongyuanAwakenedEvent>().Length == 1 &&
            s.Invocations.Length == 8 && s.Invocations.All(e => e.NativeUseFrameId is { } id ?
                s.E<CardUseDeclaredEvent>().Any(use => use.ResolutionId == id && use.SourceSeat == 0) : s.Actual(e.ActionId).PhysicalCards.Count == e.MaterialCount),
            "Qingren uses only the current actual turn's four invocations rather than the game's eight; Longyuan remains once per game.");
        s.Replay();
    }

    private static bool Conversion(PromptChoice c, string binding) => c.Parameters.GetValueOrDefault("conversion-skill-id") == Yizan && c.Parameters.GetValueOrDefault("conversion-binding-id") == binding;
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public AnniversaryYizanInvokedEvent[] Invocations => E<AnniversaryYizanInvokedEvent>().Where(e => e.Source.OwnerSeat == 0).ToArray();
        public CardActionContext Actual(long id) => E<CardActionAcceptedEvent>().First(e => e.Action.ActionId == id).Action;
        public ProgramSkillFrame? Qingren => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryQingren is not null);
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Driver(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, AnniversaryZhaoTongZhaoGuangChecks.Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Pair(CardKind kind, int[] ids, int[] targets)
        {
            var a = G.GetHumanLegalActions().Single(a => a.ProgramSkillId == Yizan && a.PlayedCardKind == kind && a.SelectableCardIds is { } cards && cards.Order().SequenceEqual(ids.Order()));
            Accept(new UseProgramSkillCommand(0, Yizan, a.ProgramActivationId!, ids, targets, G.Revision, P!.PromptId));
        }
        public void Native(CardKind kind, int card, int? target = null)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == card && a.PlayedCardKind == kind && a.ConversionSource?.SkillId == Yizan &&
                (target is null || a.TargetSeats.Contains(target.Value)));
            Accept(new PlayCardCommand(0, card, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "The expected genuine native choice must be published."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 500; i++)
            {
                if (ready()) return;
                if (P?.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Zhao Tong/Zhao Guang native boundary. " + Dump());
                Need(G.State.Winner == Winner.None, "The short fixed real-turn scenario must keep its participants alive.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Zhao Tong/Zhao Guang did not reach the bounded native flow.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            Moves = G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The original Qingren count and native draw child cold-restore exactly."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Real pair/single uses, played responses, awakening and Qingren replay identically."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Views = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }).ToArray(),
            Invocations, Qingren = Qingren?.AnniversaryQingren, Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        private void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected genuine Zhao Tong/Zhao Guang command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = [new(CardKind.Slash, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dismantlement, Suit.Spade, 4),
                new(CardKind.Peach, Suit.Heart, 5), new(CardKind.DrawTwo, Suit.Diamond, 6), new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dismantlement, Suit.Heart, 8),
                new(CardKind.Slash, Suit.Spade, 9), new(CardKind.Dodge, Suit.Diamond, 10), new(CardKind.DrawTwo, Suit.Spade, 11), new(CardKind.Dismantlement, Suit.Diamond, 12)];
            var setup = new ShanheBattleSetup(47, 1, "anniversary-zhao-tong-zhao-guang", Hero, 4, 0, 0, 0, 0, [AnniversaryZhaoTongZhaoGuangChecks.Driver, Watch], opening,
                [new(CardKind.Crossbow, Suit.Club, 1)], [new(Peer, ExtraSkillIds: [Counter]) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhao-tong-zhao-guang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"lose-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Counter}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],
                "triggers":[{"id":"counter","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Driver] = new { name = "实际场景驱动", description = "原生失去体力、决斗与伤害。" }, [Quiet] = new { name = "安静同桌", description = "保留实际回合并跳过出牌。" },
                [Counter] = new { name = "真实反击", description = "原伤害后的原生杀。" }, [Watch] = new { name = "原生摸牌暂停", description = "保留原生摸牌子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定基本牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "赵统赵广原生双牌与觉醒小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
