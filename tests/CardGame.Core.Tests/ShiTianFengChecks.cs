using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiTianFengChecks
{
    private const string Hero = "mobile:shi-tian-feng", Ganggeng = "mobile:shi-ganggeng", Sijian = "mobile:shi-sijian",
        Driver = "fixture:shi-tian-feng-driver", Quiet = "fixture:shi-tian-feng-quiet", Peer = "fixture:shi-tian-feng-peer", Deck = "fixture:shi-tian-feng-deck";

    public static void GiftEndingTiesOpaqueDiscardAndNextRealDodgeCold()
    {
        var tied = Scene.Start(2, 3); tied.Play(); tied.GiveAll(); tied.SijianOffer(); tied.Activate(); tied.Effect("debt", 1); tied.Play();
        tied.Need(tied.V(1).HandCount == 3 && tied.V(2).HandCount == 3 && tied.E<MobileShiTianFengGiftEvent>() is [{ CardCount: 2, TargetSeat: 1 }],
            "At least two real hand cards transfer as one gift; both highest players are tied before the actual ending.");
        tied.End(); tied.Play();
        tied.Need(tied.E<MobileShiTianFengGiftSettledEvent>() is [{ TargetSeat: 1, Maximum: true }] && tied.V(0).HandCount == 1,
            "A tied maximum at the actual turn ending pays the native one-card draw once."); tied.Replay();

        var lower = Scene.Start(3, 4); lower.Play();
        var gifts = lower.V(0).Hand.Take(2).Select(c => c.Id).ToArray(); lower.Give(gifts); lower.Play(); lower.End();
        lower.Reach(() => lower.P?.SkillPrompt?.SkillId == Ganggeng && lower.P.Choices.Any(c => Branch(c) == "discard"));
        lower.Need(lower.P!.Choices.Where(c => Branch(c) == "discard").All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == "Hand"),
            "The nonmaximum branch offers only opaque foreign hand slots, with no hidden entity identities.");
        lower.Cold(); lower.Answer(c => Branch(c) == "discard"); lower.Play();
        lower.Need(lower.E<MobileShiTianFengGiftSettledEvent>() is [{ Maximum: false }] && lower.V(1).HandCount == 2 &&
            lower.G.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-tian-feng.discard") == 1,
            "The nonmaximum ending discards exactly one real target card, preserving the original delayed gift across cold recovery."); lower.Replay();

        var next = Scene.Start(2, 3, deckKind: CardKind.Slash); next.Play(); next.GiveAll(); next.SijianOffer(); next.Activate(); next.Effect("debt", 1); next.Play();
        next.Driver("draw-one"); next.Play(); next.Slash(1);
        next.Reach(() => next.P is { PlayerSeat: 1 } && next.P.SkillPrompt?.SkillId == Sijian && next.P.Choices.Any(c => Branch(c) == "discard"));
        next.Need(next.E<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 1, EffectiveKind: CardKind.Dodge, Type: CardActionType.Response }) &&
            next.E<MobileShiTianFengDebtClaimedEvent>() is [{ TargetSeat: 1 }],
            "The next used Dodge really claims the obligation before the original Slash completes; it is a use-qualified response.");
        next.Cold(); next.Answer(c => Branch(c) == "discard"); next.Play();
        next.Need(next.V(1).Hp == 8 && next.V(1).HandCount == 1 &&
            next.G.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-tian-feng.discard") == 1,
            "The defender's successful used Dodge remains effective and the original debt pays exactly one later owned discard after cold recovery."); next.Replay();
    }

    public static void SharedTurnCapIncreasingBackwaterAndDyingChoiceCold()
    {
        var s = Scene.Start(2, 3); s.Play(); s.GiveAll(); s.SijianOffer(); s.Activate(); s.Effect("backwater", 1); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(0).HandCount == 2 && s.E<MobileShiTianFengBackwaterEvent>() is [{ Ordinal: 1 }] &&
            s.E<MobileShiTianFengDebtIssuedEvent>() is [{ TargetSeat: 1 }],
            "First backwater executes both effects and then genuinely loses one HP, recording its once-paid first ordinal.");
        s.Driver("strip", s.V(0).Hand.Select(c => c.Id).ToArray()); s.SijianOffer(); s.Activate(); s.Effect("backwater", 2);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.V(0).Hp == 0 && s.E<MobileShiTianFengBackwaterEvent>().Select(e => e.Ordinal).SequenceEqual([1, 2]) &&
            s.E<ProgramSkillHpLostEvent>().Where(e => e.SkillId == Sijian).Select(e => e.Amount).SequenceEqual([1, 2]),
            "Second backwater is a genuine increasing two-HP loss after its two-card native draw, reaching the engine's real Dying child.");
        s.Cold(); s.Rescue(); s.Play();
        s.Need(s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Sijian) == 2 && s.V(0).Hp == 1 &&
            s.V(0).SkillRuntimeStates!.Single(r => r.SkillId == Sijian).PublicRuleStates!.Single(r => r.StateId == "sijian") is { Value: 2, Used: 2 },
            "Rescue returns to the paid original frame without repaying loss, and both last-hand/dying triggers share the same visible two-use turn quota.");
        s.Driver("strip", s.V(0).Hand.Select(c => c.Id).ToArray()); s.Play();
        s.Need(s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Sijian && e.BindingId is "last-hand" or "enter-dying") == 2,
            "A third last-hand occurrence and the paid second backwater's Dying entry cannot exceed the same two-use turn limit."); s.Replay();

        var dying = Scene.Start(1, 3, hp: 1); dying.Play(); dying.Driver("lose-one"); dying.SijianOffer(); dying.Activate();
        dying.Reach(() => dying.P?.SkillPrompt?.SkillId == Sijian && dying.P.Choices.Any(c => Branch(c) == "draw"));
        dying.Need(!dying.P!.Choices.Any(c => Branch(c) == "backwater") && dying.P.Choices.Any(c => Branch(c) == "debt"),
            "An actual entering-Dying invocation keeps both ordinary options and excludes backwater while a living player is Dying.");
        dying.Cold(); dying.Effect("draw"); dying.Reach(() => dying.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        dying.Rescue(); dying.Play();
        dying.Need(dying.V(0).Hp == 1 && dying.E<MobileShiTianFengBackwaterEvent>().Length == 0 && dying.V(0).HandCount == 2,
            "The ordinary draw option grants the current turn player two real rescue cards without issuing any hidden backwater payment."); dying.Replay();
    }

    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
    private static string? Key(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void GiveAll() => Give(V(0).Hand.Select(c => c.Id).ToArray());
        public void Give(int[] cards) => Accept(new UseProgramSkillCommand(0, Ganggeng, "gift-at-least-two", cards, [1], G.Revision, P!.PromptId));
        public void Driver(string activation, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, ShiTianFengChecks.Driver, activation, cards ?? [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void SijianOffer() => Reach(() => P?.SkillPrompt?.SkillId == Sijian && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Effect(string branch, int? target = null)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Sijian && P.Choices.Any(c => Branch(c) == branch && (target is null || c.Targets.SequenceEqual([target.Value]))));
            Answer(c => Branch(c) == branch && (target is null || c.Targets.SequenceEqual([target.Value])));
        }
        public void Rescue() => Answer(c => c.Cards.Count == 1 && V(0).Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach));
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected original Shi Tian Feng choice must exist."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 220; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Tian Feng boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded native scene retains its actual participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Tian Feng missed its bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery retains original gift/debt identities, paid HP child, current choices and zones without any repeated cost."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The real command journal reproduces the delayed gift, actual used Dodge debt and increasing paid backwater branches exactly."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Shi Tian Feng command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        public static Scene Start(int handCount, int largestPeerHand, int hp = 3, CardKind deckKind = CardKind.Peach)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(deckKind));
            var opening = Enumerable.Range(0, handCount).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i + 1)).ToArray();
            var setup = new ShanheBattleSetup(623, 1, "shi-tian-feng-native", Hero, hp, 0, 0, 0, 0, [ShiTianFengChecks.Driver], opening, [],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 2)] },
                 new(Peer, Hp: 8) { StartingHand = Enumerable.Range(0, largestPeerHand).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Diamond, i + 1)).ToArray() },
                 new(Peer, Hp: 8) { StartingHand = [] }], [], new string('d', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(CardKind deckKind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-tian-feng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-hand","query":"handLimit","operation":"add","value":20,"priority":0}],
               "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
               "activations":[
                 {"id":"draw-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
                 {"id":"strip","minCards":1,"maxCards":null,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"strip-cards"},{"op":"moveBoundCards","target":"owner","sourceBind":"strip-cards","destination":"discardPile","awaitMovementTriggers":true}]},
                 {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}
               ]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实手牌及体力驱动", description = "真实摸牌、弃牌和体力子结算。" },
                [Quiet] = new { name = "安静同桌", description = "保留实际回合和正常响应。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "实际同桌角色", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(deckKind == CardKind.Slash ? "standard:slash" : "standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势田丰原生赠牌背水与下一用牌债", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
