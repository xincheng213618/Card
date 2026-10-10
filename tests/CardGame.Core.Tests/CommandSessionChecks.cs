using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CommandSessionChecks
{
    public static void EventCollectionObserversCannotRewriteCommittedHistory()
    {
        // Reuse the existing real conversion fixture and its verified seed 17;
        // no new character definition, deck, or seed search is needed here.
        var factory = typeof(Fame2016TaoluanChecks).GetMethod("Create",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var (game, registry) = ((GameEngine, ContentRegistry))factory.Invoke(null,
            [false, false, false, false])!;
        var first = new List<string>();
        var second = new List<string>();
        var attempts = 0;
        var rejected = 0;
        var revealed = 0;
        var initialEventCount = game.Events.Count;
        game.EventCommitted += item =>
        {
            first.Add(SerializeEvent(item));
            if (item.Payload is CardsRevealedEvent) revealed++;
            AttemptCollectionMutation(item.Payload,
                new HashSet<object>(ReferenceEqualityComparer.Instance), ref attempts, ref rejected);
        };
        game.EventCommitted += item => second.Add(SerializeEvent(item));

        CommandResult? lastResult = null;
        void Submit(GameCommand command)
        {
            lastResult = game.Submit(command);
            Require(lastResult.Accepted, lastResult.Error?.Message ?? "The real reveal command was rejected.");
        }
        var action = game.GetHumanLegalActions().First(item =>
            item.ConversionSource?.SkillId == "classic:taoluan" && item.PlayedCardKind == CardKind.FiveGrains);
        Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources
        });
        for (var step = 0; step < 80 && revealed == 0; step++)
        {
            if (game.PendingDecision is { } prompt)
            {
                Require(prompt.Kind == DecisionKind.Nullification,
                    "The verified Five Grains fixture must only pause for Nullification before revealing.");
                var pass = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "pass");
                Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, pass.Id, game.Revision));
            }
            else Submit(new AdvanceOneStepCommand(game.Revision));
        }
        Require(revealed == 1 && attempts > 0 && rejected == attempts,
            "The real reveal must publish collections whose every attempted write is rejected.");
        Require(game.ObserverFailures.Count == 0 && first.SequenceEqual(second) &&
                first.SequenceEqual(game.Events.Skip(initialEventCount).Select(SerializeEvent)),
            "A first observer must not rewrite committed history or the next observer's payload.");
        Require(lastResult is not null &&
                SnapshotJson.Serialize(lastResult.State) == SnapshotJson.Serialize(game.State),
            "Event delivery must leave the prepared command result identical to the committed state.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(game.Events.Select(SerializeEvent).SequenceEqual(restored.Events.Select(SerializeEvent)) &&
                Enumerable.Range(0, game.PlayerCount).All(seat =>
                    SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))),
            "Accepted-command replay must reproduce the exact event bytes and every viewer after mutation attempts.");

        // Nested policy/deposit collections must be detached as well as read-only.
        var kinds = new[] { CardKind.Slash };
        var actions = new[] { CardActionType.Use };
        var suits = new[] { Suit.Spade };
        var ids = new[] { 1, 2 };
        var skillIds = new[] { "fixture:original" };
        var armIds = new[] { 7L, 8L };
        IGameEvent[] nested =
        [
            new CardActionProhibitionGrantedEvent(new TurnCardActionProhibition(1, 1, 0, 1, 0,
                new CardUseEffectSource("fixture", "binding", 0, "instance"), kinds, actions) { Suits = suits }),
            new DeferredPublicPileDepositedEvent(new DeferredPublicPileDeposit(1, 0, 1, "fixture", "instance", ids, 1)),
            new ProgramJiezhenConvertedEvent(1, "fixture", "binding", 0, 1, skillIds),
            new ProgramJiezhenRestoredEvent(1, "fixture", "binding", 0, 1, skillIds, 2),
            new ProgramDaoshuEvent(1, "fixture", "binding", 0, 1, Suit.Spade, true, false, 2, ids),
            new ProgramChangjiEndingEvent(1, "fixture", "binding", 0, 1, "discard", ids, 0),
            new ProgramZhuihuanResolvedEvent(1, "fixture", "binding", 0, 1, armIds, ids, ids, 2),
            new ProgramZhanyiCategoryChosenEvent(1, "fixture", "binding", 0, "trick", ids),
            new ProgramLuochongResolvedEvent(1, "fixture", "binding", 0, 1, "discardTwo", 1, 0, 0, ids, 0),
            new ProgramBijingPunishEvent(1, "fixture", 0, 1, ids, ids),
            new ProgramTongxieArmedEvent(1, "fixture", "binding", 0, 1, ids),
            new ProgramYuanziDamageDrawEvent(1, "fixture", "binding", 0, 1, 2, ids),
            new ProgramLiejieSourceDiscardEvent(1, "fixture", "binding", 0, 1, 2, ids),
            new ProgramTongxieFollowUpResolvedEvent(1, "fixture", "binding", 0, 1, 2, ids, ids, ids)
        ];
        var bytes = nested.Select(item => JsonSerializer.Serialize(item, item.GetType())).ToArray();
        var frozen = nested.Select(CommittedEventProjection.Freeze).ToArray();
        kinds[0] = CardKind.Dodge;
        actions[0] = CardActionType.Response;
        suits[0] = Suit.Club;
        ids[0] = -1;
        skillIds[0] = "fixture:rewritten";
        armIds[0] = -1;
        foreach (var item in frozen)
            AttemptCollectionMutation(item, new HashSet<object>(ReferenceEqualityComparer.Instance), ref attempts, ref rejected);
        Require(rejected == attempts && bytes.SequenceEqual(frozen.Select(item => JsonSerializer.Serialize(item, item.GetType()))),
            "Nested committed collections must preserve their concrete type, values and serialized shape after source or observer writes.");
    }

    private static string SerializeEvent(EventEnvelope item) => JsonSerializer.Serialize(new
    {
        item.Id, item.ParentId, item.Sequence, item.Revision, item.CorrelationId,
        PayloadType = item.Payload.GetType().FullName,
        Payload = JsonSerializer.Serialize(item.Payload, item.Payload.GetType())
    });

    public static void ProjectionFailurePreservesPriorCommit()
    {
        var registry = StandardContentRegistry.Create();
        var game = Create(registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Setup must commit.");
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var revision = game.Revision;
        var events = game.Events.Count;
        var observed = 0;
        game.EventCommitted += _ => observed++;
        game.StateChanged += _ => observed++;
        // Exercise the real exclusive/commit pipeline with real domain advance.
        // The projection dependency fails after invariants and decision refresh.
        InvokePipeline(game, new AdvanceOneStepCommand(revision), "AdvanceOneStepCore",
            () => throw new InvalidOperationException("forced preparation projection failure"),
            snapshot => InvokePrivate(game, "FlushNotifications", snapshot));
        Require(game.IsFaulted && game.Revision == revision &&
                game.AcceptedCommands.Count == revision && game.Events.Count == events && observed == 0,
            "Preparation failure must expose neither a command, revision, event nor notification.");
        Require(game.LastTrustedCheckpoint is { } recovery &&
                GameCheckpointJson.Serialize(recovery) == before,
            "Preparation failure must preserve exactly the preceding accepted prefix.");
        var restored = GameReplay.Restore(game.LastTrustedCheckpoint!, registry);
        Require(GameCheckpointJson.Serialize(restored.CreateCheckpoint()) == before,
            "The preceding prefix must still restore without replaying the failed advance.");
    }

    public static void DeliveryFailurePreservesVisibleCommit()
    {
        var game = Create();
        var eventsSeen = 0;
        game.EventCommitted += _ =>
        {
            eventsSeen++;
            Require(game.Revision == 1 && game.AcceptedCommands.Count == 1,
                "Observers must see the already committed command and revision.");
        };
        InvokePipeline(game, new StartGameCommand(), "StartCore", () => game.State, snapshot =>
        {
            InvokePrivate(game, "FlushNotifications", snapshot);
            throw new InvalidOperationException("forced post-commit delivery failure");
        });
        Require(eventsSeen > 0 && !game.IsFaulted && game.Revision == 1 &&
                game.AcceptedCommands.Count == 1 && game.Events.Count > 0,
            "Output failure must preserve an observable commit and must not become a rules fault.");
        var checkpoint = game.CreateCheckpoint();
        var restored = GameReplay.Restore(checkpoint, StandardContentRegistry.Create());
        Require(SnapshotJson.Serialize(restored.State) == SnapshotJson.Serialize(game.State),
            "The command preserved after delivery failure must restore the committed state.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
            "A delivery-only failure must release exclusivity and leave the committed session usable.");
    }

    public static void ObserverExceptionPreservesCommitAndResult()
    {
        var game = Create();
        var notified = 0;
        game.EventCommitted += _ => throw new InvalidOperationException("observer fixture failure");
        game.EventCommitted += _ => notified++;
        GameSnapshot? published = null;
        game.StateChanged += state => published = state;
        var result = game.Submit(new StartGameCommand());
        Require(result.Accepted && notified > 0 && game.ObserverFailures.Count > 0 &&
                !game.IsFaulted && game.AcceptedCommands.Count == game.Revision,
            "Observer exceptions must be contained while later observers and the result succeed.");
        Require(published is not null &&
                SnapshotJson.Serialize(result.State) == SnapshotJson.Serialize(published) &&
                result.Revision == published.Revision &&
                (result.PendingDecision is null || result.PendingDecision.Revision == result.Revision),
            "The prepared result, state notification and decision must share one revision.");
    }

    public static void ObserverMutationCannotChangePreparedSnapshot()
    {
        var registry = StandardContentRegistry.Create();
        var game = Create(registry);
        string? beforeDelivery = null;
        string? afterDelivery = null;
        var attempts = 0;
        var rejected = 0;
        game.StateChanged += snapshot =>
        {
            beforeDelivery = SnapshotJson.Serialize(snapshot);
            AttemptCollectionMutation(snapshot, new HashSet<object>(ReferenceEqualityComparer.Instance),
                ref attempts, ref rejected);
        };
        // A later observer must receive the same unmodified prepared projection.
        game.StateChanged += snapshot => afterDelivery = SnapshotJson.Serialize(snapshot);
        var result = game.Submit(new StartGameCommand());
        Require(result.Accepted && beforeDelivery is not null && attempts > 2 && rejected == attempts,
            "Every attempted mutation of a published collection must be rejected.");
        Require(beforeDelivery == afterDelivery && beforeDelivery == SnapshotJson.Serialize(result.State) &&
                beforeDelivery == SnapshotJson.Serialize(game.State),
            "The first observer must not change later delivery, the prepared result or engine projection.");
        Require(game.ObserverFailures.Count == 0,
            "The mutation fixture must catch rejection itself so every observer runs normally.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(beforeDelivery == SnapshotJson.Serialize(restored.State),
            "Published output and restored committed state must remain identical after mutation attempts.");
        // Exercise populated decision payloads even when this seed's setup has no prompt.
        // Freeze a detached projection; the live engine and content remain untouched.
        var prompt = new PendingDecision(DecisionKind.ProgramTrigger, 0, "fixture", new[] { 1 }, new[] { 1 })
        {
            ValidContentIds = new[] { "fixture" },
            Choices = new[] { new PromptChoice(new ChoiceId("fixture"), "fixture", new[] { 1 }, new[] { 1 },
                new Dictionary<string, string> { ["fixture"] = "value" }) { ContentIds = new[] { "fixture" } } }
        };
        var detached = game.State with { PendingDecision = prompt };
        var detachedJson = SnapshotJson.Serialize(detached);
        var frozen = (GameSnapshot)typeof(GameEngine).GetMethod("FreezePlayerView",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [detached])!;
        AttemptCollectionMutation(frozen, new HashSet<object>(ReferenceEqualityComparer.Instance), ref attempts, ref rejected);
        Require(attempts == rejected && SnapshotJson.Serialize(frozen) == detachedJson &&
                SnapshotJson.Serialize(detached) == detachedJson,
            "Populated decision lists and choice parameters must be frozen without modifying their detached source.");
        var pendingField = typeof(GameEngine).GetField("_pendingDecisionBacking", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var originalPending = pendingField.GetValue(game);
        try
        {
            pendingField.SetValue(game, prompt);
            var exposed = game.PendingDecision!;
            var exposedJson = System.Text.Json.JsonSerializer.Serialize(exposed);
            Require(exposedJson == System.Text.Json.JsonSerializer.Serialize(game.CreateSnapshot(0).PendingDecision),
                "The decision-only read must match the full human view.");
            AttemptCollectionMutation(exposed, new HashSet<object>(ReferenceEqualityComparer.Instance), ref attempts, ref rejected);
            Require(attempts == rejected && exposedJson == System.Text.Json.JsonSerializer.Serialize(game.PendingDecision),
                "The decision-only read must detach and freeze every exposed nested collection.");
            pendingField.SetValue(game, prompt with { PlayerSeat = 1 });
            Require(game.PendingDecision is null && game.CreateSnapshot(1).PendingDecision is not null,
                "Reading the human decision must not disclose another player's private prompt.");
        }
        finally { pendingField.SetValue(game, originalPending); }
        if (result.PendingDecision is { } decision)
        {
            var beforeDecision = System.Text.Json.JsonSerializer.Serialize(decision);
            AttemptCollectionMutation(decision, new HashSet<object>(ReferenceEqualityComparer.Instance),
                ref attempts, ref rejected);
            Require(rejected == attempts && beforeDecision == System.Text.Json.JsonSerializer.Serialize(result.PendingDecision),
                "The separately exposed result decision must also reject nested collection mutation.");
        }
    }

    private static void AttemptCollectionMutation(object? value, HashSet<object> visited,
        ref int attempts, ref int rejected)
    {
        if (value is null || value is string || value.GetType().IsValueType || !visited.Add(value)) return;
        if (value is System.Collections.IDictionary dictionary)
        {
            if (dictionary.Count > 0)
            {
                var enumerator = dictionary.GetEnumerator();
                enumerator.MoveNext();
                var entry = enumerator.Entry;
                attempts++;
                try { dictionary[entry.Key] = entry.Value; }
                catch (NotSupportedException) { rejected++; }
            }
            foreach (var item in dictionary.Values)
                AttemptCollectionMutation(item, visited, ref attempts, ref rejected);
            return;
        }
        if (value is System.Collections.IList list)
        {
            if (list.Count == 0)
            {
                attempts++;
                try { list.Add(null); }
                catch (NotSupportedException) { rejected++; }
            }
            if (list.Count > 0)
            {
                var replacement = list[0] switch
                {
                    PlayerSnapshot player => player with { Hp = 999 },
                    CardSnapshot card => card with { Id = -1 },
                    _ => list[0]
                };
                attempts++;
                try { list[0] = replacement; }
                catch (NotSupportedException) { rejected++; }
            }
            foreach (var item in list)
                AttemptCollectionMutation(item, visited, ref attempts, ref rejected);
            return;
        }
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0))
            AttemptCollectionMutation(property.GetValue(value), visited, ref attempts, ref rejected);
    }

    private static void InvokePipeline(GameEngine game, GameCommand input, string operationName,
        Func<GameSnapshot> project, Action<GameSnapshot> deliver)
    {
        var awaiting = typeof(GameEngine).GetProperty("_commandAwaitingCommit",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        awaiting.SetValue(game, input);
        try
        {
            var operation = new Action(() => InvokePrivate(game, operationName));
            var method = typeof(GameEngine).GetMethod("ExecuteCommandOperation",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            try { method.Invoke(game, [operation, project, deliver]); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException failure &&
                failure.Message.StartsWith("forced ", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("The forced pipeline failure did not reach its expected boundary.");
        }
        finally { awaiting.SetValue(game, null); }
    }

    private static object? InvokePrivate(GameEngine game, string name, params object?[] args) =>
        typeof(GameEngine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, args);

    private static GameEngine Create(ContentRegistry? registry = null) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 337, HumanSeat = 0, HumanRole = Role.Lord,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 100
        }, registry ?? StandardContentRegistry.Create());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
