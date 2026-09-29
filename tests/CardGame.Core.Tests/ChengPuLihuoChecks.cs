using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChengPuLihuoChecks
{
    private const int HumanSeat = 0;
    private const string OwnerGeneralId = "fixture:lihuo-owner";
    private const string LowHpOwnerGeneralId = "fixture:lihuo-low-hp-owner";
    private const string ChainedOwnerGeneralId = "fixture:lihuo-chained-owner";
    private const string PreWoundSkillId = "fixture:lihuo-pre-wound";
    private const string LihuoSkillId = "classic:lihuo";
    private const string ChunlaoSkillId = "classic:chunlao";

    public static void ConvertedFireSlashAddsTargetAndLosesHpOnce()
    {
        var game = CreateGame(ScenarioPackage.SlashModeId, seed: 1);
        ReachHumanPlay(game);
        var slash = Player(game, HumanSeat).Hand.First(card => card.Kind == CardKind.Slash);
        var actions = game.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash && action.CardId == slash.Id).ToArray();
        var ordinary = actions.FirstOrDefault(action =>
            action.PlayedCardKind is null);
        var convertedSingle = actions.FirstOrDefault(action =>
            action.PlayedCardKind == CardKind.FireSlash &&
            action.ConversionSource?.SkillId == LihuoSkillId);
        var convertedMulti = actions.FirstOrDefault(action =>
            action.PlayedCardKind == CardKind.FireSlash &&
            action.ConversionSource?.SkillId == LihuoSkillId &&
            action.TargetSeats.Count == 2);
        Require(ordinary is not null && convertedSingle is not null && convertedMulti is not null,
            "Lihuo must preserve ordinary Slash while publishing separate converted one/two-target Fire Slash actions.");
        ArgumentNullException.ThrowIfNull(convertedSingle);
        ArgumentNullException.ThrowIfNull(convertedMulti);

        var before = game.CreateCheckpoint();
        var forgedConversionSource = game.Submit(new PlayCardCommand(
            HumanSeat,
            convertedSingle.CardId!.Value,
            convertedSingle.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            convertedSingle.PlayedCardKind)
        {
            ConversionSource = new CardConversionSource("fixture:forged", "forged", HumanSeat, "forged")
        });
        Require(!forgedConversionSource.Accepted && State(game) == State(GameReplay.Restore(before, Registry())),
            "A forged Fire Slash command with an invalid conversion source must be rejected atomically.");

        var forgedAdditionalConversion = game.Submit(new PlayCardCommand(
            HumanSeat,
            convertedMulti.CardId!.Value,
            convertedMulti.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            convertedMulti.PlayedCardKind)
        {
            ConversionSource = convertedMulti.ConversionSource,
            AdditionalConversionSources =
            [new CardConversionSource("fixture:forged", "forged", HumanSeat, "forged")]
        });
        Require(!forgedAdditionalConversion.Accepted && State(game) == State(GameReplay.Restore(before, Registry())),
            "A forged multi-target Lihuo command with an undeclared chained conversion must be rejected atomically.");

        var ownerHpBefore = Player(game, HumanSeat).Hp;
        var eventStart = game.Events.Count;
        var used = Play(game, convertedMulti);
        Require(used.Accepted, used.Error?.Message ?? "The exact converted Lihuo Fire Slash was rejected.");
        var events = game.Events.Skip(eventStart).Select(item => item.Payload).ToArray();
        var targetRule = events.OfType<ProgramCardTargetCountAppliedEvent>().Single();
        var hpLoss = events.OfType<ProgramSkillHpLostEvent>().Single(item => item.SkillId == LihuoSkillId);
        var finishedIndex = Array.FindIndex(events, item => item is CardUseFinishedEvent);
        var hpLossIndex = Array.FindIndex(events, item => item is ProgramSkillHpLostEvent lost && lost.SkillId == LihuoSkillId);
        Require(targetRule.EffectiveCardKind == CardKind.FireSlash &&
                targetRule.TargetSeats.SequenceEqual(convertedMulti.TargetSeats) &&
                targetRule.ContributionSourceIds.Count == 1 &&
                events.OfType<CardUsedEvent>().Count() == 1 &&
                events.OfType<DamageAppliedEvent>().Count() >= 2 &&
                Player(game, HumanSeat).Hp == ownerHpBefore - 1 &&
                hpLoss.Amount == 1 &&
                events.OfType<ProgramBindingResolvedEvent>().Single(item => item.SkillId == LihuoSkillId)
                    is { Window: SkillProgramTriggerWindow.CardUseCompleted, Completed: true } &&
                finishedIndex >= 0 && hpLossIndex > finishedIndex,
            "One converted multi-target Fire Slash must resolve every target, finish once, then lose exactly one HP.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), Registry());
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A completed Lihuo multi-target use must replay exactly.");
    }

    public static void NativeAndZhuqueFireSlashDoNotPayConversionPenalty()
    {
        var native = CreateGame(ScenarioPackage.FireModeId, seed: 1);
        ReachHumanPlay(native);
        var nativeFire = native.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.TargetSeats.Count == 2);
        var nativeHp = Player(native, HumanSeat).Hp;
        var nativeStart = native.Events.Count;
        Require(Play(native, nativeFire).Accepted, "The native Fire Slash Lihuo target extension was rejected.");
        var nativeEvents = native.Events.Skip(nativeStart).Select(item => item.Payload).ToArray();
        Require(nativeEvents.OfType<ProgramCardTargetCountAppliedEvent>().Single() is
                    { EffectiveCardKind: CardKind.FireSlash } &&
                nativeEvents.OfType<DamageAppliedEvent>().Count() >= 2 &&
                nativeEvents.All(item => item is not ProgramSkillHpLostEvent { SkillId: LihuoSkillId }) &&
                Player(native, HumanSeat).Hp == nativeHp,
            "A native Fire Slash may add one Lihuo target but must not pay the conversion penalty.");

        var zhuque = FindZhuqueGame();
        var weapon = Player(zhuque, HumanSeat).Hand.First(card => card.Kind == CardKind.ZhuqueFan);
        Require(zhuque.Submit(new PlayCardCommand(
            HumanSeat,
            weapon.Id,
            [],
            zhuque.Revision,
            zhuque.PendingDecision!.PromptId)).Accepted,
            "The Lihuo fixture could not equip Zhuque Fan.");
        var zhuqueAction = zhuque.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.PlayedCardKind == CardKind.FireSlash &&
            action.ConversionSource is null &&
            action.TargetSeats.Count == 2);
        var zhuqueHp = Player(zhuque, HumanSeat).Hp;
        var zhuqueStart = zhuque.Events.Count;
        var zhuqueUsed = Play(zhuque, zhuqueAction);
        Require(zhuqueUsed.Accepted,
            zhuqueUsed.Error?.Message ??
            "The Zhuque-converted Fire Slash with a Lihuo extra target was rejected.");
        var zhuqueEvents = zhuque.Events.Skip(zhuqueStart).Select(item => item.Payload).ToArray();
        Require(zhuqueEvents.OfType<ZhuqueFanConvertedEvent>().Count() == 1 &&
                zhuqueEvents.OfType<ProgramCardTargetCountAppliedEvent>().Single() is
                    { EffectiveCardKind: CardKind.FireSlash } &&
                zhuqueEvents.All(item => item is not ProgramSkillHpLostEvent { SkillId: LihuoSkillId }) &&
                Player(zhuque, HumanSeat).Hp == zhuqueHp,
            "Zhuque Fan supplies the Fire conversion, so Lihuo's extra target must not cause HP loss.");
    }

    public static void FullyDodgedConversionDoesNotLoseHp()
    {
        GameEngine? selected = null;
        LegalAction? selectedAction = null;
        for (var seed = 1; seed <= 512 && selected is null; seed++)
        {
            var candidate = CreateGame(ScenarioPackage.DodgeModeId, seed);
            ReachHumanPlay(candidate);
            var revealed = candidate.CreateSnapshot(HumanSeat, revealAll: true);
            selectedAction = candidate.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.ConversionSource?.SkillId == LihuoSkillId &&
                action.TargetSeats.Count == 1 &&
                revealed.Players[action.TargetSeats[0]].Role == Role.Rebel &&
                revealed.Players[action.TargetSeats[0]].Hand.Any(card => card.Kind == CardKind.Dodge));
            if (selectedAction is not null) selected = candidate;
        }
        Require(selected is not null && selectedAction is not null,
            "No bounded hostile Dodge fixture exposed a converted Lihuo Slash.");
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(selectedAction);

        var ownerHp = Player(selected, HumanSeat).Hp;
        var start = selected.Events.Count;
        Require(Play(selected, selectedAction).Accepted, "The Dodge-boundary Lihuo Slash was rejected.");
        var events = selected.Events.Skip(start).Select(item => item.Payload).ToArray();
        Require(events.OfType<CardRespondedEvent>().Any(item => item.EffectiveCardKind == CardKind.Dodge) &&
                events.All(item => item is not DamageAppliedEvent) &&
                events.All(item => item is not ProgramSkillHpLostEvent { SkillId: LihuoSkillId }) &&
                Player(selected, HumanSeat).Hp == ownerHp,
            "A converted Fire Slash canceled by Dodge must not make the Lihuo owner lose HP.");
    }

    public static void CompletedPenaltyCanEnterDyingAndReplay()
    {
        var registry = Registry();
        var game = CreateGame(ScenarioPackage.LethalPenaltyModeId, seed: 1,
            ownerGeneralId: LowHpOwnerGeneralId);
        ReachHumanPlay(game);
        Require(Player(game, HumanSeat).Hp == 1,
            "The Lihuo penalty fixture must pre-wound its owner to one HP.");
        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.ConversionSource?.SkillId == LihuoSkillId);
        Require(Play(game, action).Accepted,
            "The one-HP Lihuo owner could not use a converted Fire Slash.");
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<DamageAppliedEvent>().Any() &&
                events.OfType<ProgramSkillHpLostEvent>().Single(item =>
                    item.SkillId == LihuoSkillId) is { Amount: 1, RemainingHp: 0 } &&
                events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == HumanSeat) &&
                game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: HumanSeat },
            $"A lethal configured Lihuo penalty must suspend the completed card use into dying: " +
            $"status={game.State.Status}, pending={game.PendingDecision?.Kind}, " +
            $"damage={events.OfType<DamageAppliedEvent>().Count()}, " +
            $"loss={events.OfType<ProgramSkillHpLostEvent>().Count(item => item.SkillId == LihuoSkillId)}, " +
            $"dying={events.OfType<PlayerDyingEvent>().Count(item => item.VictimSeat == HumanSeat)}.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The configured Lihuo dying prompt must restore exactly.");
        foreach (var branch in new[] { game, paused })
        {
            var decision = RequirePrompt(branch, DecisionKind.RescueDying);
            Answer(branch, decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "let-die"));
        }
        Require(game.State.Status == EngineStatus.Completed &&
                State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The Lihuo penalty dying continuation must finish once and replay deterministically.");
    }

    public static void ChainedWushengLihuoConversionKeepsBothSources()
    {
        var registry = Registry();
        GameEngine? game = null;
        LegalAction? action = null;
        for (var seed = 1; seed <= 128 && action is null; seed++)
        {
            var candidate = CreateGame(ScenarioPackage.ChainedConversionModeId, seed,
                ownerGeneralId: ChainedOwnerGeneralId);
            ReachHumanPlay(candidate);
            action = candidate.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash &&
                item.PlayedCardKind == CardKind.FireSlash &&
                item.ConversionSource?.SkillId == "classic:wusheng" &&
                item.AdditionalConversionSources?.Single().SkillId == LihuoSkillId);
            if (action is not null) game = candidate;
        }
        Require(game is not null && action is not null,
            "No bounded red Peach exposed the Wusheng then Lihuo action.");
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(action);
        var hpBefore = Player(game, HumanSeat).Hp;
        var used = Play(game, action);
        Require(used.Accepted, used.Error?.Message ??
            "The Wusheng-to-Lihuo chained conversion was rejected.");
        var events = game.Events.Select(item => item.Payload).ToArray();
        var cardAction = events.OfType<CardActionAcceptedEvent>().Single(item =>
            item.Action.EffectiveKind == CardKind.FireSlash &&
            item.Action.ConversionChain.Any(source => source.SkillId == "classic:wusheng"));
        Require(cardAction.Action.ConversionChain.Select(source => source.SkillId)
                    .SequenceEqual(["classic:wusheng", LihuoSkillId]) &&
                events.All(item => item is not LihuoSlashUsedEvent) &&
                events.OfType<ProgramSkillHpLostEvent>().Single(item => item.SkillId == LihuoSkillId)
                    is { Amount: 1 } &&
                Player(game, HumanSeat).Hp == hpBefore - 1 &&
                events.All(item => item is not ZhuqueFanConvertedEvent),
            "The chained Fire Slash must retain both conversion sources and pay the penalty once.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The Wusheng-to-Lihuo chain must replay exactly.");
    }

    public static void ChunlaoContentAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals["classic:cheng-pu"];
        var chunlao = current.Skills[ChunlaoSkillId];

        Require(general is
                {
                    Name: "程普",
                    PortraitKey: "cheng_pu",
                    FactionId: "wu",
                    BaseHp: 4,
                    Gender: GeneralGender.Male
                } && general.SkillIds.SequenceEqual([LihuoSkillId, ChunlaoSkillId]),
            "Classic Cheng Pu metadata drifted.");
        Require(chunlao.Tags == SkillTag.None &&
                chunlao.ActionForms == SkillActionForm.None &&
                chunlao.ExecutionForms == SkillExecutionForm.Trigger,
            "Chunlao must remain an optional trigger rather than an active play-phase action.");
        Require(chunlao.Program is { Triggers.Count: 2 } &&
                chunlao.Program.Triggers[0] is
                { Id: "store-chun-at-turn-end", Window: SkillProgramTriggerWindow.TurnEnding, Optional: true } &&
                chunlao.Program.Triggers[1] is
                { Id: "spend-chun-for-dying-alcohol", Window: SkillProgramTriggerWindow.DyingResponse, Optional: true } &&
                chunlao.Program.Triggers[0].Effects[0] is
                { MinimumCards: 1, MaximumCards: 20, CardKinds.Count: 3 },
            "Current Chunlao must compose end-phase storage and dying rescue with the configured selection cost.");
        var rules = ReadResource("CardGame.Content.Standard.SkillPrograms.owned-zone-storage-skills.rules.json");
        var presentation = ReadResource("CardGame.Content.Standard.SkillPrograms.owned-zone-storage-skills.presentation.json");
        try
        {
            SkillProgramCatalog.Load(
                rules.Replace("\"schemaVersion\": 62", "\"schemaVersion\": 57", StringComparison.Ordinal),
                presentation);
            throw new InvalidOperationException("Unsupported schema 57 accepted variable owned-card selection.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("expected 62", StringComparison.Ordinal))
        {
        }

        var rescueRules = ReadResource(
            "CardGame.Content.Standard.SkillPrograms.owned-zone-dying-rescue-skills.rules.json");
        try
        {
            SkillProgramCatalog.Load(
                rescueRules.Replace("\"schemaVersion\": 62", "\"schemaVersion\": 57", StringComparison.Ordinal),
                presentation);
            throw new InvalidOperationException("Unsupported schema 57 accepted cross-seat dying response.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("expected 62", StringComparison.Ordinal))
        {
        }

    }

    public static void ChunlaoStoresExactSlashesAndReplays()
    {
        var registry = Registry();
        var game = CreateGame(ScenarioPackage.SlashModeId, seed: 1);
        ReachHumanPlay(game);
        var handBefore = Player(game, HumanSeat).HandCount;
        EndHumanPlay(game);
        var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(activation.IsPrivate && activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip"),
            "An empty Chun pile must offer a generic optional turn-ending binding.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "Other players must not receive the owner's private Chunlao hand candidates.");
        Answer(game, activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var firstPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(firstPrompt.ValidCardIds.Count == handBefore &&
                firstPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"),
            "The composed storage prompt must expose only the owner's Slash cards.");

        var first = firstPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Answer(game, first);
        var secondPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(secondPrompt.ValidCardIds.All(id => id != first.Cards.Single()) &&
                game.CardMovements.All(move => move.To != CardLocation.Chunlao(HumanSeat)) &&
                secondPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") &&
                secondPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") != "skip"),
            "After one selection Chunlao must preserve the exact card and expose finish instead of skip.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game),
            "A paused multi-step Chunlao selection must replay exactly.");

        var second = secondPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        var selectedIds = new[] { first.Cards.Single(), second.Cards.Single() };
        Answer(game, second);
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Cards.SequenceEqual(second.Cards)));
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));

        var owner = Player(game, HumanSeat);
        Require(owner.Hand.All(card => !selectedIds.Contains(card.Id)) &&
                owner.ChunlaoCount == selectedIds.Length &&
                owner.ChunlaoCards?.Select(card => card.Id).SequenceEqual(selectedIds) == true &&
                game.CardMovements.Count(move =>
                    selectedIds.Contains(move.CardId) &&
                    move.From == CardLocation.Hand(HumanSeat) &&
                    move.To == CardLocation.Chunlao(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:chunlao.MoveBoundCards") == selectedIds.Length &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == ChunlaoSkillId && item.OwnerSeat == HumanSeat && item.Completed),
            "Finishing Chunlao must atomically move the exact selected Slash cards into a public owner pile.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Chunlao storage branch must replay exactly.");
    }

    public static void ChunlaoRescuesWithVirtualAlcoholAndReplays()
    {
        var fixture = FindChunlaoRescuePrompt();
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.RescueDying);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId);
        var victimSeat = int.Parse(choice.Parameters["target-seat"],
            System.Globalization.CultureInfo.InvariantCulture);
        var victimBefore = Player(game, victimSeat);
        var chunCardId = Player(game, HumanSeat).ChunlaoCards!.Single().Id;
        Require(victimBefore.Hp <= 0 &&
                Player(game, HumanSeat).ChunlaoCards?.Any(card => card.Id == chunCardId) == true,
            "The dying prompt must expose a configured rescue over Cheng Pu's public Chun pile.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(State(paused) == State(game),
            "A paused Chunlao dying response must restore exactly.");
        var eventStart = game.Events.Count;
        Answer(game, choice);
        Answer(paused, RequirePrompt(paused, DecisionKind.RescueDying).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId));
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The activated Chunlao rescue must replay at its source-card prompt.");
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            candidate.Cards.SequenceEqual([chunCardId])));
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            candidate.Cards.SequenceEqual([chunCardId])));

        var events = game.Events.Skip(eventStart).Select(item => item.Payload).ToArray();
        var rescued = events.OfType<ProgramDyingRescueEvent>().Single();
        var declared = events.OfType<CardUseDeclaredEvent>().Single(item => item.CardId == chunCardId);
        var targets = events.OfType<TargetsConfirmedEvent>().Single(item =>
            item.ResolutionId == declared.ResolutionId);
        Require(rescued.OwnerSeat == HumanSeat && rescued.VictimSeat == victimSeat &&
                rescued.CardId == chunCardId && rescued.SkillId == ChunlaoSkillId &&
                rescued.RecoveredHp == 1 &&
                rescued.VictimHp == Math.Min(victimBefore.MaxHp, victimBefore.Hp + 1) &&
                declared.CardKind == CardKind.Alcohol && declared.SourceSeat == victimSeat &&
                targets.TargetSeats.SequenceEqual([victimSeat]) &&
                events.OfType<CardUseFinishedEvent>().Any(item =>
                    item.CardId == chunCardId && item.CardKind == CardKind.Alcohol) &&
                game.CardMovements.Any(move =>
                    move.CardId == chunCardId &&
                    move.From == CardLocation.Chunlao(HumanSeat) &&
                    move.To == CardLocation.Processing &&
                    move.Reason.Value == "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol") &&
                game.CardMovements.Any(move =>
                    move.CardId == chunCardId &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason.Value == "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol"),
            "Chunlao must pay one public Chun and make the victim use a replayable virtual Alcohol on itself.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Chunlao dying response must replay exactly.");
    }

    public static void ChunlaoAiStoresOneExplainedReserve()
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ChunlaoAiModeId,
            HumanSeat = -1,
            HumanRole = null,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted,
            "The AI Chunlao fixture failed to start.");

        for (var step = 0; step < 256; step++)
        {
            var stored = game.CardMovements.FirstOrDefault(move =>
                move.To.Zone == CardZoneKind.Chunlao &&
                move.Reason.Value == "skill-program.classic:chunlao.MoveBoundCards");
            if (stored is not null)
            {
                var ownerSeat = stored.To.OwnerSeat!.Value;
                Require(game.CreateSnapshot(ownerSeat, revealAll: true).Players[ownerSeat].ChunlaoCount == 1 &&
                        game.AiThoughts.Any(thought =>
                            thought.ActorSeat == ownerSeat &&
                            thought.Decision.Contains("醇醪", StringComparison.Ordinal) &&
                            thought.Candidates.Any(candidate =>
                                candidate.Action.Description == "跳过【醇醪】")),
                    "Chunlao AI must keep exactly one public reserve and explain both use and skip candidates.");
                return;
            }

            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "The AI Chunlao fixture could not advance.");
        }
        throw new InvalidOperationException("The AI Chunlao fixture stored no reserve in bounded steps.");
    }

    private static (GameEngine Game, ContentRegistry Registry) FindChunlaoRescuePrompt()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateGame(ScenarioPackage.ChunlaoModeId, seed);
            ReachHumanPlay(game);
            EndHumanPlay(game);
            var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
            Answer(game, activation.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));

            for (var step = 0; step < 256 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: HumanSeat } dying &&
                    dying.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                        choice.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId))
                {
                    return (game, registry);
                }

                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } play)
                {
                    var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Slash &&
                        action.TargetSeats.Count == 1 &&
                        Player(game, action.TargetSeats[0]).Hp <= 1);
                    if (slash is not null)
                    {
                        Require(Play(game, slash).Accepted,
                            "The Chunlao rescue fixture could not use its lethal Slash.");
                    }
                    else
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            play.PromptId));
                        Require(ended.Accepted,
                            ended.Error?.Message ?? "The Chunlao rescue fixture could not end Play.");
                    }
                    continue;
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    var decline = human.Choices.FirstOrDefault(choice =>
                        choice.Cards.Count == 0 && choice.Targets.Count == 0);
                    if (decline is null) break;
                    Answer(game, decline);
                    continue;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "The Chunlao rescue fixture could not advance.");
            }
        }
        throw new InvalidOperationException("No bounded Chunlao dying-response fixture was found.");
    }

    private static void EndHumanPlay(GameEngine game)
    {
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat,
            game.Revision,
            play.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "The Cheng Pu fixture could not end Play.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            game.PendingDecision?.PromptId ?? throw new InvalidOperationException("No human prompt is pending."),
            choice.Id,
            game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The Cheng Pu prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static GameEngine FindZhuqueGame()
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(ScenarioPackage.ZhuqueModeId, seed);
            ReachHumanPlay(game);
            var hand = Player(game, HumanSeat).Hand;
            if (hand.Any(card => card.Kind == CardKind.ZhuqueFan) &&
                hand.Any(card => card.Kind == CardKind.Slash))
            {
                return game;
            }
        }
        throw new InvalidOperationException("No bounded Lihuo/Zhuque fixture dealt both required cards.");
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        HumanSeat,
        action.CardId!.Value,
        action.TargetSeats,
        game.Revision,
        game.PendingDecision!.PromptId,
        action.PlayedCardKind,
        action.TargetCardId)
    {
        ConversionSource = action.ConversionSource,
        AdditionalConversionSources = action.AdditionalConversionSources,
    });

    private static GameEngine CreateGame(string modeId, int seed, int rulesVersion = GameCheckpoint.CurrentRulesVersion,
        string ownerGeneralId = OwnerGeneralId)
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = true,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }
        Require(game.Submit(new StartGameCommand()).Accepted, "The Lihuo fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Lihuo fixture has no setup prompt.");
        Require(prompt.Kind == DecisionKind.SelectGeneral && prompt.ValidContentIds.Contains(ownerGeneralId),
            "The Lihuo fixture did not offer its owner general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            ownerGeneralId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Lihuo fixture could not select its owner.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Lihuo play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Lihuo fixture could not advance.");
        }
        throw new InvalidOperationException("The Lihuo fixture did not reach human Play.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static string ReadResource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name) ??
            throw new InvalidOperationException($"Missing embedded rules resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string SlashModeId = "identity:classic-lihuo-slash-test-4";
        public const string FireModeId = "identity:classic-lihuo-fire-test-4";
        public const string ZhuqueModeId = "identity:classic-lihuo-zhuque-test-4";
        public const string DodgeModeId = "identity:classic-lihuo-dodge-test-4";
        public const string LethalPenaltyModeId = "identity:classic-lihuo-lethal-penalty-test-4";
        public const string ChainedConversionModeId = "identity:classic-lihuo-chained-conversion-test-4";
        public const string ChunlaoModeId = "identity:classic-chunlao-rescue-test-4";
        public const string ChunlaoAiModeId = "identity:classic-chunlao-ai-test-4";
        private const string SlashDeckId = "fixture:lihuo-slash-deck";
        private const string FireDeckId = "fixture:lihuo-fire-deck";
        private const string ZhuqueDeckId = "fixture:lihuo-zhuque-deck";
        private const string DodgeDeckId = "fixture:lihuo-dodge-deck";
        private const string LethalPenaltyDeckId = "fixture:lihuo-lethal-penalty-deck";
        private const string ChainedConversionDeckId = "fixture:lihuo-chained-conversion-deck";
        private const string ChunlaoDeckId = "fixture:chunlao-slash-deck";
        private static readonly string[] ChunlaoTargetIds =
            ["fixture:chunlao-target-1", "fixture:chunlao-target-2", "fixture:chunlao-target-3"];
        private static readonly string[] ChunlaoAiOwnerIds =
            ["fixture:chunlao-ai-1", "fixture:chunlao-ai-2", "fixture:chunlao-ai-3", "fixture:chunlao-ai-4"];

        public PackageManifest Manifest { get; } = new(
            "lihuo-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 91, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            const string woundRules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:lihuo-pre-wound","revision":1,
                  "minimumRulesVersion": 171,"triggers":[{"id":"pre-wound",
                    "window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                    "effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """;
            const string woundPresentation = """
                {"schemaVersion":3,"skills":{"fixture:lihuo-pre-wound":{
                  "name":"预伤","description":"测试开始时失去一点体力。"}}}
                """;
            var woundCatalog = SkillProgramCatalog.Load(woundRules, woundPresentation);
            builder.AddSkill(new ContentSkillDefinition(PreWoundSkillId, "预伤", "测试开始时失去一点体力。")
            {
                Program = woundCatalog.Programs[PreWoundSkillId],
                ProgramPresentation = woundCatalog.Presentations[PreWoundSkillId]
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "疠火测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 4,
                AdditionalSkillIds: [ChunlaoSkillId]));
            builder.AddGeneral(new ContentGeneralDefinition(
                LowHpOwnerGeneralId,
                "疠火失血测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 1,
                AdditionalSkillIds: [PreWoundSkillId]));
            builder.AddGeneral(new ContentGeneralDefinition(
                ChainedOwnerGeneralId,
                "疠火连续转化测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:wusheng"]));
            foreach (var id in ChunlaoTargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "醇醪测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 1));
            }
            foreach (var id in ChunlaoAiOwnerIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "醇醪AI",
                    "cheng_pu",
                    ChunlaoSkillId,
                    "wu",
                    BaseHp: 4));
            }
            AddDeck(builder, SlashDeckId, "疠火普通杀牌堆", [new("standard:slash", 48)]);
            AddDeck(builder, FireDeckId, "疠火火杀牌堆", [new("standard:fire_slash", 48)]);
            AddDeck(builder, ZhuqueDeckId, "疠火朱雀牌堆",
                [new("classic:zhuque-fan", 16), new("standard:slash", 32)]);
            AddDeck(builder, DodgeDeckId, "疠火闪避牌堆",
                [new("standard:slash", 8), new("standard:dodge", 56)]);
            AddDeck(builder, LethalPenaltyDeckId, "疠火失血濒死牌堆",
                [new("standard:slash", 32), new("standard:peach", 16)]);
            AddDeck(builder, ChainedConversionDeckId, "疠火连续转化牌堆",
                [new("standard:peach", 48)]);
            AddDeck(builder, ChunlaoDeckId, "醇醪濒死测试牌堆", [new("standard:slash", 96)]);
            AddMode(builder, SlashModeId, SlashDeckId);
            AddMode(builder, FireModeId, FireDeckId);
            AddMode(builder, ZhuqueModeId, ZhuqueDeckId);
            AddMode(builder, DodgeModeId, DodgeDeckId);
            AddMode(builder, LethalPenaltyModeId, LethalPenaltyDeckId,
                [LowHpOwnerGeneralId, "classic:sun-quan", "classic:huang-gai", "classic:gan-ning"]);
            AddMode(builder, ChainedConversionModeId, ChainedConversionDeckId,
                [ChainedOwnerGeneralId, "classic:sun-quan", "classic:huang-gai", "classic:gan-ning"]);
            AddMode(builder, ChunlaoModeId, ChunlaoDeckId, [OwnerGeneralId, .. ChunlaoTargetIds]);
            AddMode(builder, ChunlaoAiModeId, ChunlaoDeckId, ChunlaoAiOwnerIds);
        }

        private static void AddDeck(
            IContentRegistryBuilder builder,
            string id,
            string name,
            IReadOnlyList<ContentDeckCardCount> cards) =>
            builder.AddDeck(new ContentDeckRecipe(id, name, InitialHandSize: 4, DrawPerTurn: 0, cards));

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string deckId,
            IReadOnlyList<string>? generalIds = null) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "四人经典身份（疠火场景）",
                MinPlayers: 4,
                MaxPlayers: 4,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: generalIds ??
                    [
                        OwnerGeneralId,
                        "classic:sun-quan",
                        "classic:huang-gai",
                        "classic:gan-ning"
                    ]));
    }
}
