using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinWangJiChecks
{
    public static void ActualTargetsPrivateAndNested()
    {
        var (g, r) = Start("nested"); Supply(g); Play(g, LegalActionKind.ArrowBarrage); Reach(g, Flow);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(use.TargetSeats.Count == 3 && P(g)!.Choices.All(c => c.Targets.SequenceEqual([0])), "A real global card excludes every final target and may discard the non-target owner.");
        Require(g.CreateSnapshot(1, false).PendingDecision is null && P(g)!.IsPrivate, "Other viewers cannot see the owner's hand costs.");
        Atomic(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices[0].Id, g.Revision)); Replay(g, r);
        var paid = P(g)!.Choices[0].Cards.Single(); Choose(g, c => c.Cards.SequenceEqual([paid]));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:wj-discard");
        Require(Count(g) == 1 && g.CreateCardZoneDiagnostics().Single(z => z.CardId == paid).Location == CardLocation.DiscardPile &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program.non-final-target.draw"), "Cost and count commit once before the full discard child; reward draw has not happened.");
        Replay(g, r); Choose(g, c => true); Reach(g, p => p.SkillPrompt?.SkillId == "fixture:wj-gain");
        Require(g.CardMovements.Count(m => m.Reason.Value == "skill-program.non-final-target.draw") == 1, "The same living selected actor draws once after the discard child.");
        Replay(g, r); Choose(g, c => true); Settle(g);
        Require(Count(g) == 1 && g.CardMovements.Count(m => m.CardId == paid && m.Reason.Value == "skill-program.non-final-target.discard") == 1, "Nested restore cannot pay or count again.");
        Supply(g); Play(g, LegalActionKind.Duel); Reach(g, Flow);
        var other = P(g)!.Choices.First(c => c.Targets.Single() != 0 && c.Parameters["source-zone"] == "Hand");
        Require(other.Cards.Count == 0 && P(g)!.Choices.Where(c => c.Targets.Single() != 0 && c.Parameters["source-zone"] == "Hand").All(c => c.Cards.Count == 0 && !c.Description.Contains("【")), "Other hand costs are opaque published slots.");
        Atomic(g, new AnswerPromptCommand(0, P(g)!.PromptId, new("unknown"), g.Revision)); Replay(g, r); Answer(g, other); Settle(g); Replay(g, r); Conserve(g);
    }
    public static void NamedCountEndingAndReset()
    {
        foreach (var times in new[] { 0, 2, 5 })
        {
            var (g, r) = Start(); Supply(g);
            for (var i = 0; i < times; i++) { Play(g, LegalActionKind.Duel); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0])); Settle(g); }
            Require(Count(g) == times, "Accepted legal actions count by owner and named skill.");
            if (times == 5) { Driver(g, "empty"); Settle(g); }
            var before = g.CardMovements.Count(m => m.Reason.Value == "skill-program.classic:jinqu.Draw");
            End(g); Reach(g, p => p.SkillPrompt?.SkillId == "classic:jinqu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Replay(g, r); Choose(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            if (times < g.State.Players[0].HandCount)
            {
                Reach(g, Flow); var draft = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().NamedTurnCountFlow!;
                Require(draft.Required == g.State.Players[0].HandCount - times && draft.Stage == "hand", "Ending freezes the actual post-draw hand minus this turn's named count.");
                Require(P(g)!.IsPrivate && g.CreateSnapshot(1, false).PendingDecision is null, "Ending hand choices remain private."); Replay(g, r);
                while (P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "named-turn-flow") == true) Choose(g, c => true);
            }
            Require(g.State.Players[0].HandCount <= times && g.CardMovements.Count(m => m.Reason.Value == "skill-program.classic:jinqu.Draw") == before + 2,
                "Jinqu draws two then discards to X; X zero empties hand and a larger X never creates cards.");
            Replay(g, r); Settle(g); Supply(g); End(g); Reach(g, p => p.SkillPrompt?.SkillId == "classic:jinqu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Choose(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, Flow);
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().NamedTurnCountFlow!.Required == g.State.Players[0].HandCount, "A new actual turn reads zero prior-turn accepts.");
            Replay(g, r); Conserve(g);
        }
    }
    public static void DuplicateGrantsDeclineEquipmentAndLoss()
    {
        var (g, r) = Start(); Driver(g, "grant"); Settle(g); Supply(g);
        Play(g, LegalActionKind.Equip); Settle(g); Require(Count(g) == 0, "Real equipment use does not trigger the non-equipment flow.");
        Play(g, LegalActionKind.Duel); Reach(g, p => p.SkillPrompt?.SkillId == "classic:qizhi" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"));
        Choose(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Settle(g); Require(Count(g) == 0, "Decline does not count and duplicate grants cannot reopen the same action.");
        Play(g, LegalActionKind.Duel); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0]) && c.Parameters["source-zone"] == "Equipment"); Settle(g); Require(Count(g) == 1 && g.CardMovements.Any(m => m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.non-final-target.discard"), "Two actual enabled grants accept one physical equipment payment and one named count.");
        Driver(g, "loss"); Settle(g); Driver(g, "grant"); Settle(g);
        Play(g, LegalActionKind.Duel); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0])); Settle(g);
        Require(Count(g) == 2, "Loss/regrant keeps the owner named skill turn count."); Replay(g, r); Conserve(g);
    }
    public static void PaidOwnerDeath()
    {
        var (g, r) = Start("death"); Supply(g); Play(g, LegalActionKind.ArrowBarrage); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:wj-discard"); Replay(g, r); Choose(g, c => true);
        for (var i = 0; i < 70 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:qizhi"); i++) Tick(g);
        Require(!g.State.Players[0].IsAlive && Count(g) == 1 && !g.CardMovements.Any(m => m.Reason.Value == "skill-program.non-final-target.draw"), "Real paid child death keeps one accepted count and cancels the unpaid reward."); Replay(g, r); Conserve(g);
        (g, r) = Start("target-death"); Supply(g); Play(g, LegalActionKind.Duel); Reach(g, Flow);
        var victim = P(g)!.Choices.First(c => c.Targets.Single() != 0).Targets.Single(); Choose(g, c => c.Targets.SequenceEqual([victim]));
        Settle(g);
        Require(g.State.Players[0].IsAlive && !g.State.Players[victim].IsAlive && Count(g) == 1 && !g.CardMovements.Any(m => m.Reason.Value == "skill-program.non-final-target.draw"), "A discarded card's full child can kill its selected other owner; the later draw requires that same owner still alive."); Replay(g, r); Conserve(g);
    }
    public static void ExtraActualPlayAndEndingGain()
    {
        var (g, r) = Start("extra"); Supply(g); Play(g, LegalActionKind.Duel); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0])); Settle(g);
        var turn = g.Events.Select(e => e.Payload).OfType<NamedTurnSkillChoiceCommittedEvent>().Last().TurnNumber;
        End(g); Settle(g); Supply(g); Play(g, LegalActionKind.Duel); Reach(g, Flow); Choose(g, c => c.Targets.SequenceEqual([0])); Settle(g);
        Require(g.Events.Select(e => e.Payload).OfType<NamedTurnSkillChoiceCommittedEvent>().Count(e => e.TurnNumber == turn) == 2 &&
            g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Any(e => e.Phase == TurnPhase.Play && e.Started), "A real inserted Play and normal Play share one actual turn count.");
        Replay(g, r); Conserve(g);
        (g, r) = Start("ending-gain"); Supply(g); End(g); Reach(g, p => p.SkillPrompt?.SkillId == "fixture:wj-ending-gain");
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.NamedTurnCountFlow is not null), "Jinqu cannot freeze discard size before its Draw2 gain children finish.");
        Replay(g, r); Choose(g, c => true); Reach(g, Flow);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().NamedTurnCountFlow!.Required == g.State.Players[0].HandCount &&
            g.CardMovements.Count(m => m.Reason.Value == "skill-program.fixture:wj-ending-gain.Draw") == 3, "The dynamic discard includes the child's real extra three gains. required=" + g.ResolutionStack.OfType<ProgramSkillFrame>().Last().NamedTurnCountFlow!.Required + " hand=" + g.State.Players[0].HandCount + " childDraws=" + g.CardMovements.Count(m => m.Reason.Value == "skill-program.fixture:wj-ending-gain.Draw"));
        Replay(g, r); Conserve(g);
    }
    public static void StrictResources()
    {
        foreach (var body in new[]
        {
            "\"activations\":[{\"id\":\"x\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"discardNonFinalTargetCardThenDraw\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"target\",\"optional\":true,\"effects\":[{\"op\":\"discardNonFinalTargetCardThenDraw\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"crossbow\"],\"optional\":true,\"effects\":[{\"op\":\"discardNonFinalTargetCardThenDraw\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"discardHandToNamedTurnCount\",\"target\":\"owner\",\"skillIds\":[\"classic:qizhi\"]}]}]"
        })
        {
            var rejected = false; try { SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:bad","revision":1,{{body}}}]}""", """{"schemaVersion":3,"skills":{"fixture:bad":{"name":"错","description":"错误资源"}}}"""); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Generic target/count operations reject incorrect entry windows and participants.");
        }
        foreach (var source in new[] { "fixture:missing", "standard:none" })
        {
            var rejected = false; try { ContentRegistry.Build(new StandardContentPackage(), new InvalidCountSource(source)); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains(source, StringComparison.Ordinal); }
            Require(rejected, "A named count reference must resolve to an existing declared accepting producer.");
        }
    }
    public static void LiuliFinalTargetsAndNativeAi()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new RedirectFixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5, ModeId = "identity:classic-wj-redirect", UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, r);
        Accept(g, new StartGameCommand()); var weapon = g.CreateSnapshot(0, true).Players[0].Hand.Single(c => c.Kind == CardKind.FangtianHalberd);
        Accept(g, new PlayCardCommand(0, weapon.Id, [], g.Revision, P(g)!.PromptId)); Settle(g);
        var slash = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 3]));
        Accept(g, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, P(g)!.PromptId, slash.PlayedCardKind) { ConversionSource = slash.ConversionSource });
        Reach(g, Flow); var use = g.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(use.TargetSeats.SequenceEqual([2, 4]) && P(g)!.Choices.All(c => !use.TargetSeats.Contains(c.Targets.Single())) &&
            g.Events.Select(e => e.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count(e => e.SkillId == "fixture:wj-redirect" && e.Activated) == 2,
            "Both real Liuli redirections finish before one action offers only seats outside every finalized target.");
        Replay(g, r); Choose(g, c => true); Settle(g); Require(Count(g) == 1, "One multi-target action counts once after both redirects."); Replay(g, r);
        (g, r) = Start("ai"); End(g);
        for (var i = 0; i < 150 && !g.Events.Select(e => e.Payload).OfType<NamedTurnSkillChoiceCommittedEvent>().Any(e => e.OwnerSeat != 0); i++) Tick(g);
        Require(g.Events.Select(e => e.Payload).OfType<NamedTurnSkillChoiceCommittedEvent>().Any(e => e.OwnerSeat != 0), "Normal AdvanceOneStep AI accepts a real own-turn non-target discard without inspecting hidden faces."); Replay(g, r); Conserve(g);
    }
    private static (GameEngine, ContentRegistry) Start(string mode = "normal")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(mode));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-wang-ji-check", UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:wj-owner", g.Revision, P(g)!.PromptId)); Settle(g); return (g, r);
    }
    private static int Count(GameEngine g) => g.Events.Select(e => e.Payload).OfType<NamedTurnSkillChoiceCommittedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == "classic:qizhi");
    private static bool Flow(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "named-turn-flow");
    private static PendingDecision? P(GameEngine g) => g.PendingDecision ?? Enumerable.Range(0, g.State.Players.Count).Select(s => g.CreateSnapshot(s, true).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Driver(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, "fixture:wj-driver", id, [], [], g.Revision, P(g)!.PromptId));
    private static void Supply(GameEngine g) { Driver(g, "supply"); Settle(g); }
    private static void Play(GameEngine g, LegalActionKind kind) { var a = g.GetHumanLegalActions().First(a => a.Kind == kind); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Choose(GameEngine g, Func<PromptChoice, bool> predicate) => Answer(g, P(g)!.Choices.First(predicate));
    private static void Answer(GameEngine g, PromptChoice choice) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, g.Revision)); }
    private static void Tick(GameEngine g)
    {
        if (P(g) is { PlayerSeat: 0 } p)
        {
            if (p.Kind == DecisionKind.PlayCard) { End(g); return; }
            if (p.Kind == DecisionKind.DiscardCards) { Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision)); return; }
            if (p.Choices.Count > 0) { Answer(g, p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "activate") ?? p.Choices[0]); return; }
            throw new InvalidOperationException("Unhandled fixture prompt " + JsonSerializer.Serialize(p));
        }
        Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var i = 0; i < 220; i++) { if (P(g) is { } p && predicate(p)) return; Tick(g); } throw new InvalidOperationException("Missing Wang Ji boundary " + JsonSerializer.Serialize(P(g))); }
    private static void Settle(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && !g.ResolutionStack.Any(f => f is ProgramSkillFrame or CardUseFrame));
    private static void Replay(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(Enumerable.Range(0, g.State.Players.Count).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false)) == SnapshotJson.Serialize(copy.CreateSnapshot(s, false))) && g.CardMovements.SequenceEqual(copy.CardMovements) && JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) && g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), "Every viewer, typed frame, fact and actual movement must restore through accepted command JSON."); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected fixture command."); }
    private static void Atomic(GameEngine g, GameCommand command) { var before = GameCheckpointJson.Serialize(g.CreateCheckpoint()); Require(!g.Submit(command).Accepted && before == GameCheckpointJson.Serialize(g.CreateCheckpoint()), "Invalid choice must reject atomically."); }
    private static void Conserve(GameEngine g) => Require(g.CreateCardZoneDiagnostics().Count == 64 && g.CreateCardZoneDiagnostics().Select(z => z.CardId).Distinct().Count() == 64, "Every fixture entity belongs to exactly one zone.");
    private static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wang-ji", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var pause = """{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}""";
            var after = mode is "death" or "target-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"fixture:wj-driver","revision":1,"activations":[
              {"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":16}]},
              {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
              {"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:qizhi"]}]},
              {"id":"loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:qizhi"],"sourceBind":"standard:none"}]}
             ]},
             {"id":"fixture:wj-discard","revision":1,"triggers":[{"id":"pause","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.non-final-target.discard"],"optional":false,"effects":[{{pause}}{{after}}]}]},
             {"id":"fixture:wj-gain","revision":1,"triggers":[{"id":"pause","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.non-final-target.draw"],"optional":false,"effects":[{{pause}}]}]},
             {"id":"fixture:wj-phase","revision":1,"triggers":[{"id":"insert","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
             {"id":"fixture:wj-ending-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:jinqu.Draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{{pause}},{"op":"draw","target":"owner","amount":3}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:wj-driver":{"name":"驱动","description":"真实补牌与授予"},"fixture:wj-discard":{"name":"弃牌暂停","description":"完整移动子链","optionLabels":{"continue":"继续"}},"fixture:wj-gain":{"name":"摸牌暂停","description":"真实获得子链","optionLabels":{"continue":"继续"}},"fixture:wj-phase":{"name":"额外出牌","description":"真实阶段调度"},"fixture:wj-ending-gain":{"name":"进趋摸牌子链","description":"真实增加手牌","optionLabels":{"continue":"继续"}}}}""");
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, pair.Key) { Program = pair.Value });
            b.AddGeneral(new("fixture:wj-owner", "王基机制", "supporter", mode == "ai" ? "standard:none" : "classic:qizhi", "wei", 20, ["classic:jinqu", "fixture:wj-driver", ..(mode is "nested" or "death" ? new[] { "fixture:wj-discard", "fixture:wj-gain" } : []), ..(mode == "extra" ? new[] { "fixture:wj-phase" } : []), ..(mode == "ending-gain" ? new[] { "fixture:wj-ending-gain" } : [])]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:wj-{i}", "目标" + i, "supporter", mode == "ai" ? "classic:qizhi" : "standard:none", "wei", 20, mode == "target-death" ? ["fixture:wj-discard"] : []));
            b.AddDeck(new("fixture:wj-deck", "固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(i % 4 == 0 ? "standard:qinggang_sword" : i % 4 == 1 ? "standard:arrow_barrage" : "standard:duel", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-wang-ji-check", "王基机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:wj-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wj-owner", "fixture:wj-1", "fixture:wj-2", "fixture:wj-3"]));
        }
    }
    private sealed class RedirectFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wj-redirect", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"fixture:wj-swap","revision":1,"viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]},
              {"id":"fixture:wj-redirect","revision":1,"triggers":[{"id":"redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},{"op":"redirectCurrentAttack","target":"selectedTarget"}]}]}
            ]}
            """, """{"schemaVersion":3,"skills":{"fixture:wj-swap":{"name":"转换","description":"真实转换杀"},"fixture:wj-redirect":{"name":"流离","description":"真实重定向"}}}""");
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, pair.Key) { Program = pair.Value });
            var ids = Enumerable.Range(0, 5).Select(i => "fixture:wj-redirect-" + i).ToArray();
            for (var i = 0; i < 5; i++) b.AddGeneral(new(ids[i], "真实重定向", "zhao_yun", "fixture:wj-swap", AdditionalSkillIds: i is 2 or 3 ? ["classic:qizhi", "fixture:wj-redirect"] : ["classic:qizhi"]));
            b.AddDeck(new("fixture:wj-redirect-deck", "固定已验证种子", 2, 0, [new("classic:fangtian-halberd", 8), new("standard:dodge", 24), new("standard:peach", 28)]));
            b.AddMode(new("identity:classic-wj-redirect", "流离最终目标", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:wj-redirect-deck", GeneralCandidateCount: 1, GeneralPoolIds: ids));
        }
    }
    private sealed class InvalidCountSource(string source) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-invalid-turn-count", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-count","revision":1,"triggers":[{"id":"ending","window":"turnEnding","subject":"owner","optional":true,"effects":[{"op":"discardHandToNamedTurnCount","target":"owner","skillIds":["{{source}}"]}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:invalid-count":{"name":"错误计数来源","description":"必须声明真实producer"}}}""");
            builder.AddSkill(new("fixture:invalid-count", "错误来源", "编译拒绝") { Program = catalog.Programs["fixture:invalid-count"] });
        }
    }
}
