using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheStartingResourceChecks
{
    private const string Driver = "fixture:starting-resource-driver", UseWatch = "fixture:starting-resource-use-watch", ResponseWatch = "fixture:starting-resource-response-watch";
    private const string BoardWatch = "fixture:starting-resource-board-watch";
    private const string Owner = "fixture:starting-resource-owner", Enemy = "fixture:starting-resource-enemy", Deck = "fixture:starting-resource-deck";

    public static void NewResourcesUseConversionDodgeAndEquipmentExitCold()
    {
        var s = Scene.Start(true); s.Play();
        var gear = s.Initial(true, 0); var draw = s.Initial(false, 0); var converted = s.Initial(false, 1); var dodge = s.Initial(false, 2);
        var recast = s.Initial(false, 3); var discard = s.Initial(false, 4); var delayed = s.Initial(false, 5);
        Need(s.V().Equipment.Count == 0 && s.V().Hand.Any(c => c.Id == gear) && s.V(1).Hand.Count == 1 &&
            s.G.CreateSnapshot(1).Players[0].Hand.Count == 0 && !SnapshotJson.Serialize(s.G.CreateSnapshot(1)).Contains(nameof(ShanheStartingResourceCreatedEvent), StringComparison.Ordinal),
            "New-contract resources start as real private hand entities, including the supplied equipment; enemy fixed hand stays separate from provenance facts.");
        s.Card(draw, []); s.Until(() => s.At(UseWatch));
        Need(s.Zone(draw) == CardLocation.OutsideGame && s.E<ShanheStartingResourceDestroyedEvent>().Single().CardId == draw &&
            s.G.CardMovements.Count(m => m.CardId == draw && m.From == CardLocation.Processing && m.To == CardLocation.OutsideGame && m.Reason == CardMoveReasons.UseFinished) == 1,
            "A real Draw Two ends through its exact Processing-to-Outside movement, with no intermediate reusable discard entity.");
        s.Cold(); s.Continue(); s.Play();
        s.Card(converted, [1], conversion: true); s.Until(() => s.At(UseWatch));
        Need(s.Zone(converted) == CardLocation.OutsideGame && s.E<CardActionAcceptedEvent>().Any(e => e.Action.EffectiveKind == CardKind.Slash &&
            e.Action.PhysicalCards.Any(c => c.CardId == converted) && e.Action.ConversionChain.Any(c => c.SkillId == "classic:longdan")),
            "An actual Longdan Slash consumes the starting Dodge material under its original accepted conversion, rather than only handling printed card kinds.");
        s.Cold(); s.Continue(); s.Play();
        var normal = s.V().Hand.First(c => c.Kind == CardKind.Dodge && !s.E<ShanheStartingResourceCreatedEvent>().Any(e => e.CardId == c.Id)).Id;
        s.Card(normal, [1], conversion: true); s.Play();
        Need(s.Zone(normal) == CardLocation.DiscardPile, "A naturally drawn card with the same printed kind and conversion follows the ordinary discard cycle.");
        s.Card(gear, []); s.Play(); Need(s.Zone(gear) == CardLocation.Equipment(0), "The supplied equipment installs through a genuine native equipment Use and remains on the field.");
        s.Use("equip", [], [0]); s.Play(); Need(s.Zone(gear) == CardLocation.OutsideGame && s.E<ShanheStartingResourceDestroyedEvent>().Count(e => e.CardId == gear) == 1,
            "Replacing a genuinely used supplied equipment card moves only that exact old equipment entity outside."); s.Cold();
        var armor = s.Initial(true, 1); s.Card(armor, []); s.Play(); s.Use("discard-board");
        s.Until(() => s.P!.Choices.Any(c => c.Cards.SequenceEqual([armor]))); s.Answer(c => c.Cards.SequenceEqual([armor])); s.Until(() => s.At(BoardWatch));
        Need(s.Zone(armor) == CardLocation.OutsideGame && s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == BoardWatch) == 1,
            "Discarding a genuinely used initial armor remains one real discard-only CardsLost occurrence with its original Equipment source despite the final Outside destination.");
        s.Cold(); s.Continue(); s.Play();
        s.Use("discard", [discard]); s.Play(); s.Accept(new RecastCardCommand(0, recast, s.G.Revision, s.P!.PromptId)); s.Play();
        Need(s.Zone(discard) == CardLocation.DiscardPile && s.Zone(recast) == CardLocation.DiscardPile &&
            s.E<ShanheStartingResourceDestroyedEvent>().All(e => e.CardId != discard && e.CardId != recast),
            "An unused initial card paid as an ordinary discard cost and a recast initial Iron Chain remain ordinary discard entities.");
        s.Card(delayed, [0]); s.Play(); Need(s.Zone(delayed) == CardLocation.Judgment(0), "A real supplied delayed trick retains its exact field placement until resolution.");
        s.End(); s.Until(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.SequenceEqual([dodge])); s.Until(() => s.At(ResponseWatch));
        var response = s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ResponseCompletion is not null);
        Need(s.Zone(dodge) == CardLocation.OutsideGame && response.ResponseCompletion!.NativeCosts.Count(m => m.CardId == dodge) == 2 &&
            response.ResponseCompletion.NativeCosts.Any(m => m.CardId == dodge && m.From == CardLocation.Processing && m.To == CardLocation.OutsideGame && m.Reason == CardMoveReasons.ResponseFinished),
            "A native Dodge response keeps the exact entered and destroyed payment movements on its original response-completion owner.");
        s.Cold(); s.Continue(); s.Play(); Need(s.Zone(delayed) == CardLocation.OutsideGame && s.E<ShanheStartingResourceDestroyedEvent>().Count(e => e.CardId == dodge) == 1,
            "The paid response tail returns after cold recovery without destroying or paying the same material twice.");
    }

    public static void ClaimedResourcesAndLegacyRecipeRetainTheirPhysicalCards()
    {
        var s = Scene.Start(true, claim: true); s.Play(); var slash = s.Initial(false, 0);
        s.Card(slash, [1]); s.Until(() => s.At(UseWatch));
        Need(s.Zone(slash) == CardLocation.Hand(1) && s.E<ShanheStartingResourceDestroyedEvent>().All(e => e.CardId != slash) &&
            s.G.CardMovements.Any(m => m.CardId == slash && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.classic:jianxiong.ClaimDamageCards") &&
            s.E<ProgramDamageCardsClaimedEvent>().Single(e => e.SkillId == "classic:jianxiong").CardIds.SequenceEqual([slash]),
            "Genuine Jianxiong takes the original used entity before cleanup, retaining the card rather than receiving a generated substitute or a destroyed card.");
        s.Cold(); s.Continue(); s.Play();
        var originalSlot = s.V(1).Hand.Select((card, index) => (card.Id, Index: index)).Single(c => c.Id == slash).Index;
        s.Use("take", [], [1]); s.Until(() => s.P!.Choices.Any(c => c.Parameters.ContainsKey("slot-index")));
        Need(s.P!.Choices.All(c => c.Cards.Count == 0), "The genuine foreign-hand selection publishes opaque slots rather than private original entity IDs.");
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand" &&
            c.Parameters.GetValueOrDefault("slot-index") == originalSlot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        s.Play(); s.End(); s.Until(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.SequenceEqual([slash])); s.Until(() => s.At(ResponseWatch));
        Need(s.Zone(slash) == CardLocation.DiscardPile && s.E<ShanheStartingResourceDestroyedEvent>().All(e => e.CardId != slash) &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge &&
                e.Action.PhysicalCards.Any(c => c.CardId == slash) && e.Action.ConversionChain.Any(c => c.SkillId == "classic:longdan")),
            "After a real skill acquisition and real return, that same Slash can supply Longdan Dodge and enters the ordinary discard cycle permanently.");
        s.Cold(); s.Continue(); s.Play();

        var old = Scene.Start(false); old.Play(); var oldGear = old.V().Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        var oldDraw = old.V().Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        Need(old.E<ShanheStartingResourceCreatedEvent>().Length == 0 && old.Zone(oldGear) == CardLocation.Equipment(0),
            "The absent/false contract keeps legacy direct starting equipment placement and emits no new provenance.");
        old.Card(oldDraw, []); old.Until(() => old.At(UseWatch)); Need(old.Zone(oldDraw) == CardLocation.DiscardPile && old.E<ShanheStartingResourceDestroyedEvent>().Length == 0,
            "Old false recipes preserve their original physical use/discard lifecycle without new exits."); old.Cold(); old.Continue(); old.Play();
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() where T : IGameEvent => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Initial(bool equipment, int index) => E<ShanheStartingResourceCreatedEvent>().Single(e => e.EquipmentResource == equipment && e.RecipeIndex == index).CardId;
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public bool At(string skill) => P?.SkillPrompt?.SkillId == skill;
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected resource command."); }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Card(int id, IReadOnlyList<int> targets, bool conversion = false)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && (a.ConversionSource is not null) == conversion && a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Until(Func<bool> predicate)
        {
            for (var i = 0; i < 200; i++)
            {
                if (predicate()) return;
                if (P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(c => c.Cards.Count == 0);
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Starting resource boundary absent: " + JsonSerializer.Serialize(new { P, Frames = G.ResolutionStack }));
        }
        public void Cold()
        {
            string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))).ToArray(),
                Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold replay retains the exact origin entities, native payment/cleanup/response frame, all views and movement prefix."); G = restored;
        }
        public static Scene Start(bool contract, bool claim = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(claim));
            ShanheStartingCard[] cards = claim ? [new(CardKind.Slash, Suit.Heart, 9)] :
                [new(CardKind.DrawTwo, Suit.Heart, 1), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Spade, 3), new(CardKind.IronChain, Suit.Club, 4), new(CardKind.Peach, Suit.Heart, 5), new(CardKind.Lightning, Suit.Heart, 6)];
            var setup = new ShanheBattleSetup(83, 1, "fixture:starting-resources", Owner, 20, 0, 0, 0, 0, [], cards,
                claim ? [] : [new(CardKind.Crossbow, Suit.Spade, 1), new(CardKind.BaguaFormation, Suit.Club, 2)], [new(Enemy) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7)] }], [], new string('a', 64))
            { StartingResourcesExpireOnUse = contract };
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(bool claim) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-starting-resources", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
              {"schemaVersion":0,"skills":[
                {"id":"fixture:starting-resource-driver","revision":1,"modifiers":[{"id":"slashes","query":"slashLimit","operation":"add","value":8,"priority":0},{"id":"hands","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                  {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                  {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                  {"id":"discard-board","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                  {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand"}]}]},
                {"id":"fixture:starting-resource-use-watch","revision":1,"triggers":[{"id":"native-use-tail","window":"cardUseCompleted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"fixture:starting-resource-response-watch","revision":1,"triggers":[{"id":"native-response-tail","window":"cardResponseCompleted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ,{"id":"fixture:starting-resource-board-watch","revision":1,"triggers":[{"id":"real-discard","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.fixture:starting-resource-driver.SelectAndMoveOwnedCard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
              ]}
              """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var names = new[] { Driver, UseWatch, ResponseWatch, BoardWatch }.ToDictionary(id => id, id => new { name = id, description = "真实初始实体生命周期", optionLabels = id == Driver ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实实体夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:starting-resource-noop", "固定敌军", "无额外能力"));
            b.AddGeneral(new(Owner, "初始资源拥有者", "supporter", Driver, "shu", 20, ["classic:longdan", UseWatch, ResponseWatch, BoardWatch]));
            b.AddGeneral(new(Enemy, "真实敌方", "supporter", claim ? "classic:jianxiong" : "fixture:starting-resource-noop", "wei", 20));
            var standard = ContentRegistry.Build(new StandardContentPackage());
            foreach (var kind in new[] { CardKind.IronChain, CardKind.Indulgence, CardKind.Crossbow })
                if (standard.Cards.Values.All(c => c.LegacyKind != kind)) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:starting-resource-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            b.AddDeck(new(Deck, "普通真实实体对照", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i =>
                new ContentDeckPhysicalCard(i % 10 == 0 ? "standard:crossbow" : "standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "初始实体真实生命周期", 2, 2, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
