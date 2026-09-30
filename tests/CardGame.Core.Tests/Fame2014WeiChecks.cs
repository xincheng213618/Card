using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2014WeiChecks
{
    public static void ShenduanRealDiscardAndReplay()
    {
        var (game, registry) = Create("classic:shenduan", "standard:slash");
        var use = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash);
        Accept(game.Submit(new PlayCardCommand(0, use.CardId!.Value, [use.TargetSeat!.Value], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var red = game.CreateSnapshot(0, true).Players[0].Hand.First(item => item.Suit is Suit.Heart or Suit.Diamond);
        Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:wei-discard", "discard", [red.Id], [], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(!game.Events.Any(item => item.Payload is ProgramSkillStartedEvent { SkillId: "classic:shenduan" }),
            "Ordinary use cleanup and a red basic discard do not trigger discarded black basic conversion.");
        var card = game.CreateSnapshot(0, true).Players[0].Hand.First(item => item.Suit is Suit.Spade or Suit.Club);
        var start = game.CardMovements.Count;
        Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:wei-discard", "discard", [card.Id], [], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:shenduan");
        Activate(game);
        Require(game.CreateSnapshot(1).PendingDecision is null || game.CreateSnapshot(1).PendingDecision!.Choices.Count == 0,
            "A different viewer cannot receive the owner's private target choices.");
        var replay = Restore(game, registry);
        var choice = game.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([2]));
        RejectUnknownChoice(game);
        PairChoice(game, replay, choice);
        ReachPair(game, replay, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Skip(start).Count(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile) == 1,
            "The triggering black basic card is discarded exactly once.");
        Require(game.CardMovements.Skip(start).Any(move => move.CardId == card.Id && move.From == CardLocation.DiscardPile && move.To == CardLocation.Processing) &&
            game.CardMovements.Skip(start).Any(move => move.CardId == card.Id && move.To == CardLocation.Judgment(choice.Targets[0])),
            "Supply Shortage must reuse the identical real discarded card through ordinary processing.");
        Equivalent(game, replay);
    }

    private static void SidiUsedSlashSuppressesEndAttack()
    {
        var (game, registry) = Create("classic:sidi", "standard:crossbow", mixedSlash: true);
        var equipment = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip);
        Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:sidi");
        Activate(game);
        var replay = Restore(game, registry);
        var payment = game.PendingDecision!.Choices.First();
        PairChoice(game, replay, payment);
        for (var step = 0; step < 512 && game.CreateSnapshot(0).CurrentSeat == 1; step++) StepPair(game, replay);
        Require(game.Events.Any(item => item.Payload is CardUseDeclaredEvent { SourceSeat: 1, CardKind: CardKind.Slash }),
            "Opposite-color actual Slash remains available during the restricted Play phase.");
        Require(!game.Events.Any(item => item.Payload is CardUsedEvent { CardId: 0, SourceSeat: 0, CardKind: CardKind.Slash }),
            "Using a real Slash in the restricted phase suppresses the end-of-phase virtual Slash.");
        Equivalent(game, replay);
    }

    public static void SidiPaymentPrivacyAndReplay()
    {
        var (game, registry) = Create("classic:sidi", "standard:crossbow");
        var equip = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip);
        Accept(game.Submit(new PlayCardCommand(0, equip.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:sidi");
        Activate(game);
        var payment = game.PendingDecision!;
        Require(payment.Choices.All(item => item.Cards.Count == 1), "Color payment selects one actual owner card.");
        var hidden = game.CreateSnapshot(1).PendingDecision;
        Require(hidden is null || hidden.Choices.Count == 0, "Color-payment hand candidates remain private to their chooser.");
        RejectUnknownChoice(game);
        var replay = Restore(game, registry);
        var cost = payment.Choices.First(item => item.Cards[0] != equip.CardId);
        var beforeUses = game.Events.Count(item => item.Payload is CardUsedEvent { CardId: 0, CardKind: CardKind.Slash });
        PairChoice(game, replay, cost);
        for (var step = 0; step < 512 && game.Events.Count(item => item.Payload is CardUsedEvent { CardId: 0, CardKind: CardKind.Slash }) == beforeUses; step++) StepPair(game, replay);
        Require(game.CardMovements.Count(move => move.CardId == cost.Cards[0] && move.To == CardLocation.DiscardPile) == 1,
            "The restriction payment is not charged again after restoring the paused checkpoint.");
        Require(game.Events.Count(item => item.Payload is CardUsedEvent { CardId: 0, CardKind: CardKind.Slash }) > beforeUses,
            "A phase without a used Slash must create the configured virtual Slash at phase end.");
        Equivalent(game, replay);
        SidiUsedSlashSuppressesEndAttack();
    }

    public static void YonglueRealJudgmentAndReplay()
    {
        var (game, registry) = Create("classic:yonglue", "standard:indulgence");
        var use = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Indulgence && item.TargetSeat == 1);
        Accept(game.Submit(new PlayCardCommand(0, use.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:yonglue");
        Activate(game);
        var replay = Restore(game, registry);
        var selected = game.PendingDecision!.Choices.First(item => item.Cards.Count == 1);
        RejectUnknownChoice(game);
        PairChoice(game, replay, selected);
        for (var step = 0; step < 128 && !game.Events.Any(item => item.Payload is CardUsedEvent { CardId: 0, CardKind: CardKind.Slash }); step++) StepPair(game, replay);
        Require(game.CardMovements.Any(move => move.CardId == use.CardId && move.From == CardLocation.Judgment(1) && move.To == CardLocation.DiscardPile),
            "Yonglue discards the actual delayed card from the other character's judgment zone.");
        Require(game.Events.Any(item => item.Payload is CardUseDeclaredEvent { CardId: 0, SourceSeat: 0 }) &&
            !game.CardMovements.Any(move => move.CardId == 0),
            "The virtual Slash has the skill owner as source without inventing a physical card.");
        Equivalent(game, replay);
    }

    public static void YonglueNoDamageDrawAndReplay()
    {
        (GameEngine Game, ContentRegistry Registry)? found = null;
        LegalAction? delayed = null;
        for (var seed = 1; seed <= 40; seed++)
        {
            var pair = Create("classic:yonglue", "standard:indulgence", seed, mixedJink: true);
            delayed = pair.Item1.GetHumanLegalActions().FirstOrDefault(item => item.Kind == LegalActionKind.Indulgence && item.TargetSeat == 1);
            if (delayed is not null && pair.Item1.CreateSnapshot(0, true).Players[1].Hand.Any(card => card.Kind == CardKind.Dodge))
            { found = pair; break; }
        }
        Require(found is not null && delayed is not null, "The mixed-deck fixture must supply a real delayed card and a target Jink.");
        var (game, registry) = found!.Value;
        Accept(game.Submit(new PlayCardCommand(0, delayed!.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:yonglue");
        Activate(game);
        var before = game.CreateSnapshot(0, true);
        var replay = Restore(game, registry);
        PairChoice(game, replay, game.PendingDecision!.Choices.First(item => item.Cards.Count == 1));
        for (var step = 0; step < 256 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == "classic:yonglue"); step++) StepPair(game, replay);
        var after = game.CreateSnapshot(0, true);
        Require(after.Players[1].Hp == before.Players[1].Hp && after.Players[0].HandCount == before.Players[0].HandCount + 1,
            "A fully dodged configured virtual Slash draws exactly one card for its owner.");
        Require(game.Events.Any(item => item.Payload is CardRespondedEvent response && response.ResponderSeat == 1),
            "The no-damage branch must pass through a real response rather than skipping the attack.");
        Equivalent(game, replay);
    }

    private static void RejectUnknownChoice(GameEngine game)
    {
        var state = State(game); var commands = game.AcceptedCommands.Count; var moves = game.CardMovements.Count;
        var result = game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId, new ChoiceId("fixture:illegal"), game.Revision));
        Require(!result.Accepted && state == State(game) && commands == game.AcceptedCommands.Count && moves == game.CardMovements.Count,
            "An illegal prompt answer must reject atomically before payment or movement.");
    }
    private static void Activate(GameEngine game) => Choose(game, game.PendingDecision!.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "activate"));
    private static GameEngine Restore(GameEngine game, ContentRegistry registry) => GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    { for (var step = 0; step < 1024 && !(game.PendingDecision is { } prompt && condition(prompt)); step++) Step(game); Require(game.PendingDecision is { } pending && condition(pending), "The expected Wei program boundary was not reached."); }
    private static void ReachPair(GameEngine game, GameEngine replay, Func<PendingDecision, bool> condition)
    { for (var step = 0; step < 512 && !(game.PendingDecision is { } prompt && condition(prompt)); step++) StepPair(game, replay); Require(game.PendingDecision is { } pending && condition(pending), "The expected restored Wei boundary was not reached."); }
    private static void Step(GameEngine game)
    {
        if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } prompt)
            Choose(game, prompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("program-action") == "decline") ?? prompt.Choices[0]);
        else if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } play) Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }
    private static void StepPair(GameEngine game, GameEngine replay)
    {
        if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } prompt)
            PairChoice(game, replay, prompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("program-action") == "decline") ?? prompt.Choices[0]);
        else if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } play)
        { Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId))); Accept(replay.Submit(new EndPlayPhaseCommand(0, replay.Revision, replay.PendingDecision!.PromptId))); }
        else { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision))); }
    }
    private static void PairChoice(GameEngine game, GameEngine replay, PromptChoice choice) { Choose(game, choice); var command = new AnswerPromptCommand(0, replay.PendingDecision!.PromptId, replay.PendingDecision.Choices.Single(item => item.Id == choice.Id).Id, replay.Revision); Accept(replay.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single())); }
    private static void Choose(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static void Equivalent(GameEngine game, GameEngine replay) => Require(State(game) == State(replay) && JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(replay.Events) && JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(replay.AcceptedCommands) && game.CardMovements.SequenceEqual(replay.CardMovements), "Checkpoint replay must retain state, commands, events and real card movements.");
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Wei command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(string skill, string card, int seed = 17, bool mixedJink = false, bool mixedSlash = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(skill, card, mixedJink, mixedSlash));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = "fixture:wei-mode", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 50 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:wei-owner", game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        return (game, registry);
    }
    private sealed class Fixture(string skill, string card, bool mixedJink, bool mixedSlash) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wei-2014", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            foreach (var bundle in new[] { "classic-cao-zhen", "classic-han-hao-shi-huan" })
            {
                string Read(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(bundle + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var catalog = SkillProgramCatalog.Load(Read(".rules.json"), Read(".presentation.json"));
                foreach (var id in catalog.Programs.Keys) builder.AddSkill(new ContentSkillDefinition(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            var discard = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wei-discard","revision":1,"activations":[{"id":"discard","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:wei-discard":{"name":"弃牌","description":"弃置一张牌"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:wei-discard", "弃牌", "弃置一张牌") { Program = discard.Programs["fixture:wei-discard"], ProgramPresentation = discard.Presentations["fixture:wei-discard"] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:wei-owner", "魏将", "supporter", skill, "wei", BaseHp: 9, AdditionalSkillIds: ["fixture:wei-discard"]));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new ContentGeneralDefinition($"fixture:wei-{index}", $"目标{index}", "supporter", "standard:none", "wei", BaseHp: 9));
            var deck = new ContentDeckRecipe("fixture:wei-deck", "魏将测试", 22, 0,
                mixedJink ? [new ContentDeckCardCount(card, 80), new ContentDeckCardCount("standard:dodge", 80)] : [new ContentDeckCardCount(card, 160)]);
            if (mixedSlash) deck = deck with { Cards = [], PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                new ContentDeckPhysicalCard(index % 2 == 0 ? "standard:crossbow" : "standard:slash", index % 2 == 0 ? Suit.Heart : Suit.Club, index % 13 + 1)).ToArray() };
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition("fixture:wei-mode", "魏将测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:wei-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wei-owner", "fixture:wei-1", "fixture:wei-2", "fixture:wei-3"]));
        }
    }
}




