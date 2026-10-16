using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryXuHuangChecks
{
    private const string Duanliang = "boundary:duanliang-current";
    private const string Jiezi = "boundary:jiezi-current";
    private const string Driver = "fixture:xh-driver";
    private const string Skip = "fixture:xh-skip";
    private const string Start = "fixture:xh-start";
    private const string End = "fixture:xh-end";
    private const string Gain = "fixture:xh-gain";
    private const string Hp = "fixture:xh-hp";
    private const string Mode = "identity:classic-boundary-xu-huang-fixture";
    private static PlayerMarkerKind Zi => Enum.Parse<PlayerMarkerKind>("Zi");

    public static void DuanliangDistanceTracksActualWholeTurnDamageAndPaidEquipment()
    {
        var (game, registry) = Create(silver: true); ReachPlay(game);
        Require(game.GetCombatDistance(0, 2) == 2 && Supplies(game, 2).Count > 0,
            "Before actual damage, black HE materials can create a real distance-two SupplyShortage action.");
        Use(game, "hp-only"); ReachPlay(game);
        Require(Supplies(game, 2).Count > 0 && !game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0),
            "A real HP-loss command does not falsely count as damage dealt and leaves no-distance qualification active.");
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip); Play(game, equip); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var conversion = Supplies(game, 1).Single(a => a.CardId == armor);
        Cold(game, registry); Play(game, conversion); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Hp);
        var paid = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(paid.CardKind == CardKind.SupplyShortage && paid.PhysicalCardIds is { } physical && physical.SequenceEqual([armor]) &&
            paid.Action!.PhysicalCards is [var material] && material.CardId == armor && material.From == CardLocation.Equipment(0) &&
            paid.Action.ConversionChain.Any(c => c.SkillId == Duanliang) && game.State.Players[0].Hp > 3,
            "The real equipment conversion freezes its original material and exact source before its loss-of-SilverLion recovery child.");
        Private(game, 0); Cold(game, registry); Continue(game); ReachPlay(game);
        Require(game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
            game.CreateSnapshot(0).Players[1].Judgment.Any(c => c.Id == armor), "The paid use returns once to its actual delayed-trick placement.");
        Use(game, "damage", targets: [3]); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.Amount == 1) &&
            Supplies(game, 2).Count == 0 && Supplies(game, 3).Count > 0,
            "After real damage, the new policy withdraws distance two and preserves ordinary distance one; it does not inherit classic Duanliang's +1.");
        var turn = game.State.TurnNumber; EndPlay(game); ReachPlay(game);
        Require(game.State.TurnNumber > turn && Supplies(game, 2).Count > 0 &&
            game.Events.Select(e => e.Payload).OfType<DrawPhaseSkippedEvent>().Any(e => e.OwnerSeat == 1 && !e.IsExtra) &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Jiezi && e.OwnerSeat == 0 && e.Window == SkillProgramTriggerWindow.DrawPhaseSkipped && !e.Activated),
            "The next actual turn restores no-distance from the new public damage boundary, without resetting a private Boolean source sidecar."); Cold(game, registry);
    }

    private static IReadOnlyList<LegalAction> Supplies(GameEngine g, int seat) => g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.SupplyShortage && a.TargetSeat == seat).ToArray();
    private static GameSnapshot[] Decisions(GameEngine g) => Enumerable.Range(0, 4).Select(viewer => g.CreateSnapshot(viewer)).ToArray();
    private static PendingDecision? Prompt(GameEngine g) => Decisions(g).Select(v => v.PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Select(GameEngine g, int seat)
    { Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets")); Answer(g, c => c.Targets.SequenceEqual([seat])); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Play(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, Prompt(g)!.PromptId, action.PlayedCardKind)
    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, Prompt(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachUntil(g, () => Prompt(g) is { } p && predicate(p));
    private static void ReachUntil(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 280; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed Xu Huang fixture missed its real boundary: " + JsonSerializer.Serialize(Prompt(g))); }
    private static void Advance(GameEngine g)
    {
        var p = Prompt(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-own-card-discard")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "choose-own-card-discard");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Start or End or Gain or Hp) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Private(GameEngine g, int owner)
    { Require(Prompt(g) is { IsPrivate: true }, "The published skill choice is private to its actual chooser."); foreach (var seat in Enumerable.Range(0, 4).Where(s => s != owner)) Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[owner].Hand.Count == 0, "Other viewers do not receive private hand entities or choices."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), g.CardMovements, Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)),
        "Cold replay retains four private projections, exact stage/observer/payment ownership, public marker policies and actual materials.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool silver = false, bool existingZi = false, bool qiaobian = false, bool renegade = false, bool skipCardCost = false, bool newCapability = true, bool normalGainRecovery = false)
    {
        var packages = new List<IGameContentPackage> { new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage() };
        if (newCapability) packages.Add(new StandardClassicGeneralPackage());
        packages.Add(new Fixture(silver, existingZi, qiaobian, skipCardCost, newCapability, normalGainRecovery));
        var registry = ContentRegistry.Build(packages.ToArray());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = renegade ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:xh-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }
    private sealed class Fixture(bool silver, bool existingZi, bool qiaobian, bool skipCardCost, bool newCapability, bool normalGainRecovery) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-xu-huang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var skipCost = skipCardCost ? "{\"op\":\"chooseOwnCardDiscard\",\"target\":\"owner\",\"zones\":[\"hand\"]}," : "";
            var normalGain = normalGainRecovery ? $$"""
                ,{"id":"normal-gained","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":[{{JsonSerializer.Serialize(CardMoveReasons.Draw.Value)}}],"effects":[{"op":"chooseOption","target":"owner","resultBind":"normal-seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}
                """ : "";
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"hp-only","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"empty-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":64}]},
                {"id":"extra-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
                {"id":"{{Skip}}","revision":1,"triggers":[{"id":"skip-draw","window":"turnStartBeforeNormalFlow","subject":"owner","priority":300,"optional":true,"effects":[{{skipCost}}{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]},
                {"id":"fixture:xh-quiet","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash","silverLion"],"actionTypes":["use"]}]}]},
                {"id":"fixture:xh-initial-zi","revision":1,"triggers":[{"id":"one-marker","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"owner"},"marker":"zi","amount":1}]}]},
                {"id":"{{Start}}","revision":1,"triggers":[{"id":"starting","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{End}}","revision":1,"triggers":[{"id":"ended","window":"drawPhaseEnded","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.extra-draw-phase.draw","program.least-hand-marker.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}{{normalGain}}]},
                {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令准备", description = "真实伤害/HP损失/AI弃牌/额外回合" }, [Skip] = new { name = "实际跳过摸牌", description = "旧通用阶段替代" },
                    ["fixture:xh-quiet"] = new { name = "安静回合", description = "禁止固定牌堆的AI用牌" }, ["fixture:xh-initial-zi"] = new { name = "既有标记", description = "真实游戏开始独立标记" },
                    [Start] = Label("实际摸牌开始"), [End] = Label("实际摸牌结束"), [Gain] = Label("实际获得牌"), [Hp] = Label("实际回复") } }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "程序观察夹具") { Program = catalog.Programs[id] });
            if (!newCapability) { builder.AddSkill(new(Duanliang, "旧兼容夹具主技能", "无程序")); builder.AddSkill(new(Jiezi, "旧兼容夹具副技能", "无程序")); }
            builder.AddSkill(new("fixture:xh-selection", "固定选将", "无运行程序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var ownerSkills = new List<string> { Jiezi, Driver, Skip, Start, End, Gain, Hp };
            if (existingZi) ownerSkills.Add("fixture:xh-initial-zi"); if (qiaobian) ownerSkills.Add("boundary:qiaobian");
            builder.AddGeneral(new("fixture:xh-owner", "当前断粮真实程序", "supporter", Duanliang, "wei", 4, ownerSkills.ToArray()) { InitialHp = silver ? 3 : 4 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:xh-target-{i}", "真实目标", "supporter", "fixture:xh-selection", "shu", 8,
                ["fixture:xh-quiet", Gain, Start, End]) { InitialHp = 8 });
            builder.AddDeck(new("fixture:xh-deck", "固定实际黑实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(silver ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实断粮截辎", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:xh-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xh-owner", "fixture:xh-target-1", "fixture:xh-target-2", "fixture:xh-target-3"]));
        }
        private static object Label(string name) => new { name, description = "实际阶段/移牌/回复孩子观察", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
