using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryLiuYouChecks
{
    private const string Hero = "anniversary:liu-you", Skill = "anniversary:kannan", Peer = "fixture:liu-you-peer", Aggressor = "fixture:liu-you-aggressor",
        NoDraw = "fixture:liu-you-no-draw", Quiet = "fixture:liu-you-quiet", Driver = "fixture:liu-you-driver", Rank = "fixture:liu-you-rank", Hold = "fixture:liu-you-paid";
    public static void ActualPlayQuotaEffectivePindianAndPersistentNextSlashCold()
    {
        var s = Scene.Start(); s.Play();
        var tieCard = s.V().Hand!.Single(c => c.Kind == CardKind.Dodge && c.Rank == 8).Id;
        var winCard = s.V().Hand!.Single(c => c.Kind == CardKind.Dodge && c.Rank == 13).Id;
        var loseCard = s.V().Hand!.Single(c => c.Kind == CardKind.Dodge && c.Rank == 7).Id;
        var slash = s.V().Hand!.Single(c => c.Kind == CardKind.Slash).Id;
        s.Contest(1, tieCard); s.Play();
        var first = s.E<AnniversaryLiuYouStartedEvent>().Single();
        s.Need(s.E<AnniversaryLiuYouResultEvent>() is [{ SourceRank: 8, OpponentRank: 8, Winner: -1 }] && !s.E<AnniversaryLiuYouBonusIssuedEvent>().Any() &&
            s.Zone(tieCard) == CardLocation.DiscardPile && !s.CanContest() && s.V().Hp == 1,
            "A native effective-rank tie (printed opposing6 plus2) rewards neither role, consumes one real use, and reaches the current HP1 quota despite other available opponents.");
        s.Heal(); s.Play();
        s.Need(s.V().Hp == 2 && s.CanContest() && !s.Targets().Contains(1) && s.Targets().Contains(2),
            "The quota follows current HP after native recovery while the original actual Play still excludes its already challenged role.");
        s.Contest(2, winCard); s.HoldPaid();
        var win = s.E<AnniversaryLiuYouResultEvent>().Single(e => e.Winner == 0);
        var grant = s.E<AnniversaryLiuYouBonusIssuedEvent>().Single();
        var result = s.E<PindianResultDeterminedEvent>().Single(e => e.FrameId == win.PindianId).Result;
        s.Need(win.SourceRank == 13 && win.OpponentRank == 8 && grant.FrameId == win.FrameId && grant.Beneficiary == 0 &&
            new[] { result.SourceCardId, result.OpponentCardId }.All(id => s.Zone(id) == CardLocation.DiscardPile &&
                s.G.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.PindianReveal && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.PindianFinish && m.To == CardLocation.DiscardPile) == 1),
            "The original native contest pays both real entities, returns its effective result once, and issues the actual winner's persistent next-Slash reward before the paid movement observer pauses.");
        s.Cold(); s.Continue(); s.Play(); s.Heal(); s.Play();
        s.Need(s.V().Hp == 3 && !s.CanContest() && s.V(3).HandCount > 0 && s.E<AnniversaryLiuYouStartedEvent>().Length == 2 &&
            s.E<AnniversaryLiuYouStartedEvent>().All(e => e.Phase == first.Phase) && s.E<AnniversaryLiuYouBonusIssuedEvent>().Length == 1,
            "Cold never repays the native contest; winning locks this exact Play even after HP3 leaves quota and an untouched legal opponent.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 5 && s.V().Hp == 2 && s.CanContest() && !s.E<AnniversaryLiuYouBonusConsumedEvent>().Any(),
            "A later actual Play has a fresh quota, while the already issued reward survives all intervening turns and a real incoming ordinary Slash.");
        var targetHp = s.V(2).Hp; s.Native(slash, 2); s.Play();
        var ownCapture = s.E<AnniversaryLiuYouSlashCapturedEvent>().Single();
        s.Need(ownCapture is { Actor: 0, Amount: 1 } && s.E<AnniversaryLiuYouBonusConsumedEvent>() is [{ GrantFrameId: var consumed, UseId: var used }] &&
            consumed == grant.FrameId && used == ownCapture.UseId && s.V(2).Hp == targetHp - 2 &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == ownCapture.UseId && e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == ownCapture.UseId && e.CardKind == CardKind.Slash) == 1,
            "The winner's next genuine native Slash consumes its exact old grant once, deals real base2 damage, and completes the original Use.");
        s.Contest(3, loseCard); s.Play();
        var lost = s.E<AnniversaryLiuYouResultEvent>().Single(e => e.Winner == 3);
        var otherGrant = s.E<AnniversaryLiuYouBonusIssuedEvent>().Single(e => e.Beneficiary == 3);
        s.Need(lost.SourceRank == 7 && lost.OpponentRank == 8 && otherGrant.FrameId == lost.FrameId &&
            s.E<AnniversaryLiuYouStartedEvent>().Single(e => e.FrameId == lost.FrameId).Phase != first.Phase &&
            s.E<AnniversaryLiuYouBonusConsumedEvent>().All(e => e.GrantFrameId != otherGrant.FrameId),
            "The other participant really wins with its effective rank and receives its own independent reward in the later actual Play.");
        s.End(); s.Reach(() => s.E<AnniversaryLiuYouSlashCapturedEvent>().Any(e => e.Actor == 3));
        var foreignCapture = s.E<AnniversaryLiuYouSlashCapturedEvent>().Single(e => e.Actor == 3);
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == foreignCapture.UseId));
        s.Need(foreignCapture.Amount == 1 && s.E<AnniversaryLiuYouBonusConsumedEvent>().Count(e => e.GrantFrameId == otherGrant.FrameId && e.UseId == foreignCapture.UseId && e.Beneficiary == 3) == 1 &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == foreignCapture.UseId && e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 2) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == foreignCapture.UseId && e.CardKind == CardKind.Slash) == 1 && s.E<AnniversaryLiuYouBonusIssuedEvent>().Length == 2,
            "The foreign winner's next actual Slash also consumes exactly its own reward and finishes genuine base2 damage, without carrying the previous winner's consumed bonus forward.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public bool CanContest() => G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill);
        public int[] Targets() => G.GetHumanLegalActions().Where(a => a.ProgramSkillId == Skill).SelectMany(a => a.SelectableTargetSeats).Distinct().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Heal() => Accept(new UseProgramSkillCommand(0, Driver, "recover", [], [], G.Revision, P!.PromptId));
        public void Contest(int target, int card)
        {
            Accept(new UseProgramSkillCommand(0, Skill, "phase-contest", [], [target], G.Revision, P!.PromptId));
            Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Cards.SequenceEqual([card])));
            Answer(c => c.Cards.SequenceEqual([card]));
        }
        public void HoldPaid() => Reach(() => P?.SkillPrompt?.SkillId == Hold);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Native(int id, int target)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.TargetSeats.SequenceEqual([target]));
            Need(a is not null, "The fixed original Slash retains a genuine native legal use against its actual target.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
                { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only the actual human chooser can answer the canonical original prompt."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 200; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Hold) { Continue(); continue; }
                    if (p.Kind == DecisionKind.RespondDodge) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.Kind == DecisionKind.RescueDying) { Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected original Liu You prompt at " + line + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded native Liu You flow not reached at " + line + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold preserves the paid native contest observer, actual phase issue, effective result and unconsumed winner grant.");
        }
        public void Accept(GameCommand c)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Liu You command."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Started = E<AnniversaryLiuYouStartedEvent>(), Results = E<AnniversaryLiuYouResultEvent>(),
            Issued = E<AnniversaryLiuYouBonusIssuedEvent>(), Consumed = E<AnniversaryLiuYouBonusConsumedEvent>(), Slashes = E<AnniversaryLiuYouSlashCapturedEvent>(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(25).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Dodge, Suit.Heart, 13), new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Slash, Suit.Club, 3)];
            ShanheStartingCard[] peer = [new(CardKind.Dodge, Suit.Heart, 6)];
            ShanheStartingCard[] aggressive = [new(CardKind.Dodge, Suit.Heart, 6), new(CardKind.Slash, Suit.Club, 6), new(CardKind.Slash, Suit.Club, 6)];
            var setup = new ShanheBattleSetup(42, 1, "liu-you-native-pindian", Hero, 1, 0, 0, 0, 0, [NoDraw, Driver, Hold], hand, [new(CardKind.QinggangSword, Suit.Spade, 5)],
                [new(Peer, Hp: 8) { StartingHand = peer }, new(Aggressor, Hp: 8) { StartingHand = aggressive }], [new(Peer, Hp: 8) { StartingHand = peer }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-liu-you", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[{"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]}]},
             {"id":"{{Rank}}","revision":1,"cardPolicies":[{"id":"actual-rank","kind":"pindianRankBonus","value":2}]},
             {"id":"{{Hold}}","revision":1,"triggers":[{"id":"paid-contest","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.pindian.reveal"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, Driver, Rank }) names[id] = new { name = "刘繇固定原生输入", description = "原生阶段、回复和有效拼点点数" };
            names[Hold] = new { name = "拼点已付观察者", description = "真实原生移动后的暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "固定同桌", "supporter", NoDraw, "wei", 8, AdditionalSkillIds: [Quiet, Rank]));
            b.AddGeneral(new(Aggressor, "固定敌方", "warrior", NoDraw, "wei", 8, AdditionalSkillIds: [Rank]));
            const string deck = "fixture:liu-you-deck";
            b.AddDeck(new(deck, "固定原生闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "刘繇原生拼点", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer, Aggressor], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
