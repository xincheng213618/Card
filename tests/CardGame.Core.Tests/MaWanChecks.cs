using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaWanChecks
{
    private const string Hunjiang = "ol:hunjiang", Driver = "fixture:mw-driver", Watch = "fixture:mw-draw", Quiet = "fixture:mw-quiet";
    private const string Shield = "fixture:mw-shield", Redirect = "fixture:mw-redirect", Source = "fixture:mw-hp-source", LateShield = "fixture:mw-late-shield", Owner = "fixture:mw-owner", Mode = "identity:classic-ma-wan-fixture";

    public static void SimultaneousPrivateCommitmentsUnanimousSupplementActualDrawAndCold()
    {
        foreach (var scenario in new[] { "normal", "draw", "mixed", "loss" })
        {
            var (g, registry) = Start(scenario);
            if (scenario == "loss") Need(V(g, 0).Hp == 9 && E<HpConditionalSkillGrantChangedEvent>(g).Any(e => e.OwnerSeat == 0 && e.SkillId == Hunjiang && e.Added),
                "The loss fixture begins with a genuine HP-nine conditional Hunjiang source, rather than a printed source or a fake replacement.");
            if (scenario is "draw" or "mixed") { Use(g, "hurt", [1]); Play(g); }
            if (scenario == "draw") { Use(g, "hurt", [2]); Play(g); }
            var participants = scenario == "normal" ? new[] { 1, 2, 3 } : new[] { 1, 2 };
            var before = V(g, 0).HandCount;
            Accept(g, new UseProgramSkillCommand(0, Hunjiang, "simultaneous-range-choices", [], participants, g.Revision, P(g)!.PromptId));
            Reach(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MaWanHunjiang?.Stage == MaWanHunjiangStage.PrivateVotes));
            var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MaWanHunjiang is not null).Id;
            for (var vote = 0; vote < participants.Length; vote++)
            {
                var expected = vote;
                Reach(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == id && f.MaWanHunjiang?.Votes.Count == expected) && P(g)?.PlayerSeat == participants[expected]);
                var chooser = participants[vote];
                Need(P(g) is { IsPrivate: true } && P(g)!.Choices.Count == 2 &&
                    Enumerable.Range(0, 4).Where(s => s != chooser).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                    !E<MaWanHunjiangVoteRevealedEvent>(g).Any() && !E<MaWanHunjiangEntitledEvent>(g).Any() && V(g, 0).HandCount == before,
                    "Every frozen participant sees only their own two choices; previous private votes produce no public result, grant or early draw.");
                g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var expectedDraw = scenario == "mixed" ? 1 : participants.Length;
            var grants = E<MaWanHunjiangEntitledEvent>(g).Where(e => e.FrameId == id).Select(e => e.TargetSeat).ToArray();
            Need(V(g, 0).HandCount == before + expectedDraw && E<MaWanHunjiangDrawIssuedEvent>(g).Single().Count == expectedDraw &&
                E<MaWanHunjiangVoteRevealedEvent>(g).Count() == participants.Length &&
                E<MaWanHunjiangVoteRevealedEvent>(g).All(e => e.Unanimous == (scenario != "mixed")) &&
                grants.SequenceEqual(scenario == "mixed" ? new[] { 2 } : participants) &&
                g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.ma-wan.hunjiang.draw") == expectedDraw,
                "All real commitments reveal together: a mixed vote applies each selected option, while unanimous votes execute both options for every participant and draw actual entities.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Hunjiang), "The real accepted activation consumes exactly this Play phase's single use.");
            Need(V(g, 0).Skills!.Any(s => s.Id == Hunjiang) == (scenario != "loss"), "The paid draw child really loses Hunjiang in the loss fixture, then returns exactly once.");
            if (scenario == "loss")
            {
                var issuedGrant = E<HpConditionalSkillGrantChangedEvent>(g).Single(e => e.OwnerSeat == 0 && e.SkillId == Hunjiang && e.Added);
                Need(V(g, 0).Hp == 8 && E<HpConditionalSkillGrantChangedEvent>(g).Count(e => !e.Added && e.GrantId == issuedGrant.GrantId &&
                    e.SkillInstanceId == issuedGrant.SkillInstanceId && e.Source == issuedGrant.Source) == 1,
                    "The actual paid draw child's native HP loss really revokes its one conditional skill grant before the later use. " +
                    JsonSerializer.Serialize(new { Hp = V(g, 0).Hp, Grants = E<HpConditionalSkillGrantChangedEvent>(g), Loss = E<ProgramSkillHpLostEvent>(g), Paid = E<MaWanHunjiangDrawIssuedEvent>(g) }));
                Slash(g, 3); Reach(g, () => Branch(P(g), "append"));
                Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).Select(c => c.Targets[0]).Order().SequenceEqual([1, 2]),
                    "The already-issued actual-phase entitlement publishes its exact native Slash candidate even after its source skill was lost before that use.");
                g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1])); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Play(g);
                Need(E<MaWanHunjiangTargetAddedEvent>(g).Count() == 1, "A lost printed skill cannot cancel its issued target choice in the executor's pre-receipt guard.");
            }
            _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Ma Wan completed before the required native boundary: " + Diagnostic(g));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Ma Wan prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Ma Wan boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Pending = P(g),
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, Skills = V(g, s).Skills!.Select(x => x.Id) }),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Native = g.Events.Select(e => e.Payload).Where(e => e is ProgramBindingStartedEvent or ProgramBindingResolvedEvent or MaWanHunjiangTargetAddedEvent or MaWanHunjiangRedirectedEvent or DamageAppliedEvent).Select(e => e.ToString()),
        Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) });
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Ma Wan command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Ma Wan preserves every viewer's private commitment boundary, issued actual-phase targets, native draw/payment/response children and original virtual producer return."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ma-wan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ma-wan", "classic:ma-chao");
            var loss = scenario == "loss" ? ", {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scenario == "death" ? ", {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            var redirectTail = scenario == "duplicate" ? ", {\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"redirect-after\",\"options\":[{\"id\":\"continue\"}]}" : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":5}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"strip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]},
                {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"virtual-return","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid-hunjiang-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ma-wan.hunjiang.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]} {{{loss}}} ]}]},
              {"id":"{{{Shield}}}","revision":1,"cardPolicies":[{"id":"native-prohibition","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash"]}]},
              {"id":"{{{Source}}}","revision":1,"hpSkillGrants":[{"id":"actual-nine","skillId":"{{{Hunjiang}}}","minimumHp":9}]},
              {"id":"{{{LateShield}}}","revision":1,"hpSkillGrants":[{"id":"after-real-damage","skillId":"{{{Shield}}}","maximumHp":7}]},
              {"id":"{{{Redirect}}}","revision":1,"triggers":[{"id":"real-extra-redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},{"op":"redirectCurrentAttack","target":"selectedTarget"} {{{redirectTail}}} ]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Shield, Redirect, Source, LateShield };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "马玩实际命令小夹具", optionLabels = id is Driver or Watch || id == Redirect && scenario == "duplicate" ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "马玩原生规则") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:mw-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddGeneral(new(Owner, "马玩真实本人", "ol-ma-wan", "fixture:mw-owner-pick", "qun", 8,
                scenario == "range" ? [Hunjiang, Driver, Watch] : scenario == "human" ? ["classic:mashu", Driver, Watch] : scenario == "loss" ? [Source, "classic:mashu", Driver, Watch] : [Hunjiang, "classic:mashu", Driver, Watch]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:mw-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:mw-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000000d : 100000000d * (3 - index)) });
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "qun", 8,
                    scenario == "human" && index == 0 ? [Hunjiang, "classic:mashu", Watch] : scenario == "redirect" && index == 2 || scenario == "duplicate" && index > 0 ? [Quiet, Redirect] : scenario == "duplicate" && index == 0 ? [Quiet, LateShield] : scenario == "shield" && index == 1 ? [Quiet, Shield] : [Quiet])
                { InitialHp = scenario == "duplicate" && index == 1 ? 1 : scenario == "duplicate" && index == 2 ? 2 : 8 });
            }
            b.AddCard(new("fixture:mw-blade", "青龙偃月刀", "装备牌", "真实攻击范围3武器", CardKind.QinglongCrescentBlade));
            b.AddDeck(new("fixture:mw-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i =>
                new ContentDeckPhysicalCard(scenario == "range" && i % 9 == 8 ? "fixture:mw-blade" : "standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "马玩真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:mw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
