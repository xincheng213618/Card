using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class ExtraEffectsAndRankGuessChecks
{
    private const string Feng = "ol:fengzi", Ji = "ol:jizhan", Fu = "ol:fusong", Driver = "fixture:extra-rank-driver";
    private const string Watch = "fixture:extra-rank-watch", Quiet = "fixture:extra-rank-quiet", Owner = "fixture:extra-rank-owner", Mode = "identity:classic-extra-rank";
    private const string Before = "fixture:extra-rank-before-harvest";
    private const string Jianxiong = "classic:jianxiong";
    public static void ActualPeachAndDoubleAlcoholBonusColdReplay()
    {
        var (peach, peachRegistry) = Start("peach"); Use(peach, "hurt2"); Play(peach); var hp = V(peach, 0).Hp;
        Native(peach, A(peach, LegalActionKind.Peach)); PayExtra(peach); Play(peach);
        Need(V(peach, 0).Hp == hp + 2 && E<RecoveryAppliedEvent>(peach).Count(e => e.TargetSeat == 0) == 2, "The same physical Peach recovers twice without another Use or cost.");
        _ = Cold(peach, peachRegistry);
        var (wine, registry) = Start("alcohol"); Native(wine, A(wine, LegalActionKind.Alcohol)); PayExtra(wine); Play(wine);
        Need(V(wine, 0) is { HasAlcoholEffect: true, AlcoholDamageBonus: 2 }, "The extra Wine effect stacks two actual damage bonuses instead of collapsing into a bool.");
        wine = Cold(wine, registry); var before = V(wine, 1).Hp; End(wine); Reach(wine, () => Optional(P(wine), Driver)); Activate(wine);
        Reach(wine, () => P(wine)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        Answer(wine, c => c.Targets.SequenceEqual([1])); Play(wine);
        Need(V(wine, 1).Hp == before - 3 && !V(wine, 0).HasAlcoholEffect && V(wine, 0).AlcoholDamageBonus is null,
            "A subsequent native virtual Slash consumes both issued Wine bonuses once, including after real checkpoint replay.");
    }
    public static void ActualGlobalExtraEffectRetainsLegitimatelyClaimedMaterialAndColdCounterspell()
    {
        var (g, registry) = Start("claimed-arrows"); var before = Enumerable.Range(1, 3).Select(s => V(g, s).Hp).ToArray();
        Native(g, A(g, LegalActionKind.ArrowBarrage)); PayExtra(g);
        Reach(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        Reach(g, () => E<ExtraCardEffectStartedEvent>(g).Any() && P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var issued = E<ExtraCardEffectIssuedEvent>(g).Single();
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.CardUseFrameId); var material = use.PhysicalCardIds!.Single();
        Need(E<ProgramDamageCardsClaimedEvent>(g).Count(e => e.SkillId == Jianxiong && e.CardIds.Contains(material)) == 1 &&
            g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1)) == 1 &&
            V(g, 1).Hand.Any(c => c.Id == material), "The real first native damage issues one legal Jianxiong claim of the original entity into its recipient's hand.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass"); Play(g);
        Need(Enumerable.Range(1, 3).All(s => V(g, s).Hp == before[s - 1] - 2) &&
            E<DamageAppliedEvent>(g).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3, 1, 2, 3]), "Claiming its physical material leaves the same already-paid global effect active for all of both native rounds.");
        Need(E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == issued.ActionId) == 1 &&
            E<GroupCardUsedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            g.CardMovements.All(m => m.CardId != material || m.Reason != CardMoveReasons.UseFinished) &&
            E<ProgramDamageCardsClaimedEvent>(g).Count(e => e.CardIds.Contains(material)) == 1,
            "The original Use is accepted, announced and completed once without reclaiming, repaying or discarding its legitimately claimed material after a cold second counterspell window.");
        _ = Cold(g, registry);
    }
    public static void ActualSecondSnatchRechoosesPublicCardAndRetainsMaterial()
    {
        var (g, registry) = Start("snatch-public"); Use(g, "draw24"); Play(g);
        foreach (var kind in new[] { CardKind.GudingBlade, CardKind.BaguaFormation })
        { Use(g, "place", cards: [V(g, 0).Hand.First(c => c.Kind == kind).Id], targets: [1]); Play(g); }
        var first = V(g, 1).Equipment.First(c => c.Kind == CardKind.GudingBlade).Id;
        Native(g, A(g, LegalActionKind.Snatch, target: 1, publicCard: first)); PayExtra(g);
        Reach(g, () => P(g) is { Kind: DecisionKind.SelectTargetCard } && g.ResolutionStack.OfType<TargetCardSelectionFrame>().Any(f => f.ExtraForeignSelection is not null));
        var issued = E<ExtraCardEffectIssuedEvent>(g).Single(); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.CardUseFrameId);
        Need(g.CardMovements.All(m => m.CardId != use.CardId || m.Reason != CardMoveReasons.UseFinished), "The trick material remains in Processing until the additional public-card choice returns.");
        var armor = V(g, 1).Equipment.Single(c => c.Kind == CardKind.BaguaFormation).Id;
        Need(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([armor])) && P(g)!.Choices.Any(c => c.Cards.Count == 0),
            "The second Snatch exposes remaining public equipment and opaque hand slots without foreign hand IDs.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([armor])); Play(g);
        Need(V(g, 0).Hand.Any(c => c.Id == first) && V(g, 0).Hand.Any(c => c.Id == armor) && E<TargetCardTakenEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 2,
            "The two actual effects acquire independently chosen equipment entities and settle the same Use once.");
    }
    public static void ActualRankEqualityPublicRevealGainAndPaidChildColdReturn()
    {
        foreach (var branch in new[] { "greater", "less" })
        {
            var (g, registry) = Start("guess", stopAtDraw: true, child: true); Activate(g); Reach(g, () => Choice(g));
            Need(P(g)!.Choices.All(c => c.Parameters["branch"] != "stop") && g.CreateSnapshot(1).PublicRevealedCards.Count == 1,
                "The first top reveal is public and requires at least one strict comparison.");
            g = Cold(g, registry); Answer(g, c => c.Parameters["branch"] == branch);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ExtraEffectsAndRankGuess is not null);
            Need(root.ExtraEffectsAndRankGuess is { Stage: ExtraEffectsAndRankGuessStage.GainChildren, RevealedCardIds.Count: 2 } &&
                E<RankGuessComparedEvent>(g).Single() is { PreviousRank: 7, NewRank: 7, Correct: false }, "A tie fails both guesses and includes the failed comparison entity in the paid gain.");
            var moves = g.CardMovements.Count(m => m.Reason.Value == "skill-program.rank-guess.gain");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
            Need(g.CardMovements.Count(m => m.Reason.Value == "skill-program.rank-guess.gain") == moves && moves == 2 &&
                g.CardMovements.Count(m => m.Reason == CardMoveReasons.Draw && m.To == CardLocation.Hand(0)) == 0,
                "The normal draw was replaced and the issued two-card gain is not repeated after its real movement observer returns.");
        }
    }
    public static void ActualDeathRecipientOwnChoicePermanentGrantAndOwnerDecline()
    {
        foreach (var chosen in new[] { Feng, Ji })
        {
            var (g, registry) = Start("death");
            var victim = Enumerable.Range(1, 3).First(s => V(g, s).Role != Role.Lord && V(g, s).Skills!.Any(skill => skill.ContentId == Fu));
            Need(V(g, victim).Role != Role.Lord && V(g, victim).Skills!.Any(s => s.ContentId == Fu), "The actual non-Lord victim owns the configured death grant.");
            Use(g, "kill", targets: [victim]);
            Reach(g, () => Choice(g) && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ExtraEffectsAndRankGuess?.Stage == ExtraEffectsAndRankGuessStage.DeathSkill));
            Need(!V(g, victim).IsAlive && g.State.Winner == Winner.None, "The real non-Lord death retains a living match while the issued recipient is still choosing.");
            Need(P(g) is { PlayerSeat: 0 } && E<DeathRecipientSelectedEvent>(g).Single() is { RecipientSeat: 0 } selected && selected.RecipientMaxHp > selected.OwnerMaxHp,
                "The real dead owner chooses a greater-MaxHP recipient, then that recipient owns the skill choice.");
            Need(P(g)!.Choices.Count == 2 && P(g)!.Choices.All(c => c.Parameters["branch"] == "skill"), "The selected recipient must choose one of the exact two skills, without an invented decline option.");
            g = Cold(g, registry); Answer(g, c => c.Parameters["grant-skill"] == chosen); Play(g);
            Need(V(g, 0).Skills!.Any(s => s.ContentId == chosen), "Either selected full skill is acquired by the recipient, independently of the dead source.");
            g = Cold(g, registry); End(g); Reach(g, () => g.CreateSnapshot(0).CurrentSeat == 0 && P(g) is { Kind: DecisionKind.PlayCard } || chosen == Ji && Optional(P(g), Ji));
            Need(V(g, 0).Skills!.Any(s => s.ContentId == chosen), "The acquired skill survives later actual turns and real command replay.");
        }
        var (decline, declineRegistry) = Start("death-owner-decline"); Use(decline, "self-kill");
        Reach(decline, () => Optional(P(decline), Fu)); Activate(decline); Reach(decline, () => Choice(decline));
        decline = Cold(decline, declineRegistry); Answer(decline, c => c.Parameters["branch"] == "decline");
        Reach(decline, () => !decline.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ExtraEffectsAndRankGuess is not null));
        Need(!V(decline, 0).IsAlive && !E<DeathRecipientSelectedEvent>(decline).Any() && !E<DeathRecipientSkillChosenEvent>(decline).Any(),
            "The actual dead owner retains the optional right to decline choosing any recipient.");
    }
    private static bool Choice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "extra-effects-rank-guess") == true;
    private static string Embedded(string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-wang-rong.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Real extra-effect command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static LegalAction A(GameEngine g, LegalActionKind kind, int? target = null, int? publicCard = null) => g.GetLegalActions().First(a => a.Kind == kind && (target is null || a.TargetSeat == target) && (publicCard is null || a.TargetCardId == publicCard));
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void PayExtra(GameEngine g) { Reach(g, () => Optional(P(g), Feng)); Activate(g); Reach(g, () => Choice(g)); Answer(g, c => c.Parameters["branch"] == "cost"); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 160; i++)
        {
            if (done()) return;
            Need(g.CreateSnapshot(0).Status != EngineStatus.Completed, "The fixed actual fixture ended before its expected rule boundary. " +
                JsonSerializer.Serialize(new { g.State.Winner, g.State.TurnNumber, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, Player = V(g, s), Prompt = g.CreateSnapshot(s).PendingDecision }).ToArray(),
                    Bindings = E<ProgramBindingStartedEvent>(g).ToArray(), Deaths = E<PlayerDiedEvent>(g).ToArray(), Selected = E<DeathRecipientSelectedEvent>(g).ToArray(), Skills = E<DeathRecipientSkillChosenEvent>(g).ToArray(),
                    Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() }));
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Kind == DecisionKind.SelectTargetCard) Answer(g, _ => true);
                else if (p.Kind == DecisionKind.SelectHarvestCard) Answer(g, _ => true);
                else if (p.Kind == DecisionKind.PlayCard) End(g);
                else throw new InvalidOperationException($"Unexpected actual extra effect prompt {p.Kind}: {p.Prompt}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed extra effect fixture exceeded its bounded real command sequence.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Real checkpoint replay retains exact paid cards, public choices, extra-effect cursor and Wine count."); return copy; }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(string scenario, bool stopAtDraw = false, bool child = false, bool stopAtVirtual = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, child));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "death-owner-decline" ? Role.Rebel : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (stopAtDraw) Reach(g, () => scenario == "guess-stop" ? P(g)?.Kind == DecisionKind.ProgramTopReorder : Optional(P(g), Ji)); else if (stopAtVirtual) Reach(g, () => Optional(P(g), Driver)); else Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario, bool child) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("extra-effect-rank-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var content = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            if (scenario != "claimed-arrows-baseline") foreach (var (id, program) in content.Programs) b.AddSkill(new(id, content.Presentations[id].Name, content.Presentations[id].Description) { Program = program, ProgramPresentation = content.Presentations[id] });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt2","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"self-kill","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
                {"id":"draw24","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"slash-quota","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":10}]},
                {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.rank-guess.gain"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            if (scenario is "virtual" or "alcohol") rules["skills"]![0]!["triggers"] = JsonNode.Parse($$"""
                [{"id":"native-virtual","window":"{{(scenario == "virtual" ? "playPhaseStarting" : "discardPhaseStarting")}}","subject":"owner","turnOwnerScope":"own","usageScope":"game","usageLimit":1,"optional":true,
                  "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]
                """);
            if (scenario == "guess-stop") rules["skills"]![0]!["triggers"] = JsonNode.Parse("""
                [{"id":"actual-prepare-top","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":100,"drawPhaseMode":"additive","usageScope":"game","usageLimit":1,
                  "effects":[{"op":"reorderTopCards","target":"owner","amount":4,"exactTopCount":2}]},
                 {"id":"actual-later-draw-adjustment","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":-100,"drawPhaseMode":"additive","usageScope":"game","usageLimit":1,
                  "effects":[{"op":"adjustNormalDraw","target":"owner","amount":1}]}]
                """);
            if (scenario is "counter" or "claimed-arrows" or "claimed-arrows-baseline") rules["skills"]![0]!["viewAs"] = JsonNode.Parse("""[{"id":"counter","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}]""");
            if (scenario == "harvest") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Before}}","revision":1,"triggers":[{"id":"actual-death-before-reveal","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["fiveGrains"],"usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":20}]}]}
                """));
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实牌准备", description = "真实原生规则准备" },
                [Quiet] = new { name = "安静其他角色", description = "通过真实规则限制主动杀" },
                [Watch] = new { name = "真实获得子窗", description = "获得后的真实子事件", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (scenario == "harvest") labels[Before] = new { name = "真实选牌前死亡", description = "通过已存在规则产生真实死亡" };
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description) { Program = program, ProgramPresentation = fixture.Presentations[id], SelectionWeights = id == Quiet ? Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) : new Dictionary<Role, double>() });
            if (scenario is "claimed-arrows" or "claimed-arrows-baseline" or "claimed-slash")
            {
                string DamageSource(string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.damage-skills.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var damage = SkillProgramCatalog.Load(DamageSource("rules.json"), DamageSource("presentation.json"));
                var p = damage.Presentations[Jianxiong]; b.AddSkill(new(Jianxiong, p.Name, p.Description) { Program = damage.Programs[Jianxiong], ProgramPresentation = p });
            }
            var ownerSkills = scenario is "death" or "claimed-arrows-baseline" ? new List<string>() : new List<string> { Feng, Ji, Fu };
            if (child) ownerSkills.Add(Watch); if (scenario == "harvest") ownerSkills.Add(Before);
            b.AddGeneral(new(Owner, "实际规则拥有者", "supporter", Driver, "qun", scenario == "death" ? 8 : 3, ownerSkills, GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:extra-rank-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "wei", scenario == "death" ? 3 : 8,
                scenario == "death" ? [Fu] : scenario == "guess-ai" ? [Ji] : scenario is "claimed-arrows" or "claimed-arrows-baseline" or "claimed-slash" ? [Jianxiong] : []));
            var cards = scenario switch { "slash" or "virtual" or "death" or "claimed-slash" => new[] { "standard:slash" }, "peach" => ["standard:peach"],
                "alcohol" => ["standard:alcohol"], "barbarian" => ["standard:barbarian_assault"], "arrows" or "claimed-arrows" or "claimed-arrows-baseline" => ["standard:arrow_barrage"],
                "counter" => ["standard:draw_two"], "harvest" => ["standard:five_grains"],
                "snatch-public" => ["standard:snatch", "fixture:extra-rank-blade", "standard:bagua"], _ => ["standard:dodge"] };
            if (scenario == "snatch-public") b.AddCard(new("fixture:extra-rank-blade", "古锭刀", "装备牌", "真实原生古锭刀", CardKind.GudingBlade));
            b.AddDeck(new("fixture:extra-rank-deck", "固定小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Spade, scenario == "guess-stop" ? i % 13 + 1 : 7)).ToArray() });
            b.AddMode(new(Mode, "实际额外效果及摸牌死亡", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:extra-rank-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
