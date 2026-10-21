using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryShenZhangJiaoChecks
{
    private const string Hero = "anniversary:shen-zhang-jiao", Ally = "fixture:anniversary-zj-ally", Enemy = "fixture:anniversary-zj-enemy",
        Driver = "fixture:anniversary-zj-driver", Quiet = "fixture:anniversary-zj-quiet", NoDraw = "fixture:anniversary-zj-no-draw",
        Keep = "fixture:anniversary-zj-keep", GainHold = "fixture:anniversary-zj-gain", Deck = "fixture:anniversary-zj-deck";
    public static void OwnedDiscardRandomThirtySixShuffleAndThunderCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V(0).MaxHp == 4, "The current formal anniversary version has four HP.");
        var slash = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]));
        s.Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, s.G.Revision, s.P!.PromptId, slash.PlayedCardKind)); s.Play();
        var used = s.E<AnniversaryYizhaoAccumulatedEvent>().Single();
        s.Need(used.Rank == 13 && used.Before == 0 && used.After == 13 && used.ObtainedCount == 1,
            "A real rank-thirteen use crosses a tens digit and randomly obtains a real rank-one deck entity.");
        var discarded = s.V(0).Hand.ToArray(); s.Use("discard"); s.Play();
        var facts = s.E<AnniversaryYizhaoAccumulatedEvent>();
        s.Need(facts.Length == 1 + discarded.Length && facts.Skip(1).Sum(e => e.Rank) == discarded.Sum(c => c.Rank) &&
            s.Mark() == 13 + discarded.Sum(c => c.Rank) && discarded.All(c => s.G.CardMovements.Any(m => m.CardId == c.Id && m.To == CardLocation.DiscardPile)),
            "The added anniversary branch counts every original owned logical discard once, without counting the used Slash's cleanup again.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:sijun");
        var marker = s.Mark(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.AnniversaryZhangJiao?.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun));
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryZhangJiao?.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun);
        var paid = parent.AnniversaryZhangJiao!; var selected = paid.Cards.ToArray();
        s.Need(paid.Stage == AnniversaryZhangJiaoStage.Obtained && paid.MarkerBefore == marker && s.Mark() == 0 && paid.Rank == 36 &&
            selected.Sum(id => s.V(0).Hand.Single(c => c.Id == id).Rank) == 36 &&
            s.E<AnniversaryZhangJiaoShuffledEvent>().Length == 0 && s.E<AnniversarySijunObtainedEvent>().Single().RemovedHuang == marker,
            "Sijun pays Huang and randomly obtains an exact physical sum of 36 before a real gain child; the later shuffle has not happened.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.E<AnniversarySijunObtainedEvent>().Length == 1 && s.E<AnniversaryZhangJiaoShuffledEvent>().Length == 1 &&
            selected.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.Sequence > paid.Before && m.Sequence <= paid.After &&
                m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.anniversary-zhang-jiao.obtain") == 1),
            "Cold movement return never repeats payment or random acquisition and performs the actual shuffle once.");
        var hp2 = s.V(2).Hp; var hp3 = s.V(3).Hp;
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary-god:tianjie");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-tianjie") == true);
        s.Answer(c => c.Targets.SequenceEqual([3])); s.Answer(c => c.Targets.SequenceEqual([2]));
        var t = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryZhangJiao?.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie);
        s.Need(t.AnniversaryZhangJiao!.Targets.SequenceEqual([3, 2]) && t.AnniversaryZhangJiao.Cursor == 0, "Chosen targets are frozen on their original ending-turn frame before damage.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "finish");
        s.Reach(() => s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Thunder) == 2);
        var thunder = s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0 && e.Nature == DamageNature.Thunder).ToArray();
        s.Need(thunder.Select(e => e.TargetSeat).SequenceEqual([2, 3]) && thunder.Select(e => e.Amount).SequenceEqual([2, 1]) && s.V(2).Hp == hp2 - 2 && s.V(3).Hp == hp3 - 1,
            "Tianjie settles in actual table order with two real Dodges versus the minimum of one, through ordinary damage returns.");
    }
    public static void SanshouTracksWholeTurnRealUseTypesAndCleansReveals()
    {
        var s = Scene.Start(sanshou: true); s.Play(); var hp = s.V(1).Hp;
        s.Use("damage", [1]); s.Play();
        var first = s.E<AnniversarySanshouResolvedEvent>().Single();
        s.Need(first.Prevented && s.V(1).Hp == hp && s.E<ProgramCardsRevealedEvent>().Single(e => e.SkillId == "anniversary-god:sanshou").Cards.Count == 3,
            "An equipment category not used by anyone this actual turn prevents real incoming skill damage after publicly revealing three actual cards.");
        var crossbow = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        var equip = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == crossbow);
        s.Accept(new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, s.G.Revision, s.P!.PromptId, equip.PlayedCardKind)); s.Play();
        s.Use("damage", [1]); s.Play();
        var all = s.E<AnniversarySanshouResolvedEvent>();
        var shown = s.E<ProgramCardsRevealedEvent>().Where(e => e.SkillId == "anniversary-god:sanshou").SelectMany(e => e.Cards).ToArray();
        s.Need(all.Length == 2 && !all[1].Prevented && (all[1].UsedCategoryMask & (1 << (int)SkillProgramCardCategory.Equipment)) != 0 && s.V(1).Hp == hp - 1 &&
            shown.Length == 6 && shown.All(c => s.G.CardMovements.Any(m => m.CardId == c.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile)) &&
            !s.E<AnniversaryYizhaoAccumulatedEvent>().Any(),
            "Another participant's actual equipment use counts globally; revealing that already-used category no longer prevents damage, and cleanup is not an owned discard.");
        s.Use("damage", [1]); s.Play();
        s.Need(s.G.CardMovements.Any(m => m.Reason == CardMoveReasons.Reshuffle) && s.E<AnniversarySanshouResolvedEvent>().Length == 3,
            "The next native top-three reveal really exhausts and replenishes the deck in this same actual turn.");
        s.Use("collapse");
        s.Reach(() => s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.Nature == DamageNature.Thunder) == 2 && s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0));
        var history = s.G.Events.Select(e => e.Payload).ToList();
        var lastDamage = history.FindLastIndex(e => e is DamageAppliedEvent { SourceSeat: 1, Nature: DamageNature.Thunder });
        var completed = history.FindLastIndex(e => e is ProgramBindingResolvedEvent { SkillId: "anniversary-god:tianjie", OwnerSeat: 1, Activated: true, Completed: true });
        var ended = history.FindIndex(e => e is TurnEndedEvent { ActorSeat: 0 });
        s.Need(!s.V(0).IsAlive && s.V(1).IsAlive && lastDamage >= 0 && completed > lastDamage && ended > completed &&
            s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 1 && e.Nature == DamageNature.Thunder).Select(e => e.TargetSeat).SequenceEqual([2, 3]),
            "A genuinely dying then dead turn owner does not suppress a living Zhang Jiao's Tianjie; it retains the same pre-finalization ending boundary.");
    }
    private sealed class Fixture(bool sanshou) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-shen-zhang-jiao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"discard","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"collapse","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keep}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary-zhang-jiao.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]}]}
            """;
            var labels = new[] { Driver, NoDraw, Quiet, Keep, GainHold }.ToDictionary(id => id, id => new { name = id, description = "十周年神张角真实引擎小局",
                optionLabels = id == GainHold ? new Dictionary<string, string> { ["continue"] = "继续原技能" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Ally, "真实同行", "", Driver, "wu", 9)); b.AddGeneral(new(Enemy, "真实敌方", "", Quiet, "wei", 9));
            var cards = sanshou ? Enumerable.Range(0, 6).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 1)).ToArray() :
                Enumerable.Range(0, 10).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 1))
                    .Concat(Enumerable.Range(0, 3).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 12)))
                    .Concat(Enumerable.Range(0, 4).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 13))).ToArray();
            b.AddDeck(new(Deck, "固定真实类型与点数牌堆", 0, 2, []) { PhysicalCards = cards });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "十周年神张角小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Mark() => V(0).Markers?.FirstOrDefault(m => m.Kind == PlayerMarkerKind.Huang)?.Count ?? 0;
        public void Use(string id, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId == GainHold) Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else throw new InvalidOperationException("Unexpected anniversary Zhang Jiao boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Bounded anniversary Zhang Jiao occurrence not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves original random materials, paid marker and target cursor.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool sanshou = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(sanshou));
            var hand = sanshou ? new[] { new ShanheStartingCard(CardKind.Crossbow, Suit.Club, 1) } : Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Spade, 13)).ToArray();
            var setup = new ShanheBattleSetup(37, 1, "anniversary-zj", sanshou ? Ally : Hero, 2, 0, 0, 0, 0, [Driver, NoDraw, Keep, GainHold], hand, [],
                [new(Enemy, ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 7)] }, new(Enemy, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(sanshou ? Hero : Ally, ExtraSkillIds: [NoDraw, Quiet]) { StartingHand = [] }], new string('d', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 28 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
