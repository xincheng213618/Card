using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DrawAdviceAndDesignatedExtraTargetChecks
{
    private const string Mode = "identity:classic-draw-advice-designation";
    private const string Owner = "fixture:draw-advice-owner";
    private const string Driver = "fixture:draw-advice-driver";
    private const string Prep = "fixture:draw-advice-preparation";
    private const string DrawChild = "fixture:draw-advice-gain";
    private const string CostChild = "fixture:draw-advice-cost";
    private const string HpChild = "fixture:draw-advice-hp";
    private const string CompleteChild = "fixture:draw-advice-completion";
    private const string Bizheng = "ol:bizheng";
    private const string Yidian = "ol:yidian";

    public static void DrawAdviceFreezesBothQualificationsAfterRealDrawAndPaysHeOnce()
    {
        for (var variant = 0; variant < 4; variant++) ExerciseAdvice(variant);
    }

    public static void DesignationUsesCanonicalSlashNameAndIgnoresOnlyDistance()
    {
        foreach (var conversion in new[] { "as-fire", "as-thunder" })
        {
            var (g, registry) = Start(CardKind.Slash, forbidden: true);
            Use(g, "discard-one", []); Reach(g, IsPlay);
            Require(g.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
                "One printed ordinary Slash really enters public discard before the elemental converted use.");
            var action = FindAction(g, a => a.ConversionSource?.BindingId == conversion && a.TargetSeats is [1 or 3],
                "published " + conversion + " converted Slash toward a real legal adjacent seat after the real discard");
            Play(g, action); Reach(g, IsPlay);
            Require(Facts<DesignatedExtraTargetOfferedEvent>(g).Length == 0 && Facts<DesignatedExtraTargetResolvedEvent>(g).Length == 0,
                "A discarded ordinary Slash suppresses the same-name gate for both Fire Slash and Thunder Slash conversions.");
            AssertOneUse(g, action.CardId!.Value, action.PlayedCardKind ?? (conversion == "as-fire" ? CardKind.FireSlash : CardKind.ThunderSlash));
            _ = Cold(g, registry);
        }
        foreach (var kind in new[] { CardKind.Slash, CardKind.Snatch })
        {
            var (g, registry) = Start(kind, forbidden: true);
            var forbiddenSeat = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:draw-advice-peer-3").Seat;
            Require(!g.GetHumanLegalActions().Any(a => a.Kind == ActionKind(kind) && a.TargetSeats.SequenceEqual([2])),
                "Seat two is outside the printed card's normal distance before the designation instruction.");
            var action = FindAction(g, a => a.Kind == ActionKind(kind) && a.ConversionSource is null && a.TargetSeats is [1 or 3],
                "published ordinary card toward a real legal adjacent original target");
            var originalSeat = action.TargetSeats.Single();
            var use = BeginAddition(ref g, registry, action);
            Require(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.SequenceEqual([2])) &&
                    !P(g)!.Choices.Any(c => c.Targets.Contains(0) || c.Targets.Contains(originalSeat) || c.Targets.Contains(forbiddenSeat)),
                "The published extension adds distant seat two while excluding the actor, actual original duplicate and actual publicly prohibited character.");
            Add(ref g, registry, use, [2]); Reach(g, IsPlay); AssertOneUse(g, action.CardId!.Value, kind);
            Require(Facts<DesignatedExtraTargetResolvedEvent>(g).Single().ResultTargetSeats.SequenceEqual([originalSeat, 2]),
                "The same native use retains its original target and appends exactly one distance-free legal target.");
            if (kind == CardKind.Snatch)
                Require(Facts<TargetCardSelectionRequestedEvent>(g).Where(e => e.ResolutionId == use.Id).Select(e => e.TargetSeat).SequenceEqual([originalSeat, 2]),
                    "Both Snatch targets receive their own actual opaque-card selection, rather than repeating the first victim's payment.");
            _ = Cold(g, registry);
        }
    }

    public static void DesignationAppendsOneIronChainTargetAndKeepsOnePhysicalUse()
    {
        var (g, registry) = Start(CardKind.IronChain);
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.SequenceEqual([1, 3]));
        var use = BeginAddition(ref g, registry, action);
        Require(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.Count == 1 && !c.Targets.Contains(1) && !c.Targets.Contains(3)),
            "A two-target Iron Chain publishes single distinct additional targets and cannot select either original target again.");
        Add(ref g, registry, use, [2]); Reach(g, p => IsContinue(p, CompleteChild)); g = Cold(g, registry);
        Require(Facts<IronChainStateChangedEvent>(g).Where(e => e.ResolutionId == use.Id).Select(e => e.TargetSeat).SequenceEqual([1, 3, 2]) &&
                new[] { 1, 3, 2 }.All(s => g.CreateSnapshot(0).Players[s].IsChained),
            "Each original and added target receives one actual chain-state effect in native target order.");
        Reach(g, IsPlay); AssertOneUse(g, action.CardId!.Value, CardKind.IronChain); _ = Cold(g, registry);
    }

    public static void DesignationPeachAndAlcoholExecuteRealExtraEffectsAndColdReturns()
    {
        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol })
        {
            var (g, registry) = Start(kind);
            if (kind == CardKind.Peach)
            {
                Use(g, "hurt", [0]); Reach(g, IsPlay); Use(g, "hurt", [2]); Reach(g, IsPlay);
            }
            var before = g.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
            var action = g.GetHumanLegalActions().First(a => a.Kind == ActionKind(kind));
            var use = BeginAddition(ref g, registry, action);
            Require(use.TargetSeats.SequenceEqual([0]), "The actual self-use target is explicit before an additional Basic target is offered.");
            Add(ref g, registry, use, [2]);
            if (kind == CardKind.Peach)
            {
                Reach(g, p => IsContinue(p, HpChild) && p!.PlayerSeat == 0); g = Cold(g, registry);
                Require(g.CreateSnapshot(0).Players[0].Hp == before[0] + 1 && g.CreateSnapshot(0).Players[2].Hp == before[2],
                    "The first native Peach HP child pauses before the additional recipient's actual recovery.");
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Reach(g, p => IsContinue(p, HpChild) && p!.PlayerSeat == 2); g = Cold(g, registry);
                Require(g.CreateSnapshot(0).Players[2].Hp == before[2] + 1 &&
                        Facts<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
                        Facts<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1,
                    "Cold return applies exactly one real recovery to each designated target.");
            }
            Reach(g, p => IsContinue(p, CompleteChild)); g = Cold(g, registry);
            if (kind == CardKind.Alcohol)
                Require(new[] { 0, 2 }.All(s => g.CreateSnapshot(0).Players[s].HasAlcoholEffect) &&
                        Facts<AlcoholAppliedEvent>(g).Count(e => e.ResolutionId == use.Id) == 2,
                    "One material Alcohol actually arms both designated characters, once each, before native whole-use completion.");
            Reach(g, IsPlay); AssertOneUse(g, action.CardId!.Value, kind);
            Require(kind == CardKind.Peach
                    ? Facts<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
                      Facts<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1
                    : Facts<AlcoholAppliedEvent>(g).Count(e => e.ResolutionId == use.Id) == 2,
                "Returning the whole-use completion child cannot repeat either already completed Basic target effect.");
            _ = Cold(g, registry);
        }
    }

    public static void DesignationBorrowedSwordFreezesOneNewPairAndResolvesBothOwners()
    {
        var (g, registry) = Start(CardKind.BorrowedSword);
        foreach (var seat in new[] { 1, 3 }) { Use(g, "equip", [seat]); Reach(g, IsPlay); }
        Use(g, "draw-three", []); Reach(g, IsPlay);
        var weapons = new[] { 1, 3 }.ToDictionary(s => s, s => g.CreateSnapshot(0).Players[s].Equipment.Single().Id);
        var action = FindAction(g, a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 2]),
            "published physical Borrowed Sword pair [holder one, victim two] after both native equipment uses and Draw3");
        var use = BeginAddition(ref g, registry, action);
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3, 2])) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3, 0])) &&
                P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.Count == 2 && c.Targets[0] != 1),
            "Each public Borrowed Sword extension is one complete legal pair; its new holder is unique while its victim may repeat the original victim or actor.");
        Add(ref g, registry, use, [3, 2]);
        Reach(g, p => IsContinue(p, CompleteChild)); g = Cold(g, registry);
        var bothWeaponsPaid = Facts<DesignatedExtraTargetResolvedEvent>(g).Single().ResultTargetSeats.SequenceEqual([1, 2, 3, 2]) &&
                weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Equipment(pair.Key) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1) &&
                weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Processing &&
                    m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1);
        if (!bothWeaponsPaid)
            Require(false, "Both genuinely Slash-less weapon holders execute their own native give-weapon response and transfer one exact equipment entity to the original trick user: " +
                BorrowedSwordDiagnostic(g, use.Id, weapons));
        Reach(g, IsPlay); AssertOneUse(g, action.CardId!.Value, CardKind.BorrowedSword); _ = Cold(g, registry);
    }

    public static void DesignationDoesNotOfferForResponseRecastOrUnsupportedCards()
    {
        SharedNodesRejectUnsupportedEntryContracts();
        foreach (var kind in new[] { CardKind.Indulgence, CardKind.Crossbow, CardKind.IronChain })
        {
            var (g, registry) = Start(kind);
            var action = g.GetHumanLegalActions().First(a => a.Kind == (kind == CardKind.IronChain ? LegalActionKind.Recast : ActionKind(kind)));
            Play(g, action); Reach(g, IsPlay);
            Require(Facts<DesignatedExtraTargetOfferedEvent>(g).Length == 0 && Facts<DesignatedExtraTargetResolvedEvent>(g).Length == 0,
                "Delayed placement, equipment use and true targetless recast publish no designated-extra-target offer.");
            if (kind == CardKind.IronChain) Require(Facts<CardRecastEvent>(g).Count(e => e.ActorSeat == 0 && e.CardId == action.CardId) == 1,
                "The excluded Iron Chain branch is an actual paid recast rather than an unexecuted candidate.");
            _ = Cold(g, registry);
        }
        var (response, rr) = Start(CardKind.Duel);
        Accept(response, new EndPlayPhaseCommand(0, response.Revision, P(response)!.PromptId));
        Reach(response, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        var reply = P(response)!.Choices.First(c => c.Cards.Count == 1);
        var entity = reply.Cards.Single(); response = Cold(response, rr);
        Answer(response, c => c.Id == reply.Id);
        Reach(response, p => IsContinue(p, CompleteChild)); response = Cold(response, rr);
        Require(Facts<CardRespondedEvent>(response).Any(e => e.CardId == entity && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Slash) &&
                Facts<DesignatedExtraTargetOfferedEvent>(response).Length == 0 && Facts<DesignatedExtraTargetResolvedEvent>(response).Length == 0,
            "A real converted Slash Response consumes the human's published physical entity without being treated as a designated-target Use.");
    }

    private static void SharedNodesRejectUnsupportedEntryContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Embedded(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Embedded("ordinary-sun-shao.rules.json");
        var presentation = Embedded("ordinary-sun-shao.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        void RejectNode(string label, string skill, Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(rules)!.AsObject();
            var trigger = root["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == skill)!["triggers"]![0]!.AsObject();
            edit(trigger);
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Shared draw-advice/designation loader must reject unsupported entry: " + label);
        }
        RejectNode("advice outside true DrawPhaseEnded", Bizheng, t => t["window"] = "turnEnding");
        RejectNode("advice other than owner subject", Bizheng, t => t["subject"] = "any");
        RejectNode("advice extra instruction", Bizheng, t => t["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}""")));
        RejectNode("unnamed OtherLiving cannot select two", Bizheng, t => t["effects"]![0]!["maximumTargets"] = 2);
        RejectNode("unnamed OtherLiving cannot use a dynamic count", Bizheng, t => t["effects"]![0]!["numberExpression"] = "currentHandCount");
        RejectNode("unnamed OtherLiving requires supportDraw", Bizheng, t => t["effects"]![0]!["targetAiOrder"] = "stable");
        RejectNode("designation cannot include Response", Yidian, t => t["includeResponseUses"] = true);
        RejectNode("designation cannot be an observer", Yidian, t => t["ownerRelation"] = "observer");
        RejectNode("designation cannot share its instruction", Yidian, t => t["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}""")));
        RejectNode("designation requires one actual action", Yidian, t => t["singleActionInstance"] = false);
    }

    private static CardUseFrame BeginAddition(ref GameEngine g, ContentRegistry registry, LegalAction action)
    {
        Play(g, action); Reach(g, p => p?.SkillPrompt?.SkillId == Yidian);
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == Yidian && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "designated-extra-target"));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DesignatedExtraTargetDraft is not null);
        var draft = frame.DesignatedExtraTargetDraft!;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == draft.CardUseFrameId);
        Require(frame.OwnerSeat == 0 && frame.SkillId == Yidian && draft.ActionId == use.Action!.ActionId &&
                draft.InstructionIndex == frame.InstructionIndex && draft.GameplayHash == frame.GameplayHash &&
                draft.Source == new CardConversionSource(Yidian, frame.TriggerId!, 0, frame.SkillInstanceId) &&
                draft.OriginalTargetSeats.SequenceEqual(use.TargetSeats) && use.CardId == action.CardId &&
                Facts<DesignatedExtraTargetOfferedEvent>(g).Count(e => e.ProgramFrameId == frame.Id && e.CardUseFrameId == use.Id &&
                    e.ActionId == use.Action.ActionId && e.OriginalTargetSeats.SequenceEqual(use.TargetSeats)) == 1,
            "The public designation draft freezes one exact native owning use/action, original targets, paused instruction, source instance and gameplay fingerprint.");
        Frozen(draft.OriginalTargetSeats); Frozen(draft.Choices); Frozen(P(g)!.Choices);
        foreach (var choice in P(g)!.Choices.Where(c => c.Targets.Count > 0)) { Frozen(choice.Targets); Require(choice.Cards.Count == 0, "Public extension choices never expose a foreign Hand entity."); }
        Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("designation:not-published"), g.Revision));
        g = Cold(g, registry); return use;
    }

    private static void Add(ref GameEngine g, ContentRegistry registry, CardUseFrame original, IReadOnlyList<int> extra)
    {
        var old = P(g)!; var choice = old.Choices.Single(c => c.Targets.SequenceEqual(extra)); var revision = g.Revision;
        Answer(g, c => c.Id == choice.Id);
        var resolved = Facts<DesignatedExtraTargetResolvedEvent>(g).Single(e => e.CardUseFrameId == original.Id);
        Require(resolved.ActionId == original.Action!.ActionId && resolved.OriginalTargetSeats.SequenceEqual(original.TargetSeats) &&
                resolved.AddedTargetSeats.SequenceEqual(extra) && resolved.ResultTargetSeats.SequenceEqual(original.TargetSeats.Concat(extra)),
            "One accepted published choice appends the frozen additional target or compound pair to the same action exactly once.");
        Frozen(resolved.OriginalTargetSeats); Frozen(resolved.AddedTargetSeats); Frozen(resolved.ResultTargetSeats);
        Reject(g, new AnswerPromptCommand(0, old.PromptId, choice.Id, revision)); g = Cold(g, registry);
    }

    private static void AssertOneUse(GameEngine g, int card, CardKind kind)
    {
        var declared = Facts<CardUseDeclaredEvent>(g).Single(e => e.CardId == card);
        var finishReason = kind == CardKind.IronChain ? CardMoveReasons.IronChainFinished : CardMoveReasons.UseFinished;
        Require(Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == declared.ResolutionId && e.CardId == card && e.CardKind == kind) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finishReason) == 1 &&
                !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == declared.ResolutionId),
            "The original exact material is paid once, finishes once with its effective kind, and leaves no suspended native use.");
    }

    private static (GameEngine, ContentRegistry) Start(CardKind kind, int advice = -1, bool forbidden = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind, advice, forbidden));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = kind == CardKind.BorrowedSword ? 31 : 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (advice >= 0) PrepareAdvice(g, advice);
        Reach(g, advice >= 0 ? p => p?.SkillPrompt?.SkillId == Bizheng : IsPlay); return (g, registry);
    }

    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
    private static bool IsContinue(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static LegalActionKind ActionKind(CardKind kind) => kind == CardKind.Crossbow ? LegalActionKind.Equip : Enum.Parse<LegalActionKind>(kind.ToString());
    private static LegalAction FindAction(GameEngine g, Func<LegalAction, bool> predicate, string label)
    {
        var action = g.GetHumanLegalActions().FirstOrDefault(predicate);
        Require(action is not null, "Missing required " + label + ": " + LegalDiagnostic(g)); return action!;
    }
    private static string LegalDiagnostic(GameEngine g)
    {
        var snapshot = g.CreateSnapshot(0);
        return JsonSerializer.Serialize(new
        {
            Hand = snapshot.Players[0].Hand.Select(c => new { c.Id, c.Kind, c.Suit, c.Rank }).ToArray(),
            Players = snapshot.Players.Select(p => new { p.Seat, p.GeneralId, p.IsAlive, p.HandCount, p.Equipment }).ToArray(),
            LegalActions = g.GetHumanLegalActions().Where(a => a.CardId is not null).Select(a => new
            { a.Kind, a.CardId, a.TargetSeats, a.PlayedCardKind, a.TargetCardId, a.ConversionSource, a.AdditionalConversionSources }).ToArray(),
            LastMovements = g.CardMovements.TakeLast(10).ToArray()
        });
    }
    private static string BorrowedSwordDiagnostic(GameEngine g, long useId, IReadOnlyDictionary<int, int> weapons) => JsonSerializer.Serialize(new
    {
        AddedTargets = Facts<DesignatedExtraTargetResolvedEvent>(g).Where(e => e.CardUseFrameId == useId).ToArray(),
        Weapons = weapons.Select(pair => new
        {
            OwnerSeat = pair.Key, CardId = pair.Value,
            CurrentLocation = g.CreateCardZoneDiagnostics().Single(c => c.CardId == pair.Value),
            Movements = g.CardMovements.Where(m => m.CardId == pair.Value).ToArray()
        }).ToArray(),
        RelatedFacts = g.Events.Where(e => e.Payload switch
        {
            NullificationRequestedEvent f => f.ResolutionId == useId,
            NullificationRespondedEvent f => f.ResolutionId == useId,
            NullificationResolvedEvent f => f.ResolutionId == useId,
            BorrowedSwordResolvedEvent f => f.ResolutionId == useId,
            ResponseRequestedEvent f => f.IncomingCard == CardKind.BorrowedSword && f.SourceSeat == 0,
            CardRespondedEvent f => f.ResponderSeat is 1 or 3,
            _ => false
        }).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray()
    });
    private static void Play(GameEngine g, LegalAction action)
    {
        if (action.Kind == LegalActionKind.Recast)
            Accept(g, new RecastCardCommand(0, action.CardId!.Value, g.Revision, P(g)!.PromptId) { ConversionSource = action.ConversionSource });
        else Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        Require(P(g)?.PlayerSeat == 0, "A human Answer never impersonates a native AI chooser.");
        var choice = P(g)!.Choices.FirstOrDefault(predicate); Require(choice is not null, "Required published choice is absent: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, choice!.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 320; step++) { if (stop(P(g))) return; Step(g); }
        throw new InvalidOperationException("Bounded native fixture missed its boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); return; }
        if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash) { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.SelectTargetCard) { Answer(g, _ => true); return; }
        if (p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
        { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"); return; }
        throw new InvalidOperationException("Unexpected human boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Prompt = P(g), g.State.CurrentSeat, g.State.Phase,
        LastFacts = g.Events.TakeLast(12).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray() });
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static void Reject(GameEngine g, GameCommand command)
    { var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(!result.Accepted && result.Error is not null && before == State(g), "Wrong actor, unpublished or stale choice must reject atomically."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Require(State(g) == State(restored), "Cold accepted-command replay preserves all player views, exact owning children, frozen targets and paid movements."); return restored; }
    private static void Frozen<T>(IReadOnlyList<T> list)
    { Require(list.Count > 0 && list is IList<T> { IsReadOnly: true }, "A genuine published nonempty collection is frozen."); var rejected = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Published collections reject observer mutation."); }
    private static void Private(GameEngine g, int actor)
    { Require(P(g) is { IsPrivate: true, PlayerSeat: var actual } && actual == actor && Enumerable.Range(0, 4).Where(s => s != actor).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[actor].Hand.Count == 0), "Only the actual payer sees its private choices and foreign Hand faces remain redacted."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void PrepareAdvice(GameEngine g, int variant)
    {
        // These are genuine Preparation instructions. No hand, HP or equipment state is injected.
        for (var step = 0; step < 160; step++)
        {
            if (Facts<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Prep && e.BindingId == "prepare" && e.Completed)) return;
            var p = P(g);
            if (p is not { PlayerSeat: 0 }) { Step(g); continue; }
            if (p?.SkillPrompt?.SkillId != Prep) { Step(g); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"))
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p.Choices.Any(c => c.Targets.Count > 0))
            {
                var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
                var effect = ProgramInstructionResolver.Default.Resolve(frame, g.ContentRegistry.GetSkill(Prep).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
                Answer(g, c => c.Targets.SequenceEqual([effect.TargetKind == SkillProgramTargetKind.AnyLiving ? 0 : 1]));
            }
            else if (p.Choices.Any(c => c.Cards.Count == 1)) Answer(g, c => c.Cards.Count == 1);
            else Answer(g, c => c.Parameters.GetValueOrDefault("program-action") is "finish" or "finish-owned-cards");
        }
        throw new InvalidOperationException("Native preparation did not finish within its fixed bound: " + Diagnostic(g));
    }

    private static void ExerciseAdvice(int variant)
    {
        var (g, registry) = Start(CardKind.Dodge, advice: variant);
        var initial = g.CreateSnapshot(0).Players;
        var ownerHand = initial[0].HandCount; var recipientHand = initial[1].HandCount;
        var armor = initial[0].Equipment.SingleOrDefault()?.Id;
        Require(ownerHand == ((variant & 1) != 0 ? 8 : 0) && recipientHand == ((variant & 2) != 0 ? 8 : 0),
            "Real Preparation payments, rather than injected hand or HP state, establish the four fixed qualification branches.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == Bizheng && p.Choices.Any(c => c.Targets.Count > 0));
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => IsContinue(p, DrawChild));
        var parent = Advice(g); var receipt = parent.DrawAdviceReceipt!; var parentId = parent.Id;
        var firstChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        var firstPrompt = P(g)!.PromptId;
        Require(parent.OwnerSeat == 0 && parent.SkillId == Bizheng && parent.InstructionIndex == 2 &&
                parent.WindowContext is { Window: SkillProgramTriggerWindow.DrawPhaseEnded } context &&
                context.ParentFrameId == receipt.ParentLifecycleFrameId &&
                g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Id == receipt.ParentLifecycleFrameId) &&
                receipt.Source == new CardConversionSource(Bizheng, parent.TriggerId!, 0, parent.SkillInstanceId) &&
                receipt.GameplayHash == parent.GameplayHash && receipt.ActualTurnOwnerSeat == 0 && receipt.RecipientSeat == 1 &&
                receipt.DrawActual == 2 && receipt.Stage == DrawAdviceStage.Drawing && !receipt.QualificationsFrozen &&
                Draws(g, receipt).Length == 2 && Facts<DrawAdviceDrawIssuedEvent>(g).Count(e => e.FrameId == parentId && e.ActualCount == 2 &&
                    e.SequenceBefore == receipt.DrawBefore && e.SequenceAfter == receipt.DrawAfter) == 1 &&
                !Facts<DrawAdviceQualifiedEvent>(g).Any(e => e.FrameId == parentId),
            "True DrawPhaseEnded issues two native private entities once and suspends its exact original lifecycle parent before qualifying either participant.");
        AssertAdviceDrawChild(g, parentId); g = Cold(g, registry); Step(g);
        Reach(g, p => IsContinue(p, DrawChild) && p!.PromptId != firstPrompt);
        AssertAdviceDrawChild(g, parentId); var secondChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        Require(secondChild.Id != firstChild.Id && !Advice(g).DrawAdviceReceipt!.QualificationsFrozen &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == firstChild.Id && e.Completed) == 1,
            "Each one-card draw batch has its own real child; the first child returns once without redrawing or prematurely freezing the threshold.");
        g = Cold(g, registry); Step(g); Reach(g, _ => Facts<DrawAdviceQualifiedEvent>(g).Any(e => e.FrameId == parentId));
        var qualified = Facts<DrawAdviceQualifiedEvent>(g).Single(e => e.FrameId == parentId);
        Require(qualified.OwnerHandCount == ownerHand && qualified.OwnerMaxHp == initial[0].MaxHp &&
                qualified.RecipientHandCount == recipientHand + 2 && qualified.RecipientMaxHp == initial[1].MaxHp &&
                qualified.OwnerMustDiscard == ((variant & 1) != 0) && qualified.RecipientMustDiscard == ((variant & 2) != 0),
            "After both real gain children finish, one fact freezes both public hand/max-HP qualifications for the none, owner, recipient and both branches.");
        var costChildren = new List<long>();
        if (qualified.OwnerMustDiscard) costChildren.Add(PayAdviceParticipant(ref g, registry, parentId, 0, armor));
        if (qualified.RecipientMustDiscard) costChildren.Add(PayAdviceParticipant(ref g, registry, parentId, 1, null));
        Reach(g, IsPlay); g = Cold(g, registry);
        var resolved = Facts<DrawAdviceResolvedEvent>(g).Single(e => e.FrameId == parentId);
        var payments = Facts<DrawAdviceDiscardIssuedEvent>(g).Where(e => e.FrameId == parentId).ToArray();
        Require(resolved.OwnerDiscardActual == (qualified.OwnerMustDiscard ? 2 : 0) &&
                resolved.RecipientDiscardActual == (qualified.RecipientMustDiscard ? 2 : 0) &&
                payments.Select(e => e.DiscardSeat).SequenceEqual(new[] { 0, 1 }.Where(s => s == 0 ? qualified.OwnerMustDiscard : qualified.RecipientMustDiscard)) &&
                payments.All(e => e.ActualCount == 2 && e.CardIds.Count == 2) &&
                costChildren.All(id => Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Completed) == 1 &&
                Facts<DrawAdviceDrawIssuedEvent>(g).Count(e => e.FrameId == parentId) == 1 &&
                Facts<DrawAdviceQualifiedEvent>(g).Count(e => e.FrameId == parentId) == 1 && Draws(g, receipt).Length == 2 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == parentId),
            "Owner then recipient pay their own frozen HE cost in text order; all native children return once without requalifying, redrawing or repaying the original DrawEnd instruction.");
        foreach (var payment in payments)
            Require(payment.CardIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile &&
                m.Sequence > payment.SequenceBefore && m.Sequence <= payment.SequenceAfter && m.Reason.Value == AdviceDiscardReason) == 1),
                "Each issued participant receipt proves one actual atomic discard per selected physical entity.");
        if (qualified.OwnerMustDiscard)
            Require(armor is { } lion && payments.Single(e => e.DiscardSeat == 0).CardIds.Contains(lion) &&
                    g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == AdviceDiscardReason) == 1 &&
                    g.CreateSnapshot(0).Players[0].Hp == initial[0].Hp + 1 &&
                    Facts<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
                "The owner's real Silver Lion equipment cost heals one HP through its native child, without converting discard-two into hand-only payment.");
    }

    private const string AdviceDrawReason = "skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.draw";
    private const string AdviceDiscardReason = "skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.discard";
    private static ProgramSkillFrame Advice(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DrawAdviceReceipt is not null);
    private static CardMovementRecord[] Draws(GameEngine g, DrawAdviceReceipt receipt) => g.CardMovements.Where(m => m.From == CardLocation.DrawPile &&
        m.To == CardLocation.Hand(1) && m.Reason.Value == AdviceDrawReason && m.Sequence > receipt.DrawBefore && m.Sequence <= receipt.DrawAfter).ToArray();
    private static void AssertAdviceDrawChild(GameEngine g, long parentId)
    {
        var parent = Advice(g); var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == child.WindowContext!.ParentFrameId);
        Require(parent.Id == parentId && child.OwnerSeat == 1 && movement.Batch.ParentFrameId == parentId &&
                movement.Batch.OriginOwnerSeat == 0 && movement.Batch.OriginSkillId == Bizheng && movement.Batch.OriginSkillInstanceId == parent.SkillInstanceId &&
                movement.Batch.Movements is [var draw] && draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) && draw.Reason.Value == AdviceDrawReason &&
                child.WindowContext!.MovementBatch?.Id == movement.Batch.Id,
            "The recipient's private gain child has the exact original program movement parent, source instance and one-card physical draw batch.");
        Private(g, 1); Frozen(P(g)!.Choices); Frozen(movement.Batch.Movements);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
    }

    private static long PayAdviceParticipant(ref GameEngine g, ContentRegistry registry, long parentId, int seat, int? armor)
    {
        var sawArmorRecovery = false;
        for (var step = 0; step < 96; step++)
        {
            var p = P(g);
            if (IsContinue(p, HpChild)) { sawArmorRecovery |= p!.PlayerSeat == 0; g = Cold(g, registry); Step(g); continue; }
            if (IsContinue(p, CostChild))
            {
                var parent = Advice(g); var r = parent.DrawAdviceReceipt!;
                var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == CostChild);
                var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == child.WindowContext!.ParentFrameId);
                var issued = Facts<DrawAdviceDiscardIssuedEvent>(g).Single(e => e.FrameId == parentId && e.DiscardSeat == seat);
                Require(parent.Id == parentId && parent.InstructionIndex == 2 && r.Stage == DrawAdviceStage.Discarding && r.QualificationsFrozen &&
                        r.ParticipantCursor == seat && r.SelectedDiscardCardIds.Count == 2 && r.SelectedSourceLocations.Count == 2 &&
                        issued.CardIds.SequenceEqual(r.SelectedDiscardCardIds) && issued.BatchId == r.LastDiscardBatchId &&
                        issued.SequenceBefore == r.LastDiscardBefore && issued.SequenceAfter == r.LastDiscardAfter &&
                        child.OwnerSeat == seat && movement.Batch.Id == r.LastDiscardBatchId && movement.Batch.ParentFrameId == parentId &&
                        child.WindowContext!.MovementBatch?.Id == movement.Batch.Id &&
                        movement.Batch.Movements.Select(m => m.CardId).SequenceEqual(r.SelectedDiscardCardIds) &&
                        movement.Batch.Movements.All(m => m.From.OwnerSeat == seat && m.To == CardLocation.DiscardPile && m.Reason.Value == AdviceDiscardReason),
                    "A forced HE discard freezes the selected entities and locations on its original receipt, pays one exact native batch, then suspends on that participant's real child.");
                Require(seat != 0 || armor is null || sawArmorRecovery, "The paid Silver Lion's actual recovery child was cold-restored before the mixed HE movement child.");
                Frozen(r.SelectedDiscardCardIds); Frozen(r.SelectedSourceLocations); Frozen(issued.CardIds); Frozen(movement.Batch.Movements); Private(g, seat);
                g = Cold(g, registry); Step(g); return child.Id;
            }
            if (p?.SkillPrompt?.SkillId == Bizheng && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "draw-advice-discard"))
            {
                var receipt = Advice(g).DrawAdviceReceipt!;
                Require(receipt.QualificationsFrozen && receipt.Stage == DrawAdviceStage.ChoosingDiscard && receipt.ParticipantCursor == seat &&
                        receipt.RequiredDiscardCount == 2 && p.PlayerSeat == seat, "Only the actual frozen participant chooses its own two-card HE cost.");
                Private(g, seat); Frozen(p.Choices);
                if (receipt.SelectedDiscardCardIds.Count > 0) { Frozen(receipt.SelectedDiscardCardIds); Frozen(receipt.SelectedSourceLocations); }
                g = Cold(g, registry);
                if (seat == 0)
                {
                    var oldPrompt = P(g)!; var oldRevision = g.Revision;
                    var locations = g.CreateCardZoneDiagnostics().ToDictionary(z => z.CardId, z => z.Location);
                    var chosen = receipt.SelectedDiscardCardIds.Count == 0 && armor is { } equipment ? equipment :
                        P(g)!.Choices.First(c => c.Cards.Count == 1 && locations[c.Cards[0]] == CardLocation.Hand(0)).Cards[0];
                    Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
                    Answer(g, c => c.Cards.SequenceEqual([chosen]));
                    Reject(g, new AnswerPromptCommand(0, oldPrompt.PromptId, oldPrompt.Choices.First().Id, oldRevision));
                }
                else { Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision)); Step(g); }
                continue;
            }
            Step(g);
        }
        throw new InvalidOperationException("Frozen participant payment missed its real child: " + Diagnostic(g));
    }

    private sealed class Fixture(CardKind kind, int advice, bool forbidden) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-draw-advice-designation", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var prep = new List<string>();
            if (advice >= 0)
            {
                if ((advice & 1) == 0) prep.AddRange(["{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"empty-owner\"}",
                    "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"empty-owner\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}"]);
                if ((advice & 2) == 0) prep.AddRange(["{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"}",
                    "{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"empty-recipient\"}",
                    "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"empty-recipient\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}"]);
                if (prep.Count == 0) prep.Add("{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"ready\",\"options\":[{\"id\":\"continue\"}]}");
            }
            var equipmentPreparation = advice >= 0 && (advice & 1) != 0 ? """
                ,{"id":"equip-preparation","window":"turnStartBeforeNormalFlow","subject":"owner","priority":100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[
                    {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"},{"op":"loseHp","target":"owner","amount":1}]}
                """ : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"viewAs":[
                {"id":"as-fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false},
                {"id":"as-thunder","inputKinds":["slash"],"inputSuits":[],"outputKind":"thunderSlash","forPlay":true,"forResponse":false,"usesPerPhase":1},
                {"id":"duel-response","inputKinds":["duel"],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true}],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"discard-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"gate"},{"op":"moveBoundCards","target":"owner","sourceBind":"gate","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{{Prep}}}","revision":1,"triggers":[{"id":"prepare","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{{{string.Join(',', prep.Count > 0 ? prep : ["{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"ready\",\"options\":[{\"id\":\"continue\"}]}"])}}}]} {{{equipmentPreparation}}} ]},
              {"id":"{{{DrawChild}}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{CostChild}}}","revision":1,"triggers":[{"id":"actual-cost","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{HpChild}}}","revision":1,"triggers":[{"id":"real-hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{CompleteChild}}}","revision":1,"triggers":[{"id":"real-complete","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardKinds":["slash","fireSlash","thunderSlash","snatch","ironChain","peach","alcohol","borrowedSword","duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"complete-child","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:draw-advice-prohibited","revision":1,"cardPolicies":[{"id":"real-prohibition","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash","snatch"]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Prep, DrawChild, CostChild, HpChild, CompleteChild, "fixture:draw-advice-prohibited" }.ToDictionary(id => id,
                    id => new { name = id, description = "真实阶段、实体付款与精确目标子窗", optionLabels =
                        id is Driver or "fixture:draw-advice-prohibited" || id == Prep && advice >= 0 && !prep.Any(e => e.Contains("\"op\":\"chooseOption\"", StringComparison.Ordinal))
                            ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "共享机制行为夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:draw-advice-peer", "固定其他角色", "选将对照") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new(Owner, "实际共享技能拥有者", "supporter", advice >= 0 ? Bizheng : Yidian, "wu", advice >= 0 ? 3 : 12,
                advice >= 0 ? [Prep, Driver, DrawChild, CostChild, HpChild] : [Driver, HpChild, CompleteChild]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:draw-advice-peer-{i}").ToArray();
            for (var i = 0; i < peers.Length; i++) b.AddGeneral(new(peers[i], "真实其他角色", "supporter", "fixture:draw-advice-peer", "wei", advice >= 0 ? 3 : 12,
                forbidden && i == 2 ? ["fixture:draw-advice-prohibited", DrawChild, CostChild, HpChild] : [DrawChild, CostChild, HpChild]));
            var definition = kind switch { CardKind.Slash => "standard:slash", CardKind.Dodge => "standard:dodge", CardKind.Peach => "standard:peach",
                CardKind.Alcohol => "standard:alcohol", CardKind.Snatch => "standard:snatch", CardKind.IronChain => "standard:iron_chain",
                CardKind.BorrowedSword => "classic:borrowed-sword", CardKind.Indulgence => "standard:indulgence", CardKind.Crossbow => "standard:crossbow",
                CardKind.Duel => "standard:duel", _ => throw new InvalidOperationException("Fixture card has no native definition.") };
            // The Borrowed Sword fixture has no Slash or counterspell, so both native weapon-transfer branches execute.
            b.AddDeck(new("fixture:draw-advice-deck", "固定实体小牌库", advice >= 0 ? 8 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(
                advice >= 0 ? (i % 3 == 0 ? "classic:silver-lion" : "standard:dodge") : kind == CardKind.BorrowedSword ? (i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? definition : "standard:dodge") : definition, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实摸牌结束与指定目标扩展", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:draw-advice-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
