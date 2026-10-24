using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiLuYuShengChecks
{
    private const string Hero = "mobile:shi-lu-yu-sheng", Runwei = "mobile:shi-runwei", Shuanghuai = "mobile:shi-shuanghuai",
        Quiet = "fixture:shi-lu-yu-sheng-quiet", Peer = "fixture:shi-lu-yu-sheng-peer",
        SlashOwner = "fixture:shi-lu-yu-sheng-slash-owner", HandKeeper = "fixture:shi-lu-yu-sheng-keeper",
        Watch = "fixture:shi-lu-yu-sheng-watch", Extra = "fixture:shi-lu-yu-sheng-extra", Deck = "fixture:shi-lu-yu-sheng-deck";

    public static void RunweiGrantsColorDebtRefundAndExcludesGainedTargets()
    {
        var s = Scene.Start("runwei"); s.AutoEndPlay = false; s.SkipShuanghuaiOffers = true; s.Play();
        var peerHand = s.V(1).HandCount; var otherHand = s.V(2).HandCount;
        s.UseRunwei(3, 1, 2); s.Play();
        var first = s.E<MobileShiRunweiGrantIssuedEvent>().Single();
        s.Need(first is { RevealedCount: 3, GainedCount: 2 } && s.V(1).HandCount == peerHand + 2,
            "X is the two actually gained color-matching cards, rather than the three publicly revealed cards.");
        s.Slash(1); s.Play(); s.Need(s.E<MobileShiRunweiDebtRefundedEvent>().Length == 0, "Losing one card does not repay the frozen X=2.");
        s.Slash(1); s.Play(); s.Cold();
        s.Need(s.E<MobileShiRunweiDebtRefundedEvent>() is [{ Required: 2, Counted: 2 }], "Two genuine losses unlock exactly one repeat in this actual phase.");
        s.Offer(Runwei); s.Activate(); s.Answer(c => c.Parameters.GetValueOrDefault("count") == "1");
        s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "target").All(c => c.Targets is not [1]),
            "The unlocked repeat excludes the actual turn's earlier recipient.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets is [2]); s.Play();
        s.Slash(1); s.Play();
        s.Need(s.E<MobileShiRunweiDebtRefundedEvent>().Length == 1 && s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Runwei),
            "The second grant and another real loss cannot reset the once-per-phase refund flag.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 1 && s.E<ActualPhaseEndedEvent>().Any(e => e.Phase == first.Phase) && s.V(1).HandCount == peerHand + 2 && s.V(2).HandCount == otherHand + 1 &&
            !s.G.CardMovements.Any(m => m.Reason.Value == "skill-program.mobile:shi-runwei.phase-end"),
            "Foreign recipients retain all their granted hand cards at the first actual phase end, while an inserted Play resumes into a new normal Play in the same turn.");
        s.StockLossMaterials(); s.Play(); s.UseRunwei(3, 1, 2); s.Play(); s.DiscardTwo(); s.Play();
        var refunds = s.E<MobileShiRunweiDebtRefundedEvent>();
        s.Need(refunds.Length == 2 && refunds[0].Phase != refunds[1].Phase && refunds.All(e => e.ActualTurn == 1),
            "A genuinely new actual Play phase independently permits its one refund without resetting the turn's history.");
        s.Cold(); s.Replay();

        var own = Scene.Start("own"); own.AutoEndPlay = false; own.SkipShuanghuaiOffers = true; own.Play();
        own.UseRunwei(3, 0, 2, waitForGain: true); own.AtWatch();
        own.Need(own.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Runwei && f.MobileShiLuYuSheng?.Stage == 9),
            "The real own color group is paid on its original grant frame before its gain child finishes.");
        own.Cold(); own.ContinueWatch(); own.Play();
        var granted = own.E<MobileShiRunweiCardGrantedEvent>().Where(e => e.TargetSeat == 0).Select(e => e.CardId).ToArray();
        own.Slash(1, granted[0]); own.Play(); own.DropRunwei(); own.Play(); own.Cold(); own.End(); own.AtWatch();
        var payment = own.G.CardMovements.Where(m => m.Reason.Value == "skill-program.mobile:shi-runwei.phase-end").ToArray();
        own.Need(payment is [var only] && only.CardId == granted[1] && only.From == CardLocation.Hand(0) &&
            own.E<MobileShiRunweiPhaseEndDiscardedEvent>() is [{ DiscardedCount: 1, LeftHandCount: 1 }],
            "The issued phase-end obligation survives source loss and discards only the originally self-gained entity still in the owner's hand.");
        own.Cold(); own.ContinueWatch(); own.Reach(() => own.E<TurnStartedEvent>().Any(e => e.TurnNumber == 2)); own.Replay();
    }

    public static void ShuanghuaiPreventsDamageAndSettlesSameDifferentMemory()
    {
        var s = Scene.Start("shuanghuai"); s.Play(); s.SeedPeaches(); s.Play(); s.End();
        s.Offer(Shuanghuai); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => Key(c) == "mobile-shi-shuanghuai") == true); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "peach"); s.AtWatch();
        s.Need(s.E<MobileShiShuanghuaiGuardIssuedEvent>() is [{ VictimSeat: 1, Option: "peach", SameAsLast: false }] &&
            s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-shuanghuai.peach" && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(1)) == 1 &&
            s.E<MobileShiShuanghuaiHpPaidEvent>().Length == 0,
            "The first guard obtains a real discarded Peach and awaits its gain child before paying the different-target HP loss.");
        s.Cold(); s.ContinueWatch();
        s.Reach(() => s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1) && s.E<MobileShiShuanghuaiHpPaidEvent>().Length == 1);
        s.Need(s.V(1).Hp == 1 && s.V(0).Hp == 2, "Choosing the Peach does not prevent the original damage, and the first target costs one real owner HP.");
        s.Offer(Shuanghuai); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => Key(c) == "mobile-shi-shuanghuai") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "prevent"); s.AtWatch();
        s.Need(s.E<MobileShiShuanghuaiGuardIssuedEvent>() is [_, { VictimSeat: 1, Option: "prevent", SameAsLast: true }] &&
            s.E<MobileShiShuanghuaiDrawReturnedEvent>() is [{ RecipientSeat: 0, ActualCount: 1 }],
            "The next actual turn's same target prevents the damage and issues only the owner's first native draw before that draw's child returns.");
        s.Cold(); s.ContinueWatch(); s.AtWatch(); s.ContinueWatch();
        s.Reach(() => s.E<MobileShiShuanghuaiDrawReturnedEvent>().Length == 2 && !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Shuanghuai));
        s.Need(s.V(1).Hp == 1 && s.V(0).Hp == 2 && s.E<MobileShiShuanghuaiHpPaidEvent>().Length == 1 &&
            s.E<MobileShiShuanghuaiDrawReturnedEvent>().Select(e => e.RecipientSeat).SequenceEqual([0, 1]) &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 1) == 1,
            "The two separate native draw returns finish once, preserve the original damage prevention, and retain the last-target memory without another HP payment.");
        s.Cold(); s.Replay();
    }

    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public bool AutoEndPlay = true;
        public bool SkipShuanghuaiOffers = false;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Offer(string skill) => Reach(() => skill == Runwei
            ? P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
              (G.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == Runwei) ||
               P!.Choices.All(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
            : P is { } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
        public void Activate()
        {
            if (P is { Kind: DecisionKind.PlayCard })
                Accept0(new UseProgramSkillCommand(0, Runwei, "reveal-grant", [], [], G.Revision, P.PromptId));
            else
                Accept0(new AnswerPromptCommand(P!.PlayerSeat, P.PromptId, P.Choices.First(c => Key(c) == "activate").Id, G.Revision));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected native prompt choice must be published.");
            Accept0(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept0(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 900; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Shuanghuai && SkipShuanghuaiOffers &&
                        p.Choices.Any(c => Key(c) == "skip"))
                    { Answer(c => Key(c) == "skip"); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Runwei or Shuanghuai) &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Runwei or Shuanghuai) && p.Choices.Any(c => Key(c) == "skip"))
                    { Answer(c => Key(c) == "skip"); continue; }
                    if (AutoEndPlay && p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Lu Yusheng boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Lu Yusheng did not reach the expected bounded native boundary.");
        }
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Slash(int target, int? card = null)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && (card is null || a.CardId == card));
            Accept0(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void UseRunwei(int count, int recipient, int gained, bool waitForGain = false)
        {
            Offer(Runwei); Activate(); Answer(c => c.Parameters.GetValueOrDefault("count") == count.ToString());
            Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([recipient]));
            if (P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "color") == true)
                Answer(c => c.Parameters.GetValueOrDefault("branch") == "color" && c.Description.Contains($"（{gained} 张）"));
            if (!waitForGain) Play();
        }
        public void DropRunwei() => Accept0(new UseProgramSkillCommand(0, HandKeeper, "drop-runwei", [], [], G.Revision, P!.PromptId));
        public void StockLossMaterials() => Accept0(new UseProgramSkillCommand(0, HandKeeper, "loss-materials", [], [], G.Revision, P!.PromptId));
        public void DiscardTwo()
        {
            var cards = V(0).Hand.Take(2).Select(c => c.Id).ToArray();
            Need(cards.Length == 2, "The second actual Play phase has two genuine owned hand entities for its loss driver.");
            Accept0(new UseProgramSkillCommand(0, HandKeeper, "discard-two", cards, [], G.Revision, P!.PromptId));
        }
        public void SeedPeaches()
        { var cards = V(0).Hand.Where(c => c.Kind == CardKind.Peach).Select(c => c.Id).ToArray(); Need(cards.Length == 2, "The fixture provides exactly two real peaches to discard."); Accept0(new UseProgramSkillCommand(0, HandKeeper, "seed-peaches", cards, [], G.Revision, P!.PromptId)); }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact phase identity, paid groups, original cleanup obligations and native guard children."); }
        public void AcceptPlay(GameCommand command) => Accept0(command);
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the grants, guards and every state exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Lu Yusheng command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Grants = E<MobileShiRunweiGrantIssuedEvent>(), Refunds = E<MobileShiRunweiDebtRefundedEvent>(),
            PhaseEnds = E<MobileShiRunweiPhaseEndDiscardedEvent>(), Guards = E<MobileShiShuanghuaiGuardIssuedEvent>(),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening =
            [
                new(CardKind.Slash, Suit.Heart, 4), new(CardKind.Slash, Suit.Club, 5), new(CardKind.Slash, Suit.Spade, 6)
            ];
            ShanheStartingCard[] PeerHand(int seat) =>
                Enumerable.Range(0, nodeId == "shuanghuai" && seat == 1 ? 3 : 6).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3 + i)).ToArray();
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: nodeId == "shuanghuai" && seat == 1 ? 2 : 8,
                ExtraSkillIds: nodeId == "shuanghuai" && seat == 2 ? [Quiet, SlashOwner, Watch] : [Quiet, Watch]) { StartingHand = PeerHand(seat) };
            if (nodeId == "shuanghuai") opening = opening.Concat(new[] { new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3), new ShanheStartingCard(CardKind.Peach, Suit.Diamond, 4) }).ToArray();
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, 3, 0, 0, 0, 0, nodeId == "runwei" ? [HandKeeper, Watch, Extra] : [HandKeeper, Watch], opening, [],
                [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-lu-yu-sheng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{HandKeeper}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0},{"id":"real-loss-drivers","query":"slashLimit","operation":"add","value":10,"priority":0}],
                "activations":[{"id":"drop-runwei","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Runwei}}"],"sourceBind":"{{Quiet}}"}]},
                  {"id":"seed-peaches","minCards":2,"maxCards":2,"cardKinds":["peach"],"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"discardSelected","target":"owner","amount":2},{"op":"draw","target":"owner","amount":1}]},
                  {"id":"loss-materials","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":2}]},
                  {"id":"discard-two","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"discardSelected","target":"owner","amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile:shi-runwei.grant","skill-program.mobile:shi-shuanghuai.peach","skill-program.mobile:shi-shuanghuai.draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"cleanup-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile:shi-runwei.phase-end"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Extra}}","revision":1,"triggers":[{"id":"insert","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{SlashOwner}}","revision":1,"triggers":[{"id":"slash-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [HandKeeper] = new { name = HandKeeper, description = "保持真实损失驱动与手牌。" },
                [Watch] = new { name = "原生已支付子结算", description = "停在真实得牌、摸牌或阶段末弃牌之后。", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } },
                [Extra] = new { name = "真实额外出牌阶段", description = "一次真实插入出牌阶段。" },
                [SlashOwner] = new { name = SlashOwner, description = "回合开始对最少手牌角色使用虚拟杀。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 10));
            b.AddCard(new("fixture:shi-lu-yu-sheng-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddDeck(new(Deck, "固定真实双色杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard("fixture:shi-lu-yu-sheng-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势陆郁生润微霜怀实际检查", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
