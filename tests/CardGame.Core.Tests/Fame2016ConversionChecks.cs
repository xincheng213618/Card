using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2016ConversionChecks
{

    public static void DeclarationEntityPrivacyAndReplay()
    {
        var (game, registry) = Create();
        var card = game.CreateSnapshot(0).Players[0].Hand[0];
        Declare(game, registry, card.Id, CardKind.Slash);
        var action = game.GetHumanLegalActions().First(action => action.ConversionSource?.SkillId == "classic:jiaozhao" && action.TargetSeat == 1);
        Require(action.CardId == card.Id && game.GetHumanLegalActions().Where(action => action.ConversionSource?.SkillId == "classic:jiaozhao")
            .All(action => action.CardId == card.Id), "Only the exact declared hand entity can use its declared name.");
        Play(game, action);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(move => move.CardId == card.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile), "The declared card pays one real card cost and finishes in discard.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:jiaozhao" || action.ConversionSource?.SkillId == "classic:jiaozhao"), "The declaration cannot be activated again in its Play phase.");
        Replay(game, registry);
    }

    private static void Declare(GameEngine game, ContentRegistry registry, int card, CardKind kind)
    {
        Accept(game, new UseProgramSkillCommand(0, "classic:jiaozhao", "declare-card", [card], [], game.Revision, Prompt(game)!.PromptId));
        var prompt = Prompt(game)!;
        var declarationFrame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(declarationFrame.CardDeclarationDraft is { DeclarerSeat: null } draft &&
            draft.CardId == card && draft.NearestSeats.SequenceEqual(prompt.Choices.Select(choice => choice.Targets.Single())),
            "The private two-stage declaration draft must belong to its suspended program frame.");
        ProgramChoiceDraftChecks.Frozen(declarationFrame.CardDeclarationDraft!.NearestSeats);
        Require(prompt.Choices.All(choice => choice.Targets.Count == 1 && choice.Targets[0] != 0 && game.GetCombatDistance(0, choice.Targets[0]) == 1), "The owner chooses only nearest living other characters.");
        foreach (var seat in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(seat).PublicRevealedCards.Single().Id == card, "The displayed real hand card is public during declaration.");
        RejectUnknown(game); Replay(game, registry);
        Answer(game, _ => true);
        var declarer = Prompt(game)!.PlayerSeat;
        Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == declarationFrame.Id)
            .CardDeclarationDraft?.DeclarerSeat == declarer,
            "Choosing a declarer updates the same owning frame before its second private prompt.");
        Require(declarer != 0 && Prompt(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice") == kind.ToString()), "The selected other character declares a permitted real name.");
        foreach (var seat in Enumerable.Range(0, 4).Where(seat => seat != declarer))
            Require(game.CreateSnapshot(seat).PendingDecision is null || game.CreateSnapshot(seat).PendingDecision!.Choices.Count == 0, "Only the declarer sees their pending choices.");
        RejectUnknown(game); Replay(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("choice") == kind.ToString());
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.CardDeclarationDraft is null),
            "Completing the declaration must retire its temporary frame draft.");
        Replay(game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 400; step++)
        {
            if (Prompt(game) is { } prompt && predicate(prompt)) return;
            if (Prompt(game) is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 } draft) Answer(game, _ => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The conversion fixture did not reach its prompt.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), Movements = game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static void RejectUnknown(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; var result = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("fixture:illegal"), game.Revision)); Require(!result.Accepted && before == State(game), "An invalid answer preserves every view, event, real movement, cursor and accepted command."); }
    private static void Replay(GameEngine game, ContentRegistry registry)
    { var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(game) == State(replay), "Checkpoint and command JSON replay all public/private states and physical moves."); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool slashCards = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(slashCards));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:conversion", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand()); Accept(game, new SelectGeneralCommand(0, "fixture:conversion-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard); return (game, registry);
    }
    private sealed class Scenario(bool slashCards) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-conversion", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:conversion-damage","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:conversion-damage":{"name":"伤害","description":"测试真实伤害"}}}""");
            builder.AddSkill(new("fixture:conversion-damage", "伤害", "测试真实伤害") { Program = catalog.Programs["fixture:conversion-damage"] });
            builder.AddGeneral(new("fixture:conversion-owner", "郭皇后测试", "supporter", "classic:jiaozhao", "wei", BaseHp: 9, AdditionalSkillIds: ["classic:danxin", "classic:tiaoxin", "fixture:conversion-damage"]));
            foreach (var seat in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:conversion-{seat}", "目标", "supporter", "standard:none", "wei", BaseHp: 9));
            builder.AddDeck(new("fixture:conversion-deck", "真实装备牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(slashCards && index % 2 == 0 ? "standard:slash" : "standard:crossbow", index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:conversion", "转换测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:conversion-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:conversion-owner", "fixture:conversion-1", "fixture:conversion-2", "fixture:conversion-3"]));
        }
    }
}
