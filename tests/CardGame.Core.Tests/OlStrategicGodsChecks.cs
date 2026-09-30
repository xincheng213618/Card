using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OlStrategicGodsChecks
{
    private const string Driver = "fixture:strategic-driver";
    private const string Mode = "identity:strategic-fixture";
    public static void LongnuChainAuraAndReplay()
    {
        var (game, registry) = Create("liu");
        var player = game.CreateSnapshot(0, true).Players[0];
        Require(player.IsChained && player.Hp == player.MaxHp - 1, "Longnu must pay one HP and Jieying must chain its owner at game start.");
        Use(game, "unchain");
        Require(game.CreateSnapshot(0, true).Players[0].IsChained &&
            game.Events.Select(item => item.Payload).OfType<ProgramChainedStateSetEvent>().Last().IsChained,
            "Forced chaining must remain active and publish the actual chained state after an unchain request.");
        EqualReplay(game, registry);
        Use(game, "draw");
        var red = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => card.Suit is Suit.Heart or Suit.Diamond).ToArray();
        Require(red.Length > 0 && red.All(card => game.GetHumanLegalActions().Any(action => action.CardId == card.Id && action.PlayedCardKind == CardKind.FireSlash && action.ConversionSource?.SkillId == "ol:longnu")), "Yang Longnu must forcibly offer every red hand card as a fire Slash.");
        EqualReplay(game, registry);
        RespondWithLongnu(game, registry, "red-hand-fire-slash");
        var previousMax = player.MaxHp;
        NextTurn(game);
        Require(game.CreateSnapshot(0, true).Players[0].MaxHp == previousMax - 1, "The next Longnu polarity must reduce maximum HP.");
        Require(game.GetHumanLegalActions().Any(action => action.PlayedCardKind == CardKind.ThunderSlash && action.ConversionSource?.SkillId == "ol:longnu"), "Yin Longnu must turn tricks into thunder Slashes.");
        EqualReplay(game, registry);
        RespondWithLongnu(game, registry, "trick-hand-thunder-slash");
    }
    private static void RespondWithLongnu(GameEngine game, ContentRegistry registry, string identity)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, Driver, "ordered-duel", [], [1, 0], game.Revision, game.PendingDecision!.PromptId)));
        AdvanceUntil(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("conversion-binding-id") == identity) == true);
        var response = game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("conversion-binding-id") == identity);
        var physical = game.CreateSnapshot(0, true).Players[0].Hand.Single(card => card.Id == response.Cards.Single());
        EqualReplay(game, registry);
        Answer(game, response);
        Drain(game);
        var action = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Last(item => item.Action.PhysicalCards.Any(card => card.CardId == physical.Id)).Action;
        Require(action.Type == CardActionType.Response && action.EffectiveKind == CardKind.Slash && action.PhysicalCards.Single().CardKind == physical.Kind &&
            action.ConversionChain.Any(source => source.SkillId == "ol:longnu" && source.BindingId == identity),
            "An elemental Longnu identity must satisfy a real Slash response while preserving the physical card cost and conversion source.");
        var zones = typeof(GameEngine).GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        var discarded = (IReadOnlyList<Card>)zones.GetType().GetMethod("CardsAt")!.Invoke(zones, new object[] { CardLocation.DiscardPile })!;
        Require(discarded.Any(card => card.Id == physical.Id && card.Kind == physical.Kind && card.Suit == physical.Suit && card.Rank == physical.Rank),
            "The Slash response must discard the same physical card without changing its printed suit, rank or kind.");
        EqualReplay(game, registry);
    }
    public static void PoxiCampAndPrivateReplay()
    {
        var (game, registry) = Create("gan");
        Require(game.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Camp) == true, "Jieying must provide a camp when the table has none.");
        Use(game, "draw");
        var prompt = game.PendingDecision!;
        Accept(game.Submit(new UseProgramSkillCommand(0, "ol:poxi", "four-suit-discard", [], [1], game.Revision, prompt.PromptId)));
        Require(game.PendingDecision is { IsPrivate: true, Kind: DecisionKind.ProgramTrigger }, "Poxi must privately reveal the inspected hands only to its owner.");
        var observer = game.CreateSnapshot(2);
        Require(observer.PendingDecision is null && observer.Players[0].Hand.Count == 0 && observer.Players[1].Hand.Count == 0 && observer.PublicRevealedCards.Count == 0,
            "A Poxi observer must not receive private choices or either inspected hand.");
        EqualReplay(game, registry);

        foreach (var mine in new[] { 0, 1, 3 })
        {
            var (branch, branchRegistry) = Create("gan");
            Use(branch, "draw");
            Use(branch, "draw-opponent");
            var before = branch.CreateSnapshot(0, true).Players[0];
            Accept(branch.Submit(new UseProgramSkillCommand(0, "ol:poxi", "four-suit-discard", [], [1], branch.Revision, branch.PendingDecision!.PromptId)));
            for (var i = 0; i < 4; i++)
            {
                var seat = i < mine ? 0 : 1;
                Answer(branch, branch.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1 &&
                    branch.CreateSnapshot(0, true).Players[seat].Hand.Any(card => card.Id == choice.Cards[0])));
                if (i < 3) EqualReplay(branch, branchRegistry);
            }
            if (mine == 1)
            {
                AdvanceUntil(branch, () => branch.State.Phase != TurnPhase.Play || branch.State.CurrentSeat != 0);
                Require(branch.State.Phase != TurnPhase.Play || branch.State.CurrentSeat != 0, "Poxi's one-card branch must end the current play phase.");
            }
            else Drain(branch);
            var after = branch.CreateSnapshot(0, true).Players[0];
            Require(mine != 0 || after.MaxHp == before.MaxHp - 1, "Poxi's zero-card branch must lose one maximum HP.");
            Require(mine != 3 || after.Hp == Math.Min(before.MaxHp, before.Hp + 1), "Poxi's three-card branch must recover one HP.");
            EqualReplay(branch, branchRegistry);
        }

        var (camp, campRegistry) = Create("gan");
        Accept(camp.Submit(new EndPlayPhaseCommand(0, camp.Revision, camp.PendingDecision!.PromptId)));
        AdvanceUntil(camp, () => camp.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "give-camp") == true);
        Answer(camp, camp.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        AdvanceUntil(camp, () => camp.PendingDecision?.Choices.Any(choice => choice.Targets.SequenceEqual(new[] { 1 })) == true);
        Answer(camp, camp.PendingDecision!.Choices.First(choice => choice.Targets.SequenceEqual(new[] { 1 })));
        AdvanceUntil(camp, () => camp.CreateSnapshot(0, true).Players[1].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Camp) == true);
        EqualReplay(camp, campRegistry);
        Require(camp.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Camp) != true, "Camp transfer must remove the donor's mark.");
        AdvanceUntil(camp, () => camp.CardMovements.Any(move => move.Reason.Value.Contains("ClaimMarkedHand") && move.From == CardLocation.Hand(1) && move.To == CardLocation.Hand(0)));
        Require(camp.CreateSnapshot(0, true).Players[1].HandCount == 0 && camp.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Camp) == true,
            "After the marked opponent's turn, its remaining hand and attributed camp must return to the skill owner.");
        EqualReplay(camp, campRegistry);
        for (var i = 0; i < 4; i++)
        {
            var choice = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1 &&
                game.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == choice.Cards[0]));
            Answer(game, choice);
            if (i < 3) EqualReplay(game, registry);
        }
        Drain(game);
        Require(game.CardMovements.Count(move => move.Reason.Value.Contains("ApplyHandDiscardShare") && move.To == CardLocation.DiscardPile) == 4, "Poxi must discard exactly four distinct suits, then resolve the owner's four-card draw outcome.");
        EqualReplay(game, registry);
    }
    public static void JunlueZhanhuoAndSuppression()
    {
        var (game, registry) = Create("lu");
        Use(game, "damage");
        Require(game.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Junlue && marker.Count == 1) == true, "Junlue must award one mark for each damage point caused.");
        Use(game, "markers");
        EqualReplay(game, registry);
        Accept(game.Submit(new UseProgramSkillCommand(0, "ol:zhanhuo", "limited-fire", [], [], game.Revision, game.PendingDecision!.PromptId)));
        Require(game.CreateSnapshot(0, true).Players[0].Markers?.All(marker => marker.Kind != PlayerMarkerKind.Junlue) != false, "Zhanhuo must remove all military marks when activated.");
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.Count == 1));
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("choice") == "finish"));
        Drain(game);
        EqualReplay(game, registry);

        var (liao, liaoRegistry) = Create("liao");
        Use(liao, "damage", drain: false);
        for (var i = 0; i < 60 && liao.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice") == "ol:zhiti") != true; i++) Step(liao, activate: true);
        Require(liao.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice") == "ol:zhiti") == true, "Duorui must allow choosing a target's general skill.");
        EqualReplay(liao, liaoRegistry);
        Answer(liao, liao.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("choice") == "ol:zhiti"));
        Require(liao.Events.Any(item => item.Payload is ProgramSkillSuppressedEvent { TargetSeat: 1, SkillId: "ol:zhiti", Suppressed: true }), "Duorui must suppress the selected skill.");
        EqualReplay(liao, liaoRegistry);
        Drain(liao);
        Require(liao.Events.Any(item => item.Payload is ProgramSkillSuppressedEvent { TargetSeat: 1, SkillId: "ol:zhiti", Suppressed: false }), "Duorui must expire when the affected player's next turn finishes.");
        EqualReplay(liao, liaoRegistry);

        var (cuike, cuikeRegistry) = Create("lu");
        Use(cuike, "markers");
        Accept(cuike.Submit(new EndPlayPhaseCommand(0, cuike.Revision, cuike.PendingDecision!.PromptId)));
        AdvanceUntil(cuike, () => cuike.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "mass-damage") == true);
        Answer(cuike, cuike.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Drain(cuike);
        Require(cuike.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Where(damage => damage.SourceSeat == 0).Select(damage => damage.TargetSeat).Order().SequenceEqual(new[] { 1, 2, 3 }),
            "Cuike must resolve one complete damage window for every other living player.");
        Require(cuike.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Junlue && marker.Count == 3) == true,
            "After clearing military marks, each new mass-damage point must award a new mark.");
        EqualReplay(cuike, cuikeRegistry);

        var (marks, marksRegistry) = Create("marks");
        Use(marks, "add-source");
        Require(marks.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Ren && marker.Count == 8) == true,
            "Adding one marker source must preserve three other attributed sources.");
        Use(marks, "pay-sources");
        Use(marks, "pay-sources");
        var payments = marks.Events.Select(item => item.Payload).OfType<PlayerMarkerChangedEvent>().Where(item => item.Reason.EndsWith("pay-sources.cost", StringComparison.Ordinal)).ToArray();
        Require(payments.Select(item => (item.SkillOwnerSeat, item.Delta)).SequenceEqual(new (int?, int)[] { (0, -2), (1, -1), (1, -1), (2, -2) }) &&
            marks.CreateSnapshot(0, true).Players[0].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Ren && marker.Count == 2) == true,
            "Marker costs must consume stable attributed sources and keep aggregate totals equal to the remaining sources.");
        Require(!marks.GetHumanLegalActions().Any(action => action.ProgramActivationId == "pay-sources"), "An unaffordable multi-source marker cost must disappear from legal actions.");
        EqualReplay(marks, marksRegistry);
    }
    private static (GameEngine Game, ContentRegistry Registry) Create(string flavor)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(flavor));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:strategic-owner", game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        return (game, registry);
    }
    private static void Use(GameEngine game, string id, bool drain = true)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, Driver, id, [], [], game.Revision, game.PendingDecision!.PromptId)));
        if (drain) Drain(game);
    }
    private static void Drain(GameEngine game)
    {
        for (var i = 0; i < 1000; i++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0) return;
            Step(game);
        }
        throw new InvalidOperationException("Strategic fixture did not return to owner play.");
    }
    private static void Step(GameEngine game, bool activate = false)
    {
        if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        if (prompt.Kind == DecisionKind.PlayCard) { Accept(game.Submit(new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId))); return; }
        var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == (activate ? "activate" : "skip")) ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("choice") == "finish") ?? prompt.Choices.First();
        Answer(game, choice);
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void AdvanceUntil(GameEngine game, Func<bool> complete)
    {
        for (var i = 0; i < 1000; i++) { if (complete()) return; Step(game); }
        throw new InvalidOperationException("Strategic fixture did not reach its expected boundary.");
    }
    private static void NextTurn(GameEngine game) { Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId))); Drain(game); }
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Strategic state must survive checkpoint replay.");
        Require(game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Strategic events must replay deterministically.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Strategic command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string flavor) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("strategic-fixture", new Version(1, 0, 0), [new PackageDependency("standard", new Version(1, 0, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string suffix) { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-strategic-gods." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var (id, program) in catalog.Programs)
            {
                var presentation = catalog.Presentations[id];
                builder.AddSkill(new ContentSkillDefinition(id, presentation.Name, presentation.Description) { Program = program, ProgramPresentation = presentation });
            }
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:strategic-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
                {"id":"unchain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":false}]},
                {"id":"draw-opponent","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"draw","target":"selectedTarget","amount":20},{"op":"draw","target":"selectedTarget","amount":20}]},
                {"id":"add-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"ren","amount":2}]},
                {"id":"pay-sources","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"markerCost":{"marker":"ren","amount":3},"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"ordered-duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"markers","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"junlue","amount":8},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"setChainedState","target":"selectedTarget","chained":true}]}]}]}
                """,
                """{"schemaVersion":3,"skills":{"fixture:strategic-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = driver.Programs[Driver] });
            var donor = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:marker-donor","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"donate","window":"gameStarting","subject":"owner","optional":false,"priority":0,"condition":{"kind":"always"},"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"setMarkerAmount","target":"selectedTarget","marker":"ren","amount":2}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:marker-donor":{"name":"Donor","description":"Donor"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:marker-donor", "Donor", "Donor") { Program = donor.Programs["fixture:marker-donor"] });
            string[] skills = flavor switch { "liu" => ["ol:longnu", "ol:shen-liu-bei-jieying"], "gan" => ["ol:poxi", "ol:shen-gan-ning-jieying"], "lu" => ["ol:junlue", "ol:cuike", "ol:zhanhuo"], _ => ["ol:duorui", "ol:zhiti"] };
            builder.AddGeneral(new ContentGeneralDefinition("fixture:strategic-owner", "Fixture", "supporter", skills[0], "god", BaseHp: flavor is "liu" or "gan" ? 6 : 4, AdditionalSkillIds: [.. skills.Skip(1), Driver]) { InitialHp = flavor == "gan" ? 3 : null });
            var opponents = Enumerable.Range(1, 3).Select(index => $"fixture:strategic-opponent-{index}").ToArray();
            foreach (var opponent in opponents) builder.AddGeneral(new ContentGeneralDefinition(opponent, "Opponent", "supporter", flavor == "marks" ? "fixture:marker-donor" : "ol:zhiti", "qun", BaseHp: 20));
            builder.AddDeck(new ContentDeckRecipe("fixture:strategic-deck", "Fixture", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 400).Select(index => new ContentDeckPhysicalCard(flavor == "gan" ? "standard:nullification" : index % 3 == 0 ? "standard:draw_two" : "standard:crossbow", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:strategic-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:strategic-owner", .. opponents]));
        }
    }
}

