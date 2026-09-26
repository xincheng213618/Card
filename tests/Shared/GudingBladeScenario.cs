using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record GudingBladeBoundary(
    ContentRegistry Registry,
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int WeaponCardId,
    int SourceSeat,
    int TargetSeat,
    int TargetHandCount);

internal static class GudingBladeScenario
{
    private const string ModeId = "identity:classic-guding-5";

    public static GudingBladeBoundary FindHumanSlash(bool requireEmptyTarget)
    {
        const int sourceSeat = 0;
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
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
                "Guding Blade fixture failed to start.");
            var setup = game.PendingDecision;
            var general = setup?.Choices.FirstOrDefault(candidate =>
                candidate.ContentIds.Count == 1 &&
                registry.Generals[candidate.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.LegacyKind is not (SkillKind.Tieqi or SkillKind.Liegong)));
            if (setup is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: sourceSeat } || general is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                sourceSeat,
                general.ContentIds[0],
                game.Revision,
                setup.PromptId)).Accepted,
                "Guding Blade fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Guding Blade fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } play)
            {
                continue;
            }

            var source = game.CreateSnapshot(sourceSeat, revealAll: true).Players[sourceSeat];
            if (source.Hand.Count != 2)
            {
                continue;
            }

            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.GudingBlade);
            var slash = source.Hand.FirstOrDefault(card => card.Kind == CardKind.Slash);
            if (weapon is null || slash is null)
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
                equipped.Error?.Message ?? "Guding Blade fixture could not equip the weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Guding Blade fixture did not resume play after equipping.");
            }

            if (!requireEmptyTarget && TryFindSlash(game, slash.Id, requireEmptyTarget, out var immediate))
            {
                return CreateBoundary(registry, game, immediate, weapon.Id, sourceSeat);
            }

            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip)
            {
                continue;
            }
            Require(game.Submit(new EndPlayPhaseCommand(
                sourceSeat,
                game.Revision,
                afterEquip.PromptId)).Accepted,
                "Guding Blade fixture could not preserve its Slash for a later round.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var full = game.CreateSnapshot(sourceSeat, revealAll: true);
                source = full.Players[sourceSeat];
                if (!source.IsAlive ||
                    source.Equipment.All(card => card.Id != weapon.Id) ||
                    source.Hand.All(card => card.Id != slash.Id))
                {
                    break;
                }

                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } humanPlay)
                {
                    if (TryFindSlash(game, slash.Id, requireEmptyTarget, out var action))
                    {
                        return CreateBoundary(registry, game, action, weapon.Id, sourceSeat);
                    }

                    Require(game.Submit(new EndPlayPhaseCommand(
                        sourceSeat,
                        game.Revision,
                        humanPlay.PromptId)).Accepted,
                        "Guding Blade fixture could not wait for an empty-hand target.");
                    continue;
                }

                GameCommand command;
                if (game.PendingDecision is { PlayerSeat: sourceSeat } prompt)
                {
                    var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ??
                        prompt.Choices.First();
                    command = new AnswerPromptCommand(
                        sourceSeat,
                        prompt.PromptId,
                        decline.Id,
                        game.Revision);
                }
                else
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            $"No bounded Guding Blade fixture found a {(requireEmptyTarget ? "empty" : "non-empty")}-hand Slash target.");
    }

    private static bool TryFindSlash(
        GameEngine game,
        int slashCardId,
        bool requireEmptyTarget,
        out LegalAction action)
    {
        var full = game.CreateSnapshot(0, revealAll: true);
        action = game.GetHumanLegalActions()
            .Where(candidate =>
                candidate.Kind == LegalActionKind.Slash &&
                candidate.CardId == slashCardId &&
                candidate.TargetSeat is not null)
            .Where(candidate =>
            {
                var target = full.Players[candidate.TargetSeat!.Value];
                return (requireEmptyTarget ? target.HandCount == 0 : target.HandCount > 0) &&
                       target.Hp > 2 &&
                       target.Equipment.All(card =>
                           card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                       target.Skills?.All(skill =>
                           skill.Kind is not (SkillKind.Qingguo or SkillKind.Longdan or SkillKind.Hujia)) != false;
            })
            .OrderBy(candidate => candidate.TargetSeat)
            .FirstOrDefault()!;
        return action is not null;
    }

    private static GudingBladeBoundary CreateBoundary(
        ContentRegistry registry,
        GameEngine game,
        LegalAction action,
        int weaponCardId,
        int sourceSeat)
    {
        var targetSeat = action.TargetSeat ??
            throw new InvalidOperationException("A Guding Blade Slash requires one target.");
        var target = game.CreateSnapshot(sourceSeat, revealAll: true).Players[targetSeat];
        return new GudingBladeBoundary(
            registry,
            game,
            game.CreateCheckpoint(),
            action,
            weaponCardId,
            sourceSeat,
            targetSeat,
            target.HandCount);
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new GudingScenarioPackage());

    private sealed class GudingScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "guding-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 31, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe(
                "guding-scenario:deck",
                "古锭刀空手增伤测试牌堆",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:guding-blade", 8),
                    new ContentDeckCardCount("standard:slash", 24),
                    new ContentDeckCardCount("standard:crossbow", 28)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人经典身份（古锭刀场景）",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "guding-scenario:deck",
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
