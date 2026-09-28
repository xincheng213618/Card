using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramOwnedCardsChecks
{
    private const string SkillId = "fixture:owned-card-set";
    private const string ModeId = "identity:classic-owned-cards-4";

    public static void DefinitionsAndPublicAi()
    {
        var current = Load(2);
        Require(current.RuntimeVersion == "skill-program-v62" && current.MinimumRulesVersion == 171,
            "Owned-card selection must declare its schema/rules boundary.");
        Reject(Rules(2).Replace("\"amount\":2", "\"amount\":0"), "between");
        Reject(Rules(2).Replace("[\"hand\",\"equipment\"]", "[\"discardPile\"]"), "owned");
        Reject(Rules(2).Replace("\"resultBind\":\"chosen\"", "\"resultBind\":\"missing\""), "unknown");
        Reject(Rules(2).Replace("\"amount\":2", "\"amount\":2,\"numberExpression\":\"ownerLostHp\""), "constant");
        var doubled = Rules(2).Replace(Move, Move + "," + Move);
        Reject(doubled, "more than once");
        var owner = new PlayerSkillContext(0, 1, 4, 4, TurnPhase.Play, IsOwnTurn: true);
        var ownerEstimate = ProgramCompositionAi.Estimate(current.Activations.Single().Effects, owner);
        var targetEstimate = ProgramCompositionAi.Estimate(Load(2, other: true).Activations.Single().Effects, owner,
            publicContext: new ProgramAiPublicContext(0, SelectedTarget: new(1, 4, 4, 4, TurnPhase.Play)));
        Require(ownerEstimate.Score == -16 && targetEstimate.Score == 0 &&
                targetEstimate.Hint.TargetValueAdjustment == -14 && targetEstimate.Hint.OwnerDraw == 0,
            "The shared estimator must attribute consumed cards to their actual holder, not the skill owner.");
    }

    public static void PrivateDraftBatchMovementAndReplay()
    {
        var (game, registry) = Create(2);
        var play = game.PendingDecision!;
        var equipment = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, [], game.Revision, play.PromptId)));
        for (var step = 0; step < 32 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        StartSelection(game);
        var first = game.PendingDecision!;
        var before = game.CardMovements.Count;
        var beforeSnapshot = State(game);
        Require(first is { IsPrivate: true, PlayerSeat: 0 } && game.CreateSnapshot(1).PendingDecision is null,
            "Only the card holder may see the exact candidate identities.");
        var wrong = game.Submit(new AnswerPromptCommand(1, first.PromptId, first.Choices[0].Id, game.Revision));
        var forged = game.Submit(new AnswerPromptCommand(0, first.PromptId, new ChoiceId("forged"), game.Revision));
        Require(!wrong.Accepted && !forged.Accepted && State(game) == beforeSnapshot && game.CardMovements.Count == before,
            "Wrong responders and forged choices must reject atomically.");
        Answer(game, first.Choices.Single(choice => choice.Cards.Contains(equipment.CardId.Value)));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        Require(frame.OwnedCardSelection is { SelectedCardIds.Count: 1 } && game.CardMovements.Count == before &&
                game.PendingDecision!.Choices.All(choice => !choice.Cards.Contains(equipment.CardId.Value)),
            "The first selection must not move cards or allow selecting the same physical card twice.");
        var json = JsonSerializer.Serialize<ResolutionFrame>(frame);
        Require(JsonSerializer.Deserialize<ResolutionFrame>(json) is ProgramSkillFrame
                { OwnedCardSelection.SelectedCardIds.Count: 1 }, "The partial draft must remain typed serializable data.");
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(replay) == State(game) &&
                JsonSerializer.Serialize(replay.ResolutionStack.OfType<ProgramSkillFrame>().Single().OwnedCardSelection) ==
                JsonSerializer.Serialize(frame.OwnedCardSelection), "A partial private draft must rebuild from its command prefix.");
        var second = game.PendingDecision!.Choices[0];
        Answer(game, second);
        Answer(replay, replay.PendingDecision!.Choices.Single(choice => choice.Id == second.Id));
        var moved = game.CardMovements.Skip(before).Where(move => move.Reason.Value == $"skill-program.{SkillId}.MoveBoundCards").ToArray();
        Require(moved.Select(move => move.CardId).ToHashSet().SetEquals([equipment.CardId.Value, second.Cards[0]]) &&
                moved.All(move => move.To == CardLocation.DiscardPile) && moved.Length == 2 &&
                State(replay) == State(game) && replay.CardMovements.SequenceEqual(game.CardMovements),
            "The selected set must move exactly once and replay before its equipment-loss trigger.");
        for (var step = 0; step < 32 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:xiaoji"; step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        Require(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:xiaoji" },
            "Discarding selected equipment must retain the shared equipment-loss trigger.");
        var skip = game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Answer(game, skip);
        Answer(replay, replay.PendingDecision!.Choices.Single(choice => choice.Id == skip.Id));
        for (var step = 0; step < 32 && !game.Events.Any(item =>
                 item.Payload is ProgramSkillResolvedEvent { SkillId: SkillId, Completed: true }); step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        Require(game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent
                    { SkillId: SkillId, Completed: true }) == 1 &&
                State(replay) == State(game) && replay.CardMovements.SequenceEqual(game.CardMovements),
            "The suspended equipment-loss trigger must finish the owned-card program once and replay exactly.");
    }

    public static void ShortfallEmptyAndInvalidatedDraft()
    {
        var (shortfall, _) = Create(20);
        var available = shortfall.CreateSnapshot(0).Players[0].HandCount;
        StartSelection(shortfall);
        Require(shortfall.ResolutionStack.OfType<ProgramSkillFrame>().Single().OwnedCardSelection!.RequiredCount == available,
            "An insufficient zone must select all available cards, not demand nonexistent cards.");
        for (var index = 0; index < available; index++) Answer(shortfall, shortfall.PendingDecision!.Choices[0]);
        Require(shortfall.CreateSnapshot(0).Players[0].HandCount == 0,
            "All available cards must be consumed on the shortfall branch.");
        var (empty, _) = Create(2, equipmentOnly: true);
        StartSelection(empty);
        Require(empty.Events.Any(item => item.Payload is ProgramSkillResolvedEvent { SkillId: SkillId, Completed: true }) &&
                !empty.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
            "An empty source must produce an empty binding and finish without a zero-choice prompt.");

        foreach (var invalidateSource in new[] { false, true })
        {
            var (game, _) = Create(2);
            StartSelection(game);
            Answer(game, game.PendingDecision!.Choices[0]);
            var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
            var before = game.CardMovements.Count;
            if (invalidateSource)
            {
                var zones = typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
                zones.GetType().GetMethod("Move")!.Invoke(zones,
                    [frame.OwnedCardSelection!.SelectedCardIds[0], CardLocation.Hand(0), CardLocation.DiscardPile]);
            }
            else
            {
                var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
                    .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
                foreach (var grant in players[0].SkillGrants.Grants.Where(grant => grant.SkillId == SkillId).ToArray())
                    players[0].SkillGrants.SetEnabled(grant.GrantId, false);
            }
            Answer(game, game.PendingDecision!.Choices[0]);
            Require(game.CardMovements.Count == before &&
                    game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent { SkillId: SkillId, Completed: false }) == 1 &&
                    !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
                "Stale exact instances and source locations must cancel once without paying the uncommitted draft.");
        }
    }

    private const string Move = """{"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"discardPile"}""";
    private static string Rules(int count, bool other = false, bool equipmentOnly = false) =>
        $$"""{"schemaVersion":62,"skills":[{"id":"{{SkillId}}","revision":1,"activations":[{"id":"select","minCards":0,"maxCards":0,"minTargets":{{(other ? 1 : 0)}},"maxTargets":{{(other ? 1 : 0)}},"targetKind":"otherLiving","usesPerTurn":1,"condition":{"kind":"always"},"effects":[{"op":"selectOwnedCards","target":"{{(other ? "selectedTarget" : "owner")}}","amount":{{count}},"zones":{{(equipmentOnly ? "[\"equipment\"]" : "[\"hand\",\"equipment\"]")}},"resultBind":"chosen"},{{Move}}]}]}]}""";
    private static string Presentation => JsonSerializer.Serialize(new
    {
        schemaVersion = 3,
        skills = new Dictionary<string, object> { [SkillId] = new { name = "区域牌集合", description = "选择自己的牌，再统一移动。" } }
    });
    private static SkillProgram Load(int count, bool other = false) => SkillProgramCatalog.Load(Rules(count, other), Presentation).Programs[SkillId];
    private static void Reject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected definition rejection: {expected}");
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(int count, bool equipmentOnly = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(count, equipmentOnly));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:owned-cards-owner", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture failed to reach Play.");
        return (game, registry);
    }

    private static void StartSelection(GameEngine game) => Accept(game.Submit(new UseProgramSkillCommand(
        0, SkillId, "select", [], [], game.Revision, game.PendingDecision!.PromptId)));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(
        0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected fixture command.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(int count, bool equipmentOnly) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-owned-cards", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules(count, equipmentOnly: equipmentOnly), Presentation);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "区域牌集合", "选择自己的牌，再统一移动。")
            { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:owned-cards-owner", "牌主", "supporter", SkillId,
                "wu", BaseHp: 4, AdditionalSkillIds: ["classic:xiaoji"]));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:owned-cards-{index}", $"目标{index}", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:owned-cards-deck", "区域牌测试", 4, 0,
                [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "区域牌集合测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:owned-cards-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:owned-cards-owner", "fixture:owned-cards-1", "fixture:owned-cards-2", "fixture:owned-cards-3"]));
        }
    }
}
