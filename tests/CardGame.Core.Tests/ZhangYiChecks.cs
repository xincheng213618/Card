using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangYiChecks
{
    private const string Dianjun = "ol:dianjun", Kangrui = "ol:kangrui", Driver = "fixture:zy-driver", Quiet = "fixture:zy-quiet";
    private const string Watch = "fixture:zy-draw", Health = "fixture:zy-health", Boundary = "fixture:zy-boundary", After = "fixture:zy-after";
    private const string Prevent = "fixture:zy-prevent", PeerDamage = "fixture:zy-peer-damage", Owner = "fixture:zy-owner", Mode = "identity:classic-zhang-yi-fixture";
    private const string LegacyMode = "identity:zhang-yi-chain-legacy-fixture";
    private const string DyingWatch = "fixture:zy-dying", Loss = "fixture:zy-loss", CleanupWatch = "fixture:zy-cleanup";
    private const string Lease = "fixture:zy-leased-noop", Kept = "fixture:zy-permanent-noop";

    public static void ActualEndDamageDrawRecoveryExtraPlayQuotasAndPaidSourceLossCold()
    {
        foreach (var scenario in new[] { "normal", "loss" })
        {
            var (g, registry) = Start(scenario); var turn = g.State.TurnNumber; var hp = V(g, 0).Hp;
            Slash(g, 1); Play(g);
            Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash), "A real ordinary Slash consumes the original Play phase's quota before Dianjun.");
            End(g); Reach(g, () => Optional(P(g), Kangrui));
            Need(V(g, 0).Hp == hp - 1 && E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.SourceLess && e.Amount == 1) == 1 &&
                E<TurnEndedEvent>(g).Count(e => e.TurnNumber == turn && e.ActorSeat == 0) == 1,
                "Dianjun requests actual source-less damage after the one real turn end, then Kangrui reacts to that actual first injury.");
            Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ZhangYi?.Stage == ZhangYiStage.KangruiDraw);
            Need(paid.ZhangYi is { RecipientSeat: 0, ActualTurnNumber: var issuedTurn } && issuedTurn == turn &&
                E<ZhangYiKangruiDrawIssuedEvent>(g).Count() == 1 && g.CreateSnapshot(1).PendingDecision is null,
                "The owner really draws under the fixed first-injury frame before choosing; its private draw child is not visible to another viewer.");
            g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "recover"));
            Need(V(g, 0).Skills!.Any(s => s.Id == Kangrui) == (scenario != "loss") && V(g, 0).Skills!.Any(s => s.Id == Dianjun) == (scenario != "loss"),
                "The loss scenario actually removes both printed sources inside the paid draw child, while its issued Kangrui choice survives.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "recover");
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, registry); Continue(g); Play(g);
            Need(g.State.TurnNumber == turn && V(g, 0).Hp == hp && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
                E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 2 &&
                g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
                "The native recovery finishes once, then the same actual turn receives exactly one Play phase with a freshly usable ordinary Slash quota, including after both sources were lost.");
            g = Cold(g, registry); Slash(g, 1); Play(g);
            Need(!PositiveHandLimit(g) && E<ZhangYiKangruiHandLimitSetEvent>(g).Count() == 1 && !E<ZhangYiKangruiDamageConsumedEvent>(g).Any(),
                "The recovery option also arms Kangrui's common actual-damage condition; the extra-phase real Slash sets hand limit exactly zero despite the fixture's additive hand-limit modifier, without adding damage.");
            Use(g, "turn-lease"); Play(g); Use(g, "phase-lease"); Play(g);
            var turnGrant = E<ProgramTurnSkillsGrantedEvent>(g).Single(); var phaseGrant = E<ProgramPlayPhaseSkillsGrantedEvent>(g).Single();
            Need(V(g, 0).Skills!.Any(s => s.Id == Lease) && V(g, 0).Skills!.Count(s => s.Id == Kept) == 1 &&
                turnGrant.TurnExpiry is not null && phaseGrant.Grants.Count == 2,
                "The actual end-owned extra Play can really issue both actual-turn and phase skill sources after the existing TurnEnded fact, sharing a permanent no-op without duplicating its public skill.");
            g = Cold(g, registry);
            Use(g, "hold", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"); Play(g);
            var held = E<ProgramHoldCardsPlacedEvent>(g).Single().CardIds.Single();
            Need(g.CreateCardZoneDiagnostics().Any(z => z.Location == CardLocation.PojunHold(1) && z.CardId == held),
                "A real extra-Play native hold removes one foreign hand entity until actual turn-end cleanup.");
            var alcohol = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol);
            Accept(g, new PlayCardCommand(0, alcohol.CardId!.Value, alcohol.TargetSeats, g.Revision, P(g)!.PromptId, alcohol.PlayedCardKind) { ConversionSource = alcohol.ConversionSource }); Play(g);
            Need(V(g, 0).HasAlcoholEffect, "The extra Play really uses native Alcohol and leaves its unconsumed turn effect active before ending.");
            var kept = V(g, 0).HandCount; Need(kept > 0, "The real extra phase retains cards that a second Discard phase would wrongly remove.");
            End(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == CleanupWatch && P(g)?.PlayerSeat == 1);
            Need(!V(g, 0).HasAlcoholEffect && E<AlcoholExpiredEvent>(g).Count() == 1 &&
                E<ZhangYiDianjunCleanupStartedEvent>(g).Count() == 1 && E<ZhangYiDianjunCleanupIssuedEvent>(g).Count() == 1 &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ZhangYi?.Stage == ZhangYiStage.DianjunCleanupChildren) &&
                !V(g, 0).Skills!.Any(s => s.Id == Lease) && V(g, 0).Skills!.Any(s => s.Id == Kept) &&
                E<ProgramTurnSkillGrantExpiredEvent>(g).Count(e => e.SourceId == turnGrant.GrantSourceId && e.SkillId is Lease or Kept) == 2 &&
                E<ProgramPlayPhaseSkillGrantExpiredEvent>(g).Count(e => phaseGrant.Grants.Any(source => source.GrantId == e.Grant.GrantId) && e.Reason == "play-phase-ended") == 2 &&
                g.CardMovements.Count(m => m.CardId == held && m.From == CardLocation.PojunHold(1) && m.To == CardLocation.Hand(1) && m.Reason == CardMoveReasons.PojunHoldReturn) == 1 &&
                !E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == After),
                "The phase sources close at Play end and the exact turn sources close at tail cleanup, preserving the permanent source. The original end-owned program also expires new Alcohol and returns the exact hold once, awaiting its real movement child before the next sibling.");
            g = Cold(g, registry); Reach(g, () => g.State.TurnNumber > turn);
            Need(V(g, 0).HandCount == kept &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Boundary && e.OwnerSeat == 0) == 3 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == After && e.OwnerSeat == 0 && e.Completed) == 1 &&
                E<TurnEndedEvent>(g).Count(e => e.ActorSeat == 0 && e.TurnNumber == turn) == 1 &&
                E<ZhangYiResolutionStartedEvent>(g).Count(e => e.Source.SkillId == Dianjun) == 1 && E<ZhangYiKangruiDrawIssuedEvent>(g).Count() == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId is Dianjun or Kangrui && e.Completed) == 2,
                "Ending the inserted Play invokes no second Discard/Ending/TurnEnded; the original end parent's remaining sibling still resolves once and retained cards stay.");
            Play(g); Need(g.State.TurnNumber > turn && PositiveHandLimit(g), "The next actual own turn publishes its positive-hand-limit activation again, proving the zero override expired.");
            _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool PositiveHandLimit(GameEngine g) => g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Driver && a.ProgramActivationId == "positive-limit");
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Zhang Yi completed before the required native boundary: " + Diagnostic(g));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Zhang Yi prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Zhang Yi boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Pending = P(g),
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Tail = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Zhang Yi command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Zhang Yi preserves exact first actual damage, private fixed-recipient choice, paid draw/recovery, native damage and end-owned extra Play return."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = scenario == "chain-legacy" ? LegacyMode : Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhang-yi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:zhang-yi");
            var loss = scenario == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"native-alcohol","inputKinds":["slash"],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false}],"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"turn-lease","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["{{Lease}}","{{Kept}}"],"expiry":"actualTurnEnd"}]},
                {"id":"phase-lease","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantPlayPhaseSkills","target":"owner","skillIds":["{{Lease}}","{{Kept}}"]}]},
                {"id":"hold","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"holdTargetCards","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"minimumCards":1,"resultBind":"held"}]},
                {"id":"positive-limit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"condition":{"kind":"positiveHandLimit"},"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-kangrui-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.zhang-yi.kangrui.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}{{loss}}]}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"health","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Boundary}}","revision":1,"triggers":[{"id":"discard-start","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"start","options":[{"id":"continue"}]}]},{"id":"discard-end","window":"discardPhaseEnded","subject":"owner","turnOwnerScope":"own","allowOwnDiscardPhaseEnded":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"end","options":[{"id":"continue"}]}]},{"id":"ending","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"ending","options":[{"id":"continue"}]}]}]},
              {"id":"{{After}}","revision":1,"triggers":[{"id":"after-sibling","window":"afterTurnEnded","subject":"owner","turnOwnerScope":"own","priority":-100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}]}]},
              {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent-first-attempt","window":"beforeDamageApplied","subject":"damageTarget","optional":true,"usageScope":"turn","usageLimit":1,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{PeerDamage}}","revision":1,"triggers":[{"id":"self-damage","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{DyingWatch}}","revision":1,"triggers":[{"id":"real-dying-entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"dying","options":[{"id":"continue"}]}]}]},
              {"id":"{{Loss}}","revision":1,"triggers":[{"id":"restore-and-lose","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:kangrui","ol:dianjun"],"sourceBind":"standard:none"}]}]},
              {"id":"{{CleanupWatch}}","revision":1,"triggers":[{"id":"returned-hold","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.pojun.return"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"returned","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Health, Boundary, After, Prevent, PeerDamage, DyingWatch, Loss, CleanupWatch };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "张翼实际规则小夹具", optionLabels = id is Watch or Health or Boundary or After or DyingWatch or CleanupWatch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "张翼原生行为") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Lease, "限时无副作用技能", "仅验证真实来源过期")); b.AddSkill(new(Kept, "永久无副作用技能", "保留永久来源"));
            b.AddSkill(new("fixture:zy-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            var skills = new List<string> { Kangrui, Driver, Watch, Health, Boundary, After, Kept };
            if (scenario != "bonus" && scenario != "peer" && !scenario.StartsWith("chain-", StringComparison.Ordinal)) skills.Add(Dianjun); if (scenario == "bonus") skills.Add(Prevent);
            if (scenario == "loss") skills.Add(Loss); if (scenario is "save" or "death") skills.Add(DyingWatch);
            b.AddGeneral(new(Owner, "张翼真实本人", "ol-zhang-yi", "fixture:zy-owner-pick", "shu", 4, skills) { InitialHp = scenario is "save" or "death" ? 1 : 4 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:zy-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:zy-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100003d - index) });
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "shu", 8, scenario == "peer" && index == 0 ? [PeerDamage, Quiet, Health, CleanupWatch] : [Quiet, CleanupWatch]));
            }
            b.AddCard(new("fixture:zy-fire-slash", "火杀", "基本牌", "真实火杀传播实体", CardKind.FireSlash));
            b.AddDeck(new("fixture:zy-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i =>
                new ContentDeckPhysicalCard(scenario == "save" ? "standard:peach" : scenario.StartsWith("chain-", StringComparison.Ordinal) ? "fixture:zy-fire-slash" : "standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(scenario == "chain-legacy" ? LegacyMode : Mode, "张翼真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:zy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
