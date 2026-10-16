using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryFaZhengChecks
{
    private const string Xuanhuo = "boundary:xuanhuo";
    private const string Enyuan = "boundary:enyuan";
    private const string Driver = "fixture:fz-driver";
    private const string Recovery = "fixture:fz-recovery";
    private const string Reward = "fixture:fz-reward";
    private const string RepaymentGain = "fixture:fz-repayment-gain";
    private const string RepaymentHp = "fixture:fz-repayment-hp";
    private const string DyingEntry = "fixture:fz-dying-entry";
    private const string RescueCommitted = "fixture:fz-rescue-committed";
    private const string TwoCardRescue = "fixture:fz-two-card-rescue";
    private const string Mode = "identity:classic-boundary-fa-zheng-fixture";

    public static void XuanhuoRealSlashAndNativeAiRetainPaidGift()
    {
        var (game, registry) = Create(); ReachXuanhuo(game);
        var given = BeginGift(game, registry, 1);
        Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 0);
        var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xuanhuo);
        Require(owner.BoundParticipantGift is null && owner.AssistedSlashRequest is { ActorSeat: 1, TargetSeat: null } &&
            given.All(id => game.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == id)),
            "The actual atomic two-card gift finishes before the owner specifies the other actor's real Slash target.");
        Require(!Prompt(game)!.Choices.Any(c => c.Targets.SequenceEqual([3])),
            "The designated target uses the commanded actor's actual range, excluding distance two.");
        Replay(game, registry); Reject(game); Answer(game, c => c.Targets.SequenceEqual([2]));
        Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 1);
        var offered = Prompt(game)!.Choices.Where(c => c.Parameters.GetValueOrDefault("request-option") == "use")
            .SelectMany(c => c.Cards).Distinct().ToArray();
        Require(offered.Length > 0 && Prompt(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "decline"),
            "The actual giver chooses a physical Slash or an explicit refusal.");
        for (var viewer = 0; viewer < 4; viewer++) if (viewer != 1)
            Require(game.CreateSnapshot(viewer).PendingDecision is null,
                "The commanded actor's physical payment choices stay private to that actor.");
        Replay(game, registry);
        // The actor is a native AI. Its own command advances the real published use choice.
        Accept(game, new AdvanceOneStepCommand(game.Revision)); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 2) == 1 &&
            game.CardMovements.Any(m => offered.Contains(m.CardId) && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) &&
            !game.Events.Any(e => e.Payload is ProgramPrivateHandViewedEvent),
            "Native AI pays its real Slash and resolves actual damage, then bypasses the private-hand refusal branch.");
        foreach (var id in given) Require(GiftMoves(game, id).Count() == 1, "Resuming the Slash child never pays the two-card gift twice.");
        Replay(game, registry);
    }

    private static IEnumerable<CardMovementRecord> GiftMoves(GameEngine game, int id) => game.CardMovements.Where(m =>
        m.CardId == id && m.From.OwnerSeat == 0 && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.boundary:xuanhuo.MoveBoundCards");
    private static int[] BeginGift(GameEngine game, ContentRegistry registry, int recipient)
    {
        Activate(game, Xuanhuo, "give-two-and-request"); Reach(game, p => Action(p, "select-target"));
        Answer(game, c => c.Targets.SequenceEqual([recipient])); Reach(game, p => Action(p, "select-owned-cards"));
        var cards = game.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
        Replay(game, registry); Reject(game); Answer(game, c => c.Cards.SequenceEqual([cards[0]])); Replay(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([cards[1]])); return cards;
    }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool giftObservers = false, bool damageObservers = false, bool effectiveRed = false, int? sourceInitialHp = null, bool rescueWithPeach = false, bool rescueWithTwoCards = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, giftObservers, damageObservers, effectiveRed, sourceInitialHp, rescueWithPeach, rescueWithTwoCards));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:fz-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool ActivateChoice(PendingDecision p, string skill, string binding) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void ReachXuanhuo(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && ActivateChoice(p, Xuanhuo, "give-two-and-request"));
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Activate(GameEngine game, string skill, string binding) => Answer(game, c => c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++) { var p = Prompt(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Fa Zheng fixture did not reach boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Skip(game);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p?.SkillPrompt?.SkillId is Recovery or Reward or RepaymentGain or RepaymentHp or DyingEntry or RescueCommitted) Continue(game);
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && State(game) == before, "An unpublished choice cannot change a frozen hand draft or repeat a paid cost."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "All four private views, exact owning payment stages, movement facts and real command prefixes cold-restore exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool giftObservers, bool damageObservers, bool effectiveRed, int? sourceInitialHp, bool rescueWithPeach, bool rescueWithTwoCards) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-fa-zheng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
                 {"id":"fixture:fz-quiet","revision":1,"triggers":[{"id":"quiet-turn","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Reward}}","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:enyuan.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"reward-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RepaymentGain}}","revision":1,"triggers":[{"id":"paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:enyuan.hand-repayment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RepaymentHp}}","revision":1,"triggers":[{"id":"paid","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{DyingEntry}}","revision":1,"triggers":[{"id":"entering","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RescueCommitted}}","revision":1,"triggers":[{"id":"paid-rescue","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["peach"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{TwoCardRescue}}","revision":1,"viewAs":[{"id":"two-spades","inputKinds":["slash"],"inputSuits":["spade"],"outputKind":"peach","forPlay":false,"forResponse":true,"inputCount":2,"sameSuit":true,"sourceZones":["hand","equipment"],"extendedUse":true}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实伤害驱动", description = "来自实际指定来源的两点伤害" },
                  ["fixture:fz-quiet"] = new { name = "安静回合", description = "固定夹具不发动普通杀" },
                  [Recovery] = Observer("银狮实际回复"), [Reward] = Observer("来源实际摸牌"),
                  [RepaymentGain] = Observer("红牌实际取得"), [RepaymentHp] = Observer("来源实际失血"), [DyingEntry] = Observer("真实濒死进入"),
                  [RescueCommitted] = Observer("真实救援提交"), [TwoCardRescue] = new { name = "两张实际材料救援", description = "固定无红实体的真实转化救援" } } }));
            foreach (var id in new[] { Driver, "fixture:fz-quiet", Recovery, Reward, RepaymentGain, RepaymentHp, DyingEntry, RescueCommitted, TwoCardRescue })
                builder.AddSkill(new(id, id, "真实边界夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:fz-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:fz-owner", "当前界法正机制", "supporter", Xuanhuo, "shu", 3,
                giftObservers ? [Enyuan, Driver, Recovery, Reward] : damageObservers ? [Enyuan, Driver, RepaymentGain] : [Enyuan, Driver]) { InitialHp = equipment ? 2 : 3 });
            for (var i = 1; i < 4; i++)
            {
                var skills = new List<string> { "fixture:fz-quiet" };
                if (giftObservers) skills.Add(Enyuan);
                if (damageObservers) skills.Add(RepaymentHp);
                if (sourceInitialHp is not null) skills.Add(DyingEntry);
                if (rescueWithPeach || rescueWithTwoCards) { skills.Add(Recovery); skills.Add(RescueCommitted); }
                if (rescueWithTwoCards) skills.Add(TwoCardRescue);
                if (effectiveRed) skills.Add("classic:hongyan");
                builder.AddGeneral(new($"fixture:fz-target-{i}", "固定实际来源", "supporter", "fixture:fz-selection", "wu", 8, skills) { InitialHp = sourceInitialHp });
            }
            builder.AddDeck(new("fixture:fz-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : rescueWithPeach ? "standard:peach" : "standard:slash", equipment ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实界法正", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:fz-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:fz-owner", "fixture:fz-target-1", "fixture:fz-target-2", "fixture:fz-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "付款后的真实子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
