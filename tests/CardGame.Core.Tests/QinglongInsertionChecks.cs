using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class QinglongCrescentBladeChecks
{
    public static void NestedInsertionsFinishInsideOutAndReplay()
    {
        var (game, registry) = StartInsertion();
        UseInsertionSlash(game, [1]);
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade);
        var parents = new List<long> { game.ResolutionStack.OfType<CardUseFrame>().Last().Id };
        for (var depth = 0; depth < 3; depth++)
        {
            var prompt = GiftPrompt(game)!;
            var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
            var card = choice.Cards.Single();
            GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
            var child = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == card);
            Require(child.Action!.ParentActionId == game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == parents[^1]).Action!.ActionId &&
                parents.All(id => !game.Events.Any(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == id)) &&
                parents.All(id => game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == id).Continuations.QinglongFollowup is { Active: true }),
                "Every new Slash must insert above its still-unfinished immediate parent, including repeated Qinglong triggers.");
            Require(parents.SelectMany(id => game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == id).PhysicalCardIds!)
                .All(id => game.CardMovements.Last(m => m.CardId == id).To == CardLocation.Processing),
                "Every suspended ancestor keeps all of its own physical materials in Processing until the inserted chain returns.");
            parents.Add(child.Id);
            GiftCold(game, registry);
            GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade);
        }
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var finished = game.Events.Where(e => e.Payload is CardUseFinishedEvent f && parents.Contains(f.ResolutionId))
            .Select(e => ((CardUseFinishedEvent)e.Payload).ResolutionId).ToArray();
        Require(finished.SequenceEqual(parents.AsEnumerable().Reverse()) &&
            game.Events.Count(e => e.Payload is CardUseDebitRecordedEvent f && f.Debit.Query == SkillRuleQuery.SlashLimit) == 1 &&
            !game.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.Processing),
            "Nested Slash and Zhongyong chains unwind inside out, debit ordinary Slash quota once, and release every physical cost.");
        GiftCold(game, registry);
    }

    public static void MultipleTargetsInsertBeforeAdvancingTheOriginalCursor()
    {
        var (game, registry) = StartInsertion(multi: true);
        GiftAccept(game, new UseProgramSkillCommand(0, GiftDriver, "multi", [], [], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard);
        UseInsertionSlash(game, [1, 2]);
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade && p.TargetSeat == 1);
        var original = game.ResolutionStack.OfType<CardUseFrame>().Last();
        var originalIndex = original.TargetIndex;
        var prompt = GiftPrompt(game)!;
        var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        var child = game.ResolutionStack.OfType<CardUseFrame>().Last();
        var retained = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == original.Id);
        Require(retained.TargetIndex == originalIndex && retained.CardAttack!.TargetSeat == 1 &&
            retained.Continuations.FangtianHalberd is { Active: false } && child.Id != original.Id,
            "The first dodging target can trigger Qinglong while the original multi-target cursor stays frozen.");
        GiftCold(game, registry);
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade && p.TargetSeat == 2);
        retained = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == original.Id);
        Require(retained.TargetIndex > originalIndex && retained.CardAttack!.TargetSeat == 2 &&
            game.Events.Any(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == child.Id) &&
            !game.Events.Any(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id),
            "Only after the child returns does the next original target receive its own independent Qinglong opportunity.");
        GiftCold(game, registry);
        prompt = GiftPrompt(game)!;
        choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.Events.Count(e => e.Payload is QinglongCrescentBladeResolvedEvent f && f.ResolutionId == original.Id && f.Used) == 2 &&
            game.Events.Count(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id) == 1,
            "Both dodging targets of the same original Slash can insert a paid follow-up, then the original finishes once.");
        GiftCold(game, registry);
    }

    public static void FollowupIgnoresChangedDistanceAndReturnsOnce()
    {
        var (game, registry) = StartInsertion(distant: true);
        UseInsertionSlash(game, [1]);
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade);
        Require(game.GetCombatDistance(0, 1) > game.GetAttackRange(0),
            "An actual dodged-use skill has moved the target outside the equipped Qinglong's ordinary range.");
        var prompt = GiftPrompt(game)!;
        var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        var original = game.ResolutionStack.OfType<CardUseFrame>().Last();
        GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(game.ResolutionStack.OfType<CardUseFrame>().Last().Action!.ParentActionId == original.Action!.ActionId,
            "Qinglong inserts a physical follow-up against its now-distant original target.");
        GiftCold(game, registry);
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.Events.Count(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id) == 1 &&
            !game.ResolutionStack.Any(), "The distant follow-up returns through its original Slash and completed-use skills exactly once.");
        GiftCold(game, registry);
    }

    public static void SourceDeathInInsertedSkillDrainsParentsWithoutResurrectingPrompt()
    {
        var (game, registry) = StartInsertion(dying: true);
        UseInsertionSlash(game, [1]);
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade);
        var original = game.ResolutionStack.OfType<CardUseFrame>().Last();
        var prompt = GiftPrompt(game)!;
        var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        var childId = game.ResolutionStack.OfType<CardUseFrame>().Last().Id;
        for (var step = 0; step < 150 && game.State.Status != EngineStatus.Completed; step++)
        {
            prompt = GiftPrompt(game);
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying })
            {
                GiftCold(game, registry);
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId,
                    prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "let-die").Id, game.Revision));
            }
            else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade })
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId,
                    prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip").Id, game.Revision));
            else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("gift-option") == "decline"))
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId,
                    prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("gift-option") == "decline").Id, game.Revision));
            else GiftAccept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.State.Status == EngineStatus.Completed && game.PendingDecision is null && !game.ResolutionStack.Any() &&
            !game.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.Processing) &&
            !game.CreateSnapshot(0).Players[0].IsAlive &&
            game.Events.Single(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == childId).Sequence <
                game.Events.Single(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id).Sequence,
            "Death in a real inserted completed-use skill retires child then parent without reviving a prompt or leaving physical cards.");
        GiftCold(game, registry);
    }

    public static void FactionProvidedFollowupRetainsItsOriginalUseAndPaysOnce()
    {
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger(requireFactionSlash: true, verifiedSeed: 1);
        var game = boundary.Game;
        var original = game.ResolutionStack.OfType<CardUseFrame>().Last();
        var prompt = game.PendingDecision!;
        var request = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-jijiang");
        GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, request.Id, game.Revision));
        CardUseFrame? child = null;
        for (var step = 0; step < 100 && child is null; step++)
        {
            child = game.ResolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Id != original.Id &&
                f.Action?.ParentActionId == original.Action!.ActionId);
            if (child is null) GiftAccept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(child is not null && child.SourceSeat == 0 && child.Action!.PhysicalCards.Count > 0 &&
            child.Action.PhysicalCards.All(c => c.From.OwnerSeat is > 0) &&
            !game.Events.Any(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id),
            "A genuine allied provider pays the inserted Slash while the original actor and unfinished Use remain unchanged.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)), "The in-flight provided insertion restores exactly.");
        var providerCosts = child!.Action!.PhysicalCards.Select(c => c.CardId).ToArray();
        var nestedRequest = false;
        for (var step = 0; step < 150 && game.PendingDecision is not { PlayerSeat: 0, Kind: DecisionKind.PlayCard }; step++)
        {
            prompt = game.PendingDecision;
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade })
            {
                if (!nestedRequest)
                {
                    var next = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-jijiang");
                    GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, next.Id, game.Revision));
                    nestedRequest = true;
                    replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
                    Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
                        "A repeated Jijiang request suspends its earlier supplied Slash and restores cold without replacing either request.");
                    continue;
                }
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId,
                    prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip").Id, game.Revision));
            }
            else GiftAccept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && !game.ResolutionStack.Any() &&
            nestedRequest && game.Events.Count(e => e.Payload is FactionSlashRequestedEvent) == 2 &&
            providerCosts.All(id => game.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1) &&
            game.Events.Single(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == child.Id).Sequence <
                game.Events.Single(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == original.Id).Sequence,
            "The supplied child and original request return once, inside out, without repeating any provider payment.");
        replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)), "The completed provided insertion replays exactly.");
    }

    private static (GameEngine Game, ContentRegistry Registry) StartInsertion(bool multi = false, bool distant = false, bool dying = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CompletedGiftQinglongFixture(distant, dying));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = GiftMode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        GiftAccept(game, new StartGameCommand()); GiftReach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        GiftAccept(game, new SelectGeneralCommand(0, "fixture:qinglong-gift-owner", game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        GiftAccept(game, new UseProgramSkillCommand(0, GiftDriver, "draw", [], [], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        foreach (var target in multi ? new[] { 1, 2 } : new[] { 1 })
        {
            GiftAccept(game, new UseProgramSkillCommand(0, GiftDriver, "draw-target", [], [target], game.Revision, GiftPrompt(game)!.PromptId));
            GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        }
        var weapon = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.QinglongCrescentBlade).Id;
        GiftAccept(game, new PlayCardCommand(0, weapon, [], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (game, registry);
    }

    private static void UseInsertionSlash(GameEngine game, int[] targets)
    {
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(targets));
        GiftAccept(game, new PlayCardCommand(0, slash.CardId!.Value, targets, game.Revision, GiftPrompt(game)!.PromptId));
    }
}
