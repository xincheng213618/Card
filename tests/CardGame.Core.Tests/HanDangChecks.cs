using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HanDangChecks
{
    private const int HumanSeat = 0;
    private const string HanDangId = "classic:han-dang";

    public static void ContentAndPackageBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[HanDangId] is { BaseHp: 4, FactionId: "wu", Gender: GeneralGender.Male } hanDang &&
                hanDang.SkillIds.SequenceEqual(["classic:gongqi", "classic:jiefan"]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(HanDangId),
            "Current identity roster must publish complete Han Dang metadata.");
        Require(current.Skills["classic:gongqi"] is
                {
                    LegacyKind: null,
                    Program: { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } program,
                    ActionForms: SkillActionForm.Active,
                    ExecutionForms: SkillExecutionForm.State
                } &&
                program.Activations.Single() is
                {
                    Id: "discard-for-unlimited-range",
                    MinCards: 1,
                    MaxCards: 1,
                    UsesPerTurn: null,
                    UsesPerPhase: 1
                } activation &&
                activation.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                current.Skills["classic:jiefan"] is
                {
                    LegacyKind: null,
                    Program: { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } jiefanProgram,
                    ActionForms: SkillActionForm.Active,
                    Tags: SkillTag.Limited
                } &&
                jiefanProgram.Activations.Single() is
                {
                    Id: "aid-by-attack-range",
                    MinCards: 0,
                    MaxCards: 0,
                    MinTargets: 1,
                    MaxTargets: 1,
                    UsesPerGame: 1,
                    Effects: [{ Op: SkillProgramEffectOp.RequestAttackRangeAid }]
                },
            "Current Gongqi and Jiefan must use their composed programs.");
    }

    public static void GongqiEquipmentCostAndOpaqueDiscardReplay()
    {
        var game = CreateGame();
        ReachHumanPlay(game);
        var cost = Player(game, HumanSeat).Hand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        var beforeRange = game.GetAttackRange(HumanSeat);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:gongqi" &&
            candidate.ProgramActivationId == "discard-for-unlimited-range");
        Require(action.SelectableCardIds.Contains(cost.Id),
            "Gongqi must publish equipment cards held in hand as exact legal costs.");

        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            action.ProgramSkillId!,
            action.ProgramActivationId!,
            [cost.Id],
            [],
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Gongqi equipment cost was rejected.");
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(prompt.IsPrivate && prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" &&
                    choice.Cards.Count == 0 && choice.Targets.Count == 1) &&
                prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline"),
            "Gongqi must expose other hands only as private opaque slots and retain the optional skip.");
        Require(game.GetAttackRange(HumanSeat) == int.MaxValue && beforeRange < int.MaxValue,
            "Gongqi must establish unlimited attack range immediately after paying its cost.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), Registry());
        Require(State(paused) == State(game),
            "A paused Gongqi target-card selection must replay exactly.");
        var selected = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" &&
            choice.Cards.Count == 0);
        Answer(game, selected);
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice => choice.Id == selected.Id));
        ReachHumanPlay(game);
        ReachHumanPlay(paused);

        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
            .Single(item => item.SkillId == "classic:gongqi");
        var targetDiscard = game.CardMovements.Last(move =>
            move.Reason == new CardMoveReason("skill-program.classic:gongqi.ChooseOtherOwnedCardDiscard"));
        Require(resolved.OwnerSeat == HumanSeat && resolved.Completed &&
                game.CardMovements.Any(move => move.CardId == cost.Id &&
                    move.Reason == new CardMoveReason("skill-program.classic:gongqi.MoveBoundCards") &&
                    move.To == CardLocation.DiscardPile) &&
                targetDiscard.To == CardLocation.DiscardPile &&
                targetDiscard.From.OwnerSeat == selected.Targets.Single(),
            "Gongqi must audit both the equipment-category cost and exact optional target discard.");
        Require(game.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != "classic:gongqi") &&
                State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "Gongqi must be once per play phase and complete identically after replay.");
    }

    public static void JiefanFreezesRespondersConsumesLimitedUseAndReplays()
    {
        var game = CreateGame();
        ReachHumanPlay(game);
        var equipmentCards = Player(game, HumanSeat).Hand
            .Where(card => EquipmentCatalog.IsEquipment(card.Kind))
            .ToArray();
        Require(equipmentCards.Length >= 2, "The Han Dang fixture needs two equipment cards.");

        var equip = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Equip && action.CardId == equipmentCards[0].Id);
        var equipped = game.Submit(new PlayCardCommand(
            HumanSeat,
            equip.CardId!.Value,
            equip.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(equipped.Accepted, equipped.Error?.Message ?? "The Jiefan fixture could not equip its weapon.");
        ReachHumanPlay(game);

        var gongqiAction = game.GetHumanLegalActions().Single(candidate =>
            candidate.ProgramSkillId == "classic:gongqi");
        var gongqi = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            gongqiAction.ProgramSkillId!,
            gongqiAction.ProgramActivationId!,
            [equipmentCards[1].Id],
            [],
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(gongqi.Accepted, gongqi.Error?.Message ?? "The Jiefan fixture could not establish Gongqi range.");
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline"));
        ReachHumanPlay(game);

        var targetSeat = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Where(player => player.IsAlive && player.Seat != HumanSeat)
            .OrderByDescending(player => game.GetCombatDistance(HumanSeat, player.Seat))
            .First().Seat;
        Require(game.GetCombatDistance(HumanSeat, targetSeat) > 1 && game.GetAttackRange(HumanSeat) == int.MaxValue,
            "The Jiefan fixture must prove Gongqi adds Han Dang to a formerly out-of-range responder set.");
        var jiefanAction = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:jiefan" &&
            candidate.ProgramActivationId == "aid-by-attack-range");
        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            jiefanAction.ProgramSkillId!,
            jiefanAction.ProgramActivationId!,
            [],
            [targetSeat],
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Jiefan activation was rejected.");
        var started = game.Events.Select(item => item.Payload).OfType<ProgramAttackRangeAidStartedEvent>().Single();
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(started.SkillId == "classic:jiefan" && started.BindingId == "aid-by-attack-range" &&
                started.TargetSeat == targetSeat && started.ResponderSeats.Contains(HumanSeat) &&
                started.ResponderSeats.All(seat => seat != targetSeat) &&
                prompt.PlayerSeat == HumanSeat && prompt.TargetSeat == targetSeat && prompt.IsPrivate &&
                prompt.SkillPrompt is { SkillId: "classic:jiefan", Title: "解烦 · 响应方式" } &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") ==
                                             "attack-range-aid-discard-weapon") &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") ==
                                             "attack-range-aid-draw"),
            "Jiefan must freeze every current attacker except the beneficiary and publish both legal owner branches.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), Registry());
        Require(State(paused) == State(game), "A paused Jiefan responder prompt must replay exactly.");
        var discard = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "attack-range-aid-discard-weapon");
        Answer(game, discard);
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice => choice.Id == discard.Id));
        DrainAiAttackRangeAid(game);
        DrainAiAttackRangeAid(paused);

        var choices = game.Events.Select(item => item.Payload).OfType<ProgramAttackRangeAidChoiceResolvedEvent>().ToArray();
        var usage = game.Events.Select(item => item.Payload).OfType<SkillUsageConsumedEvent>()
            .Single(item => item.SkillId == "classic:jiefan");
        Require(choices.Length == started.ResponderSeats.Count &&
                choices[0].ResponderSeat == HumanSeat &&
                choices[0].DiscardedWeaponCardId == discard.Cards.Single() &&
                choices.Skip(1).All(choice => choice.DiscardedWeaponCardId is null && choice.DrawnCardIds.Count == 1) &&
                game.CardMovements.Any(move => move.CardId == discard.Cards.Single() &&
                    move.Reason == new CardMoveReason("skill-program.classic:jiefan.RequestAttackRangeAid") &&
                    move.To == CardLocation.DiscardPile) &&
                usage is { UsageId: "aid-by-attack-range", Scope: SkillUsageScope.Game, Count: 1 } &&
                game.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:jiefan"),
            "Jiefan must resolve the frozen mandatory sequence, pay a weapon exactly, draw for weaponless responders and stay consumed.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Jiefan response chain must replay exactly.");
    }

    private static void DrainAiAttackRangeAid(GameEngine game)
    {
        for (var step = 0; step < 16 && game.PendingDecision?.Kind == DecisionKind.ProgramTrigger; step++)
        {
            Require(game.PendingDecision.PlayerSeat != HumanSeat,
                "The bounded Jiefan fixture unexpectedly returned to the human responder.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Jiefan chain could not advance.");
        }
        ReachHumanPlay(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Jiefan must return to the owner's play prompt after every frozen responder resolves.");
    }

    private static GameEngine CreateGame()
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Han Dang fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Han Dang fixture has no selection prompt.");
        Require(prompt.Kind == DecisionKind.SelectGeneral && prompt.ValidContentIds.Contains(HanDangId),
            "The Han Dang fixture did not offer Han Dang.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            HanDangId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Han Dang fixture could not select Han Dang.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Han Dang's play phase.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Han Dang fixture could not advance.");
        }
        throw new InvalidOperationException("The Han Dang fixture did not reach human Play.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var answered = game.Submit(new AnswerPromptCommand(
            game.PendingDecision?.PlayerSeat ?? throw new InvalidOperationException("No prompt is pending."),
            game.PendingDecision.PromptId,
            choice.Id,
            game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The Han Dang prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

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
        public const string ModeId = "identity:classic-han-dang-test-4";
        private const string DeckId = "fixture:han-dang-weapons";
        private static readonly string[] TargetIds =
            ["fixture:han-dang-target-1", "fixture:han-dang-target-2", "fixture:han-dang-target-3"];

        public PackageManifest Manifest { get; } = new(
            "han-dang-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 93, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "解烦测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "韩当武器测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "四人经典身份（韩当场景）",
                MinPlayers: 4,
                MaxPlayers: 4,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [HanDangId, .. TargetIds]));
        }
    }
}
