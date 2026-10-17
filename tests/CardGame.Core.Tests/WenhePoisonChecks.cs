using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WenhePoisonChecks
{
    private const string Driver = "fixture:wenhe-poison-driver", Quiet = "fixture:wenhe-poison-quiet";
    private const string Watch = "fixture:wenhe-poison-watch", Owner = "fixture:wenhe-poison-owner";
    private const string Mode = "fixture:wenhe-poison-mode";

    public static void PublicDeparturesAreSerialNativeLossesAndHiddenTransfersStayOpaque()
    {
        var (g, registry) = Start();
        var hand = V(g, 0).Hand;
        Need(hand.Count >= 4 && hand.All(c => c.Kind == CardKind.Peach && c.IdentityKind == CardKind.Poison) &&
            g.CreateSnapshot(1).Players[0].Hand.Count == 0 &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Peach && a.ConversionSource is null) &&
            !g.GetHumanLegalActions().Any(a => a.ConversionSource?.BindingId == "peach-to-alcohol") &&
            g.GetHumanLegalActions().Any(a => a.ConversionSource?.BindingId == "poison-slash"),
            "A genuine Poison round keeps the physical Peach entity, blocks native Peach and exact Peach-to-Alcohol input, and still accepts Poison as a basic-card conversion material without exposing another hand.");
        var hidden = hand[0].Id;
        var hp = V(g, 0).Hp;
        Use(g, "give-hidden", [hidden], [1]); Play(g);
        Need(V(g, 0).Hp == hp && V(g, 1).Hand.Any(c => c.Id == hidden && c.IdentityKind == CardKind.Poison) &&
            !E<WenhePoisonLossAppliedEvent>(g).Any(e => e.CardId == hidden) &&
            g.CreateSnapshot(2).Players[1].Hand.Count == 0,
            "A non-public hand gift transfers its real entity and current held identity without issuing Poison loss or disclosing it to unrelated seats.");
        var receiverHp = V(g, 1).Hp;
        Use(g, "give-public", [], [1]); Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch); Cold(g, registry); Play(g);
        var publicGift = E<WenhePoisonLossAppliedEvent>(g).Last();
        Need(publicGift.OwnerSeat == 0 && V(g, 0).Hp == hp - 1 && V(g, 1).Hp == receiverHp &&
            V(g, 1).Hand.Any(c => c.Id == publicGift.CardId && c.IdentityKind == CardKind.Poison) &&
            g.CardMovements.Any(m => m.CardId == publicGift.CardId && m.From == CardLocation.Hand(0) && m.Disclosure == CardMovementDisclosure.FaceUp),
            "An explicitly revealed native gift charges only the original holder once and grants the same poisoned physical entity to the recipient.");
        hp = V(g, 0).Hp;
        var selected = V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray();
        Use(g, "discard-two", selected, []);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.WenhePoisonLoss is not null);
        Need(window.WenhePoisonLoss is { Cursor: 0, Stage: WenhePoisonLossStage.HealthChildren } &&
            window.Batch.WenhePoisonDepartures!.Select(d => d.CardId).SequenceEqual(selected) &&
            E<WenhePoisonLossAppliedEvent>(g).Count(e => selected.Contains(e.CardId)) == 1 &&
            selected.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Disclosure == CardMovementDisclosure.FaceUp) == 1),
            "One real public multi-card discard owns a frozen two-departure receipt and pauses after only its first native HP loss.");
        Cold(g, registry); Play(g);
        Need(V(g, 0).Hp == hp - 2 && E<WenhePoisonLossAppliedEvent>(g).Count(e => selected.Contains(e.CardId) && e.HpBefore - e.HpAfter == 1) == 2 &&
            E<WenhePoisonDepartureSettledEvent>(g).Count(e => selected.Contains(e.CardId)) == 2 &&
            !g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.WenhePoisonLoss is not null),
            "The two physical Poisons each lose exactly one HP and settle once after optional health children; resuming never pays either departure twice.");
        Cold(g, registry);
        var convertedPeach = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Peach && a.ConversionSource?.SkillId == Driver);
        var beforePeach = V(g, 0).Hp;
        Accept(g, new PlayCardCommand(0, convertedPeach.CardId!.Value, convertedPeach.TargetSeats, g.Revision, P(g)!.PromptId,
            convertedPeach.PlayedCardKind, convertedPeach.TargetCardId) { ConversionSource = convertedPeach.ConversionSource });
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch); Cold(g, registry); Play(g);
        Need(V(g, 0).Hp == beforePeach && E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0) &&
            E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == convertedPeach.CardId) == 1,
            "A red-card conversion without AllowSameKind accepts effective Poison as Peach and performs real recovery; its printed Peach material separately pays one loss before recovery.");
        Cold(g, registry);
    }

    public static void ConvertedPhysicalPoisonPaysBeforeItsRealSlashAndCanEnterDying()
    {
        var (g, registry) = Start();
        var action = g.GetHumanLegalActions().First(a => a.CardId is not null && a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([1]));
        var material = action.CardId!.Value;
        var hp = V(g, 0).Hp;
        Accept(g, new PlayCardCommand(0, material, action.TargetSeats, g.Revision, P(g)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.PhysicalCardIds?.Contains(material) == true || f.CardId == material);
        Need(IsSlash(use.CardKind) && use.PaymentMovementReceipt is not null &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == material).Location == CardLocation.Processing &&
            V(g, 0).Hp == hp - 1 && !E<DamageAppliedEvent>(g).Any(),
            "A skill-converted physical Poison remains a genuine native Slash material and pays its HP loss while its true use still owns Processing, before Slash damage.");
        Cold(g, registry); Play(g);
        Need(E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == material) == 1 &&
            g.CardMovements.Count(m => m.CardId == material && m.To == CardLocation.DiscardPile) == 1,
            "The converted Slash and its poison payment each finish once.");

        while (V(g, 0).Hp > 1) { Use(g, "hurt", [], []); Play(g); }
        var dyingMaterial = V(g, 0).Hand.First().Id;
        Use(g, "discard-one", [dyingMaterial], []);
        Until(g, () => P(g)?.Kind == DecisionKind.RescueDying);
        var dying = g.ResolutionStack.OfType<DyingFrame>().Last();
        Need(dying.VictimSeat == 0 && dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.WenhePoison &&
            V(g, 0).Hp == 0 && !P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "peach-to-alcohol") &&
            P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("response") == "peach")
                .All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver &&
                    c.Parameters.GetValueOrDefault("conversion-binding-id") == "converted-peach"),
            "A lethal public Poison departure enters real native dying with no killer; held physical Poisons have no ordinary rescue-Peach choice and require the exact allowed conversion.");
        Cold(g, registry);
        var rescue = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "converted-peach");
        var rescueMaterial = rescue.Cards.Single();
        Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, rescue.Id, g.Revision)); Play(g);
        Need(V(g, 0).IsAlive && V(g, 0).Hp == 1 && E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == dyingMaterial) == 1 &&
            E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == rescueMaterial) == 1 &&
            E<DyingResolvedEvent>(g).Any(e => e.ResolutionId == dying.Id && e.Survived),
            "A correctly declared skill Peach still genuinely rescues its holder and both distinct public materials retain once-paid Poison receipts.");
        Cold(g, registry);
    }

    private static bool IsSlash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    public static void FireAttackWaitsForItsPublicPoisonCostBeforeDamage()
    {
        var (g, registry) = Start(fireAttack: true);
        var targetHp = V(g, 1).Hp;
        var cost = PayFireAttack(g);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var paid = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.WenhePoisonFireAttackPayment is not null);
        Need(paid.WenhePoisonFireAttackPayment!.CostCardId == cost && V(g, 1).Hp == targetHp &&
            !E<FireAttackResolvedEvent>(g).Any(e => e.ResolutionId == paid.Id),
            "The native Fire Attack's true public discard first suspends its own paid effect for Poison HP children; damage has not happened.");
        Cold(g, registry); Play(g);
        Need(V(g, 1).Hp == targetHp - 1 && E<FireAttackResolvedEvent>(g).Count(e => e.ResolutionId == paid.Id && e.CausedDamage) == 1 &&
            E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == cost) == 1,
            "After the precise cost child returns, one original Fire Attack deals its real fire damage once.");
        while (V(g, 0).Hp > 1) { Use(g, "hurt", [], []); Play(g); }
        targetHp = V(g, 1).Hp;
        var lethal = PayFireAttack(g);
        Until(g, () => P(g)?.Kind == DecisionKind.RescueDying);
        var cancelledUse = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.WenhePoisonFireAttackPayment is not null);
        Cold(g, registry);
        Until(g, () => !V(g, 0).IsAlive && E<FireAttackResolvedEvent>(g).Any(e => e.ResolutionId == cancelledUse.Id));
        Need(V(g, 1).Hp == targetHp && E<FireAttackResolvedEvent>(g).Count(e => e.ResolutionId == cancelledUse.Id && !e.CausedDamage) == 1 &&
            E<WenhePoisonLossAppliedEvent>(g).Count(e => e.CardId == lethal) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == cancelledUse.Id),
            "When the poison cost kills its user, the already paid Fire Attack finishes without harming its target or paying the discard again.");
        Cold(g, registry);
        var (colorGame, colorRegistry) = Start(fireAttack: true, colorFire: true);
        while (V(colorGame, 0).Hp > 1) { Use(colorGame, "hurt", [], []); Play(colorGame); }
        var colorTargetHp = V(colorGame, 1).Hp;
        var colorCost = PayFireAttack(colorGame);
        Until(colorGame, () => P(colorGame)?.Kind == DecisionKind.RescueDying);
        var colorUse = colorGame.ResolutionStack.OfType<CardUseFrame>().Single(f => f.ColorFireAttack?.PaidCardId == colorCost);
        Need(colorGame.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.ResumeColorFireAttackFrameId == colorUse.Id &&
                f.WenhePoisonLoss is { Stage: WenhePoisonLossStage.Dying }) && V(colorGame, 1).Hp == colorTargetHp,
            "The existing random-reveal color Fire Attack retains its own exact paid receipt while its disclosed Poison cost enters genuine dying.");
        Cold(colorGame, colorRegistry);
        Until(colorGame, () => !V(colorGame, 0).IsAlive && E<FireAttackResolvedEvent>(colorGame).Any(e => e.ResolutionId == colorUse.Id));
        Need(V(colorGame, 1).Hp == colorTargetHp && E<FireAttackResolvedEvent>(colorGame).Count(e => e.ResolutionId == colorUse.Id && !e.CausedDamage) == 1 &&
            E<WenhePoisonLossAppliedEvent>(colorGame).Count(e => e.CardId == colorCost) == 1,
            "The color-policy native tail also consumes one Poison cost and cancels damage when that cost kills its user.");
        Cold(colorGame, colorRegistry);
    }

    private static int PayFireAttack(GameEngine g)
    {
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.FireAttack && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.Kind == DecisionKind.FireAttackDiscard);
        var p = P(g)!;
        var cost = V(g, 0).Hand.First(c => c.Kind == CardKind.Peach && p.Choices.Any(choice => choice.Cards.Contains(c.Id))).Id;
        var choice = p.Choices.First(c => c.Cards.SequenceEqual([cost]));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
        return cost;
    }

    private static (GameEngine, ContentRegistry) Start(bool fireAttack = false, bool colorFire = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardWenheActivityPackage(), new Fixture(fireAttack, colorFire));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0,
            HumanRole = null, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 200 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Until(g, () => IsPlay(g) && g.CreateSnapshot(0).WenheRule?.Rule == WenheRuleKind.Poison, allowEndPlay: true);
        Need(V(g, 0).Hp >= 4 && V(g, 0).HandCount >= 5, "The fixed native-round fixture reaches Poison with enough real HP and cards to test its payments.");
        return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 8).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    private static void Use(GameEngine g, string id, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Until(GameEngine g, Func<bool> condition, bool allowEndPlay = false)
    {
        for (var step = 0; step < 8000; step++)
        {
            if (condition()) return;
            var p = P(g);
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && allowEndPlay)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p is { PlayerSeat: 0 } && p.Kind != DecisionKind.PlayCard && p.Kind != DecisionKind.SelectGeneral)
            {
                var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0 && c.Parameters.GetValueOrDefault("program-action") != "activate");
                if (choice is null) throw new InvalidOperationException("Poison fixture needs an explicit safe native response: " + p.Kind);
                Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException($"Poison fixture missed native boundary: status={g.CreateSnapshot(0).Status}, prompt={P(g)?.Kind}, frames={string.Join(',', g.ResolutionStack.Select(f => f.Kind))}");
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 8).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements),
            "All player views, native loss children, hidden entities and exact once-paid movement receipts cold-replay from actual commands.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Poison command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool fireAttack, bool colorFire) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wenhe-poison-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var colorPolicy = colorFire ? "\"cardPolicies\":[{\"id\":\"color-fire\",\"kind\":\"randomRevealColorFireAttack\",\"cardKinds\":[\"fireAttack\"]}]," : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"initialHandSize","operation":"add","value":10,"priority":0,"condition":{"kind":"always"}}],
               {{{colorPolicy}}}"viewAs":[{"id":"poison-slash","inputKinds":[],"inputCategories":["basic"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false},
                 {"id":"converted-peach","inputKinds":[],"inputSuits":["heart","diamond"],"sourceZones":["hand"],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true},
                 {"id":"peach-to-alcohol","inputKinds":["peach"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"alcohol","forPlay":true,"forResponse":true}],
               "activations":[
                {"id":"give-hidden","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
                {"id":"give-public","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"transferRandomOwnedCard","target":"selectedTarget","resultBind":"gift"}]},
                {"id":"discard-two","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":2}]},
                {"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}
               ]},
              {"id":"{{{Quiet}}}","revision":1,"modifiers":[{"id":"peer-hand","query":"initialHandSize","operation":"add","value":1,"priority":0,"condition":{"kind":"always"}}],"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"watch","window":"afterHpLost","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Native Wenhe Poison fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Native Wenhe Poison fixture.") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, Owner, "", Driver, "wei", 20, [Watch]));
            var peers = Enumerable.Range(1, 7).Select(i => $"fixture:wenhe-poison-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, id, "", Quiet, "qun", 20));
            const string deck = "fixture:wenhe-poison-deck";
            b.AddDeck(new(deck, "Native Poison fixture", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 120)
                .Select(i => new ContentDeckPhysicalCard(fireAttack && i % 3 == 0 ? "standard:fire_attack" : "standard:peach", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 8, [Owner, .. peers], ContentModeKind.Survival)
                { WenheActivity = new(PlayerTier: 1, EnableAspirations: false) });
        }
    }
}
