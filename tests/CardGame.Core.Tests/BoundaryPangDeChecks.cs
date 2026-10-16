using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryPangDeChecks
{
    private const string Skill = "boundary:jianchu";
    private const string Driver = "fixture:pd-driver";
    private const string Cost = "fixture:pd-cost";
    private const string Gain = "fixture:pd-gain";
    private const string Mode = "identity:classic-boundary-pang-de-fixture";

    public static void BasicDiscardClaimsExactEntitiesAndStillResolvesOriginalSlash()
    {
        var (game, registry) = Create(observers: true); var hp = game.State.Players[1].Hp;
        var slash = Play(game, [1]); Activate(game); var choice = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
        Require(choice.Cards.Count == 0 && choice.Parameters.ContainsKey("slot-index"),
            "The acting source chooses opaque hand slots without reading a target's hidden card kind or identity.");
        Private(game); Cold(game, registry); Reject(game); Answer(game, c => c.Id == choice.Id);
        Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        Require(Claims(game).Length == 0 && game.State.Players[1].Hp == hp,
            "The exact target discard and its movement child complete before the original-use entity is obtained or damage applies.");
        Cold(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        var owner = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CurrentUsePhysicalClaims is not null);
        var receipt = owner.CurrentUsePhysicalClaims!.Single();
        Require(receipt.CardIds.SequenceEqual([slash]) && receipt.ActionId == owner.Action!.ActionId &&
            receipt.ActorSeat == 0 && receipt.RecipientSeat == 1 && receipt.CardUseFrameId == owner.Id &&
            game.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == slash) && game.State.Players[1].Hp == hp,
            "The still-resolving use owns the exact physical Slash receipt before the recipient's gain observer and original damage.");
        Frozen(receipt.CardIds); Frozen(Claims(game).Single().Claim.CardIds); FreezeDetached(receipt);
        Cold(game, registry); Continue(game); Finish(game);
        Require(game.State.Players[1].Hp == hp - 1 && Claims(game).Length == 1 &&
            game.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.Hand(1)) == 1 &&
            !game.CardMovements.Any(m => m.CardId == slash && m.Reason == CardMoveReasons.UseFinished),
            "Obtaining the actual Slash never cancels it or repays its final cleanup; the original target still takes one damage.");
        Cold(game, registry);

        var (two, tr) = Create(observers: true); var materials = two.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
        Accept(two, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", materials, [1], two.Revision, P(two)!.PromptId));
        Activate(two); DiscardHand(two); Reach(two, p => p.SkillPrompt?.SkillId == Cost); Continue(two);
        Reach(two, p => p.SkillPrompt?.SkillId == Gain);
        Require(Claims(two).Single().Claim.CardIds.Order().SequenceEqual(materials.Order()) &&
            two.CreateSnapshot(1).Players[1].Hand.Where(c => materials.Contains(c.Id)).Count() == 2,
            "A real two-material conversion obtains both frozen actual costs rather than a fabricated Slash entity.");
        Cold(two, tr); Continue(two); Finish(two);
        Require(two.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
            materials.All(id => !two.CardMovements.Any(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished)),
            "The original converted use survives the gain child and skips already claimed material cleanup."); Cold(two, tr);
    }

    private static ProgramCurrentUsePhysicalCardsClaimedEvent[] Claims(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramCurrentUsePhysicalCardsClaimedEvent>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsActivation(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) { Reach(g, IsActivation); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void DiscardHand(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
    private static int Play(GameEngine g, IReadOnlyList<int> targets)
    {
        Finish(g); var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(targets));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, targets, g.Revision, P(g)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        return action.CardId.Value;
    }
    private static void Finish(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 150; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Pang De command prefix did not reach its boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.SkillPrompt?.SkillId is Cost or Gain) Continue(g);
        else if (p is { Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(g);
        else if (p is { Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real command."); }
    private static void Private(GameEngine g) { Require(P(g)!.PlayerSeat == 0 && P(g)!.IsPrivate, "The exact opponent-zone payment belongs to its actual chooser."); for (var v = 1; v < 4; v++) Require(g.CreateSnapshot(v).PendingDecision is null, "Other views receive no opaque hand-slot choice."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished payment is atomically rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All four private views, owning use receipts, exact paid entities, journal and commands cold-restore identically.");
    private static void Frozen(IReadOnlyList<int> values) { Require(values is IList<int> { IsReadOnly: true }, "The public exact-entity collection is frozen."); try { ((IList<int>)values).Add(-1); throw new InvalidOperationException("An exposed collection allowed mutation."); } catch (NotSupportedException) { } }
    private static void FreezeDetached(ProgramCurrentUsePhysicalClaim receipt)
    {
        var mutable = receipt.CardIds.ToArray(); var original = new ProgramCurrentUsePhysicalCardsClaimedEvent(receipt with { CardIds = mutable });
        var frozen = (ProgramCurrentUsePhysicalCardsClaimedEvent)typeof(GameEngine).Assembly.GetType("CardGame.Core.CommittedEventProjection")!.GetMethod("Freeze", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [original])!;
        mutable[0] = -1; Require(frozen.Claim.CardIds[0] == receipt.CardIds[0], "Commit preparation detaches the exact nested claim entity list from its producer."); Frozen(frozen.Claim.CardIds);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool tricks = false, string? armor = null, bool observers = false, bool loseSource = false, bool native = false, bool borrowed = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(tricks, armor, observers, loseSource, native, borrowed));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, ModeId = Mode, UseInteractiveSetup = !native, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(game, new SelectGeneralCommand(0, "fixture:pd-owner", game.Revision, P(game)!.PromptId)); Finish(game); }
        return (game, registry);
    }
    private sealed class Fixture(bool tricks, string? armor, bool observers, bool loseSource, bool native, bool borrowed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pd", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
              {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"two-targets","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"cardTargetCount","ruleOperation":"add","amount":1,"cardKinds":["slash","fireSlash","thunderSlash"]}]}]},
             {"id":"fixture:pd-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
             {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid","window":"discardPileReceived","subject":"owner","optional":false,"movementReasons":["skill-program.{{Skill}}.SelectAndMoveOwnedCard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{Gain}}","revision":1,"triggers":[{"id":"claimed","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.current-use-physical-claim"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:pd-loss","revision":1,"triggers":[{"id":"suppress-source","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","optional":false,"movementReasons":["skill-program.{{Skill}}.SelectAndMoveOwnedCard"],"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> {
                [Driver] = new { name = "真实准备", description = "真实装备与目标上限准备" }, ["fixture:pd-quiet"] = new { name = "固定目标", description = "固定轻量回合" },
                [Cost] = new { name = "实际弃牌子链", description = "支付后的冻结窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Gain] = new { name = "实体取得子链", description = "领取后原杀继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                ["fixture:pd-loss"] = new { name = "实际来源失效", description = "支付后的参与者抑制来源非锁定技" } } }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实准备程序") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:pd-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:pd-owner", "鞬出真实命令", "supporter", Skill, "qun", 6,
                (native ? new[] { "classic:mashu" } : new[] { "classic:mashu", "classic:wusheng", "classic:fuhun", "boundary:rende", Driver })
                    .Concat(observers ? [Cost] : Array.Empty<string>()).ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:pd-target-{i}", "固定目标", "supporter", native ? Skill : "fixture:pd-selection", "wei", 8,
                (native ? new[] { "classic:mashu" } : observers ? new[] { "fixture:pd-quiet", Gain } : new[] { "fixture:pd-quiet" })
                    .Concat(loseSource && i == 1 ? ["fixture:pd-loss"] : Array.Empty<string>()).ToArray()));
            b.AddDeck(new("fixture:pd-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, native ? 32 : 80).Select(i => new ContentDeckPhysicalCard(native ? i % 3 == 0 ? "standard:slash" : "standard:duel" : borrowed && i % 4 == 0 ? "classic:borrowed-sword" : tricks ? "standard:duel" : "standard:slash", Suit.Heart, i % 13 + 1))
                .Concat(armor is null ? [] : Enumerable.Range(0, 4).Select(i => new ContentDeckPhysicalCard(armor, Suit.Heart, 6))).ToArray() });
            b.AddMode(new(Mode, "真实鞬出", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:pd-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:pd-owner", "fixture:pd-target-1", "fixture:pd-target-2", "fixture:pd-target-3"]));
        }
    }
}
