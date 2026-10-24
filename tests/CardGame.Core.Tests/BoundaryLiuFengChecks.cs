using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class BoundaryLiuFengChecks
{
    private const string Hero = "boundary:liu-feng", Xiansi = "boundary:xiansi", Qinling = "boundary:qinling", Driver = "fixture:liu-feng-driver", Quiet = "fixture:liu-feng-quiet", NoDraw = "fixture:liu-feng-no-draw", Ask = "fixture:liu-feng-ask", Watch = "fixture:liu-feng-cost-watch", Deck = "fixture:liu-feng-deck";
    private static string Peer(int seat) => "fixture:liu-feng-peer-" + seat;
    public static void SameOwnerTwoCardPileTrueDiscardSlashAndForeignTurnRequestCold()
    {
        var s = Scene.Start(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Xiansi && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Menu(BoundaryLiuFengStage.Collect);
        var original = s.V(2).Hand!.Where(c => c.Kind == CardKind.Peach).Take(2).Select(c => c.Id).ToArray();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "collect" && c.Parameters.GetValueOrDefault("count") == "2" && c.Parameters.GetValueOrDefault("0.card-owner-seat") == "2" && c.Parameters.GetValueOrDefault("1.card-owner-seat") == "2" &&
            new[] { int.Parse(c.Parameters["0.slot-index"]), int.Parse(c.Parameters["1.slot-index"]) }.Select(i => s.V(2).Hand![i].Id).SequenceEqual(original));
        s.Play();
        s.Need(original.Length == 2 && original.All(id => s.Zone(id) == CardLocation.Authority(0)) && s.G.CardMovements.Count(m => original.Contains(m.CardId) && m.From == CardLocation.Hand(2) && m.To == CardLocation.Authority(0)) == 2,
            "The current OL Xiansi really moves two distinct entities from the same other Hand into the original public inverse pile.");
        var weapon = s.V(0).Hand!.Single(c => c.Kind == CardKind.ZhangbaSerpentSpear).Id; s.Equip(weapon); s.Play();
        s.Need(s.G.GetCombatDistance(0, 2) > 1, "The fixed opposite target actually needs the original weapon's range.");
        s.Qinling(); s.Menu(BoundaryLiuFengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([2])); s.Menu(BoundaryLiuFengStage.Cost);
        s.Need(!s.P!.Choices.Any(c => c.Cards.Contains(weapon)) && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost"),
            "Paying the only long weapon cannot manufacture a legal far-target Slash after that weapon leaves Equipment.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel"); s.Play();
        s.Need(s.E<BoundaryLiuFengCostPaidEvent>().Length == 0 && s.G.GetHumanLegalActions().Any(a => a.SkillId == Qinling), "An unpaid cancellation does not consume the real-turn opportunity.");
        var cost = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id; var hp = s.V(3).Hp; var hand = s.V(0).HandCount;
        s.Qinling(); s.Menu(BoundaryLiuFengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([3])); s.Menu(BoundaryLiuFengStage.Cost); s.Answer(c => c.Cards.Contains(cost)); s.Watch();
        var paidFrame = s.Root!.Id;
        s.Need(s.Zone(cost) == CardLocation.DiscardPile && s.E<NativeLogicalDiscardMaterialCompletedEvent>().Count(e => e.CardId == cost && e.From == CardLocation.Hand(0)) == 1 && s.E<BoundaryLiuFengUseIssuedEvent>().Length == 0,
            "The original frame really pays one native logical discard before any zero-material Slash is issued.");
        s.Cold(); s.Continue(); s.Watch();
        var rewardAttempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == paidFrame);
        s.Need(s.Root!.BoundaryLiuFeng is { Stage:BoundaryLiuFengStage.RewardDraw, Returned:true, ConsequencePaid:true, RewardDraw:null } && s.Root.NativeDraw is { RequestedCount:1, ReturnKind:NativeDrawAttemptReturnKind.ProgramOperation, Operation:SkillProgramEffectOp.ResolveBoundaryLiuFengQinling } &&
            rewardAttempt.Context.RecipientSeat == 0 && rewardAttempt.Context.RequestedCount == 1 && rewardAttempt.Context.Reason.Value == "skill-program.boundary:qinling.reward" && s.V(0).HandCount == hand-1,
            "The reward first opens the genuine NativeDrawStarting replacement window on the original paid frame, after the whole real Slash returns and before drawing any reward material.");
        s.Continue(); s.Watch();
        var reward = s.Root!.BoundaryLiuFeng!.RewardDraw!;
        s.Need(reward.AttemptFrameId == rewardAttempt.Id && reward.ActualDrawIds.Count == 1 && reward.Replacement is null && reward.ActualDrawIds.All(id => s.Zone(id) == CardLocation.Hand(0)) && s.E<BoundaryLiuFengRewardDrawReturnedEvent>().Count(e => e.FrameId == paidFrame) == 1 && s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == rewardAttempt.Id && e.ParentFrameId == paidFrame && !e.Replaced) == 1,
            "One native draw returns its exact typed outcome and actual reward entity to the same paid frame; its movement observer still suspends final completion.");
        s.Cold(); s.Continue(); s.Play();
        var hit = s.E<BoundaryLiuFengUseIssuedEvent>().Single().Origin;
        s.Need(hit.FrameId == paidFrame && s.V(3).Hp == hp - 1 && s.V(0).HandCount == hand && s.Zone(cost) == CardLocation.DiscardPile && s.E<BoundaryLiuFengCostPaidEvent>().Count(e => e.FrameId == paidFrame) == 1 && s.E<BoundaryLiuFengConsequenceEvent>().Single().Damaged &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == hit.UseId && e.CardId == 0 && e.SourceSeat == 0 && e.CardKind == CardKind.Slash) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == hit.UseId && e.CardId == 0) == 1 && !s.G.CardMovements.Any(m => m.CardId == cost && m.Reason == CardMoveReasons.Use),
            "Cold resumes the same already-paid frame; a real zero-entity Slash hits once, leaves its distinct discard cost in Discard, and draws exactly one after the whole Use returns.");
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.SkillId == Qinling), "A completed use spends the one opportunity for the actual current turn.");
        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("boundary-liu-feng-open")) == true);
        var foreignTurn = s.G.State.TurnNumber;
        s.Need(s.G.State.CurrentSeat == 2 && foreignTurn > 1 && s.P!.PlayerSeat == 0 && s.P.SkillPrompt?.SkillId == Ask,
            "The existing automatic native request reaches the real human Liu Feng during somebody else's actual turn, even without any physical Slash.");
        var enemyHp = s.V(2).Hp; var secondCost = s.V(0).Hand!.First(c => c.Kind == CardKind.Peach).Id;
        s.Answer(c => c.Parameters.ContainsKey("boundary-liu-feng-open")); s.Menu(BoundaryLiuFengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([2])); s.Menu(BoundaryLiuFengStage.Cost); s.Answer(c => c.Cards.Contains(secondCost)); s.Watch(); s.Continue();
        s.Reach(() => s.E<BoundaryLiuFengCompletedEvent>().Length == 4 && !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Ask));
        var missed = s.E<BoundaryLiuFengUseIssuedEvent>().Last().Origin;
        s.Need(missed.Turn == foreignTurn && missed.TurnOwner == 2 && s.V(2).Hp == enemyHp && s.Zone(secondCost) == CardLocation.Authority(0) && s.E<BoundaryLiuFengConsequenceEvent>().Last() is { Damaged: false, MovedCost: true } &&
            s.E<BoundaryLiuFengUseReturnedEvent>().Count(e => e.UseId == missed.UseId) == 1 && s.E<BoundaryLiuFengCostPaidEvent>().Select(e => e.Turn).SequenceEqual([1, foreignTurn]) && s.E<ProgramOptionChosenEvent>().Any(e => e.SkillId == Ask && e.OptionId == "used-slash" && e.ChooserSeat == 0),
            "The next actual turn grants a fresh genuine Use opportunity; native Dodge prevents damage, returns to the original caller once, and moves exactly the original discarded card into inverse without drawing a reward.");
        s.Replay();
    }
    public static void NativeDuelPlayedBoundaryAndHejiPaidParentReturnCold()
    {
        var s = Scene.Start(heji: true); s.Play();
        var duel = s.V(0).Hand!.Single(c => c.Kind == CardKind.Duel).Id; var hp = s.V(0).Hp;
        s.Card(duel,[2]);
        s.Reach(() => s.P is { PlayerSeat:0, Kind:DecisionKind.RespondSlash });
        s.Need(!s.P!.Choices.Any(c => c.Parameters.ContainsKey("boundary-liu-feng-open")) && s.E<BoundaryLiuFengCostPaidEvent>().Length == 0,
            "The real Duel asks Liu Feng only to Play a Slash; Qinling does not turn that actual Played boundary into a Use.");
        s.Answer(c => c.Cards.Count == 0);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:heji" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Need(s.V(0).Hp == hp-1 && s.Zone(duel) == CardLocation.DiscardPile, "The original physical Duel really resolves its played exchange, and its native completion has already settled the original physical entity into Discard.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("boundary-liu-feng-open")) == true);
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.WuJing is { Stage:WuJingStage.UseChoice });
        var cost = s.V(0).Hand!.First(c => c.Kind == CardKind.Peach).Id; var targetHp = s.V(2).Hp;
        s.Answer(c => c.Parameters.ContainsKey("boundary-liu-feng-open")); s.Menu(BoundaryLiuFengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([2])); s.Menu(BoundaryLiuFengStage.Cost); s.Answer(c => c.Cards.Contains(cost)); s.Watch();
        s.Need(s.Root!.BoundaryLiuFeng!.Need is { OwnerFrameId:var original } && original == parent.Id && s.Zone(cost) == CardLocation.DiscardPile && s.Zone(duel) == CardLocation.DiscardPile,
            "The same paid Qinling frame sits between the actual Heji request and its native movement child, retaining the exact already-settled outer Duel action.");
        s.Cold(); s.Continue(); s.Play();
        var issued = s.E<BoundaryLiuFengUseIssuedEvent>().Single().Origin;
        var heji = s.E<WuJingHejiNativeIssuedEvent>().Single();
        s.Need(heji.ProgramFrameId == parent.Id && heji.UseId == issued.UseId && heji.ActionId == issued.ActionId && heji.MaterialCount == 0 && !heji.RewardEligible && s.E<WuJingHejiPaidEvent>().Count(e => e.UseId == issued.UseId) == 1 && s.E<WuJingHejiReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
            s.V(2).Hp == targetHp && s.Zone(cost) == CardLocation.Authority(0) && s.Zone(duel) == CardLocation.DiscardPile && s.G.CardMovements.Count(m => m.CardId == duel && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && s.E<WuJingRewardIssuedEvent>().Length == 0 && s.E<BoundaryLiuFengConsequenceEvent>().Single() is { Damaged:false,MovedCost:true },
            "The exact native special parent accepts one real zero-material Qinling Use, Dodge resolves it, its original cost becomes inverse, Heji returns once without an unconverted-card reward, and the outer physical Duel remains cleaned up exactly once.");
        s.Replay();
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f => f.BoundaryLiuFeng is { Stage: not BoundaryLiuFengStage.Completed });
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Menu(BoundaryLiuFengStage stage) => Reach(() => Root?.BoundaryLiuFeng?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "boundary-liu-feng") == true);
        public void Watch() => Reach(() => P?.SkillPrompt?.SkillId == BoundaryLiuFengChecks.Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Qinling() => Accept(new UseProgramSkillCommand(0, BoundaryLiuFengChecks.Qinling, BoundaryLiuFengComposition.Activation, [], [], G.Revision, P!.PromptId));
        public void Equip(int id) { var a = G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Equip && a.CardId == id); Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId,a.PlayedCardKind,a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); }
        public void Card(int id,int[] targets) { var a = G.GetHumanLegalActions().Single(a => a.CardId == id && a.TargetSeats.SequenceEqual(targets) && a.Kind != LegalActionKind.Recast); Accept(new PlayCardCommand(0,id,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId) { ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources }); }
        public void Answer(Func<PromptChoice,bool> predicate) { var p = P!; var c = p.Choices.First(predicate); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, G.Revision)); }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, "The actual native command is accepted: " + r.Error?.Message); }
        public void Reach(Func<bool> reached, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 220; n++)
            {
                if (reached()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("boundary-liu-feng-open")); continue; }
                    throw new InvalidOperationException("Unexpected native Liu Feng boundary at " + line + Dump());
                }
                Need(G.State.Winner == Winner.None, "The fixed short native table retains its participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Liu Feng did not reach the native boundary at " + line + Dump());
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0,4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f,f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold preserves the exact already-paid discard, original skill frame and native movement child before issuance."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry); Need(before == State(), "Same-owner collection, true logical discard, both native Use outcomes and actual foreign-turn return replay exactly."); }
        public void Need(bool valid,string why) { if (!valid) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.BoundaryLiuFeng, Players = Enumerable.Range(0,4).Select(i => new { i,V(i).Hp,V(i).HandCount }), Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray() });
        public static Scene Start(bool heji=false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var peaches = Enumerable.Range(0,6).Select(i => new ShanheStartingCard(CardKind.Peach,Suit.Heart,i+2)).ToArray();
            var setup = new ShanheBattleSetup(17,1,"boundary-liu-feng-native",Hero,4,0,0,0,0,heji ? [NoDraw,BoundaryLiuFengChecks.Watch,"ol:heji"] : [NoDraw,BoundaryLiuFengChecks.Watch],
                new[] { new ShanheStartingCard(CardKind.ZhangbaSerpentSpear,Suit.Spade,12),new ShanheStartingCard(CardKind.Peach,Suit.Heart,3),new ShanheStartingCard(CardKind.Peach,Suit.Diamond,4),new ShanheStartingCard(CardKind.Duel,Suit.Spade,1) }.Concat(heji ? [new ShanheStartingCard(CardKind.Slash,Suit.Club,8)] : Array.Empty<ShanheStartingCard>()).ToArray(),[],
                [new(Peer(2),Hp:heji ? 1 : 8) { StartingHand = new[] { new ShanheStartingCard(CardKind.Dodge,Suit.Club,2),new ShanheStartingCard(CardKind.Dodge,Suit.Diamond,3) }.Concat(heji ? [new ShanheStartingCard(CardKind.Slash,Suit.Heart,7)] : Array.Empty<ShanheStartingCard>()).Concat(peaches).ToArray() },new(Peer(3),Hp:8) { StartingHand = peaches }],
                [new(Peer(1),Hp:8) { StartingHand = peaches }],new string('a',64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed,PlayerCount = 4,HumanSeat = 0,HumanRole = null,HumanTeamId = ShanheBattleSetup.PlayerTeamId,ModeId = ShanheBattleSetup.BattleModeId,ShanheBattle = setup,UseInteractiveSetup = false,UseInteractiveDiscard = false,AdvanceAfterHumanCommands = false,MaxTurns = 8 },registry);
            var result = new Scene(g,registry); result.Accept(new StartGameCommand()); return result;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-liu-feng",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"ol:wu-jing");
            b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:zhangba-serpent-spear"));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Ask}}","revision":1,"triggers":[{"id":"native-foreign-turn","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingLeastHandCount"},{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"request"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-original","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:qinling.cost"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"native-reward-request","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"reward-request","options":[{"id":"continue"}]}]},
                {"id":"reward-gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:qinling.reward"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"reward-gained","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """;
            var names = new Dictionary<string,object>();
            foreach (var id in new[] { Quiet,NoDraw,Ask,Watch }) names[id] = new { name = "界刘封固定原生输入",description = "真实暗槽收集、原生弃牌、杀与回合外使用请求。" };
            names[Watch] = new { name = "界刘封已付弃牌观察者",description = "在真实弃牌已付后保留原帧等待。",optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,skills = names }));
            foreach (var (id,p) in catalog.Programs) b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description) { Program = p,ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat),"安静原生同桌"+seat,"standard-guanyu",Quiet,"qun",8,AdditionalSkillIds: seat == 2 ? [NoDraw,Ask] : [NoDraw]));
            b.AddDeck(new(Deck,"固定桃库存",0,2,[]) { PhysicalCards = Enumerable.Range(0,60).Select(i => new ContentDeckPhysicalCard("standard:peach",Suit.Heart,i%13+1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"界刘封真实弃置与使用",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2 }));
        }
    }
}
