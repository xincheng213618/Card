using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryLingJuChecks
{
    private const string Jieyuan = "ol:jieyuan", Fenxin = "ol:fenxin", Driver = "fixture:lj-driver";
    private const string Hp = "fixture:lj-hp", Entry = "fixture:lj-entry", Pulse = "fixture:lj-pulse";
    private const string Mode = "identity:classic-ordinary-ling-ju-fixture";
    public static void PaidDamageAdjustmentUsesEffectiveOwnedColorAndActualAmount()
    {
        var (g, r) = Create(); Use(g, "range"); Play(g);
        var target = AlivePeer(g); var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == target && a.ConversionSource is null);
        var useCard = slash.CardId ?? throw new InvalidOperationException("The actual Slash requires its physical entity.");
        Accept(g, new PlayCardCommand(0, useCard, [target], g.Revision, P(g)!.PromptId, slash.PlayedCardKind)
        { ConversionSource = slash.ConversionSource, AdditionalConversionSources = slash.AdditionalConversionSources });
        Activate(g); ReachAction(g); Private(g); Reject(g); var root = Root(g); var receipt = root.SignedDamagePayment!;
        Require(receipt is { Delta: 1, WaiveHp: false, AnyColor: false, AllowEquipment: false } && receipt.OtherHp >= receipt.OwnerHp &&
            !P(g)!.ValidCardIds.Contains(useCard), "A true material Slash owns the other-target HP gate and black hand cost; its paid use card is unavailable.");
        var cost = P(g)!.Choices.First().Cards.Single(); g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([cost])); Play(g);
        var payment = Facts<ProgramSignedDamagePaidEvent>(g).Single(); var applied = Facts<ProgramSignedDamageAdjustedEvent>(g).Single();
        Require(payment.CostFrom == CardLocation.Hand(0) && payment.EffectiveSuit == Suit.Spade && applied.BeforeAmount == 1 && applied.AfterAmount == 2 &&
            g.CardMovements.Count(m => m.CardId == cost && m.Sequence == payment.MovementSequence && m.To == CardLocation.DiscardPile) == 1 &&
            Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == target && e.Amount == 2) &&
            Facts<CardUseDeclaredEvent>(g).Any(e => e.ResolutionId == applied.OriginalAttackFrameId && e.CardId == useCard && e.SourceSeat == 0),
            "The exact Slash damage increases once after one real public discard, without replacing its material use.");
        Use(g, "hongyan"); Play(g); var before = Facts<DamageAppliedEvent>(g).Count();
        Use(g, "incoming-one", [AlivePeer(g)]); Activate(g); ReachAction(g); Private(g); var reducedRoot = Root(g);
        var redCard = P(g)!.Choices.First().Cards.Single(); var printedSuit = g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == redCard).Suit;
        g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([redCard])); Play(g);
        var red = Facts<ProgramSignedDamagePaidEvent>(g).Last(); var zero = Facts<ProgramSignedDamageAdjustedEvent>(g).Last();
        Require(red.EffectiveSuit == Suit.Heart && printedSuit == Suit.Spade &&
            zero.ProgramFrameId == reducedRoot.Id && zero.BeforeAmount == 1 && zero.AfterAmount == 0 && !zero.Cancelled &&
            Facts<DamageAppliedEvent>(g).Count() == before,
            "Granted Hongyan makes an actual Spade entity a red cost; reducing to zero produces no fake damage or HP change.");
        g = Cold(g, r);
    }
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().First(f => f.SignedDamagePayment is not null);
    private static PendingDecision? P(GameEngine g) => g.State.PendingDecision;
    private static IEnumerable<T> Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static int AlivePeer(GameEngine g) => g.State.Players.First(p => p.Seat != 0 && p.IsAlive).Seat;
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g)
    { Reach(g, p => p.SkillPrompt?.SkillId == Jieyuan && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void ReachAction(GameEngine g) => Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "signed-damage-payment"));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 180; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Ling Ju actual boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Private(GameEngine g)
    { Require(P(g) is { IsPrivate: true, PlayerSeat: 0 }, "Only the payer sees cost identities."); for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[0].Hand.Count == 0, "Other prepared views contain neither private choice nor private hand identities."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "Invalid choice does not pay or advance any state."); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Four views, owning frames, private payloads, actual movements/facts and accepted commands restore identically."); return restored; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool nested = false, Role role = Role.Renegade, int ownerHp = 2, int peerHp = 6, bool native = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, nested, role, ownerHp, peerHp, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:lj-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(bool equipment, bool nested, Role role, int ownerHp, int peerHp, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-ling-ju", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rule = FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, "fixture:lj-quiet", Hp, Entry, Pulse }) names[id] = id is Driver or "fixture:lj-quiet"
                ? (object)new { name = id, description = "实际拥有帧小夹具" } : new { name = id, description = "实际拥有帧小夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            foreach (var (id, program) in SkillProgramCatalog.Load(rule, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names })).Programs)
                b.AddSkill(new(id, id, "实际拥有帧小夹具") { Program = program, Tags = id is Entry or "fixture:lj-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:lj-selection", "固定既有角色", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100000d) });
            var owner = new List<string> { Jieyuan }; if (role == Role.Loyalist) owner.Add("boundary:hongyan");
            if (nested) owner.AddRange([Hp, Entry, Pulse]);
            b.AddGeneral(new("fixture:lj-owner", "正式竭缘共享能力", "supporter", Driver, "qun", 4, owner) { InitialHp = ownerHp });
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:lj-peer-{i}", "固定其他角色", "supporter", "fixture:lj-selection", "wu", 8,
                    native ? ["fixture:lj-quiet", Jieyuan, Fenxin, "boundary:hongyan"] : ["fixture:lj-quiet"]) { InitialHp = peerHp });
            b.AddDeck(new("fixture:lj-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            var counts = role == Role.Renegade ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 };
            b.AddMode(new(Mode, "普通OL灵雎真实命令", 4, 4, counts, "fixture:lj-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:lj-owner", "fixture:lj-peer-1", "fixture:lj-peer-2", "fixture:lj-peer-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:lj-driver","revision":1,"activations":[
      {"id":"incoming-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
      {"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
      {"id":"outgoing-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
      {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
      {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
      {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
      {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
      {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
      {"id":"weaken","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},
      {"id":"fenxin","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:fenxin"]}]},
      {"id":"hongyan","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:hongyan"]}]},
      {"id":"range","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashDistanceLimit","ruleOperation":"unlimited"}]}]},
     {"id":"fixture:lj-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
     {"id":"fixture:lj-hp","revision":1,"triggers":[{"id":"paid-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":3}]}]},
     {"id":"fixture:lj-entry","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:lj-pulse","revision":1,"triggers":[{"id":"actual-wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"wine-seen","options":[{"id":"continue"}]},{"op":"useVirtualDyingAlcohol","target":"owner"},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}
    ]}
    """;
}
