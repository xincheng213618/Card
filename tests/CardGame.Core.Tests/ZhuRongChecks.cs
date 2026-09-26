using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuRongChecks
{
    private const string TerminalLeijiMode = "identity:classic-leiji-terminal-4";

    public static void LeijiWinningDamageCleansParentSlash()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new TerminalLeijiPackage());
        var game = ClassicZhangJiaoProgramChecks.FindLeijiActivation(
            registry, modeId: TerminalLeijiMode, playerCount: 4);
        var rebelSeat = game.CreateSnapshot(0, revealAll: true).Players.Single(item => item.Role == Role.Rebel).Seat;
        var parentSlash = game.ResolutionStack.OfType<CardUseFrame>().Last(item =>
            item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
            item.TargetSeats.Contains(0));
        Require(game.CreateCardZoneDiagnostics().Any(item =>
                item.CardId == parentSlash.CardId && item.Location == CardLocation.Processing),
            "The Slash which Zhang Jiao dodged must remain in Processing during the Leiji choice.");
        var before = game.CreateCheckpoint();
        var replayed = GameReplay.Restore(before, registry);
        ResolveTerminalLeiji(game, rebelSeat);
        ResolveTerminalLeiji(replayed, rebelSeat);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replayed.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(game.Events.Select(item => item.Payload.GetType().Name)),
            "Winning Leiji must replay from its preceding Dodge-trigger choice.");
        var terminalDamage = game.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentDamageRequestedEvent>()
            .LastOrDefault(item => item.SkillId == "classic:leiji" && item.TargetSeat == rebelSeat);
        Require(game.State.Winner == Winner.LordAndLoyalists &&
                terminalDamage is { SourceSeat: 0, Amount: 2, Nature: DamageNature.Thunder } &&
                game.ResolutionStack.Count == 0 &&
                game.CreateCardZoneDiagnostics().All(item => item.Location != CardLocation.Processing) &&
                game.Events.Select(item => item.Payload).OfType<GameEndedEvent>().Count() == 1 &&
                game.CardMovements.Count(item => item.CardId == parentSlash.CardId &&
                    item.From == CardLocation.Processing && item.Reason == CardMoveReasons.UseFinished) == 1,
            "A winning configured Leiji must finish its interrupted physical Slash once and release all frames and Processing cards.");
    }

    private static void ResolveTerminalLeiji(GameEngine game, int rebelSeat)
    {
        var activation = game.PendingDecision ?? throw new InvalidOperationException("Leiji activation was lost.");
        var activate = activation.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(game.Submit(new AnswerPromptCommand(0, activation.PromptId, activate.Id, game.Revision)).Accepted,
            "Could not activate winning Leiji.");
        var target = game.PendingDecision ?? throw new InvalidOperationException("Leiji target choice was lost.");
        var rebel = target.Choices.Single(item => item.Targets.SequenceEqual([rebelSeat]));
        Require(game.Submit(new AnswerPromptCommand(0, target.PromptId, rebel.Id, game.Revision)).Accepted,
            "Could not select the last Rebel for winning Leiji.");
        for (var step = 0; step < 128 && game.State.Winner == Winner.None; step++)
        {
            var prompt = game.PendingDecision;
            var choice = prompt?.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip" ||
                item.Parameters.GetValueOrDefault("response") == "let-die") ?? prompt?.Choices.LastOrDefault();
            var command = prompt is { PlayerSeat: 0 } && choice is not null
                ? (GameCommand)new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            Require(game.Submit(command).Accepted, "Winning Leiji did not complete its judgment and dying continuation.");
        }
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

    private sealed class TerminalLeijiPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("terminal-leiji-fixture", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:terminal-leiji-{index}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "雷击目标", "supporter", "standard:none", "qun", BaseHp: 1));
            builder.AddDeck(new ContentDeckRecipe("fixture:terminal-leiji-deck", "终局雷击牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).SelectMany(_ => new[]
                {
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                    new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
                }).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                TerminalLeijiMode, "终局雷击身份局", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 1
                },
                "fixture:terminal-leiji-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["classic:zhang-jiao", .. others]));
        }
    }

}
