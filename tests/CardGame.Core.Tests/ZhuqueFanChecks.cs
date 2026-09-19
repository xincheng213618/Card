using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuqueFanChecks
{
    public static void FireConversionChainJijiangAndLegacyBoundary()
    {
        var boundary = ZhuqueFanScenario.FindHumanChainedSlash();
        VerifyPhysicalSlashConversion(boundary);
        VerifyNormalAndLegacyBranches(boundary);
        VerifyJijiangOwnerChoice();
    }

    private static void VerifyPhysicalSlashConversion(ZhuqueFanBoundary boundary)
    {
        var game = boundary.Game;
        var before = game.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var source = before.Players[boundary.SourceSeat];
        var primary = before.Players[boundary.PrimaryTargetSeat];
        var chained = before.Players[boundary.ChainedTargetSeat];
        Require(source.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.ZhuqueFan) &&
                primary.IsChained && chained.IsChained,
            "The formal Zhuque Fan fixture must expose the weapon and two chained targets.");

        var matching = game.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == boundary.NormalSlashAction.CardId &&
            action.TargetSeat == boundary.PrimaryTargetSeat).ToArray();
        Require(matching.Length == 2 &&
                matching.Count(action => action.PlayedCardKind is null) == 1 &&
                matching.Count(action => action.PlayedCardKind == CardKind.FireSlash) == 1,
            "One physical ordinary Slash must publish exactly normal and Zhuque Fire Slash actions.");
        var thunder = game.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == boundary.ThunderSlashAction.CardId &&
            action.TargetSeat == boundary.PrimaryTargetSeat).ToArray();
        Require(thunder.Length == 1 && thunder[0].PlayedCardKind is null,
            "Zhuque Fan must not convert a physical Thunder Slash a second time.");

        var ai = new SimpleAiBrain(boundary.SourceSeat, 51001, policyVersion: 2);
        var (_, thought) = ai.ChoosePlay(
            game.CreateSnapshot(boundary.SourceSeat),
            game.GetHumanLegalActions(),
            thoughtSequence: 1);
        var candidates = thought.Candidates.Where(candidate =>
            candidate.Action.CardId == boundary.NormalSlashAction.CardId &&
            candidate.Action.TargetSeat == boundary.PrimaryTargetSeat).ToArray();
        var normalScore = candidates.Single(candidate => candidate.Action.PlayedCardKind is null);
        var fireScore = candidates.Single(candidate =>
            candidate.Action.PlayedCardKind == CardKind.FireSlash);
        Require(fireScore.Score > normalScore.Score &&
                fireScore.Reason.Contains("朱雀羽扇", StringComparison.Ordinal) &&
                fireScore.Reason.Contains("公开连环", StringComparison.Ordinal),
            "Zhuque Fan AI must score the explicit Fire Slash branch only from public chain state.");

        var played = PlaySlash(game, boundary.FireSlashAction);
        Require(played.Accepted, played.Error?.Message ?? "Zhuque Fan Fire Slash was rejected.");
        SettleCard(game, boundary.FireSlashAction.CardId!.Value);
        var after = game.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var events = game.Events.Select(item => item.Payload).ToArray();
        var converted = events.OfType<ZhuqueFanConvertedEvent>().Single(item =>
            item.SourceSeat == boundary.SourceSeat &&
            item.TargetSeats.SequenceEqual([boundary.PrimaryTargetSeat]));
        var damage = events.OfType<DamageRequestedEvent>().Where(item =>
            item.SourceSeat == boundary.SourceSeat &&
            item.Nature == DamageNature.Fire &&
            (item.TargetSeat == boundary.PrimaryTargetSeat ||
             item.TargetSeat == boundary.ChainedTargetSeat)).ToArray();
        var propagation = events.OfType<ChainedDamagePropagatedEvent>().Single(item =>
            item.FromSeat == boundary.PrimaryTargetSeat &&
            item.TargetSeat == boundary.ChainedTargetSeat);
        var used = events.OfType<CardUsedEvent>().Single(item =>
            item.CardId == boundary.FireSlashAction.CardId &&
            item.TargetSeat == boundary.PrimaryTargetSeat);
        Require(converted.PhysicalCardIds.SequenceEqual([boundary.FireSlashAction.CardId!.Value]) &&
                used.CardKind == CardKind.FireSlash &&
                damage.Length == 2 &&
                propagation.Nature == DamageNature.Fire &&
                after.Players[boundary.PrimaryTargetSeat].Hp == primary.Hp - 1 &&
                after.Players[boundary.ChainedTargetSeat].Hp == chained.Hp - 1 &&
                Array.IndexOf(events, converted) < Array.IndexOf(events, damage[0]),
            "Zhuque Fan must announce the conversion before Fire damage and one chained propagation.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed Zhuque Fan Fire Slash must replay exactly.");
    }

    private static void VerifyNormalAndLegacyBranches(ZhuqueFanBoundary boundary)
    {
        var normal = GameReplay.Restore(RoundTrip(boundary.BeforeSlash), boundary.Registry);
        var before = normal.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var normalAction = normal.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == boundary.NormalSlashAction.CardId &&
            action.TargetSeat == boundary.PrimaryTargetSeat &&
            action.PlayedCardKind is null);
        Require(PlaySlash(normal, normalAction).Accepted,
            "The normal Slash branch was rejected while Zhuque Fan was equipped.");
        SettleCard(normal, normalAction.CardId!.Value);
        var normalAfter = normal.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var normalDamage = normal.Events.Select(item => item.Payload)
            .OfType<DamageRequestedEvent>()
            .Where(item => item.SourceSeat == boundary.SourceSeat &&
                           item.TargetSeat == boundary.PrimaryTargetSeat)
            .Last();
        Require(normalDamage.Nature == DamageNature.Normal &&
                normalAfter.Players[boundary.PrimaryTargetSeat].Hp ==
                    before.Players[boundary.PrimaryTargetSeat].Hp - 1 &&
                normalAfter.Players[boundary.ChainedTargetSeat].Hp ==
                    before.Players[boundary.ChainedTargetSeat].Hp &&
                normalAfter.Players[boundary.PrimaryTargetSeat].IsChained &&
                normalAfter.Players[boundary.ChainedTargetSeat].IsChained &&
                normal.Events.Select(item => item.Payload).All(item =>
                    item is not ZhuqueFanConvertedEvent and not ChainedDamagePropagatedEvent),
            "Keeping the physical Slash normal must not deal elemental or propagated damage.");

        var legacy = GameReplay.Restore(
            RoundTrip(boundary.BeforeSlash) with { RulesVersion = 50 },
            boundary.Registry);
        var legacyActions = legacy.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == boundary.NormalSlashAction.CardId &&
            action.TargetSeat == boundary.PrimaryTargetSeat).ToArray();
        Require(legacyActions.Length == 1 && legacyActions[0].PlayedCardKind is null,
            "Rules v50 must keep only the pre-Zhuque ordinary Slash action.");
        Require(PlaySlash(legacy, legacyActions[0]).Accepted,
            "Rules v50 ordinary Slash control was rejected.");
        SettleCard(legacy, legacyActions[0].CardId!.Value);
        Require(legacy.Events.Select(item => item.Payload).All(item => item is not ZhuqueFanConvertedEvent),
            "Rules v50 must never publish a Zhuque Fan conversion event.");
    }

    private static void VerifyJijiangOwnerChoice()
    {
        var (registry, modeId) = CreateJijiangRegistry();
        GameEngine? selected = null;
        PendingDecision? ownerPrompt = null;
        int targetSeat = -1;
        for (var seed = 1; seed <= 2_048 && selected is null; seed++)
        {
            var game = StartJijiangGame(registry, modeId, seed);
            var source = game.CreateSnapshot(0, revealAll: true).Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ZhuqueFan);
            if (weapon is null || game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(0, weapon.Id, [], game.Revision, play.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ?? "Jijiang Zhuque fixture could not equip its weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Jijiang Zhuque fixture did not resume play after equipping.");
            }
            var jijiang = game.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Jijiang);
            var full = game.CreateSnapshot(0, revealAll: true);
            var rebel = jijiang.SelectableTargetSeats.FirstOrDefault(seat =>
                full.Players[seat].Role == Role.Rebel, -1);
            if (rebel < 0 || game.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt)
            {
                continue;
            }

            var requested = game.Submit(new UseSkillCommand(
                0,
                SkillKind.Jijiang,
                [],
                [rebel],
                game.Revision,
                prompt.PromptId));
            Require(requested.Accepted, requested.Error?.Message ?? "Jijiang Zhuque request was rejected.");
            var providerPrompt = Enumerable.Range(1, game.PlayerCount - 1)
                .Select(seat => game.CreateSnapshot(seat).PendingDecision)
                .FirstOrDefault(decision => decision?.Kind == DecisionKind.RespondSlash);
            Require(providerPrompt is not null && providerPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("response-card-kind") != CardKind.FireSlash.ToString()),
                "The Shu provider prompt must not decide Liu Bei's Zhuque Fan conversion.");

            for (var step = 0; step < 24 && game.PendingDecision?.Kind != DecisionKind.ZhuqueFan; step++)
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "The Jijiang provider cursor did not advance to Zhuque Fan.");
            }
            var privatePrompt = game.CreateSnapshot(0).PendingDecision;
            if (privatePrompt is not { Kind: DecisionKind.ZhuqueFan })
            {
                continue;
            }

            Require(Enumerable.Range(1, game.PlayerCount - 1)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The Zhuque Fan decision after Jijiang must be private to Liu Bei.");
            selected = game;
            ownerPrompt = privatePrompt;
            targetSeat = rebel;
        }

        if (selected is null || ownerPrompt is null)
        {
            throw new InvalidOperationException("No bounded Jijiang fixture reached Liu Bei's Zhuque Fan choice.");
        }

        var paused = GameReplay.Restore(RoundTrip(selected.CreateCheckpoint()), registry);
        Require(State(paused) == State(selected) && Events(paused).SequenceEqual(Events(selected)),
            "A paused post-Jijiang Zhuque Fan choice must replay exactly.");
        var fire = ownerPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhuque-fan-fire");
        var answered = selected.Submit(new AnswerPromptCommand(
            0,
            ownerPrompt.PromptId,
            fire.Id,
            selected.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Liu Bei's Zhuque Fan choice was rejected.");
        for (var step = 0; step < 64 && !selected.Events.Select(item => item.Payload)
                     .OfType<DamageRequestedEvent>()
                     .Any(item => item.SourceSeat == 0 && item.TargetSeat == targetSeat); step++)
        {
            Require(selected.Submit(new AdvanceOneStepCommand(selected.Revision)).Accepted,
                "The post-Jijiang Fire Slash did not settle.");
        }

        var resolved = selected.Events.Select(item => item.Payload)
            .OfType<JijiangResolvedEvent>()
            .Last(item => item is { IsActiveUse: true, Succeeded: true });
        var converted = selected.Events.Select(item => item.Payload)
            .OfType<ZhuqueFanConvertedEvent>()
            .Last(item => item.SourceSeat == 0);
        var damage = selected.Events.Select(item => item.Payload)
            .OfType<DamageRequestedEvent>()
            .Last(item => item.SourceSeat == 0 && item.TargetSeat == targetSeat);
        Require(resolved.EffectiveSlashKind == CardKind.FireSlash &&
                resolved.ProviderSeat is not null &&
                resolved.SlashCardId is { } slashCardId &&
                converted.PhysicalCardIds.Contains(slashCardId) &&
                damage.Nature == DamageNature.Fire,
            "Jijiang must let Liu Bei convert the provider's ordinary Slash into one Fire Slash after provision.");
    }

    private static (ContentRegistry Registry, string ModeId) CreateJijiangRegistry()
    {
        const string modeId = "identity:classic-jijiang-zhuque-test";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new JijiangZhuquePackage(modeId));
        return (registry, modeId);
    }

    private static GameEngine StartJijiangGame(ContentRegistry registry, string modeId, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = modeId,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 80,
            AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted,
            "Jijiang Zhuque fixture failed to start.");
        var setup = game.PendingDecision ??
            throw new InvalidOperationException("Jijiang Zhuque fixture has no general prompt.");
        Require(game.Submit(new SelectGeneralCommand(
            0,
            "classic:liu-bei",
            game.Revision,
            setup.PromptId)).Accepted,
            "Jijiang Zhuque fixture could not select Liu Bei.");
        Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
            "Jijiang Zhuque fixture did not reach play.");
        return game;
    }

    private sealed class JijiangZhuquePackage(string modeId) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "jijiang-zhuque-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 32, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe(
                "jijiang-zhuque-test:deck",
                "激将朱雀羽扇测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("standard:slash", 90),
                    new ContentDeckCardCount("classic:zhuque-fan", 10)
                ]));
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "激将朱雀羽扇测试身份局",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "jijiang-zhuque-test:deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds:
                [
                    "classic:liu-bei",
                    "standard:zhang-fei",
                    "standard:liu-bei",
                    "standard:zhuge-liang",
                    "classic:zhuge-liang"
                ]));
        }
    }

    private static CommandResult PlaySlash(GameEngine game, LegalAction action) =>
        game.Submit(new PlayCardCommand(
            game.State.HumanSeat,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind));

    private static void SettleCard(GameEngine game, int cardId)
    {
        for (var step = 0; step < 256 && !game.Events.Select(item => item.Payload)
                     .OfType<CardUseFinishedEvent>()
                     .Any(item => item.CardId == cardId); step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(candidate => candidate.Cards.Count == 0) ??
                    throw new InvalidOperationException($"Unexpected human prompt while settling Zhuque Fan: {prompt.Kind}.");
                Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)).Accepted,
                    "Zhuque Fan control prompt could not be declined.");
            }
            else
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Zhuque Fan Slash did not advance.");
            }
        }
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
