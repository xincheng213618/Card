using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryXiahouYuanChecks
{
    private const string Driver = "fixture:xy-driver";
    private const string Movement = "fixture:xy-movement";
    private const string FaceUp = "fixture:xy-face-up";
    private const string DamageTurnOver = "fixture:xy-damage-turn-over";
    private const string Mode = "identity:classic-xy-fixture";

    public static void ThreeShensuBranchesAndHandEquipmentCost()
    {
        var (game, registry) = Create();
        Reach(game, p => Activation(p, "boundary:shensu", "skip-judgment-and-draw"));
        var initialHand = game.State.Players[0].HandCount;
        Activate(game); SelectTarget(game, 2); Replay(game, registry);
        Reach(game, p => Activation(p, "boundary:shensu", "skip-play-pay-equipment"));
        Require(game.State.Players[0].HandCount == initialHand,
            "The first real branch skips normal draw while the second branch remains available.");
        Activate(game); SelectTarget(game, 2);
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Cards.Count == 1));
        var cost = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == cost && c.Kind == CardKind.SilverLion),
            "An actual equipment card in hand is a published Shensu payment.");
        Replay(game, registry); Reject(game);
        Answer(game, c => c.Cards.SequenceEqual([cost]));
        Reach(game, p => Activation(p, "boundary:shensu", "skip-discard-and-turn-over"));
        Require(game.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) &&
            m.To == CardLocation.DiscardPile) == 1 && game.State.Players[0].HandCount == initialHand - 1,
            "The second branch pays once and reaches discard without opening a normal play prompt.");
        Activate(game); SelectTarget(game, 2);
        Reach(game, p => Activation(p, "boundary:shebian"));
        var shensu = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:shensu");
        var turned = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
        Require(game.State.Players[0].IsFaceDown && turned.Window == SkillProgramTriggerWindow.CharacterTurnedOver &&
            turned.ResumeProgramFrameId == shensu.Id && turned.CharacterStateContinuation == CharacterStateContinuation.Program &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2) == 2,
            "The third branch owns its turned-over child before its final distance-unlimited Slash.");
        Replay(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        for (var step = 0; step < 80 && !game.Events.Any(e => e.Payload is TurnEndedEvent { ActorSeat: 0, TurnNumber: 1 }); step++)
            Advance(game);
        Require(game.Events.Any(e => e.Payload is TurnEndedEvent { ActorSeat: 0, TurnNumber: 1 }) &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2) == 3 &&
            !game.Events.Any(e => e.Payload is HandLimitDiscardedEvent discarded && discarded.ActorSeat == 0),
            "Three recorded branches each use a real virtual Slash and the third skips hand-limit discard.");
        Require(game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e =>
            e.SkillId == "boundary:shensu" && e.OwnerSeat == 0) == 3,
            "Each chosen Shensu branch starts once on the same actual turn.");
        Replay(game, registry);
    }

    public static void ShebianBothDirectionsMovementChildrenAndReplay()
    {
        var (game, registry) = Create(); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        foreach (var pair in new[] { (From: 0, To: 1, FaceDown: true), (From: 1, To: 0, FaceDown: false) })
        {
            var hand = game.State.Players[0].HandCount;
            Use(game, "flip");
            if (!pair.FaceDown)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == FaceUp);
                Require(game.State.Players[0].HandCount == hand,
                    "The existing face-up observer precedes the new window and parent draw.");
                Replay(game, registry); Continue(game);
            }
            Reach(game, p => Activation(p, "boundary:shebian"));
            Require(game.State.Players[0].IsFaceDown == pair.FaceDown,
                "Shebian is offered for each actual face-state direction.");
            var stateWindow = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
            var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
            Require(stateWindow.ResumeProgramFrameId == parent.Id && stateWindow.Facts.OwnerIsFaceDown == pair.FaceDown,
                "The window freezes the changed state and returns to the exact paid parent instruction.");
            Replay(game, registry); Reject(game); Activate(game);
            Reach(game, p => p.Choices.Any(c => c.Targets.SequenceEqual([pair.From, pair.To])));
            Answer(game, c => c.Targets.SequenceEqual([pair.From, pair.To]));
            Reach(game, p => p.SkillPrompt?.SkillId == "boundary:shebian" && p.Choices.Any(c => c.Cards.Contains(armor)));
            Replay(game, registry); Answer(game, c => c.Cards.Contains(armor));
            Reach(game, p => p.SkillPrompt?.SkillId == Movement);
            Require(game.CreateSnapshot(0).Players[pair.To].Equipment.Any(c => c.Id == armor) &&
                !game.CreateSnapshot(0).Players[pair.From].Equipment.Any(c => c.Id == armor) &&
                game.State.Players[0].HandCount == hand,
                "The actual corresponding-zone move commits before its observer, while the parent draw remains unpaid.");
            var paid = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:shebian");
            AssertAwaitedMovement(game, paid, armor, pair.From, pair.To);
            Replay(game, registry); Continue(game); ReachPlay(game);
            Require(game.State.Players[0].HandCount == hand + 1 && game.CardMovements.Count(m => m.CardId == armor &&
                m.From == CardLocation.Equipment(pair.From) && m.To == CardLocation.Equipment(pair.To)) == 1,
                "The child returns once, does not repeat the equipment transfer, and then pays the parent draw.");
            Replay(game, registry);
        }
        var changes = game.Events.Select(e => e.Payload).OfType<CharacterStateChangedEvent>()
            .Where(e => e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver).ToArray();
        Require(changes.Length == 2 && changes[0].Change.TurnedOver == new CharacterTurnedOverState(false, true) &&
            changes[1].Change.TurnedOver == new CharacterTurnedOverState(true, false),
            "Committed public facts retain both immutable before/after transitions.");
        var before = changes.Length;
        Use(game, "down"); ReachPlay(game);
        Use(game, "down"); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<CharacterStateChangedEvent>().Count(e =>
            e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver) == before + 1,
            "Setting an already-matching face state does not create another turned-over occurrence.");
        Replay(game, registry);
    }

    public static void NaturalFaceUpSkippedTurnAndOldWindowCompatibility()
    {
        var (game, registry) = Create(); ReachPlay(game); Use(game, "down"); ReachPlay(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, "boundary:shebian") && !game.State.Players[0].IsFaceDown);
        var window = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single();
        Require(window.CharacterStateContinuation == CharacterStateContinuation.SkippedTurn &&
            window.ResumeProgramFrameId is null && game.State.Players[0].IsFaceDown == false,
            "Natural face-up on the owner's next actual turn suspends its skipped-turn completion.");
        Replay(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Require(game.Events.Select(e => e.Payload).OfType<TurnEndedEvent>().Count(e => e.ActorSeat == 0) == 2,
            "The skipped actual turn completes after the same turned-over window.");
        Replay(game, registry);

        var (old, oldRegistry) = Create(oldWindowOnly: true); ReachPlay(old);
        Use(old, "down"); ReachPlay(old); Use(old, "down"); ReachPlay(old);
        Use(old, "up"); Reach(old, p => p.SkillPrompt?.SkillId == FaceUp); Replay(old, oldRegistry);
        Continue(old); ReachPlay(old);
        Require(old.Events.Select(e => e.Payload).OfType<CharacterStateChangedEvent>().Count() == 1 &&
            old.Events.Select(e => e.Payload).OfType<CharacterStateChangedEvent>().All(e =>
                e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedFaceUp && e.Change.TurnedOver is null),
            "A registry without the new window preserves the old opt-in face-up event stream.");
        Replay(old, oldRegistry);
    }

    public static void AfterDamageTurnOverRetainsCursorAndMovementChild()
    {
        var (game, registry) = Create(damageTurnOver: true); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var hand = game.State.Players[0].HandCount;
        Use(game, "damage", [1]);
        Reach(game, p => Activation(p, "boundary:shebian"));
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == DamageTurnOver);
        var changed = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
        Require(parent.WindowContext?.ParentFrameId == damage.Id && changed.ResumeProgramFrameId == parent.Id &&
            changed.CharacterStateContinuation == CharacterStateContinuation.Program && game.State.Players[0].IsFaceDown,
            "A real AfterDamage binding retains its exact damage cursor beneath the turned-over window.");
        Replay(game, registry); Activate(game);
        Reach(game, p => p.Choices.Any(c => c.Targets.SequenceEqual([0, 2])));
        Answer(game, c => c.Targets.SequenceEqual([0, 2]));
        Reach(game, p => p.Choices.Any(c => c.Cards.Contains(armor)));
        Answer(game, c => c.Cards.Contains(armor));
        Reach(game, p => p.SkillPrompt?.SkillId == Movement);
        Require(game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single().Id == damage.Id &&
            game.State.Players[0].HandCount == hand,
            "The actual movement observer retains both the paid Shebian parent and original damage ancestor before either draw tail.");
        AssertAwaitedMovement(game, game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:shebian"),
            armor, 0, 2);
        Replay(game, registry); Continue(game); ReachPlay(game);
        Require(game.ResolutionStack.Count == 0 && game.State.Players[0].HandCount == hand + 2 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.Equipment(2)) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == DamageTurnOver) == 1,
            "Exact child returns complete one equipment move, the AfterDamage draw, and then the original damage producer's draw.");
        Replay(game, registry);
    }

    public static void ShebianCannotReplaceOccupiedEquipmentSlot()
    {
        var (game, registry) = Create(); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        var sourceArmor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Use(game, "equip-other", [1]);
        Reach(game, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Cards.Count == 1));
        var destinationArmor = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Replay(game, registry); Answer(game, c => c.Cards.SequenceEqual([destinationArmor])); ReachPlay(game);
        Require(sourceArmor != destinationArmor && game.CreateSnapshot(0).Players[1].Equipment.Any(c => c.Id == destinationArmor),
            "Actual Equip and hand-to-equipment commands fill the two corresponding armor slots with distinct physical cards.");
        var movementCount = game.CardMovements.Count; var eventCount = game.Events.Count;
        var hand = game.State.Players[0].HandCount;
        var sourceHp = game.State.Players[0].Hp; var destinationHp = game.State.Players[1].Hp;
        Use(game, "flip"); Reach(game, p => Activation(p, "boundary:shebian"));
        Replay(game, registry); Activate(game);
        Reach(game, p => p.Choices.Any(c => c.Targets.SequenceEqual([0, 1])));
        Replay(game, registry); Reject(game); Answer(game, c => c.Targets.SequenceEqual([0, 1])); ReachPlay(game);
        Require(game.ResolutionStack.Count == 0 && game.State.Players[0].IsFaceDown &&
            game.CreateSnapshot(0).Players[0].Equipment.Single().Id == sourceArmor &&
            game.CreateSnapshot(0).Players[1].Equipment.Single().Id == destinationArmor &&
            !game.CardMovements.Skip(movementCount).Any(m => m.From.Zone == CardZoneKind.Equipment) &&
            !game.Events.Skip(eventCount).Any(e => e.Payload is SilverLionRemovedRecoveryEvent) &&
            game.State.Players[0].Hp == sourceHp && game.State.Players[1].Hp == destinationHp,
            "No legal card for the occupied corresponding slot skips Shebian without replacing, transferring, discarding or recovering either armor.");
        var parentDraws = game.CardMovements.Skip(movementCount).Where(m =>
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
        Require(game.State.Players[0].HandCount == hand + 1 && parentDraws.Length == 1 &&
            game.Events.Skip(eventCount).Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e =>
                e.SkillId == "boundary:shebian" && e.OwnerSeat == 0) == 1,
            "The no-card occurrence ends once and resumes only the original flip program's single draw tail.");
        Replay(game, registry);
    }

    private static (GameEngine, ContentRegistry) Create(bool oldWindowOnly = false, bool damageTurnOver = false)
    {
        var packages = new List<IGameContentPackage> { new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage() };
        if (!oldWindowOnly) packages.Add(new StandardClassicGeneralPackage());
        packages.Add(new Fixture(oldWindowOnly, damageTurnOver)); var registry = ContentRegistry.Build(packages.ToArray());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:xy-owner", game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill, string? binding = null) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill &&
        (binding is null || c.Parameters.GetValueOrDefault("binding-id") == binding));
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 160; step++)
        {
            var prompt = Prompt(game); if (prompt is not null && predicate(prompt)) return;
            Advance(game);
        }
        throw new InvalidOperationException("Fixed Xiahou Yuan fixture did not reach boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var prompt = Prompt(game);
        if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (prompt?.SkillPrompt?.SkillId is FaceUp or Movement) Continue(game);
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void SelectTarget(GameEngine game, int seat)
    {
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([seat])));
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([seat]));
    }
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? targets = null) => Accept(game, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], game.Revision, Prompt(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected command."); }
    private static void Reject(GameEngine game)
    {
        var state = State(game); var prompt = Prompt(game)!;
        Require(!game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && State(game) == state,
            "An unpublished choice rejects atomically without paying or changing the frozen occurrence.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "The recorded command prefix cold-restores all player views, owning frames, movements and frozen public facts.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void AssertAwaitedMovement(GameEngine game, ProgramSkillFrame paid, int cardId, int from, int to)
    {
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f =>
            f.Batch.ParentFrameId == paid.Id && f.Batch.AwaitingProgramFrameId == paid.Id);
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Movement);
        Require(paid.PendingMovementContinuation is { SubjectSeat: var source, BeforeCount: 0, CoverageResultBind: null } && source == from &&
            movement.ResumeProgramFrameId is null && observer.WindowContext is
                { Window: SkillProgramTriggerWindow.CardsMoved, ParentFrameId: var parent } && parent == movement.Id &&
            observer.OwnerSeat == from && movement.Batch.Movements.Count == 1 &&
            movement.Batch.Movements.Single() is var moved && moved.CardId == cardId &&
            moved.From == CardLocation.Equipment(from) && moved.To == CardLocation.Equipment(to),
            "The exact SelectAndMoveOwnedCard continuation owns the committed equipment batch and its source owner's movement observer.");
    }

    private sealed class Fixture(bool oldWindowOnly, bool damageTurnOver) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xy-boundaries", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"flip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"},{"op":"draw","target":"owner","amount":1}]},
                  {"id":"down","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":true}]},
                  {"id":"up","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":false}]},
                  {"id":"equip-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},
                  {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1},{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"{{DamageTurnOver}}","revision":1,"triggers":[{"id":"actual-damage-flip","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"turnOver","target":"owner"},{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"{{FaceUp}}","revision":1,"triggers":[{"id":"old-face-up","window":"characterTurnedFaceUp","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
                {"id":"{{Movement}}","revision":1,"triggers":[{"id":"equipment-transfer","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementReasons":["skill-program.boundary:shebian.SelectAndMoveOwnedCard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实翻面", description = "通过命令翻面并保留后续摸牌。" },
                    [DamageTurnOver] = new { name = "伤害翻面", description = "真实伤害观察中的翻面子窗口。" },
                    [FaceUp] = new { name = "正面观察", description = "旧正面窗口。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Movement] = new { name = "移动观察", description = "支付后的装备移动。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var id in new[] { Driver, FaceUp, Movement }) builder.AddSkill(new(id, id, "正式程序检查") { Program = catalog.Programs[id] });
            if (damageTurnOver) builder.AddSkill(new(DamageTurnOver, "伤害翻面", "正式伤害子窗口") { Program = catalog.Programs[DamageTurnOver] });
            builder.AddGeneral(new("fixture:xy-owner", "神速驱动", "supporter", oldWindowOnly ? Driver : "boundary:shensu", "wei", 6,
                oldWindowOnly ? [FaceUp] : damageTurnOver ? ["boundary:shebian", Driver, FaceUp, Movement, DamageTurnOver] : ["boundary:shebian", Driver, FaceUp, Movement]) { InitialHp = 4 });
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:xy-target-{index}", "固定目标", "supporter",
                "standard:none", "shu", 6, oldWindowOnly ? [] : [Movement]) { InitialHp = 5 });
            builder.AddDeck(new("fixture:xy-deck", "固定装备", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(oldWindowOnly ? "standard:slash" : "classic:silver-lion", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实神速和设变", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:xy-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xy-owner", "fixture:xy-target-1", "fixture:xy-target-2", "fixture:xy-target-3"]));
        }
    }
}
