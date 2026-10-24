using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiCaoZhenChecks
{
    private const string Hero = "mobile:shi-cao-zhen", Sifeng = "mobile:shi-sifeng", Driver = "fixture:shi-cao-zhen-driver", Quiet = "fixture:shi-cao-zhen-quiet",
        Watch = "fixture:shi-cao-zhen-watch", Spent = "fixture:shi-cao-zhen-spent", Peer = "fixture:shi-cao-zhen-peer", Deck = "fixture:shi-cao-zhen-deck", Mode = "fixture:shi-cao-zhen-mode";

    public static void NativeOrderedForecastColorHandDiscardAndObtainCold()
    {
        var s = Scene.Start(false); s.Place();
        var top = s.E<ShiCaoZTopTakenEvent>().OrderBy(e => e.Index).Select(e => e.CardId).ToArray();
        s.Need(top.Length == 3 && top.Distinct().Count() == 3 && top.All(id => s.G.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing)),
            "The current formal skill takes exactly three real top entities, with no invented draw or fourth card.");
        s.Need(Enumerable.Range(1, 3).All(viewer => !s.G.CreateSnapshot(viewer).PublicRevealedCards.Any(c => top.Contains(c.Id)) &&
            !(s.G.CreateSnapshot(viewer).PrivateRevealedCards ?? []).Any(c => top.Contains(c.Id))), "Only the original issuer sees the held top entities.");
        var order = new[] { top[2], top[0], top[1] };
        foreach (var id in order) { s.AtAssign(); s.Answer(c => Branch(c) == "assign" && c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([1])); }
        s.Play(1);
        var issuerPile = s.Pile(0, 1); var holderPile = s.Pile(1, 1);
        s.Need(issuerPile.Count == 3 && issuerPile.Entries!.Select(e => e.Card.Id).SequenceEqual(order) && holderPile.Count == 3 && holderPile.Entries is null &&
            s.V(1).Hand.All(c => !order.Contains(c.Id)), "The issuer's selected order is preserved while the holder sees count only, and no forecast becomes hand material.");
        s.Cold(); s.Native(CardKind.Crossbow);
        s.Reach(() => s.Root?.ShiCaoZhen?.Stage == ShiCaoZStage.Discard && s.P?.PlayerSeat == 1);
        var equipped = s.V(1).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Need(s.E<ShiCaoZUseIssuedEvent>().Single() is { Holder: 1, DifferentColor: true } first && first.PlacementId == s.E<ShiCaoZPlacedEvent>().Single(e => e.Material.CardId == order[0]).Material.PlacementId &&
            s.P!.Choices.All(c => c.Cards.Count == 1 && s.V(1).Hand.Any(h => h.Id == c.Cards[0]) && !c.Cards.Contains(equipped)),
            "A fully resolved native black equipment use consumes the selected red head; its owner must pay from hand, never its just-equipped weapon.");
        var before = s.V(1).HandCount; s.Cold(); s.Answer(c => c.Cards.Any(id => s.V(1).Hand.Single(h => h.Id == id).Kind == CardKind.Peach));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.E<ShiCaoZDiscardPaidEvent>().Single();
        s.Need(s.Root?.ShiCaoZhen?.Stage == ShiCaoZStage.DiscardChildren && s.V(1).HandCount == before - 1 && s.V(1).Equipment.Any(c => c.Id == equipped) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == paid.FrameId && e.ExecutorSeat == 1 && e.ActualCount == 1 && e.HandCount == 1) == 1,
            "The real hand-only logical discard pays once before its native observer child.");
        s.Cold(); s.ContinueWatch(); s.Play(1);
        s.Need(s.E<ShiCaoZDiscardPaidEvent>().Length == 1, "Cold resuming the paid hand child cannot charge again.");
        s.Native(CardKind.Alcohol); s.Play(1);
        s.Need(s.E<ShiCaoZUseIssuedEvent>().Length == 2 && !s.E<ShiCaoZUseIssuedEvent>().Last().DifferentColor && s.E<ShiCaoZDiscardPaidEvent>().Length == 1 &&
            s.Pile(0, 1).Entries!.Single().Card.Id == order[2], "A red native use consumes exactly the second red head without a hand payment.");
        var originalHand = s.V(0).HandCount; s.End(); s.AtEndChoice(); s.Answer(c => Branch(c) == "obtain"); s.Play(2);
        s.Need(s.V(0).HandCount == originalHand + 1 && s.V(0).Hand.Any(c => c.Id == order[2]) &&
            s.G.CardMovements.Any(m => m.CardId == order[2] && m.From.Zone == CardZoneKind.PrivatePersistentPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.mobile-shi-cao-zhen.obtain") &&
            s.E<ShiCaoZEndChoiceEvent>().Single() is { Obtain: true, Count: 1 }, "At the holder's real turn end, the original remaining entity is obtained directly and the ordered queue is empty.");
        s.Cold(); s.Replay();
    }

    public static void IssuedEndAfterLossThreeRecipientsAndRemoveDamageCold()
    {
        var s = Scene.Start(true); s.Place(); var top = s.E<ShiCaoZTopTakenEvent>().OrderBy(e => e.Index).Select(e => e.CardId).ToArray();
        for (var i = 0; i < 3; i++) { s.AtAssign(); s.Answer(c => Branch(c) == "assign" && c.Cards.SequenceEqual([top[i]]) && c.Targets.SequenceEqual([i + 1])); }
        s.Play(1);
        s.Need(Enumerable.Range(1, 3).All(i => s.Pile(i, i) is { Count: 1, Entries: null }) &&
            s.E<ProgramOwnerSkillsReplacedEvent>().Any(e => e.OwnerSeat == 0 && e.LostSkillIds.Contains(Sifeng)) && s.V(0).Skills!.All(skill => skill.Id != Sifeng),
            "All three other recipients are allowed by the current formal text; the issuer has actually lost Sifeng after placing its obligations.");
        var hp = s.V(1).Hp; s.End(); s.AtEndChoice(); s.Answer(c => Branch(c) == "damage");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var root = s.Root!;
        s.Need(root.ShiCaoZhen is { Stage: ShiCaoZStage.EndChildren, Damage: true, Materials.Count: 1 } && s.V(1).Hp == hp &&
            s.E<ShiCaoZRemovedEvent>().Count(e => e.FrameId == root.Id && e.CardId == top[0]) == 1 && s.E<ShiCaoZDamageIssuedEvent>().All(e => e.FrameId != root.Id) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().All(e => e.ParentFrameId != root.Id),
            "End settlement physically removes the remaining forecast before its damage; this rule removal never impersonates the holder's discard.");
        s.Cold(); s.ContinueWatch(); s.Play(2);
        s.Need(s.V(1).Hp == hp - 1 && s.E<ShiCaoZDamageIssuedEvent>().Count(e => e.FrameId == root.Id && e.Source == 0 && e.Target == 1) == 1 &&
            s.E<ShiCaoZRemovedEvent>().Count(e => e.FrameId == root.Id && e.CardId == top[0]) == 1 && s.E<ShiCaoZCompletedEvent>().Count(e => e.FrameId == root.Id) == 1,
            "The issued obligation survives genuine skill loss and a paid movement-child cold restore, then deals one original-source native damage exactly once.");
        s.Cold(); s.Replay();
    }

    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry, bool rescueSource)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShiCaoZhen is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public NamedAmbushPileSnapshot Pile(int viewer, int holder) => G.CreateSnapshot(viewer).Players.Single(p => p.Seat == holder).NamedAmbushPiles!.Single(p => p.SkillId == Sifeng);
        public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
        public void End() { var p = P!; Accept(new EndPlayPhaseCommand(p.PlayerSeat, G.Revision, p.PromptId)); }
        public void Place()
        {
            Play(0); End(); Reach(() => P?.SkillPrompt?.SkillId == Sifeng && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); AtAssign();
        }
        public void AtAssign() => Reach(() => P?.Choices.Any(c => Branch(c) == "assign") == true);
        public void AtEndChoice() => Reach(() => P?.Choices.Any(c => Branch(c) == "obtain") == true);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Native(CardKind kind)
        {
            var p = P!; var actor = p.PlayerSeat;
            var choice = p.Choices.FirstOrDefault(c => c.Cards is [var id] && c.Parameters.GetValueOrDefault("action") != "recast" &&
                !c.Parameters.ContainsKey("conversion-skill-id") && V(actor).Hand.Any(card => card.Id == id && card.Kind == kind));
            Need(choice is not null, "The actual holder's published Play prompt contains the intended real native hand use.");
            var played = choice!.Parameters.TryGetValue("played-card-kind", out var text) ? Enum.Parse<CardKind>(text) : (CardKind?)null;
            var targetCard = choice.Parameters.TryGetValue("target-card-id", out var cardText) ? int.Parse(cardText, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
            Accept(new PlayCardCommand(actor, choice.Cards.Single(), choice.Targets, G.Revision, p.PromptId, played, targetCard));
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The exact published native choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 220; step++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { ContinueWatch(); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (rescueSource && p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0 &&
                        p.Choices.Any(c => c.Cards.Any(id => V(0).Hand.Any(card => card.Id == id && card.Kind == CardKind.Peach))))
                    { Answer(c => c.Cards.Any(id => V(0).Hand.Any(card => card.Id == id && card.Kind == CardKind.Peach))); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Sifeng native boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short real-turn fixture remains live."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded native flow reaches its expected boundary.");
        }
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore retains issuer-private ordered entities and exact paid child state."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Accepted commands reproduce actual top entities, order, source loss and once-paid native settlement."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Sifeng command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Root?.ShiCaoZhen, Uses = E<ShiCaoZUseIssuedEvent>(), Ends = E<ShiCaoZEndIssuedEvent>() });
        public static Scene Start(bool loseSource)
        {
            var stock = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray();
            var probe = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(loseSource, stock)), loseSource);
            ShanheStartingCard[] hand = [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Alcohol, Suit.Heart, 3), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Peach, Suit.Heart, 5)];
            for (var seat = 1; seat < 4; seat++)
            {
                var ids = probe.V(seat).Hand.Select(c => c.Id).ToArray(); probe.Need(ids.Length == hand.Length, "The fixed native identity deal retains four real hand entities per holder.");
                for (var i = 0; i < ids.Length; i++) stock[ids[i] - 1] = new(probe.registry.Cards.Values.First(c => c.LegacyKind == hand[i].Kind).Id, hand[i].Suit, hand[i].Rank);
            }
            return Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(loseSource, stock)), loseSource);
        }
        private readonly ContentRegistry registry = registry;
        private static Scene Boot(ContentRegistry registry, bool rescueSource)
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry, rescueSource); scene.Accept(new StartGameCommand());
            for (var step = 0; step < 40; step++)
            {
                if (scene.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return scene;
                if (scene.P is { Kind: DecisionKind.SelectGeneral } p)
                { scene.Accept(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : Peer + p.PlayerSeat, game.Revision, p.PromptId)); continue; }
                if (scene.P is { Kind: DecisionKind.SelectFaction }) { scene.Answer(_ => true); continue; }
                scene.Need(scene.P is null, "The fixed identity opening has only its real native setup and deal boundaries."); scene.Accept(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("The fixed native identity scene did not reach its original Play phase.");
        }
    }
    private sealed class Fixture(bool loseSource, IReadOnlyList<ContentDeckPhysicalCard> stock) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-cao-zhen", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var loss = loseSource ? $$"""
                ,{"id":"lose-hp-after-placement","window":"afterTurnEnded","subject":"owner","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":5}]}
                ,{"id":"lose-sifeng-in-real-dying","window":"dyingEntering","subject":"owner","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Sifeng}}"],"sourceBind":"{{Spent}}"}]}
                """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}{{loss}}]},
              {"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-hand-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-cao-zhen.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"paid-removal-child","window":"cardsMoved","subject":"owner","sourceZones":["privatePersistentPile"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-cao-zhen.remove"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实回合与失源", description = "保留手牌，布置后可失去伺锋。" }, [Quiet] = new { name = "固定同桌", description = "保留固定实际手牌。" },
                    [Watch] = new { name = "已付款子结算", description = "在原生手牌弃置或伺锋移去后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Spent, "伺锋已失去", "已经签发的实体队列继续结算。"));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer + seat, "实际队列目标", "", Quiet, "wei", 10, [Watch]));
            b.AddDeck(new(Deck, "固定原生实体牌堆", 4, 2, []) { PhysicalCards = stock });
            b.AddMode(new(Mode, "势曹真真实有序暗牌与结算", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition definition) => b.AddGeneral(definition.Id == Hero ? definition with { AdditionalSkillIds = [.. definition.AdditionalSkillIds ?? [], Driver] } : definition);
        public void AddSkill(ContentSkillDefinition definition) => b.AddSkill(definition);
        public void AddCard(ContentCardDefinition definition) => b.AddCard(definition);
        public void AddDeck(ContentDeckRecipe definition) => b.AddDeck(definition);
        public void AddMode(ContentModeDefinition definition) => b.AddMode(definition);
    }
}
