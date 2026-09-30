using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record QinglongCrescentBladeBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int TargetSeat,
    int WeaponCardId);

internal static class QinglongCrescentBladeScenario
{
    public static QinglongCrescentBladeBoundary FindHumanTrigger(bool requireFactionSlash = false)
    {
        var registry = requireFactionSlash
            ? ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
                new FactionFollowupScenarioPackage())
            : StandardContentRegistry.CreateWithClassicGenerals();
        var maxSeeds = requireFactionSlash ? 128 : 32_768;
        for (var seed = 1; seed <= maxSeeds; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = requireFactionSlash ? "identity:classic-qinglong-faction-5" : "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Qinglong Crescent Blade fixture failed to start.");
            var generalChoice = requireFactionSlash
                ? game.PendingDecision?.Choices.FirstOrDefault(choice =>
                    choice.ContentIds.SequenceEqual(["classic:liu-bei"]))
                : game.PendingDecision?.Choices.FirstOrDefault(choice =>
                    choice.ContentIds.Count == 1 &&
                    registry.Generals[choice.ContentIds[0]].SkillIds
                        .Select(registry.GetSkill)
                        .All(skill => skill.Id is not (
                            "classic:tieqi" or
                            "classic:liegong" or
                            "classic:wusheng" or
                            "classic:longdan")) &&
                    // The private follow-up prompt lists conversion-eligible cards, so a
                    // general whose program turns other cards into Slash breaks the naive
                    // physical-hand expectation below. Keep such generals out of this fixture.
                    registry.Generals[choice.ContentIds[0]].SkillIds
                        .Select(registry.GetSkill)
                        .All(skill => skill.Program is not { } program ||
                            program.CardIdentities.All(identity => identity.OutputKind != CardKind.Slash) &&
                            program.ViewAs.All(view => view.OutputKind != CardKind.Slash)));
            if (generalChoice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                0,
                generalChoice.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId)).Accepted,
                "Qinglong Crescent Blade fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Qinglong Crescent Blade fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.QinglongCrescentBlade);
            if (weapon is null ||
                source.Hand.Count(card =>
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) <
                    (requireFactionSlash ? 1 : 2))
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ??
                "Qinglong Crescent Blade fixture could not equip the weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Qinglong Crescent Blade fixture did not return to play after equipping.");
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip)
            {
                continue;
            }

            full = game.CreateSnapshot(0, revealAll: true);
            source = full.Players[0];
            var slash = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.Slash &&
                    action.CardId is not null &&
                    action.TargetSeat is not null &&
                    source.Hand.Any(card =>
                        card.Id == action.CardId &&
                        card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item =>
                    item.Target.Hp > 1 &&
                    item.Target.Hand.Any(card => card.Kind == CardKind.Dodge) &&
                    item.Target.Equipment.All(card =>
                        card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                    item.Target.Skills?.All(skill =>
                        skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
                .Where(item => !requireFactionSlash || HasHelpingShuProvider(
                    full,
                    registry,
                    item.Target.Seat))
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (slash is null)
            {
                continue;
            }

            var beforeSlash = game.CreateCheckpoint();
            var played = game.Submit(new PlayCardCommand(
                0,
                slash.Action.CardId!.Value,
                slash.Action.TargetSeats,
                game.Revision,
                afterEquip.PromptId,
                slash.Action.PlayedCardKind));
            Require(played.Accepted, played.Error?.Message ??
                "Qinglong Crescent Blade fixture could not use Slash.");
            for (var step = 0; step < 16 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.QinglongCrescentBlade, PlayerSeat: 0 })
                {
                    if (requireFactionSlash && !game.PendingDecision.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("action") == "qinglong-jijiang"))
                    {
                        break;
                    }

                    return new QinglongCrescentBladeBoundary(
                        registry,
                        game,
                        beforeSlash,
                        slash.Action,
                        slash.Target.Seat,
                        weapon.Id);
                }

                if (game.PendingDecision is { PlayerSeat: 0 })
                {
                    break;
                }

                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Qinglong Crescent Blade fixture could not advance the target's Dodge response.");
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Qinglong Crescent Blade trigger with a human source was found.");
    }

    private static bool HasHelpingShuProvider(
        GameSnapshot snapshot,
        ContentRegistry registry,
        int targetSeat) =>
        snapshot.Players.Any(player =>
            player.Seat != 0 &&
            player.Seat != targetSeat &&
            player.Role is Role.Loyalist or Role.Renegade &&
            string.Equals(player.FactionId ?? registry.Generals[player.GeneralId].FactionId,
                "shu", StringComparison.Ordinal) &&
            (player.Hand.Any(card => card.Kind is
                 CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
             player.Equipment.Any(card => card.Kind == CardKind.ZhangbaSerpentSpear) &&
             player.Hand.Count >= 2));

    private sealed class FactionFollowupScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("qinglong-faction-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 0, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("qinglong-faction-scenario:deck", "青龙激将场景牌堆", 4, 2,
                [new ContentDeckCardCount("classic:qinglong-crescent-blade", 8),
                    new ContentDeckCardCount("standard:slash", 24),
                    new ContentDeckCardCount("standard:dodge", 28)]));
            builder.AddMode(new ContentModeDefinition("identity:classic-qinglong-faction-5", "青龙激将场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "qinglong-faction-scenario:deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:liu-bei", "classic:guan-yu", "classic:zhang-fei",
                    "classic:huang-yueying", "classic:lu-xun"]));
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
