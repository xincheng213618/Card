using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryGuoZhaoChecks
{
    private const string Hero = "anniversary:guo-zhao", Pianchong = "anniversary:pianchong", Zunwei = "anniversary:zunwei",
        Driver = "fixture:guo-zhao-driver", Watch = "fixture:guo-zhao-watch", Quiet = "fixture:guo-zhao-quiet", Peer = "fixture:guo-zhao-peer", Deck = "fixture:guo-zhao-deck";
    public static void ReplacementColorLeasePermanentOptionsAndNativeEquipmentCold()
    {
        var s = Scene.Start(); s.Offer(Pianchong); var hand = s.V(0).HandCount; var draws = s.E<CardsDrawnEvent>().Length;
        var weapon = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow && c.Suit == Suit.Heart).Id;
        var armor = s.V(0).Hand.Single(c => c.Kind == CardKind.BaguaFormation && c.Suit == Suit.Diamond).Id;
        s.Activate(); s.Menu(AnniversaryGuoZhaoStage.ColorChoice); s.Cold();
        var initial = s.Root!.AnniversaryGuoZhao!;
        s.Need(s.V(0).HandCount == hand + 2 && initial.Gains.Count == 2 && initial.Gains.Select(g => g.IsRed).Distinct().Count() == 2 && s.E<CardsDrawnEvent>().Length == draws,
            "Replacement obtains two real opposite-color deck entities rather than fabricating normal draws.");
        s.Answer(c => Option(c) == "lose-red"); s.Play();
        var lease = s.E<AnniversaryGuoZhaoLeaseEvent>().Single();
        hand = s.V(0).HandCount; s.EquipPeer(weapon, 1);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Root?.AnniversaryGuoZhao is { Op: SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor, Stage: AnniversaryGuoZhaoStage.Drawing });
        var rewardFrame = s.Root!; var loss = s.G.CardMovements.Single(m => m.CardId == weapon && m.From == CardLocation.Hand(0));
        s.Need(s.Zone(weapon) == CardLocation.Equipment(1) && loss.AnniversaryGuoZhaoLoss is { IsRed: true } && rewardFrame.AnniversaryGuoZhao!.Benefits is [{ LeaseId: var paidLease, Count: 1, DrawRed: false }] && paidLease == lease.FrameId,
            "The real red hand departure freezes its original lease and enters a genuine one-card black draw request.");
        s.Cold(); s.Continue(); s.Play();
        var black = s.E<AnniversaryGuoZhaoDrawReturnedEvent>().Single(e => e.FrameId == rewardFrame.Id);
        var drawn = s.G.CardMovements.Single(m => m.Sequence > black.Before && m.Sequence <= black.After && m.Reason.Value == "skill-program.anniversary:pianchong.draw");
        s.Need(s.V(0).HandCount == hand && black.Actual == 1 && s.V(0).Hand.Single(c => c.Id == drawn.CardId).Suit is Suit.Spade or Suit.Club &&
            s.E<AnniversaryGuoZhaoBenefitIssuedEvent>().Count(e => e.FrameId == rewardFrame.Id) == 1,
            $"Cold return preserves the single already-issued reward and draws only a real black entity (before={hand}, after={s.V(0).HandCount}, card={drawn.CardId}, suit={s.V(0).Hand.Single(c => c.Id == drawn.CardId).Suit}, actual={black.Actual}, issued={s.E<AnniversaryGuoZhaoBenefitIssuedEvent>().Count(e => e.FrameId == rewardFrame.Id)}).");
        s.EquipPeer(armor, 1); s.Play();
        s.Need(s.V(1).Equipment.Count == 2, "The native fixture genuinely gives the other character a weapon and armor.");

        s.Zunwei(); s.Menu(AnniversaryGuoZhaoStage.OptionChoice); s.Answer(c => Option(c) == "equipment" && c.Targets.SequenceEqual([1]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Root?.AnniversaryGuoZhao is { Option: "equipment", Stage: AnniversaryGuoZhaoStage.EquipmentChildren });
        var equipmentFrame = s.Root!; var first = equipmentFrame.AnniversaryGuoZhao!.Equipment!;
        s.Need(s.Zone(first.CardId) == CardLocation.Equipment(0) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == first.UseId && e.CardId == first.CardId) == 1 &&
            s.E<AnniversaryGuoZhaoOptionPaidEvent>().Count(e => e.FrameId == equipmentFrame.Id) == 1,
            "A real equipment Use finishes before its movement observer and before the next random entity is chosen.");
        s.Cold(); s.Continue(); s.Play();
        var issued = s.E<AnniversaryGuoZhaoEquipmentIssuedEvent>().Where(e => e.FrameId == equipmentFrame.Id).ToArray();
        s.Need(s.V(0).Equipment.Count == 2 && issued.Length >= 2 && issued.Select(e => e.CardId).Distinct().Count() == issued.Length &&
            s.E<AnniversaryGuoZhaoEquipmentReturnedEvent>().Count(e => e.FrameId == equipmentFrame.Id) == issued.Length &&
            issued.All(e => s.E<CardUseFinishedEvent>().Count(done => done.ResolutionId == e.UseId && done.CardId == e.CardId) == 1) &&
            s.E<AnniversaryGuoZhaoOptionPaidEvent>().Count(e => e.Option == "equipment") == 1,
            "Each random physical equipment completes exactly once, including legal replacements, until the actual counts agree.");

        s.End(); s.Offer(Pianchong); s.Skip(); s.Play();
        var benefits = s.E<AnniversaryGuoZhaoBenefitIssuedEvent>().Length; hand = s.V(0).HandCount;
        var red = s.V(0).Hand.First(c => c.Suit is Suit.Heart or Suit.Diamond).Id;
        s.Use("discard-one", [red], []); s.Play();
        s.Need(s.Zone(red) == CardLocation.DiscardPile && s.V(0).HandCount == hand - 1 && s.E<AnniversaryGuoZhaoBenefitIssuedEvent>().Length == benefits,
            "The previous lease expires at the next actual owner turn start even when the next replacement is declined.");
        s.Use("stock-peer", [], [1]); s.Play();
        hand = s.V(0).HandCount;
        s.Need(s.V(1).HandCount - hand >= 5, $"A real native draw gives the chosen peer a large enough actual hand difference (owner={hand}, peer={s.V(1).HandCount}).");
        s.Zunwei(); s.Menu(AnniversaryGuoZhaoStage.OptionChoice);
        s.Need(s.P!.Choices.All(c => Option(c) != "equipment"), "The equipment option is permanently removed, not reset with the Play phase.");
        s.Answer(c => Option(c) == "hand" && c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.V(0).HandCount == hand + 5 && s.E<AnniversaryGuoZhaoOptionPaidEvent>().Single(e => e.Option == "hand").Amount == 5,
            "Zunwei draws at most five new cards rather than setting the total hand to five.");
        s.Use("lose", [], []); s.Play(); var wounded = s.V(0).Hp;
        s.End(); s.Offer(Pianchong); s.Activate(); s.Menu(AnniversaryGuoZhaoStage.ColorChoice); s.Answer(c => Option(c) == "lose-black"); s.Play();
        hand = s.V(0).HandCount; var blackCard = s.V(0).Hand.First(c => c.Suit is Suit.Spade or Suit.Club).Id;
        s.Use("discard-one", [blackCard], []); s.Play();
        var redDraw = s.E<AnniversaryGuoZhaoDrawReturnedEvent>().Last();
        var redMaterial = s.G.CardMovements.Single(m => m.Sequence > redDraw.Before && m.Sequence <= redDraw.After && m.Reason.Value == "skill-program.anniversary:pianchong.draw");
        s.Need(s.V(0).HandCount == hand && s.V(0).Hand.Single(c => c.Id == redMaterial.CardId).Suit is Suit.Heart or Suit.Diamond,
            "The other legal color branch independently turns a real black departure into one red native draw.");
        s.Zunwei(); s.Menu(AnniversaryGuoZhaoStage.OptionChoice);
        s.Need(s.P!.Choices.All(c => Option(c) == "hp"), "Only the third permanent option remains after two real activations.");
        s.Answer(c => Option(c) == "hp" && c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.V(0).Hp == wounded + 1 && s.V(0).Hp == s.V(0).MaxHp && s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) &&
            s.E<AnniversaryGuoZhaoOptionPaidEvent>().Select(e => e.Option).Order().SequenceEqual(new[] { "equipment", "hand", "hp" }.Order()) &&
            s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Zunwei),
            "Native recovery respects the owner's maximum; all three distinct options stay removed for the rest of the game.");
        s.Replay();
    }
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryGuoZhao is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Offer(string id) => Reach(() => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Menu(AnniversaryGuoZhaoStage stage) => Reach(() => Root?.AnniversaryGuoZhao?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-guo-zhao") == true);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string id, int[] cards, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, cards, targets, G.Revision, P!.PromptId));
        public void EquipPeer(int card, int target) => Use("equip-peer", [card], [target]);
        public void Zunwei() => Accept(new UseProgramSkillCommand(0, AnniversaryGuoZhaoChecks.Zunwei, "three-permanent-options", [], [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing the exact original Guo Zhao choice.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 500; step++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Guo Zhao boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short fixed native scenario must retain its participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Guo Zhao did not reach the bounded native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == state, "The original color/issued draw/paid equipment cursor cold-restores exactly."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == state, "The complete color leases, native equipment Uses and permanent choices replay identically."); }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected exact Guo Zhao command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.AnniversaryGuoZhao,
            Paid = E<AnniversaryGuoZhaoOptionPaidEvent>(), Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var opening = new[] { new ShanheStartingCard(CardKind.Crossbow, Suit.Heart, 1), new ShanheStartingCard(CardKind.BaguaFormation, Suit.Diamond, 2),
                new ShanheStartingCard(CardKind.Dodge, Suit.Club, 3), new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 4), new ShanheStartingCard(CardKind.Peach, Suit.Heart, 5), new ShanheStartingCard(CardKind.Slash, Suit.Spade, 6) };
            var setup = new ShanheBattleSetup(47, 1, "anniversary-guo-zhao", Hero, 3, 0, 0, 0, 0, [Driver, Watch], opening, [],
                [new(Peer, Hp: 8) { StartingHand = [] }, new(Peer, Hp: 8) { StartingHand = [] }], [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7)] }], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-guo-zhao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load(Rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实郭照驱动", description = "实际移动、摸牌及失去体力。" }, [Quiet] = new { name = "安静同桌", description = "跳过出牌。" },
                [Watch] = new { name = "真实子窗", description = "在原生请求或装备入场之后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "真实有色实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 144).Select(i => (i % 4) switch
            { 0 => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 1), 1 => new("standard:bagua", Suit.Club, 2), 2 => new("standard:slash", Suit.Heart, 3), _ => new("standard:dodge", Suit.Diamond, 4) }).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "郭照原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
            {"id":"equip-peer","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"real-equipment"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"real-equipment"}]},
            {"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
            {"id":"stock-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":20}]},
            {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}
          ]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Watch}}","revision":1,"triggers":[
            {"id":"native-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]},
            {"id":"equipment-child","window":"cardsGained","subject":"owner","destinationZones":["equipment"],"movementOccurrence":"perCard","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment-child","options":[{"id":"continue"}]}]}
          ]}
        ]}
        """;
    }
}
