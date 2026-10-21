using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class ShenDengAiChecks
{
    private const string Hero = "anniversary:shen-deng-ai", Driver = "fixture:shen-da-driver", Quiet = "fixture:shen-da-quiet",
        Ally = "fixture:shen-da-ally", Enemy = "fixture:shen-da-enemy", Deck = "fixture:shen-da-deck";

    public static void DevelopAllocateAwakenAndUseIssuedDomains()
    {
        var s = Awaken();
        s.Need(s.G.CreateSnapshot(0).TableOrder?.SequenceEqual([1, 2, 0, 3]) == true && s.V().MaxHp == 3 && s.G.State.CurrentSeat == 0 &&
            s.E<ProgramExtraTurnPendedEvent>().Length == 1 && s.V().Skills!.Any(k => k.ContentId == "anniversary-god:cuixin"),
            "Qijing moves the actual general between the selected living neighbors, reduces max HP, grants Cuixin and begins exactly one real extra turn.");
        var qingqu = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.SkillMarkLabel == "清渠");
        var fengtian = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.SkillMarkLabel == "丰田");
        var junshan = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.SkillMarkLabel == "峻山");
        var hp = s.V(1).Hp; s.PlayCard(qingqu.Id, 1); s.Play(skipCuixin: true);
        s.Need(s.V(1).Hp == hp - 1 && s.E<ShenDengAiDomainIssuedEvent>().Any(e => (e.DomainMask & 2) != 0),
            "Qingqu reaches an actual distance-two target with a genuine native Slash and signs its own quota exemption.");
        hp = s.V(2).Hp; s.PlayCard(fengtian.Id, 2); s.Play(skipCuixin: true);
        s.Need(s.V(2).Hp == hp - 2 && !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == junshan.Id),
            "Fengtian raises actual Slash damage to two; Qingqu did not debit the ordinary quota, while Junshan does not inherit unlimited usage.");
        s.Accept(new UseProgramSkillCommand(0, Driver, "hurt", [], [], s.G.Revision, s.P!.PromptId)); s.Play();
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach && c.SkillMarkLabel == "丰田");
        s.PlayCard(peach.Id, 0); s.Play();
        s.Need(s.V().Hp == 3 && s.E<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 2),
            "Fengtian also changes an actual recovery card's requested value to two through the native recovery pipeline.");
        s.Cold();
    }

    public static void CuixinRestoresCompletedUseAfterNativeChildAndDelayedCarrier()
    {
        var s = Awaken(); var card = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.SkillMarkLabel == "峻山");
        var hp2 = s.V(2).Hp; var hp3 = s.V(3).Hp;
        s.PlayCard(card.Id, 2); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:cuixin");
        s.Need(s.V(2).Hp == hp2 - 1 && s.E<IssuedCardNoResponseEvent>().Any() && s.P!.Choices.Any(c => c.Targets.SequenceEqual([3])),
            "Junshan makes its real hand Slash unrespondable, and that completed use offers the opposite living neighbor.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "slash:3"); s.Cold(); s.Play(skipCuixin: true);
        var issued = s.E<ShenDengAiNativeIssuedEvent>().Single();
        s.Need(s.V(3).Hp == hp3 - 1 && s.E<ShenDengAiNativeReturnedEvent>() is [{ UseId: var useId }] && useId == issued.Origin.UseId &&
            s.E<ShenDengAiCursorRestoredEvent>().Length == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.Origin.OriginalUseId) == 1 &&
            s.E<ShenDengAiDomainIssuedEvent>().All(e => e.UseId != issued.Origin.UseId),
            "The native zero-material Slash returns once and restores its original completed mother without copying the physical card's domain.");
        var delayed = s.V().Hand.First(c => c.Kind == CardKind.Indulgence);
        s.PlayCard(delayed.Id, 2); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:cuixin"); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "delayed:3"); s.Play(skipCuixin: true);
        var carrier = s.E<ShenDengAiCarrierCreatedEvent>().Single();
        s.Need(s.V(3).Judgment.Any(c => c.Id == carrier.CardId && c.Kind == CardKind.Indulgence) &&
            s.G.CardMovements.Any(m => m.CardId == carrier.CardId && m.From == CardLocation.OutsideGame && m.To == CardLocation.Processing) &&
            s.E<ShenDengAiNativeReturnedEvent>().Length == 2,
            "A repeated delayed trick places one genuine generated judgment carrier and returns through the original native flow.");
        s.Cold();
    }

    internal static (GameEngine Game, ContentRegistry Registry) CreateAwakenedTableFixture()
    { var s = Awaken(); return (s.G, s.Registry); }

    private static Scene Awaken()
    {
        var s = Scene.Start(); s.Play();
        for (var domain = 1; domain <= 3; domain++)
        {
            s.Tap(2); s.Play(); s.Tap(2); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:xianjin");
            if (domain == 1) s.Cold();
            s.Answer(c => c.Parameters.GetValueOrDefault("option") == "develop:" + domain); s.Play();
        }
        s.Need(s.E<ShenDengAiDevelopedEvent>().Select(e => e.Domain).SequenceEqual(Enum.GetValues<ShenDengAiDomain>()) &&
            s.E<ShenDengAiXianjinReservedEvent>().Select(e => e.DamageCount).SequenceEqual([2, 4, 6]),
            "Six true damage occurrences develop the selected three regions at exactly two, four and six.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:tuoyu"); s.Cold();
        var slashes = s.V().Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        while (s.P?.SkillPrompt?.SkillId == "anniversary-god:tuoyu")
        {
            var id = s.P.ValidCardIds.Single(); var index = Array.IndexOf(slashes, id);
            var domain = index is >= 0 and <= 2 ? index + 1 : s.V().Hand.Single(c => c.Id == id).Kind == CardKind.Peach ? 2 : 3;
            s.Answer(c => c.Parameters.GetValueOrDefault("option") == "assign:" + domain);
            if (s.P is null) s.Reach(() => s.P?.SkillPrompt?.SkillId is "anniversary-god:tuoyu" or "anniversary-god:qijing");
        }
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:qijing"); s.Cold();
        s.Answer(c => c.Targets.SequenceEqual([2, 3])); s.Play(); return s;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-deng-ai", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var json = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-samples","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                    {"id":"tap","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":1}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]}]},
                  {"id":"fixture:shen-da-no-draw","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实伤害驱动", description = "保留规则手牌" },
                ["fixture:shen-da-no-draw"] = new { name = "固定牌例", description = "取消正常摸牌" }, [Quiet] = new { name = "安静同行", description = "跳过无关出牌" } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Ally, "实际盟友", "", Quiet, "wu", 12)); b.AddGeneral(new(Enemy, "实际对手", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神邓艾原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Tap(int target) => Accept(new UseProgramSkillCommand(0, Driver, "tap", [], [target], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play(bool skipCuixin = false) => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }, skipCuixin);
        public void PlayCard(int id, int target)
        { var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.TargetSeats.SequenceEqual([target])); Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.First(predicate); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, G.Revision)); }
        public void Reach(Func<bool> done, bool skipCuixin = false)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (skipCuixin && p.SkillPrompt?.SkillId == "anniversary-god:cuixin") Answer(c => c.Parameters.GetValueOrDefault("option") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected Shen Deng Ai boundary " + JsonSerializer.Serialize(p));
            }
            throw new InvalidOperationException("Bounded Shen Deng Ai occurrence not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "Cold restore preserves the original native receipt and private chooser.");
        }
        public void Accept(GameCommand c)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason)
        { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, G.State.Phase, P, Facts = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(31, 1, "shen-da-native", Hero, 4, 0, 0, 0, 0, [Driver, "fixture:shen-da-no-draw"],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Indulgence, Suit.Heart, 6), new(CardKind.Peach, Suit.Heart, 3)], [],
                [new(Enemy) { StartingHand = [] }, new(Enemy) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('d', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
