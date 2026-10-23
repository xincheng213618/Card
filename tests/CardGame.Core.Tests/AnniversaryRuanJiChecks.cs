using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryRuanJiChecks
{
    private const string Hero = "anniversary:ruan-ji", Zhaowen = "anniversary:zhaowen", Jiudun = "anniversary:jiudun";
    private const string Driver = "fixture:ruan-ji-driver", Watch = "fixture:ruan-ji-watch", Quiet = "fixture:ruan-ji-quiet", Ending = "fixture:ruan-ji-ending";
    private const string Deck = "fixture:ruan-ji-deck", DiscardReason = "skill-program.anniversary:jiudun.discard";

    public static void ShownEntitiesOrdinaryNamesAndPersistentNativeWineCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Offer(g, Zhaowen);
        var turn = g.State.TurnNumber; var shown = V(g).Hand.Select(c => c.Id).Order().ToArray();
        var peach = V(g).Hand.Single(c => c.Kind == CardKind.Peach).Id;
        var dodge = V(g).Hand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Heart).Id;
        var black = V(g).Hand.Where(c => c.Kind == CardKind.Slash).OrderBy(c => c.Rank).Select(c => c.Id).ToArray();
        Need(V(g).Hp == 2 && V(g).MaxHp == 3 && black.Length == 3, "The real three-maximum-HP Ruan Ji has fixed physical starting materials and a genuinely wounded Peach opportunity.");
        Activate(g); Play(g);
        Need(E<RuanJiShownEntityAuthorizedEvent>(g).Where(e => e.ActualTurn == turn).Select(e => e.CardId).Order().SequenceEqual(shown) &&
            E<CardsRevealedEvent>(g).Any(e => e.Cards.Select(c => c.Id).Order().SequenceEqual(shown)),
            "Zhaowen publicly reveals the entire real Hand and grants exactly those original entities in this actual turn, including the normal pre-Play draw.");

        var enemyWeapon = V(g).Hand.Single(c => c.Kind == CardKind.QinggangSword).Id;
        Accept(g, new UseProgramSkillCommand(0, Driver, "equip-peer", [enemyWeapon], [2], g.Revision, P(g)!.PromptId)); Play(g);
        Need(V(g, 2).Equipment.Any(c => c.Id == enemyWeapon && c.Kind == CardKind.QinggangSword) &&
            g.CardMovements.Any(m => m.CardId == enemyWeapon && m.From == CardLocation.Hand(0) && m.To == CardLocation.Equipment(2)),
            "The distance-two enemy receives a real range-two weapon through native equipment placement before either ending-phase physical Slash.");

        var hand = V(g).HandCount;
        var drawTwo = Conversion(g, black[0], CardKind.DrawTwo); var drawSource = drawTwo.ConversionSource!;
        Native(g, drawTwo); Play(g);
        var laterCards = V(g).Hand.Where(c => !shown.Contains(c.Id)).Select(c => c.Id).ToArray();
        Need(V(g).HandCount == hand + 1 && laterCards.Length == 2 &&
            g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Zhaowen || !laterCards.Contains(a.CardId ?? 0)) &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.EffectiveKind == CardKind.DrawTwo &&
                e.Action.PhysicalCards is [{ CardId: var id, CardKind: CardKind.Slash, From: var from }] && id == black[0] && from == CardLocation.Hand(0)),
            "A shown original Slash is really paid as native DrawTwo, while the two later black entities are not silently authorized.");
        var beforeRejected = State(g);
        var rejected = g.Submit(new PlayCardCommand(0, black[1], drawTwo.TargetSeats, g.Revision, P(g)!.PromptId, CardKind.DrawTwo) { ConversionSource = drawSource });
        Need(!rejected.Accepted && State(g) == beforeRejected && g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Zhaowen || a.PlayedCardKind != CardKind.DrawTwo),
            "The same actual-turn conversion name is absent and cannot be submitted with another genuinely shown black entity.");
        var peerHp = V(g, 1).Hp;
        Native(g, Conversion(g, black[1], CardKind.Duel, 1)); Play(g);
        Need(V(g, 1).Hp == peerHp - 1 && E<RuanJiNameUsedEvent>(g).Where(e => e.ActualTurn == turn).Select(e => e.Name).Order().SequenceEqual(new[] { CardKind.Duel, CardKind.DrawTwo }.Order()),
            "An independent ordinary-trick name remains available and resolves the real native Duel once with its original physical material.");

        hand = V(g).HandCount;
        Native(g, g.GetHumanLegalActions().First(a => a.CardId == peach && a.ConversionSource is null)); Draw(g, SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse);
        var redFrame = Root(g)!;
        Need(redFrame.RuanJi is { Stage: RuanJiStage.Drawing } && redFrame.NativeDraw is { RequestedCount: 1, Recipients: [0] } && V(g).Hp == 2,
            "Using the originally shown red Peach requests its native one-card benefit at real commitment before the original recovery effect.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g).Hp == 3 && V(g).HandCount == hand && E<RuanJiDrawReturnedEvent>(g).Count(e => e.FrameId == redFrame.Id && e.Actual == 1) == 1,
            "The restored shown-red benefit is paid once and returns to the same real Peach recovery.");

        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Offer(g, Jiudun); Activate(g);
        Draw(g, SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun);
        var sober = Root(g)!.RuanJi!;
        Need(!sober.WasDrunk && sober.Stage == RuanJiStage.Drawing && g.State.TurnNumber == turn &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActionId == sober.ActionId && e.Action.ActorSeat != 0 && e.Action.EffectiveIsRed == false),
            "The first real foreign black physical Slash reaches Ruan Ji in his ending phase and freezes the sober branch without requiring his own Play.");
        Continue(g);
        Reach(g, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        Need(V(g).HasAlcoholEffect && E<RuanJiWineIssuedEvent>(g).Length == 1 && E<RuanJiWineReturnedEvent>(g).Length == 1 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action is { ActorSeat: 0, ProviderSeat: 0, Type: CardActionType.Use, EffectiveKind: CardKind.Alcohol, PhysicalCards.Count: 0 } a && a.ParentActionId == sober.ActionId),
            "Sober Jiudun performs a genuine zero-entity Alcohol Use and typed return before resuming the original Slash response in a non-Play phase.");
        Answer(g, c => c.Cards.SequenceEqual([dodge])); Draw(g, SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse); Continue(g);
        Offer(g, Jiudun); Activate(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Jiudun && p.Choices.Any(c => Key(c) == "anniversary-ruan-ji-discard"));
        var drunkFrame = Root(g)!; var drunk = drunkFrame.RuanJi!;
        Need(drunk.WasDrunk && drunk.Stage == RuanJiStage.DiscardChoice && drunk.ActionId != sober.ActionId && V(g).Hp == 3,
            "A shown red Dodge is a genuine use with one reward; the second real black Slash independently freezes the already-drunk discard branch.");
        var discard = P(g)!.Choices.First(c => !c.Cards.Contains(black[2])).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([discard]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch && Root(g)?.RuanJi?.Stage == RuanJiStage.DiscardChildren);
        Need(Zone(g, discard) == CardLocation.DiscardPile && E<RuanJiDiscardPaidEvent>(g).Count(e => e.FrameId == drunkFrame.Id) == 1 &&
            g.CardMovements.Count(m => m.CardId == discard && m.From == CardLocation.Hand(0) && m.Reason.Value == DiscardReason) == 1 &&
            g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == drunk.UseId).IneffectiveTargetSeats?.Contains(0) != true,
            "The selected hand entity is already truly discarded once while its real movement child suspends the original incoming Use before applying target invalidation.");
        g = Cold(g, registry); Continue(g); Offer(g, Zhaowen);
        Need(g.State.TurnNumber != turn && V(g).Hp == 3 && V(g).HasAlcoholEffect && E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn) &&
            E<ProgramCardEffectNullifiedEvent>(g).Count(e => e.FrameId == drunkFrame.Id && e.CardUseFrameId == drunk.UseId && e.OwnerSeat == 0) == 1 &&
            E<RuanJiDiscardPaidEvent>(g).Count(e => e.FrameId == drunkFrame.Id) == 1 && E<RuanJiWineIssuedEvent>(g).Length == 1,
            "Cold return applies the paid invalidation exactly once, prevents the second Slash without using up the wine, and preserves the real Alcohol effect across the actual turn end.");
        Answer(g, c => Key(c) == "skip"); Play(g);
        Need(g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Zhaowen), "Skipping the next real Play's reveal leaves no authorization from the prior actual turn.");
        peerHp = V(g, 3).Hp;
        var damageBeforeSlash = E<DamageAppliedEvent>(g).Length; var actionsBeforeSlash = E<CardActionAcceptedEvent>(g).Length;
        Native(g, g.GetHumanLegalActions().First(a => a.CardId == black[2] && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]))); Play(g);
        Need(!V(g).HasAlcoholEffect && V(g, 3).Hp == peerHp - 2 &&
            E<AlcoholAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.DamageBonus == 1 && E<RuanJiWineIssuedEvent>(g).Any(w => w.UseId == e.ResolutionId)) == 1 &&
            E<CardActionAcceptedEvent>(g).Skip(actionsBeforeSlash).Any(e => e.Action is { ActorSeat: 0, Type: CardActionType.Use, EffectiveKind: CardKind.Slash } a &&
                a.TargetSeats.SequenceEqual([3]) && a.PhysicalCards is [{ CardId: var id }] && id == black[2]) &&
            E<DamageAppliedEvent>(g).Skip(damageBeforeSlash).ToArray() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2, Nature: DamageNature.Normal, SourceLess: false }],
            "The retained real Alcohol is consumed by the next actual native Slash and adds its ordinary one damage, rather than becoming a permanent or synthetic damage modifier." +
            JsonSerializer.Serialize(new { Before = peerHp, After = V(g, 3).Hp, V(g).HasAlcoholEffect,
                WineApplied = E<AlcoholAppliedEvent>(g).Where(e => e.SourceSeat == 0),
                Damage = E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == 0),
                RecentUses = E<CardActionAcceptedEvent>(g).TakeLast(4).Select(e => e.Action) }));
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The full accepted command journal replays frozen shown entities, per-name actual-turn quotas, red use draws, sober native Wine, drunk paid discard cold return and normal Alcohol consumption.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "ruan-ji", Hero, 2, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Club, 8), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.QinggangSword, Suit.Spade, 6)], [],
            [new(Peer(2)) { StartingHand = [new(CardKind.Slash, Suit.Spade, 8)] }, new(Peer(3)) { StartingHand = [new(CardKind.Slash, Suit.Club, 7)] }],
            [new(Peer(1)) { StartingHand = [] }], new string('f', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:ruan-ji-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.RuanJi is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static CardLocation Zone(GameEngine g, int card) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
    private static void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Ruan Ji native command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(pick);
        Need(choice is not null, "Missing Ruan Ji choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Activate(GameEngine g) => Answer(g, c => Key(c) == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
    private static void Draw(GameEngine g, SkillProgramEffectOp op) => Reach(g, p => p.SkillPrompt?.SkillId == Watch && Root(g)?.RuanJi?.Operation == op && Root(g)?.NativeDraw is not null);
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static LegalAction Conversion(GameEngine g, int card, CardKind kind, int? target = null) => g.GetHumanLegalActions().First(a =>
        a.CardId == card && a.PlayedCardKind == kind && a.ConversionSource?.SkillId == Zhaowen && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(g, c => c.Cards.Count == 0); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Ruan Ji boundary: " + JsonSerializer.Serialize(new { Prompt = p, Receipt = Root(g)?.RuanJi, Turn = g.State.TurnNumber, V(g).Hp, V(g).HandCount, V(g).HasAlcoholEffect,
                    BlackPhysicalUses = E<CardActionAcceptedEvent>(g).Where(e => e.Action.EffectiveIsRed == false && e.Action.PhysicalCards.Count > 0).Select(e => new { e.Action.ActionId, e.Action.ActorSeat, e.Action.EffectiveKind, e.Action.TargetSeats, Cards = e.Action.PhysicalCards.Select(c => c.CardId).ToArray() }).ToArray() }));
            }
            Need(g.State.Winner == Winner.None, "The bounded Ruan Ji scene must retain its native next boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Ruan Ji missed a bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Ruan Ji cold restore keeps the exact original Use, shown-entity authorization, native draw or already-paid discard and all viewer states."); return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-ruan-ji", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-real-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[{"id":"equip-peer","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"real-weapon"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"real-weapon"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","isSkillInvocation":false,"optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Ending}}","revision":1,"triggers":[{"id":"real-black-slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","isSkillInvocation":false,"optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"cardKinds":["slash"],"suits":["spade","club"],"resultBind":"black-material"},{"op":"useBoundCardsAsSlash","target":"owner","sourceBind":"black-material"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"native-draw","window":"nativeDrawStarting","subject":"owner","isSkillInvocation":false,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]},
                {"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["{{DiscardReason}}"],"movementOccurrence":"perBatch","isSkillInvocation":false,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-child","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "保留原实体", description = "保留这条真实主流程需要的实体手牌。" },
                [Quiet] = new { name = "安静同行", description = "跳过普通出牌。" }, [Ending] = new { name = "真实黑色来牌", description = "结束阶段使用原黑色手牌杀。" },
                [Watch] = new { name = "原生子窗", description = "暂停真实摸牌与已付弃牌子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new(Peer(seat), "真实来牌角色", "supporter", Quiet, "wei", 8, seat is 2 or 3 ? [Ending] : []));
            builder.AddDeck(new(Deck, "固定黑闪实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(i => new ContentDeckPhysicalCard("standard:dodge", i % 2 == 0 ? Suit.Spade : Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "阮籍真实昭文酒遁", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
