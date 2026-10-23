using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryXiahouEnChecks
{
    private const string Hero = "anniversary:xiahou-en", Sword = "anniversary:chijian", Shiwu = "anniversary:shiwu";
    private const string Driver = "fixture:ann-xe-driver", Watch = "fixture:ann-xe-watch", Quiet = "fixture:ann-xe-quiet", Deck = "fixture:ann-xe-deck";
    public static void ActualTurnNativeDuelPermanentSwordAndTemporaryLossCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Play(g);
        Need(V(g).Hp == 5 && HasSword(g, 0) && g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "Formal five-HP Xiahou En has his real virtual Qinggang range two while the opposite seat is outside ordinary range one." + Detail(g));
        EquipOther(g);
        var hp = V(g, 1).Hp;
        Native(g, Slash(g, 1)); Play(g); Native(g, Slash(g, 1)); Play(g);
        Need(V(g, 1).Hp == hp - 2 && V(g, 1).Equipment.Any(c => c.Kind == CardKind.RenwangShield) &&
            V(g).Hand.Any(c => c.Kind == CardKind.Slash) && g.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash) &&
            V(g).Equipment.All(c => c.Kind != CardKind.QinggangSword),
            "Two actual black Slashes ignore the real Renwang Shield through virtual Qinggang; the still-held third Slash is genuinely beyond the printed plus-one quota, with no physical sword created." + Detail(g));
        End(g); Offer(g, 1); var hand = V(g).HandCount; Activate(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = Root(g)!; var origin = E<XiahouEnShiwuIssuedEvent>(g).Single().Origin;
        Need(paid.AnniversaryXiahouEn is { Operation: SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage,
                Stage: AnniversaryXiahouEnStage.GainChildren, GainedCardId: > 0 } receipt && paid.PendingMovementContinuation is not null &&
            receipt.Origin == origin && V(g).HandCount == hand + 1 &&
            g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == origin.UseId) is { CardId: 0, CardKind: CardKind.Duel,
                PhysicalCardIds.Count: 0, Action.Type: CardActionType.Use } &&
            E<DamageAppliedEvent>(g).Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 1 } &&
            g.CardMovements.Count(m => m.CardId == receipt.GainedCardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.anniversary-shiwu.damage-card") == 1,
            "The actual first foreign turn issues a real zero-entity Duel; its genuine owner-to-opponent damage gains one real damage card and pauses that already-paid gain child." + Detail(g));
        var paidId = paid.Id; g = Cold(g, registry); Continue(g); Offer(g, 2);
        Need(E<XiahouEnShiwuGainPaidEvent>(g).Count(e => e.FrameId == paidId && e.ActualCount == 1) == 1 &&
            E<XiahouEnShiwuReturnedEvent>(g).Count(e => e.FrameId == origin.FrameId && e.UseId == origin.UseId) == 1,
            "The cold gain return never repeats payment and resumes the exact original native Duel once." + Detail(g));
        var beforeLoss = V(g).Hp; Activate(g);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash } && p.IncomingCard == CardKind.Duel);
        var losing = E<XiahouEnShiwuIssuedEvent>(g).Last().Origin;
        Need(losing.TurnOwner == 2 && E<DuelResponseEvent>(g).Any(e => e.ResolutionId == losing.UseId && e.ResponderSeat == 2 && e.UsedSlash),
            "The real opponent spends its own physical Slash before the human makes the next native Duel response.");
        Answer(g, c => c.Cards.Count == 0); Offer(g, 3);
        Need(V(g).Hp == beforeLoss - 1 && HasSword(g, 0) && HasSword(g, 2) &&
            E<XiahouEnChijianLostGrantEvent>(g).Count(e => e.ActualTurn == losing.ActualTurn && e.OwnerSeat == 0) == 1 &&
            E<XiahouEnChijianRestoredEvent>(g).Count(e => e.ActualTurn == losing.ActualTurn && e.TurnOwner == 2 && e.Outcome == "restored") == 1 &&
            E<TurnEndedEvent>(g).Any(e => e.TurnNumber == losing.ActualTurn && e.ActorSeat == 2),
            "After the human really declines a Duel Slash, the opponent permanently receives Chijian and only the original owner's removed grant returns at that exact actual turn end." + Detail(g));
        Answer(g, c => Key(c) == "skip"); Play(g);
        Need(HasSword(g, 2) && V(g, 2).Skills!.Count(s => s.ContentId == Sword) == 1 &&
            E<XiahouEnChijianGrantedEvent>(g).Any(e => e.RecipientSeat == 2 && !e.DeathBequest),
            "The opponent's independent acquired sword survives into the next own turn and does not duplicate one effective skill.");
        Use(g, "lose-three", []); Play(g);
        Need(V(g).Hp == 1, "The final real native Duel is prepared by a genuine three-HP loss from four, without injecting state.");
        End(g); Offer(g, 1); Answer(g, c => Key(c) == "skip"); Offer(g, 2); Answer(g, c => Key(c) == "skip"); Offer(g, 3); Activate(g);
        ReachState(g, () => !V(g).IsAlive && E<XiahouEnChijianGrantedEvent>(g).Any(e => e.DeathBequest && e.RecipientSeat == 3) &&
            E<XiahouEnShiwuReturnedEvent>(g).Count() == 3);
        var lethal = E<XiahouEnShiwuIssuedEvent>(g).Last().Origin;
        Need(HasSword(g, 3) && V(g, 3).Skills!.Count(s => s.ContentId == Sword) == 1 &&
            E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0 && e.KillerSeat == 3) &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 1) &&
            E<XiahouEnChijianGrantedEvent>(g).Count(e => e.OwnerSeat == 0 && e.RecipientSeat == 3 && e.DeathBequest) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == lethal.UseId && e.CardKind == CardKind.Duel) == 1,
            "The actual native Duel killer receives a permanent independent Chijian source after true death, and the original dead owner's issued Duel still completes once." + Detail(g));
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The short command journal replays virtual weapon behavior, actual-turn Duels, paid gain cold return, permanent acquisition, exact loss restoration and true-death bequest.");
    }
    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "anniversary-xiahou-en", Hero, 5, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.RenwangShield, Suit.Club, 2), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 2)], [],
            [new(Peer(2), MaxHpBonus: 0, Hp: 4) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7)] },
             new(Peer(3), MaxHpBonus: 0, Hp: 2) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7)] }],
            [new(Peer(1)) { StartingHand = [] }], new string('a', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10 };
    }
    private static string Peer(int seat) => "fixture:ann-xe-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static bool HasSword(GameEngine g, int seat) => V(g, seat).Skills!.Any(s => s.ContentId == Sword);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryXiahouEn is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Xiahou En command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "Missing Xiahou En choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => Key(c) == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static LegalAction Slash(GameEngine g, int target) => g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && a.CardId is { } id && V(g).Hand.Single(c => c.Id == id).Kind == CardKind.Slash);
    private static void EquipOther(GameEngine g)
    {
        Use(g, "equip-other", [1]);
        ReachState(g, () => V(g, 1).Equipment.Any(c => c.Kind == CardKind.RenwangShield) || P(g)?.Choices.Any(c => Key(c) == "select-and-move-owned-card") == true);
        if (P(g)?.Choices.Any(c => Key(c) == "select-and-move-owned-card") == true) Answer(g, _ => true);
        Play(g);
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Offer(GameEngine g, int target) => Reach(g, p => p.SkillPrompt?.SkillId == Shiwu && p.Choices.Any(c => Key(c) == "activate") &&
        g.ResolutionStack.OfType<ForeignActualTurnStartWindowFrame>().Last().OwnerSeat == target);
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready) => ReachState(g, () => P(g) is { } p && ready(p));
    private static void ReachState(GameEngine g, Func<bool> ready)
    {
        for (var n = 0; n < 180; n++)
        {
            if (ready()) return;
            if (P(g) is { } p)
            {
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash) { Answer(g, c => c.Cards.Count == 0); continue; }
                if (p.PlayerSeat == 0 && p.Kind == DecisionKind.RescueDying) { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Xiahou En native boundary: " + JsonSerializer.Serialize(p) + Detail(g));
            }
            Need(g.State.Winner == Winner.None, "The short Xiahou En scene must retain its genuine native boundary." + Detail(g));
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Xiahou En missed his bounded native boundary." + Detail(g));
    }
    private static string Detail(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).HandCount, Sword = HasSword(g, s) }),
        Root = Root(g)?.AnniversaryXiahouEn, Issued = E<XiahouEnShiwuIssuedEvent>(g), Returned = E<XiahouEnShiwuReturnedEvent>(g), Granted = E<XiahouEnChijianGrantedEvent>(g), Lost = E<XiahouEnChijianLostGrantEvent>(g), Restored = E<XiahouEnChijianRestoredEvent>(g) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "The paid original Duel-damage gain child cold-restores identically."); return cold; }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-xiahou-en", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"equip-other","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
                {"id":"lose-three","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-damage-card-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary-shiwu.damage-card"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "真实装备与体力支付", description = "使用原生场上装备移动和三点失去体力。" },
              [Watch] = new { name = "已支付恃武获得", description = "在真实获得子窗暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
              [Quiet] = new { name = "固定资源角色", description = "不进行普通摸牌或出牌。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "同桌决斗角色", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定小杀牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "恃武真实回合与永久持剑", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
