using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class PrivateWhirlChecks
{
    private const string Owner = "fixture:whirl-owner", Driver = "fixture:whirl-driver", Quiet = "fixture:whirl-quiet", Mode = "identity:classic-whirl";
    public static void PrivateFiveDepositsColdViewsAndResidualDrawCount()
    {
        var (g, registry) = Start(); Deposit(g, 5, registry); NextPlay(g); var hand = V(g, 0).Hand.Count;
        Need(V(g, 0).PrivateWhirlPiles is [var owned] && owned.Count == 5 && owned.Cards!.Count == 5 && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].PrivateWhirlPiles is [{ Count: 5, Cards: null }]), "Owner sees actual five deposited entities while every foreign view sees only five hidden Whirl.");
        var hp = V(g, 1).Hp; NativeSlash(g, 1); Reach(g, () => WhirlStage(g, PrivateWhirlStage.SelectRemove));
        var p = P(g)!; var before = State(g); Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && before == State(g), "A foreign viewer cannot pay private Whirl removal.");
        g = Cold(g, registry); Answer(g, _ => true); Play(g);
        var first = E<PrivateWhirlSegmentIssuedEvent>(g).Where(e => e.ActionId is not null).ToArray();
        Need(first.Select(e => e.Stage).SequenceEqual([PrivateWhirlStage.RemovalChildren, PrivateWhirlStage.FirstDrawChildren, PrivateWhirlStage.ExtraDrawChildren]) && first.Last() is { Count: 4, RemainingCount: 4, NotUniqueMaximum: true } && V(g, 0).Hand.Count == hand + 4 && V(g, 1).Hp == hp - 1 && V(g, 0).PrivateWhirlPiles![0].Count == 4, "Real use removes one, draws one, then draws remaining four based on current hand comparison.");
        NativeSlash(g, 1); Reach(g, () => WhirlStage(g, PrivateWhirlStage.SelectRemove)); Answer(g, _ => true); Play(g);
        Need(V(g, 0).Hand.Count == hand + 4 && V(g, 0).PrivateWhirlPiles![0].Count == 3 && E<PrivateWhirlSegmentIssuedEvent>(g).Count(e => e.Stage == PrivateWhirlStage.ExtraDrawChildren) == 1, "A later real use after becoming unique maximum draws only its first card and keeps three Whirl."); _ = Cold(g, registry);
    }
    public static void ActualNextPlayEndingExpiresPrivateEntities()
    {
        var (g, registry) = Start(); Deposit(g, 3, registry); Need(V(g, 0).PrivateWhirlPiles![0].Count == 3, "Deposit survives the already-ended Play phase."); NextPlay(g); var cards = V(g, 0).PrivateWhirlPiles![0].Cards!.Select(c => c.Id).ToArray(); g = Cold(g, registry); End(g);
        Reach(g, () => E<PrivateWhirlRemovedEvent>(g).Count(e => e.Reason == "play-expired") == 3);
        Need(V(g, 0).PrivateWhirlPiles is null && cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.private-whirl.play-expired") == 1), "The first later actual own PlayEnding expires every exact private physical deposit once."); _ = Cold(g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool WhirlStage(GameEngine g, PrivateWhirlStage stage) => g.ResolutionStack.LastOrDefault() is ProgramSkillFrame { PrivateWhirl: { } r } && r.Stage == stage && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "private-whirl") == true;
    private static void Need(bool b, string reason) { if (!b) throw new InvalidOperationException(reason); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Whirl command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextPlay(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 0) >= 2);
    private static void NativeSlash(GameEngine g, int target) { var a = g.GetHumanLegalActions().First(a => a.PlayedCardKind is null or CardKind.Slash && a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target])); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
    private static void Deposit(GameEngine g, int count, ContentRegistry registry)
    {
        Play(g); End(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == "ol:zhouxuan" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, () => WhirlStage(g, PrivateWhirlStage.SelectDeposit));
        for (var i = 0; i < count; i++) { Answer(g, c => c.Parameters["branch"].StartsWith("add-", StringComparison.Ordinal)); if (i == 0) { var cold = Cold(g, registry); Need(State(cold) == State(g), "Unpaid private multiselection freezes across cold restore."); } }
        if (count == 5) Need(P(g)!.Choices.Count == 1 && P(g)!.Choices[0].Parameters["branch"] == "confirm", "The total pile cap five closes all additional hand selections."); Answer(g, c => c.Parameters["branch"] == "confirm"); Reach(g, () => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PrivateWhirl is not null));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 240; i++) { if (done()) return; var p = P(g); Need(g.State.Winner == Winner.None, "Small Whirl fixture ended before expected boundary.");
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("Small real Whirl fixture exceeded its command bound.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold replay preserves private entities, real action cursor and once-paid child stage."); return restored; }
    private static (GameEngine, ContentRegistry) Start(string child = "")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(child)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = child == "death" ? Role.Rebel : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry); Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private sealed class Fixture(string child) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("private-whirl-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog) { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) }); }
            string Embedded(string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-sp-zhang-he." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); } Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var definitions = new List<string> { $$"""{"id":"{{Driver}}","revision":1,"triggers":[{"id":"virtual","window":"playPhaseStarting","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]},{"id":"quota","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":8}]}]}""", $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""" };
            if (child is "loss" or "death") definitions.Add("""{"id":"fixture:whirl-child","revision":1,"triggers":[{"id":"paid-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.private-whirl.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
            if (child == "loss") definitions.Add("""{"id":"fixture:whirl-loss","revision":1,"triggers":[{"id":"actual-loss","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhouxuan"],"sourceBind":"standard:none"}]}]}""");
            if (child == "skip") definitions.Add("""{"id":"fixture:whirl-skip","revision":1,"triggers":[{"id":"skip-next-play","window":"afterNormalDraw","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""");
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生驱动", description = "真实零实体牌与次数" }, [Quiet] = new { name = "静默", description = "跳过其他出牌" } }; if (child is "loss" or "death") labels["fixture:whirl-child"] = new { name = "已付费子步骤", description = "失源与死亡", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }; if (child == "loss") labels["fixture:whirl-loss"] = new { name = "真实失源", description = "濒死回复后失去周旋" }; if (child == "skip") labels["fixture:whirl-skip"] = new { name = "真实跳过", description = "跳过下次出牌" };
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', definitions)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var skills = new List<string> { "ol:zhouxuan" }; if (child is "loss" or "death") skills.Add("fixture:whirl-child"); if (child == "loss") skills.Add("fixture:whirl-loss"); if (child == "skip") skills.Add("fixture:whirl-skip"); b.AddGeneral(new(Owner, "周旋真实拥有者", "supporter", Driver, "qun", 12, skills)); var peers = Enumerable.Range(1, 3).Select(i => $"fixture:whirl-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "真实静默其他角色", "supporter", Quiet, "wei", 12)); b.AddDeck(new("fixture:whirl-deck", "固定实际手牌", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray() }); b.AddMode(new(Mode, "周旋真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:whirl-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
