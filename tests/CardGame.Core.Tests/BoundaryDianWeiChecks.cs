using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryDianWeiChecks
{
    private const string Driver = "fixture:dw-driver";
    private const string Cost = "fixture:dw-cost";
    private const string Gain = "fixture:dw-gain";
    private const string Hp = "fixture:dw-hp";
    private const string Mode = "identity:classic-boundary-dian-wei-fixture";

    public static void RealDamageWeaponCostSharedQuotaAndActualTurnTargets()
    {
        var (g, r) = Create(extra: true); var turn = g.State.TurnNumber;
        var beforeHp = g.State.Players[0].Hp;
        Qiangxi(g, "receive-damage", 2); Play(g);
        Require(Events<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1 && e.SourceLess) &&
            g.State.Players[0].Hp == beforeHp - 1 && !Events<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == "boundary:qiangxi"),
            "Receiving the official cost is actual source-less damage, with its normal damage fact rather than an HP-loss payment.");
        Require(Actions(g).All(a => !a.SelectableTargetSeats.Contains(2)), "Both branches exclude the already designated actual-turn target.");
        var unchanged = State(g);
        Require(!g.Submit(new UseProgramSkillCommand(0, "boundary:qiangxi", "receive-damage", [], [2], g.Revision, P(g)!.PromptId)).Accepted && State(g) == unchanged,
            "A repeated target rejects atomically before another damage cost or ledger commitment.");
        var weapon = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.QinggangSword).Id;
        Accept(g, new PlayCardCommand(0, weapon, [], g.Revision, P(g)!.PromptId)); Play(g);
        var targetHp = g.State.Players[3].Hp;
        Qiangxi(g, "discard-weapon", 3, weapon); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:qiangxi");
        var child = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == paid.Id);
        Require(paid.SelectedCardPayment is { MovementCommitted: true } && child.Batch.OriginSkillInstanceId == paid.SkillInstanceId &&
            child.Batch.Movements is [var actual] && actual.CardId == weapon && actual.From == CardLocation.Equipment(0) &&
            actual.To == CardLocation.DiscardPile && g.State.Players[3].Hp == targetHp,
            "The real equipment payment and its typed movement child finish before any target damage.");
        Private(g); Cold(g, r); Continue(g); Play(g);
        Require(g.State.Players[3].Hp == targetHp - 1 && !Actions(g).Any() &&
            g.CardMovements.Count(m => m.CardId == weapon && m.Reason.Value == "skill-program.boundary:qiangxi.DiscardSelected") == 1,
            "Damage and weapon branches share the same two-use actual Play quota, and pay the weapon exactly once.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Require(g.State.TurnNumber == turn && Actions(g).Any() && Actions(g).All(a => !a.SelectableTargetSeats.Contains(2) && !a.SelectableTargetSeats.Contains(3)),
            "Returning from the real inserted Play phase refreshes its phase quota while preserving both actual-turn target commitments.");
        Qiangxi(g, "receive-damage", 1); Play(g);
        var committed = Events<ProgramDistinctTurnTargetCommittedEvent>(g).Where(e => e.OwnerSeat == 0 && e.TurnNumber == turn).ToArray();
        Require(committed.Length == 3 && committed.Select(e => e.TargetSeat).Distinct().Count() == 3 &&
            committed.Select(e => e.PhaseInstanceId).Distinct().Count() == 2 && committed.All(e => !string.IsNullOrWhiteSpace(e.SkillInstanceId) && e.GameplayHash.Length > 0),
            "The public history freezes the exact source instance, actual turn and distinct real phase ids.");
        Cold(g, r); EndTurn(g); Play(g);
        Require(g.State.TurnNumber > turn && Actions(g).All(a => a.SelectableTargetSeats.Contains(2)),
            "Only the next actual turn clears the different-target restriction."); Cold(g, r);
    }

    private static LegalAction[] Actions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ProgramSkillId == "boundary:qiangxi").ToArray();
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Qiangxi(GameEngine g, string id, int target, int? card = null) =>
        Accept(g, new UseProgramSkillCommand(0, "boundary:qiangxi", id, card is { } c ? [c] : [], [target], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void EndTurn(GameEngine g)
    { var turn = g.State.TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Until(g, () => Events<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 240; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Did not reach the exact fixture prompt: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 300; i++) { if (predicate()) return; Step(g); } throw new InvalidOperationException("Did not reach the exact real event/frame boundary."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real fixture command."); }
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && p.PlayerSeat == 0, "Only the actual chooser owns this private prompt."); for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null, "Another view must not expose private choices."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four player views, exact owning frames, real movement facts and accepted command prefix cold-restore identically.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool extra = false, bool armor = false, bool gainObserver = false, bool invalidate = false, bool fragile = false, bool native = false, bool nested = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(extra, armor, gainObserver, invalidate, fragile, native, nested));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 4 : 12 }, r);
        Accept(g, new StartGameCommand());
        if (native) Until(g, () => g.State.Players.All(player => player.GeneralId.StartsWith("fixture:dw-", StringComparison.Ordinal)));
        if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:dw-owner", g.Revision, P(g)!.PromptId)); Play(g); }
        return (g, r);
    }

    private sealed class Fixture(bool extra, bool armor, bool gainObserver, bool invalidate, bool fragile, bool native, bool nested) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-dian-wei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
              {"id":"foreign","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{Cost}}","revision":1,"triggers":[{"id":"actual-payment","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.boundary:qiangxi.DiscardSelected"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
             {"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.applied-damage-benefit.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gained","options":[{"id":"continue"}]}{{(invalidate ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "")}}]}]},
             {"id":"{{Hp}}","revision":1,"triggers":[{"id":"real-equipment-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"healed","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:dw-extra","revision":1,"triggers":[{"id":"actual-extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
             {"id":"fixture:dw-nested","revision":1,"triggers":[{"id":"earlier-nested-injury","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","priority":100,"optional":false,"condition":{"kind":"compare","left":{"kind":"eventTargetDamageInstancesTakenThisTurn"},"operator":"equal","right":{"kind":"integerConstant","value":2} },"effects":[{"op":"damage","target":"owner","amount":1}]}]},
             {"id":"fixture:dw-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Cost, Gain, Hp, "fixture:dw-extra", "fixture:dw-nested", "fixture:dw-quiet" })
                presentations[id] = id is Cost or Gain or Hp
                    ? new { name = id, description = "真实通用能力边界夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    : (object)new { name = id, description = "真实通用能力边界夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实通用边界夹具") { Program = catalog.Programs[id], Tags = id == "fixture:dw-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:dw-pick-owner", "固定来源", "只用于公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == (native ? Role.Rebel : Role.Lord) ? 100000d : -100000d) });
            b.AddSkill(new("fixture:dw-pick-other", "固定其他", "只用于公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == (native ? Role.Rebel : Role.Lord) ? -100000d : 100000d) });
            var skills = new List<string> { "boundary:qiangxi", "boundary:ninge" };
            if (!native) skills.AddRange([Driver, Cost]); if (gainObserver) skills.Add(Gain); if (extra) skills.Add("fixture:dw-extra");
            b.AddGeneral(new("fixture:dw-owner", "真实界典韦机制", "supporter", "fixture:dw-pick-owner", "wei", fragile ? 4 : 8, skills.ToArray()) { InitialHp = fragile ? 1 : null });
            for (var i = 1; i < 4; i++)
            {
                var otherSkills = new List<string> { "fixture:dw-quiet" };
                if (gainObserver && i == 1) otherSkills.Add(Hp);
                if (nested && i == 1) otherSkills.Add("fixture:dw-nested");
                b.AddGeneral(new($"fixture:dw-other-{i}", "固定其他角色", "supporter", "fixture:dw-pick-other", "wei", 12, otherSkills.ToArray()));
            }
            b.AddDeck(new("fixture:dw-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(armor ? "classic:silver-lion" : "standard:qinggang_sword", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "界典韦真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dw-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dw-owner", "fixture:dw-other-1", "fixture:dw-other-2", "fixture:dw-other-3"]));
        }
    }
}
