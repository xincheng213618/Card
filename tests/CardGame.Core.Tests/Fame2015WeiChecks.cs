using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2015WeiChecks
{

    public static void TopDeckBasicUseNameLedgerAndReplay()
    {
        var (game, registry) = CreateWithAction("classic:huomo", "standard:crossbow", action => action.ConversionSource?.SkillId == "classic:huomo" && action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        var action = game.GetHumanLegalActions().First(item => item.ConversionSource?.SkillId == "classic:huomo" &&
            item.Kind == LegalActionKind.Slash && item.TargetSeat == 1);
        var card = game.CreateSnapshot(0, true).Players[0].Hand.Single(item => item.Id == action.CardId);
        Require(card.Suit is Suit.Spade or Suit.Club && CardCatalog.Get(card.Kind).CategoryName != "基本牌",
            "Top-deck use requires an actual black nonbasic cost.");
        var before = State(game);
        var illegal = game.Submit(new PlayCardCommand(0, action.CardId!.Value, [0], game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        Require(!illegal.Accepted && State(game) == before, "An illegal virtual-basic target must not pay or reveal the top-deck cost.");
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        Replay(game, registry);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(move => move.CardId == card.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1 &&
            !game.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile),
            "The same physical nonbasic cost is placed on draw-pile top and survives virtual-card cleanup.");
        Require(game.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "classic:huomo" ||
            item.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)),
            "Used basic-name history blocks all three elemental Slash forms together.");
        Replay(game, registry);

        NativeDodgeObserverBelowTopDeckSlashAndReplay();
    }

    private static void NativeDodgeObserverBelowTopDeckSlashAndReplay()
    {
        GameEngine? game = null;
        ContentRegistry? registry = null;
        for (var seed = 17; seed < 49 && game is null; seed++)
        {
            var candidate = Create("standard:none", "standard:crossbow", seed, 8, responseObserver: true);
            Accept(candidate.Game, new EndPlayPhaseCommand(0, candidate.Game.Revision,
                candidate.Game.PendingDecision!.PromptId));
            for (var step = 0; step < 160; step++)
            {
                var prompt = Prompt(candidate.Game);
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice => choice.Cards.Count == 1 &&
                        !choice.Parameters.ContainsKey("conversion-skill-id")) &&
                    candidate.Game.ResolutionStack.OfType<CardUseFrame>().Any(frame =>
                        frame.Action?.ConversionChain.Any(source => source.SkillId == "classic:huomo") == true &&
                        frame.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                {
                    (game, registry) = candidate;
                    break;
                }
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) break;
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge })
                    Answer(candidate.Game, choice => choice.Cards.Count == 0);
                else Accept(candidate.Game, new AdvanceOneStepCommand(candidate.Game.Revision));
            }
        }
        Require(game is not null && registry is not null,
            "A real AI Huomo Slash must reach the human's native Dodge response.");
        var g = game!;
        var parent = g.ResolutionStack.OfType<CardUseFrame>().Last(frame =>
            frame.Action?.ConversionChain.Any(source => source.SkillId == "classic:huomo") == true &&
            frame.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        var parentCost = parent.Action!.PhysicalCards.Single().CardId;
        var native = Prompt(g)!.Choices.First(choice => choice.Cards.Count == 1 &&
            !choice.Parameters.ContainsKey("conversion-skill-id"));
        var dodgeCost = native.Cards.Single();
        foreach (var seat in Enumerable.Range(1, 3))
        {
            var view = g.CreateSnapshot(seat);
            Require(view.Players[0].Hand.Count == 0 &&
                (view.PendingDecision is null || view.PendingDecision.Choices.Count == 0),
                "Other players cannot read the native responder's hand or physical response choices.");
        }
        RejectUnknown(g);
        Replay(g, registry!);
        Answer(g, choice => choice.Id == native.Id);
        Require(Prompt(g)?.SkillPrompt?.SkillId == "fixture:wei2015-response-observer" &&
            g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(frame =>
                frame.Action.Type == CardActionType.Response && frame.Action.EffectiveKind == CardKind.Dodge &&
                frame.Action.ActorSeat == 0 && frame.Action.ProviderSeat == 0 &&
                frame.Action.ConversionChain.Count == 0 && frame.Action.PhysicalCards.Single().CardId == dodgeCost &&
                frame.Action.PhysicalCards.Single().From == CardLocation.Hand(0)),
            "Native Dodge must pause in its own optional response window above the real Huomo attack.");
        Require(g.CreateCardZoneDiagnostics().Single(card => card.CardId == dodgeCost).Location == CardLocation.Processing &&
            g.CreateCardZoneDiagnostics().Single(card => card.CardId == parentCost).Location == CardLocation.DrawPile,
            "The parent alternative cost and child native response retain different exact physical zones while paused.");
        foreach (var seat in Enumerable.Range(1, 3))
        {
            var view = g.CreateSnapshot(seat);
            Require(view.Players[0].Hand.Count == 0 &&
                (view.PendingDecision is null || view.PendingDecision.Choices.Count == 0),
                "The paused optional response observer exposes its choices only to its owner.");
        }
        RejectUnknown(g);
        Replay(g, registry!);
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry!);
        var journalReplay = GameReplay.Replay(g.CreateCheckpoint().Options,
            CommandJson.Deserialize(CommandJson.Serialize(g.AcceptedCommands)), registry!);
        Require(State(g) == State(journalReplay), "Command JSON must reproduce the paused native response and every viewer's privacy boundary.");
        foreach (var branch in new[] { g, restored, journalReplay })
        {
            Answer(branch, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            for (var step = 0; step < 100 && branch.ResolutionStack.Count > 0; step++)
                Accept(branch, new AdvanceOneStepCommand(branch.Revision));
            Require(branch.ResolutionStack.Count == 0 &&
                branch.CardMovements.Count(move => move.CardId == dodgeCost && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                branch.CardMovements.Count(move => move.CardId == dodgeCost && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1 &&
                branch.CardMovements.Count(move => move.CardId == parentCost && move.To == CardLocation.DrawPile) == 1 &&
                branch.CardMovements.Count(move => move.CardId == parentCost && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)) == 1 &&
                branch.CardMovements.All(move => move.CardId != parentCost || move.To != CardLocation.Processing && move.To != CardLocation.DiscardPile),
                "Nested observer draw obtains the real parent top cost; native Dodge cleanup spends its own card exactly once.");
            Replay(branch, registry!);
        }
        Require(State(g) == State(restored) && State(g) == State(journalReplay),
            "Paused native response checkpoint and command JSON replay must preserve every view, event and physical ledger.");
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 300; step++)
        { if (Prompt(game) is { } prompt && condition(prompt)) return; Accept(game, new AdvanceOneStepCommand(game.Revision)); }
        throw new InvalidOperationException("The Wei 2015 fixture did not reach its configured prompt.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> condition)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(condition).Id, game.Revision));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        TrustedViews = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat, true))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(envelope => JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())).ToArray(),
        Movements = game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands)
    });
    private static void RejectUnknown(GameEngine game)
    {
        var before = State(game); var prompt = Prompt(game)!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("fixture:illegal"), game.Revision));
        Require(!result.Accepted && State(game) == before, "Invalid answers must preserve every player's view, frames, events, movement ledger and command journal atomically.");
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(replay), "Checkpoint plus command JSON must reproduce every player's view, draft, typed event and real movement ledger.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Wei 2015 command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) CreateWithAction(string skill, string weapon, Func<LegalAction, bool> condition)
    {
        for (var seed = 17; seed < 33; seed++)
        {
            var result = Create(skill, weapon, seed, 8);
            if (result.Game.GetHumanLegalActions().Any(condition)) return result;
        }
        throw new InvalidOperationException("No deterministic fixture hand exposes the required real physical card action.");
    }
    private static (GameEngine Game, ContentRegistry Registry) Create(string skill, string weapon, int seed, int hp, bool diverse = false, bool fragileResponders = false, bool responseObserver = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(skill, weapon, hp, diverse, fragileResponders, responseObserver));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-wei2015", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:wei2015-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        return (game, registry);
    }
    private sealed class Scenario(string skill, string weapon, int hp, bool diverse, bool fragileResponders, bool responseObserver) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wei2015", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wei2015-damage","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:wei2015-damage":{"name":"测试伤害","description":"造成4点伤害"}}}""");
            builder.AddSkill(new("fixture:wei2015-damage", "测试伤害", "造成4点伤害") { Program = catalog.Programs["fixture:wei2015-damage"], ProgramPresentation = catalog.Presentations["fixture:wei2015-damage"] });
            if (responseObserver)
            {
                var observer = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wei2015-response-observer","revision":1,"triggers":[{"id":"native-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:wei2015-response-observer":{"name":"响应观察","description":"闪响应后可摸一张"}}}""");
                builder.AddSkill(new("fixture:wei2015-response-observer", "响应观察", "闪响应后可摸一张") { Program = observer.Programs["fixture:wei2015-response-observer"] });
            }
            builder.AddGeneral(new("fixture:wei2015-owner", "魏将2015", "supporter", skill, "wei", BaseHp: hp, AdditionalSkillIds: responseObserver ? ["fixture:wei2015-damage", "fixture:wei2015-response-observer"] : ["fixture:wei2015-damage"]));
            foreach (var seat in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:wei2015-{seat}", "目标", "supporter", responseObserver ? "classic:huomo" : diverse ? "classic:zuoding" : "standard:none", seat == 3 ? "shu" : "wei", BaseHp: fragileResponders ? 1 : 9));
            var ids = new[] { "standard:crossbow", "standard:alcohol", "standard:peach", "standard:indulgence", "standard:supply_shortage", "standard:lightning", "standard:peach_garden" };
            builder.AddDeck(new("fixture:wei2015-deck", "实体杀及武器", diverse ? 35 : 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, diverse ? 350 : 160).Select(index => new ContentDeckPhysicalCard(responseObserver ? index % 2 == 0 ? "standard:crossbow" : "standard:dodge" : diverse ? ids[index % ids.Length] : index % 3 == 0 ? weapon : "standard:slash", diverse || index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-wei2015", "魏将2015测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:wei2015-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wei2015-owner", "fixture:wei2015-1", "fixture:wei2015-2", "fixture:wei2015-3"]));
        }
    }
}
