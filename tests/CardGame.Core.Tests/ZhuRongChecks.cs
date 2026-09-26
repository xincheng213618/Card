using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuRongChecks
{
    private const string LeijiFixtureMode = "identity:classic-leiji-gameover-5";

    public static void LeijiWinningDamageCleansParentSlash()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new LeijiFixtureModePackage());
        var game = Create(296, registry, LeijiFixtureMode);
        Require(SelectZhuRong(game), "The fixed Leiji cleanup fixture could not select Zhu Rong.");
        for (var step = 0; step < 900 && game.State.Winner == Winner.None; step++)
        {
            var checkpoint = game.CreateCheckpoint();
            Require(AdvanceAggressively(game), $"Leiji cleanup fixture stopped at step {step}.");
            if (game.State.Winner == Winner.None) continue;
            var replayed = GameReplay.Restore(checkpoint, registry);
            Require(AdvanceAggressively(replayed) &&
                    SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                "Winning Leiji command must replay the same terminal state.");
        }
        Require(game.State.Winner == Winner.Rebels &&
                game.ResolutionStack.Count == 0 &&
                game.CreateCardZoneDiagnostics().All(item => item.Location != CardLocation.Processing) &&
                game.Events.Select(item => item.Payload).OfType<GameEndedEvent>().Count() == 1 &&
                game.CardMovements.Count(item => item.CardId == 128 &&
                    item.From == CardLocation.Processing && item.Reason == CardMoveReasons.UseFinished) == 1,
            "A winning Leiji damage must release its interrupted Slash and all Processing cards.");
    }

    public static void JuxiangAndLierenReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var juxiangVerified = false;
        var lierenVerified = false;
        for (var seed = 1; seed <= 4096 && (!juxiangVerified || !lierenVerified); seed++)
        {
            var game = Create(seed, registry);
            if (!SelectZhuRong(game)) continue;
            for (var step = 0; step < 900 && game.State.Winner == Winner.None; step++)
            {
                var claim = game.Events.Select(e => e.Payload).OfType<JuxiangCardClaimedEvent>().LastOrDefault();
                if (claim is not null)
                {
                    var used = game.Events.Select(e => e.Payload).OfType<GroupCardUsedEvent>()
                        .Last(e => e.ResolutionId == claim.ResolutionId);
                    Require(claim.OwnerSeat == 0 && !used.TargetSeats.Contains(0) &&
                            claim.CardIds.All(id => game.CardMovements.Any(move =>
                                move.CardId == id && move.Reason == CardMoveReasons.JuxiangGain)),
                        "Juxiang must make Barbarian Assault ineffective and claim its discarded physical cards.");
                    juxiangVerified = true;
                }
                if (game.PendingDecision is
                    { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: "classic:lieren" } offer &&
                    offer.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                {
                    var checkpoint = game.CreateCheckpoint();
                    ResolveLieren(game);
                    var result = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>()
                        .LastOrDefault(e => e.SkillId == "classic:lieren");
                    var resolved = game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
                        .LastOrDefault(e => e.SkillId == "classic:lieren");
                    var gained = game.CardMovements.Any(move =>
                        move.Reason == new CardMoveReason("skill-program.classic:lieren.MoveBoundCards"));
                    Require(resolved is { OwnerSeat: 0, Completed: true } && result is not null &&
                            (!gained || result.Result.SourceWon),
                        "Lieren must resolve its physical Pindian before a conditional target-card gain.");
                    var restored = GameReplay.Restore(checkpoint, registry);
                    ResolveLieren(restored);
                    Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                        "A paused Lieren trigger must replay exactly.");
                    if (result?.Result.SourceWon == true && gained)
                    {
                        lierenVerified = true;
                        break;
                    }
                }
                try { if (!AdvanceAggressively(game)) break; }
                catch (Exception exception)
                {
                    var count = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>()
                        .Count(e => e.SkillId == "classic:lieren");
                    throw new InvalidOperationException($"seed={seed}, step={step}, lierenPindian={count}", exception);
                }
            }
        }
        Require(juxiangVerified && lierenVerified, "No bounded Zhu Rong fixtures verified Juxiang and Lieren.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry, string modeId = "identity:classic-5") => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = modeId, UseInteractiveSetup = true, UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false, MaxTurns = 80
    }, registry);

    private static bool SelectZhuRong(GameEngine game) =>
        game.Submit(new StartGameCommand()).Accepted &&
        game.PendingDecision is { Kind: DecisionKind.SelectGeneral } setup &&
        setup.ValidContentIds.Contains("classic:zhu-rong") &&
        game.Submit(new SelectGeneralCommand(0, "classic:zhu-rong", game.Revision, setup.PromptId)).Accepted;

    private static void ResolveLieren(GameEngine game)
    {
        var before = game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
            .Count(e => e.SkillId == "classic:lieren");
        var trace = new List<string>();
        for (var step = 0; step < 12; step++)
        {
            if (game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
                .Count(e => e.SkillId == "classic:lieren") > before) return;
            trace.Add($"{step}:{game.PendingDecision?.Kind}/{game.PendingDecision?.SkillPrompt?.SkillId}/{game.PendingDecision?.PlayerSeat}");
            if (game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: "classic:lieren" } decision)
            {
                if (decision.Kind == DecisionKind.SkillModule)
                {
                    var contest = game.ResolutionStack.OfType<PindianFrame>().Single();
                    var parentCards = game.ResolutionStack.OfType<CardUseFrame>().Last().PhysicalCardIds ?? [];
                    var processing = game.CreateCardZoneDiagnostics()
                        .Where(item => item.Location == CardLocation.Processing)
                        .Select(item => item.CardId).ToHashSet();
                    Require(parentCards.All(processing.Contains) &&
                            parentCards.All(contest.ParentProcessingCardIds!.Contains),
                        "Nested Lieren Pindian must retain the parent Slash in Processing.");
                }
                var choice = decision.Kind == DecisionKind.SkillModule
                    ? decision.Choices.OrderByDescending(c => game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                        .First(card => card.Id == c.Cards.Single()).Rank).First()
                    : decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "activate") ??
                      decision.Choices.First();
                Require(game.Submit(new AnswerPromptCommand(0, decision.PromptId, choice.Id, game.Revision)).Accepted,
                    "Human Lieren choice was rejected.");
            }
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "AI Lieren continuation could not advance.");
        }
        throw new InvalidOperationException($"Lieren did not finish: {string.Join(';', trace)}");
    }

    private static bool AdvanceAggressively(GameEngine game)
    {
        if (game.PendingDecision is not { } pending)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
        {
            var slash = game.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.Slash);
            if (slash is not null)
                return game.Submit(new PlayCardCommand(0, slash.CardId!.Value, [slash.TargetSeat!.Value],
                    game.Revision, pending.PromptId, slash.PlayedCardKind)).Accepted;
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        }
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(c =>
            c.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true) ??
            pending.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LeijiFixtureModePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("leiji-gameover-fixture", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder) => builder.AddMode(new ContentModeDefinition(
            LeijiFixtureMode, "Leiji terminal Slash fixture", 5, 5,
            new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            },
            "classic:standard-deck", GeneralCandidateCount: 3,
            GeneralPoolIds:
            [
                "classic:liu-bei", "classic:sun-quan", "classic:sima-yi", "classic:xiahou-dun",
                "classic:hua-tuo", "classic:cao-cao", "classic:zhang-liao", "classic:xu-chu",
                "classic:dian-wei", "classic:xu-huang", "classic:zhen-ji", "classic:huang-yueying",
                "classic:ma-chao", "classic:huang-zhong", "classic:wei-yan", "classic:lu-bu",
                "classic:huang-gai", "classic:gan-ning", "classic:lu-meng", "classic:zhang-fei",
                "classic:zhou-yu", "classic:zhuge-liang", "classic:guan-yu", "classic:zhao-yun",
                "classic:guo-jia", "classic:da-qiao", "classic:diao-chan", "classic:sun-shangxiang",
                "classic:lu-xun", "classic:pang-de", "classic:xun-yu", "classic:yan-liang-wen-chou",
                "classic:wolong-zhuge-liang", "classic:pang-tong", "classic:taishi-ci", "classic:cao-ren",
                "classic:xiao-qiao", "classic:zhou-tai", "classic:yuan-shao", "classic:xiahou-yuan",
                "classic:hua-xiong", "classic:gongsun-zan", "classic:zhang-jiao", "classic:sun-jian",
                "classic:meng-huo", "classic:zhu-rong"
            ]));
    }
}
