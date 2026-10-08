using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class TangJiPaidContinuationChecks
{
    private const string Mode = "team:tang-ji-paid-continuation";
    private const string Owner = "fixture:tang-ji-paid-owner";
    private const string Driver = "fixture:tang-ji-paid-driver";
    private const string LossChild = "fixture:tang-ji-paid-loss-child";
    private const string MoveChild = "fixture:tang-ji-paid-move-child";
    private const string EntryChild = "fixture:tang-ji-paid-entry-child";
    private const string GiftChild = "fixture:tang-ji-paid-gift-child";
    private const string Kangge = "ol:kangge";
    private const string Jielie = "ol:jielie";
    private const string DeathReason = "skill-program.ol:kangge.kangge-death-price";
    private const string GiftReason = "skill-program.ol:jielie.jielie-gift";

    public static void MarkedDeathDiscardsOriginalHandEquipmentAndReturnsOnce()
    {
        RejectInvalidKanggeContracts();
        var (g, registry) = Start(lethalGift: false);
        ChooseMarked(g, 1);
        Reach(g, IsPlay);
        var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Play(g, equip);
        Reach(g, IsPlay);
        var originalHand = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).Order().ToArray();
        var originalEquipment = g.CreateSnapshot(0).Players[0].Equipment.Select(c => c.Id).Order().ToArray();
        Require(originalHand.Length > 0 && originalEquipment.SequenceEqual([equip.CardId!.Value]),
            "The native owner really holds remaining Hand cards and the equipped material before the marked character dies.");
        var hp = g.CreateSnapshot(0).Players[0].Hp;
        Accept(g, new UseProgramSkillCommand(0, Driver, "kill", [], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => HasAction(p, "kangge-heal"));
        RejectWrongActor(g);
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("decline") == "true");
        Reach(g, p => IsContinue(p, LossChild));
        var price = Facts<ProgramKanggeDeathPriceEvent>(g).Single();
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == price.FrameId);
        var loss = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == LossChild);
        Require(price.VictimSeat == 1 && price.OwnerSeat == 0 &&
                price.DiscardedCardCount == originalHand.Length + originalEquipment.Length &&
                parent.SkillId == Kangge && parent.TriggerId == "kangge-death" && parent.InstructionIndex == 1 &&
                parent.WindowContext is { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: 1 } &&
                loss.WindowContext is { Window: SkillProgramTriggerWindow.AfterHpLost, Amount: 1 } &&
                !g.CreateSnapshot(0).Players[1].IsAlive && g.CreateSnapshot(0).Players[0].Hp == hp - 1 &&
                g.CreateSnapshot(0).Players[0].HandCount == 0 && g.CreateSnapshot(0).Players[0].Equipment.Count == 0 &&
                Facts<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 1) == 1 &&
                Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == parent.Id && e.SkillId == Kangge &&
                    e.TargetSeat == 0 && e.Amount == 1 && e.RemainingHp == hp - 1) == 1,
            "The actual marked death pays its original HE invoice and one HP loss before its native loss child returns: " + Diagnostic(g));
        AssertDeathPayments(g, originalHand, originalEquipment);
        RejectWrongActor(g);
        g = Cold(g, registry);
        Continue(g);
        Reach(g, p => IsContinue(p, MoveChild));
        var movementChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == MoveChild);
        var batch = movementChild.WindowContext!.MovementBatch!;
        Require(batch.ParentFrameId == parent.Id && batch.AwaitingProgramFrameId is null &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.Id == batch.Id && f.ResumeProgramFrameId == parent.Id) &&
                batch.Movements.All(m => m.To == CardLocation.DiscardPile && m.Reason.Value == DeathReason),
            "The original real discard child keeps the exact paid program owner and typed resume target.");
        g = Cold(g, registry);
        Continue(g);
        Reach(g, IsPlay);
        Require(Facts<ProgramKanggeDeathPriceEvent>(g).Count() == 1 &&
                Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == parent.Id) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == parent.Id) &&
                g.CreateSnapshot(0).Players[0].Hp == hp - 1 &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Kangge && e.BindingId == "kangge-death" && e.Completed) == 1,
            "Cold native loss and movement children return once without replaying the marked-death cost.");
        AssertDeathPayments(g, originalHand, originalEquipment);
        _ = Cold(g, registry);
    }

    public static void LethalPreventionRescuesThenGiftsWithoutRepaying()
    {
        var (g, registry) = Start(lethalGift: true);
        var players = g.CreateSnapshot(0).Players;
        var marked = players.Single(p => p.Seat != 0 && p.TeamId == players[0].TeamId).Seat;
        ChooseMarked(g, marked);
        Reach(g, IsPlay);
        Accept(g, new UseProgramSkillCommand(0, Driver, "hurt", [], [], g.Revision, P(g)!.PromptId));
        Reach(g, IsPlay);
        var source = g.GetHumanLegalActions().Where(a => a.ProgramSkillId == "classic:tiaoxin")
            .SelectMany(a => a.SelectableTargetSeats).First(s => players[s].TeamId != players[0].TeamId);
        var discardedSlash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
            a.ConversionSource is null && a.TargetSeats.SequenceEqual([source]));
        Play(g, discardedSlash);
        Reach(g, IsPlay);
        Require(g.CreateSnapshot(0).Players[0].Hp == 1 &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == discardedSlash.CardId).Location == CardLocation.DiscardPile,
            "One real ordinary Slash seeds the public Spade discard pool; the real preparatory HP payment leaves its owner at one HP.");
        Accept(g, new UseProgramSkillCommand(0, "classic:tiaoxin", "taunt", [], [source], g.Revision, P(g)!.PromptId));
        Reach(g, p => HasAction(p, "jielie-gift"));
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jielie);
        var parentId = parent.Id;
        var windowId = parent.WindowContext!.ParentFrameId;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } a && a.ActorSeat == source);
        Require(parent.WindowContext is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: 0, Amount: 1 } &&
                use.Action!.PhysicalCards is [var material] && material.From == CardLocation.Hand(source) &&
                use.CardAttack?.ProgramSkillCardUseFrameId is not null,
            "The native AI genuinely pays its Hand Slash through the real request and reaches the owner's exact before-damage choice.");
        RejectWrongActor(g);
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("suit") == nameof(Suit.Spade));
        Reach(g, p => IsContinue(p, EntryChild));
        var live = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.ParentFrameId == parentId);
        Require(live is { InstructionIndex: 1, ReexecuteParticipantInstruction: true,
                    JielieGift: { Prevented: true, HpLost: true, HpBeforePayment: 1, PaidHpLost: 1 } } &&
                dying.VictimSeat == 0 && dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill &&
                g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.Id == windowId).Prevented &&
                Facts<ProgramDamagePreventedEvent>(g).Count(e => e.FrameId == windowId && e.SkillId == Jielie && e.Amount == 1) == 1 &&
                Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == parentId && e.Amount == 1 && e.RemainingHp == 0) == 1 &&
                !Facts<ProgramJieliePreventedEvent>(g).Any(),
            "The lethal native HP payment pauses on its own dying child while the original prevented attack remains live and the gift unpaid: " + Diagnostic(g));
        RejectWrongActor(g);
        g = Cold(g, registry);
        Continue(g);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"));
        var rescue = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach");
        var rescueCard = rescue.Cards.Single();
        g = Cold(g, registry);
        Answer(g, c => c.Id == rescue.Id);
        Reach(g, p => IsContinue(p, GiftChild));
        var gift = Facts<ProgramJieliePreventedEvent>(g).Single();
        var gifted = g.CardMovements.Single(m => m.Reason.Value == GiftReason);
        var giftChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == GiftChild);
        Require(gift.FrameId == parentId && gift.OwnerSeat == 0 && gift.DamageSourceSeat == source &&
                gift.PreventedAmount == 1 && gift.LostHp == 1 && gift.GiftedCount == 1 && gift.MarkedSeat == marked &&
                gifted.From == CardLocation.DiscardPile && gifted.To == CardLocation.Hand(marked) &&
                giftChild.WindowContext?.MovementBatch is { } batch && batch.ParentFrameId == parentId &&
                batch.Movements.Single().Sequence == gifted.Sequence &&
                g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id) &&
                Facts<DyingResolvedEvent>(g).Count(e => e.ResolutionId == dying.Id && e.VictimSeat == 0 && e.Survived) == 1 &&
                Facts<DyingResponseEvent>(g).Count(e => e.ResolutionId == dying.Id && e.ResponderSeat == 0 && e.UsedPeach && e.PeachCardId == rescueCard) == 1,
            "A real converted Peach saves the payer, then the original receipt issues exactly one random public-pile gift before the attack returns.");
        g = Cold(g, registry);
        Reach(g, IsPlay);
        Require(g.CreateSnapshot(0).Players[0].Hp == 1 &&
                Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == parentId) == 1 &&
                Facts<ProgramJieliePreventedEvent>(g).Count() == 1 &&
                Facts<ProgramKanggeGainedDrawEvent>(g).Where(e => e.GainerSeat == marked).Sum(e => e.DrawnCount) == 1 &&
                !Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == source && e.TargetSeat == 0) &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.Slash) == 1 &&
                g.CardMovements.Count(m => m.CardId == use.Action!.PhysicalCards.Single().CardId && m.From == CardLocation.Hand(source) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == rescueCard && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == parentId || f.Id == dying.Id || f.Id == use.Id),
            "Cold paid-child recovery neither repeats the HP cost nor duplicates gift, attack completion or either physical payment; the marked gain still invokes Kangge once.");
        _ = Cold(g, registry);
    }

    private static void AssertDeathPayments(GameEngine g, IReadOnlyList<int> hand, IReadOnlyList<int> equipment)
    {
        foreach (var id in hand.Concat(equipment))
        {
            var from = hand.Contains(id) ? CardLocation.Hand(0) : CardLocation.Equipment(0);
            Require(g.CardMovements.Count(m => m.CardId == id && m.From == from && m.To == CardLocation.DiscardPile && m.Reason.Value == DeathReason) == 1 &&
                    g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile,
                "Every original cost entity retains its exact owned zone and one native discard, including cold returns.");
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool lethalGift)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(lethalGift));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:tang-ji-red", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Reach(g, p => HasAction(p, "kangge-choose"));
        return (g, registry);
    }

    private static void ChooseMarked(GameEngine g, int seat) => Answer(g, c => c.Targets.SequenceEqual([seat]));
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool HasAction(PendingDecision? p, string action) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static bool IsContinue(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Play(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats,
        g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var p = P(g)!;
        Require(p.PlayerSeat == 0, "A human command never answers on behalf of the native AI chooser.");
        var c = p.Choices.FirstOrDefault(choose);
        Require(c is not null, "The required published choice is absent: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Continue(g); return; }
        if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
        { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.RescueDying) { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die"); return; }
        throw new InvalidOperationException("Unexpected actual human boundary: " + Diagnostic(g));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 192; step++) { if (stop(P(g))) return; Step(g); }
        throw new InvalidOperationException("The bounded real paid continuation did not reach its boundary: " + Diagnostic(g));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The serialized native command was rejected.");
    }
    private static void RejectWrongActor(GameEngine g)
    {
        var p = P(g)!;
        var before = State(g);
        var result = g.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices.First().Id, g.Revision));
        Require(!result.Accepted && result.Error is not null && State(g) == before,
            "A wrong actor atomically preserves all player views, owning frames, exact paid movements and accepted command history.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(restored) == State(g), "Cold accepted-prefix replay preserves the exact native children, private entities and once-paid receipt.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(FrameState).ToArray(), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static string FrameState(ResolutionFrame frame) => frame is DyingFrame dying
        // Death observers can pause after the responder cursor reaches Count.
        // Preserve every stored field without invoking the derived next-seat getter.
        ? JsonSerializer.Serialize(new { dying.Id, dying.Kind, dying.Step, dying.ParentFrameId, dying.VictimSeat,
            dying.KillerSeat, dying.ResponderSeats, dying.ResponderIndex, dying.Continuation, dying.AttemptedSelfDyingBindings })
        : JsonSerializer.Serialize(frame, frame.GetType());
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Prompt = P(g), g.State.CurrentSeat, g.State.Phase,
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }).ToArray(),
        LastFacts = g.Events.TakeLast(10).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name }).ToArray()
    });
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void RejectInvalidKanggeContracts()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Embedded(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-tang-ji." + suffix)
                ?? throw new InvalidOperationException("The production Tang Ji rules are missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var rules = Embedded("rules.json");
        var presentation = Embedded("presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var mutate in new Action<JsonObject>[]
        {
            trigger => trigger["movementOccurrence"] = "perBatch",
            trigger => trigger["movementOccurrence"] = "perCard",
            trigger => trigger["destinationZones"] = new JsonArray("equipment"),
            trigger => trigger["ignoreOwnSkillMovements"] = true,
            trigger => trigger["optional"] = true,
            trigger => trigger["subject"] = "any",
            trigger => trigger["effects"]!.AsArray().Add(new JsonObject { ["op"] = "draw", ["target"] = "owner", ["amount"] = 1 })
        })
        {
            var root = JsonNode.Parse(rules)!.AsObject();
            var skill = root["skills"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == Kangge)!;
            mutate(skill["triggers"]!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "kangge-gain-draw")!.AsObject());
            ExpectInvalid(root);
        }
        var invalidHeal = JsonNode.Parse(rules)!.AsObject();
        var heal = invalidHeal["skills"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == Kangge)!["triggers"]!
            .AsArray().Single(t => t!["id"]!.GetValue<string>() == "kangge-heal")!;
        heal["effects"]![0]!["amount"] = 2;
        ExpectInvalid(invalidHeal, "exactly 1");
        void ExpectInvalid(JsonObject root, string? error = null)
        {
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (InvalidOperationException exception) when (error is null || exception.Message.Contains(error, StringComparison.OrdinalIgnoreCase))
            { rejected = true; }
            Require(rejected, "The strict Kangge collector/recovery contract rejects its unsupported caller without creating a runtime program.");
        }
    }

    private sealed class Fixture(bool lethalGift) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("tang-ji-paid-continuation-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                 {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"range","query":"attackRange","operation":"add","value":2,"priority":0}],"viewAs":[
                  {"id":"armor-as-slash","inputKinds":["silverLion"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},
                  {"id":"real-rescue","inputKinds":["slash"],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true}],"activations":[
                  {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
                  {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]}]},
                 {"id":"{{{LossChild}}}","revision":1,"triggers":[{"id":"paid-loss","window":"afterHpLost","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{MoveChild}}}","revision":1,"triggers":[{"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{{DeathReason}}}"],"movementDiscardOnly":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"move","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{EntryChild}}}","revision":1,"triggers":[{"id":"paid-own-entry","window":"dyingEntering","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{GiftChild}}}","revision":1,"triggers":[{"id":"real-gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{GiftReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, LossChild, MoveChild, EntryChild, GiftChild }.ToDictionary(id => id, id =>
                    {
                        var fields = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实费用和所属子窗观察" };
                        if (id != Driver) fields["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                        return fields;
                    })
                }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "原生实体与费用返回")
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string neutral = "fixture:tang-ji-paid-neutral";
            b.AddSkill(new(neutral, "真实其他角色", "沿用原生AI与响应"));
            b.AddGeneral(new(Owner, "真实抗歌和节烈当事人", "supporter", Kangge, "qun", 3,
                lethalGift ? [Jielie, "classic:tiaoxin", Driver, EntryChild, LossChild] : [Driver, LossChild, MoveChild])
            { InitialHp = lethalGift ? 1 : 3 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:tang-ji-paid-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定真实其他角色", "supporter", neutral, "wei", lethalGift ? 4 : 1,
                lethalGift ? [GiftChild] : []));
            const string deck = "fixture:tang-ji-paid-deck";
            b.AddDeck(new(deck, "固定同类原实体", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard(lethalGift ? "standard:slash" : "classic:silver-lion", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "公开2v2费用返回", 4, 4, new Dictionary<string, int>(), deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:tang-ji-blue"] = 2, ["team:tang-ji-red"] = 2 }));
        }
    }
}
