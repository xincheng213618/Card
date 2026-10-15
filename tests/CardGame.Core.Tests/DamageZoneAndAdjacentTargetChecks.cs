using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DamageZoneAndAdjacentTargetChecks
{
    private const string Zhi = "ol:zhidao", Ji = "ol:jili", Driver = "fixture:zone-adjacent-driver", Watch = "fixture:zone-adjacent-watch",
        Loss = "fixture:zone-adjacent-loss", Swap = "fixture:zone-adjacent-swap", Quiet = "fixture:zone-adjacent-quiet",
        Owner = "fixture:zone-adjacent-owner", Mode = "identity:classic-zone-adjacent-fixture";
    public static void ActualEachZonePrivateSelectionOnceAndTurnExpiry()
    {
        var (g, registry) = Start("zones"); PrepareZones(g); var hand = V(g, 1).HandCount; var equip = V(g, 1).Equipment.Single().Id; var judge = V(g, 1).Judgment.Single().Id; var hp = V(g, 1).Hp;
        Use(g, "harm", targets: [1]); Reach(g, () => ZoneChoice(g));
        Need(P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Parameters["source-zone"] == "Hand") && g.CreateSnapshot(1).PendingDecision is null,
            "The owner chooses only opaque foreign hand slots; other viewers cannot see that private decision.");
        g = Cold(g, registry); Answer(g, _ => true); Need(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([equip])), "Public equipment remains an exact physical choice.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([equip])); Need(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([judge])), "Judgment is the third distinct real region.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([judge])); Play(g);
        var paid = E<DamageZoneAcquisitionPaidEvent>(g).Single();
        Need(paid.Receipt.Cards.Count == 3 && paid.Receipt.From.Select(l => l.Zone).SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
            V(g, 1).HandCount == hand - 1 && V(g, 1).Equipment.Count == 0 && V(g, 1).Judgment.Count == 0 && V(g, 1).Hp == hp &&
            paid.Receipt.Cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == "skill-program.damage-zone-acquisition.obtain") == 1),
            "One exact card from each occupied H/E/J region is obtained once, across three actual recovered selection states.");
        Need(E<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 1 && e.TargetSeat == 1 && e.Amount == 1) == 1,
            "Obtaining the actual foreign SilverLion drains its native recovery once before issuing the restriction.");
        Need(!g.GetLegalActions().Any(a => a.TargetSeats.Any(s => s != 0) && a.Kind is not (LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill)),
            "The remaining actual turn cannot designate other players for cards.");
        Use(g, "harm", targets: [2]); Play(g); Need(E<DamageZoneAcquisitionStartedEvent>(g).Count() == 1, "A later real second damage does not obtain another player's cards.");
        End(g); Play(g);
        // Replenish a real Slash after automatic discard so expiry is independent of the retained hand.
        Use(g, "draw24"); Play(g);
        Need(g.GetLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Any(s => s != 0)),
            "The target restriction expires at the actual turn boundary."); _ = Cold(g, registry);
    }
    public static void ActualRedDyingPeachUseJoinsAndReturnsOriginalRescue()
    {
        var (g, registry) = Start("rescue"); Use(g, "hurt"); Play(g); var hp = V(g, 0).Hp; Use(g, "harm8", targets: [1]);
        Reach(g, () => E<AdjacentRedTargetAddedEvent>(g).Any(e => e.ActorSeat != 0));
        var added = E<AdjacentRedTargetAddedEvent>(g).First(e => e.ActorSeat != 0);
        g = Cold(g, registry); Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == added.CardUseFrameId));
        Need(added.OriginalTargets.SequenceEqual([1]) && added.AddedTargets.SequenceEqual([0]) && V(g, 0).Hp == hp + 1 && V(g, 1).Hp > 0 &&
            E<RecoveryAppliedEvent>(g).Any(e => e.SourceSeat == added.ActorSeat && e.TargetSeat == 1) &&
            E<RecoveryAppliedEvent>(g).Any(e => e.SourceSeat == added.ActorSeat && e.TargetSeat == 0) &&
            E<DyingResponseEvent>(g).Count(e => e.ResponderSeat == added.ActorSeat && e.UsedPeach) == 1,
            "An actual foreign self-rescue Peach is a Use, extends both native recoveries and returns to the original dying responder only once."); _ = Cold(g, registry);
    }
    private static bool ZoneChoice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "damage-zone-acquisition") == true;
    private static string Embedded(string suffix) { using var s = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-yan-bai-hu.{suffix}")!; using var r = new StreamReader(s); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Need(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Actual native zone/target command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static LegalAction A(GameEngine g, LegalActionKind k, int? target = null) => g.GetLegalActions().First(a => a.Kind == k && (target is null || a.TargetSeat == target));
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
    { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Place(GameEngine g, int target) { var card = V(g, 0).Hand.First(c => c.Kind is CardKind.GudingBlade or CardKind.SilverLion).Id; Use(g, "place", [card], [target]); Play(g); }
    private static void PrepareZones(GameEngine g) { Use(g, "draw24"); Play(g); Place(g, 1); Native(g, A(g, LegalActionKind.Indulgence, 1)); Play(g); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 240; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "The fixed native zone/target fixture ended before its expected boundary.");
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Kind == DecisionKind.PlayCard) End(g);
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException($"Unexpected zone/target prompt {p.Kind}: {p.Prompt}; skill={p.SkillPrompt?.SkillId}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed actual zone/target fixture exceeded its bounded command sequence.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(copy), "Real cold command replay preserves opaque selection, paid H/E/J invoice and one accepted Use cursor."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario, string child = "none")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, child));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = child == "death-live" ? Role.Rebel : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20,
            AiPolicyVersion = scenario == "wine" ? AiDecisionPolicy.CurrentVersion : 1 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario, string child) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("damage-zone-adjacent-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var own = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            foreach (var (id, p) in own.Programs) b.AddSkill(new(id, own.Presentations[id].Name, own.Presentations[id].Description) { Program = p, ProgramPresentation = own.Presentations[id], Tags = SkillTag.Locked });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"harm","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"harm8","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":8}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"draw24","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":4}]},
                {"id":"trim","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"count":1,"zones":["hand"],"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.damage-zone-acquisition.obtain"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            // Native response prompts are published only when a real response
            // is available. These existing single-card conversions give the
            // human an actual choice; choosing pass still runs native damage.
            var response = child == "death-live" ? (Input: "slash", Output: "peach")
                : scenario == "duel" ? (Input: "duel", Output: "slash")
                : scenario == "slash" ? (Input: "slash", Output: "dodge") : default;
            if (response.Input is not null)
                rules["skills"]![0]!["viewAs"] = JsonNode.Parse($$"""
                [{"id":"actual-response","inputKinds":["{{response.Input}}"],"inputSuits":[],"outputKind":"{{response.Output}}","forPlay":false,"forResponse":true}]
                """);
            if (child != "none") rules["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse(child == "loss"
                ? """{"op":"loseHp","target":"owner","amount":5}""" : """{"op":"damage","target":"owner","amount":5}"""));
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实规则准备", description = "真实装备、牌、伤害准备" },
                [Quiet] = new { name = "安静其他角色", description = "真实限制杀次数" },
                [Watch] = new { name = "真实获得子窗", description = "实际获得后的子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (child == "loss")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":5,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Zhi}}"],"sourceBind":"standard:none"}]}]}
                """)); labels[Loss] = new { name = "真实失源", description = "实际濒死恢复及技能失去", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scenario is "duel" or "borrowed" or "slash")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Swap}}","revision":1,"triggers":[{"id":"actual-swap","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["{{(scenario == "duel" ? "duel" : scenario == "slash" ? "slash" : "borrowedSword")}}"],"priority":1000,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}
                """)); labels[Swap] = new { name = "真实使用者替换", description = "原生用牌时替换实际使用者" };
            }
            // A nonzero existing SlashLimit addition lets the native AI use Wine
            // followed by Slash; the loader intentionally rejects a no-op Add0.
            if (scenario == "wine") rules["skills"]![1]!["triggers"]![0]!["effects"]![0]!["amount"] = 1;
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = p, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) });
            var extra = new List<string> { scenario == "zones" ? Zhi : Ji }; if (child != "none") extra.Add(Watch); if (child == "loss") extra.Add(Loss); if (scenario is "duel" or "borrowed" or "slash") extra.Add(Swap);
            b.AddGeneral(new(Owner, "实际区域与寄篱拥有者", "supporter", Driver, "qun", 4, extra));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:zone-adjacent-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "wei", 8, []));
            b.AddCard(new("fixture:zone-adjacent-blade", "古锭刀", "装备牌", "原生古锭刀", CardKind.GudingBlade));
            b.AddCard(new("fixture:zone-adjacent-lion", "白银狮子", "装备牌", "原生白银狮子", CardKind.SilverLion));
            b.AddCard(new("fixture:zone-adjacent-borrowed", "借刀杀人", "锦囊牌", "实际原生借刀杀人", CardKind.BorrowedSword));
            var cards = scenario switch { "zones" => new[] { "standard:dodge", "fixture:zone-adjacent-lion", "standard:indulgence", "standard:slash" },
                "draw" or "draw-black" => ["standard:draw_two"], "duel" => ["standard:duel"], "rescue" => ["standard:peach"],
                "global" => ["standard:arrow_barrage"], "borrowed" => ["fixture:zone-adjacent-blade", "fixture:zone-adjacent-borrowed"], "slash" => ["standard:slash"],
                "wine" => ["standard:alcohol", "standard:slash"], _ => ["standard:dodge"] };
            b.AddDeck(new("fixture:zone-adjacent-deck", "固定小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length],
                scenario == "draw-black" ? Suit.Spade : Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "真实区域与追加目标", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:zone-adjacent-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
