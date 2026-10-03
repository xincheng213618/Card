using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class RecoveryPaidWinnerChecks
{
    private const string Driver = "classic:qixi";
    private const string Preparation = "fixture:paid-winner-preparation";
    private const string HpObserver = "fixture:paid-winner-hp";
    private const string RewardObserver = "fixture:paid-winner-reward";
    private const string SelectionPreference = "fixture:paid-winner-selection";
    private const string ModeId = "identity:classic-paid-recovery-winner";

    public static void SilverLionPaidQixiStopsAfterObserverVictory()
    {
        foreach (var observer in new[] { "hp", "reward" })
        {
            // Every mutation, including the winning death, comes from recorded commands.
            var (game, registry) = Create(observer);
            var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
            Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats,
                game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
            var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion);
            var lord = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
            var target = Enumerable.Range(1, 3).First(seat => seat != lord);
            var targetHp = game.State.Players[target].Hp;
            var targetMovements = game.CardMovements.Count(m => m.From.OwnerSeat == target);
            var ownerHp = game.State.Players[0].Hp;
            var ownerHand = game.State.Players[0].HandCount;
            Require(game.CreateSnapshot(target).Players[target].Hand.Count > 0 &&
                game.CreateSnapshot(target).Players[target].Skills!.Any(s => s.ContentId == "classic:kanpo"),
                "A surviving responder holds black cards and actual Kanpo, so an incorrectly resumed trick can open Nullification.");
            var trick = game.GetHumanLegalActions().First(a => a.CardId == armor.Id &&
                a.PlayedCardKind == CardKind.Dismantlement && a.TargetSeat == target &&
                a.ConversionSource?.SkillId == Driver);
            Accept(game, new PlayCardCommand(0, trick.CardId!.Value, trick.TargetSeats,
                game.Revision, Prompt(game)!.PromptId, trick.PlayedCardKind, trick.TargetCardId)
            { ConversionSource = trick.ConversionSource });
            Reach(game, p => p.Kind == DecisionKind.RecoveryReplacement);
            var paid = game.ResolutionStack.OfType<CardUseFrame>().Single();
            Require(paid.CardKind == CardKind.Dismantlement && paid.RecoveryPaidContinuation?.Kind == RecoveryPaidCardUseKind.Trick &&
                game.State.ProcessingCardCount == 1,
                "Actual Qixi pays the equipment Silver Lion and pauses on its exact paid card-use parent before the Dismantlement effect.");
            Replay(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("recovery-action") == "redirect");
            var childSkill = observer == "hp" ? HpObserver : RewardObserver;
            Reach(game, p => p.SkillPrompt?.SkillId == childSkill);
            var replacement = game.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
            Require(replacement.ParentFrameId == paid.Id &&
                replacement.Stage == (observer == "hp" ? RecoveryReplacementStage.RecoveryApplied : RecoveryReplacementStage.RewardApplied) &&
                game.State.Players[0].HandCount == ownerHand + (observer == "reward" ? 1 : 0),
                "The winning observer belongs to the HP or already-paid reward child of the same recovery attempt.");
            Replay(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            if (observer == "reward")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == RewardObserver &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
                Replay(game, registry);
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                    c.Targets.SequenceEqual([lord]));
            }
            Complete(game);
            Require(!game.State.Players[lord].IsAlive && game.State.Winner == Winner.Rebels &&
                game.Events.Count(e => e.Payload is WinnerDeterminedEvent) == 1 &&
                game.Events.Any(e => e.Payload is PlayerDiedEvent death && death.VictimSeat == lord) &&
                game.Events.Any(e => e.Payload is ProgramSkillHpLostEvent loss && loss.SkillId == childSkill && loss.TargetSeat == lord),
                "The observer's real HP-loss/death child determines victory; no HOST winner or player state is injected.");
            Require(game.State.Status == EngineStatus.Completed && Prompt(game) is null && game.ResolutionStack.Count == 0 &&
                !game.Events.Any(e => e.Payload is NullificationRequestedEvent request && request.ResolutionId == paid.Id) &&
                !game.Events.Any(e => e.Payload is TargetCardSelectionRequestedEvent request && request.ResolutionId == paid.Id) &&
                game.CardMovements.Count(m => m.From.OwnerSeat == target) == targetMovements &&
                game.State.Players[target].Hp == targetHp && game.State.Players[0].Hp == ownerHp,
                "Victory drains the paid Dismantlement without reopening Nullification, selecting or moving a target card, or restoring the replaced owner HP.");
            Require(game.State.ProcessingCardCount == 0 &&
                game.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                game.Events.Count(e => e.Payload is CardUseFinishedEvent finished && finished.ResolutionId == paid.Id) == 1 &&
                game.Events.Count(e => e.Payload is RecoveryReplacementChosenEvent) == 1 &&
                game.State.Players[0].HandCount == ownerHand + (observer == "reward" ? 1 : 0),
                "The original physical payment, recovery choice, reward if already committed, and use cleanup each occur once.");
            Replay(game, registry);
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(string observer)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(observer));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:paid-winner-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
        var lord = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
        for (var step = 0; step < 6 && game.State.Players[lord].Hp > 3; step++)
        {
            Accept(game, new UseProgramSkillCommand(0, Preparation, "lower-other", [], [lord],
                game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
        }
        Require(game.State.Players[lord].Hp <= 3 && game.State.Players[lord].IsAlive,
            "Recorded one-point HP loss prepares the actual lord below the recovering owner's HP after any earlier AI armor replacements.");
        return (game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 160; step++)
        {
            var prompt = Prompt(game);
            if (prompt is not null && predicate(prompt)) return;
            Require(game.State.Winner == Winner.None, "The fixed winner fixture ended before its requested recovery boundary.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed paid-recovery fixture did not reach its boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }

    private static void Complete(GameEngine game)
    {
        for (var step = 0; step < 160; step++)
        {
            if (game.State.Status == EngineStatus.Completed) return;
            var prompt = Prompt(game);
            Require(prompt is null || prompt.Kind == DecisionKind.RescueDying,
                "The winning recovery child must return without a new trick or program response: " + JsonSerializer.Serialize(prompt));
            if (prompt is { PlayerSeat: 0 }) Answer(game, c => c.Cards.Count == 0);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The paid recovery did not drain after the winning observer death.");
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            prompt.Choices.First(predicate).Id, game.Revision));
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    }

    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });

    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "The real paid recovery, winning observer death, projections, exact frames, zones, and history restore from the accepted command prefix.");

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(string observer) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-paid-recovery-winner", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var observerId = observer == "hp" ? HpObserver : RewardObserver;
            var trigger = observer == "hp"
                ? "\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"condition\":{\"kind\":\"not\",\"children\":[{\"kind\":\"ownerIsTurnPlayer\"}]}"
                : "\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.recovery-replacement.reward\"]";
            var target = observer == "hp" ? "owner" : "selectedTarget";
            var select = observer == "hp" ? "" : ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"}";
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Preparation}}","revision":1,"activations":[{"id":"lower-other","minCards":0,"maxCards":0,
                "minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                {"id":"{{observerId}}","revision":1,"triggers":[{"id":"winning-observer",{{trigger}},"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"observed","options":[{"id":"continue"}]}{{select}},
                {"op":"loseHp","target":"{{target}}","amount":20}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = 3,
                    skills = new Dictionary<string, object>
                    {
                        [Preparation] = new { name = "回复准备", description = "正式命令使主公失去一点体力。" },
                        [observerId] = new
                        {
                            name = "胜负观察", description = "真实回复子链中的体力流失与死亡。",
                            optionLabels = new Dictionary<string, string> { ["continue"] = "继续" }
                        }
                    }
                }));
            builder.AddSkill(new(observerId, "胜负观察", "真实死亡子链") { Program = catalog.Programs[observerId] });
            builder.AddSkill(new(Preparation, "回复准备", "真实体力流失") { Program = catalog.Programs[Preparation] });
            // Earlier AI setup seats prefer the three targets and leave the human driver published.
            builder.AddSkill(new(SelectionPreference, "固定候选", "保留人类驱动候选")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:paid-winner-owner", "回复者", "supporter", Driver, "wu", 6,
                observer == "reward" ? [RewardObserver, Preparation] : [Preparation]) { InitialHp = 4 });
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:paid-winner-target-{index}", "救援候选", "supporter", SelectionPreference, "wu", 6,
                    observer == "hp" ? ["boundary:jiuyuan", "classic:kanpo", HpObserver] : ["boundary:jiuyuan", "classic:kanpo"])
                { InitialHp = 2 });
            builder.AddDeck(new("fixture:paid-winner-deck", "固定黑色装备", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("classic:silver-lion", Suit.Club, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "回复支付胜负", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:paid-winner-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:paid-winner-owner", "fixture:paid-winner-target-1", "fixture:paid-winner-target-2", "fixture:paid-winner-target-3"]));
        }
    }
}
