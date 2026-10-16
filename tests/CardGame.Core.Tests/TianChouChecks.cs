using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TianChouChecks
{
    private const string Skill = "ol:shandao", Owner = "fixture:tc-owner", Driver = "fixture:tc-driver", Quiet = "fixture:tc-quiet", Watch = "fixture:tc-watch",
        Commit = "fixture:tc-commit", Health = "fixture:tc-health", Grain = "fixture:tc-grain", Source = "fixture:tc-source", Empty = "fixture:tc-empty", Shield = "fixture:tc-shield", Mode = "identity:classic-tian-chou-fixture";
    private const string TopReason = "skill-program.tian-chou.shandao.top";
    public static void SequentialTopFiveGrainsComplementAndExhaustionCold()
    {
        var (g, registry) = Start(); Begin(g); Select(g, 2, 0, 1);
        Need(Current(g) is { TargetsFrozen: true, Stage: TianChouShandaoStage.Card } r && r.Targets.SequenceEqual([0, 1, 2]), "The chosen unordered set freezes in real action order starting with the source, including itself.");
        Top(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(E<TianChouShandaoTopPaidEvent>(g).Length == 1 && !E<TianChouShandaoNativeIssuedEvent>(g).Any(), "The first real top move opens its own loss child before the next participant or either native trick.");
        g = Cold(g, registry); Continue(g); Card(g, 1);
        var prompt = P(g)!; var before = State(g);
        Need(prompt is { PlayerSeat: 0, IsPrivate: true } && prompt.Choices.All(c => c.Cards.Count == 0 && c.Parameters["source-zone"] == nameof(CardZoneKind.Hand)) && g.CreateSnapshot(1).PendingDecision is null,
            "The source chooses another player's face-down hand slots without exposing their actual cards or its private choice to that player.");
        Need(!g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "A foreign seat cannot answer the exact private selection or mutate its pending receipt.");
        g = Cold(g, registry); Top(g); Card(g, 2); Top(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Commit);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Last();
        Need(use is { CardId: 0, CardKind: CardKind.FiveGrains, PhysicalCardIds.Count: 0 } && use.TargetSeats.SequenceEqual([0, 1, 2]) && use.Action is { EffectiveSuit: Suit.None, EffectiveRank: 0 },
            "The completed sequential real payments issue one real colorless zero-entity FiveGrains against exactly the frozen beneficiaries.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.Kind == DecisionKind.SelectHarvestCard);
        var native = E<TianChouShandaoNativeIssuedEvent>(g).Single(); var paid = E<TianChouShandaoTopPaidEvent>(g);
        Need(E<CardsRevealedEvent>(g).Single(e => e.ResolutionId == native.UseId).Cards.Select(c => c.Id).SequenceEqual(paid.Reverse().Select(p => p.CardId)),
            "Each selected card really moved directly to the pile top; the genuine FiveGrains reveals their resulting last-placed-first deck order.");
        g = Cold(g, registry); Reach(g, () => E<TianChouShandaoCompletedEvent>(g).Any()); Play(g);
        var arrows = E<TianChouShandaoNativeIssuedEvent>(g).Single(e => e.Kind == CardKind.ArrowBarrage);
        Need(E<TianChouShandaoNativeTargetEvent>(g).Where(e => e.UseId == arrows.UseId).Select(e => e.TargetSeat).SequenceEqual([3]) &&
            E<GroupResponseEvent>(g).Single(e => e.ResolutionId == arrows.UseId).ResponderSeat == 3 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 3) &&
            E<TianChouShandaoNativeReturnedEvent>(g).Length == 2 && E<TianChouShandaoTopPaidEvent>(g).Length == 3 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Skill),
            "The second native use attacks the original set's complement, excludes the source, returns once, and consumes exactly this real Play's one activation.");
        _ = Cold(g, registry);

        var (shortGame, shortRegistry) = Start("exhaust"); Begin(shortGame); Select(shortGame, 3, 2, 1, 0); Top(shortGame);
        Reach(shortGame, () => P(shortGame)?.SkillPrompt?.SkillId == Watch); shortGame = Cold(shortGame, shortRegistry); Continue(shortGame);
        foreach (var seat in new[] { 1, 2, 3 }) { Card(shortGame, seat); Top(shortGame); }
        Reach(shortGame, () => P(shortGame)?.Kind == DecisionKind.SelectHarvestCard);
        var shortUse = E<TianChouShandaoNativeIssuedEvent>(shortGame).Single();
        Need(shortUse.TargetCount == 4 && E<TianChouShandaoNativeTargetEvent>(shortGame).Where(e => e.UseId == shortUse.UseId).Select(e => e.TargetSeat).SequenceEqual([0, 1, 2, 3]) &&
            E<CardsRevealedEvent>(shortGame).Single(e => e.ResolutionId == shortUse.UseId).Cards.Count == 3,
            "A real intervening draw exhausts the small pile: all four legal targets remain declared even though only three actual cards can be revealed.");
        shortGame = Cold(shortGame, shortRegistry); Reach(shortGame, () => E<TianChouShandaoCompletedEvent>(shortGame).Any());
        Need(E<HarvestCardSelectedEvent>(shortGame).Count(e => e.ResolutionId == shortUse.UseId) == 3 && E<TianChouShandaoNativeIssuedEvent>(shortGame).Length == 1 &&
            E<TianChouShandaoTrickSkippedEvent>(shortGame).Single().Kind == CardKind.ArrowBarrage,
            "The native draft finishes after three actual picks without inventing a fourth card or removing the fourth declared target, and the empty complement never issues an ArrowBarrage."); _ = Cold(shortGame, shortRegistry);
    }
    public static void NativeNullificationDodgeAndTargetGatesCold()
    {
        var (counter, counterRegistry) = Start("counterspell"); Begin(counter); Select(counter, 0); Top(counter);
        Reach(counter, () => P(counter) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
        var original = E<TianChouShandaoNativeIssuedEvent>(counter).Single(); counter = Cold(counter, counterRegistry);
        Answer(counter, c => c.Cards.Count == 1); Reach(counter, () => E<TianChouShandaoCompletedEvent>(counter).Any());
        Need(E<NullificationRespondedEvent>(counter).Length == 1 && E<NullificationResolvedEvent>(counter).Any(e => e.ResolutionId == original.UseId && e.EffectNullified) &&
            !E<HarvestCardSelectedEvent>(counter).Any(e => e.ResolutionId == original.UseId) && E<TianChouShandaoNativeReturnedEvent>(counter).Count(e => e.UseId == original.UseId) == 1 &&
            E<TianChouShandaoNativeIssuedEvent>(counter).Any(e => e.Kind == CardKind.ArrowBarrage),
            "An actual paid physical Nullification cancels the real FiveGrains, then its exact native return still issues the following ArrowBarrage."); _ = Cold(counter, counterRegistry);

        var (dodge, dodgeRegistry) = Start("dodge"); Begin(dodge); Select(dodge, 0); Top(dodge); Reach(dodge, () => E<TianChouShandaoCompletedEvent>(dodge).Any());
        var arrow = E<TianChouShandaoNativeIssuedEvent>(dodge).Single(e => e.Kind == CardKind.ArrowBarrage);
        Need(E<GroupResponseEvent>(dodge).Where(e => e.ResolutionId == arrow.UseId).Select(e => e.ResponderSeat).SequenceEqual([1, 2, 3]) &&
            E<GroupResponseEvent>(dodge).Where(e => e.ResolutionId == arrow.UseId).All(e => e.UsedResponse && e.ResponseCardId is > 0 && e.RequiredCardKind == CardKind.Dodge) && !E<DamageAppliedEvent>(dodge).Any(),
            "Every actual native ArrowBarrage recipient personally pays an entity Dodge through the ordinary group response path, rather than a simulated response or fixed damage."); _ = Cold(dodge, dodgeRegistry);

        var (blocked, blockedRegistry) = Start("shield"); Begin(blocked); Select(blocked, 1); Top(blocked); Reach(blocked, () => E<TianChouShandaoCompletedEvent>(blocked).Any());
        Need(E<TianChouShandaoTopPaidEvent>(blocked).Single().OwnerSeat == 1 && E<TianChouShandaoNativeIssuedEvent>(blocked).Length == 0 && E<TianChouShandaoTrickSkippedEvent>(blocked).Length == 2,
            "Real native target prohibitions filter both tricks without undoing the already completed top placement or silently bypassing target legality."); _ = Cold(blocked, blockedRegistry);

    }
    public static void PaidLossDeathAndFrozenParticipantContinuationCold()
    {
        foreach (var scenario in new[] { "loss", "death", "foreign-death" })
        {
            var (g, registry) = Start(scenario); Begin(g); Select(g, scenario == "foreign-death" ? [1, 2] : [0, 1]); Top(g);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TianChouShandao is not null).Id;
            g = Cold(g, registry); Continue(g);
            if (scenario == "death")
            {
                Reach(g, () => E<TianChouShandaoCompletedEvent>(g).Any(e => e.FrameId == frame));
                Need(!V(g).IsAlive && g.State.Winner == Winner.None && E<TianChouShandaoTopPaidEvent>(g).Count(e => e.FrameId == frame) == 1 && !E<TianChouShandaoNativeIssuedEvent>(g).Any(),
                    "Real payment-child damage kills the non-Lord source through actual dying/death cleanup; the completed first top is preserved and a dead actor makes no further selections or card uses.");
            }
            else
            {
                Card(g, scenario == "foreign-death" ? 2 : 1);
                if (scenario == "loss") Need(V(g).Skills!.All(s => s.ContentId != Skill), "Actual HP-conditioned source loss has already removed Shandao before the next participant's payment.");
                else Need(!V(g, 1).IsAlive && g.State.Winner == Winner.None, "The first selected foreign payer has actually died in its native movement child.");
                g = Cold(g, registry); Top(g); Reach(g, () => E<TianChouShandaoCompletedEvent>(g).Any(e => e.FrameId == frame));
                var first = E<TianChouShandaoNativeIssuedEvent>(g).Single(e => e.Kind == CardKind.FiveGrains);
                Need(E<TianChouShandaoTopPaidEvent>(g).Count(e => e.FrameId == frame) == 2 && E<TianChouShandaoNativeReturnedEvent>(g).Count(e => e.FrameId == frame) == 2 &&
                    E<TianChouShandaoCompletedEvent>(g).Count(e => e.FrameId == frame) == 1,
                    "The original once-paid source generation drains its child and resumes every remaining actual payment and both exact native returns once.");
                if (scenario == "foreign-death") Need(E<TianChouShandaoNativeTargetEvent>(g).Where(e => e.UseId == first.UseId).Select(e => e.TargetSeat).SequenceEqual([2]) &&
                    E<TianChouShandaoNativeTargetEvent>(g).Where(e => e.UseId != first.UseId).Select(e => e.TargetSeat).SequenceEqual([3]),
                    "The selected dead payer is removed from current legal FiveGrains recipients, while the original frozen beneficiary set still defines the later complement and excludes the source.");
            }
            _ = Cold(g, registry);
        }
    }
    public static void SilverLionWoodenOxAndPrivateSelectionCold()
    {
        foreach (var scenario in new[] { "lion", "ox" })
        {
            var (g, registry) = Start(scenario); Use(g, "draw"); Play(g);
            var kind = scenario == "lion" ? CardKind.SilverLion : CardKind.WoodenOx; var equipment = V(g).Hand.First(c => c.Kind == kind).Id;
            Use(g, "place", [0], [equipment]); Play(g); int? grain = null;
            if (scenario == "lion") { Use(g, "lose"); Play(g); }
            else { grain = V(g).Hand.First(c => c.Id != equipment).Id; Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], g.Revision, P(g)!.PromptId)); Play(g); }
            Begin(g); Select(g, 0); var prompt = P(g)!; var before = State(g);
            Need(prompt.IsPrivate && prompt.Choices.Any(c => c.Cards.SequenceEqual([equipment]) && c.Parameters["source-zone"] == nameof(CardZoneKind.Equipment)) &&
                !g.Submit(new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId("forged-card-slot"), g.Revision)).Accepted && State(g) == before,
                "The private menu exposes the source's real equipment and rejects an invented slot without moving cards.");
            g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([equipment]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == (scenario == "lion" ? Health : Grain));
            Need(!E<TianChouShandaoNativeIssuedEvent>(g).Any() && E<TianChouShandaoTopPaidEvent>(g).Single().CardId == equipment,
                "The real armor or treasure left Equipment directly for DrawPile before the native recovery/grain subtree and before the first trick.");
            g = Cold(g, registry); Continue(g); Reach(g, () => E<TianChouShandaoCompletedEvent>(g).Any());
            Need(g.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DrawPile && m.Reason.Value == TopReason) == 1 &&
                E<TianChouShandaoNativeReturnedEvent>(g).Length == 2, "A recovered equipment-payment child cannot repeat its original top move or either native trick.");
            if (scenario == "lion") Need(V(g).Hp == V(g).MaxHp, "The actual SilverLion removal heals its owner through the genuine HP child exactly once.");
            else Need(g.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
                V(g).WoodenOxGrainCount == 0 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0),
                "The actual Ox grain spills once and its real nested damage child drains before either native global trick continues.");
            _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static TianChouShandaoReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.TianChouShandao is not null)?.TianChouShandao;
    private static string State(GameEngine g) => JsonSerializer.Serialize(g.ResolutionStack) + JsonSerializer.Serialize(g.CreateSnapshot(0)) + g.Events.Count;
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold command replay must reproduce the private prompt, frozen participants and paid native children exactly."); return restored; }
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Shandao command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the real human answers its own prompt."); var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing exact Shandao choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g)
    { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") is "continue" or "after-child"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, "top-and-two-tricks", [], [], g.Revision, P(g)!.PromptId));
    private static void Select(GameEngine g, params int[] seats) { foreach (var s in seats) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([s])); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "confirm"); }
    private static void Top(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card");
    private static void Card(GameEngine g, int seat) => Reach(g, () => Current(g) is { Stage: TianChouShandaoStage.Card } r && r.Cursor < r.Targets.Count && r.Targets[r.Cursor] == seat && P(g)?.SkillPrompt?.SkillId == Skill);
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static string Diagnostic(GameEngine g) => $"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};cursor={Current(g)?.Cursor};tops={E<TianChouShandaoTopPaidEvent>(g).Length};uses={E<TianChouShandaoNativeIssuedEvent>(g).Length}";
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Shandao ended before its real boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p is { Kind: DecisionKind.SelectHarvestCard }) Answer(g, _ => true);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") is "continue" or "after-child") == true) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected real Shandao prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Shandao boundary: " + Diagnostic(g));
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tian-chou", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:tian-chou");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Commit}}}","revision":1,"triggers":[{"id":"actual-native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["fiveGrains","arrowBarrage"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "原生小夹具" }, [Quiet] = new { name = "其他实际阶段", description = "仅跳出牌" },
                [Commit] = new { name = "真正使用锦囊", description = "实际提交子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Health] = new { name = "原生回复子窗", description = "实际狮子回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, bool options = false)
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}"));
                labels[id] = options ? new { name = id, description = "真实子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : new { name = id, description = "真实原生规则" };
            }
            var skills = new List<string> { Driver, Health, Commit };
            if (scenario == "loss") { Add(Source, "\"hpSkillGrants\":[{\"id\":\"full\",\"skillId\":\"ol:shandao\",\"minimumHp\":9}]"); skills.Add(Source); }
            else skills.Add(Skill);
            if (scenario is "plain" or "exhaust" or "loss" or "death" or "foreign-death")
            {
                var tail = scenario == "exhaust" ? ",{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}" : scenario == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" :
                    scenario is "death" or "foreign-death" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : "";
                Add(Watch, "\"triggers\":[{\"id\":\"actual-top-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"" + TopReason + "\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}" + tail + "]}]", true);
                if (scenario != "foreign-death") skills.Add(Watch);
            }
            if (scenario == "counterspell") Add(Empty, "\"triggers\":[{\"id\":\"real-empty\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]");
            if (scenario == "shield") Add(Shield, "\"cardPolicies\":[{\"id\":\"real-target-ban\",\"kind\":\"prohibitTarget\",\"cardKinds\":[\"fiveGrains\",\"arrowBarrage\"]}]");
            if (scenario == "ox")
            {
                Add(Grain, "\"triggers\":[{\"id\":\"actual-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]", true); skills.Add(Grain);
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:tc-pick-owner", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实田畴", "ol-tian-chou", "fixture:tc-pick-owner", "qun", 8, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:tc-peer-" + i).ToArray();
            foreach (var (peer, i) in peers.Select((peer, i) => (peer, i)))
            {
                var pick = "fixture:tc-pick-" + i; b.AddSkill(new(pick, "其他角色", "固定公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - i)) });
                var extra = new List<string> { Quiet }; if (scenario == "counterspell") extra.Add(Empty); if (scenario == "shield") extra.Add(Shield); if (scenario == "foreign-death" && i == 0) extra.Add(Watch);
                b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 12, extra));
            }
            b.AddCard(new("fixture:tc-lion", "白银狮子", "装备牌", "真实离场回复", CardKind.SilverLion)); b.AddCard(new("fixture:tc-ox", "木牛流马", "装备牌", "真实粮离场", CardKind.WoodenOx));
            var ids = scenario == "counterspell" ? new[] { "standard:nullification" } : scenario == "dodge" ? ["standard:dodge"] : scenario is "lion" or "ox" ? ["standard:slash", "fixture:tc-lion", "fixture:tc-ox"] : ["standard:slash"];
            b.AddDeck(new("fixture:tc-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, scenario == "exhaust" ? 20 : 100).Select(i => new ContentDeckPhysicalCard(ids[i % ids.Length], new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i / ids.Length % 4], i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实善刀命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:tc-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
