using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryCheZhouChecks
{
    private const string Hero = "anniversary:che-zhou", Pigua = "anniversary:pigua", Watch = "fixture:che-zhou-paid-watch",
        Quiet = "fixture:che-zhou-quiet", Plain = "fixture:che-zhou-plain", SourceHero = "fixture:che-zhou-source", Deck = "fixture:che-zhou-deck";

    public static void RealHandAgeWineAndPiguaNativeObtainCold()
    {
        var s = Scene.Start(false); s.Play();
        var oldSlash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        var wine = s.V(0).Hand.Single(c => c.Kind == CardKind.Alcohol).Id;
        s.Card(s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        s.Need(s.V(0).HandCount == 4, "The first real turn equips Crossbow and retains its original Slash and Wine without exceeding the native four-card limit.");
        s.End(); s.Play();
        s.Need(s.E<RoundStartedEvent>().Last().RoundNumber == 2 && s.V(0).HandCount == 6,
            "A real second round, including all other actual turns, retains the first-round card and adds two newly drawn Hand entities.");
        var fresh = s.G.CardMovements.Where(m => m.TurnNumber == s.G.State.TurnNumber && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw).Select(m => m.CardId).First();
        s.Card(wine); s.Play();
        var freshHp = s.V(1).Hp; s.Card(fresh, [1]); s.Play();
        var freshUse = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == fresh);
        s.Need(s.E<AnniversaryCheZhouUseAgeCapturedEvent>().Single(e => e.UseId == freshUse.ResolutionId).Age == 1 && s.V(1).Hp == freshHp - 1 &&
            s.E<AnniversaryCheZhouDamageReplacedEvent>().Single(e => e.AttackId == freshUse.ResolutionId) is { Previous: 2, Replacement: 1 } &&
            !s.V(0).HasAlcoholEffect && s.E<AnniversaryCheZhouPiguaStartedEvent>().Length == 0,
            "A newly drawn age-one Slash really consumes native Wine and replaces its two damage with one; it creates no greater-than-one Pigua opportunity.");
        var hp = s.V(3).Hp; s.Card(oldSlash, [3]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Pigua && s.P.Choices.Any(c => Key(c) == "activate"));
        var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == oldSlash);
        s.Need(native.AnniversaryCheZhouAge is { Age: 2, Round: 2, Materials: 1 } && s.V(3).Hp == hp - 2 &&
            s.E<AnniversaryCheZhouDamageReplacedEvent>().Single(e => e.AttackId == native.Id) is { Previous: 1, Replacement: 2 } && !s.V(0).HasAlcoholEffect,
            "The original retained Slash declared while still in Hand freezes age two and replaces its ordinary one damage with two after the preceding real Wine was consumed.");
        s.Answer(c => Key(c) == "activate"); s.Menu();
        s.Need(s.P!.Choices.All(c => c.Cards.Count == 0) && s.P.ValidCardIds.Count == 0,
            "Pigua's foreign-hand menu publishes opaque slots without hidden card identities or faces.");
        s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == "0"); s.Menu();
        s.Need(s.Root!.AnniversaryCheZhou is { Selected.Count: 1, Round: 2, Stage: AnniversaryCheZhouStage.Selecting },
            "Selecting one opaque slot retains an unpaid original frame and still permits the second card or an early finish.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "card");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var root = s.Root!; var receipt = root.AnniversaryCheZhou!; var obtained = receipt.Moves.Select(m => m.CardId).ToArray(); var id = root.Id;
        s.Need(receipt is { Selected.Count: 2, Moves.Count: 2, Stage: AnniversaryCheZhouStage.MovementChildren } &&
            obtained.All(card => s.Zone(card) == CardLocation.Hand(0)) && s.E<AnniversaryCheZhouPiguaPaidEvent>().Count(e => e.FrameId == id && e.Count == 2) == 1 &&
            s.V(0).HandCount == 5, "Two chosen native Hand entities are really obtained as one paid batch before their gain observer pauses the original damage continuation.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.E<AnniversaryCheZhouPiguaCompletedEvent>().Count(e => e.FrameId == id) == 1 &&
            obtained.All(card => s.G.CardMovements.Count(m => m.CardId == card && m.Reason.Value == "skill-program.anniversary:pigua.obtain") == 1),
            "Restoring the paid gain child returns to the same Pigua frame and completes once without paying either entity twice.");
        s.Need(s.E<AnniversaryCheZhouPiguaStartedEvent>().Length == 1 && s.V(0).HandCount == 5,
            "Only the retained age-two Slash offers Pigua; the preceding Wine-funded age-one Slash did not add a duplicate opportunity.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V(0).HandCount == 7 && obtained.All(card => s.Zone(card) == CardLocation.Hand(0)) &&
            !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard),
            "The actual discard phase retains five cards because its two truly obtained Pigua entities are exempt in that original turn; the next real Draw adds two.");
        s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
        s.Need(s.P!.RequiredCardCount == 3 && obtained.All(card => s.P.ValidCardIds.Contains(card)),
            "In the following actual turn the same entities are ordinary Hand cards again and are legal members of the required native three-card discard.");
        var discards = obtained.Concat(s.P.ValidCardIds.Except(obtained).Take(1)).ToArray();
        s.Accept(new DiscardCardsCommand(0, discards, s.P.PromptId, s.G.Revision));
        s.Need(obtained.All(card => s.Zone(card) == CardLocation.DiscardPile) && s.V(0).HandCount == 4,
            "Expired Pigua entities really pay the next native hand-limit discard instead of remaining indefinitely exempt.");
        s.Replay();
    }

    public static void IncomingLaterChainRecipientUsesOriginalHandAge()
    {
        var s = Scene.Start(true); s.Play();
        var fire = s.V(0).Hand.Single(c => c.Kind == CardKind.FireSlash).Id;
        s.Card(s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        s.Card(s.V(0).Hand.Single(c => c.Kind == CardKind.IronChain).Id, [1, 3]); s.Play();
        s.Need(s.V(1).IsChained && s.V(3).IsChained, "The first actual turn uses a real IronChain to link the ordinary first recipient and later Shefu holder.");
        s.End(); s.Play(); var firstHp = s.V(1).Hp; var cheHp = s.V(3).Hp;
        s.Card(fire, [1]); s.Play();
        var declared = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == fire);
        var requested = s.E<DamageRequestedEvent>().Where(e => e.SourceSeat == 0 && e.Nature == DamageNature.Fire).ToArray();
        s.Need(s.E<RoundStartedEvent>().Last().RoundNumber == 2 && s.E<AnniversaryCheZhouUseAgeCapturedEvent>().Single(e => e.UseId == declared.ResolutionId).Age == 2 &&
            s.V(1).Hp == firstHp - 1 && s.V(3).Hp == cheHp - 2 && requested.Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 1), (3, 2)]) &&
            s.E<AnniversaryCheZhouDamageReplacedEvent>().Where(e => e.AttackId == declared.ResolutionId).ToArray() is [{ Target: 3, Previous: 1, Replacement: 2 }] &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId) == 1,
            "A source without Shefu deals the original one to the first chained recipient; a later incoming Shefu recipient independently replaces it with the same original real Use's frozen age two, and that one native Use finishes once.");
        s.Replay();
    }

    private static string Peer(int n) => "fixture:che-zhou-peer-" + n;
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine engine, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = engine;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(n => G.CreateSnapshot(n).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryCheZhou is not null);
        public PlayerSnapshot V(int n) => G.CreateSnapshot(n).Players.Single(p => p.Seat == n);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int card) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Menu() => Reach(() => P?.Choices.Any(c => Key(c) == "anniversary-che-zhou") == true);
        public void Card(int card, int[]? targets = null)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == card && (targets is null || a.TargetSeats.SequenceEqual(targets)));
            Need(action is not null, "The deterministic fixture supplies the exact native card use and target set.");
            Accept(new PlayCardCommand(0, card, action!.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var selected = p.Choices.FirstOrDefault(predicate); Need(selected is not null, "Missing original native Che Zhou choice.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, selected!.Id, G.Revision));
        }
        public void Accept(GameCommand c)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());
            Need(result.Accepted, result.Error?.Message ?? "Rejected native Che Zhou command.");
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 320; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected native Che Zhou boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short deterministic Che Zhou scene keeps its native participants alive.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Che Zhou missed its bounded actual continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(n => SnapshotJson.Serialize(G.CreateSnapshot(n))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The original opaque selection or already-paid native gain child cold-restores exactly."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The actual round, native card ages, damage replacement, paid obtain and actual-turn expiry replay exactly."); }
        public void Need(bool value, string text) { if (!value) throw new InvalidOperationException(text + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, Prompt = P, Root = Root?.AnniversaryCheZhou,
            Views = Enumerable.Range(0, 4).Select(n => new { Seat = n, V(n).Hp, V(n).HandCount, V(n).HandLimit }),
            Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(bool incoming)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(incoming));
            var setup = new ShanheBattleSetup(31, 1, "anniversary-che-zhou", incoming ? SourceHero : Hero, 4, 0, 0, 0, 0, [Watch],
                incoming ? [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.FireSlash, Suit.Spade, 7), new(CardKind.IronChain, Suit.Club, 12)] :
                    [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Alcohol, Suit.Diamond, 9)], [],
                [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3)).ToArray() }],
                [new(Peer(1)) { StartingHand = [] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool incoming) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-che-zhou", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:pigua.obtain"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Quiet] = new { name = "真实安静同桌", description = "保留真实摸牌弃牌阶段，仅跳过主动出牌。" },
                [Watch] = new { name = "披挂真实付款子窗", description = "在真正获得手牌后暂停原生子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Plain, "普通真实牌使用者", "没有慑伏的固定原生使用者。") { ExecutionForms = SkillExecutionForm.State });
            b.AddGeneral(new(SourceHero, "普通出牌者", "", Plain, "wei", 4));
            for (var i = 1; i < 4; i++) b.AddGeneral(new(Peer(i), "固定安静同桌", "", Quiet, "wei", 12)
                { AdditionalSkillIds = incoming && i == 3 ? ["anniversary:shefu"] : [] });
            b.AddDeck(new(Deck, "固定真实杀牌库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 8)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "车胄真实轮数与原生披挂", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, SourceHero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
