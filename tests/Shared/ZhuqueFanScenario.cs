using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record ZhuqueFanBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction NormalSlashAction,
    LegalAction FireSlashAction,
    LegalAction ThunderSlashAction,
    int WeaponCardId,
    int SourceSeat,
    int PrimaryTargetSeat,
    int ChainedTargetSeat);

internal static class ZhuqueFanScenario
{
    private const string ModeId = "identity:classic-zhuque-fan-5";

    public static ZhuqueFanBoundary FindHumanChainedSlash()
    {
        const int sourceSeat = 0;
        const int primaryTargetSeat = 1;
        const int chainedTargetSeat = 2;
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = sourceSeat,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = ModeId,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Zhuque Fan fixture failed to start.");
            var setup = game.PendingDecision;
            var general = setup?.Choices.FirstOrDefault();
            if (setup is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: sourceSeat } || general is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                sourceSeat,
                general.ContentIds[0],
                game.Revision,
                setup.PromptId)).Accepted,
                "Zhuque Fan fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Zhuque Fan fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } play)
            {
                continue;
            }

            var source = game.CreateSnapshot(sourceSeat, revealAll: true).Players[sourceSeat];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ZhuqueFan);
            var slash = source.Hand.FirstOrDefault(card => card.Kind == CardKind.Slash);
            var thunderSlash = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ThunderSlash);
            var ironChain = source.Hand.FirstOrDefault(card => card.Kind == CardKind.IronChain);
            if (weapon is null || slash is null || thunderSlash is null || ironChain is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                sourceSeat,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted,
                equipped.Error?.Message ?? "Zhuque Fan fixture could not equip the weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Zhuque Fan fixture did not resume play after equipping.");
            }
            var ironChainActions = game.GetHumanLegalActions().Where(action =>
                action.Kind == LegalActionKind.IronChain && action.CardId == ironChain.Id).ToArray();
            var chainAction = ironChainActions.SingleOrDefault(action =>
                action.Kind == LegalActionKind.IronChain &&
                action.CardId == ironChain.Id &&
                action.TargetSeats.SequenceEqual([primaryTargetSeat, chainedTargetSeat]));
            if (chainAction is null || game.PendingDecision is not { Kind: DecisionKind.PlayCard } chainPrompt)
            {
                continue;
            }

            var chained = game.Submit(new PlayCardCommand(
                sourceSeat,
                ironChain.Id,
                chainAction.TargetSeats,
                game.Revision,
                chainPrompt.PromptId));
            Require(chained.Accepted,
                chained.Error?.Message ?? "Zhuque Fan fixture could not chain its targets.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Zhuque Fan fixture did not resume play after Iron Chain.");
            }

            var full = game.CreateSnapshot(sourceSeat, revealAll: true);
            if (!full.Players[primaryTargetSeat].IsChained ||
                !full.Players[chainedTargetSeat].IsChained)
            {
                continue;
            }

            var actions = game.GetHumanLegalActions();
            var matchingSlash = actions.Where(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == slash.Id &&
                action.TargetSeat == primaryTargetSeat).ToArray();
            var normal = matchingSlash.SingleOrDefault(action => action.PlayedCardKind is null);
            var fire = matchingSlash.SingleOrDefault(action => action.PlayedCardKind == CardKind.FireSlash);
            var thunder = actions.SingleOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == thunderSlash.Id &&
                action.TargetSeat == primaryTargetSeat &&
                action.PlayedCardKind is null);
            if (normal is null || fire is null || thunder is null)
            {
                continue;
            }

            return new ZhuqueFanBoundary(
                registry,
                game,
                game.CreateCheckpoint(),
                normal,
                fire,
                thunder,
                weapon.Id,
                sourceSeat,
                primaryTargetSeat,
                chainedTargetSeat);
        }

        throw new InvalidOperationException(
            "No bounded Zhuque Fan fixture exposed the physical Slash conversion boundary.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(new Version(1, 63, 0)),
        new ZhuqueFanScenarioPackage());

    private sealed class ZhuqueFanScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "zhuque-fan-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 32, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe(
                "zhuque-fan-scenario:deck",
                "朱雀羽扇属性杀测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:zhuque-fan", 10),
                    new ContentDeckCardCount("standard:slash", 10),
                    new ContentDeckCardCount("standard:thunder_slash", 10),
                    new ContentDeckCardCount("standard:iron_chain", 10)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人经典身份（朱雀羽扇场景）",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "zhuque-fan-scenario:deck",
                GeneralCandidateCount: 3,
                GeneralPoolIds:
                [
                    "classic:sun-quan",
                    "classic:huang-gai",
                    "classic:gan-ning",
                    "classic:lu-meng",
                    "classic:zhang-liao"
                ]));
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
