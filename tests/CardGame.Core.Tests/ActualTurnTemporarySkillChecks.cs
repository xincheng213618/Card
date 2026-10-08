using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualTurnTemporarySkillChecks
{
    private const int Recipient = 0;
    private const string Yuxu = "ol:yuxu";
    private const string Shijian = "ol:shijian";
    private const string Driver = "fixture:temporary-skill-driver";
    private const string Movement = "fixture:temporary-skill-movement";
    private const string Gain = "fixture:temporary-skill-gain";
    private const string Extra = "fixture:temporary-skill-extra-play";
    private const string Ended = "fixture:temporary-skill-ended";
    private const string Debt = "next-use-discard";
    private const string DrawTrigger = "draw-and-arm-next-use-discard";
    private const string SettleTrigger = "settle-next-use-discard";
    private const string GrantCost = "skill-program.ol:shijian.SelectAndMoveOwnedCard";
    private const string DebtCost = "skill-program.ol:yuxu.SelectAndMoveOwnedCard";
    private const string YuxuDraw = "skill-program.ol:yuxu.Draw";
    private const string Mode = "identity:classic-actual-turn-temporary-skill-fixture";
    private const string OwnerGeneral = "fixture:temporary-skill-owner";

    public static void ActualSecondUseForeignGrantsShareStateAndExpireOnEnd()
    {
        foreach (var permanent in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(permanent));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 4, HumanSeat = Recipient, HumanRole = Role.Lord, ModeId = Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: Recipient });
            Accept(game, new SelectGeneralCommand(Recipient, OwnerGeneral, game.Revision, P(game)!.PromptId));
            ReachPlay(game);
            var turn = game.State.TurnNumber;
            var starts = E<TurnStartedEvent>(game).Count();
            Require(E<ProgramPhaseScheduledEvent>(game).Any(e => e.SkillId == Extra && e.Started) &&
                    HasYuxu(game) == permanent,
                "The fixed fixture starts a real pre-preparation Play and preserves its printed skill selection.");
            var printedInstance = permanent ? StateOfDebt(game).SkillInstanceId : null;

            PlayDrawTwo(game);
            ReachPlay(game, allowYuxuSkip: permanent);
            Require(!E<ProgramTurnSkillsGrantedEvent>(game).Any(e => e.SkillId == Shijian),
                "The first real card use does not offer or issue a second-use skill grant.");

            PlayDrawTwo(game);
            var paid = new List<(int Owner, int Card)>();
            for (var donorIndex = 0; donorIndex < 2; donorIndex++)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == Shijian &&
                    p.Choices.Any(c => Action(c) == "activate"), allowYuxuSkip: permanent);
                var donor = P(game)!.PlayerSeat;
                Require(donor != Recipient && paid.All(item => item.Owner != donor),
                    "Both second-use offers belong to distinct real foreign donors.");
                WrongActor(game, Recipient);
                Answer(game, c => Action(c) == "activate");
                Reach(game, p => p.SkillPrompt?.SkillId == Shijian && p.Choices.Any(c => c.Cards.Count == 1));
                var cost = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
                Require(P(game) is { IsPrivate: true } && P(game)!.PlayerSeat == donor &&
                        game.CreateSnapshot(donor).Players[donor].Hand.Any(c => c.Id == cost && c.Kind == CardKind.Crossbow),
                    "The donor privately pays its own real hand equipment through the HE cost selector.");
                WrongActor(game, Recipient);
                Answer(game, c => c.Cards.SequenceEqual([cost]));
                Reach(game, p => p.SkillPrompt?.SkillId == Movement);
                PaidChild(game, Shijian, donor, cost, GrantCost);
                Require(E<ProgramTurnSkillsGrantedEvent>(game).Count(e => e.SkillId == Shijian) == donorIndex,
                    "The benefit waits for the exact already-paid movement child.");
                game = Restore(game, registry);
                Continue(game);
                Until(game, () => E<ProgramTurnSkillsGrantedEvent>(game).Count(e => e.SkillId == Shijian) == donorIndex + 1);
                paid.Add((donor, cost));
            }
            ReachPlay(game, allowYuxuSkip: permanent);
            var grants = E<ProgramTurnSkillsGrantedEvent>(game).Where(e => e.SkillId == Shijian).ToArray();
            Require(grants.Length == 2 && grants.All(e => e.RecipientSeat == Recipient &&
                        e.GrantedSkillIds.SequenceEqual([Yuxu]) && e.TurnExpiry == new SkillGrantTurnExpiry(turn, Recipient)) &&
                    grants.Select(e => e.GrantSourceId).Distinct().Count() == 2 &&
                    grants.All(e => e.GrantSourceId is not null) && HasYuxu(game) &&
                    DrawCount(game) == 0 && !StateOfDebt(game).Value,
                "Distinct donor sources issue one effective Yuxu without replaying the completed second use.");
            var effectiveInstance = StateOfDebt(game).SkillInstanceId;
            Require(!permanent || effectiveInstance == printedInstance,
                "A temporary grant reuses the original permanent runtime instance and phase state.");
            CheckCosts(game, paid);

            PlayDrawTwo(game);
            game = ArmDraw(game, registry, expectedDrawCount: 1);
            Require(StateOfDebt(game).Value, "A real Yuxu draw commits exactly one next-use debt.");
            PlayDrawTwo(game);
            Reach(game, p => p.SkillPrompt?.SkillId == Yuxu && p.Choices.Any(c => c.Cards.Count == 1));
            var debtPrompt = P(game)!;
            Require(debtPrompt.PlayerSeat == Recipient && !debtPrompt.Choices.Any(c =>
                        Action(c) is "activate" or "skip") && !StateOfDebt(game).Value && DrawCount(game) == 1,
                "The next use clears its debt before a mandatory payment and cannot offer another draw or decline.");
            WrongActor(game, paid[0].Owner);
            var debtCard = debtPrompt.Choices.First(c => c.Cards.Count == 1).Cards.Single();
            Answer(game, c => c.Cards.SequenceEqual([debtCard]));
            Reach(game, p => p.SkillPrompt?.SkillId == Movement);
            PaidChild(game, Yuxu, Recipient, debtCard, DebtCost);
            Require(DrawCount(game) == 1 && CountMoves(game, DebtCost) == 1,
                "Two donor grants still yield one mandatory discard, with no concurrent second draw.");
            game = Restore(game, registry);
            Continue(game);
            ReachPlay(game);
            Require(!StateOfDebt(game).Value && CountMoves(game, DebtCost) == 1 && DrawCount(game) == 1,
                "Cold continuation does not repay the mandatory cost or reopen the optional draw.");

            PlayDrawTwo(game);
            game = ArmDraw(game, registry, expectedDrawCount: 2);
            Require(StateOfDebt(game).Value, "The next independent use may arm a new phase debt.");
            Accept(game, new EndPlayPhaseCommand(Recipient, game.Revision, P(game)!.PromptId));
            ReachPlay(game);
            Require(game.State.TurnNumber == turn && E<TurnStartedEvent>(game).Count() == starts &&
                    !E<TurnEndedEvent>(game).Any(e => e.TurnNumber == turn && e.ActorSeat == Recipient) &&
                    !StateOfDebt(game).Value && StateOfDebt(game).SkillInstanceId == effectiveInstance,
                "The subsequent normal Play resets the old debt within the same actual turn and same granted instance.");
            PlayDrawTwo(game);
            game = ArmDraw(game, registry, expectedDrawCount: 3);
            Accept(game, new EndPlayPhaseCommand(Recipient, game.Revision, P(game)!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == Ended);
            var expires = E<ProgramTurnSkillGrantExpiredEvent>(game).Where(e => e.SkillId == Yuxu).ToArray();
            Require(game.State.Phase == TurnPhase.Finished && E<TurnStartedEvent>(game).Count() == starts &&
                    E<TurnEndedEvent>(game).Any(e => e.TurnNumber == turn && e.ActorSeat == Recipient) &&
                    expires.Length == 2 && expires.All(e => e.RecipientSeat == Recipient &&
                        e.TurnExpiry == new SkillGrantTurnExpiry(turn, Recipient) && e.SkillInstanceId == effectiveInstance) &&
                    expires.Select(e => e.SourceId).Order().SequenceEqual(grants.Select(e => e.GrantSourceId!).Order()) &&
                    HasYuxu(game) == permanent,
                "Both exact grants expire after actual TurnEnded and before the next BeginTurn or after-ended observer.");
            if (permanent)
                Require(StateOfDebt(game).SkillInstanceId == printedInstance && !StateOfDebt(game).Value,
                    "Expiry removes temporary sources without deleting or replacing the permanent Yuxu instance.");
            var history = game.Events.Select(e => e.Payload).ToArray();
            var endIndex = Array.FindLastIndex(history, e => e is TurnEndedEvent end && end.TurnNumber == turn && end.ActorSeat == Recipient);
            var observerIndex = Array.FindLastIndex(history, e => e is ProgramBindingStartedEvent started && started.SkillId == Ended);
            Require(endIndex >= 0 && observerIndex > endIndex &&
                    history.Skip(endIndex + 1).Take(observerIndex - endIndex - 1).OfType<ProgramTurnSkillGrantExpiredEvent>().Count() == 2,
                "Expiry facts commit in the actual-end interval before after-ended programs start.");
            CheckCosts(game, paid);
            Require(DrawCount(game) == 3 && CountMoves(game, DebtCost) == 1,
                "The complete multi-source sequence pays two donor costs, draws three times and settles one use debt.");
            game = Restore(game, registry);
            Continue(game);
            game = UseEquipmentAfterActualEnd(game, registry, turn, starts, permanent);
            Continue(game);
            Until(game, () => E<TurnStartedEvent>(game).Count() > starts);
            Require(HasYuxu(game) == permanent && E<ProgramTurnSkillGrantExpiredEvent>(game).Count(e => e.SkillId == Yuxu) == 2,
                "The next turn cannot repeat expiry or remove the preserved permanent skill.");
            _ = Restore(game, registry);
        }
    }

    private static GameEngine UseEquipmentAfterActualEnd(GameEngine game, ContentRegistry registry,
        int turn, int starts, bool permanent)
    {
        var ordinalsBefore = E<CurrentTurnCardUseKindsRecordedEvent>(game).Where(e =>
            e.TurnNumber == turn && e.ActorSeat == Recipient && e.ActualTurnActorUseOrdinal is not null)
            .Select(e => e.ActualTurnActorUseOrdinal).ToArray();
        var donorBindingsBefore = E<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Shijian);
        Reach(game, p => p.SkillPrompt?.SkillId == Ended && p.Choices.Any(c =>
            Action(c) == "select-target" && c.Targets.SequenceEqual([Recipient])));
        Answer(game, c => Action(c) == "select-target" && c.Targets.SequenceEqual([Recipient]));
        var cards = new List<int>();
        for (var index = 1; index <= 2; index++)
        {
            var binding = $"ended-equipment-{index}";
            Reach(game, p => p.SkillPrompt?.SkillId == Ended && p.Choices.Any(c =>
                Action(c) == "select-owned-cards" && c.Parameters.GetValueOrDefault("result-bind") == binding));
            var card = P(game)!.Choices.First(c => Action(c) == "select-owned-cards" &&
                c.Parameters.GetValueOrDefault("result-bind") == binding).Cards.Single();
            Require(!cards.Contains(card) && game.CreateSnapshot(Recipient).Players[Recipient].Hand.Any(c =>
                c.Id == card && c.Kind == CardKind.Crossbow),
                "Each post-ended use selects a distinct real equipment card from the surviving human hand.");
            Answer(game, c => Action(c) == "select-owned-cards" &&
                c.Parameters.GetValueOrDefault("result-bind") == binding && c.Cards.SequenceEqual([card]));
            if (P(game)?.Choices.Any(c => Action(c) == "finish-owned-cards" &&
                c.Parameters.GetValueOrDefault("result-bind") == binding) == true)
                Answer(game, c => Action(c) == "finish-owned-cards" &&
                    c.Parameters.GetValueOrDefault("result-bind") == binding);
            Reach(game, p => p.SkillPrompt?.SkillId == Ended && p.Choices.Any(c => Option(c) == "continue"));
            cards.Add(card);
            var declared = E<CardUseDeclaredEvent>(game).Single(e => e.CardId == card && e.SourceSeat == Recipient);
            var recorded = E<CurrentTurnCardUseKindsRecordedEvent>(game).Single(e => e.OriginFrameId == declared.ResolutionId);
            Require(declared.CardKind == CardKind.Crossbow && recorded.ActorSeat == Recipient &&
                    recorded.ActualTurnActorUseOrdinal is null && game.State.Phase == TurnPhase.Finished &&
                    E<TurnStartedEvent>(game).Count() == starts && HasYuxu(game) == permanent &&
                    E<ProgramTurnSkillsGrantedEvent>(game).Count(e => e.SkillId == Shijian) == 2 &&
                    E<ProgramTurnSkillGrantExpiredEvent>(game).Count(e => e.SkillId == Yuxu) == 2 &&
                    E<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Shijian) == donorBindingsBefore &&
                    CountMoves(game, GrantCost) == 2 && DrawCount(game) == 3 && CountMoves(game, DebtCost) == 1 &&
                    game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(Recipient) &&
                        m.To == CardLocation.Processing && m.Reason.Value == $"skill-program.{Ended}.UseBoundCardByTarget") == 1,
                "A real use after actual TurnEnded gets no ordinal, donor offer, regrant, repeated expiry or expired Yuxu effect.");
            Require(E<CurrentTurnCardUseKindsRecordedEvent>(game).Where(e => e.TurnNumber == turn &&
                        e.ActorSeat == Recipient && e.ActualTurnActorUseOrdinal is not null)
                    .Select(e => e.ActualTurnActorUseOrdinal).SequenceEqual(ordinalsBefore),
                "Post-ended equipment uses cannot restart or increment the ended actor's frozen positive ordinal ledger.");
            game = Restore(game, registry);
            if (index == 1) Continue(game);
        }
        Require(cards.Distinct().Count() == 2, "Both distinct post-ended physical equipment uses completed before the next BeginTurn.");
        return game;
    }

    private static GameEngine ArmDraw(GameEngine game, ContentRegistry registry, int expectedDrawCount)
    {
        Reach(game, p => p.SkillPrompt?.SkillId == Yuxu && p.Choices.Any(c => Action(c) == "activate"));
        var parent = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        var settledBefore = E<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Yuxu && e.BindingId == SettleTrigger);
        Require(parent.Candidates.Count(c => c.SkillId == Yuxu && c.TriggerId == DrawTrigger) == 1 &&
                parent.Candidates.Where(c => c.SkillId == Yuxu).Select(c => c.SkillInstanceId).Distinct().Count() == 1 &&
                parent.Action.ActorSeat == Recipient && parent.Action.EffectiveKind == CardKind.DrawTwo,
            "Two source grants expose one exact draw candidate and one shared effective instance across Yuxu's conditional branches.");
        WrongActor(game, 1);
        Answer(game, c => Action(c) == "activate");
        Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        Require(DrawCount(game) == expectedDrawCount && StateOfDebt(game).Value,
            "The optional draw pays one real card and arms its debt before the gained-card child.");
        game = Restore(game, registry);
        Continue(game);
        ReachPlay(game);
        Require(DrawCount(game) == expectedDrawCount &&
                E<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Yuxu && e.BindingId == SettleTrigger) == settledBefore,
            "The gained-card child returns without repeating its draw or also executing the mandatory settle branch on this use.");
        return game;
    }

    private static void PlayDrawTwo(GameEngine game)
    {
        var action = game.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.DrawTwo &&
            a.ConversionSource?.SkillId == Driver && a.CardId is not null);
        Require(game.CreateSnapshot(Recipient).Players[Recipient].Hand.Any(c => c.Id == action.CardId && c.Kind == CardKind.Crossbow),
            "The shared driver converts an actual owned equipment card, never a manufactured virtual use.");
        Accept(game, new PlayCardCommand(Recipient, action.CardId!.Value, action.TargetSeats,
            game.Revision, P(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
    }

    private static void PaidChild(GameEngine game, string skill, int owner, int card, string reason)
    {
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == skill && f.OwnerSeat == owner);
        var child = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>()
            .Single(w => w.Batch.Movements.Any(m => m.CardId == card && m.Reason.Value == reason));
        Require(root.PendingMovementContinuation?.SubjectSeat == owner && child.Batch.ParentFrameId == root.Id &&
                child.Batch.Movements is [var move] && move.From == CardLocation.Hand(owner) &&
                move.To == CardLocation.DiscardPile &&
                game.CardMovements.Count(m => m.CardId == card && m.Reason.Value == reason) == 1,
            "The owning program retains its exact committed physical cost and typed movement child.");
    }
    private static void CheckCosts(GameEngine game, IReadOnlyList<(int Owner, int Card)> paid) => Require(
        CountMoves(game, GrantCost) == 2 && paid.All(cost => game.CardMovements.Count(m =>
            m.CardId == cost.Card && m.From == CardLocation.Hand(cost.Owner) && m.To == CardLocation.DiscardPile && m.Reason.Value == GrantCost) == 1),
        "Each donor pays its one captured HE cost exactly once, including after cold replay.");
    private static bool HasYuxu(GameEngine game) => game.CreateSnapshot(Recipient).Players[Recipient].Skills!.Any(s => s.ContentId == Yuxu);
    private static ProgramBooleanStateSnapshot StateOfDebt(GameEngine game) => game.CreateSnapshot(Recipient)
        .Players[Recipient].SkillRuntimeStates!.Single(s => s.SkillId == Yuxu).BooleanStates!.Single(s => s.StateId == Debt);
    private static int DrawCount(GameEngine game) => CountMoves(game, YuxuDraw);
    private static int CountMoves(GameEngine game, string reason) => game.CardMovements.Count(m => m.Reason.Value == reason);
    private static IEnumerable<T> E<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option-id");
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Continue(GameEngine game) => Answer(game, c => Option(c) == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var p = P(game) ?? throw new InvalidOperationException("The expected real temporary-skill prompt is absent.");
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision));
    }
    private static void WrongActor(GameEngine game, int wrongSeat)
    {
        var p = P(game)!;
        var before = State(game);
        Require(p.PlayerSeat != wrongSeat && !game.Submit(new AnswerPromptCommand(wrongSeat,
            p.PromptId, p.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A wrong actor cannot answer, discard, issue a grant or advance another participant's exact prompt.");
    }
    private static void ReachPlay(GameEngine game, bool allowYuxuSkip = false) => Reach(game,
        p => p is { PlayerSeat: Recipient, Kind: DecisionKind.PlayCard }, allowYuxuSkip);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate, bool allowYuxuSkip = false)
    {
        for (var step = 0; step < 160; step++)
        {
            if (P(game) is { } p && predicate(p)) return;
            Step(game, allowYuxuSkip);
        }
        throw new InvalidOperationException("The fixed temporary-skill fixture did not reach its boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Until(GameEngine game, Func<bool> completed)
    {
        for (var step = 0; step < 160; step++)
        {
            if (completed()) return;
            Step(game, allowYuxuSkip: false);
        }
        throw new InvalidOperationException("The temporary-skill continuation exceeded its bounded command budget: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Step(GameEngine game, bool allowYuxuSkip)
    {
        var p = P(game);
        if (p is { PlayerSeat: Recipient, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(Recipient, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: Recipient, Kind: DecisionKind.Nullification } &&
                 p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else if (allowYuxuSkip && p is { PlayerSeat: Recipient } && p.SkillPrompt?.SkillId == Yuxu &&
                 p.Choices.Any(c => Action(c) == "skip"))
            Answer(game, c => Action(c) == "skip");
        else
        {
            Require(p is not { PlayerSeat: Recipient }, "Unexpected human prompt in the temporary-skill driver: " + JsonSerializer.Serialize(p));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real temporary-skill command was rejected.");
    }
    private static GameEngine Restore(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Four views, typed frames, actual movement receipts, history and commands cold-restore exactly.");
        return restored;
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool permanent) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("actual-turn-temporary-skill-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Read(string suffix)
            {
                using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-xu-jing." + suffix)
                    ?? throw new InvalidOperationException("The actual embedded ordinary Xu Jing program is missing.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            var actual = SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json"));
            foreach (var id in new[] { Yuxu, Shijian })
                builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                {
                    Program = actual.Programs[id], ProgramPresentation = actual.Presentations[id],
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000d)
                });
            var fixture = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"real-equipment-draw-two","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}]},
                 {"id":"{{Extra}}","revision":1,"triggers":[{"id":"before-normal-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
                 {"id":"{{Movement}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{GrantCost}}","{{DebtCost}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{YuxuDraw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Ended}}","revision":1,"triggers":[{"id":"after-actual-end","window":"afterTurnEnded","subject":"owner","optional":false,"effects":[
                  {"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},
                  {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
                  {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"cardKinds":["crossbow"],"resultBind":"ended-equipment-1"},
                  {"op":"revealBoundCards","target":"owner","sourceBind":"ended-equipment-1"},
                  {"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"ended-equipment-1"},
                  {"op":"chooseOption","target":"owner","resultBind":"ended-first","options":[{"id":"continue"}]},
                  {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"cardKinds":["crossbow"],"resultBind":"ended-equipment-2"},
                  {"op":"revealBoundCards","target":"owner","sourceBind":"ended-equipment-2"},
                  {"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"ended-equipment-2"},
                  {"op":"chooseOption","target":"owner","resultBind":"ended-second","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Extra, Movement, Gain, Ended }.ToDictionary(id => id,
                        id => new { name = id, description = "真实授技共享机制", optionLabels =
                            id is Movement or Gain or Ended
                                ? new Dictionary<string, string> { ["continue"] = "继续" }
                                : new Dictionary<string, string>() })
                }));
            foreach (var (id, program) in fixture.Programs)
                builder.AddSkill(new(id, id, "实际第二次使用、付款子窗和回合结束")
                {
                    Program = program, ProgramPresentation = fixture.Presentations[id],
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role,
                        role => id == Driver ? role == Role.Lord ? 10000d : -10000d : 0d)
                });
            const string quiet = "fixture:temporary-skill-quiet";
            builder.AddSkill(new(quiet, "固定旁观者", "无主动规则")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -1000d) });
            var ownerSkills = new List<string> { Extra, Movement, Gain, Ended };
            if (permanent) ownerSkills.Add(Yuxu);
            builder.AddGeneral(new(OwnerGeneral, "共享授技受益者", "supporter", Driver, "wei", 12, ownerSkills));
            for (var index = 1; index <= 2; index++)
                builder.AddGeneral(new($"fixture:temporary-skill-donor-{index}", "实际实荐来源", "supporter", Shijian, "shu", 12, [Movement]));
            builder.AddGeneral(new("fixture:temporary-skill-target", "固定旁观者", "supporter", quiet, "wei", 12));
            builder.AddDeck(new("fixture:temporary-skill-deck", "固定小装备实体牌堆", 8, 0,
                [new("standard:crossbow", 64)]));
            builder.AddMode(new(Mode, "实际回合临时授技", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:temporary-skill-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [OwnerGeneral, "fixture:temporary-skill-donor-1", "fixture:temporary-skill-donor-2", "fixture:temporary-skill-target"]));
        }
    }
}
