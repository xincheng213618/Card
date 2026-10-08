using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuHongActivationChecks
{
    private const string Skill = "ol:yujue";
    private const string Activation = "yujue-launch";
    private const string Owner = "fixture:liu-hong-owner";
    private const string Peer = "fixture:liu-hong-peer";
    private const string Mode = "identity:classic-liu-hong-activation-fixture";
    private const string Recovery = "fixture:liu-hong-recovery";
    private const string Movement = "fixture:liu-hong-movement";
    private const string Gain = "fixture:liu-hong-gain";
    private const string Driver = "fixture:liu-hong-damage";
    private const string GiveReason = "skill-program.ol:yujue.yujue-give";

    public static void YujueDeclineAndPlayActivationKeepRealPaidChildrenOnce()
    {
        var (game, registry) = Create(humanOwner: true, children: true);
        Play(game, 0);
        Require(!E<ProgramYujueSlotPaidEvent>(game).Any() && Available(game),
            "The actual bundle must reach Play without a forced phase-start invocation.");
        Use(game, 0, Skill, Activation);
        Reach(game, p => Has(p, "yujue-decline"));
        Require(P(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("frame-id") == Root(game).Id.ToString()),
            "The unpaid decline and every slot choice identify the exact published frame.");
        WrongActor(game, 1);
        Answer(game, c => Action(c) == "yujue-decline");
        Play(game, 0);
        Require(Available(game) && !E<ProgramYujueSlotPaidEvent>(game).Any() &&
                !E<ProgramYujueInvokedEvent>(game).Any() && Slots(game, 0).All(slot =>
                    Capacity(game, 0, slot) == 1),
            "Unpaid cancellation changes no equipment slot and preserves the once-per-Play opportunity.");
        game = Cold(game, registry);
        var lion = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        Accept(game, new PlayCardCommand(0, lion, [], game.Revision, P(game)!.PromptId));
        Play(game, 0);
        var hp = game.State.Players[0].Hp;
        var maximum = game.State.Players[0].MaxHp;
        Use(game, 0, Skill, Activation);
        Reach(game, p => Has(p, "yujue-slot"));
        Answer(game, c => c.Parameters.GetValueOrDefault("slot") == nameof(EquipmentSlot.Armor));
        Reach(game, p => p.SkillPrompt?.SkillId == Recovery);
        var root = Root(game);
        Require(root.YujuePending is { Stage: YujuePendingStage.SlotPaid, TargetSeat: null } &&
                E<ProgramYujueSlotPaidEvent>(game) is [var paid] && paid.FrameId == root.Id &&
                paid.Source.SkillInstanceId == root.SkillInstanceId &&
                Capacity(game, 0, EquipmentSlot.Armor) == 0 &&
                game.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) &&
                    m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.slot-abolished") == 1 &&
                !E<ProgramYujueInvokedEvent>(game).Any(),
            "The real equipped Lion is paid once before recovery children or the later recipient choice.");
        WrongActor(game, 1);
        var replay = Cold(game, registry);
        var live = FinishPaid(game, registry, lion, hp, maximum, coldChildren: false);
        var cold = FinishPaid(replay, registry, lion, hp, maximum, coldChildren: true);
        Require(State(live) == State(cold),
            "Cold recovery at each real paid child reproduces all views, typed receipts, movements and accepted commands.");
    }

    public static void YujueNativeAiUsesAvailableSlotsAndTuxingExcludesSourcelessDamage()
    {
        // The four distinct generals leave exactly one native AI owner. Its
        // actual live seat is discovered after setup; no role weights, score
        // overrides or injected setup/state determine its actions.
        var (ai, aiRegistry) = Create(humanOwner: false, children: false);
        Play(ai, 1);
        var aiOwner = ai.State.Players.Single(p => p.GeneralId == Owner).Seat;
        Require(aiOwner != 1 && !ai.State.Players[aiOwner].IsHuman,
            "The unique actual Yujue general belongs to a native AI participant.");
        for (var invocation = 0; invocation < 3; invocation++)
        {
            End(ai, 1);
            Play(ai, 1);
            Require(E<ProgramYujueSlotPaidEvent>(ai).Length == invocation + 1 &&
                    E<ProgramYujueSlotPaidEvent>(ai).All(p => p.Source.OwnerSeat == aiOwner),
                "One normal four-player rotation lets the native AI pay one available slot and finish its real recipient flow.");
            var expected = new[] { EquipmentSlot.Treasure, EquipmentSlot.DefensiveHorse, EquipmentSlot.OffensiveHorse };
            Require(E<ProgramYujueSlotPaidEvent>(ai).Select(e => e.AbolishedSlot)
                    .SequenceEqual(expected.Take(invocation + 1).Select(s => s.ToString())),
                "Native AI filters abolished slots before choosing its next preferred available slot.");
            if (invocation == 1) ai = Cold(ai, aiRegistry);
        }
        Require(E<ProgramYujueInvokedEvent>(ai).Length == 3 &&
                E<ProgramYujueSlotPaidEvent>(ai).Select(e => e.AbolishedSlot).Distinct().Count() == 3,
            "Native turns never repay an already abolished slot.");

        var (game, registry) = Create(humanOwner: true, children: false);
        Play(game, 0);
        var beforeMaximum = game.State.Players[0].MaxHp;
        foreach (var slot in Slots(game, 0))
        {
            Use(game, 0, Skill, Activation);
            Reach(game, p => Has(p, "yujue-slot"));
            Answer(game, c => c.Parameters.GetValueOrDefault("slot") == slot.ToString());
            Reach(game, p => Has(p, "yujue-target"));
            Answer(game, c => c.Targets.SequenceEqual([1]));
            Reach(game, p => Has(p, "yujue-give"));
            Answer(game, c => c.Cards.Count == 1);
            Play(game, 0);
            Require(!Available(game), "A paid activation consumes exactly this real Play phase's quota.");
            if (slot != EquipmentSlot.Treasure) { End(game, 0); Play(game, 0); }
        }
        Require(Slots(game, 0).All(slot => Capacity(game, 0, slot) == 0) &&
                E<ProgramYujueSlotPaidEvent>(game).Length == 5 && E<ProgramYujueInvokedEvent>(game).Length == 5 &&
                game.State.Players[0].MaxHp == beforeMaximum + 1,
            "Five actual abolitions each add one maximum HP, then the last removes four once.");
        var armed = E<ProgramGameDamageBonusArmedEvent>(game).Single();
        Require(armed.SkillId == "ol:tuxing" && armed.BindingId == "tuxing-arm-catchup" &&
                Grants(game, 0).Any(grant => grant.IsEnabled &&
                    grant.SkillId == "ol:tuxing" && grant.SkillInstanceId == armed.SkillInstanceId) &&
                E<MaximumHpChangedEvent>(game).All(e => e.SkillId == "ol:tuxing"),
            "Maximum HP and the persistent damage bonus attribute to the actual Tuxing runtime instance and binding.");
        game = Cold(game, registry);
        var hpBefore = game.State.Players[0].Hp;
        Use(game, 0, Driver, "source-less"); Play(game, 0);
        var less = E<DamageRequestedEvent>(game).Single(e => e.SourceLess);
        Require(less.Amount == 1 && E<DamageAppliedEvent>(game).Single(e => e.SourceLess).Amount == 1 &&
                game.State.Players[0].Hp == hpBefore - 1 &&
                !E<ProgramCardDamageModifiedEvent>(game).Any(e => e.ResolutionId == less.ResolutionId),
            "A source-less owner damage cost retains its true one point and never borrows the armed source seat's bonus.");
        Use(game, 0, Driver, "other", [1]); Play(game, 0);
        var hit = E<DamageAppliedEvent>(game).Single(e => !e.SourceLess);
        Require(hit.SourceSeat == 0 && hit.TargetSeat == 1 && hit.Amount == 2 &&
                E<ProgramCardDamageModifiedEvent>(game) is [var bonus] && bonus.Source.SkillId == armed.SkillId &&
                bonus.Source.BindingId == armed.BindingId && bonus.Source.SkillInstanceId == armed.SkillInstanceId &&
                E<ProgramGameDamageBonusArmedEvent>(game).Length == 1,
            "Real owner-sourced damage gains one point from the original Tuxing instance without rearming on cold replay.");
        _ = Cold(game, registry);
    }

    private static GameEngine FinishPaid(GameEngine game, ContentRegistry registry, int lion,
        int hp, int maximum, bool coldChildren)
    {
        Reach(game, p => p.SkillPrompt?.SkillId == Movement);
        Require(Root(game).YujuePending is { Stage: YujuePendingStage.SlotPaid } &&
                !E<ProgramYujueInvokedEvent>(game).Any(), "All real paid equipment children precede giving or granting.");
        if (coldChildren) game = Cold(game, registry);
        Reach(game, p => Has(p, "yujue-target"));
        Require(P(game)!.PlayerSeat == 0 && game.State.Players[0].Hp == hp + 2 &&
                game.State.Players[0].MaxHp == maximum + 1,
            "Native Lion removal and Tuxing recovery both finish before the actual owner chooses a recipient.");
        Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => Has(p, "yujue-give"));
        Require(P(game) is { PlayerSeat: 1, IsPrivate: true } &&
                game.CreateSnapshot(0).PendingDecision is null &&
                P(game)!.Choices.All(c => c.Cards.Count == 1 && game.CreateSnapshot(1).Players[1].Hand.Any(h => h.Id == c.Cards[0])),
            "Only the actual giver sees its private hand-card payment choices.");
        WrongActor(game, 0);
        var given = P(game)!.Choices.First().Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([given]));
        Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        Require(Root(game).YujuePending is { Stage: YujuePendingStage.GiftPaid, GivenCardId: var id } && id == given &&
                E<ProgramYujueGiftPaidEvent>(game) is [var gift] && gift.GivenCardId == given &&
                !E<ProgramYujueInvokedEvent>(game).Any() &&
                !Grants(game, 1).Any(g => g.IsEnabled && g.SkillId == "ol:zhihu"),
            "The gift moves once and pauses in its owning receipt before acquired Zhihu is granted.");
        if (coldChildren) game = Cold(game, registry);
        Play(game, 0);
        Require(E<ProgramYujueSlotPaidEvent>(game).Length == 1 && E<ProgramYujueGiftPaidEvent>(game).Length == 1 &&
                E<ProgramYujueInvokedEvent>(game) is [var done] && done.GivenCardId == given && done.TargetSeat == 1 &&
                game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1) &&
                    m.To == CardLocation.Hand(0) && m.Reason.Value == GiveReason) == 1 &&
                game.CardMovements.Count(m => m.CardId == lion && m.Reason.Value == "equipment.slot-abolished") == 1 &&
                Grants(game, 1).Count(g => g.IsEnabled && g.SkillId == "ol:zhihu") == 1 &&
                !Available(game), "Paid children and cold replay complete one cost, one gift, one grant and one phase quota.");
        return game;
    }

    private static EquipmentSlot[] Slots(GameEngine game, int seat) =>
        [EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.OffensiveHorse, EquipmentSlot.DefensiveHorse, EquipmentSlot.Treasure];
    private static int Capacity(GameEngine game, int seat, EquipmentSlot slot) =>
        game.CreateSnapshot(seat).Players[seat].EquipmentSlotCapacities?.GetValueOrDefault(slot) ?? 1;
    // Read trusted ownership solely for attribution; setup and mutations remain
    // normal commands, with no injected skills, slots, HP or AI state.
    private static IReadOnlyList<SkillGrant> Grants(GameEngine game, int seat) =>
        ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(game)!)[seat].SkillGrants.Grants;
    private static bool Available(GameEngine game) => game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill && a.ProgramActivationId == Activation);
    private static ProgramSkillFrame Root(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
    private static T[] E<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => Action(c) == action);
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, game.Revision)); }
    private static void Use(GameEngine game, int owner, string skill, string activation, IReadOnlyList<int>? targets = null) =>
        Accept(game, new UseProgramSkillCommand(owner, skill, activation, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void End(GameEngine game, int seat) => Accept(game, new EndPlayPhaseCommand(seat, game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game, int seat) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == seat);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++)
        {
            if (P(game) is { } p && predicate(p)) return;
            if (P(game) is { } child && child.SkillPrompt?.SkillId is Recovery or Movement or Gain)
                Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (P(game) is { } giver && giver.PlayerSeat == game.State.HumanSeat && Has(giver, "yujue-give"))
                Answer(game, c => c.Cards.Count == 1);
            else
            {
                Require(P(game)?.PlayerSeat != game.State.HumanSeat,
                    "Unexpected human boundary in the bounded Liu Hong fixture: " + JsonSerializer.Serialize(P(game)));
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
        }
        throw new InvalidOperationException("Liu Hong fixture exceeded its bounded real command prefix: " + JsonSerializer.Serialize(P(game)));
    }
    private static void WrongActor(GameEngine game, int wrong)
    {
        var p = P(game)!; var before = State(game);
        Require(p.PlayerSeat != wrong && !game.Submit(new AnswerPromptCommand(wrong, p.PromptId,
                p.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A different actor cannot answer or mutate the exact published private/public choice.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real Liu Hong command."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(cold) == State(game), "Cold accepted-command replay preserves all exact paid receipts, views, facts and native movements."); return cold; }
    private static (GameEngine, ContentRegistry) Create(bool humanOwner, bool children)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(children, humanOwner));
        var human = humanOwner ? 0 : 1;
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = human,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(human, humanOwner ? Owner : Peer, game.Revision, P(game)!.PromptId));
        return (game, registry);
    }
    private sealed class Fixture(bool children, bool humanOwner) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("liu-hong-activation-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Read(string suffix)
            { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-liu-hong." + suffix) ??
                    throw new InvalidOperationException("Missing actual embedded Liu Hong bundle.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json"));
            foreach (var (id, program) in actual.Programs)
                builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                    { Program = program, ProgramPresentation = actual.Presentations[id] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Movement}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["equipment.slot-abolished"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"given","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{GiveReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                  {"id":"other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}
                """;
            var labels = new[] { Recovery, Movement, Gain, Driver }.ToDictionary(id => id, id => new
            { name = id, description = "真实付款与来源归因子窗", optionLabels = id == Driver ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } });
            var fixtures = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixtures.Programs)
                builder.AddSkill(new(id, id, "真实共享行为") { Program = program, ProgramPresentation = fixtures.Presentations[id] });
            builder.AddCard(new("fixture:liu-hong-lion", "白银狮子", "装备牌", "真实装备栏成本", CardKind.SilverLion));
            var ownerSkills = new List<string> { "ol:tuxing" };
            if (humanOwner) ownerSkills.Add(Driver);
            if (children) ownerSkills.AddRange([Recovery, Movement, Gain]);
            builder.AddGeneral(new(Owner, "实际鬻爵本人", "supporter", Skill, "qun", 12, ownerSkills)
                { InitialHp = 2 });
            // The damage driver is supplied only to the human-owner variant,
            // never to the native AI recipient-selection fixture.
            builder.AddGeneral(new(Peer, "实际交牌参与者", "supporter", "standard:none", "wei", 12));
            var quiet = new[] { "fixture:liu-hong-quiet-2", "fixture:liu-hong-quiet-3" };
            foreach (var id in quiet)
                builder.AddGeneral(new(id, "固定普通参与者", "supporter", "standard:none", "wei", 12));
            builder.AddDeck(new("fixture:liu-hong-deck", "固定小实体牌堆", 6, 0, [])
                { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(
                    children ? "fixture:liu-hong-lion" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "实际出牌阶段发动", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:liu-hong-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, Peer, .. quiet]));
        }
    }
}
