using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WoLongFengChuChecks
{
    private const string Youlong = "ol:youlong", Luanfeng = "ol:luanfeng", Driver = "fixture:wlfc-driver";
    private const string Watch = "fixture:wlfc-watch", Gain = "fixture:wlfc-gain", Hp = "fixture:wlfc-hp", Conditional = "fixture:wlfc-conditional";
    private const string Quiet = "fixture:wlfc-quiet", Attack = "fixture:wlfc-attack", Owner = "fixture:wlfc-owner", Mode = "identity:classic-wolong-fixture";

    public static void YoulongCounterspellPreservesFrozenResponseAdmissionCold()
    {
        var (g, registry) = Start("counterspell-admission");
        Use(g, "duel", [1]);
        Reach(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var windowId = g.ResolutionStack.OfType<NullificationWindowFrame>().Last().Id;
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "tiered-round-zero-use");
        Reach(g, () => Slots(P(g))); g = Cold(g, registry);
        Slot(g, EquipmentSlot.Weapon); Play(g);
        var prepared = E<MouZhaJiangResponsePreparedEvent>(g).Where(e => e.OwnerFrameId == windowId).ToArray();
        var admitted = E<MouZhaJiangResponseAdmittedEvent>(g).Where(e => e.OwnerFrameId == windowId).ToArray();
        Need(prepared is [{ Index: 0, ActorSeat: 0, Kind: CardKind.Nullification, MaterialCount: 0,
                Admission.Source: null, Admission.Applies: false }] &&
            admitted is [{ Index: 0, ActorSeat: 0, Kind: CardKind.Nullification, ActionId: > 0 }] &&
            prepared[0].Admission == admitted[0].Admission &&
            E<NullificationResolvedEvent>(g).Single(e => e.WindowFrameId == windowId).EffectNullified &&
            E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 1 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1,
            "A genuine Youlong counterspell without any ZhaJiang owner retains exactly one pre-use admission, cancels its original trick and pays its slot once across cold replay.");
        _ = Cold(g, registry);
    }

    public static void YoulongPaidEquipmentMovementSurvivesColdSourceLossAndRoundFaces()
    {
        foreach (var loss in new[] { false, true })
        {
            var (g, registry) = Start(loss ? "loss" : "normal");
            var gear = EquipmentAction(g, CardKind.Crossbow);
            SubmitPlay(g, gear); Play(g); var hand = V(g, 0).HandCount;
            Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g)));
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && g.CreateSnapshot(1).PendingDecision is null && !E<EquipmentSlotNamedUsePaidEvent>(g).Any(),
                "The accepted actual zero use owns a private unpaid slot choice; no card name or cost is consumed before selection.");
            g = Cold(g, registry); Slot(g, EquipmentSlot.Weapon); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var payment = E<EquipmentSlotNamedUsePaidEvent>(g).Single().Payment;
            Need(payment is { Yin: false, EffectiveKind: CardKind.DrawTwo, Slot: EquipmentSlot.Weapon, PreviousCapacity: 1 } &&
                V(g, 0).Equipment.Count == 0 && !E<EquipmentSlotNamedUseIssuedEvent>(g).Any() &&
                g.CardMovements.Count(m => m.CardId == gear.CardId && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.slot-abolished") == 1 &&
                V(g, 0).Skills!.Any(s => s.Id == Youlong) != loss,
                "The actual equipment entity and Yang round allowance are paid once before their real child, including HP-driven source loss.");
            g = Cold(g, registry); AuditPaidDraft(g, registry); Continue(g); Play(g);
            Need(V(g, 0).HandCount == hand + 2 && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 1 &&
                E<EquipmentSlotNamedUseConsumedEvent>(g).Count() == 1 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1 &&
                E<TieredRoundConversionUseIssuedEvent>(g).Any(e => e.Receipt is { Source.SkillId: Youlong, EffectiveKind: CardKind.DrawTwo, MaterialCount: 0 }) &&
                E<CardUseFinishedEvent>(g).Any(e => e is { CardId: 0, CardKind: CardKind.DrawTwo } && E<EquipmentSlotNamedUseIssuedEvent>(g).Any(i => i.NativeOwnerId == e.ResolutionId)),
                "Cold returning from the paid cost issues one real native DrawTwo, even after the source disappeared, without a second equipment or quota payment. " +
                JsonSerializer.Serialize(new { loss, hand, current = V(g, 0).HandCount, paid = E<EquipmentSlotNamedUsePaidEvent>(g).Count(), consumed = E<EquipmentSlotNamedUseConsumedEvent>(g).Count(), issued = E<EquipmentSlotNamedUseIssuedEvent>(g).Count(), actions = E<CardActionAcceptedEvent>(g).Select(e => new { e.Action.ActorSeat, e.Action.Type, e.Action.EffectiveKind, physical = e.Action.PhysicalCards.Count, conversions = e.Action.ConversionChain.Select(c => c.SkillId) }) }));
            _ = Cold(g, registry); if (loss) continue;
            var zeroSlash = NamedActions(g).First(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
            var endPlay = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.EndPlay);
            var (aiChoice, aiThought) = new SimpleAiBrain(0, 31, policyVersion: 2).ChoosePlay(g.CreateSnapshot(0), [zeroSlash, endPlay], 1);
            Need(aiChoice == zeroSlash && aiThought.Candidates.Single(c => c.Action == zeroSlash).Score > 0 &&
                zeroSlash.CardId == 0 && zeroSlash.TieredRoundZeroUse is not null,
                "Tactical AI scores and selects the genuinely published zero-material Slash without looking up a nonexistent physical card or charging a hand reserve cost.");
            Named(g, CardKind.Slash, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
            Need(E<EquipmentSlotNamedUsePaidEvent>(g).Select(e => e.Payment.Yin).SequenceEqual([false, true]) && !NamedActions(g).Any(),
                "Yang and Yin each pay their own one-use allowance during the actual round.");
            var round = E<RoundStartedEvent>(g).Count(); Use(g, "extra"); Play(g); End(g); Play(g);
            Need(E<RoundStartedEvent>(g).Count() == round && !NamedActions(g).Any(), "An additional actual turn does not refresh either round allowance.");
            NextRound(g, round + 1);
            Need(!NamedActions(g).Any(a => a.PlayedCardKind == CardKind.DrawTwo) && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Duel),
                "The next real round refreshes face quota while preserving each already used card name.");
            Named(g, CardKind.Duel, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.DefensiveHorse); Play(g);
            Need(!NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Slash) && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.FireSlash) &&
                NamedActions(g).Any(a => a.PlayedCardKind == CardKind.ThunderSlash), "Ordinary, fire and thunder Slash retain distinct actually used kinds.");
            _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static IEnumerable<LegalAction> NamedActions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Youlong);
    private static LegalAction EquipmentAction(GameEngine g, CardKind kind) => g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == kind));
    private static bool Slots(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-slot-named-use") == true;
    private static void Named(GameEngine g, CardKind k, int? target = null) => SubmitPlay(g, NamedActions(g).First(a => a.PlayedCardKind == k && (target is null || a.TargetSeats.SequenceEqual([target.Value]))));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Slot(GameEngine g, EquipmentSlot s) => Answer(g, c => c.Parameters.GetValueOrDefault("equipment-slot") == s.ToString());
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void NextRound(GameEngine g, int round) { End(g); Reach(g, () => E<RoundStartedEvent>(g).Count() >= round && P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); }
    private static void Reach(GameEngine g, Func<bool> predicate)
    {
        for (var i = 0; i < 230; i++)
        {
            if (predicate()) return; var p = P(g);
            if (p?.SkillPrompt?.SkillId is Watch or Gain or Hp) Continue(g);
            else if (p?.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade }) Answer(g, c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard }) Answer(g, _ => true);
            else if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var text = JsonSerializer.Serialize(P(g)); throw new InvalidOperationException("WoLongFengChu boundary missing: " + text[..Math.Min(500, text.Length)] + "; frames=" + JsonSerializer.Serialize(g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step })) + "; facts=" + JsonSerializer.Serialize(g.Events.TakeLast(5).Select(e => e.Payload.ToString())));
    }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual WoLongFengChu command."); }
    private static void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        static string State(GameEngine e) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(e.CreateSnapshot(s))).ToArray(), Frames = e.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = e.Events.Select(x => JsonSerializer.Serialize(x.Payload, x.Payload.GetType())).ToArray(), e.CardMovements, Commands = CommandJson.Serialize(e.AcceptedCommands), Zones = e.CreateCardZoneDiagnostics() });
        Need(State(g) == State(cold), "Cold replay preserves all private views, exact paid/native receipts, pending recovery children, events, physical movements and accepted commands."); return cold;
    }
    private static void AuditPaidDraft(GameEngine game, ContentRegistry registry)
    {
        var original = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.EquipmentSlotNamedUse?.Payment is not null);
        var d = original.EquipmentSlotNamedUse!;
        var issued = E<EquipmentSlotNamedUseStartedEvent>(game).Single(e => e.FrameId == original.Id);
        Need(!JsonSerializer.Serialize(issued).Contains("OriginalDecisionSignature", StringComparison.Ordinal) &&
            !issued.ToString().Contains(d.OriginalDecisionSignature, StringComparison.Ordinal) &&
            issued.GetType().GetProperties().All(p => p.Name != "OriginalDecisionSignature"),
            "The private native prompt integrity digest stays out of public JSON, text and reflection, rather than exposing a hand-membership oracle.");
        foreach (var bad in new[] { d with { Source = d.Source with { OwnerSeat = 1 } }, d with { OriginalDecisionSignature = "changed-private-invoice" },
            d with { Payment = d.Payment! with { PhaseInstance = d.Payment.PhaseInstance + 1 } } })
        {
            var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var store = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var owner = store.OfType<ProgramSkillFrame>().Single(f => f.Id == original.Id); store.Replace(owner with { EquipmentSlotNamedUse = bad });
            var rejected = false;
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", flags)!.Invoke(invalid, null); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException e && e.Message.StartsWith("Youlong", StringComparison.Ordinal)) { rejected = true; }
            Need(rejected, "The restored owning payment frame rejects altered source, private prompt invoice and paid phase scalars against immutable committed receipts.");
        }
    }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wo-long-feng-chu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:wo-long-feng-chu");
            if (scenario == "counterspell-admission") CharacterFixtureContent.Register(b, "mou:huang-gai-mobile");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,{{(scenario == "range" ? "" : "\"modifiers\":[{\"id\":\"range\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0}],")}}
                {{(scenario is "quota" or "qinglong" ? "\"viewAs\":[{\"id\":\"real-slash\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}]," : "")}}
                "triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":12}]}],
                "activations":[
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":8}]},
                {"id":"die-five","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"kill-six","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]},
                {"id":"empty-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"whole","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]},
                {"id":"two-slots","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["armor","offensiveHorse"],"amount":0}]},
                {"id":"double-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon"],"amount":2}]},
                {"id":"only-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["armor","offensiveHorse","defensiveHorse","treasure"],"amount":0}]},
                {"id":"only-offhorse","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon","armor","defensiveHorse","treasure"],"amount":0}]},
                {"id":"equip-deck","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"weapon"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"cost-child","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["equipment.slot-abolished"],"optional":false,"effects":[{{(scenario == "loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.luanfeng.draw-to"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-youlong","skillId":"{{Youlong}}","minimumHp":5}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Attack}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"triggers":[{"id":"real-counter-slash","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}
            ]}
            """;
            var ids = new[] { Driver, Watch, Gain, Hp, Conditional, Quiet, Attack };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "卧龙凤雏实际命令与原生结算夹具", optionLabels = id is Watch or Gain or Hp ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际规则夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:wlfc-owner-pick", "固定主人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:wlfc-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new(Owner, "卧龙凤雏机制", "ol-wo-long-feng-chu", "fixture:wlfc-owner-pick", "shu", 4, [scenario == "loss" ? Conditional : Youlong, Luanfeng, Driver, Watch, Gain, Hp]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:wlfc-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "其他", "supporter", "fixture:wlfc-peer-pick", "wei", 6,
                scenario == "response" ? [Quiet, Gain, Hp, Attack] : [Quiet, Gain, Hp]));
            b.AddCard(new("fixture:wlfc-qinglong", "青龙偃月刀", "装备牌", "原生追杀武器", LegacyKind: CardKind.QinglongCrescentBlade));
            b.AddCard(new("fixture:wlfc-lion", "白银狮子", "装备牌", "原生移除回复", LegacyKind: CardKind.SilverLion));
            b.AddDeck(new("fixture:wlfc-deck", "固定无响应装备实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(scenario == "qinglong" ? i < 160 ? "standard:dodge" : "fixture:wlfc-qinglong" : scenario == "range" ? "standard:qinggang_sword" : scenario == "snatch" ? "standard:offensive_horse" : scenario == "silver" ? "fixture:wlfc-lion" : "standard:crossbow", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "卧龙凤雏真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:wlfc-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
