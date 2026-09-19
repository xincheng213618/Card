using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record FangtianHalberdBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    CardSnapshot Slash,
    int WeaponCardId,
    IReadOnlyList<LegalAction> SlashActions);

internal static class FangtianHalberdScenario
{
    private const string ModeId = "identity:classic-fangtian-5";
    private const string ResponseModeId = "identity:classic-fangtian-response-5";

    public static FangtianHalberdBoundary FindHumanLastHandSlash(bool requireFirstTargetDodge = false)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = requireFirstTargetDodge ? ResponseModeId : ModeId,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 40,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Fangtian Halberd fixture failed to start.");
            var prompt = game.PendingDecision;
            var choice = prompt?.Choices.FirstOrDefault(candidate =>
                candidate.ContentIds.Count == 1 &&
                registry.Generals[candidate.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.LegacyKind is not (SkillKind.Tieqi or SkillKind.Liegong)));
            if (prompt is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } || choice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                0,
                choice.ContentIds[0],
                game.Revision,
                prompt.PromptId)).Accepted,
                "Fangtian Halberd fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Fangtian Halberd fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var source = game.CreateSnapshot(0, revealAll: true).Players[0];
            if (source.Hand.Count != 2)
            {
                continue;
            }

            var weapon = source.Hand.SingleOrDefault(card => card.Kind == CardKind.FangtianHalberd);
            var slash = source.Hand.SingleOrDefault(card => card.Kind == CardKind.Slash);
            if (weapon is null || slash is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted,
                equipped.Error?.Message ?? "Fangtian Halberd fixture could not equip the weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Fangtian Halberd fixture did not resume play after equipping.");
            }

            var slashActions = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.Slash &&
                    action.CardId == slash.Id)
                .ToArray();
            if (!slashActions.Any(action => action.TargetSeats.Count == 3))
            {
                continue;
            }

            if (requireFirstTargetDodge)
            {
                var full = game.CreateSnapshot(0, revealAll: true);
                var hasResponsiveCombination = slashActions.Any(action =>
                    action.TargetSeats.Count == 3 &&
                    full.Players[action.TargetSeats[0]].Hand.Any(card => card.Kind == CardKind.Dodge));
                if (!hasResponsiveCombination)
                {
                    continue;
                }
            }

            return new FangtianHalberdBoundary(
                registry,
                game,
                game.CreateCheckpoint(),
                slash,
                weapon.Id,
                slashActions);
        }

        throw new InvalidOperationException(
            "No bounded Fangtian Halberd fixture dealt the human source exactly the weapon and one Slash.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new FangtianScenarioPackage());

    private sealed class FangtianScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "fangtian-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 30, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe(
                "fangtian-scenario:deck",
                "方天画戟最后手牌杀测试牌堆",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:fangtian-halberd", 8),
                    new ContentDeckCardCount("standard:slash", 24),
                    new ContentDeckCardCount("standard:peach", 28)
                ]));
            builder.AddDeck(new ContentDeckRecipe(
                "fangtian-scenario:response-deck",
                "方天画戟逐目标响应测试牌堆",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:fangtian-halberd", 8),
                    new ContentDeckCardCount("standard:slash", 24),
                    new ContentDeckCardCount("standard:dodge", 28)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人经典身份（方天画戟场景）",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "fangtian-scenario:deck",
                GeneralCandidateCount: 3,
                GeneralPoolIds:
                [
                    "classic:sun-quan",
                    "classic:huang-gai",
                    "classic:gan-ning",
                    "classic:lu-meng",
                    "classic:zhang-liao"
                ]));
            builder.AddMode(new ContentModeDefinition(
                ResponseModeId,
                "五人经典身份（方天画戟逐目标响应场景）",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "fangtian-scenario:response-deck",
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
