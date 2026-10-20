using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouTianYuMobileChecks
{
    private const string Zhenxi = "mou-mobile:zhenxi", Yangshi = "mou-mobile:yangshi", Driver = "fixture:mtmy-driver",
        Quiet = "fixture:mtmy-quiet", Owner = "fixture:mtmy-owner", Enemy = "fixture:mtmy-enemy", Far = "fixture:mtmy-far", Ally = "fixture:mtmy-ally", Deck = "fixture:mtmy-deck";
    private static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);

    public static void NativeBackwaterDiscardFieldMoveAndPermanentRangeOrSlash()
    {
        var s = Scene.Start(); s.Use("equip-peer", [1]); s.Play();
        var crossbow = s.V(1).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        var firstSlash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        s.NativeSlash(1); s.Reach(() => s.P?.SkillPrompt?.SkillId == Zhenxi);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.IsVerb("backwater"));
        s.Need(s.E<MouTianYuStartedEvent>() is [{ Distance: 1, Backwater: true, TargetSeat: 1 }], "The real designated target's native distance and highest HP qualify the complete Backwater branch.");
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "backwater");
        s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("verb") == "toggle").All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")),
            "Foreign Hand discards use native opaque slots, not hidden entity IDs.");
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "toggle");
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "confirm"); s.Reach(() => s.IsVerb("field"));
        var discarded = s.G.CardMovements.Single(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.mou-mobile:zhenxi.discard");
        s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ExecutorSeat == 0 && e.SubjectSeat == 1 && e.ActualCount == 1 && e.HandCount == 1 &&
            e.SequenceBefore < discarded.Sequence && e.SequenceAfter >= discarded.Sequence) == 1 && s.V(1).Equipment.Any(c => c.Id == crossbow),
            "Backwater first performs one real logical Hand discard while the field equipment is still in place.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([crossbow]) && c.Targets.SequenceEqual([0])); s.Play();
        var start = s.E<MouTianYuStartedEvent>().Single();
        s.Need(s.V().Equipment.Any(c => c.Id == crossbow) && s.G.CardMovements.Count(m => m.CardId == crossbow && m.From == new CardLocation(CardZoneKind.Equipment, 1) && m.To == new CardLocation(CardZoneKind.Equipment, 0)) == 1 &&
            s.E<MouTianYuCompletedEvent>().Count(e => e.FrameId == start.FrameId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == start.UseId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == firstSlash && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "The native field move follows the discard, returns its exact parent Slash, and never repays its material after cold restoration.");
        s.NativeSlash(1); s.Play();
        s.Need(s.E<MouTianYuStartedEvent>().Count(e => e.Source.SkillId == Zhenxi) == 1,
            "A second real Slash made possible by the moved Crossbow cannot refresh Zhenxi within the same actual turn.");
        s.Need(s.G.GetAttackRange(0) == 1, "Crossbow's native attack range starts at one before permanent Yangshi growth.");
        s.Use("damage-one", [0]); s.Play();
        s.Need(s.V().Hp == 3 && s.G.GetAttackRange(0) == 2 && s.E<MouTianYuRangeIncreasedEvent>() is [{ RangeBefore: 1 }],
            "A real damage event outside all-player range grants the permanent native range increment.");
        var before = s.V().HandCount; s.Use("damage-one", [0]); s.Play();
        var obtained = s.E<MouTianYuSlashObtainedEvent>().Single();
        s.Need(s.V().Hp == 2 && s.G.GetAttackRange(0) == 2 && s.V().HandCount == before + 1 && obtained.CardId is { } gained && s.V().Hand.Any(c => c.Id == gained && c.Kind == CardKind.Slash) &&
            s.G.CardMovements.Count(m => m.CardId == obtained.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.mou-mobile:yangshi.obtain") == 1 &&
            s.E<MouTianYuRangeIncreasedEvent>().Length == 1 && s.E<MouTianYuCompletedEvent>().Length == 3,
            "When the actual native range covers every other living character, the next damage obtains a real deck Slash once instead of increasing range again."); s.Cold();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-tian-yu-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-tian-yu-mobile." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual Tian Yu program."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Zhenxi, Yangshi }) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"real-equipment"}]},
                  {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":1}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实移动与伤害", description = "原生固定小局" }, [Quiet] = new { name = "静默", description = "跳过无关出牌" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "原生固定小局") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var kind in new[] { CardKind.Slash, CardKind.Indulgence, CardKind.Crossbow }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mtmy-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            b.AddGeneral(new(Owner, "移动谋田豫本人", "", "standard:none", "wei", 4, [Zhenxi, Yangshi, Driver]));
            foreach (var id in new[] { Enemy, Far, Ally }) b.AddGeneral(new(id, id, "", "standard:none", "qun", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mtmy-card-" + (i < 8 ? CardKind.Crossbow : CardKind.Slash), Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋田豫原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Enemy, Far, Ally], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine g, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = g;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool IsVerb(string verb) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-tian-yu" && c.Parameters.GetValueOrDefault("verb") == verb) == true;
        public void Use(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void NativeSlash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.First(predicate); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> condition)
        {
            for (var n = 0; n < 100; n++)
            {
                if (condition()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff.");
                if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected native prompt: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded Tian Yu boundary: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements), "The real pending move and paid discard survive cold restoration without repayment.");
        }
        public void Accept(GameCommand c) { var result = G.Submit(c); Need(result.Accepted, "Rejected native " + c.GetType().Name + ": " + result.Error?.Message); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, P, G.State.Winner, Hp = V().Hp, Range = G.GetAttackRange(0) })); }
        public static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(83, 1, "mtmy-main", Owner, 4, 0, 0, 0, 0, [], [C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Indulgence)], [],
                [new(Enemy) { StartingHand = [C(CardKind.Indulgence), C(CardKind.Indulgence)] }, new(Far) { StartingHand = [C(CardKind.Indulgence)] }], [new(Ally) { StartingHand = [C(CardKind.Indulgence)] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
