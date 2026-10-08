using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using static LastZoneConversionScenario;

internal static class LastZoneConversionChecks
{
    public static void StrictPolicyAndRealHandCountAreRecheckedAtNativeDodgePayment()
    {
        ValidateSchema();
        var (g, registry) = Create("hand");
        var original = g.CreateSnapshot(0).Players[0].Hand;
        Activate(g, "incoming", targets: [1]);
        FinishUnavailableDodge(g, 4, "Four physical Hand entities cannot publish a last-Hand conversion.");
        Activate(g, "drop", g.CreateSnapshot(0).Players[0].Hand.Skip(1).Select(c => c.Id).ToArray()); ReachPlay(g);
        var id = g.CreateSnapshot(0).Players[0].Hand.Single().Id;
        Activate(g, "draw"); ReachPlay(g);
        Activate(g, "incoming", targets: [1]);
        FinishUnavailableDodge(g, 2, "A real newly drawn second Hand entity removes the conversion immediately.");
        Activate(g, "drop", g.CreateSnapshot(0).Players[0].Hand.Where(c => c.Id != id).Select(c => c.Id).ToArray()); ReachPlay(g);
        Activate(g, "incoming", targets: [1]); Reach(g, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        var choice = Prompt(g)!.Choices.Single(c => IsConversion(c) && c.Cards.SequenceEqual([id]));
        RejectWrongActor(g); PrivateHand(g); g = Cold(g, registry);
        Answer(g, c => c.Id == choice.Id);
        Reach(g, p => p?.SkillPrompt?.SkillId == Accepted);
        var action = Response(g, id, CardKind.Dodge);
        Require(action.PhysicalCards.Single().From == CardLocation.Hand(0) && action.ConversionChain.Single().BindingId == "last-hand-dodge" &&
            action.ActorSeat == 0 && action.ProviderSeat == 0 && action.RequesterSeat is null,
            "The last Hand card is paid as the actor's own native Slash-defense Dodge, with exact configured provenance.");
        g = Cold(g, registry); ReachPlay(g);
        AssertPayment(g, id, CardKind.Crossbow, CardLocation.Hand(0), CardMoveReasons.Respond, CardMoveReasons.ResponseFinished);
        Require(original.Count == 4 && g.CreateSnapshot(0).Players[0].HandCount == 0,
            "Retained prepared Hand views are immutable and the final real Hand entity leaves once.");
        _ = Cold(g, registry);
        var (grain, grainRegistry) = Create("grain");
        Play(grain, grain.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip)); ReachPlay(grain);
        var storage = grain.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.WoodenOx);
        var stored = storage.SelectableCardIds.First();
        Accept(grain, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [stored], [], grain.Revision, Prompt(grain)!.PromptId)); ReachPlay(grain);
        Activate(grain, "drop", grain.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray()); ReachPlay(grain);
        Require(grain.CreateSnapshot(0).Players[0].HandCount == 0 && grain.CreateSnapshot(0).Players[0].WoodenOxGrainCount == 1,
            "The native Wooden Ox action leaves one real stored entity and no physical Hand entity.");
        Activate(grain, "incoming", targets: [1]); grain = Cold(grain, grainRegistry);
        FinishUnavailableDodge(grain, 0, "A sole usable Wooden Ox grain is never the last actual Hand card.");
        Require(grain.CreateCardZoneDiagnostics().Single(d => d.CardId == stored).Location == CardLocation.WoodenOxGrain(0),
            "The rejected Hand-like conversion neither spends nor relocates the real grain.");

        var (group, groupRegistry) = Create("arrow");
        Activate(group, "drop", group.CreateSnapshot(0).Players[0].Hand.Skip(1).Select(c => c.Id).ToArray()); ReachPlay(group);
        var groupCard = group.CreateSnapshot(0).Players[0].Hand.Single().Id;
        Accept(group, new EndPlayPhaseCommand(0, group.Revision, Prompt(group)!.PromptId));
        Reach(group, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        Require(group.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.ArrowBarrage && f.SourceSeat != 0),
            "An original native AI actually uses Arrow Barrage against the last-Hand holder.");
        var groupChoice = Prompt(group)!.Choices.Single(c => IsConversion(c) && c.Cards.SequenceEqual([groupCard]));
        group = Cold(group, groupRegistry); Answer(group, c => c.Id == groupChoice.Id);
        Reach(group, p => p?.SkillPrompt?.SkillId == Accepted);
        Require(Response(group, groupCard, CardKind.Dodge).Type == CardActionType.Response &&
            group.Events.Any(e => e.Payload is GroupResponseEvent r && r.ResponderSeat == 0 && r.IncomingCard == CardKind.ArrowBarrage && r.ResponseCardId == groupCard && r.UsedResponse),
            "The same last-Hand policy also supports a native pure group Dodge response, without imposing a use-only direction.");
        group = Cold(group, groupRegistry);
        for (var step = 0; step < 700 && !group.CardMovements.Any(m => m.CardId == groupCard && m.Reason == CardMoveReasons.ResponseFinished); step++) Step(group);
        AssertPayment(group, groupCard, CardKind.ArrowBarrage, CardLocation.Hand(0), CardMoveReasons.Respond, CardMoveReasons.ResponseFinished);
        _ = Cold(group, groupRegistry);
    }

    public static void LastEquipmentNullificationOwnsSilverLionRecoveryAndMovementChildren()
    {
        var (g, registry) = Create("equipment");
        var lionAction = g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Equip &&
            g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Kind == CardKind.SilverLion)
            ?? throw new InvalidOperationException("The fixed equipment fixture needs a real Lion: " + JsonSerializer.Serialize(g.CreateSnapshot(0).Players[0].Hand));
        var lion = lionAction.CardId!.Value; Play(g, lionAction); ReachPlay(g);
        var weaponAction = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip &&
            g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Kind == CardKind.Crossbow);
        var weapon = weaponAction.CardId!.Value; Play(g, weaponAction); ReachPlay(g);
        var nullBefore = g.Events.Count(e => e.Payload is CardActionAcceptedEvent x && x.Action.EffectiveKind == CardKind.Nullification);
        Play(g, ObtainNativeDrawTwoAction(g)); ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Equipment.Count == 2 && g.Events.Count(e => e.Payload is CardActionAcceptedEvent x && x.Action.EffectiveKind == CardKind.Nullification) == nullBefore,
            "Two actual equipment entities cannot supply a last-equipment counterspell.");
        Activate(g, "remove-equipment"); Reach(g, p => p?.Choices.Any(c => c.Cards.SequenceEqual([weapon])) == true);
        Answer(g, c => c.Cards.SequenceEqual([weapon])); ReachPlay(g);
        Activate(g, "wound"); ReachPlay(g); var hp = g.CreateSnapshot(0).Players[0].Hp;
        Play(g, ObtainNativeDrawTwoAction(g));
        Reach(g, p => p is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var choice = Prompt(g)!.Choices.Single(c => IsConversion(c) && c.Cards.SequenceEqual([lion]));
        RejectWrongActor(g); g = Cold(g, registry); Answer(g, c => c.Id == choice.Id);
        Reach(g, p => p?.SkillPrompt?.SkillId == Health);
        Require(g.CreateSnapshot(0).Players[0].Hp == hp + 1 &&
            g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1,
            "The real equipped Lion pays once and its own native removal recovery precedes the suspended counterspell return.");
        g = Cold(g, registry); Step(g);
        Reach(g, p => p?.SkillPrompt?.SkillId == Cost); g = Cold(g, registry); Step(g); ReachPlay(g);
        var action = Response(g, lion, CardKind.Nullification);
        Require(action.PhysicalCards.Single().From == CardLocation.Equipment(0) && action.ConversionChain.Single().BindingId == "last-equipment-nullification" &&
            g.Events.Count(e => e.Payload is SilverLionRemovedRecoveryEvent s && s.PlayerSeat == 0) == 1,
            "The counterspell retains the printed armor and exact equipment source through every real HP and movement child, with one recovery.");
        AssertPayment(g, lion, CardKind.SilverLion, CardLocation.Equipment(0), CardMoveReasons.Nullification, CardMoveReasons.NullificationFinished);
        _ = Cold(g, registry);
    }

    public static void LastJudgmentSlashUsesAndPureDuelResponsesRetainPrintedDelayedMaterial()
    {
        foreach (var use in new[] { true, false })
        {
            var (g, registry) = Create("judgment"); var id = PlaceJudgment(g);
            Require(g.CreateSnapshot(0).Players[0].HandCount == 3 && g.GetHumanLegalActions().Any(a => a.CardId == id && a.ConversionSource?.BindingId == "last-judgment-slash"),
                "The lone real Judgment card converts independently of three physical Hand cards.");
            g = Cold(g, registry);
            if (use)
            {
                var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == id && a.ConversionSource?.BindingId == "last-judgment-slash");
                var before = State(g);
                var rejected = g.Submit(new PlayCardCommand(1, id, [action.TargetSeat!.Value], g.Revision, Prompt(g)!.PromptId,
                    CardKind.Slash) { ConversionSource = action.ConversionSource });
                Require(!rejected.Accepted && State(g) == before, "A foreign actor cannot use the owner's public Judgment material.");
                Play(g, action); Reach(g, p => p?.SkillPrompt?.SkillId == Completed);
                var paid = g.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Single(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Any(c => c.CardId == id)).Action;
                Require(paid.PhysicalCards.Single().From == CardLocation.Judgment(0) && paid.PhysicalCards.Single().CardKind == CardKind.Lightning &&
                    paid.ActorSeat == 0 && paid.ProviderSeat == 0 && paid.ConversionChain.Single().BindingId == "last-judgment-slash",
                    "A real Slash uses the actor's exact Judgment Lightning; only the effective kind changes, not the physical delayed identity.");
                g = Cold(g, registry); ReachPlay(g);
                AssertPayment(g, id, CardKind.Lightning, CardLocation.Judgment(0), CardMoveReasons.Use, CardMoveReasons.UseFinished);
                Require(g.Events.Count(e => e.Payload is CardUseFinishedEvent f && f.CardId == id && f.CardKind == CardKind.Slash) == 1,
                    "The complete real Judgment Slash returns from its native completion child once.");
            }
            else
            {
                Activate(g, "duel", targets: [1]); Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
                var choice = Prompt(g)!.Choices.Single(c => IsConversion(c) && c.Cards.SequenceEqual([id]));
                RejectWrongActor(g); g = Cold(g, registry); Answer(g, c => c.Id == choice.Id);
                Reach(g, p => p?.SkillPrompt?.SkillId == Accepted);
                var paid = Response(g, id, CardKind.Slash);
                Require(paid.PhysicalCards.Single().From == CardLocation.Judgment(0) && paid.PhysicalCards.Single().CardKind == CardKind.Lightning &&
                    paid.Type == CardActionType.Response && paid.ConversionChain.Single().BindingId == "last-judgment-slash",
                    "Pure Duel response can pay the actual final Judgment entity without turning it into an independent Slash use.");
                g = Cold(g, registry); ReachPlay(g);
                AssertPayment(g, id, CardKind.Lightning, CardLocation.Judgment(0), CardMoveReasons.Respond, CardMoveReasons.ResponseFinished);
                Require(g.Events.Count(e => e.Payload is DuelResponseEvent d && d.ResponderSeat == 0 && d.SlashCardId == id && d.UsedSlash) == 1 &&
                    !g.Events.Any(e => e.Payload is CardUseFinishedEvent f && f.CardId == id && f.CardKind == CardKind.Slash),
                    "Native Duel accepts the exact response once and never fabricates a separate Slash use completion.");
            }
            Require(!g.CreateSnapshot(0).Players[0].Judgment.Any() && !g.GetHumanLegalActions().Any(a => a.CardId == id),
                "The paid delayed entity leaves Judgment, its delayed-kind claim is cleaned, and it cannot be replayed as another conversion.");
            _ = Cold(g, registry);
        }
    }

    private static bool IsConversion(PromptChoice c) => c.Parameters.GetValueOrDefault("conversion-skill-id") == Conversion;
    private static void FinishUnavailableDodge(GameEngine g, int expectedHandCount, string message)
    {
        var start = g.Events.Count;
        var owner = g.CreateSnapshot(0).Players[0];
        var originalHand = owner.Hand.Select(c => c.Id).ToArray();
        Require(owner.HandCount == expectedHandCount, message);
        for (var step = 0; step < 700; step++)
        {
            var prompt = Prompt(g);
            Require(prompt?.Choices.Any(IsConversion) != true &&
                prompt is not { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 }, message);
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            Step(g);
        }
        var after = g.CreateSnapshot(0).Players[0];
        var facts = g.Events.Skip(start).Select(e => e.Payload).ToArray();
        Require(Prompt(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
            after.Hand.Select(c => c.Id).SequenceEqual(originalHand) && after.Hp == owner.Hp - 1 &&
            facts.OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            !facts.OfType<ResponseRequestedEvent>().Any(e => e.TargetSeat == 0 && e.RequiredCardKind == CardKind.Dodge) &&
            !facts.OfType<CardActionAcceptedEvent>().Any(e => e.Action.ProviderSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge),
            message + " Native no-card defense completes one real Slash damage without a response prompt, payment, or Hand mutation.");
    }
    private static LegalAction ObtainNativeDrawTwoAction(GameEngine g)
    {
        for (var draws = 0; draws <= 8; draws++)
        {
            var action = g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.DrawTwo);
            if (action is not null) return action;
            if (draws == 8) break;
            Activate(g, "draw"); ReachPlay(g);
        }
        throw new InvalidOperationException("Eight bounded real preparatory draws did not supply an actual legal DrawTwo: " +
            JsonSerializer.Serialize(g.CreateSnapshot(0).Players[0].Hand) + "; actions=" + JsonSerializer.Serialize(g.GetHumanLegalActions()));
    }
    private static CardActionContext Response(GameEngine g, int id, CardKind kind) => g.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>()
        .Single(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == kind && e.Action.PhysicalCards.Any(c => c.CardId == id)).Action;
    private static void AssertPayment(GameEngine g, int id, CardKind kind, CardLocation from, CardMoveReason pay, CardMoveReason finish) =>
        Require(g.CardMovements.Count(m => m.CardId == id && m.CardKind == kind && m.From == from && m.To == CardLocation.Processing && m.Reason == pay) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.CardKind == kind && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finish) == 1,
            "The original printed entity pays once from its real region into Processing and finishes to DiscardPile once.");
    private static void RejectWrongActor(GameEngine g)
    {
        var before = State(g); var p = Prompt(g)!;
        var rejected = g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision));
        Require(!rejected.Accepted && State(g) == before, "Wrong actor input cannot pay the public/private entity or advance its native parent.");
        rejected = g.Submit(new AnswerPromptCommand(0, p.PromptId, new("last-zone-unpublished-choice"), g.Revision));
        Require(!rejected.Accepted && State(g) == before, "An unpublished conversion source or stale choice rejects atomically before payment.");
    }
    private static void PrivateHand(GameEngine g)
    {
        for (var viewer = 1; viewer < 4; viewer++) Require(g.CreateSnapshot(viewer).Players[0].Hand.Count == 0,
            "The owner conversion candidate never exposes foreign Hand identities to other viewers.");
        var choices = Prompt(g)!.Choices; var rejected = false;
        try { ((IList<PromptChoice>)choices)[0] = choices[0]; } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Published native conversion choices retain frozen collection boundaries.");
    }
    private static void ValidateSchema()
    {
        var baseline = JsonNode.Parse(Rules())!;
        var legacy = baseline.DeepClone(); legacy["skills"]![0]!["viewAs"]!.AsArray().RemoveAt(2);
        foreach (var rule in legacy["skills"]![0]!["viewAs"]!.AsArray()) rule!.AsObject().Remove("lastInSourceZone");
        legacy["skills"]![0]!["viewAs"]![1]!["useOnly"] = false;
        var old = SkillProgramCatalog.Load(legacy.ToJsonString(), Presentation()).Programs[Conversion];
        Require(old.ViewAs.All(r => r.LastInSourceZone is null) && !JsonSerializer.Serialize(old).Contains("LastInSourceZone", StringComparison.Ordinal),
            "Omitted additive policies preserve the old serialized shape without fabricated false values.");
        foreach (var invalid in new[] { "multiple-zones", "multi-card", "judgment-without-opt-in", "judgment-false", "wrong-output", "wrong-direction", "wrong-use-only", "hand-like", "other-policy" })
        {
            var copy = baseline.DeepClone(); var rule = copy["skills"]![0]!["viewAs"]![2]!.AsObject();
            switch (invalid)
            {
                case "multiple-zones": rule["sourceZones"] = new JsonArray("judgment", "hand"); break;
                case "multi-card": rule["inputCount"] = 2; break;
                case "judgment-without-opt-in": rule.Remove("lastInSourceZone"); break;
                case "judgment-false": rule["lastInSourceZone"] = false; break;
                case "wrong-output": rule["outputKind"] = "dodge"; rule["forPlay"] = false; break;
                case "wrong-direction": rule["forResponse"] = false; break;
                case "wrong-use-only": copy["skills"]![0]!["viewAs"]![1]!["useOnly"] = false; break;
                case "hand-like": rule["sourceZones"] = new JsonArray("woodenOxGrain"); break;
                case "other-policy": rule["extendedUse"] = true; break;
            }
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), Presentation()); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The shared last-region loader rejects an unsupported exact policy: " + invalid);
        }
    }
}
