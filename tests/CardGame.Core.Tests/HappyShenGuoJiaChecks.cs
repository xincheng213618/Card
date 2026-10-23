using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenGuoJiaChecks
{
    private const string Hero = "happy:shen-guo-jia", Huishi = "happy:huishi", Gift = "happy:huishi-gift", Tianyi = "happy:tianyi", Zuoxing = "happy:zuoxing",
        Driver = "fixture:happy-guojia-driver", Quiet = "fixture:happy-guojia-quiet", Watch = "fixture:happy-guojia-watch", Peer = "fixture:happy-guojia-peer", Deck = "fixture:happy-guojia-deck";
    public static void FiveSuitRevealStrictGiftAndTurnNamedNativeUsesCold()
    {
        var s = Scene.Start(); s.Play();
        s.Use(Driver, "drop"); s.Play();
        s.Need(s.V(0).MaxHp == 2 && !s.Actions.Any(a => a.ProgramSkillId == Gift),
            "At maximum two, no nonawakening target qualifies for Happy Hui Shi; own unawakened Tianyi also fails the four-living threshold.");
        s.Use(Driver, "grow"); s.Play(); s.Use(Gift, "grant-awakening"); s.Menu(Gift, "target");
        s.Need(s.P!.Choices.All(c => !c.Targets.Contains(0)) && s.P.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([1, 2, 3]),
            "The real Happy Hui Shi menu excludes an unawakened target below the living-count threshold, instead of offering mobile's fallback draw.");
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.V(1).HandCount == 4 && s.V(0).MaxHp == 1 && s.E<ShenGuoJiaMaximumPaidEvent>().Count(e => e.Purpose == "happy-huishi-gift") == 1,
            "A genuine no-awakening target draws four real cards, then the original limited activation pays two maximum HP once.");
        for (var i = 0; i < 2; i++) { s.Use(Driver, "grow"); s.Play(); }
        var firstMax = s.V(0).MaxHp;
        s.Use(Huishi, "five-suit-reveal"); s.Menu(Huishi, "recipient");
        var first = s.Receipt(Huishi); var shown = s.E<ProgramCardsRevealedEvent>().Single(e => e.FrameId == first.Id && e.Bind == "huishi-five");
        var suits = shown.Cards.Select(c => c.Suit).Where(x => x != Suit.None).Distinct().Count();
        s.Need(shown.Cards.Count == 5 && first.HappyGuoJia!.Cards.SequenceEqual(shown.Cards.Select(c => c.Id)) &&
            s.V(0).MaxHp == firstMax + suits && !s.E<JudgmentResolvedEvent>().Any(),
            "Happy Huishi publicly reveals all five actual deck entities and grows by distinct suits, without native judgment or stopping on a repeated suit.");
        s.Answer(c => c.Targets.SequenceEqual([0])); s.Play();
        s.Need(s.V(0).HandCount == 5 && s.V(0).MaxHp == firstMax + suits - 1 &&
            shown.Cards.All(c => s.G.CardMovements.Count(m => m.CardId == c.Id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.happy:huishi.give") == 1),
            "The complete revealed packet is genuinely given once; becoming the largest hand pays exactly one maximum HP.");

        s.Use(Driver, "recover"); s.Play();
        for (var seat = 0; seat < 4; seat++) { s.Use(Driver, "hurt", [seat]); s.Play(); }
        s.EndPlay();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Tianyi && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        s.Answer(c => c.Targets.SequenceEqual([0])); s.Play();
        s.Need(s.E<SkillAwakenedEvent>().Count(e => e.PlayerSeat == 0 && e.SkillId == Tianyi) == 1 && s.V(0).Skills!.Any(x => x.ContentId == Zuoxing),
            "Four genuine damaged facts awaken Tianyi on the next preparation and grant the real Happy Zuoxing instance.");
        while (s.V(0).MaxHp < 9) { s.Use(Driver, "grow"); s.Play(); }
        s.Use(Huishi, "five-suit-reveal"); s.Menu(Huishi, "recipient");
        var second = s.Receipt(Huishi); var secondId = second.Id;
        s.Need(s.V(0).MaxHp == 10 && second.HappyGuoJia is { MaximumBefore: 9, MaximumAfter: 10, Cards.Count: 5 },
            "The next real Huishi caps its suit growth at ten before asking for the full revealed packet's recipient.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([0])); s.Play();
        s.Need(s.V(0).MaxHp == 9 && s.E<ShenGuoJiaMaximumPaidEvent>().Count(e => e.FrameId == secondId && e.Purpose == "happy-huishi-grow") == 1,
            "Cold restoration preserves the already paid capped growth and never reveals or raises the maximum twice.");
        var beforeCancel = s.V(0).MaxHp;
        s.Use(Zuoxing, "borrow-ordinary-trick"); s.Menu(Zuoxing, "use"); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel"); s.Play();
        s.Need(s.V(0).MaxHp == beforeCancel && !s.E<ShenGuoJiaNativeIssuedEvent>().Any(),
            "Canceling the unpaid unlimited menu spends neither maximum HP nor any card-name allowance.");

        var hand = s.V(0).HandCount;
        s.Use(Zuoxing, "borrow-ordinary-trick"); s.Menu(Zuoxing, "use"); s.Card(CardKind.DrawTwo); s.Hold();
        var drawUse = s.Receipt(Zuoxing); var drawFrameId = drawUse.Id;
        s.Need(s.V(0).MaxHp == beforeCancel - 1 && drawUse.HappyGuoJia is { Stage: HappyGuoJiaStage.NativeUse, Plan.Kind: CardKind.DrawTwo } &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.VirtualOrdinaryTrickOrigin?.ParentProgramFrameId == drawFrameId && f.Action is { PhysicalCards.Count: 0, ConversionChain.Count: 0 }),
            "The successful no-material Draw Two owns a real native use and a once-paid benefactor cost on its original frame.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.V(0).HandCount == hand + 2 && s.V(0).MaxHp == beforeCancel - 1 &&
            s.E<ShenGuoJiaNativeIssuedEvent>().Count(e => e.FrameId == drawFrameId) == 1,
            "Cold returns the actual native Draw Two once, without a second cost or draw.");
        s.Use(Zuoxing, "borrow-ordinary-trick"); s.Menu(Zuoxing, "use");
        s.Need(!s.HasCard(CardKind.DrawTwo) && s.HasCard(CardKind.Duel),
            "Using one kind removes all its target plans for this actual turn while a different ordinary trick remains available.");
        var targetHp = s.V(2).Hp;
        s.Card(CardKind.Duel, 2); s.Hold(); s.Skip(); s.Play();
        s.Need(s.V(2).Hp == targetHp - 1 && s.V(0).MaxHp == beforeCancel - 2 &&
            s.E<ShenGuoJiaNativeReturnedEvent>().Any(e => e.HadAttack) && s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.HappyGuoJia is null),
            "A different paid kind follows the real Duel response failure and returns its original native attack tail without repeating the cost.");
        s.EndPlay(); s.Play();
        s.Use(Zuoxing, "borrow-ordinary-trick"); s.Menu(Zuoxing, "use");
        s.Need(s.HasCard(CardKind.DrawTwo) && s.HasCard(CardKind.Duel), "The card-name budget resets on the next actual turn.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel"); s.Play(); s.Replay();
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-shen-guo-jia", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":1}]},
              {"id":"drop","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1}]},
              {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
              {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-use","isSkillInvocation":false,"window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["drawTwo","duel"],"optional":true,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = Driver, description = "真实上限、回复和伤害驱动" },
                [Quiet] = new { name = Quiet, description = "保留真实回合，跳过他人出牌阶段" },
                [Watch] = new { name = Watch, description = "保留佐幸的真实原生用牌母帧", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静角色", "", Quiet, "wei", 6));
            b.AddDeck(new(Deck, "固定真实展示牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐郭嘉本体小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public IReadOnlyList<LegalAction> Actions => G.GetHumanLegalActions();
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame Receipt(string skill) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == skill && f.HappyGuoJia is not null);
        public void Use(string skill, string id, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, skill, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Menu(string skill, string branch) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch));
        public bool HasCard(CardKind kind) => P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Description.Contains(CardCatalog.Get(kind).DisplayName, StringComparison.Ordinal));
        public void Card(CardKind kind, int? target = null) => Answer(c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Description.Contains(CardCatalog.Get(kind).DisplayName, StringComparison.Ordinal) && (target is null || c.Targets.SequenceEqual([target.Value])));
        public void Hold() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 240; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected Happy Guo Jia boundary " + JsonSerializer.Serialize(p));
                Need(G.State.Winner == Winner.None, "The short real-turn Happy fixture retains its next actual boundary.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Guo Jia missed its bounded native occurrence " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold preserves the exact real reveal/cost, published owner choice and native child return."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Replay preserves actual five-card packets, maximum payments, granted instances and successful turn-name use facts."); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Happy Guo Jia command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "happy-guo-jia", Hero, 3, 0, 0, 0, 0, [Driver, Watch], [], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('9', 64)) { DefaultGodFactionId = "wei" };
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
