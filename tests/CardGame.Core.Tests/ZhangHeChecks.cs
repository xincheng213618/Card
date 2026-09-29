using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangHeChecks
{
    private const string General = "classic:zhang-he";
    private const string Qiaobian = "classic:qiaobian";
    private const string Mode = "identity:classic-zhang-he-check-5";
    private const string SkipJudgmentTrigger = "skip-judgment-phase";
    private const string SkipDrawTrigger = "skip-draw-phase-and-take-hands";
    private const string SkipPlayTrigger = "skip-play-phase-and-move-field-card";
    private const string SkipDiscardTrigger = "skip-discard-phase";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Qiaobian]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Zhang He must be in the current Wei roster with four HP and Qiaobian.");

        var qiaobian = current.Skills[Qiaobian].Program!;
        var windows = qiaobian.Triggers.Select(item => item.Window).ToArray();
        Require(windows.Count(item => item == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) == 2 &&
                windows.Contains(SkillProgramTriggerWindow.AfterNormalDraw) &&
                windows.Contains(SkillProgramTriggerWindow.DiscardPhaseStarting),
            "Qiaobian must expose one branch per skippable phase boundary.");

        foreach (var trigger in qiaobian.Triggers)
        {
            Require(trigger.Optional &&
                    trigger.Condition.Kind == SkillProgramTriggerConditionKind.Compare &&
                    trigger.UsageScope == SkillUsageScope.Turn &&
                    trigger.UsageLimit == 1 &&
                    trigger.Effects[0].Op == SkillProgramEffectOp.SelectOwnedCards &&
                    trigger.Effects[1] is { Op: SkillProgramEffectOp.MoveBoundCards, Destination: SkillProgramCardDestination.DiscardPile } &&
                    trigger.Effects.Any(item => item.Op == SkillProgramEffectOp.SkipTurnPhases),
                "Every Qiaobian branch must discard one hand card before declaring the phase substitution.");
        }

        var judgmentBranch = qiaobian.Triggers.Single(item =>
            item.Effects.Any(effect => effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Judgment])));
        var drawBranch = qiaobian.Triggers.Single(item =>
            item.Effects.Any(effect => effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Draw])));
        var playBranch = qiaobian.Triggers.Single(item =>
            item.Effects.Any(effect => effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Play])));
        var discardBranch = qiaobian.Triggers.Single(item =>
            item.Effects.Any(effect => effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Discard])));
        Require(drawBranch.Effects.Any(effect =>
                effect.Op == SkillProgramEffectOp.SelectTargets &&
                effect.TargetKind == SkillProgramTargetKind.OtherLivingWithHand &&
                effect.MinimumTargets == 1 && effect.MaximumTargets == 2) &&
                drawBranch.Effects.Any(effect => effect.Op == SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets),
            "The draw branch must offer one or two opponents with hand cards and take one card from each.");
        Require(playBranch.Effects.Any(effect =>
                effect.Op == SkillProgramEffectOp.SelectTargets &&
                effect.TargetKind == SkillProgramTargetKind.LivingPairDistinct) &&
                playBranch.Effects.Any(effect =>
                    effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                    effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone &&
                    effect.Zones.SequenceEqual([CardZoneKind.Judgment, CardZoneKind.Equipment])),
            "The play branch must pick an ordered pair and move a judgment or equipment card to the paired corresponding zone.");
        Require(judgmentBranch.Id == SkipJudgmentTrigger && drawBranch.Id == SkipDrawTrigger &&
                playBranch.Id == SkipPlayTrigger && discardBranch.Id == SkipDiscardTrigger,
            "The four branches must keep their stable trigger ids.");

        const string windowTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:qiaobian-window","revision":1,
            "minimumRulesVersion": 186,
            "triggers":[{"id":"t","window":"WINDOW","subject":"owner","optional":true,
            "condition":{"kind":"compare","left":{"kind":"currentHandCount"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":1}},
            "effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"cost"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile"},
            {"op":"skipTurnPhases","target":"owner","phases":["PHASE"]}]}]}]}
            """;
        const string windowPresentation = """
            {"schemaVersion":3,"skills":{"fixture:qiaobian-window":{"name":"测试","description":"测试"}}}
            """;
        Load(windowTemplate.Replace("WINDOW", "turnStartBeforeNormalFlow").Replace("PHASE", "judgment"),
            windowPresentation);
        Load(windowTemplate.Replace("WINDOW", "turnStartBeforeNormalFlow").Replace("PHASE", "draw"),
            windowPresentation);
        Load(windowTemplate.Replace("WINDOW", "discardPhaseStarting").Replace("PHASE", "discard"),
            windowPresentation);
        Reject(windowTemplate.Replace("WINDOW", "discardPhaseStarting").Replace("PHASE", "draw"),
            windowPresentation, "each lifecycle boundary admits only its own phase substitution");
        Reject(windowTemplate.Replace("WINDOW", "turnEnding").Replace("PHASE", "discard"),
            windowPresentation, "the discard substitution belongs to the discard-phase boundary");
    }

    public static void SkipDiscardBranchKeepsHandCardsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 300 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            var play = DriveToFirstPlaySkippingQiaobian(game);
            if (play is null) continue;
            var endResult = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            Accept(endResult);
            if (DriveToQiaobianBranch(game, SkipDiscardTrigger) is null) continue;
            var before = game.CreateSnapshot(0, true);
            Require(before.Players[0].Hand.Count > before.Players[0].MaxHp,
                "The fixture must reach the discard phase above the hand limit.");
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (!UseBranch(game, SkipDiscardTrigger) || !UseBranch(replay, SkipDiscardTrigger)) continue;
            DriveUntil(game, () => game.ResolutionStack.Count == 0 && game.PendingDecision is null);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0 && replay.PendingDecision is null);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The skip-discard branch must replay identically from the paused checkpoint.");

            var limitDiscards = game.CardMovements.Where(item =>
                item.Reason.Value.Contains("rule.hand-limit-discard", StringComparison.Ordinal) &&
                item.From.OwnerSeat == 0).ToArray();
            Require(limitDiscards.Length == 0,
                "Skipping the discard phase must suppress every hand-limit discard.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised the skip-discard branch.");
    }

    private static PendingDecision? DriveToQiaobianBranch(GameEngine game, string triggerId)
    {
        for (var step = 0; step < 800 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger && prompt.PlayerSeat == 0 &&
                prompt.SkillPrompt?.SkillId == Qiaobian)
            {
                var activate = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate");
                if (activate is not null &&
                    activate.Parameters.GetValueOrDefault("binding-id") == triggerId)
                    return prompt;
                var skip = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (skip is null) return null;
                Answer(game, skip);
                continue;
            }
            if (!AnswerForeignOrRoutine(game, prompt)) return null;
        }
        return null;
    }

    private static PendingDecision? DriveToFirstPlaySkippingQiaobian(GameEngine game)
    {
        for (var step = 0; step < 800 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger && prompt.PlayerSeat == 0 &&
                prompt.SkillPrompt?.SkillId == Qiaobian)
            {
                var skip = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (skip is null) return null;
                Answer(game, skip);
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0) return prompt;
            if (!AnswerForeignOrRoutine(game, prompt)) return null;
        }
        return null;
    }

    private static bool AnswerForeignOrRoutine(GameEngine game, PendingDecision prompt)
    {
        if (prompt.PlayerSeat != 0)
        {
            Advance(game);
            return true;
        }
        switch (prompt.Kind)
        {
            case DecisionKind.PlayCard:
                return true;
            case DecisionKind.RespondDodge or DecisionKind.RespondSlash:
                var answer = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "dodge" or "slash") ??
                    prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage");
                if (answer is null) return false;
                Answer(game, answer);
                return true;
            case DecisionKind.DiscardCards:
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                return true;
            default:
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("response") == "pass") ??
                    prompt.Choices.First();
                Answer(game, choice);
                return true;
        }
    }

    private static bool UseBranch(GameEngine game, string triggerId)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } ||
            prompt.SkillPrompt?.SkillId != Qiaobian)
            return false;
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
            choice.Parameters.GetValueOrDefault("binding-id") == triggerId);
        if (activate is null) return false;
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, activate.Id, game.Revision)));
        return PaySkillCost(game);
    }

    private static bool UseSkipDrawBranch(GameEngine game) =>
        UseBranch(game, SkipDrawTrigger) && AnswerPairTargets(game, twoTargets: true);

    private static bool PaySkillCost(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } ||
            !prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
            return false;
        var pick = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, pick.Id, game.Revision)));
        return true;
    }

    private static bool AnswerPairTargets(GameEngine game, bool twoTargets)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } ||
            !prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-targets"))
            return false;
        var choice = prompt.Choices
            .Where(item => item.Parameters.GetValueOrDefault("program-action") == "select-targets")
            .FirstOrDefault(item => item.Targets.Count == (twoTargets ? 2 : 1)) ??
            prompt.Choices.First(item =>
                item.Parameters.GetValueOrDefault("program-action") == "select-targets");
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
        return true;
    }

    private static void DriveUntil(GameEngine game, Func<bool> done, int budget = 800)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat == 0)
            {
                switch (prompt.Kind)
                {
                    case DecisionKind.PlayCard:
                        return;
                    case DecisionKind.RespondDodge or DecisionKind.RespondSlash:
                        var answer = prompt.Choices.FirstOrDefault(choice =>
                            choice.Parameters.GetValueOrDefault("response") is "dodge" or "slash") ??
                            prompt.Choices.FirstOrDefault(choice =>
                                choice.Parameters.GetValueOrDefault("response") == "take-damage");
                        if (answer is null) return;
                        Answer(game, answer);
                        continue;
                    case DecisionKind.DiscardCards:
                        Accept(game.Submit(new DiscardCardsCommand(0,
                            prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                            prompt.PromptId, game.Revision)));
                        continue;
                    default:
                        var choice = prompt.Choices.FirstOrDefault(item =>
                            item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                            prompt.Choices.First();
                        Answer(game, choice);
                        continue;
                }
            }
            Advance(game);
        }
    }

    private static void Load(string rules, string presentation) =>
        _ = SkillProgramCatalog.Load(rules, presentation);

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Qiaobian composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Zhang He command failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang He skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed, bool crossbowDeck = false)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhang He fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Zhang He selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang He fixture did not advance.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zhang-he-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 156, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-he-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-he-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-he-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-he-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 240).Select(index => (index % 6) switch
            {
                0 => "standard:slash",
                1 => "standard:dodge",
                2 => "standard:crossbow",
                3 => "standard:dodge",
                4 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:zhang-he-deck", "张郃测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "张郃测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:zhang-he-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:zhang-he-bank-a",
                    "fixture:zhang-he-bank-b",
                    "fixture:zhang-he-bank-c",
                    "fixture:zhang-he-bank-d"]));
        }
    }
}
