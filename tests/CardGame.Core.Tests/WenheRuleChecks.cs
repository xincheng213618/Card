using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WenheRuleChecks
{
    private const string Mode = "fixture:wenhe-rules", Driver = "fixture:wenhe-rules-driver",
        Owner = "fixture:wenhe-rules-owner", Quiet = "fixture:wenhe-rules-quiet", DrawWatch = "fixture:wenhe-rules-draw-watch";

    public static void ChampionMarkNativeDrawPrivateUntilUseOnceAndColdReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = New(registry);
        Start(game);
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(game.CreateSnapshot(0).WenheAspiration is { GoalRank: 1, CanUseChampionMark: true, ChampionMarkUsed: false } &&
            Enumerable.Range(0, 8).All(viewer => game.CreateSnapshot(viewer).Players.All(player => player.WenheChampionMarkUsed is null)) &&
            Enumerable.Range(1, 7).All(viewer => game.CreateSnapshot(viewer).Players[0].Hand.Count == 0) &&
            Enumerable.Range(0, 8).All(viewer => game.CreateSnapshot(viewer).Players.All(player =>
                player.Skills!.Any(skill => skill.ContentId == "wenhe:champion-mark") == (player.Seat == viewer))) &&
            game.GetHumanLegalActions().Count(action => action.ProgramSkillId == "wenhe:champion-mark") == 1 &&
            E<WenheChampionMarkUsedEvent>(game).Length == 0,
            "Before actual use the champion owner has one private legal action, while no viewer sees an unused champion marker or another player's hand.");
        game = Cold(game, registry);
        var before = V(game, 0).HandCount;
        Accept(game, new UseProgramSkillCommand(0, "wenhe:champion-mark", "draw-two", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var used = E<WenheChampionMarkUsedEvent>(game).Single();
        Need(used.Seat == 0 && V(game, 0).HandCount == before + 2 &&
            game.CreateSnapshot(0).WenheAspiration is { GoalRank: 1, CanUseChampionMark: false, ChampionMarkUsed: true } &&
            E<NativeDrawAttemptStartedEvent>(game).Count(e => e.Context.ParentFrameId == used.ProgramFrameId &&
                e.Context.RecipientSeat == 0 && e.Context.RequestedCount == 2) == 1 &&
            Enumerable.Range(0, 8).All(viewer => game.CreateSnapshot(viewer).Players[0].WenheChampionMarkUsed == true &&
                game.CreateSnapshot(viewer).Players[0].Skills!.Any(skill => skill.ContentId == "wenhe:champion-mark") &&
                game.CreateSnapshot(viewer).Players.Skip(1).All(player => player.WenheChampionMarkUsed is null)) &&
            game.GetHumanLegalActions().All(action => action.ProgramSkillId != "wenhe:champion-mark"),
            "One actual targetless champion activation must pay the once-game mark before a native two-card draw and only then publicly reveal that used marker.");
        game = Cold(game, registry);
        var frozen = State(game);
        var forged = game.Submit(new UseProgramSkillCommand(0, "wenhe:champion-mark", "draw-two", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Need(!forged.Accepted && State(game) == frozen && E<WenheChampionMarkUsedEvent>(game).Length == 1,
            "A forged second champion activation must reject without drawing, changing any private viewer, moving a cursor or recording another use.");
    }

    public static void FirstRoundChaosIncludesOwnerUsesNearestPhysicalSlashAndColdPrompt()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(slash: true));
        var game = New(registry);
        Start(game);
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger,
            SkillPrompt.SkillId: "wenhe:chaos-resolution" });
        var chaos = E<WenheChaosIssuedEvent>(game).Single();
        var actors = E<WenheChaosActorEvent>(game).Where(e => e.ProgramFrameId == chaos.ProgramFrameId).OrderBy(e => e.Index).ToArray();
        var prompt = game.PendingDecision!;
        Need(E<WenheRoundRuleChangedEvent>(game) is [{ RoundNumber: 1, Rule: WenheRuleKind.Chaos }] &&
            chaos.CardActionId is null && chaos.ActorCount == 8 && actors.Length == 8 &&
            actors.Select(e => e.Seat).Distinct().Count() == 8 && actors.Any(e => e.Seat == chaos.OwnerSeat) &&
            prompt.IsPrivate && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") != "decline"),
            "The actual first round must issue one frozen eight-actor chaos queue including its owner and reach a genuine private native Slash prompt.");
        var minimum = game.CreateSnapshot(0).Players.Where(p => p.IsAlive && p.Seat != 0).Min(p => game.GetCombatDistance(0, p.Seat));
        var choices = prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("request-option") != "decline").ToArray();
        Need(choices.All(c => c.Cards.Count == 1 && c.Targets.Count == 1 &&
                game.GetCombatDistance(0, c.Targets[0]) == minimum) &&
            Enumerable.Range(1, 7).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Every actual published Slash payment must target a nearest living other character; foreign viewers cannot see the human's cards or choices.");
        game = Cold(game, registry);
        prompt = game.PendingDecision!;
        var use = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("request-option") != "decline");
        var material = use.Cards.Single(); var target = use.Targets.Single();
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, use.Id, game.Revision));
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var declared = E<CardUseDeclaredEvent>(game).Single(e => e.SourceSeat == 0 && e.CardId == material && e.CardKind == CardKind.Slash);
        Need(E<TargetsConfirmedEvent>(game).Any(e => e.ResolutionId == declared.ResolutionId && e.TargetSeats.SequenceEqual([target])) &&
            E<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == declared.ResolutionId) &&
            E<ProgramSkillResolvedEvent>(game).Count(e => e.SkillId == "wenhe:chaos-resolution") == 1 &&
            E<WenheChaosIssuedEvent>(game).Length == 1 &&
            game.GetHumanLegalActions().All(action => action.ProgramSkillId != "wenhe:chaos-resolution") &&
            game.CreateCardZoneDiagnostics().Single(card => card.CardId == material).Location.Zone == CardZoneKind.DiscardPile,
            "A native nearest Slash must retain its physical material and target, finish its real card child, and return once to the round parent without exposing the internal chaos activation.");
        Cold(game, registry);
    }

    public static void KillRewardRaisesOnlyMaximumDrawsThreeAndFinalDeathChildPrecedesWinner()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = New(registry);
        Start(game);
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var first = V(game, 0);
        Need(first.Hp < first.MaxHp && E<WenheRoundRuleChangedEvent>(game).Single().Rule == WenheRuleKind.Chaos,
            "Real first-round refusal must wound the actual killer before testing maximum-health-only rewards.");
        for (var victim = 1; victim < 8; victim++)
        {
            var beforeRescue = V(game, 0);
            Accept(game, new UseProgramSkillCommand(0, Driver, "defeat", [], [victim], game.Revision, game.PendingDecision!.PromptId));
            Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Need(V(game, victim) is { IsAlive: true, Hp: 3, SurvivalRank: null } &&
                E<ProgramBindingResolvedEvent>(game).Count(e => e.OwnerSeat == victim && e.SkillId == "wenhe:niepan" && e.Activated && e.Completed) == 1 &&
                V(game, 0).Hp == beforeRescue.Hp && V(game, 0).MaxHp == beforeRescue.MaxHp &&
                V(game, 0).HandCount == beforeRescue.HandCount,
                "The first genuine lethal hit must resolve that five-tier opponent's one real Nirvana recovery and draw, with no rank or false kill reward.");
            var before = V(game, 0); var rewardCount = E<WenheRuleFlowIssuedEvent>(game).Count(e => e.Operation == SkillProgramEffectOp.RewardWenheKiller);
            var start = game.Events.Count;
            Accept(game, new UseProgramSkillCommand(0, Driver, "defeat", [], [victim], game.Revision, game.PendingDecision!.PromptId));
            Until(game, () => game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: Driver });
            Need(!V(game, victim).IsAlive && V(game, victim).SurvivalRank == 9 - victim &&
                game.CreateSnapshot(0).Winner == Winner.None && V(game, 0).Hp == before.Hp &&
                V(game, 0).MaxHp == before.MaxHp &&
                E<WenheRuleFlowIssuedEvent>(game).Count(e => e.Operation == SkillProgramEffectOp.RewardWenheKiller) == rewardCount,
                "The actual CharacterDied child must suspend the death tree before rewards and before any winner, even when only one character remains alive.");
            if (victim is 1 or 7) game = Cold(game, registry);
            var prompt = game.PendingDecision!;
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single().Id, game.Revision));
            Until(game, () => victim == 7 ? game.CreateSnapshot(0).Winner != Winner.None :
                game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            var after = V(game, 0);
            var rewards = E<WenheRuleFlowIssuedEvent>(game).Where(e => e.Operation == SkillProgramEffectOp.RewardWenheKiller).ToArray();
            Need(after.MaxHp == before.MaxHp + 1 && after.Hp == before.Hp && after.HandCount == before.HandCount + 3 &&
                rewards.Length == rewardCount + 1 && rewards[^1] is { OwnerSeat: 0, Amount: 1, Count: 1 } &&
                game.Events.Skip(start).Any(e => e.Payload is NativeDrawAttemptStartedEvent draw &&
                    draw.Context.RecipientSeat == 0 && draw.Context.RequestedCount == 3),
                "Each genuine sourced kill must grant exactly one maximum HP without healing, followed by three cards through a real native draw child.");
            if (victim == 7)
            {
                var facts = game.Events.Select(e => e.Payload).ToArray();
                var rewardFrame = rewards[^1].ProgramFrameId;
                var end = Array.FindIndex(facts, e => e is ProgramBindingResolvedEvent p && p.FrameId == rewardFrame &&
                    p.SkillId == "wenhe:rules" && p.BindingId == "kill-reward" && p.Activated && p.Completed);
                var win = Array.FindIndex(facts, e => e is WinnerDeterminedEvent);
                Need(game.CreateSnapshot(0) is { Winner: Winner.SoleSurvivor, WinnerSeat: 0 } &&
                    V(game, 0).SurvivalRank == 1 && end >= 0 && win > end &&
                    game.CreateSnapshot(0).Players.Select(p => p.SurvivalRank).Distinct().Count() == 8,
                    "The final real reward subtree must finish before the sole-survivor winner event and all eight public ranks must be unique.");
            }
        }
        Cold(game, registry);
    }

    private static GameEngine New(ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    { Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = null, ModeId = Mode, UseInteractiveSetup = true,
        AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, WenheAspirationRank = 1, MaxTurns = 20 }, registry);
    private static void Start(GameEngine game)
    {
        Accept(game, new StartGameCommand());
        Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        var prompt = game.PendingDecision!;
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, prompt.PromptId));
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
    }
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(0).Players[seat];
    private static T[] E<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Wenhe rule fixture command rejected."); }
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 240; step++)
        {
            if (ready()) return;
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or
                    DecisionKind.RespondSlash or DecisionKind.Nullification } response)
            {
                var pass = response.Choices.First(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                Accept(game, new AnswerPromptCommand(0, response.PromptId, pass.Id, game.Revision));
            }
            else if (game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: DrawWatch } draw)
            {
                var skip = draw.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ?? draw.Choices.Single();
                Accept(game, new AnswerPromptCommand(0, draw.PromptId, skip.Id, game.Revision));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Wenhe rule fixture did not reach its bounded native boundary: " + SnapshotJson.Serialize(game.CreateSnapshot(0)));
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var saved = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(saved, registry);
        Need(State(game) == State(restored), "Cold JSON replay must preserve all eight private views, physical zones, accepted commands, events and pending frame-owned children.");
        return restored;
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 8).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands)
    });

    private sealed class Fixture(bool slash = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wenhe-rules-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in activity.Programs)
                builder.AddSkill(new(id, activity.Presentations[id].Name, activity.Presentations[id].Description)
                { Program = program, ProgramPresentation = activity.Presentations[id], Tags = id == "wenhe:niepan" ? SkillTag.Limited : id == "wenhe:rules" ? SkillTag.Locked : SkillTag.None });
            var driver = SkillProgramCatalog.Load($$$"""
              {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{Driver}}}","revision":1,
              "activations":[{"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"damage","target":"selectedTarget","amount":20}]}],
              "triggers":{{{(slash ? "[]" : """
              [{"id":"pause-real-death","window":"characterDied","subject":"owner","priority":100,"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"death-watch","options":[{"id":"continue"}]}]}]
              """)}}} },
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-ai-play","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{DrawWatch}}}","revision":1,"triggers":[{"id":"actual-draw-request","window":"nativeDrawStarting","subject":"owner","optional":true,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-watch","options":[{"id":"continue"}]}]}]}]}
              """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                { [Driver] = new { name = "Native rule driver", description = "Real native activity-rule fixture.",
                    optionLabels = slash ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "Continue actual death" } },
                  [Quiet] = new { name = "Quiet peer", description = "Skip ordinary AI play while preserving actual turn and lifecycle boundaries." },
                  [DrawWatch] = new { name = "Native draw watch", description = "Expose the actual typed native request before physical draw.", optionLabels = new Dictionary<string,string> { ["continue"] = "Continue real draw" } } } }));
            foreach (var (id, program) in driver.Programs)
                builder.AddSkill(new(id, driver.Presentations[id].Name, driver.Presentations[id].Description)
                { Program = program, ProgramPresentation = driver.Presentations[id] });
            var ids = new[] { Owner }.Concat(Enumerable.Range(1, 7).Select(index => $"fixture:wenhe-rules-{index}")).ToArray();
            foreach (var id in ids) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, "wei", 4, [DrawWatch]));
            builder.AddDeck(new("fixture:wenhe-rules-deck", "Homogeneous physical test pile", 4, 2,
                [new(slash ? "standard:slash" : "standard:dodge", 200)]));
            builder.AddMode(new(Mode, "Wenhe native rules fixture", 8, 8, new Dictionary<string, int>(),
                "fixture:wenhe-rules-deck", 5, ids, ContentModeKind.Survival) { WenheActivity = new() });
        }
    }
}
