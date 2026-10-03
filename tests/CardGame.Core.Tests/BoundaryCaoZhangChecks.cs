using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaoZhangChecks
{
    private const string Skill = "boundary:jiangchi";
    private const string Driver = "fixture:cz-driver";
    private const string Cost = "fixture:cz-recast-cost";
    private const string Gain = "fixture:cz-recast-gain";
    private const string Recovery = "fixture:cz-recovery";
    private const string Mode = "identity:classic-boundary-cao-zhang-fixture";

    public static void DrawMoreReducesUseLimitButKeepsRealSlashResponse()
    {
        AssertDrawEndedChoiceGroupContract();
        var (game, registry) = Create(); ReachJiangchi(game);
        var hand = game.State.Players[0].HandCount;
        Choose(game, "draw-more"); ReachPlay(game);
        Require(game.State.Players[0].HandCount == hand + 1 &&
            !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            Grants(game).Single().Modifier is { Query: SkillRuleQuery.SlashLimit, Amount: -1 } &&
            game.State.Players[0].TurnHandLimitCardKindExemptions is [var policy] &&
            policy.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]),
            "The completed normal draw offers one extra draw, a signed -1 allowance and a dynamic three-kind hand exemption.");
        Replay(game, registry);
        var slash = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash).Id;
        var before = State(game);
        Require(!game.Submit(new PlayCardCommand(0, slash, [2], game.Revision, Prompt(game)!.PromptId)).Accepted &&
            State(game) == before, "Zero remaining Slash allowance rejects an actual direct use atomically.");
        Use(game, "duel", targets: [1]);
        Reach(game, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0);
        var response = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "slash" && c.Cards.Count == 1);
        Require(response.Cards.Contains(slash) || game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == response.Cards[0] && c.Kind == CardKind.Slash),
            "The selected actor's real Duel still publishes a physical Slash response to the human owner.");
        Replay(game, registry); Answer(game, c => c.Id == response.Id); ReachPlay(game);
        Require(game.CardMovements.Any(m => m.CardId == response.Cards[0] && m.From == CardLocation.Hand(0)) &&
            !game.Events.Any(e => e.Payload is CardActionProhibitionGrantedEvent issued && issued.Prohibition.Source.SkillId == Skill),
            "The signed use allowance pays the actual response and issues no blanket response prohibition.");
        Use(game, "plus-one"); ReachPlay(game);
        Require(game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "An independently issued +1 modifier composes with the actual -1 allowance.");
        Replay(game, registry);
    }

    public static void DynamicSlashExemptionTracksGainLossAndExpiresBeforeNextDiscard()
    {
        var (game, registry) = Create(); ReachJiangchi(game); Choose(game, "draw-more"); ReachPlay(game);
        var initial = game.CreateSnapshot(0).Players[0];
        var policy = initial.TurnHandLimitCardKindExemptions!.Single();
        Use(game, "gain"); ReachPlay(game);
        Require(game.State.Players[0].HandCount == initial.HandCount + 2 &&
            game.State.Players[0].TurnHandLimitCardKindExemptions!.Single().GrantSequence == policy.GrantSequence,
            "Later real Slash draws use the same issued kind policy without adding another policy or numerical hand limit.");
        var removed = game.CreateSnapshot(0).Players[0].Hand.First().Id;
        Use(game, "discard-one", cards: [removed]); ReachPlay(game);
        Require(!game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == removed) &&
            game.State.Players[0].HandCount == initial.HandCount + 1,
            "A physical Slash discarded earlier leaves the dynamic hand population immediately.");
        for (var viewer = 1; viewer < 4; viewer++)
            Require(game.CreateSnapshot(viewer).Players[0].Hand.Count == 0 &&
                game.CreateSnapshot(viewer).Players[0].TurnHandLimitCardKindExemptions is [var shown] &&
                shown.CardKinds.SequenceEqual(policy.CardKinds),
                "Public policy kinds never publish hidden physical identities to another viewer.");
        Replay(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        for (var step = 0; step < 70 && !game.Events.Any(e => e.Payload is TurnEndedEvent { ActorSeat: 0, TurnNumber: 1 }); step++) Advance(game);
        Require(game.Events.Any(e => e.Payload is TurnEndedEvent { ActorSeat: 0, TurnNumber: 1 }) &&
            !game.CardMovements.Any(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard) &&
            game.State.Players[0].TurnHandLimitCardKindExemptions is null,
            "Every currently held Slash is exempt at discard, and the policy expires at the actual turn boundary.");
        Replay(game, registry);
        ReachJiangchi(game); Skip(game); ReachPlay(game);
        Require(game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Declining the next actual draw-ended choice does not reissue the previous negative allowance.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0);
        var discard = Prompt(game)!;
        Require(discard.RequiredCardCount > 0 && discard.ValidCardIds.Count == game.State.Players[0].HandCount &&
            discard.ValidCardIds.All(id => game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash)),
            "The expired policy returns actual Slash entities to the central discard eligibility and published final subset contract.");
        Replay(game, registry);
        var before = State(game);
        Require(!game.Submit(new DiscardCardsCommand(0, [-1], discard.PromptId, game.Revision)).Accepted && State(game) == before,
            "An unpublished discard subset rejects without changing the command prefix or private hand.");
        Accept(game, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision));
        Replay(game, registry);
    }

    public static void HandRecastWaitsForCostAndDrawChildrenBeforeTwoDistantSlashes()
    {
        var (game, registry) = Create(observers: true); ReachJiangchi(game);
        var hand = game.State.Players[0].HandCount;
        Choose(game, "recast-assault"); ReachOwnedSelection(game);
        var selection = Prompt(game)!;
        var card = selection.Choices.First(c => c.Cards.Count == 1).Cards[0];
        AssertPrivateSelection(game); Replay(game, registry); Reject(game);
        Answer(game, c => c.Cards.SequenceEqual([card]));
        Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        var paid = RecastOwner(game);
        Require(paid.BoundCardRecast is { DrawApplied: false, CardId: var paidCard } && paidCard == card &&
            game.State.Players[0].HandCount == hand - 1 && Grants(game).Count == 0,
            "The committed hand payment owns the cost observer while its recast draw and attack grants remain unpaid.");
        AssertExactMovementReturn(game, paid, card, CardLocation.Hand(0), CardLocation.DiscardPile);
        Replay(game, registry); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        Require(RecastOwner(game).BoundCardRecast is { DrawApplied: true, DrawCount: 1 } &&
            game.State.Players[0].HandCount == hand && Grants(game).Count == 0 && Recasts(game).Count == 1,
            "The actual replacement draw and recast fact precede the reward observer; neither Slash grant has run.");
        Replay(game, registry); Continue(game); ReachPlay(game);
        Require(Grants(game).Count == 2 && game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) &&
            m.Reason == CardMoveReasons.RecastDiscard) == 1 && Recasts(game).Single().DrawCount == 1,
            "Both typed child returns resume one payment, one recast fact and the two final turn modifiers.");
        for (var attack = 0; attack < 2; attack++)
        {
            var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]));
            Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, Prompt(game)!.PromptId, slash.PlayedCardKind));
            ReachPlay(game); Replay(game, registry);
        }
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2) == 2 &&
            !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The paid branch permits two actual distance-two Slashes and then exhausts the new finite allowance.");
    }

    public static void EquipmentRecastDrainsSilverLionRecoveryAndMovementBeforeGrants()
    {
        var (game, registry) = Create(equipment: true, observers: true); ReachJiangchi(game); Skip(game); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)); ReachJiangchi(game);
        var hp = game.State.Players[0].Hp; var hand = game.State.Players[0].HandCount;
        Require(hp < game.State.Players[0].MaxHp, "The real equipped Silver Lion belongs to an injured owner on the next actual draw-ended boundary.");
        Choose(game, "recast-assault"); ReachOwnedSelection(game); AssertPrivateSelection(game);
        Require(Prompt(game)!.Choices.Any(c => c.Cards.SequenceEqual([armor])),
            "The frozen owned HE selection includes the actual equipped armor as well as hand cards.");
        Replay(game, registry); Answer(game, c => c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.SkillPrompt?.SkillId == Recovery);
        Require(RecastOwner(game).BoundCardRecast is { DrawApplied: false } && game.State.Players[0].Hp == hp + 1 &&
            game.State.Players[0].HandCount == hand && Grants(game).Count == 0 &&
            !game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == armor),
            "Silver Lion's actual recovery and HP observer run after one equipment cost and before the recast draw or grants.");
        Replay(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        AssertExactMovementReturn(game, RecastOwner(game), armor, CardLocation.Equipment(0), CardLocation.DiscardPile);
        Require(game.State.Players[0].HandCount == hand && Grants(game).Count == 0,
            "The equipment-loss observer finishes before the one-card replacement draw.");
        Replay(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        Require(game.State.Players[0].HandCount == hand + 1 && Grants(game).Count == 0,
            "The drawn entity commits before its own gain observer, and attack grants still await that child.");
        Replay(game, registry); Continue(game); ReachPlay(game);
        Require(Grants(game).Count == 2 && Recasts(game).Single().CardId == armor &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
            game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 0 && e.Reason == CardMoveReasons.RecastDiscard && e.RecoveredAmount == 1) == 1,
            "Cold continuation retains the exact equipped payment and publishes one Silver Lion removal and one recast.");
        Replay(game, registry);
    }

    private static IReadOnlyList<TurnRuleModifierGrantedEvent> Grants(GameEngine game) => game.Events.Select(e => e.Payload)
        .OfType<TurnRuleModifierGrantedEvent>().Where(e => e.Modifier.Source.SkillId == Skill && e.Modifier.Source.OwnerSeat == 0).ToArray();
    private static IReadOnlyList<CardRecastEvent> Recasts(GameEngine game) => game.Events.Select(e => e.Payload)
        .OfType<CardRecastEvent>().Where(e => e.ActorSeat == 0).ToArray();
    private static void AssertDrawEndedChoiceGroupContract()
    {
        SkillProgram Load(string first, string second) => SkillProgramCatalog.Load(JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
            skills = new[] { new { id = "fixture:cz-group", revision = 1, triggers = new[]
            {
                new { id = "one", window = first, subject = "owner", optional = true, choiceGroup = "mode",
                    effects = new[] { new { op = "draw", target = "owner", amount = 1 } } },
                new { id = "two", window = second, subject = "owner", optional = true, choiceGroup = "mode",
                    effects = new[] { new { op = "draw", target = "owner", amount = 1 } } }
            } } }
        }), JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
        { ["fixture:cz-group"] = new { name = "摸牌结束分支", description = "共享选择组窗口合同",
            triggerChoices = new Dictionary<string, string> { ["one"] = "第一项", ["two"] = "第二项" } } } })).Programs["fixture:cz-group"];
        Require(Load("drawPhaseEnded", "drawPhaseEnded").Triggers.Count == 2,
            "Both parser gates accept a same-window DrawPhaseEnded choice group.");
        foreach (var windows in new[] { ("afterNormalDraw", "afterNormalDraw"),
            ("turnEnding", "turnEnding"), ("drawPhaseEnded", "drawPhaseStarting") })
        {
            var rejected = false;
            try { Load(windows.Item1, windows.Item2); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The additive choice-group capability keeps unsupported and mixed windows invalid.");
        }
    }
    private static ProgramSkillFrame RecastOwner(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>()
        .Single(f => f.SkillId == Skill && f.OwnerSeat == 0 && f.BoundCardRecast is not null);
    private static void AssertExactMovementReturn(GameEngine game, ProgramSkillFrame paid, int card, CardLocation from, CardLocation to)
    {
        var window = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.ResumeProgramFrameId == paid.Id);
        Require(window.Batch.ParentFrameId == paid.Id && window.Batch.AwaitingProgramFrameId is null &&
            paid.PendingMovementContinuation is null && window.Batch.OriginSkillId == Skill &&
            window.Batch.OriginSkillInstanceId == paid.SkillInstanceId &&
            window.Batch.Movements.Any(m => m.CardId == card && m.From == from && m.To == to && m.Reason == CardMoveReasons.RecastDiscard),
            "The actual cost movement carries a typed return to its exact paid instruction and issuing instance.");
    }
    private static void AssertPrivateSelection(GameEngine game)
    {
        Require(Prompt(game)!.PlayerSeat == 0 && Prompt(game)!.IsPrivate, "Owned HE recast costs are private to their chooser.");
        for (var viewer = 1; viewer < 4; viewer++)
            Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).Players[0].Hand.Count == 0,
                "Another player receives neither the owned-card choices nor private hand entities.");
    }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool observers = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, observers));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:cz-owner", game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Jiangchi(PendingDecision p) => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void ReachJiangchi(GameEngine game) => Reach(game, Jiangchi);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void ReachOwnedSelection(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
    private static void Choose(GameEngine game, string binding) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Skip(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], game.Revision, Prompt(game)!.PromptId));
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 160; step++) { var p = Prompt(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Cao Zhang fixture did not reach boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(game);
        else if (p?.SkillPrompt?.SkillId is Cost or Gain or Recovery) Continue(game);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command)
    { var r = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && State(game) == before, "An unpublished choice cannot pay a recast or advance its owning cursor."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "All four private views, owning receipt stages, real movement facts and commands cold-restore exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool observers) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-cao-zhang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"gain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                  {"id":"discard-one","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                  {"id":"plus-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":1}]},
                  {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
                 {"id":"fixture:cz-quiet","revision":1,"triggers":[{"id":"quiet-turn","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Cost}}","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["card.recast.discard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令驱动", description = "得牌、失牌和实际决斗响应" },
                    ["fixture:cz-quiet"] = new { name = "安静回合", description = "固定夹具不发动普通杀" },
                    [Cost] = new { name = "重铸失牌观察", description = "支付后子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Gain] = new { name = "重铸得牌观察", description = "替换摸牌后子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Recovery] = new { name = "真实回复观察", description = "白银狮子回复后子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var id in new[] { Driver, "fixture:cz-quiet", Cost, Gain, Recovery }) builder.AddSkill(new(id, id, "正式夹具程序") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:cz-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:cz-owner", "界曹彰机制", "supporter", Skill, "wei", 4,
                observers ? [Driver, Cost, Gain, Recovery] : [Driver]) { InitialHp = 2 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:cz-target-{i}", "固定目标", "supporter", "fixture:cz-selection", "shu", 8, ["fixture:cz-quiet"]));
            builder.AddDeck(new("fixture:cz-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实界曹彰", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:cz-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:cz-owner", "fixture:cz-target-1", "fixture:cz-target-2", "fixture:cz-target-3"]));
        }
    }
}
