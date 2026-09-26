using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

// Parent-review regression source. Register as:
// ("turn-ending Xiaoguo Tianxiang game over stops later observers", TurnEndingGameOverChecks.XiaoguoTianxiangVictoryStopsLaterObservers),
internal static class TurnEndingGameOverChecks
{
    private const string ModeId = "identity:classic-xiaoguo-tianxiang-gameover-4";
    private const string Xiaoguo = "sp:xiaoguo";

    public static void XiaoguoTianxiangVictoryStopsLaterObservers()
    {
        // Fixture injects AI general templates and lord HP at a clean human Play prompt.
        // From the ending boundary onward every transition is a real command.
        // The injected state is not command-replayable; this test asserts the live boundary only.
        var (game, lordSeat) = FindFixture();
        var option = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(option.PlayerSeat == 0 && option.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("option-id") == "take-damage"),
            "The turn owner must choose Xiaoguo damage through its private prompt.");
        var boundary = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        var active = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        Require(boundary.Items.Count > boundary.ItemIndex + 1 &&
            boundary.Items.Skip(boundary.ItemIndex + 1).Any(item =>
                item.Candidate?.OwnerSeat != active.OwnerSeat),
            "A later, distinct observer must remain when the first Xiaoguo damages Xiao Qiao.");
        var startedAtBoundary = game.Events.Select(item => item.Payload)
            .OfType<ProgramBindingStartedEvent>().Count(item => item.SkillId == Xiaoguo);
        Answer(game, option.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("option-id") == "take-damage"));
        for (var i = 0; i < 40 && game.PendingDecision?.Kind != DecisionKind.Tianxiang; i++)
            Step(game);
        var tianxiang = RequirePrompt(game, DecisionKind.Tianxiang);
        Require(tianxiang.PlayerSeat == 0 && tianxiang.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tianxiang-use" &&
            choice.Targets.SequenceEqual([lordSeat])),
            "Xiao Qiao must be able to redirect Xiaoguo's one damage to the one-HP lord.");
        var selected = tianxiang.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tianxiang-use" &&
            choice.Targets.SequenceEqual([lordSeat]));
        Answer(game, selected);
        for (var i = 0; i < 80 && !HasWinner(game); i++)
            Step(game);
        Require(HasWinner(game) && !game.State.Players[lordSeat].IsAlive &&
            game.State.Players[0].IsAlive,
            "Tianxiang must make a different player die while the ending turn owner survives.");
        Require(game.State.Status == EngineStatus.Completed &&
            game.PendingDecision is null &&
            !game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any() &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any() &&
            game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Count(item => item.SkillId == Xiaoguo) == startedAtBoundary,
            "GameOver must end the ending boundary before any later Xiaoguo observer starts.");
    }

    private static (GameEngine Game, int LordSeat) FindFixture()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Scenario());
        // Seed 1 was selected by a bounded smoke run; no seed search is performed here.
        for (var seed = 1; seed <= 1; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Rebel, PlayerCount = 4,
                ModeId = ModeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            for (var step = 0; step < 20 && game.PendingDecision is
                 { Kind: DecisionKind.SelectGeneral, PlayerSeat: not 0 }; step++) Step(game);
            if (game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup) continue;
            var xiao = setup.ValidContentIds.FirstOrDefault(id =>
                id.StartsWith("fixture:xiao-qiao-", StringComparison.Ordinal));
            if (xiao is null) continue;
            Answer(game, new SelectGeneralCommand(0, xiao, game.Revision, setup.PromptId));

            for (var step = 0; step < 400 && game.PendingDecision is not
                 { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                 game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0 } human)
                {
                    var choice = human.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("action") == "tianxiang-skip") ??
                        human.Choices.FirstOrDefault(item => item.Cards.Count == 0) ??
                        human.Choices.FirstOrDefault();
                    if (choice is null) break;
                    Answer(game, choice);
                }
                else Step(game);
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play ||
                !game.State.Players[0].IsAlive) continue;
            var lordSeat = game.CreateSnapshot(0, true).Players.Single(player =>
                player.Role == Role.Lord).Seat;
            var players = (List<CharacterState>)typeof(GameEngine).GetField("_players",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            if (!players[lordSeat].IsAlive || game.CreateSnapshot(0, true).Players[0].Hand.All(card =>
                card.Suit is not (Suit.Heart or Suit.Spade))) continue;
            var pool = (IReadOnlyList<GeneralDefinition>)typeof(GameEngine).GetField("_generalPool",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            for (var observer = 1; observer < players.Count; observer++)
            {
                players[observer].General = pool.Single(item => item.Id == $"fixture:le-jin-{observer}");
                players[observer].MaxHp = observer == lordSeat ? 5 : 4;
                players[observer].Hp = observer == lordSeat ? 1 : 4;
            }
            Answer(game, new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            for (var step = 0; step < 100 && game.PendingDecision is not
                 { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: Xiaoguo } &&
                 game.State.Status != EngineStatus.Completed; step++) Step(game);
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                SkillPrompt.SkillId: Xiaoguo } &&
                game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault() is { } ending &&
                ending.Items.Count > ending.ItemIndex + 1 &&
                game.PendingDecision.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("option-id") == "take-damage"))
                return (game, lordSeat);
        }
        throw new InvalidOperationException("The fixed Xiao Qiao / multi-observer fixture did not reach Xiaoguo.");
    }

    private static bool HasWinner(GameEngine game) => game.Events.Select(item => item.Payload)
        .OfType<WinnerDeterminedEvent>().Any();
    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind ? prompt :
        throw new InvalidOperationException($"Expected {kind}, got {game.PendingDecision?.Kind}.");
    private static void Answer(GameEngine game, PromptChoice choice) => Answer(game,
        new AnswerPromptCommand(game.PendingDecision!.PlayerSeat,
            game.PendingDecision.PromptId, choice.Id, game.Revision));
    private static void Answer(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    }
    private static void Step(GameEngine game) => Answer(game,
        new AdvanceOneStepCommand(game.Revision));
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("xiaoguo-tianxiang-gameover", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var xiao = Enumerable.Range(1, 4).Select(index => $"fixture:xiao-qiao-{index}").ToArray();
            foreach (var id in xiao)
                builder.AddGeneral(new ContentGeneralDefinition(id, "小乔边界夹具", "xiao_qiao",
                    "classic:hongyan", "wu", BaseHp: 3,
                    AdditionalSkillIds: ["classic:tianxiang"], Gender: GeneralGender.Female));
            var observers = Enumerable.Range(1, 3).Select(index => $"fixture:le-jin-{index}").ToArray();
            foreach (var id in observers)
                builder.AddGeneral(new ContentGeneralDefinition(id, "乐进边界夹具", "sp_le_jin",
                    Xiaoguo, "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:xiaoguo-tianxiang-deck", "全红桃杀牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "骁果天香胜负边界", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, "fixture:xiaoguo-tianxiang-deck", GeneralCandidateCount: 7,
                GeneralPoolIds: [.. xiao, .. observers]));
        }
    }
}



