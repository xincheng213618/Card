using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class CardConversionUiChecks
{
    public static void Run()
    {
        var firstSource = new CardConversionSource("skill:first", "binding:slash", 0, "instance:first");
        var secondSource = new CardConversionSource("skill:second", "binding:slash", 0, "instance:second");
        var first = ConversionAction("第一技能：当作杀使用", firstSource);
        var second = ConversionAction("第二技能：当作杀使用", secondSource);
        IReadOnlyList<LegalAction> actions = [first, second];

        var select = typeof(MainViewModel).GetMethod(
            "SelectPlayAction",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var ambiguous = (LegalAction?)select.Invoke(null,
            [actions, 41, new[] { 2 }, CardKind.Peach, true, null]);
        var selected = (LegalAction?)select.Invoke(null,
            [actions, 41, new[] { 2 }, CardKind.Peach, true, secondSource]);

        Require(ambiguous is null,
            "Two conversions with the same physical card, output kind and targets were silently merged.");
        Require(ReferenceEquals(selected, second) && selected.ConversionSource == secondSource,
            "The selected conversion source did not identify the exact legal action.");

        var createCommand = typeof(MainViewModel).GetMethod(
            "CreatePlayCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var command = (PlayCardCommand)createCommand.Invoke(null,
            [0, 41, selected, 12L, new PromptId(73)])!;
        Require(command.PlayedCardKind == CardKind.Slash &&
                command.TargetSeats.SequenceEqual([2]) &&
                command.ConversionSource == secondSource,
            "PlayCardCommand did not retain the exact selected conversion source.");

        TrustedActionDetailsStayOutOfPlayerProjections(secondSource);
        ProgramCardTriggerChoicesRemainActionable();
    }

    private static void ProgramCardTriggerChoicesRemainActionable()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new UiTriggerFixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 5,
            ModeId = UiTriggerFixturePackage.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Program-card UI fixture did not reach human play.");

        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.ConversionSource?.SkillId == "ui-trigger:source");
        using var viewModel = new MainViewModel(false, 1, showSetup: false,
            saveStore: new MemorySaveStore(), contentRegistry: registry);
        ReplaceEngine(viewModel, game);

        Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource
        }).Accepted, "Program conversion did not enter its trigger window.");
        Refresh(viewModel, game);

        Require(viewModel.IsSkillSelectionPending && viewModel.SkillChoices.Count == 2 &&
                viewModel.SkillChoices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "program-trigger-activate") &&
                viewModel.SkillChoices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "program-trigger-skip"),
            "WPF did not expose activate and skip for the optional program-card trigger.");

        var activate = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-trigger-activate");
        viewModel.SelectSkillChoiceCommand.Execute(activate);
        Require(game.PendingDecision is { Kind: DecisionKind.ProgramCardTrigger } &&
                viewModel.IsSkillSelectionPending && viewModel.SkillChoices.Count > 0 &&
                viewModel.SkillChoices.All(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "program-trigger-take" &&
                    choice.Cards.Count == 0 && choice.Targets.SequenceEqual(action.TargetSeats)),
            "Activated trigger did not expose opaque hand slots through the generic skill choices.");

        var ownerBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
        viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices[0]);
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == ownerBefore + 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() == 1 &&
                game.PendingDecision?.Kind != DecisionKind.ProgramCardTrigger,
            "Selecting one opaque slot did not resolve exactly once or resume Slash processing.");
    }

    private static void ReplaceEngine(MainViewModel viewModel, GameEngine game)
    {
        typeof(MainViewModel).GetMethod("ReplaceEngine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [game]);
        Refresh(viewModel, game);
    }

    private static void Refresh(MainViewModel viewModel, GameEngine game) =>
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [game.CreateSnapshot(0)]);

    private static void TrustedActionDetailsStayOutOfPlayerProjections(CardConversionSource source)
    {
        var hiddenCardId = 987654321;
        var context = new CardActionContext(
            9, null, CardActionType.Use, 0, 0, null, null, null,
            CardKind.Slash, [1],
            [new CardActionCost(hiddenCardId, CardKind.Peach, new CardLocation(CardZoneKind.Hand, 0))],
            [source]);
        var audit = new EventEnvelope(new EventId(1), null, 1, 1, "private-action",
            new CardActionAcceptedEvent(context));
        var playerView = new GameSnapshot(
            null, 0, EngineStatus.Completed, Winner.Draw, 1, 0, TurnPhase.Finished,
            0, 0, [], null);

        Require(BattleCueProjector.Project([audit], playerView).Count == 0,
            "Battle feedback projected trusted physical costs or conversion provenance.");
        var summaryJson = JsonSerializer.Serialize(MatchSummary.Create(playerView, [audit]));
        Require(!summaryJson.Contains(hiddenCardId.ToString(), StringComparison.Ordinal) &&
                !summaryJson.Contains(source.SkillId, StringComparison.Ordinal) &&
                !summaryJson.Contains(source.SkillInstanceId, StringComparison.Ordinal),
            "Match summary exposed trusted physical costs or conversion provenance.");

        var snapshotJson = JsonSerializer.Serialize(playerView);
        Require(!snapshotJson.Contains("ResolutionStack", StringComparison.Ordinal) &&
                !snapshotJson.Contains("PhysicalCards", StringComparison.Ordinal) &&
                !snapshotJson.Contains("ConversionChain", StringComparison.Ordinal) &&
                !snapshotJson.Contains("CardActionAccepted", StringComparison.Ordinal),
            "Ordinary player snapshots acquired trusted event or resolution data.");
    }

    private static LegalAction ConversionAction(string description, CardConversionSource source) =>
        new(LegalActionKind.Slash, 41, 2, description, CardKind.Slash)
        {
            ConversionSource = source
        };

    private sealed class UiTriggerFixturePackage : IGameContentPackage
    {
        public const string ModeId = "identity:ui-program-trigger-5";
        public PackageManifest Manifest { get; } = new("ui-program-trigger", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var program in catalog.Programs.Values)
            {
                var text = catalog.Presentations[program.Id];
                builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description)
                {
                    Program = program
                });
            }

            var generals = Enumerable.Range(0, 5).Select(index => $"ui-trigger:general-{index}").ToArray();
            foreach (var id in generals)
                builder.AddGeneral(new ContentGeneralDefinition(id, "Trigger", "zhao_yun", "ui-trigger:source",
                    AdditionalSkillIds: ["ui-trigger:obtain"]));
            builder.AddDeck(new ContentDeckRecipe("ui-trigger:dodge-deck", "Dodge", 2, 0,
                [new ContentDeckCardCount("standard:dodge", 30)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "Trigger", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, DeckId: "ui-trigger:dodge-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }

        private const string Rules = """
            {"schemaVersion":2,"skills":[
              {"id":"ui-trigger:source","revision":1,"viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]},
              {"id":"ui-trigger:obtain","revision":1,"triggers":[{"id":"after-use","window":"cardUseTargetsFinalized","sourceSkillId":"ui-trigger:source","sourceViewAsId":"slash","optional":true,"effects":[{"op":"obtainOpponentHandCard","target":"owner","amount":1}]}]}
            ]}
            """;
        private const string Presentation = """
            {"schemaVersion":1,"skills":{
              "ui-trigger:source":{"name":"转化","description":"转化"},
              "ui-trigger:obtain":{"name":"取牌","description":"取牌"}
            }}
            """;
    }

    private static void Require(bool condition, string message) => Program.Assert(condition, message);
}
