using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramActivationBooleanStateChecks
{
    private const string SkillA = "fixture:activation-boolean-a", SkillB = "fixture:activation-boolean-b";
    private const string StateId = "disabled", Activation = "disable", Owner = "fixture:activation-boolean-owner";
    private const string Mode = "fixture:activation-boolean-mode";

    public static void NestedGatesUseTheCurrentOwnerSkillInstance()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        Require(Has(game, SkillA) && Has(game, SkillB),
            "All visits a false BooleanState; Any visits its false HP branch and then Not of the true-value BooleanState predicate.");

        Use(game, SkillA);
        var first = StateEvents(game).Single();
        Require(first is { OwnerSeat: 0, SkillId: SkillA, StateId: StateId, Value: true } &&
                !Has(game, SkillA) && Has(game, SkillB),
            "The real owner activation disables only its own skill, even when another skill declares the same state ID.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var rejected = game.Submit(new UseProgramSkillCommand(0, SkillA, Activation, [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Require(!rejected.Accepted && before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && StateEvents(game).Count() == 1,
            "An unavailable nested gate rejects before command acceptance, state payment or any visible change.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)) &&
                !Has(restored, SkillA) && Has(restored, SkillB),
            "The command-only prefix restores the same actual state and activation availability.");

        // Direct grant audit follows existing host fixtures. This segment is not a command-replay claim.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", flags)!.GetValue(game)!;
        var owner = players[0];
        var original = owner.SkillGrants.Grants.Single(g => g.SkillId == SkillA);
        var laterInstance = original.SkillInstanceId + ":later";
        owner.SkillGrants.Grant(new("fixture:later-activation-grant", SkillA, laterInstance, "acquired:activation-audit"));
        players[1].SkillGrants.Grant(new("fixture:peer-activation-grant", SkillA, original.SkillInstanceId, "acquired:activation-audit"));
        Require(!Has(game, SkillA) && Has(game, SkillB),
            "Neither a fresh later same-skill instance nor another seat's same instance ID can replace the selected owner's disabled state.");
        owner.SkillGrants.RemoveGrant(original.GrantId);
        Require(Has(game, SkillA) && Has(game, SkillB),
            "Removing the ordinal-first source selects the remaining real grant and its independent initial state.");
        var movementsBefore = game.CardMovements.Count;
        Use(game, SkillA);
        var second = StateEvents(game).Last();
        Require(second is { OwnerSeat: 0, SkillId: SkillA, StateId: StateId, Value: true } &&
                second.SkillInstanceId == laterInstance && !Has(game, SkillA) && Has(game, SkillB) &&
                game.CardMovements.Count == movementsBefore,
            "Execution freezes the same actual instance used by legality; this zero-card state cost produces no extra movement.");
        Use(game, SkillB);
        Require(StateEvents(game).Last() is { OwnerSeat: 0, SkillId: SkillB, StateId: StateId, Value: true } &&
                !Has(game, SkillA) && !Has(game, SkillB),
            "Each skill's real SetBooleanState cost closes only its own activation gate.");

        var evaluate = typeof(GameEngine).GetMethod("EvaluateProgramActivationCondition", flags)!;
        var context = new PlayerSkillContext(0, 4, 4, 4, TurnPhase.Play, IsOwnTurn: true);
        var frameOnly = new SkillProgramCondition(SkillProgramConditionKind.PindianWon, 0, [], sourceBind: "missing-result");
        try
        {
            evaluate.Invoke(game, [frameOnly, context, SkillA, laterInstance]);
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException cause &&
                                                     cause.Message.Contains("requires a running program frame", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("A frame-only activation condition must retain its explicit failure rather than become false or legal.");
    }

    private static IEnumerable<ProgramBooleanStateChangedEvent> StateEvents(GameEngine game) =>
        game.Events.Select(e => e.Payload).OfType<ProgramBooleanStateChangedEvent>().Where(e => e.SkillId is SkillA or SkillB);
    private static bool Has(GameEngine game, string skill) => game.GetHumanLegalActions().Any(a =>
        a.ProgramSkillId == skill && a.ProgramActivationId == Activation);
    private static void Use(GameEngine game, string skill)
    {
        Accept(game, new UseProgramSkillCommand(0, skill, Activation, [], [], game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The small fixed fixture must reach its owner's Play prompt.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "The real fixture command must be accepted.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:activation-boolean", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { SkillA, SkillB })
            {
                var program = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,
                    "states":[{"id":"disabled","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],
                    "activations":[{"id":"disable","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                    "condition":{"kind":"all","children":[{"kind":"booleanState","stateId":"disabled","expectedValue":false},
                        {"kind":"any","children":[{"kind":"hpAtLeast","value":20},
                            {"kind":"not","children":[{"kind":"booleanState","stateId":"disabled","expectedValue":true}]}]}]},
                    "effects":[{"op":"setBooleanState","target":"owner","stateId":"disabled","value":true}]}]}]}
                    """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                    { [id] = new { name = "共享激活状态", description = "嵌套条件与实际实例" } } })).Programs[id];
                builder.AddSkill(new(id, "共享激活状态", "嵌套条件与实际实例") { Program = program });
            }
            builder.AddSkill(new("fixture:activation-idle", "固定候选", "无运行能力"));
            builder.AddGeneral(new(Owner, "共享状态拥有者", "supporter", SkillA, "wei", 4, [SkillB]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:activation-boolean-peer-{seat}", "固定目标", "supporter", "fixture:activation-idle", "wei", 4));
            builder.AddDeck(new("fixture:activation-boolean-deck", "固定牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "共享激活状态", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:activation-boolean-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:activation-boolean-peer-1", "fixture:activation-boolean-peer-2", "fixture:activation-boolean-peer-3"]));
        }
    }
}
