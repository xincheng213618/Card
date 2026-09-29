using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenLuMengChecks
{
    private const string General = "classic:shen-lu-meng";
    private const string Shelie = "classic:shelie";
    private const string Gongxin = "classic:gongxin";
    private const string Mode = "identity:classic-shen-lu-meng-check-5";

    public static void DefinitionAndSkillSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "god" } general &&
                general.SkillIds.SequenceEqual([Shelie, Gongxin]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "God Shen Lu Meng must join the classic roster as a three-HP god general.");

        var destinations = Enum.GetValues<SkillProgramCardDestination>();
        Require(destinations.Select(value => (int)value).Distinct().Count() == destinations.Length,
            "SkillProgramCardDestination values must stay unique for stable replay rules.");

        var shelie = current.Skills[Shelie].Program!;
        var trigger = shelie.Triggers.Single();
        Require(shelie.Activations.Count == 0 &&
                trigger.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                trigger.Subject == SkillProgramTriggerSubject.Owner &&
                trigger.Optional && trigger.Priority == 90 &&
                trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement,
            "Shelie must be an optional replacement for the owner's normal draw.");
        Require(trigger.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.RevealTopCards,
            SkillProgramEffectOp.SelectCardSubset,
            SkillProgramEffectOp.MoveBoundCards,
            SkillProgramEffectOp.MoveBoundCards]),
            "Shelie must reveal five cards, take a subset, then clean up the rest.");
        var reveal = trigger.Effects[0];
        Require(reveal.Amount == 5 && reveal.Visibility == SkillProgramCardSetVisibility.Public &&
                reveal.ResultBind == "revealed",
            "Shelie must publicly reveal exactly the top five cards.");
        var subset = trigger.Effects[1];
        Require(subset.SourceBind == "revealed" && subset.ResultBind == "taken" &&
                subset.MinimumCards == 1 && subset.MaximumCards == 4 &&
                subset.MaximumRankSum == 208 && subset.OnePerSuit &&
                !subset.AllowFewerWhenInsufficient,
            "Shelie must take exactly one card of each distinct suit.");
        Require(trigger.Effects[2].SourceBind == "taken" &&
                trigger.Effects[2].Destination == SkillProgramCardDestination.OwnerHand &&
                trigger.Effects[3].SourceBind == "revealed" &&
                trigger.Effects[3].ExceptBind == "taken" &&
                trigger.Effects[3].Destination == SkillProgramCardDestination.DiscardPile,
            "Shelie gains the taken cards and discards the remaining reveals.");

        var gongxin = current.Skills[Gongxin].Program!;
        var activation = gongxin.Activations.Single();
        Require(gongxin.Triggers.Count == 0 &&
                activation.UsesPerPhase == 1 && activation.UsesPerTurn is null &&
                activation.MinCards == 0 && activation.MaxCards == 0 &&
                activation.MinTargets == 1 && activation.MaxTargets == 1 &&
                activation.TargetKind == SkillProgramTargetKind.OtherLivingWithHand &&
                activation.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.RevealTargetHandCard,
                    SkillProgramEffectOp.ChooseOption,
                    SkillProgramEffectOp.MoveBoundCards,
                    SkillProgramEffectOp.MoveBoundCards]),
            "Gongxin must be a once-per-play-phase activation against one other hand.");
        var shown = activation.Effects[0];
        Require(shown.ChooserRef!.Kind == ProgramParticipantRef.Owner &&
                shown.CardOwnerRef!.Kind == ProgramParticipantRef.SelectedTarget &&
                shown.ResultBind == "shown" &&
                shown.RevealMode == SkillProgramRevealMode.Chooser &&
                shown.Suits.SequenceEqual([Suit.Heart]) && shown.AllowDecline,
            "Gongxin must let the owner reveal one of the target's hearts or decline.");
        var choice = activation.Effects[1];
        Require(choice.ResultBind == "disposition" &&
                choice.Options.Select(item => item.Id).SequenceEqual(["discard", "top"]) &&
                choice.Condition is { Kind: SkillProgramConditionKind.BoundCardsMatchSuits, SourceBind: "shown" } suitsCondition &&
                suitsCondition.Suits.SequenceEqual([Suit.Heart]),
            "The Gongxin disposition choice must exist only for a revealed heart.");
        Require(activation.Effects[2].Destination == SkillProgramCardDestination.DiscardPile &&
                activation.Effects[2].Condition is { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "disposition", OptionId: "discard" } &&
                activation.Effects[3].Destination == SkillProgramCardDestination.DrawPileTop &&
                activation.Effects[3].Condition is { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "disposition", OptionId: "top" },
            "The two Gongxin branches must discard the heart or place it on the draw pile top.");
    }

    public static void ShelieReplacesDrawWithDistinctSuitsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 80 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            DriveUntil(game, () => false, stopAtSkills: [Shelie]);
            if (game.State.Status == EngineStatus.Completed || !IsProgramPrompt(game, Shelie)) continue;
            var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (!AnswerActivate(game) || !AnswerActivate(replay)) continue;
            AnswerLargestSubset(game);
            AnswerLargestSubset(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Shelie replacement must replay identically from the paused checkpoint.");
            var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                .Single(item => item.SkillId == Shelie);
            Require(revealed.Bind == "revealed" && revealed.Cards.Count == 5,
                "Shelie must publicly reveal exactly the top five cards.");
            var suits = revealed.Cards.Select(card => card.Suit).Distinct().Count();
            var gains = game.CardMovements.Where(movement =>
                movement.Reason.Value.Contains(Shelie, StringComparison.Ordinal) &&
                movement.To == CardLocation.Hand(0)).ToArray();
            var discards = game.CardMovements.Where(movement =>
                movement.Reason.Value.Contains(Shelie, StringComparison.Ordinal) &&
                movement.To == CardLocation.DiscardPile).ToArray();
            Require(gains.Length == suits && discards.Length == 5 - suits,
                "Shelie must gain one card per distinct suit and discard the remaining reveals.");
            var gainedIds = gains.Select(movement => movement.CardId).ToHashSet();
            Require(gainedIds.Count == gains.Length &&
                    gainedIds.IsSubsetOf(revealed.Cards.Select(card => card.Id)),
                "The gained cards must be distinct revealed cards.");
            var takenSuits = revealed.Cards.Where(card => gainedIds.Contains(card.Id))
                .Select(card => card.Suit).ToHashSet();
            Require(takenSuits.Count == gainedIds.Count,
                "The taken cards must contain exactly one card per distinct suit.");
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + suits,
                "Shelie must add exactly one card per distinct suit to the owner's hand.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Shelie && item.Completed),
                "Shelie must resolve as a completed program trigger binding.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Shelie replacement draw.");
    }

    public static void ShelieDeclineKeepsNormalDraw()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 80 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            DriveUntil(game, () => false, stopAtSkills: [Shelie]);
            if (game.State.Status == EngineStatus.Completed || !IsProgramPrompt(game, Shelie)) continue;
            var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (!AnswerSkip(game) || !AnswerSkip(replay)) continue;
            DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
            DriveUntil(replay, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
            if (game.State.Status == EngineStatus.Completed ||
                game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;

            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Shelie decline must replay identically from the paused checkpoint.");
            Require(!game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .Any(item => item.SkillId == Shelie),
                "A declined Shelie must not reveal any cards.");
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 2,
                "A declined Shelie must leave the normal two-card draw in place.");
            completed++;
        }
        Require(completed == 1, "No seeded setup declined the Shelie replacement.");
    }

    public static void GongxinRevealsHeartDiscardsItAndReplays() => RunGongxin("discard");

    public static void GongxinPlacesHeartOnDrawPileTopAndReplays() => RunGongxin("top");

    public static void GongxinDeclineWithoutHeartKeepsHandHidden()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!DriveToFirstPlay(game)) continue;
            var targetHand = game.CreateSnapshot(0, true).Players[1].Hand;
            if (targetHand.Count == 0 || targetHand.Any(card => card.Suit == Suit.Heart)) continue;
            var before = targetHand.Count;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (!ActivateGongxin(game, 1) || !ActivateGongxin(replay, 1)) continue;

            var prompt = game.PendingDecision;
            Require(prompt is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } &&
                    IsProgramPrompt(game, Gongxin),
                "The Gongxin decline prompt must stay a private program decision.");
            Require(prompt!.Choices.Count == 1 &&
                    prompt.Choices.Single().Parameters.GetValueOrDefault("program-action") ==
                        "reveal-target-hand-card-decline",
                "Without a heart the Gongxin prompt must offer exactly the decline.");
            if (replay.PendingDecision is null || replay.PendingDecision.Choices.Count != 1) continue;

            Answer(game, prompt.Choices.Single());
            Answer(replay, replay.PendingDecision.Choices.Single());
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Gongxin decline must replay identically from the paused checkpoint.");
            Require(!game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .Any(item => item.SkillId == Gongxin),
                "A declined Gongxin must not reveal any card.");
            Require(!game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Any(item => item.SkillId == Gongxin),
                "A declined Gongxin must not reach the disposition choice.");
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Count == before,
                "A declined Gongxin must leave the target's hand untouched.");
            completed++;
        }
        Require(completed == 1, "No seeded setup declined Gongxin without a heart.");
    }

    private static void RunGongxin(string disposal)
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!DriveToFirstPlay(game)) continue;
            var targetHand = game.CreateSnapshot(0, true).Players[1].Hand;
            var hearts = targetHand.Where(card => card.Suit == Suit.Heart).ToArray();
            if (hearts.Length == 0) continue;
            var before = targetHand.Count;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            if (!ActivateGongxin(game, 1) || !ActivateGongxin(replay, 1)) continue;
            Require(game.CreateSnapshot(1).PendingDecision is null &&
                    game.CreateSnapshot(2).PendingDecision is null,
                "The private Gongxin hand view must not leak into other players' snapshots.");

            var prompt = game.PendingDecision;
            Require(prompt is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } &&
                    IsProgramPrompt(game, Gongxin),
                "Gongxin must present a private program prompt to the owner.");
            var eligible = prompt!.Choices.Where(item =>
                item.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card").ToArray();
            var declines = prompt.Choices.Where(item =>
                item.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card-decline")
                .ToArray();
            if (eligible.Length != hearts.Length || declines.Length != 1) continue;
            Require(eligible.Select(item => item.Cards.Single()).ToHashSet()
                    .SetEquals(hearts.Select(card => card.Id)),
                "Only the target's hearts may be offered for the Gongxin reveal.");
            var reveal = eligible.OrderBy(item => item.Id.Value, StringComparer.Ordinal).First();
            var shownId = reveal.Cards.Single();

            var paused = RoundTrip(game.CreateCheckpoint());
            var resumed = GameReplay.Restore(paused, registry);
            Answer(game, reveal);
            Answer(resumed, reveal);
            if (!AnswerOption(game, disposal) || !AnswerOption(resumed, disposal)) continue;
            DriveUntilSettled(game);
            DriveUntilSettled(resumed);

            Require(Events(game).SequenceEqual(Events(resumed)) && State(game) == State(resumed),
                "The Gongxin disposal must replay identically from the paused reveal.");
            var shown = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                .Single(item => item.SkillId == Gongxin && item.Bind == "shown");
            Require(shown.Cards.Count == 1 && shown.Cards[0].Id == shownId &&
                    shown.Cards[0].Suit == Suit.Heart,
                "Gongxin must reveal exactly the chosen heart.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Any(item => item.SkillId == Gongxin),
                "Gongxin must resolve as a program skill activation.");
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Count == before - 1,
                "The disposed heart must leave the target's hand.");

            if (disposal == "discard")
            {
                Require(game.CardMovements.Any(movement =>
                        movement.CardId == shownId &&
                        movement.From == CardLocation.Hand(1) &&
                        movement.To == CardLocation.DiscardPile &&
                        movement.Reason.Value.Contains(Gongxin, StringComparison.Ordinal)),
                    "The discard branch must move the shown heart to the discard pile.");
            }
            else
            {
                Require(game.CardMovements.Any(movement =>
                        movement.CardId == shownId &&
                        movement.From == CardLocation.Hand(1) &&
                        movement.To == CardLocation.DrawPile),
                    "The top branch must move the shown heart onto the draw pile.");
                DriveUntil(game, () => game.CardMovements.Any(movement =>
                    movement.CardId == shownId && movement.From == CardLocation.DrawPile));
                DriveUntil(resumed, () => resumed.CardMovements.Any(movement =>
                    movement.CardId == shownId && movement.From == CardLocation.DrawPile));
                Require(game.CardMovements.Any(movement =>
                        movement.CardId == shownId &&
                        movement.From == CardLocation.DrawPile &&
                        movement.To.Zone == CardZoneKind.Hand),
                    "The placed heart must be the next card drawn into someone's hand.");
                Require(Events(game).SequenceEqual(Events(resumed)) && State(game) == State(resumed),
                    "The draw-pile-top placement must stay replay-consistent through the next draw.");
            }
            completed++;
        }
        Require(completed == 1, $"No seeded setup resolved the Gongxin {disposal} branch.");
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
        return game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    }

    private static bool ActivateGongxin(GameEngine game, int targetSeat)
    {
        if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } prompt) return false;
        var result = game.Submit(new UseProgramSkillCommand(0, Gongxin, "watch-and-dispose",
            [], [targetSeat], game.Revision, prompt.PromptId));
        return result.Accepted;
    }

    private static bool AnswerActivate(GameEngine game)
    {
        if (game.PendingDecision is not { } prompt) return false;
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "activate");
        if (choice is null) return false;
        Answer(game, choice);
        return true;
    }

    private static bool AnswerSkip(GameEngine game)
    {
        if (game.PendingDecision is not { } prompt) return false;
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip");
        if (choice is null) return false;
        Answer(game, choice);
        return true;
    }

    private static void AnswerLargestSubset(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The subset prompt vanished.");
        var choice = prompt.Choices
            .Where(item => item.Parameters.GetValueOrDefault("program-action") == "select-subset")
            .OrderByDescending(item => item.Cards.Count)
            .ThenByDescending(item => int.Parse(
                item.Parameters.GetValueOrDefault("rank-sum") ?? "0",
                System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
        Answer(game, choice);
    }

    private static bool AnswerOption(GameEngine game, string optionId)
    {
        if (game.PendingDecision is not { } prompt) return false;
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("option-id") == optionId);
        if (choice is null) return false;
        Answer(game, choice);
        return true;
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                case DecisionKind.SelectFaction:
                    Answer(game, prompt.Choices.First(item =>
                        item.Parameters.ContainsKey("faction-id")));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Lu Meng answer failed.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new ShenLuMengScenario(Mode, Deck()));

    private static ContentDeckRecipe Deck() =>
        new("fixture:shen-lu-meng-deck", "神吕蒙测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard("standard:slash", (Suit)(index % 4), index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Shen Lu Meng fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Shen Lu Meng selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Lu Meng fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Shen Lu Meng command failed.");
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

    private sealed class ShenLuMengScenario(string modeId, ContentDeckRecipe deck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shen-lu-meng-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 160, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Shelie and Gongxin on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-lu-meng-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-lu-meng-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-lu-meng-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-lu-meng-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "神吕蒙测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:shen-lu-meng-bank-a",
                    "fixture:shen-lu-meng-bank-b",
                    "fixture:shen-lu-meng-bank-c",
                    "fixture:shen-lu-meng-bank-d"]));
        }
    }
}
