using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenZhouYuChecks
{
    private const string General = "classic:shen-zhou-yu";
    private const string Qinyin = "classic:qinyin";
    private const string Yeyan = "classic:yeyan";
    private const string QinyinMode = "identity:classic-shen-zhou-yu-qinyin-check";
    private const string SmallBlazeMode = "identity:classic-shen-zhou-yu-small-blaze-check";
    private const string BigBlazeMode = "identity:classic-shen-zhou-yu-big-blaze-check";

    public static void DefinitionAndRosterSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "god" } general &&
                general.SkillIds.SequenceEqual([Qinyin, Yeyan]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "God Zhou Yu must be in the current god roster with four HP and both skills.");

        var qinyin = current.Skills[Qinyin].Program!;
        var melody = qinyin.Triggers.Single();
        Require(melody is
        {
            Window: SkillProgramTriggerWindow.DiscardPhaseEnded,
            Subject: SkillProgramTriggerSubject.Owner,
            Optional: true,
            TurnOwnerScope: SkillProgramTurnOwnerScope.Own
        } && melody.AllowOwnDiscardPhaseEnded &&
            melody.Condition.Kind == SkillProgramTriggerConditionKind.Compare &&
            melody.Condition.Comparison == SkillProgramComparisonOperator.GreaterThanOrEqual,
            "Qinyin must observe only its own discard phase ending with an optional comparison condition.");
        Require(melody.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.RecoverAllLiving,
            SkillProgramEffectOp.LoseHpParticipants]) &&
            melody.Effects[0].ResultBind == "qinyin-melody" &&
            melody.Effects[0].Options.Select(item => item.Id).SequenceEqual(["heal", "lose"]) &&
            melody.Effects[1] is { Amount: 1, TargetKind: null } &&
            melody.Effects[2] is { Amount: 1, TargetKind: SkillProgramTargetKind.AnyLiving } &&
            melody.Effects.Skip(1).All(item => item.Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
                item.Condition.SourceBind == "qinyin-melody"),
            "Qinyin must ask once, then gate recovery of all living characters or an HP loss for every " +
            "living character on the recorded option.");

        var yeyan = current.Skills[Yeyan].Program!;
        Require(yeyan.Activations.Count == 4 &&
                yeyan.Activations.All(item => item is { UsesPerGame: 1, UsageGroup: "limited" }) &&
                yeyan.Activations.Single(item => item.Id == "small") is
                { MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 3 } small &&
                small.Effects.Single() is
                {
                    Op: SkillProgramEffectOp.DamageParticipants,
                    Amount: 1,
                    DamageNature: DamageNature.Fire
                } && small.SelectedCardsDistinctSuits == false,
            "The small Blaze must burn one point through up to three targets with no card cost.");
        foreach (var (id, minimum, amount, secondary) in new (string, int, int, int)[]
                 { ("two", 1, 2, 2), ("three", 1, 3, 3), ("two-and-one", 2, 2, 1) })
        {
            var activation = yeyan.Activations.Single(item => item.Id == id);
            Require(activation is
            {
                MinCards: 4,
                MaxCards: 4,
                SelectedCardsDistinctSuits: true,
                ContinueAfterOwnerDeath: true
            } && activation.MinTargets == minimum && activation.MaxTargets == minimum &&
                activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                activation.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.DiscardSelected,
                    SkillProgramEffectOp.LoseHpUnclamped,
                    SkillProgramEffectOp.DamageParticipants]) &&
                activation.Effects[0].Amount == 4 &&
                activation.Effects[1] is { Amount: 3, Target: SkillProgramEffectTarget.Owner } &&
                activation.Effects[2] is { DamageNature: DamageNature.Fire } &&
                activation.Effects[2].Amount == amount && activation.Effects[2].MinimumValue == secondary,
            $"The big Blaze variant {id} must pay four distinct-suit hand cards and three own HP " +
            $"before dealing {amount}/{secondary} fire damage.");
        }
        Require(current.Skills[Yeyan].ProgramPresentation!.ActivationLabels.Count == 4 &&
                melody.Effects[0].Options.All(option => option.Label.Length > 0),
            "Blaze activations and both Qinyin options must carry their presentation labels.");
    }

    public static void QinyinHealsOrDrainsEveryLivingCharacterAndReplays()
    {
        var healed = false;
        var drained = false;
        for (var seed = 1; seed <= 60 && (!healed || !drained); seed++)
        {
            foreach (var option in new[] { "heal", "lose" })
            {
                var registry = Registry(QinyinMode, MixedDeck(), bankHp: 6);
                var game = Start(registry, seed, QinyinMode);
                if (!DriveToQinyinPrompt(game)) continue;

                var beforePlayers = game.CreateSnapshot(0, true).Players.ToArray();
                var eventCountBefore = game.Events.Count;
                var paused = RoundTrip(game.CreateCheckpoint());
                var replay = GameReplay.Restore(paused, registry);
                AnswerOption(game, option);
                AnswerOption(replay, option);
                DriveUntilSettled(game);
                DriveUntilSettled(replay);
                Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                    $"The Qinyin {option} branch must replay identically from its option prompt.");
                var after = game.CreateSnapshot(0, true).Players.Select(player => player.Hp).ToArray();
                if (option == "heal")
                {
                    var recoveries = game.Events.Skip(eventCountBefore).Select(item => item.Payload)
                        .OfType<RecoveryAppliedEvent>().ToArray();
                    var expectedRecovery = beforePlayers.Sum(player => Math.Min(1, player.MaxHp - player.Hp));
                    Require(expectedRecovery > 0 && recoveries.Length == expectedRecovery && recoveries.All(item => item.Amount == 1) &&
                        after.Select((hp, seat) => hp == Math.Min(beforePlayers[seat].MaxHp, beforePlayers[seat].Hp + 1)).All(matches => matches),
                        "The heal branch must recover one HP for each wounded living character and respect every character's maximum HP.");
                    healed = true;
                }
                else
                {
                    var losses = game.Events.Select(item => item.Payload)
                        .OfType<ProgramSkillHpLostEvent>().Where(item => item.SkillId == Qinyin).ToArray();
                    Require(losses.Length == 5 && losses.All(item => item.Amount == 1) &&
                            losses.Select(item => item.TargetSeat).SequenceEqual([0, 1, 2, 3, 4]),
                        "The drain branch must take one HP from every living character in seat order.");
                    drained = true;
                }
            }
        }
        Require(healed && drained, "No seeded run reached the Qinyin option prompt for both branches.");
    }

    public static void SmallBlazeBurnsUpToThreeTargetsOncePerGameAndReplays()
    {
        var registry = Registry(SmallBlazeMode, MixedDeck(), bankHp: 6);
        for (var seed = 1; seed <= 60; seed++)
        {
            var game = Start(registry, seed, SmallBlazeMode);
            DriveToHumanPlayPhase(game);
            if (!TargetsFor(FindActivation(game, Yeyan, "small"), out var targets)) continue;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            Activate(game, Yeyan, "small", [], targets);
            Activate(replay, Yeyan, "small", [], targets);
            SettleThrough(game, replay);

            var burns = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.Nature == DamageNature.Fire).ToArray();
            Require(burns.Length == targets.Count &&
                    burns.All(item => item.Amount == 1) &&
                    burns.Select(item => item.TargetSeat).Order().SequenceEqual(targets.Order()),
                    "The small Blaze must deal exactly one fire damage to each chosen target.");
            Require(!game.GetHumanLegalActions().Any(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Yeyan),
                "Blaze is a once-per-game limited skill.");
            return;
        }
        throw new InvalidOperationException("No seeded setup offered the small Blaze activation.");
    }

    public static void BigBlazePaysFourSuitsAndOwnHpAndBurnsTwoPoints()
    {
        var registry = Registry(BigBlazeMode, MixedDeck(), bankHp: 6);
        for (var seed = 1; seed <= 60; seed++)
        {
            var game = Start(registry, seed, BigBlazeMode);
            DriveToHumanPlayPhase(game);
            if (FindActivation(game, Yeyan, "two") is not { } action ||
                action.SelectableCardIds.Count < 4) continue;
            var suits = game.CreateSnapshot(0, true).Players[0].Hand;
            var paid = FourDistinctSuits(action.SelectableCardIds, suits);
            if (paid is null) continue;
            if (!TargetsFor(action, out var candidates) || action.MaxTargetCount != 1) continue;
            var target = new[] { candidates[0] };

            var before = game.CreateSnapshot(0, true).Players;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            Activate(game, Yeyan, "two", paid, target);
            Activate(replay, Yeyan, "two", paid, target);
            SettleThrough(game, replay);

            var after = game.CreateSnapshot(0, true).Players;
            Require(before[0].Hp - after[0].Hp >= 3 &&
                    game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>()
                        .Any(item => item.SkillId == Yeyan && item.TargetSeat == 0 && item.Amount == 3),
                "The big Blaze must take three unclamped HP from God Zhou Yu.");
            var burns = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.Nature == DamageNature.Fire).ToArray();
            Require(burns.Length == 1 && burns[0].Amount == 2 && burns[0].TargetSeat == target[0],
                "The big Blaze must deal two fire damage to the single chosen target.");
            Require(game.CardMovements.Count(item =>
                    item.From == CardLocation.Hand(0) && item.To == CardLocation.DiscardPile &&
                    paid.Contains(item.CardId)) == 4,
                "The big Blaze must move its four distinct-suit payment cards into the discard pile.");
            return;
        }
        throw new InvalidOperationException("No seeded setup afforded the big Blaze payment.");
    }

    private static int[]? FourDistinctSuits(IReadOnlyList<int> selectable,
        IReadOnlyList<CardSnapshot> hand)
    {
        var picked = new List<int>();
        var suits = new HashSet<Suit>();
        foreach (var card in hand.Where(card => selectable.Contains(card.Id)))
        {
            if (!suits.Add(card.Suit)) continue;
            picked.Add(card.Id);
            if (picked.Count == 4) return picked.ToArray();
        }
        return null;
    }

    private static LegalAction? FindActivation(GameEngine game, string skillId, string activationId) =>
        game.GetHumanLegalActions().SingleOrDefault(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == skillId &&
            item.ProgramActivationId == activationId);

    private static bool TargetsFor(LegalAction? action, out IReadOnlyList<int> targets)
    {
        targets = action is null ? Array.Empty<int>()
            : action.SelectableTargetSeats.Take(action.MaxTargetCount).ToArray();
        return action is not null && targets.Count == action.MaxTargetCount && action.MaxTargetCount > 0;
    }

    private static void Activate(GameEngine game, string skillId, string activationId,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Blaze play-phase prompt vanished.");
        Accept(game.Submit(new UseProgramSkillCommand(0, skillId, activationId, cards, targets,
            game.Revision, prompt.PromptId)));
    }

    private static void SettleThrough(GameEngine game, GameEngine replay)
    {
        DriveUntilSettled(game);
        DriveUntilSettled(replay);
    }

    private static bool DriveToQinyinPrompt(GameEngine game)
    {
        for (var step = 0; step < 1200 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null || prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.DiscardCards)
            {
                // Discard two or more own hand cards so Qinyin's own-phase count is met.
                var count = Math.Max(2, prompt.RequiredCardCount);
                var cards = prompt.ValidCardIds.Order().Take(count).ToArray();
                Accept(game.Submit(new DiscardCardsCommand(0, cards, prompt.PromptId, game.Revision)));
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                continue;
            }
            // Qinyin is an optional trigger: the engine first offers its activation and only
            // then publishes the two melody options, so the offer has to be accepted here.
            var activate = prompt.Choices.SingleOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("skill-id") == Qinyin &&
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            if (activate is not null)
            {
                Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, activate.Id, game.Revision)));
                return game.PendingDecision?.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("option-id") is "heal" or "lose") ?? false;
            }
            var skip = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First();
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                skip.Id, game.Revision)));
        }
        return false;
    }

    private static void AnswerOption(GameEngine game, string optionId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Qinyin option prompt vanished.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("option-id") == optionId);
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void DriveToHumanPlayPhase(GameEngine game)
    {
        for (var step = 0; step < 600; step++)
        {
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("The God Zhou Yu fixture ended before its play phase.");
            var prompt = game.PendingDecision;
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.State.CurrentSeat == 0) return;
            AnswerOrAdvance(game, prompt);
        }
        throw new InvalidOperationException("The God Zhou Yu fixture never reached the lord's play phase.");
    }

    private static void DriveUntil(GameEngine game, Func<bool> done, int budget)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
            AnswerOrAdvance(game, game.PendingDecision);
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0, 900);

    private static void AnswerOrAdvance(GameEngine game, PendingDecision? prompt)
    {
        if (prompt is null || prompt.PlayerSeat != 0)
        {
            Advance(game);
            return;
        }
        switch (prompt.Kind)
        {
            case DecisionKind.PlayCard:
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                break;
            case DecisionKind.DiscardCards:
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                break;
            default:
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First();
                Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision)));
                break;
        }
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankHp) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new ShenZhouYuScenario(mode, deck, bankHp));

    private static ContentDeckRecipe MixedDeck() =>
        new("fixture:shen-zhou-yu-deck", "神周瑜测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                0 => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, index % 13 + 1),
                1 => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, index % 13 + 1),
                2 => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1),
                _ => new ContentDeckPhysicalCard("standard:dodge", Suit.Diamond, index % 13 + 1)
            }).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "God Zhou Yu fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "God Zhou Yu selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "God Zhou Yu fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "God Zhou Yu command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ShenZhouYuScenario(string modeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shen-zhou-yu-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            // Seats 1-4 are skill-less AI banks, so the only live skills are Qinyin and
            // Yeyan on the human lord seat and the Blaze walk cannot hit a responder.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhou-yu-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp)
            { InitialHp = modeId == QinyinMode ? bankHp - 1 : null });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhou-yu-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp)
            { InitialHp = modeId == QinyinMode ? bankHp - 1 : null });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhou-yu-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp)
            { InitialHp = modeId == QinyinMode ? bankHp - 1 : null });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhou-yu-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp)
            { InitialHp = modeId == QinyinMode ? bankHp - 1 : null });
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "神周瑜测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:zhou-yu-bank-a",
                    "fixture:zhou-yu-bank-b",
                    "fixture:zhou-yu-bank-c",
                    "fixture:zhou-yu-bank-d"]));
        }
    }
}
