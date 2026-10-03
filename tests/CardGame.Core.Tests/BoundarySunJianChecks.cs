using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundarySunJianChecks
{
    private const string Driver = "fixture:sj-driver";
    private const string LossChild = "fixture:sj-loss-child";
    private const string DyingChild = "fixture:sj-dying-child";

    public static void WuliePaysOnceAndPreventsWholeDamageAfterSourceDeath()
    {
        var (game, registry) = Start();
        Require(game.State.Players[0].Hp == 4 && game.State.Players[0].MaxHp == 5,
            "Current ordinary OL Sun Jian has maximum five HP and initial four HP.");
        EndPlay(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:wulie");
        Activate(game);
        var prompt = P(game)!;
        var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:wulie").HpDamageShieldDraft!;
        Require(draft.Maximum == 3 && draft.CandidateSeats is System.Collections.IList { IsReadOnly: true } &&
            prompt.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count is >= 1 and <= 3 && !c.Targets.Contains(0)),
            "One private finite quantity-and-target prompt freezes affordable other living targets.");
        var before = State(game);
        Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, new("fixture:foreign"), game.Revision)).Accepted &&
            State(game) == before, "An unpublished shield choice is rejected atomically before HP payment.");
        Replay(game, registry);
        Answer(game, c => c.Targets.SequenceEqual(new[] { 1, 2 }));
        Reach(game, p => p.SkillPrompt?.SkillId == LossChild);
        var paid = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:wulie");
        Require(game.State.Players[0].Hp == 2 && paid.HpDamageShieldReceipt is { ActualLost: 2, Granted: false } receipt &&
            receipt.TargetSeats is System.Collections.IList { IsReadOnly: true } && Marker(game, 1) == 0 && Marker(game, 2) == 0,
            "The HP observer suspends the once-paid parent before either shield is granted.");
        Require(game.CreateSnapshot(-1).PendingDecision?.Choices.Count is null or 0 &&
            game.CreateSnapshot(1).PendingDecision?.Choices.Count is null or 0,
            "Private cost and HP-child decisions do not expose choices to another viewer or spectator.");
        Replay(game, registry);
        Continue(game);
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(Marker(game, 1) == 1 && Marker(game, 2) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == "boundary:wulie" && e.Amount == 2) == 1,
            "The typed HP return grants one independent shield to each frozen target and never repays HP.");
        var hp = game.State.Players[1].Hp;
        Use(game, "die-and-hit-twice", [1]);
        Drain(game, () => !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Driver));
        Require(!game.State.Players[0].IsAlive && game.State.Players[1].Hp == hp - 3 &&
            Marker(game, 1) == 0 && Marker(game, 2) == 1 &&
            game.Events.Select(e => e.Payload).OfType<OneUseDamageShieldConsumedEvent>().Single().PreventedAmount == 3,
            "Source death preserves already-paid shields; the first whole three-point damage is prevented and the second deals three.");
        Require(game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>()
            .Count(e => e.SkillId == "boundary:wulie") == 1, "The limited trigger is paid once in this game.");
        Replay(game, registry);
    }

    public static void WulieLethalPaymentReturnsAfterDying()
    {
        var (game, registry) = Start();
        for (var i = 0; i < 3; i++) { Use(game, "trim"); Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); }
        Require(game.State.Players[0].Hp == 1, "Real commands prepare the affordable lethal quantity.");
        EndPlay(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:wulie"); Activate(game);
        Require(P(game)!.Choices.All(c => c.Targets.Count == 1), "The lethal quantity is bounded by the actual remaining HP.");
        Answer(game, c => c.Targets.SequenceEqual(new[] { 2 }));
        Reach(game, p => p.SkillPrompt?.SkillId == DyingChild);
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:wulie");
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        Require(parent.HpDamageShieldReceipt is { ActualLost: 1, HpAfter: 0, Granted: false } &&
            dying.ParentFrameId == parent.Id && Marker(game, 2) == 0,
            "Lethal physical payment owns the exact existing ProgramSkill dying child before granting protection.");
        Replay(game, registry); Continue(game);
        Drain(game, () => !game.State.Players[0].IsAlive && Marker(game, 2) == 1);
        Require(game.State.Winner == Winner.None &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == "boundary:wulie") == 1 &&
            game.Events.Select(e => e.Payload).OfType<OneUseDamageShieldGrantedEvent>().Single().Shield.TargetSeat == 2,
            "A paid lethal instruction returns through death and grants its surviving recipient once without replacing or repaying.");
        Replay(game, registry);
    }

    public static void WulieWinnerHpChildCancelsPaidTail()
    {
        var (game, registry) = Start("winner"); EndPlay(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:wulie"); Activate(game);
        Answer(game, c => c.Targets.SequenceEqual(new[] { 1, 2 }));
        Reach(game, p => p.SkillPrompt?.SkillId == LossChild); Replay(game, registry); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == LossChild && p.Choices.Any(c => c.Targets.Count == 1));
        var lord = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
        Answer(game, c => c.Targets.SequenceEqual(new[] { lord }));
        Drain(game, () => game.State.Status == EngineStatus.Completed);
        Require(game.State.Winner != Winner.None &&
            !game.Events.Any(e => e.Payload is OneUseDamageShieldGrantedEvent) &&
            Enumerable.Range(0, 4).All(s => Marker(game, s) == 0) &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == "boundary:wulie" && e.Amount == 2) == 1,
            "A winner resolved in the real paid HP child cancels the exact tail without refund, repayment, new markers or shields.");
        Replay(game, registry);
    }

    public static void WulieNativeAiActivatesAffordableProtection()
    {
        var (game, registry) = Start("ai");
        var turn = game.State.TurnNumber; EndPlay(game);
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && game.State.TurnNumber > turn);
        var loyalist = SeatWithRole(game, Role.Loyalist);
        var lord = SeatWithRole(game, Role.Lord);
        Require(game.Events.Select(e => e.Payload).OfType<OneUseDamageShieldGrantedEvent>()
                .Any(e => e.Shield.OwnerSeat == loyalist && e.Shield.TargetSeat == lord) &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().Where(e => e.SkillId == "boundary:wulie" && e.TargetSeat != 0)
                .All(e => e.Amount >= 1 && e.RemainingHp > 0),
            "Native loyalist AI protects the publicly known friendly Lord with a real affordable quantity and stays alive.");
        Replay(game, registry);

        var (opposed, opposedRegistry) = Start("ai-one-hp");
        var opposedTurn = opposed.State.TurnNumber; EndPlay(opposed);
        Reach(opposed, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && opposed.State.TurnNumber > opposedTurn);
        var rebel = SeatWithRole(opposed, Role.Rebel);
        var otherLoyalist = SeatWithRole(opposed, Role.Loyalist);
        Require(opposed.State.Players[rebel].Hp == 1 &&
            opposed.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
                .Any(e => e.SkillId == "boundary:wulie" && e.OwnerSeat == rebel && !e.Activated) &&
            !opposed.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>()
                .Any(e => e.SkillId == "boundary:wulie" && e.TargetSeat == rebel),
            "Native one-HP AI skips a self-lethal shield cost without emitting payment or protection.");
        Replay(opposed, opposedRegistry);
        Use(opposed, "kill-other", [otherLoyalist]); Reach(opposed, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Use(opposed, "recover-other", [rebel]); Reach(opposed, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var skipsBefore = opposed.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
            .Count(e => e.SkillId == "boundary:wulie" && e.OwnerSeat == rebel && !e.Activated);
        Use(opposed, "die-only");
        Drain(opposed, () => !opposed.State.Players[0].IsAlive &&
            opposed.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>()
                .Count(e => e.SkillId == "boundary:wulie" && e.OwnerSeat == rebel && !e.Activated) > skipsBefore);
        Require(opposed.State.Players.Where(p => p.IsAlive).Select(p => p.Seat).Order()
                .SequenceEqual(new[] { rebel, SeatWithRole(opposed, Role.Lord) }.Order()) &&
            opposed.State.Players[rebel].Hp == 3 &&
            !opposed.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>()
                .Any(e => e.SkillId == "boundary:wulie" && e.TargetSeat == rebel) &&
            !opposed.Events.Select(e => e.Payload).OfType<OneUseDamageShieldGrantedEvent>()
                .Any(e => e.Shield.OwnerSeat == rebel),
            "With three HP and only the publicly known enemy Lord left, native rebel AI skips before payment and never protects the enemy.");
        Replay(opposed, opposedRegistry);
    }

    private static int SeatWithRole(GameEngine game, Role role) =>
        game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == role).Seat;

    private static (GameEngine, ContentRegistry) Start(string mode = "normal")
    {
        var definitions = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(definitions, mode));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade,
            ModeId = "identity:classic-sun-jian-check", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:sj-owner", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (game, registry);
    }

    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4)
        .Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static int Marker(GameEngine game, int seat) => game.State.Players[seat].Markers?
        .SingleOrDefault(m => m.Kind == PlayerMarkerKind.Lie)?.Count ?? 0;
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate) =>
        Drain(game, () => P(game) is { } p && predicate(p));
    private static void Drain(GameEngine game, Func<bool> finished)
    {
        for (var i = 0; i < 160; i++)
        {
            if (finished()) return;
            if (P(game) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (P(game) is { PlayerSeat: 0, SkillPrompt.SkillId: LossChild or DyingChild }) Continue(game);
            else if (P(game) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, c => c.Cards.Count == 0);
            else if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("Sun Jian fixture completed before the requested real boundary.");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Sun Jian fixture did not reach its requested boundary: " + JsonSerializer.Serialize(new
        {
            Prompt = P(game), game.State.Status, game.State.Winner, game.State.TurnNumber,
            game.State.CurrentSeat, game.State.Phase,
            Players = game.State.Players.Select(p => new { p.Seat, p.IsAlive, p.Hp, p.MaxHp, p.HandCount }),
            Frames = JsonSerializer.Serialize(game.ResolutionStack),
            Recasts = game.Events.Count(e => e.Payload is CardRecastEvent)
        }));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(-1, 5).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Cold command replay preserves all viewers, exact owning frames, scalar rule facts and card movement history.");
        Require(game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80 &&
            Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).Players.Where(p => p.Seat != s).All(p => p.Hand.Count == 0)),
            "All physical cards are conserved and other viewers never acquire private hand identities.");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(ContentRegistry definitions, string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-sun-jian", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(definitions.GetSkill("boundary:yinghun"));
            builder.AddSkill(definitions.GetSkill("boundary:wulie"));
            var lossTail = mode == "winner" ? """
                ,
                {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                {"op":"loseHp","target":"selectedTarget","amount":20},
                {"op":"loseHp","target":"selectedTarget","amount":20}
                """ : "";
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = 3,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实付款驱动", description = "通过真实命令调整体力并使用真实技能伤害。" },
                    [LossChild] = new { name = "真实体力子窗口", description = "暂停已付款后的体力变化观察。",
                        optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [DyingChild] = new { name = "真实濒死子窗口", description = "暂停已付款后的濒死观察。",
                        optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            });
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"trim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving",
                "usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"kill-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"recover-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"die-only","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving",
                "usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":8}]},
                {"id":"die-and-hit-twice","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "usesPerTurn":null,"continueAfterOwnerDeath":true,"effects":[{"op":"loseHp","target":"owner","amount":8},
                {"op":"damage","target":"selectedTarget","amount":3},{"op":"damage","target":"selectedTarget","amount":3}]}]},
                {"id":"{{LossChild}}","revision":1,"triggers":[{"id":"actual-hp-child","window":"afterHpLost","subject":"owner",
                "optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observed-loss",
                "options":[{"id":"continue"}]}{{lossTail}}]}]},
                {"id":"{{DyingChild}}","revision":1,"triggers":[{"id":"actual-dying-child","window":"selfDyingResponse",
                "subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"observed-dying","options":[{"id":"continue"}]}]}]}]}
                """, presentation);
            foreach (var id in new[] { Driver, LossChild, DyingChild })
                builder.AddSkill(new(id, id, "实际命令观察") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:sj-select", "固定其他角色", "保留人工机制角色")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:sj-owner", "界孙坚机制", "sun_jian", "boundary:yinghun", "wu", 5,
                ["boundary:wulie", Driver, LossChild, DyingChild]) { InitialHp = 4 });
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:sj-other-{i}", "其他" + i, "supporter", "fixture:sj-select", "qun",
                    mode.StartsWith("ai", StringComparison.Ordinal) ? 5 : 20,
                    mode.StartsWith("ai", StringComparison.Ordinal) ? ["boundary:wulie"] : [])
                { InitialHp = mode == "ai-one-hp" ? 1 : mode == "ai" ? 4 : null });
            // Response-only entities keep native AI turns finite and short. No
            // Peach or rescue conversion can hide a lethal HP payment.
            builder.AddCard(new("fixture:sj-dodge", "闪", "基本牌", "稳定无桃实体", CardKind.Dodge));
            builder.AddDeck(new("fixture:sj-deck", "稳定实体", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("fixture:sj-dodge", Suit.Club, i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-sun-jian-check", "界孙坚机制", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:sj-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:sj-owner", "fixture:sj-other-1", "fixture:sj-other-2", "fixture:sj-other-3"]));
        }
    }
}
