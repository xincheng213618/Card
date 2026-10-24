using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class BoundaryYiJiMobileChecks
{
    private const string Jijie = "boundary:jijie-mobile", Jiyuan = "boundary:jiyuan-mobile";
    private const string Driver = "fixture:boundary-yiji-driver";

    public static void JijieViewsBeforeChoosingAndRecovers()
    {
        var (game, registry) = Start();
        var before = game.CardMovements.Count;
        Use(game, Jijie, "view-and-give");
        Reach(game, prompt => IsTarget(prompt, Jijie));
        var actualBottom = Pile(game)[^1];
        Require(game.CreateSnapshot(0).PrivateRevealedCards?.Select(card => card.Id).SequenceEqual([actualBottom]) == true,
            "Jijie privately shows the actual bottom card before a recipient is chosen.");
        for (var viewer = 1; viewer < 4; viewer++)
            Require(game.CreateSnapshot(viewer).PrivateRevealedCards is null && game.CreateSnapshot(viewer).PendingDecision is null,
                "The unchosen bottom card and private recipient prompt remain with Yi Ji.");
        Require(game.CardMovements.Count == before, "Viewing the bottom card does not move or draw it.");
        ReplayFourViews(game, registry);
        var handBefore = game.CreateSnapshot(0).Players[0].HandCount;
        Answer(game, choice => choice.Targets.SequenceEqual([0]));
        Settle(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == handBefore + 1 &&
                game.CardMovements.Count(move => move.CardId == actualBottom && move.From == CardLocation.DrawPile &&
                    move.To == CardLocation.Hand(0)) == 1,
            "Choosing oneself gives exactly the privately viewed entity and does not offer Jiyuan.");
        var retry = game.Submit(new UseProgramSkillCommand(0, Jijie, "view-and-give", [], [], game.Revision, P(game)!.PromptId));
        Require(!retry.Accepted, "Jijie is unavailable again in the same actual play phase.");
        Replay(game, registry);
    }

    public static void JijieGiftAndMultiCardGiveOfferOneJiyuan()
    {
        var (game, registry) = Start();
        Use(game, Jijie, "view-and-give");
        Reach(game, prompt => IsTarget(prompt, Jijie));
        var bottom = game.CreateSnapshot(0).PrivateRevealedCards!.Single().Id;
        var recipientBefore = game.CreateSnapshot(0).Players[1].HandCount;
        Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Reach(game, prompt => IsOffer(prompt, "native-give-draw"));
        Require(game.CreateSnapshot(0).Players[1].HandCount == recipientBefore + 1 &&
                game.CardMovements.Any(move => move.CardId == bottom && move.To == CardLocation.Hand(1)),
            "A real Jijie deck-to-hand Give opens Jiyuan for its recipient.");
        ReplayFourViews(game, registry);
        ResolveJiyuan(game, 1);
        Require(game.CreateSnapshot(0).Players[1].HandCount == recipientBefore + 2,
            "Jiyuan draws exactly one native card after the bottom-card gift.");

        var cards = game.CreateSnapshot(0).Players[0].Hand!.Take(2).Select(card => card.Id).ToArray();
        var beforeBatch = game.CreateSnapshot(0).Players[1].HandCount;
        Use(game, Driver, "give-two", cards, [1]);
        Reach(game, prompt => IsOffer(prompt, "native-give-draw"));
        Require(game.CreateSnapshot(0).Players[1].HandCount == beforeBatch + 2,
            "The full two-card gift completes before the optional Jiyuan benefit.");
        ReplayFourViews(game, registry);
        ResolveJiyuan(game, 1);
        Require(game.CreateSnapshot(0).Players[1].HandCount == beforeBatch + 3,
            "Giving two cards together offers one Jiyuan draw, rather than one per entity.");
        Replay(game, registry);
    }

    public static void JiyuanDyingBranchDrawsForTheDyingCharacter()
    {
        var (game, registry) = Start();
        var before = game.CreateSnapshot(0).Players[1].HandCount;
        Require(game.CreateSnapshot(0).Players[1].Hp == 4,
            "The fixed non-classic identity fixture starts the recipient with four actual HP.");
        Use(game, Driver, "hurt", [], [1]);
        Reach(game, prompt => IsOffer(prompt, "dying-draw"));
        Activate(game);
        Reach(game, prompt => IsTarget(prompt, Jiyuan));
        Require(P(game)!.Choices.All(choice => choice.Targets.SequenceEqual([1])),
            "The dying branch is bound to the actual dying character.");
        Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Require(game.CreateSnapshot(0).Players[1].HandCount == before + 1,
            "Jiyuan draws for the character entering dying before rescue continues.");
        Replay(game, registry);
    }

    private static bool IsTarget(PendingDecision prompt, string skill) =>
        prompt.SkillPrompt?.SkillId == skill && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target");

    private static bool IsOffer(PendingDecision prompt, string binding) =>
        prompt.SkillPrompt?.SkillId == Jiyuan && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
            choice.Parameters.GetValueOrDefault("binding-id") == binding);

    private static void ResolveJiyuan(GameEngine game, int recipient)
    {
        Activate(game);
        Reach(game, prompt => IsTarget(prompt, Jiyuan));
        Answer(game, choice => choice.Targets.SequenceEqual([recipient]));
        Settle(game);
    }

    private static void Use(GameEngine game, string skill, string binding, int[]? cards = null, int[]? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, skill, binding, cards ?? [], targets ?? [], game.Revision, P(game)!.PromptId));

    internal static (ContentRegistry Registry, GameEngine Game) CreateScene() => BoundaryYiJiScenario.CreateScene();

    private static (GameEngine, ContentRegistry) Start()
    {
        var scene = BoundaryYiJiScenario.CreateScene();
        return (scene.Game, scene.Registry);
    }
}
