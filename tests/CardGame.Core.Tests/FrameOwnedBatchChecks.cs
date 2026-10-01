using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FrameOwnedBatchChecks
{
    public static void StrategicDamageCompletesAndCancelsWithItsFrame()
    {
        foreach (var cancels in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new Fixture(cancels));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:classic-frame-batch-check", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 10
            }, registry);
            Submit(game, new StartGameCommand());
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
            Submit(game, new SelectGeneralCommand(0, "fixture:batch-owner", game.Revision, game.PendingDecision!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Submit(game, new UseProgramSkillCommand(0, "fixture:batch-damage", "all-others", [], [],
                game.Revision, game.PendingDecision!.PromptId));

            var observedBatch = false;
            for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
            {
                if (game.ResolutionStack.OfType<ProgramSkillFrame>()
                    .SingleOrDefault(frame => frame.SkillId == "fixture:batch-damage") is { StrategicDamageBatch: { } batch })
                {
                    observedBatch = true;
                    Require(batch.InstructionIndex == 2 && batch.RemainingTargetSeats.All(seat => seat != 0),
                        "A nested damage window must preserve only the unprocessed frozen targets on its program.");
                    var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
                        "Checkpoint replay must reconstruct the same parent batch during a child window.");
                }
                if (game.PendingDecision is { } decision)
                {
                    var choice = decision.Kind == DecisionKind.RescueDying
                        ? decision.Choices.First(item => item.Cards.Count == 0)
                        : decision.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "choose-option");
                    Submit(game, new AnswerPromptCommand(decision.PlayerSeat, decision.PromptId, choice.Id, game.Revision));
                }
                else Submit(game, new AdvanceOneStepCommand(game.Revision));
            }
            Require(observedBatch && game.ResolutionStack.Count == 0,
                "The observed batch and its children must retire together on completion or cancellation.");
            var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0).ToArray();
            Require(damage.Length == (cancels ? 1 : 3) && damage.Select(item => item.TargetSeat).Distinct().Count() == damage.Length,
                $"Death-triggered skill loss must cancel the remaining batch; ordinary completion must hit each frozen target once. cancels={cancels}, damage={JsonSerializer.Serialize(damage)}");
            Require(game.CardMovements.Count(move => move.Reason.Value == "skill-program.fixture:batch-damage.Draw") == (cancels ? 0 : 1),
                "A cancelled program must not pay its later draw; a completed program must pay it once.");
            var copy = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(game.Events.Select(EventBytes).SequenceEqual(copy.Events.Select(EventBytes)) &&
                    Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                        SnapshotJson.Serialize(copy.CreateSnapshot(seat))),
                "Completion and nested skill loss must preserve exact event bytes and every viewer on replay.");
        }
    }

    private static string EventBytes(EventEnvelope envelope) => JsonSerializer.Serialize(new
    {
        envelope.Id, envelope.ParentId, envelope.Sequence, envelope.Revision, envelope.CorrelationId,
        PayloadType = envelope.Payload.GetType().FullName,
        Payload = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())
    });

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 30; step++)
        {
            if (game.PendingDecision is { } decision && predicate(decision)) return;
            Submit(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The verified small fixture did not reach its setup/play boundary.");
    }

    private static void Submit(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "The batch fixture command was rejected.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(bool cancels) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-frame-batches", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"fixture:batch-damage","revision":1,"activations":[
                    {"id":"all-others","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                     "targetKind":"anyLiving","usesPerTurn":1,"effects":[
                       {"op":"setMarkerAmount","target":"owner","amount":0,"marker":"junlue"},
                       {"op":"damageOtherLiving","target":"owner","amount":1},
                       {"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:batch-pause","revision":1,"triggers":[
                    {"id":"pause-before-damage","window":"beforeDamageApplied","subject":"damageSource","optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"damage-pause","options":[{"id":"continue"}]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:batch-damage":{"name":"群体结算","description":"逐人伤害后摸牌"},"fixture:batch-pause":{"name":"伤害暂停","description":"伤害前暂停","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new("fixture:batch-damage", "群体结算", "逐人伤害后摸牌")
                { Program = catalog.Programs["fixture:batch-damage"] });
            builder.AddSkill(new("fixture:batch-pause", "伤害暂停", "伤害前暂停")
                { Program = catalog.Programs["fixture:batch-pause"] });
            builder.AddGeneral(new("fixture:batch-owner", "结算持有人", "supporter", "fixture:batch-damage", "shu", 20,
                ["fixture:batch-pause"]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:batch-{seat}", $"结算目标{seat}", "supporter",
                    cancels ? "classic:duanchang" : "standard:none", "wei", cancels ? 1 : 20));
            builder.AddDeck(new("fixture:batch-deck", "小结算牌堆", 8, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-frame-batch-check", "结算帧测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:batch-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:batch-owner", "fixture:batch-1", "fixture:batch-2", "fixture:batch-3"]));
        }
    }
}
