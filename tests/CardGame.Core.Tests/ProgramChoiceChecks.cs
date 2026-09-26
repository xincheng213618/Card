using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramChoiceChecks
{
    private const int HumanSeat = 0;
    private const string SkillId = "fixture:conditional-benefit";

    public static void DefinitionsValidateChoicesPaymentsAndPresentationHash()
    {
        var current = Load(CurrentRules, Presentation("摸两张", "回血", "复原"));
        var effects = current.Triggers.Single().Effects;
        var payment = effects.Single(effect => effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard);
        var choice = effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption);
        Require(current.RuntimeVersion == "skill-program-v59" && current.MinimumRulesVersion == 169 &&
                payment.CardCategories.SequenceEqual([
                    SkillProgramCardCategory.Trick,
                    SkillProgramCardCategory.Equipment
                ]) && choice.Options.Select(option => option.Id).SequenceEqual(["draw", "recover", "restore"]),
            "Schema 30 must retain the generic payment categories and named conditional options.");

        Reject(BuildRules(Effects.Replace("\"sourceBind\":\"benefit\",\"optionId\":\"draw\"",
                "\"sourceBind\":\"missing\",\"optionId\":\"draw\"", StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "unknown choice result or option");
        Reject(BuildRules(Effects.Replace("\"sourceBind\":\"benefit\",\"optionId\":\"draw\"",
                "\"sourceBind\":\"benefit\",\"optionId\":\"missing\"", StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "unknown choice result or option");
        Reject(BuildRules(Effects.Replace(ChooseNode, ChooseNode + "," + ChooseNode, StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "duplicate result binding");

        var withoutFilter = Load(
            CurrentRules.Replace(",\"cardCategories\":[\"trick\",\"equipment\"]", "", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"));
        Require(withoutFilter.Triggers.Single().Effects
                    .Single(effect => effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard)
                    .CardCategories.Count == 0,
            "Omitting cardCategories must retain the unfiltered payment contract.");
        Reject(CurrentRules.Replace("[\"trick\",\"equipment\"]", "[]", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"), "must not be empty");
        Reject(CurrentRules.Replace("\"cardOwnerRef\":{\"kind\":\"owner\"}",
                "\"cardOwnerRef\":{\"kind\":\"actor\"}", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"), "same participant");

        Reject(CurrentRules, Presentation("摸两张", "回血", null), "missing label");
        var relabeled = Load(CurrentRules, Presentation("抽二", "回复一", "解除异常"));
        Require(current.GameplayHash == relabeled.GameplayHash &&
                current.Triggers.Single().Effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption)
                    .Options.Select(option => option.Label).SequenceEqual(["摸两张", "回血", "复原"]) &&
                relabeled.Triggers.Single().Effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption)
                    .Options.Select(option => option.Label).SequenceEqual(["抽二", "回复一", "解除异常"]),
            "Changing presentation-only option labels must not change the gameplay hash.");
    }

    public static void SelectedTargetChoosesConditionalEffectsAndReplays()
    {
        var draw = FindChoiceFixture(static _ => { });
        AssertPayment(draw);
        Require(OptionIds(draw.Game).SequenceEqual(["draw"]),
            "A full-health face-up and unchained responder must only receive the always-legal draw option.");
        var prompt = RequireChoicePrompt(draw.Game);
        Require(prompt.Choices.Single().Description == "摸两张" && prompt.PlayerSeat == HumanSeat &&
                draw.Game.CreateSnapshot(draw.OwnerSeat).PendingDecision is null,
            "The selected target alone must receive the private presentation-backed option prompt.");

        var checkpoint = RoundTrip(draw.Game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, draw.Registry);
        var stateBeforeForgery = State(draw.Game);
        var eventsBeforeForgery = Events(draw.Game);
        var movementsBeforeForgery = draw.Game.CardMovements.ToArray();
        var wrongResponder = draw.Game.Submit(new AnswerPromptCommand(
            draw.OwnerSeat, prompt.PromptId, prompt.Choices.Single().Id, draw.Game.Revision));
        Require(!wrongResponder.Accepted && State(draw.Game) == stateBeforeForgery &&
                Events(draw.Game).SequenceEqual(eventsBeforeForgery) &&
                draw.Game.CardMovements.SequenceEqual(movementsBeforeForgery),
            "A non-responder must not mutate a suspended generic option choice.");
        var forged = draw.Game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, new ChoiceId("program-option.forged"), draw.Game.Revision));
        Require(!forged.Accepted && State(draw.Game) == stateBeforeForgery &&
                Events(draw.Game).SequenceEqual(eventsBeforeForgery) &&
                draw.Game.CardMovements.SequenceEqual(movementsBeforeForgery),
            "A forged option id must be rejected atomically.");

        var handBeforeDraw = Player(draw.Game, HumanSeat).HandCount;
        var turnEndsBeforeDraw = draw.Game.Events.Count(item => item.Payload is TurnEndedEvent);
        AnswerOption(draw.Game, "draw");
        AnswerOption(replay, "draw");
        Require(Player(draw.Game, HumanSeat).HandCount == handBeforeDraw + 2 &&
                draw.Game.Events.Count(item => item.Payload is TurnEndedEvent) == turnEndsBeforeDraw + 1 &&
                Chosen(draw.Game) is { ResultBind: "benefit", OptionId: "draw", ChooserSeat: HumanSeat } &&
                State(draw.Game) == State(replay) && Events(draw.Game).SequenceEqual(Events(replay)) &&
                draw.Game.CardMovements.SequenceEqual(replay.CardMovements),
            "The draw option must execute exactly once, resume the turn once, and replay identically from its prompt.");

        var recover = FindChoiceFixture(target => target.Hp = target.MaxHp - 1);
        AssertPayment(recover);
        Require(OptionIds(recover.Game).SequenceEqual(["draw", "recover"]),
            "A wounded responder must receive draw and recover, but not restore.");
        var hpBefore = Player(recover.Game, HumanSeat).Hp;
        var handBeforeRecover = Player(recover.Game, HumanSeat).HandCount;
        AnswerOption(recover.Game, "recover");
        Require(Player(recover.Game, HumanSeat).Hp == hpBefore + 1 &&
                Player(recover.Game, HumanSeat).HandCount == handBeforeRecover &&
                recover.Game.Events.Count(item => item.Payload is RecoveryAppliedEvent
                {
                    SourceSeat: var source,
                    TargetSeat: HumanSeat,
                    Amount: 1
                } && source == recover.OwnerSeat) == 1 &&
                Chosen(recover.Game) is { OptionId: "recover", ChooserSeat: HumanSeat },
            "The recover option must invoke the ordinary one-HP recovery path exactly once.");

        var restore = FindChoiceFixture(target =>
        {
            target.IsFaceDown = true;
            target.IsChained = true;
        });
        AssertPayment(restore);
        Require(OptionIds(restore.Game).SequenceEqual(["draw", "restore"]),
            "A full-health face-down and chained responder must receive draw and restore, but not recover.");
        var handBeforeRestore = Player(restore.Game, HumanSeat).HandCount;
        AnswerOption(restore.Game, "restore");
        Require(!Player(restore.Game, HumanSeat).IsFaceDown && !Player(restore.Game, HumanSeat).IsChained &&
                Player(restore.Game, HumanSeat).HandCount == handBeforeRestore &&
                restore.Game.Events.Count(item => item.Payload is ProgramChainedStateSetEvent
                {
                    TargetSeat: HumanSeat,
                    IsChained: false
                }) == 1 && Chosen(restore.Game) is { OptionId: "restore", ChooserSeat: HumanSeat },
            "The restore option must set both public states false exactly once without drawing or recovering.");
    }

    public static void RevalidatesChoiceAndExactInstanceBeforeResolving()
    {
        var lostInstance = FindChoiceFixture(static _ => { });
        var lostFrame = lostInstance.Game.ResolutionStack.OfType<ProgramSkillFrame>()
            .Single(frame => frame.SkillId == SkillId);
        var owner = Players(lostInstance.Game)[lostInstance.OwnerSeat];
        foreach (var grant in owner.SkillGrants.Grants.Where(grant =>
                     grant.SkillId == SkillId && grant.SkillInstanceId == lostFrame.SkillInstanceId).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        AssertChoiceCancellation(lostInstance, "draw", target => target.HandCount,
            "A named option must not resolve after its exact skill instance is disabled.");

        var staleRecover = FindChoiceFixture(target => target.Hp = target.MaxHp - 1);
        Require(OptionIds(staleRecover.Game).Contains("recover"),
            "The stale-option fixture must initially publish recovery.");
        Players(staleRecover.Game)[HumanSeat].Hp = Players(staleRecover.Game)[HumanSeat].MaxHp;
        AssertChoiceCancellation(staleRecover, "recover", target => target.Hp,
            "A recovery option that becomes false at answer time must cancel without applying its effect.");
    }

    private static ChoiceFixture FindChoiceFixture(Action<CharacterState> prepareTarget)
    {
        var registry = Registry();
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                ModeId = ScenarioPackage.ModeId,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 3,
                MaxTurns = 12
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: HumanSeat } setup ||
                !setup.ValidContentIds.Contains(ScenarioPackage.TargetGeneralId) ||
                !game.Submit(new SelectGeneralCommand(HumanSeat, ScenarioPackage.TargetGeneralId,
                    game.Revision, setup.PromptId)).Accepted)
                continue;

            if (!ReachHumanPlay(game)) continue;
            var players = Players(game);
            var owner = players.SingleOrDefault(player =>
                player.General.Id == ScenarioPackage.OwnerGeneralId);
            if (owner is null || owner.Seat != 1 || owner.Role != Role.Loyalist) continue;
            var ownerHand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[owner.Seat].Hand;
            if (ownerHand.Count(card => CardCatalog.Get(card.Kind).CategoryName == "装备牌") < 2 ||
                ownerHand.All(card => CardCatalog.Get(card.Kind).CategoryName != "基本牌"))
                continue;

            prepareTarget(players[HumanSeat]);
            var play = game.PendingDecision!;
            if (!game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId)).Accepted)
                continue;
            if (!ReachHumanOptionChoice(game)) continue;
            return new ChoiceFixture(game, registry, owner.Seat);
        }
        throw new InvalidOperationException("No bounded generic-choice fixture reached a human selected-target option prompt.");
    }

    private static bool ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return true;
            if (game.PendingDecision?.PlayerSeat == HumanSeat) return false;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!result.Accepted) return false;
        }
        return false;
    }

    private static bool ReachHumanOptionChoice(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat } prompt &&
                prompt.Choices.Count > 0 && prompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "choose-option"))
                return true;
            if (game.PendingDecision?.PlayerSeat == HumanSeat) return false;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!result.Accepted) return false;
        }
        return false;
    }

    private static void AssertPayment(ChoiceFixture fixture)
    {
        var payments = fixture.Game.CardMovements.Where(move =>
            move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard" &&
            move.From is { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } &&
            seat == fixture.OwnerSeat && move.To == CardLocation.DiscardPile).ToArray();
        Require(payments is [{ CardKind: var kind }] && CardCatalog.Get(kind).CategoryName != "基本牌",
            "The generic payment must move exactly one Trick or Equipment card and never a Basic card.");
    }

    private static IReadOnlyList<string> OptionIds(GameEngine game) => RequireChoicePrompt(game).Choices
        .Select(choice => choice.Parameters.GetValueOrDefault("option-id") ?? "").ToArray();

    private static PendingDecision RequireChoicePrompt(GameEngine game) => game.PendingDecision is
        { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: SkillId } prompt &&
        prompt.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option")
            ? prompt
            : throw new InvalidOperationException("Expected the generic selected-target option prompt.");

    private static void AnswerOption(GameEngine game, string optionId)
    {
        var prompt = RequireChoicePrompt(game);
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("option-id") == optionId);
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"The generic option '{optionId}' was rejected.");
    }

    private static void AssertChoiceCancellation(
        ChoiceFixture fixture,
        string optionId,
        Func<PlayerSnapshot, int> benefitValue,
        string message)
    {
        var beforeTarget = Player(fixture.Game, HumanSeat);
        var valueBefore = benefitValue(beforeTarget);
        var paymentsBefore = fixture.Game.CardMovements.Count(move =>
            move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard");
        var turnEndsBefore = fixture.Game.Events.Count(item => item.Payload is TurnEndedEvent);
        var chosenBefore = fixture.Game.Events.Count(item => item.Payload is ProgramOptionChosenEvent
        {
            SkillId: SkillId
        });

        AnswerOption(fixture.Game, optionId);

        Require(benefitValue(Player(fixture.Game, HumanSeat)) == valueBefore &&
                fixture.Game.CardMovements.Count(move =>
                    move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard") == paymentsBefore &&
                paymentsBefore == 1 &&
                fixture.Game.Events.Count(item => item.Payload is ProgramOptionChosenEvent
                {
                    SkillId: SkillId
                }) == chosenBefore &&
                fixture.Game.Events.Count(item => item.Payload is TurnEndedEvent) == turnEndsBefore + 1 &&
                fixture.Game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Last(item => item.SkillId == SkillId) is { Activated: true, Completed: false },
            message);
    }

    private static ProgramOptionChosenEvent Chosen(GameEngine game) => game.Events.Select(item => item.Payload)
        .OfType<ProgramOptionChosenEvent>().Single(item => item.SkillId == SkillId);

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The generic-choice players are unavailable."));

    private static ContentRegistry Registry()
    {
        var catalog = SkillProgramCatalog.Load(CurrentRules, Presentation("摸两张", "回血", "复原"));
        return ContentRegistry.Build(new StandardContentPackage(), new ScenarioPackage(
            catalog.Programs[SkillId], catalog.Presentations[SkillId]));
    }

    private static SkillProgram Load(string rules, string presentation) =>
        SkillProgramCatalog.Load(rules, presentation).Programs[SkillId];

    private static void Reject(string rules, string presentation, string expected)
    {
        try { _ = Load(rules, presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected invalid generic-choice content to mention '{expected}'.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
                        JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string BuildRules(string effects) => $$"""
        {"schemaVersion":59,"skills":[{"id":"{{SkillId}}","revision":1,
        "minimumRulesVersion":169,"modifiers":[],"viewAs":[],"activations":[],
        "triggers":[{"id":"support","window":"turnEnding","subject":"owner","optional":false,
        "priority":0,"usageScope":"turn","usageLimit":1,"effects":{{effects}}}],
        "contributions":[],"cardIdentities":[]}]}
        """;

    private static string Presentation(string draw, string recover, string? restore) =>
        "{\"schemaVersion\":3,\"skills\":{\"" + SkillId +
        "\":{\"name\":\"条件收益\",\"description\":\"公共选择测试\",\"optionLabels\":{" +
        "\"draw\":\"" + draw + "\",\"recover\":\"" + recover + "\"" +
        (restore is null ? "" : ",\"restore\":\"" + restore + "\"") + "}}}}";

    private const string ChooseNode =
        "{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"benefit\",\"options\":[" +
        "{\"id\":\"draw\",\"condition\":{\"kind\":\"always\"}}," +
        "{\"id\":\"recover\",\"condition\":{\"kind\":\"wounded\"}}," +
        "{\"id\":\"restore\",\"condition\":{\"kind\":\"any\",\"children\":[" +
        "{\"kind\":\"faceDown\"},{\"kind\":\"chained\"}]}}]}";

    private const string Effects =
        "[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"}," +
        "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"}," +
        "\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\",\"equipment\"],\"count\":1," +
        "\"destination\":\"discardPile\",\"cardCategories\":[\"trick\",\"equipment\"]}," + ChooseNode + "," +
        "{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":2," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"draw\"}}," +
        "{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"recover\"}}," +
        "{\"op\":\"setFaceState\",\"target\":\"selectedTarget\",\"faceDown\":false," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"restore\"}}," +
        "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":false," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"restore\"}}]";

    private static readonly string CurrentRules = BuildRules(Effects);

    private sealed record ChoiceFixture(GameEngine Game, ContentRegistry Registry, int OwnerSeat);

    private sealed class ScenarioPackage(SkillProgram program, SkillPresentation presentation) : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:conditional-benefit-owner";
        public const string TargetGeneralId = "fixture:conditional-benefit-target";
        public const string ModeId = "identity:conditional-benefit-test-4";
        private const string DeckId = "fixture:conditional-benefit-deck";

        public PackageManifest Manifest { get; } = new("conditional-benefit-scenario", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "条件收益", "公共选择测试")
            {
                Program = program,
                ProgramPresentation = presentation,
                ExecutionForms = SkillExecutionForm.Trigger,
                ActionForms = SkillActionForm.None
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "条件收益拥有者", "supporter", SkillId, "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                TargetGeneralId, "条件收益响应者", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:conditional-benefit-filler-1", "条件收益目标甲", "supporter", "standard:none", "shu", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:conditional-benefit-filler-2", "条件收益目标乙", "supporter", "standard:none", "wu", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "条件收益牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("standard:crossbow", 72),
                    new ContentDeckCardCount("standard:slash", 24)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "四人身份（公共条件选择测试）",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds:
                [
                    OwnerGeneralId,
                    TargetGeneralId,
                    "fixture:conditional-benefit-filler-1",
                    "fixture:conditional-benefit-filler-2"
                ]));
        }
    }
}
