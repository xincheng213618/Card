using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiChenZhiChecks
{
    private const string Hero = "mobile:shi-chen-zhi", Quanchong = "mobile:shi-quanchong", Renxing = "mobile:shi-renxing",
        Driver = "fixture:shi-chen-zhi-driver", Watch = "fixture:shi-chen-zhi-watch", Quiet = "fixture:shi-chen-zhi-quiet",
        Peer = "fixture:shi-chen-zhi-peer", Deck = "fixture:shi-chen-zhi-deck";

    public static void ExtraTurnDiscardChildrenDyingRecoveryAndSameRoundLimitCold()
    {
        var s = Scene.Start(1); s.Play();
        var round = s.E<RoundStartedEvent>().Last().RoundNumber;
        var materials = s.V(0).Hand.Select(c => c.Id).Concat(s.V(0).Equipment.Select(c => c.Id)).Order().ToArray();
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.Root!;
        s.Need(paid.MobileShiChenZhi is { Stage: MobileShiChenZhiStage.DiscardChildren } r && r.Cards.Order().SequenceEqual(materials) &&
            paid.PendingMovementContinuation is not null && s.V(0).HandCount == 0 && s.V(0).Equipment.Count == 0 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == paid.Id).ActualCount == materials.Length &&
            s.E<MobileShiChenZhiExtraGrantedEvent>().Length == 0,
            "All real HE cards are paid on the original Quanchong frame before its movement child; the extra-turn lease is not issued before children settle.");
        s.Cold(); s.ContinueWatch(); s.OfferRenxing(); s.Activate(); s.Branch("draw");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.E<MobileShiChenZhiExtraGrantedEvent>() is [{ IssuedTurn: 1 }] &&
            s.E<MobileShiChenZhiExtraEnteredEvent>() is [{ UpcomingTurn: 2, LostHp: true }] &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Quanchong && e.Amount == 1) == 1 &&
            s.V(0).Hand.Count(c => c.Kind == CardKind.Peach) == 2,
            "The first outside-phase discard offers Renxing on the paid ending, self plus current actor draws two, and only this issued extra turn loses HP at its real start.");
        s.Cold(); s.Answer(c => c.Cards.Count == 1 && s.V(0).Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach));
        s.Play();
        s.Need(s.G.State.TurnNumber == 2 && s.E<RoundStartedEvent>().Last().RoundNumber == round && s.V(0).Hp == 1 &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Quanchong) == 1 &&
            s.G.CardMovements.Where(m => m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-chen-zhi.discard")
                .Select(m => m.CardId).Order().SequenceEqual(materials),
            "Cold dying recovery returns the real extra-turn entry without repeating discard or HP loss and without advancing the current round.");
        s.End(); s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 3 && e.ActorSeat == 1));
        s.Need(s.E<MobileShiChenZhiExtraGrantedEvent>().Length == 1 && s.E<MobileShiChenZhiExtraEnteredEvent>().Length == 1,
            "Ending the extra turn cannot start another same-round Quanchong; the saved normal table position resumes at seat one.");
        s.Replay();

        var highest = Scene.Start(3, peerHp: 2); highest.Play(); highest.End(); highest.Play();
        highest.Need(highest.G.State.TurnNumber == 2 && highest.V(0).Hp == 3 &&
            highest.E<MobileShiChenZhiExtraEnteredEvent>() is [{ LostHp: false }] &&
            highest.E<ProgramSkillHpLostEvent>().All(e => e.SkillId != Quanchong),
            "A genuine extra turn still starts when Chen Zhi alone has the greatest HP, and that lease never invents a negative HP cost.");
    }

    public static void GlobalFirstDiscardAndUsedOrPlayedSlashTargetFilterCold()
    {
        var s = Scene.Start(3); s.Play();
        s.Native(CardKind.Slash, 1); s.Play(); s.Native(CardKind.Duel, 1);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); s.Play();
        s.Need(s.E<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 1, Type: CardActionType.Response, EffectiveKind: CardKind.Slash }) &&
            s.E<MobileShiChenZhiFirstDiscardEvent>().Length == 0,
            "Both a native used Slash and the opponent's genuinely played Duel Slash occur; their normal action cleanup is not an actual discard opportunity.");
        s.Driver("strip-one", [3]); s.OfferRenxing();
        var first = s.E<MobileShiChenZhiFirstDiscardEvent>().Single();
        s.Need(s.G.CardMovements.Single(m => m.Sequence == first.MovementSequence).From == CardLocation.Hand(3),
            "The first actual discard belongs to another player, so Renxing observes the entire table rather than only Chen Zhi's losses.");
        s.Activate(); s.Branch("discard");
        var targets = s.P!.Choices.SelectMany(c => c.Targets).Distinct().ToArray();
        s.Need(!targets.Contains(0) && !targets.Contains(1) && targets.Contains(2) && targets.Contains(3),
            "Both the actual user and the actual player of Slash are excluded from this turn's discard-target list, including pure Duel responses.");
        s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Need(s.P!.Choices.Count > 0 && s.P.Choices.All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")),
            "Selecting another player's Hand publishes only opaque legal slots, with no card identities.");
        s.Cold(); s.Answer(c => c.Parameters.ContainsKey("slot-index"));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.ContinueWatch(); s.Play();
        s.Need(s.E<MobileShiChenZhiRenxingCompletedEvent>() is [{ Branch: "discard", TargetSeat: 3 }] &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(3) && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "skill.mobile-shi-chen-zhi.discard") == 1,
            "The original paid native foreign discard resumes its child once; its own nested discard cannot create another first opportunity.");
        s.Driver("discard-own", []); s.Play();
        s.Need(s.E<MobileShiChenZhiRenxingCompletedEvent>().Length == 1 && s.E<MobileShiChenZhiFirstDiscardEvent>().Length == 1 &&
            s.RenxingOffersSkipped == 0 && s.RoundUses == 1,
            "Later own discards in the same turn neither reopen the already-consumed table-wide first opportunity nor debit another round use.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 2 && s.E<RoundStartedEvent>().Length == 1,
            "The real extra turn is a distinct first-discard opportunity but retains the same round budget.");
        s.Driver("discard-own", []); s.OfferRenxing(); s.Activate(); s.Branch("draw"); s.Play();
        s.Need(s.RoundUses == 2 && s.E<MobileShiChenZhiRenxingCompletedEvent>().Length == 2,
            "The extra turn's first actual discard consumes exactly the second same-round Renxing use.");
        s.End(); s.Play();
        s.Need(s.RenxingOffersSkipped == 0 && s.RoundUses == 0 &&
            s.E<MobileShiChenZhiFirstDiscardEvent>().Select(e => e.ActualTurn).Distinct().Count() >= 5,
            "All three intervening normal actors really discard, without offering a third same-round Renxing; only the following normal round resets its visible allowance.");
        s.Replay();

        var declined = Scene.Start(3); declined.Play(); declined.Driver("strip-one", [3]); declined.OfferRenxing();
        declined.Answer(c => Key(c) == "skip"); declined.Play(); declined.Driver("discard-own", []); declined.Play();
        declined.Need(declined.E<MobileShiChenZhiRenxingCompletedEvent>().Length == 0 &&
            declined.E<MobileShiChenZhiFirstDiscardEvent>().Length == 1 && declined.RenxingOffersSkipped == 0,
            "Declining the first opportunity cannot postpone Renxing to a later discard in that same actual turn.");
    }

    private static string? Key(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MobileShiChenZhi is not null);
        public int RenxingOffersSkipped { get; private set; }
        public int? RoundUses => V(0).SkillRuntimeStates!.Single(r => r.SkillId == Renxing).PublicRuleStates!
            .Single(r => r.StateId == "renxing-first-discard").Used;
        public void Native(CardKind kind, int target)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.Kind != LegalActionKind.UseProgramSkill && a.CardId is > 0 &&
                a.ConversionSource is null && V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && a.TargetSeats.SequenceEqual([target]));
            Need(action is not null, $"The prepared real {kind} must be legal against seat {target}.");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Driver(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, ShiChenZhiChecks.Driver, id, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void OfferRenxing() => Reach(() => P?.SkillPrompt?.SkillId == Renxing && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Branch(string branch)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Renxing && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch));
            Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
        }
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "The expected native choice must exist.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 250; n++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { ContinueWatch(); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip"))
                    { if (p.SkillPrompt?.SkillId == Renxing) RenxingOffersSkipped++; Answer(c => Key(c) == "skip"); continue; }
                    if (p.Choices.Any(c => Key(c) == "select-owned-cards")) { Answer(c => Key(c) == "select-owned-cards"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Chen Zhi boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded native scene retains living participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Chen Zhi missed the bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Original paid frame, table-wide first-discard fact and exact extra-turn lease cold-restore identically."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The actual extra turn, paid HE discard, HP loss, response facts and opaque foreign discard replay exactly."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Chen Zhi command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, Rounds = E<RoundStartedEvent>(), P, Receipt = Root?.MobileShiChenZhi,
            Firsts = E<MobileShiChenZhiFirstDiscardEvent>(), Grants = E<MobileShiChenZhiExtraGrantedEvent>(), Entries = E<MobileShiChenZhiExtraEnteredEvent>(),
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        public static Scene Start(int hp, int peerHp = 8)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(79, 1, "shi-chen-zhi-" + hp, Hero, hp, 0, 0, 0, 0,
                [ShiChenZhiChecks.Driver, Watch],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Duel, Suit.Club, 1), new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Dodge, Suit.Club, 5)],
                [new(CardKind.Crossbow, Suit.Club, 1)],
                [new(Peer, Hp: peerHp) { StartingHand = [new(CardKind.Slash, Suit.Spade, 8), new(CardKind.Dodge, Suit.Club, 2)] },
                 new(Peer, Hp: peerHp) { StartingHand = [new(CardKind.Peach, Suit.Heart, 3)] },
                 new(Peer, Hp: peerHp) { StartingHand = [new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Spade, 5)] }], [], new string('d', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-chen-zhi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"strip-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","zones":["hand"],"amount":1,"resultBind":"stripped"},{"op":"moveBoundCards","target":"owner","sourceBind":"stripped","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"discard-own","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":1,"resultBind":"discarded"},{"op":"moveBoundCards","target":"owner","sourceBind":"discarded","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-own-shi-discard","window":"discardPileReceived","subject":"owner","movementDiscardOnly":true,"discardOwnerScope":"own","movementReasons":["skill.mobile-shi-chen-zhi.discard"],"optional":false,"isSkillInvocation":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"paid-other-shi-discard","window":"discardPileReceived","subject":"owner","movementDiscardOnly":true,"discardOwnerScope":"other","movementReasons":["skill.mobile-shi-chen-zhi.discard"],"optional":false,"isSkillInvocation":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]},{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实弃牌驱动", description = "真实选择及原生弃牌。" },
                    [Watch] = new { name = "已支付原生弃牌", description = "保留原生子结算。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "保留实际回合并跳过出牌。" } } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "实际同桌角色", "", Quiet, "wei", 10));
            builder.AddDeck(new(Deck, "固定真实桃牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "势陈祗原生额外回合与首次弃牌", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
