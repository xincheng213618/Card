using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundarySunQuanChecks
{
    private const string Driver = "fixture:sq-driver";
    private const string SourcePulse = "fixture:sq-source-pulse";

    public static void ZhihengSelectionAndPhaseQuota()
    {
        foreach (var all in new[] { false, true })
        {
            var (g, r) = Create();
            var before = g.CreateSnapshot(0).Players[0].HandCount;
            var cards = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).Take(all ? before : 1).ToArray();
            var original = State(g);
            Require(!g.Submit(new UseProgramSkillCommand(0, "boundary:zhiheng", "exchange-owned-cards", [], [], g.Revision, P(g)!.PromptId)).Accepted && State(g) == original,
                "A zero-card exchange is rejected without paying phase usage.");
            Accept(g, new UseProgramSkillCommand(0, "boundary:zhiheng", "exchange-owned-cards", cards, [], g.Revision, P(g)!.PromptId));
            Settle(g);
            Require(g.CreateSnapshot(0).Players[0].HandCount == before + (all ? 1 : 0),
                "The extra draw uses the frozen pre-payment all-hand selection; a partial selection draws only equal cards.");
            Require(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "boundary:zhiheng"), "Zhiheng is limited once in the actual Play phase.");
            Require(cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile) == 1), "Each selected physical cost reaches discard once.");
            Replay(g, r);
        }
    }

    public static void ProgramRecoveryAndRecoverToReplacement()
    {
        foreach (var activation in new[] { "recover", "recover-to" })
        foreach (var redirect in new[] { false, true })
        {
            var (g, r) = Create();
            var lord = Lord(g);
            var ownerHp = g.State.Players[0].Hp;
            var lordHp = g.State.Players[lord].Hp;
            var hand = g.State.Players[0].HandCount;
            Begin(g, activation);
            Reach(g, p => p.Kind == DecisionKind.RecoveryReplacement);
            var frame = g.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
            Require(frame.Attempt.TargetSeat == 0 && frame.Attempt.Amount == 2 && frame.ParentFrameId == frame.Return.ResumeFrameId &&
                frame.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && g.State.Players[0].Hp == ownerHp,
                "The exact program produces a two-point pending recovery before any original HP or recovery event.");
            Require(P(g)!.PlayerSeat == 0 && P(g)!.Choices.Where(c => c.Targets.Count != 0).All(c => c.Targets.SequenceEqual([lord])),
                "The recovering Wu player chooses; other non-lord owners cannot offer the same lord policy.");
            Require(g.CreateSnapshot(lord).PendingDecision is null, "A private recovering-player choice is not exposed to the lord viewer.");
            Replay(g, r); Reject(g);
            Answer(g, c => c.Parameters.GetValueOrDefault("recovery-action") == (redirect ? "redirect" : "keep"));
            Settle(g);
            Require(g.State.Players[0].Hp == (redirect ? ownerHp : ownerHp + 2) &&
                g.State.Players[lord].Hp == lordHp + (redirect ? 1 : 0) && g.State.Players[0].HandCount == hand + (redirect ? 1 : 0),
                "Redirect replaces the whole original recovery with exactly one lord HP and one provider draw; decline retains the original amount.");
            Require(g.Events.Select(e => e.Payload).OfType<RecoveryReplacementChosenEvent>().Count() == (redirect ? 1 : 0), "A committed choice emits one scalar replacement fact.");
            Replay(g, r);
        }
    }

    public static void SilverLionPaidQixiAndEquipmentReplacement()
    {
        foreach (var mode in new[] { "qixi", "silver" })
        {
            var (g, r) = Create(mode);
            var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
            Accept(g, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, g.Revision, P(g)!.PromptId)); Settle(g);
            var armor = g.CreateSnapshot(0).Players[0].Equipment.Single();
            var ownerHp = g.State.Players[0].Hp; var lord = Lord(g); var lordHp = g.State.Players[lord].Hp;
            var target = Enumerable.Range(1, 3).First(s => s != lord); var targetHand = g.State.Players[target].HandCount;
            var action = mode == "qixi" ? g.GetHumanLegalActions().First(a => a.CardId == armor.Id && a.PlayedCardKind == CardKind.Dismantlement && a.TargetSeat == target)
                : g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
            Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource });
            Reach(g, p => p.Kind == DecisionKind.RecoveryReplacement);
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Single();
            Require(parent.RecoveryPaidContinuation?.Kind == (mode == "qixi" ? RecoveryPaidCardUseKind.Trick : RecoveryPaidCardUseKind.EquipmentUse) &&
                g.State.Players[target].HandCount == targetHand && g.State.Players[0].Hp == ownerHp && g.CreateSnapshot(0).Players[0].Equipment.Count == 0,
                "A paid equipment conversion/replacement suspends before the original trick effect or new equipment enters.");
            Replay(g, r); Reject(g); Answer(g, c => c.Parameters.GetValueOrDefault("recovery-action") == "redirect");
            if (mode == "qixi")
            {
                Reach(g, p => p.Kind == DecisionKind.SelectTargetCard); Replay(g, r); Answer(g, _ => true);
            }
            Settle(g);
            Require(g.State.Players[0].Hp == ownerHp && g.State.Players[lord].Hp == lordHp + 1 &&
                g.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Equipment(0)) == 1,
                "The healing choice returns through the exact paid use; removed Silver Lion is never repaid.");
            Require(mode == "qixi" ? g.State.Players[target].HandCount == targetHand - 1 : g.CreateSnapshot(0).Players[0].Equipment.Single().Id == action.CardId,
                "The normal dismantlement or replacement equipment still resolves once after healing.");
            if (mode == "silver") Require(g.Events.Select(e => e.Payload).OfType<EquipmentChangedEvent>().Last().ReplacedCardId == armor.Id,
                "The paid replacement event retains the exact armor removed before the recovery child."); Replay(g, r);
        }
    }

    private static List<CharacterState> Players(GameEngine g) => (List<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!;
    private static int Lord(GameEngine g) => g.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void AnswerContinue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Begin(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) { Begin(g, id, targets); Settle(g); }
    private static void PrepareLord(GameEngine g) { for (var i = 0; i < 6 && g.State.Players[Lord(g)].Hp > 3; i++) Use(g, "hurt-other", [Lord(g)]); }
    private static void Settle(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p?.SkillPrompt?.SkillId is "fixture:sq-hp" or "fixture:sq-gain") AnswerContinue(g);
            else if (p?.SkillPrompt?.SkillId == "classic:qingxian" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p?.SkillPrompt?.SkillId == "classic:qingxian" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "recover"))
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "recover");
            else if (p?.SkillPrompt?.SkillId == "classic:qingxian" && p.Choices.Any(c => c.Cards.Count == 1))
                Answer(g, c => c.Cards.Count == 1);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed recovery fixture did not reach its expected boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All viewer projections, exact owning recovery state, history, zones, and commands restore identically.");
    private static void Reject(GameEngine g) { var state = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("fixture:illegal"), g.Revision)).Accepted && state == State(g), "An unpublished choice is rejected atomically at a restored boundary."); }
    private static (GameEngine, ContentRegistry) Create(string mode = "normal")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(mode));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = "identity:classic-sq-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Accept(g, new SelectGeneralCommand(0, "fixture:sq-owner", g.Revision, P(g)!.PromptId)); Settle(g);
        if (mode.StartsWith("silver", StringComparison.Ordinal) || mode.StartsWith("qingxian", StringComparison.Ordinal) || mode == "qixi")
        {
            while (g.State.Players[0].Hp > 4) Use(g, "trim-owner");
            PrepareLord(g);
        }
        return (g, r);
    }
    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}", "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"回复驱动\",\"description\":\"真实回复边界\"" + (members.Contains("\"chooseOption\"", StringComparison.Ordinal) ? ",\"optionLabels\":{\"continue\":\"继续\"}" : "") + "}}}").Programs[id];
    private static string Activation(string id, string effects, int targets = 0) => "{\"id\":\"" + id + "\",\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":" + effects + "}";
    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sq", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var acts = new[]
            {
                Activation("grow", "[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}]"),
                Activation("damage", "[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]", 1),
                Activation("recover", "[{\"op\":\"recover\",\"target\":\"owner\",\"amount\":2}]"),
                Activation("recover-to", "[{\"op\":\"recoverTo\",\"target\":\"owner\",\"numberExpression\":\"integerConstant\",\"minimumValue\":6,\"clampToMaxHp\":true}]"),
                Activation("recover-other", "[{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1}]", 1),
                Activation("hurt-other", "[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}]", 1),
                Activation("hurt-owner", "[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":3}]"),
                Activation("trim-owner", "[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]"),
                Activation("empty-hand", "[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")
            };
            b.AddSkill(new(Driver, "驱动", "真实回复") { Program = Load(Driver, "\"activations\":[" + string.Join(',', acts) + "]") });
            b.AddSkill(new("fixture:sq-selection", "固定选将", "只固定 AI 选将，不参与运行规则")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            if (mode == "qingxian-dying")
                b.AddSkill(new(SourcePulse, "SourcePulse", "真实通用濒死返回暂停")
                { Program = Load(SourcePulse, "\"triggers\":[{\"id\":\"generic-dying-return\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"return-step\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"recoverTo\",\"target\":\"owner\",\"numberExpression\":\"integerConstant\",\"minimumValue\":3,\"clampToMaxHp\":true}]}]") });
            if (mode == "silver-observers" || mode.StartsWith("qingxian", StringComparison.Ordinal))
                foreach (var (id, window, filter) in new[] { ("fixture:sq-hp", "afterHpRecovered", ""), ("fixture:sq-gain", "cardsGained", ",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\"") })
                    b.AddSkill(new(id, "精确回复子链", "精确回复子链") { Program = Load(id, "\"triggers\":[{\"id\":\"pause\",\"window\":\"" + window + "\",\"subject\":\"owner\",\"optional\":false" + filter +
                        (id == "fixture:sq-hp" ? ",\"condition\":{\"kind\":\"not\",\"children\":[{\"kind\":\"ownerIsTurnPlayer\"}]}" : "") +
                        (mode == "qingxian-dying" && id == "fixture:sq-hp" ? ",\"usageScope\":\"game\",\"usageLimit\":1" : "") +
                        ",\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"step\",\"options\":[{\"id\":\"continue\"}]}" +
                        (mode == "qingxian-dying" && id == "fixture:sq-hp" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}" : "") + "]}]") });
            b.AddGeneral(new("fixture:sq-owner", "回复者", "supporter", Driver, mode == "wrong-faction" ? "wei" : "wu", 6,
                mode == "silver-observers" || mode.StartsWith("qingxian", StringComparison.Ordinal) ? ["boundary:zhiheng", "fixture:sq-gain"] : mode == "qixi" ? ["boundary:zhiheng", "classic:qixi"] : ["boundary:zhiheng"]) { InitialHp = mode == "full-beneficiary" ? 5 : 4 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:sq-target-{i}", "候选主公", "supporter", "fixture:sq-selection", "wu", mode == "full-beneficiary" ? 4 : 6,
                mode == "silver-observers" ? ["boundary:jiuyuan", "fixture:sq-hp"] : mode == "qingxian" ? ["boundary:jiuyuan", "classic:qingxian", "fixture:sq-hp"] : mode == "qingxian-dying" ? ["boundary:jiuyuan", "classic:qingxian", "fixture:sq-hp", SourcePulse] : mode == "stone" ? ["boundary:jiuyuan", "classic:qingguo"] : mode == "projected" ? ["boundary:jiuyuan", "classic:weidi"] : ["boundary:jiuyuan"]) { InitialHp = mode == "full-beneficiary" ? 4 : 2 });
            b.AddDeck(new("fixture:sq-deck", "固定", 4, 2, []) { PhysicalCards = Enumerable.Range(0, mode == "stone" ? 128 : 80).Select(i =>
                new ContentDeckPhysicalCard(mode == "stone" ? new[] { "classic:stone-axe", "classic:silver-lion", "standard:slash", "standard:dodge" }[i / 32] :
                    mode.StartsWith("silver", StringComparison.Ordinal) || mode.StartsWith("qingxian", StringComparison.Ordinal) || mode == "qixi" ? "classic:silver-lion" : mode == "peach" ? "standard:peach" : "standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-sq-fixture", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:sq-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:sq-owner", "fixture:sq-target-1", "fixture:sq-target-2", "fixture:sq-target-3"]));
        }
    }
}
