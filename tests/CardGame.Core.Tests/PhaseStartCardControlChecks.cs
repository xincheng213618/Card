using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PhaseStartCardControlChecks
{
    private const string Meibu = "ol:meibu", Mumu = "ol:mumu", Zhixi = "ol:zhixi";
    private const string Driver = "fixture:phase-control-driver", Watch = "fixture:phase-control-watch", Loss = "fixture:phase-control-loss";
    private const string Protected = "fixture:phase-control-protected", Owner = "fixture:phase-control-owner", Mode = "identity:classic-phase-control";

    public static void ExactEntryAndDerivedResourceContracts()
    {
        var rules = Embedded("rules.json"); var labels = Embedded("presentation.json");
        _ = SkillProgramCatalog.Load(rules, labels);
        foreach (var skill in new[] { Meibu, Mumu })
        foreach (var mutate in new Action<JsonObject>[]
        {
            t => t["optional"] = false,
            t => { t["usageScope"] = "game"; t["usageLimit"] = 1; },
            t => t["window"] = "turnEnding",
            t => t["turnOwnerScope"] = skill == Meibu ? "own" : "otherLiving",
            t => t["effects"]![0]!["target"] = "selectedTarget",
            t => t["effects"]![0]!["amount"] = 1,
            t => t["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""))
        })
        {
            var invalid = JsonNode.Parse(rules)!;
            mutate(invalid["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == skill)!["triggers"]![0]!.AsObject());
            Reject(() => SkillProgramCatalog.Load(invalid.ToJsonString(), labels), "Phase-starting control rejects wrong boundary, unsupported payment and composed effects before runtime.");
        }
        foreach (var malformed in new[] { "unknown", "empty", "composed", "wrong-policy" })
            Reject(() => ContentRegistry.Build(new StandardContentPackage(), new Fixture("standard:dodge", "meibu", malformedDerived: malformed)),
                "A phase grant names a known, pure restriction leaf; arbitrary or composed skills cannot use its paid continuation.");
        var badPolicy = JsonNode.Parse(rules)!;
        badPolicy["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Zhixi)!["cardPolicies"]![0]!["cardKinds"] = JsonNode.Parse("""["slash"]""");
        Reject(() => SkillProgramCatalog.Load(badPolicy.ToJsonString(), labels), "The actual-use budget applies to all kinds rather than a filtered card subset.");
    }

    public static void MeibuPaymentPrivacyDirectedDistanceAndActualTurnExpiry()
    {
        foreach (var (card, suit, expected) in new[]
        {
            ("standard:slash", Suit.Spade, false), ("standard:dismantlement", Suit.Spade, false),
            ("standard:dismantlement", Suit.Heart, true), ("standard:dodge", Suit.Spade, true)
        })
        {
            var (g, registry) = Start(card, "meibu", suit: suit); End(g);
            Reach(g, p => Optional(p, Meibu)); Activate(g); Reach(g, ControlChoice);
            Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
                "The real HE payment is private to the donor and never exposes hand choices to other viewers.");
            var p = P(g)!; var before = State(g);
            Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices.First(c => c.Cards.Count == 1).Id, g.Revision)).Accepted && State(g) == before,
                "A wrong actor cannot choose a private cost or mutate an issued offer.");
            g = Cold(g, registry); var paid = P(g)!.Choices.First(c => c.Parameters["branch"] == "pay").Cards.Single();
            Answer(g, c => c.Cards.SequenceEqual([paid])); DrainControl(g);
            var grant = E<OtherPlayRestrictionGrantedEvent>(g).Single();
            Need(grant.DirectedDistanceOne == expected && grant.Source.OwnerSeat == 0 && grant.RecipientSeat == g.CreateSnapshot(0).CurrentSeat &&
                V(g, grant.RecipientSeat).Skills!.Any(s => s.ContentId == Zhixi) &&
                g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == CostReason) == 1,
                "One real discarded entity determines the exact frozen Slash/black-trick exception and one independent derived grant.");
            g = Cold(g, registry); Reach(g, _ => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == grant.ActualTurnNumber && e.ActorSeat == grant.ActualTurnOwnerSeat));
            Need(!V(g, grant.RecipientSeat).Skills!.Any(s => s.ContentId == Zhixi), "The temporary restriction expires at the recipient's actual turn end.");
        }
        var (range, rangeRegistry) = Start("standard:qinggang_sword", "meibu"); Use(range, "equip", targets: [2]); Play(range); Use(range, "equip", targets: [0]); Play(range);
        var equippedCost = V(range, 0).Equipment.Single().Id; End(range);
        Reach(range, p => Optional(p, Meibu) && range.CreateSnapshot(0).CurrentSeat == 2);
        Need(range.GetCombatDistance(2, 0) == 2 && range.GetCombatDistance(0, 2) == 2, "The actual two-seat attacker has sufficient equipped range before the directed override.");
        Activate(range); Reach(range, ControlChoice); Answer(range, c => c.Cards.SequenceEqual([equippedCost])); DrainControl(range);
        var directed = E<OtherPlayRestrictionGrantedEvent>(range).Single();
        Need(directed.RecipientSeat == 2 && range.GetCombatDistance(2, 0) == 1 && range.GetCombatDistance(0, 2) == 2,
            "The paid distance override is recipient-to-donor only, rather than the reverse direction or a symmetric distance.");
        range = Cold(range, rangeRegistry); Reach(range, _ => E<TurnEndedEvent>(range).Any(e => e.TurnNumber == directed.ActualTurnNumber && e.ActorSeat == 2));
        Need(range.GetCombatDistance(2, 0) == 2, "The directed override ends with exactly the same actual turn as the derived skill.");
    }

    public static void PaidSourceLossDeathAndGrantChildrenColdReturn()
    {
        foreach (var child in new[] { "source-loss", "owner-death", "grant-child" })
        {
            var (g, registry) = Start("standard:dodge", "meibu", child, humanRole: child == "owner-death" ? Role.Renegade : Role.Lord);
            End(g); Reach(g, p => Optional(p, Meibu)); Activate(g); Reach(g, ControlChoice);
            var cost = P(g)!.Choices.First(c => c.Parameters["branch"] == "pay").Cards.Single();
            Answer(g, c => c.Cards.SequenceEqual([cost])); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseStartCardControl is not null);
            Need(root.PhaseStartCardControl!.Payment is not null && root.PhaseStartCardControl.Stage ==
                    (child == "grant-child" ? PhaseStartCardControlStage.GrantChildren : PhaseStartCardControlStage.MovementChildren),
                "The observer is nested beneath the exact once-paid movement or issued grant, before its parent returns.");
            g = Cold(g, registry); Continue(g);
            if (child == "source-loss")
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Loss); g = Cold(g, registry); Continue(g);
                Need(V(g, 0).IsAlive && !V(g, 0).Skills!.Any(s => s.ContentId == Meibu), "A real Dying-entry replacement removes the paid donor's source after recovery.");
                g = Cold(g, registry);
            }
            if (child == "owner-death")
            {
                g = Cold(g, registry);
            }
            DrainControl(g);
            var grant = E<OtherPlayRestrictionGrantedEvent>(g).Single();
            Need(g.CreateSnapshot(0).Winner == Winner.None && V(g, grant.RecipientSeat).IsAlive &&
                V(g, grant.RecipientSeat).Skills!.Any(s => s.ContentId == Zhixi) &&
                E<PhaseStartCardControlPaidEvent>(g).Count() == 1 &&
                g.CardMovements.Count(m => m.CardId == cost && m.Reason.Value == CostReason) == 1 &&
                (child != "owner-death" || !V(g, 0).IsAlive && E<PlayerDyingEvent>(g).Any(e => e.VictimSeat == 0) && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0)),
                "The completed cost resumes once through source loss, donor death or SkillsChanged children and still grants the living actual phase owner.");
            _ = Cold(g, registry);
        }
    }

    public static void MumuProtectedDiscardSelfArmorAndSlashBan()
    {
        var (g, registry) = Start("standard:slash", "mumu", equipment: "fixture:phase-control-armor");
        Use(g, "equip", targets: [0]); Play(g); Use(g, "equip", targets: [1]); Play(g); End(g);
        Reach(g, p => Optional(p, Mumu)); Activate(g); Reach(g, ControlChoice);
        Need(P(g)!.Choices.Any(c => c.Parameters["branch"] == "obtain" && c.Targets.SequenceEqual([0])) &&
            P(g)!.Choices.Any(c => c.Parameters["branch"] == "obtain" && c.Targets.SequenceEqual([1])) &&
            P(g)!.Choices.All(c => c.Parameters["branch"] != "discard" || !c.Targets.SequenceEqual([1])),
            "Self armor is a legal acquisition; another player's protected armor forbids actual foreign discard but still permits obtaining it.");
        var armor = P(g)!.Choices.First(c => c.Parameters["branch"] == "obtain" && c.Targets.SequenceEqual([0])).Cards.Single();
        var hand = V(g, 0).HandCount; g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([armor]) && c.Parameters["branch"] == "obtain"); DrainControl(g);
        var declared = E<CardUseDeclaredEvent>(g).Count(); Reach(g, p => Optional(p, Driver)); Activate(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(g, c => c.Targets.SequenceEqual([1]));
        Pause(g, "a"); Need(E<CardUseDeclaredEvent>(g).Count() == declared, "The issued Mumu ban also blocks the vanilla zero-material Slash producer in its actual own turn.");
        Continue(g); Play(g);
        Need(V(g, 0).HandCount == hand + 1 && E<StartingEquipmentControlCompletedEvent>(g).Single().ArmorAcquired &&
            g.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Hand(0)) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "One real self-armor acquisition grants the own-turn Slash ban without a second fee or an extra draw.");
        var hp = V(g, 0).Hp; var heldSlashes = V(g, 0).Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        Need(heldSlashes.Length > 0, "Real Slash entities are held before the prohibition is exercised by the incoming Duel.");
        g = Cold(g, registry); Use(g, "incoming", targets: [1]); Play(g);
        Need(E<DuelResponseEvent>(g).Last() is { ResponderSeat: 0, UsedSlash: false, SlashCardId: null } && V(g, 0).Hp == hp - 1 &&
            heldSlashes.All(id => V(g, 0).Hand.Any(c => c.Id == id)),
            "Mumu removes all legal played Slash responses; the native no-response path applies one Duel damage and leaves every held Slash unspent.");
        _ = Cold(g, registry);

        var (discard, discardRegistry) = Start("standard:dodge", "mumu-discard", equipment: "standard:crossbow");
        Use(discard, "equip", targets: [1]); Play(discard); var initial = V(discard, 0).HandCount; End(discard);
        Reach(discard, p => Optional(p, Mumu)); Activate(discard); Reach(discard, ControlChoice);
        var gear = P(discard)!.Choices.First(c => c.Parameters["branch"] == "discard").Cards.Single();
        Answer(discard, c => c.Cards.SequenceEqual([gear])); DrainControl(discard); Play(discard);
        Need(V(discard, 0).HandCount == initial && !E<CardActionProhibitionGrantedEvent>(discard).Any() &&
            E<StartingEquipmentControlCompletedEvent>(discard).Single() is { ArmorAcquired: false, To.Zone: CardZoneKind.DiscardPile },
            "Current-version Mumu discard has no old-version draw and no acquisition-only Slash ban."); _ = Cold(discard, discardRegistry);

        var (treasure, treasureRegistry) = Start("standard:dodge", "mumu", equipment: "standard:jade_seal");
        Use(treasure, "equip", targets: [1]); Play(treasure); End(treasure); Play(treasure);
        Need(!E<ProgramBindingStartedEvent>(treasure).Any(e => e.SkillId == Mumu), "A protected foreign treasure alone never creates an illegal Mumu discard offer."); _ = Cold(treasure, treasureRegistry);
    }

    public static void ZhixiVanillaVirtualUsesDynamicHpAndPhaseReset()
    {
        var (g, registry) = Start("standard:dodge", "zhixi", "dynamic", stopAtDriver: true);
        Activate(g); Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(g, c => c.Targets.SequenceEqual([1]));
        Pause(g, "a"); Need(Uses(g).Count() == 1 && Uses(g).Last().State.UseCount == 1, "A vanilla zero-material Slash is a real use in a leaf restriction catalog.");
        g = Cold(g, registry); Continue(g); Pause(g, "b");
        Need(V(g, 0).Hp == 1 && Uses(g).Count() == 1, "Lower current HP immediately stops the next virtual use after the already used card reaches X.");
        Continue(g); Pause(g, "c"); Need(V(g, 0).Hp == 2 && Uses(g).Count() == 2 && Uses(g).Last().State.UseCount == 2,
            "Recovery raises the dynamic budget and permits exactly one more genuine virtual use.");
        Continue(g); Pause(g, "d"); Need(Uses(g).Count() == 2, "The third attempted virtual Slash remains unused at the new two-HP budget.");
        g = Cold(g, registry); Continue(g); Play(g); var firstPhase = Uses(g).Last().State.PhaseInstanceId; End(g);
        Reach(g, p => Optional(p, Driver)); Activate(g); Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(g, c => c.Targets.SequenceEqual([1])); Pause(g, "a");
        Need(Uses(g).Last().State.UseCount == 1 && Uses(g).Last().State.PhaseInstanceId != firstPhase,
            "The next actual own play phase starts a fresh ledger even with the same printed restriction source."); _ = Cold(g, registry);
    }

    public static void ZhixiTrickDelayedAndCounterspellActualUses()
    {
        foreach (var card in new[] { "standard:draw_two", "standard:indulgence" })
        {
            var (g, registry) = Start(card, "zhixi"); var action = g.GetHumanLegalActions().First(a => a.CardId is not null && a.Kind != LegalActionKind.Recast);
            Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId)); Play(g);
            Need(Uses(g).Count() == 1 && Uses(g).Single().EffectiveCardKind is CardKind.DrawTwo or CardKind.Indulgence &&
                !g.GetHumanLegalActions().Any(a => a.CardId is not null), "Either ordinary or delayed trick use immediately forbids all subsequent actual card uses.");
            var count = E<CardUseDeclaredEvent>(g).Count(); End(g); Reach(g, p => Optional(p, Driver)); Activate(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(g, c => c.Targets.SequenceEqual([1]));
            Reach(g, p => p.SkillPrompt?.SkillId == Driver && cOption(p, "continue"));
            Need(E<CardUseDeclaredEvent>(g).Count() == count, "The trick flag also prevents the vanilla zero-entity producer in the same actual phase."); _ = Cold(g, registry);
        }
        var (counter, counterRegistry) = Start("standard:nullification", "zhixi", "counterspell"); End(counter);
        Reach(counter, p => Optional(p, Driver)); Activate(counter);
        Reach(counter, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(counter, c => c.Targets.SequenceEqual([1]));
        Reach(counter, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Need(!Uses(counter).Any(), "The other participant's true zero-material trick does not charge the current owner's actual-use ledger.");
        var material = P(counter)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "nullification" && c.Cards.Count == 1).Cards.Single();
        counter = Cold(counter, counterRegistry); Answer(counter, c => c.Cards.SequenceEqual([material]));
        Reach(counter, p => p.SkillPrompt?.SkillId == Driver && cOption(p, "continue"));
        Need(Uses(counter).Single() is { State.ActorSeat: 0, State.UseCount: 1, EffectiveCardKind: CardKind.Nullification } &&
            E<CardUseDeclaredEvent>(counter).Count(e => e.CardKind == CardKind.Slash) == 0 &&
            counter.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Native counterspell response is an actual trick use; it pays once and blocks a subsequent virtual Slash in the owner's still-current phase."); _ = Cold(counter, counterRegistry);
    }

    public static void PlayedSlashResponseExcludedAndNativeAiControl()
    {
        var (g, registry) = Start("standard:slash", "zhixi", "budget", stopAtDriver: true);
        Activate(g); Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.Count == 1)); Answer(g, c => c.Targets.SequenceEqual([1]));
        foreach (var step in new[] { "a", "b", "c", "d" }) { Pause(g, step); Continue(g); }
        Play(g); Need(Uses(g).Count() == V(g, 0).Hp && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The exact current HP count forbids a new physical Slash use.");
        var count = Uses(g).Count(); Use(g, "incoming", targets: [1]); Reach(g, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0);
        Need(P(g)!.Choices.Any(c => c.Cards.Count == 1), "A Duel played Slash remains available under Zhixi's use-only prohibition.");
        var slash = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([slash]));
        Play(g); Need(Uses(g).Count() == count && E<CardActionAcceptedEvent>(g).Any(e => e.Action is { ActorSeat: 0, Type: CardActionType.Response, EffectiveKind: CardKind.Slash }),
            "The actual played response pays its real entity but never becomes an additional use ledger entry."); _ = Cold(g, registry);

        var (ai, aiRegistry) = Start("standard:dodge", "ai-control", equipment: "fixture:phase-control-armor");
        Use(ai, "equip", targets: [2]); Play(ai); End(ai); Reach(ai, p => Optional(p, Mumu) && p.PlayerSeat != 0);
        Accept(ai, new AdvanceOneStepCommand(ai.Revision)); Reach(ai, ControlChoice); Accept(ai, new AdvanceOneStepCommand(ai.Revision)); DrainControl(ai);
        Need(E<StartingEquipmentControlCompletedEvent>(ai).Any(e => e.Source.OwnerSeat != 0), "Native AI activates and resolves a public equipment control opportunity.");
        Reach(ai, p => Optional(p, Meibu) && p.PlayerSeat != 0); Accept(ai, new AdvanceOneStepCommand(ai.Revision)); Reach(ai, ControlChoice);
        Accept(ai, new AdvanceOneStepCommand(ai.Revision)); DrainControl(ai);
        Need(E<OtherPlayRestrictionGrantedEvent>(ai).Any(e => e.Source.OwnerSeat != 0) &&
            !ai.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat != 0), "Native AI chooses and pays its own private HE cost without fabricated human answers."); _ = Cold(ai, aiRegistry);
    }

    private const string CostReason = "skill-program.other-play-restriction.cost";
    private static string Embedded(string suffix)
    { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-sun-lu-yu.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static IEnumerable<ActualPlayPhaseCardUseRecordedEvent> Uses(GameEngine g) => E<ActualPlayPhaseCardUseRecordedEvent>(g).Where(e => e.State.ActorSeat == 0);
    private static bool Optional(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool ControlChoice(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "phase-start-card-control");
    private static bool cOption(PendingDecision p, string option) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void Pause(GameEngine g, string bind) => Reach(g, p => p.SkillPrompt?.SkillId == Driver &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == bind));
    private static void Accept(GameEngine g, GameCommand command)
    { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "The actual phase-control command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            var p = P(g); if (p is not null && done(p)) return;
            Need(g.CreateSnapshot(0).Status != EngineStatus.Completed, $"Fixture ended before its boundary: turn {g.CreateSnapshot(0).TurnNumber}, actor {g.CreateSnapshot(0).CurrentSeat}, HP {V(g, 0).Hp}, winner {g.CreateSnapshot(0).Winner}, tail {string.Join(',', g.Events.TakeLast(5).Select(e => e.Payload.GetType().Name))}.");
            if (p is { PlayerSeat: 0 })
            {
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (ControlChoice(p)) Answer(g, c => c.Parameters["branch"] == "decline");
                else if (p.Kind == DecisionKind.PlayCard) End(g);
                else throw new InvalidOperationException($"Unexpected actual phase-control prompt {p.Kind}: {p.Prompt}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed phase-control fixture exceeded its bounded native boundary.");
    }
    private static void DrainControl(GameEngine g)
    {
        for (var i = 0; i < 100 && g.ResolutionStack.Any(f => f is ProgramSkillFrame { PhaseStartCardControl: not null }); i++)
        {
            if (P(g) is { } p && (p.PlayerSeat == 0 || p.Kind == DecisionKind.RescueDying))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("option-id") == "continue");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Need(!g.ResolutionStack.Any(f => f is ProgramSkillFrame { PhaseStartCardControl: not null }), "The exact paid parent completes once after all real children.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves the actual paid prefix, private choices, phase ledger and typed children."); return copy; }
    private static void Reject(Action action, string message) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException(message); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(string card, string scenario, string child = "none", Suit suit = Suit.Spade,
        string? equipment = null, Role humanRole = Role.Lord, bool stopAtDriver = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(card, scenario, child, suit, equipment));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = humanRole, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (stopAtDriver) Reach(g, p => Optional(p, Driver)); else Play(g); return (g, registry);
    }
    private sealed class Fixture(string card, string scenario, string child = "none", Suit suit = Suit.Spade, string? equipment = null, string? malformedDerived = null) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("phase-start-card-control-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var productionRules = JsonNode.Parse(Embedded("rules.json"))!;
            if (malformedDerived == "unknown") productionRules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Meibu)!["triggers"]![0]!["effects"]![0]!["skillId"] = "fixture:unknown-derived";
            var derived = productionRules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Zhixi)!;
            if (malformedDerived is "empty" or "wrong-policy") derived["cardPolicies"] = JsonNode.Parse(malformedDerived == "empty" ? "[]" : """[{"id":"wrong","kind":"preventForeignEquipmentDiscard","cardKinds":[]}]""");
            if (malformedDerived == "composed") derived["triggers"] = JsonNode.Parse("""[{"id":"extra","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]""");
            var production = SkillProgramCatalog.Load(productionRules.ToJsonString(), Embedded("presentation.json"));
            foreach (var (id, program) in production.Programs) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = program, ProgramPresentation = production.Presentations[id], Tags = id == Zhixi ? SkillTag.Locked : SkillTag.None });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
              {"id":"{{Protected}}","revision":1,"cardPolicies":[{"id":"protected-armor-treasure","kind":"preventForeignEquipmentDiscard","cardKinds":[]}]}
            ]}
            """)!;
            var driver = rules["skills"]![0]!;
            if (scenario == "zhixi" || scenario == "mumu")
            {
                var start = child is "dynamic" or "budget" || scenario == "mumu";
                var effects = new JsonArray(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}"""));
                if (child == "counterspell") effects.Add(JsonNode.Parse("""{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"selectedTarget"},"outputKind":"drawTwo"}"""));
                var virtualOp = start ? """{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}""" : """{"op":"useVirtualSlash","target":"selectedTarget"}""";
                effects.Add(JsonNode.Parse(child == "budget" ? """{"op":"useVirtualSlash","target":"selectedTarget"}""" : virtualOp)); effects.Add(PauseNode("a"));
                if (child is "dynamic" or "budget")
                {
                    if (child == "dynamic") effects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":3}"""));
                    effects.Add(JsonNode.Parse(virtualOp)); effects.Add(PauseNode("b"));
                    if (child == "dynamic") effects.Add(JsonNode.Parse("""{"op":"recover","target":"owner","amount":1}"""));
                    effects.Add(JsonNode.Parse(virtualOp)); effects.Add(PauseNode("c")); effects.Add(JsonNode.Parse(virtualOp)); effects.Add(PauseNode("d"));
                }
                driver["triggers"] = new JsonArray(new JsonObject { ["id"] = "native-virtual", ["window"] = start ? "playPhaseStarting" : "playEnding", ["subject"] = "owner", ["turnOwnerScope"] = "own", ["priority"] = -1, ["optional"] = true, ["effects"] = effects });
            }
            if (child is "source-loss" or "owner-death")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-cost-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["{{CostReason}}"],"movementOccurrence":"perBatch","optional":false,"effects":[
                  {"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """));
                if (child == "source-loss") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[
                  {"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},
                  {"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Meibu}}"],"sourceBind":"standard:none"}]}]}
                """));
            }
            if (child == "grant-child") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
            {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-grant-child","window":"skillsChanged","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grant","options":[{"id":"continue"}]}]}]}
            """));
            var ids = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var withOption = rules["skills"]!.AsArray().Where(n => n!.ToJsonString().Contains("\"chooseOption\"", StringComparison.Ordinal))
                .Select(n => n!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = ids.ToDictionary(id => id,
                id => new { name = id, description = "实际阶段付款与真实用牌边界", optionLabels = withOption.Contains(id) ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) });
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), labels);
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var skills = new List<string> { Driver };
            if (scenario == "meibu") skills.Add(Meibu);
            if (scenario is "mumu" or "mumu-discard") skills.Add(Mumu);
            if (child is "source-loss" or "owner-death") skills.Add(Watch);
            if (child == "source-loss") skills.Add(Loss);
            b.AddGeneral(new(Owner, "真实阶段控制拥有者", "supporter", scenario == "zhixi" ? Zhixi : "standard:none", "wu", 3, skills, GeneralGender.Female));
            const string select = "fixture:phase-control-selection";
            b.AddSkill(new(select, "固定首个AI候选", "固定选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:phase-control-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他阶段角色", "supporter", select, "wei", 10,
                [.. (scenario == "mumu" ? new[] { Protected } : []), .. (child == "grant-child" ? new[] { Watch } : []), .. (scenario == "ai-control" ? new[] { Meibu, Mumu } : [])]));
            if (equipment == "fixture:phase-control-armor") b.AddCard(new(equipment, "仁王盾", "装备牌", "真实防具", CardKind.RenwangShield));
            b.AddDeck(new("fixture:phase-control-deck", "固定阶段实体牌堆", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(equipment is not null && i % 3 == 0 ? equipment : card, suit, 7)).ToArray() });
            b.AddMode(new(Mode, "真实阶段控制结算", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "fixture:phase-control-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static JsonNode PauseNode(string bind) => JsonNode.Parse($$"""{"op":"chooseOption","target":"owner","resultBind":"{{bind}}","options":[{"id":"continue"}]}""")!;
    }
}
