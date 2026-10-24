using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5QiangxiChecks
{
    private const string Skill = StandardShanheS5DianweiWeiziPackage.QiangxiId;
    private const string Driver = "fixture:s5-qiangxi-driver";
    private const string Cost = "fixture:s5-qiangxi-weapon-child";
    private const string Hp = "fixture:s5-qiangxi-hp-child";
    private const string Mode = "identity:classic-s5-qiangxi-fixture";

    public static void ActualRoundScalingTwoPhaseCostsNingeAndCold()
    {
        var (g, registry) = Create();
        var turn = g.State.TurnNumber;
        var equipment = Hand(g).First().Id;
        Use(g, Driver, "place", 1, equipment); Play(g);
        var weapon = Hand(g).First().Id;
        Accept(g, new PlayCardCommand(0, weapon, [], g.Revision, P(g)!.PromptId)); Play(g);

        var ownerHp = g.State.Players[0].Hp; var targetHp = g.State.Players[1].Hp;
        Use(g, Skill, "lose-hp", 1); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var first = Root(g);
        Require(first.ShanheS5Qiangxi is { ActivationCount: 1, Paid: false } && first.InstructionIndex == 2 &&
            Events<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == first.Id && e.Amount == 1) == 1 &&
            g.State.Players[0].Hp == ownerHp - 1 && g.State.Players[1].Hp == targetHp &&
            !Events<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0),
            "The first cost is actual HP loss with its original frozen round value, before original-target damage; it must not become damage.");
        Cold(g, registry); Continue(g); Play(g);
        Require(g.State.Players[1].Hp == targetHp - 2 && Paid(g).Single().Damage == 2 &&
            Actions(g).All(a => a.SelectableTargetSeats.Contains(1)),
            "The current paid activation is included in X: first damage is two, and the same target remains legal.");

        Use(g, Skill, "discard-weapon", 1, weapon); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var second = Root(g);
        Require(second.ShanheS5Qiangxi is { ActivationCount: 2, Paid: false } && second.SelectedCardPayment is { MovementCommitted: true } &&
            g.CardMovements.Count(m => m.CardId == weapon && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == $"skill-program.{Skill}.DiscardSelected") == 1 && g.State.Players[1].Hp == targetHp - 2,
            "The second use pays its actual equipped weapon and waits for its native movement child before damage.");
        Cold(g, registry); Continue(g); Play(g);
        Require(g.State.Players[1].Hp == targetHp - 5 && g.State.Players[0].Hp == ownerHp - 1 &&
            Paid(g).Select(e => e.Damage).SequenceEqual([2, 3]) && !Actions(g).Any() &&
            g.CardMovements.Any(m => m.CardId == equipment && m.From == CardLocation.Equipment(1) && m.To == CardLocation.DiscardPile) &&
            DrawnByNinge(g) == 1,
            "Both costs share two uses per real Play phase. The second actual injury triggers genuine Ninge Draw1 and field discard exactly once.");

        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Require(g.State.TurnNumber == turn && Actions(g).Any(), "The later normal Play phase refreshes the shared phase quota in the same actual turn.");
        Use(g, Skill, "lose-hp", 2); Play(g);
        Use(g, Skill, "discard-weapon", 2, Hand(g).First().Id); Play(g);
        var paid = Paid(g);
        Require(paid.Select(e => e.ActivationCount).SequenceEqual([1, 2, 3, 4]) && paid.Select(e => e.Damage).SequenceEqual([2, 3, 4, 5]) &&
            paid.Select(e => e.RoundNumber).Distinct().Count() == 1 && paid.Select(e => e.PhaseInstanceId).Distinct().Count() == 2 &&
            g.State.Players[2].Hp == 3 && !Actions(g).Any() && DrawnByNinge(g) == 2 &&
            g.CardMovements.Count(m => m.CardId == weapon && m.Reason.Value == $"skill-program.{Skill}.DiscardSelected") == 1,
            "A new Play phase gives two more uses without resetting the round-wide frozen count. Completed child returns cannot pay or draw twice.");
        Cold(g, registry);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Require(g.State.TurnNumber > turn, "The next source turn is genuinely later.");
        Use(g, Skill, "lose-hp", 3); Play(g);
        Require(Paid(g).Last() is { ActivationCount: 1, Damage: 2 } next && next.RoundNumber > paid[0].RoundNumber &&
            g.State.Players[3].Hp == 10, "A new actual round resets X without retaining the previous turn's phase or round quota.");
        Cold(g, registry);
    }

    private static int DrawnByNinge(GameEngine g) => g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "program.applied-damage-benefit.draw");
    private static CardSnapshot[] Hand(GameEngine g) => g.CreateSnapshot(0).Players[0].Hand.ToArray();
    private static ShanheS5QiangxiPaidEvent[] Paid(GameEngine g) => Events<ShanheS5QiangxiPaidEvent>(g);
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static LegalAction[] Actions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ProgramSkillId == Skill).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Use(GameEngine g, string skill, string activation, int target, int? card = null) =>
        Accept(g, new UseProgramSkillCommand(0, skill, activation, card is { } id ? [id] : [], [target], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 300; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "boundary:ninge" }) Answer(g, c => c.Cards.Count == 1);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing real S5 Qiangxi boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected real S5 Qiangxi fixture command.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)),
        "Cold replay preserves all views, original paid cost/child/round receipts, movements and command prefix exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5DianweiWeiziPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:s5-qiangxi-owner", g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-qiangxi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"activations":[
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{{Cost}}}","revision":1,"triggers":[{"id":"weapon-cost","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.{{{Skill}}}.DiscardSelected"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
             {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"hp-cost","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:s5-qiangxi-extra","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
             {"id":"fixture:s5-qiangxi-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var presentation = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Cost, Hp, "fixture:s5-qiangxi-extra", "fixture:s5-qiangxi-quiet" })
                presentation[id] = id is Cost or Hp ? new { name = id, description = "真实原生付款子流程", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : (object)new { name = id, description = "真实阶段夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentation }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实原生流程") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:s5-qiangxi-pick-owner", "固定来源", "公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:s5-qiangxi-pick-other", "固定其他", "公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new("fixture:s5-qiangxi-owner", "强袭原生机制", "supporter", "fixture:s5-qiangxi-pick-owner", "wei", 8,
                [Skill, "boundary:ninge", Driver, Cost, Hp, "fixture:s5-qiangxi-extra"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:s5-qiangxi-other-{i}", "其他角色", "supporter", "fixture:s5-qiangxi-pick-other", "wei", 12, ["fixture:s5-qiangxi-quiet"]));
            b.AddDeck(new("fixture:s5-qiangxi-deck", "固定武器实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:qinggang_sword", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "山河强袭原生机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:s5-qiangxi-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:s5-qiangxi-owner", "fixture:s5-qiangxi-other-1", "fixture:s5-qiangxi-other-2", "fixture:s5-qiangxi-other-3"]));
        }
    }
}
