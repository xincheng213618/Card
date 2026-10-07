using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Pan Jun: docs/content/sources/ol-pan-jun-2026-10-07.json.
// CompleteSetup shuffles every deck, so fixtures guarantee heart kind counts
// only; the per-mode seeds below were fixed once by bounded search so each
// dealt hand holds the combination its scenario plays.
internal static class OrdinaryPanJunChecks
{
    private const string Guanwei = "ol:guanwei";
    private const string Gongqing = "ol:gongqing";
    private const string Human = "fixture:pj-human";

    private static readonly System.Collections.Generic.Dictionary<string, int> Seeds = new()
    {
        ["identity:pj-own"] = 7,
        ["identity:pj-other"] = 7,
        ["identity:pj-mixed"] = 7,
        ["identity:pj-empty"] = 7,
        ["identity:pj-cap"] = 7,
        ["identity:pj-equal"] = 8,
        ["identity:pj-boost"] = 8,
    };

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:pan-jun"];
        Require(general.Name == "潘濬" && general.FactionId == "wu" && general.BaseHp == 3 &&
            general.SkillIds.SequenceEqual([Guanwei, Gongqing]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol" &&
            general.Gender == GeneralGender.Male,
            "The OL Pan Jun general registers the current wu 3HP skill pair.");
        var guanwei = registry.GetSkill(Guanwei).Program!.Triggers.Single();
        Require(guanwei.Window == SkillProgramTriggerWindow.PlayEnding && guanwei.Optional &&
            guanwei.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving &&
            guanwei.UsageScope == SkillUsageScope.Turn && guanwei.UsageLimit == 1 &&
            guanwei.Condition.Kind == SkillProgramTriggerConditionKind.TurnOwnerUsedSameSuitCards &&
            guanwei.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.SelectAndMoveOwnedCard,
                SkillProgramEffectOp.Draw, SkillProgramEffectOp.InsertPhase]) &&
            guanwei.Effects[1].Amount == 1 &&
            guanwei.Effects[1].Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
            guanwei.Effects[1].Destination == SkillProgramCardDestination.DiscardPile &&
            guanwei.Effects[2].Target == SkillProgramEffectTarget.SelectedTarget && guanwei.Effects[2].Amount == 2 &&
            guanwei.Effects[3].Target == SkillProgramEffectTarget.SelectedTarget &&
            guanwei.Effects[3].Phase == TurnPhase.Play,
            "Guanwei observes every play end, charges one discardable card and grants two draws plus an inserted play phase.");
        var gongqing = registry.GetSkill(Gongqing).Program!;
        Require(gongqing.Triggers.Count == 0 && gongqing.DamageModifiers.Count == 2,
            "Gongqing is a locked passive expressed through two damage modifiers.");
        var cap = gongqing.DamageModifiers.Single(m => m.CapsToAmount);
        var boost = gongqing.DamageModifiers.Single(m => !m.CapsToAmount);
        Require(cap.Condition == SkillProgramDamageModifierCondition.DamageSourceAttackRangeBelowThree &&
            cap.SourceScope == SkillProgramDamageModifierSourceScope.DamageParticipant && cap.Amount == 1 &&
            boost.Condition == SkillProgramDamageModifierCondition.DamageSourceAttackRangeAboveThree &&
            boost.SourceScope == SkillProgramDamageModifierSourceScope.DamageParticipant && boost.Amount == 1,
            "Gongqing rewrites sub-three-range damage to one and boosts above-three-range damage by one.");
        Require(registry.GetSkill(Guanwei).ProgramPresentation!.Name == "观微" &&
            registry.GetSkill(Gongqing).ProgramPresentation!.Name == "公清",
            "The presentation carries the current OL wording.");
    }

    public static void GuanweiGrantsOwnExtraPlayPhase()
    {
        var (g, r) = Start("identity:pj-own");
        // One alcohol use plus one slash use share the heart suit and arm the
        // launch condition at the owner's own play end.
        var alcohol = HandCard(g, c => c.Kind == CardKind.Alcohol, "alcohol");
        Accept(g, new PlayCardCommand(0, alcohol.Id, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var slash = HandCard(g, c => c.Kind == CardKind.Slash, "slash");
        Accept(g, new PlayCardCommand(0, slash.Id, [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei &&
            c.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([0]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var handBeforeCost = g.CreateSnapshot(0).Players[0].Hand.Count;
        var cost = g.CreateSnapshot(0).Players[0].Hand.First();
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            c.Cards.Contains(cost.Id));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var phases = g.Events.Select(e => e.Payload).OfType<PhaseChangedEvent>()
            .Where(e => e.Phase == TurnPhase.Play && e.ActorSeat == 0).ToArray();
        Require(phases.Length == 2, "The owner enters a second play phase in the same turn. count=" + phases.Length);
        var started = g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Single();
        Require(started.Started && started.SkillId == Guanwei && started.OwnerSeat == 0,
            "The inserted phase schedules one started fact for the owner's skill.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count == handBeforeCost - 1 + 2,
            "One card was paid and two were drawn. hand=" + DescribeHand(g));
        Require(g.CardMovements.Any(m => m.CardId == cost.Id && m.To == CardLocation.DiscardPile &&
                m.Reason.Value.Contains(Guanwei, StringComparison.Ordinal)),
            "The cost discard carries the guanwei movement reason.");
        // The once-per-turn usage keeps the extra phase's end silent: no new
        // launch prompt arms inside the same turn, and the finished scheduling
        // fact appears as soon as the inserted phase completes.
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        for (var i = 0;
            g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Count() < 2;
            i++)
        {
            Require(i < 40, "The extra phase completes its scheduling fact. prompt=" + P(g)?.Prompt);
            if (P(g) is { } pending &&
                pending.PlayerSeat == 0 &&
                pending.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var scheduled = g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().ToArray();
        Require(scheduled.Length == 2 && scheduled[0].Started && !scheduled[1].Started,
            "The extra phase ends through its finished scheduling fact. count=" + scheduled.Length);
        Require(g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
                .Count(e => e.SkillId == Guanwei) == 1,
            "The turn usage ledger records exactly one guanwei launch.");
        Replay(g, r);
    }

    public static void GuanweiObservesAnotherPlayPhase()
    {
        var (g, r) = Start("identity:pj-other");
        Require(PanJunSeat(g) == 0, "Pan Jun sits as the human observer.");
        // Pan Jun sits out his own first turn; a peer's natural play end arms
        // the same-suit history and the human grants the phase back.
        WaitForForeignLaunch(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei &&
            c.Parameters.GetValueOrDefault("program-action") == "activate");
        var select = P(g) ?? throw new InvalidOperationException("The activation never asked for a target.");
        var peer = select.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")
            .Select(c => c.Targets.Single()).Distinct().Single();
        Require(peer != 0, "The granted phase targets the foreign turn owner. target=" + peer);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target");
        var handBeforeCost = g.CreateSnapshot(0).Players[0].Hand.Count;
        var cost = g.CreateSnapshot(0).Players[0].Hand.First();
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            c.Cards.Contains(cost.Id));
        // The granted phase ends through its finished scheduling fact while the
        // peer's own turn continues; later foreign launches are declined.
        for (var i = 0;
            g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Count() < 2;
            i++)
        {
            Require(i < 60, "The granted phase completes its scheduling fact. prompt=" + P(g)?.Prompt);
            if (P(g) is { } pending &&
                pending.PlayerSeat == 0 &&
                pending.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var scheduled = g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().ToArray();
        Require(scheduled.Length == 2 && scheduled[0].Started && !scheduled[1].Started &&
            scheduled.All(e => e.SkillId == Guanwei && e.OwnerSeat == 0),
            "The observer's program schedules and completes the inserted phase.");
        var playEnds = g.Events.Select(e => e.Payload).OfType<PhaseChangedEvent>()
            .Where(e => e.Phase == TurnPhase.Play && e.ActorSeat == peer).ToArray();
        Require(playEnds.Length >= 2,
            "The turn owner takes the granted extra play phase. entries=" + playEnds.Length);
        Require(g.CreateSnapshot(0).Players[0].Hand.Count == handBeforeCost - 1,
            "The observer pays the one card while the turn owner draws. hand=" + DescribeHand(g));
        Require(g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
                .Count(e => e.SkillId == Guanwei) == 1,
            "The observer's once-per-turn usage records exactly one launch.");
        Replay(g, r);
    }

    // Advances the game until the human observer's foreign launch prompt
    // appears; peers play their own turns through the engine AI with the
    // fixed per-mode seed.
    private static void WaitForForeignLaunch(GameEngine g)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard)
            {
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
                continue;
            }
            if (p.PlayerSeat == 0 && p.Kind == DecisionKind.DiscardCards)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),
                    p.PromptId, g.Revision));
                continue;
            }
            if (p.PlayerSeat == 0 &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
                return;
            throw new InvalidOperationException(
                "The fixture never reached the foreign launch: seat=" + p.PlayerSeat + " prompt=" + p.Prompt);
        }
        throw new InvalidOperationException(
            "The fixture never armed the foreign launch: uses=[" + DescribeUses(g) + "]");
    }

    private static string DescribeUses(GameEngine g) =>
        string.Join(",", g.Events.Select(e => e.Payload)
            .OfType<CurrentTurnCardUseKindsRecordedEvent>()
            .Select(e => $"seat{e.ActorSeat}:suit{e.HandSuitMask}"));

    // The shared Reach does not report why its goal failed; this variant does.
    private static void ReachPj(GameEngine g, Func<PendingDecision, bool> goal)
    {
        for (var i = 0; i < 200; i++)
        {
            if (P(g) is { } p && goal(p)) return;
            if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } waiting &&
                waiting.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var current = P(g);
        throw new InvalidOperationException(
            $"Diag: kind={current?.Kind} seat={current?.PlayerSeat} prompt={current?.Prompt} " +
            $"usage=[{string.Join(",", g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
                .Select(e => $"{e.SkillOwnerSeat}:{e.SkillId}:{e.UsageId}:{e.Scope}"))}] uses=[{DescribeUses(g)}]");
    }

    public static void GuanweiDeclinesMixedSuits()
    {
        var (g, r) = Start("identity:pj-mixed");
        var alcohol = HandCard(g, c => c.Kind == CardKind.Alcohol, "alcohol");
        Accept(g, new PlayCardCommand(0, alcohol.Id, [], g.Revision, P(g)!.PromptId));
        ReachPj(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var slash = HandCard(g, c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade, "spade slash");
        Accept(g, new PlayCardCommand(0, slash.Id, [1], g.Revision, P(g)!.PromptId));
        ReachPj(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Require(!g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Any(),
            "Heart alcohol plus a spade slash never launches the skill.");
        Require(!g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
            .Any(e => e.SkillId == Guanwei), "No usage is consumed without the launch.");
        Replay(g, r);
    }

    public static void GuanweiSkipsBelowTwoSameSuitUses()
    {
        var (g, r) = Start("identity:pj-empty");
        var alcohol = HandCard(g, c => c.Kind == CardKind.Alcohol, "alcohol");
        Accept(g, new PlayCardCommand(0, alcohol.Id, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Require(!g.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Any(),
            "A single same-suit use never launches the skill.");
        Require(!g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
            .Any(e => e.SkillId == Guanwei), "No usage is consumed without the launch.");
        Replay(g, r);
    }

    public static void GongqingCapsBelowThreeRange()
    {
        var (g, r) = Start("identity:pj-cap");
        var panJunSeat = PanJunSeat(g);
        SlashAlcohol(g, panJunSeat);
        var damage = g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Single(d => d.TargetSeat == panJunSeat);
        Require(damage.SourceSeat == 0 && damage.Amount == 1,
            "A range-one alcohol slash deals its capped single damage. amount=" + damage.Amount);
        var cap = g.Events.Select(e => e.Payload).OfType<ProgramDamageCappedEvent>().Single();
        Require(cap.SourceSeat == 0 && cap.TargetSeat == panJunSeat &&
            cap.BaseAmount == 2 && cap.CappedAmount == 1 &&
            cap.Source.SkillId == Gongqing,
            "The gongqing cap rewrites two damage to one with public evidence.");
        Replay(g, r);
    }

    public static void GongqingKeepsExactThreeRange()
    {
        var (g, r) = Start("identity:pj-equal");
        var panJunSeat = PanJunSeat(g);
        EquipWeapon(g, CardKind.QinglongCrescentBlade);
        SlashAlcohol(g, panJunSeat);
        var damage = g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Single(d => d.TargetSeat == panJunSeat);
        Require(damage.SourceSeat == 0 && damage.Amount == 2,
            "A range-three source leaves the alcohol slash at two. amount=" + damage.Amount);
        Require(!g.Events.Select(e => e.Payload).OfType<ProgramDamageCappedEvent>().Any() &&
            !g.Events.Select(e => e.Payload).OfType<ProgramCardDamageModifiedEvent>().Any(),
            "The exact-three boundary neither caps nor boosts.");
        Replay(g, r);
    }

    public static void GongqingBoostsAboveThreeRange()
    {
        var (g, r) = Start("identity:pj-boost");
        var panJunSeat = PanJunSeat(g);
        EquipWeapon(g, CardKind.FangtianHalberd);
        SlashAlcohol(g, panJunSeat);
        var damage = g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Single(d => d.TargetSeat == panJunSeat);
        Require(damage.SourceSeat == 0 && damage.Amount == 3,
            "A range-four source boosts the alcohol slash to three. amount=" + damage.Amount);
        var boost = g.Events.Select(e => e.Payload).OfType<ProgramCardDamageModifiedEvent>().Single();
        Require(boost.BaseAmount == 2 && boost.ModifiedAmount == 3 && boost.Source.SkillId == Gongqing,
            "The gongqing boost adds one damage with public evidence.");
        Require(!g.Events.Select(e => e.Payload).OfType<ProgramDamageCappedEvent>().Any(),
            "The boost branch never caps.");
        Replay(g, r);
    }

    // Fires the dealt alcohol and one heart slash at the seated Pan Jun.
    private static void SlashAlcohol(GameEngine g, int panJunSeat)
    {
        var alcohol = HandCard(g, c => c.Kind == CardKind.Alcohol, "alcohol");
        Accept(g, new PlayCardCommand(0, alcohol.Id, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var slash = HandCard(g, c => c.Kind == CardKind.Slash, "slash");
        Accept(g, new PlayCardCommand(0, slash.Id, [panJunSeat], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    }

    private static void EquipWeapon(GameEngine g, CardKind kind)
    {
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var weapon = HandCard(g, c => c.Kind == kind, kind.ToString());
        Accept(g, new PlayCardCommand(0, weapon.Id, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    }

    private static CardSnapshot HandCard(GameEngine g, Func<CardSnapshot, bool> predicate, string what)
    {
        var hand = g.CreateSnapshot(0).Players[0].Hand;
        return hand.FirstOrDefault(predicate) ?? throw new InvalidOperationException(
            $"The dealt hand has no {what}: " + DescribeHand(g));
    }

    private static string DescribeHand(GameEngine g) =>
        string.Join(",", g.CreateSnapshot(0).Players[0].Hand.Select(c => $"{c.Kind}/{c.Suit}"));

    private static int PanJunSeat(GameEngine g) =>
        g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "ol:pan-jun").Seat;

    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });

    private static void Replay(GameEngine g, ContentRegistry r) => Require(
        State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four prepared viewer states, owning frames, events, physical entities and commands restore identically.");

    private static (GameEngine, ContentRegistry) Start(string modeId)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new PanJunFixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = Seeds.GetValueOrDefault(modeId, 7), PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 24
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, modeId is "identity:pj-own" or "identity:pj-mixed"
            or "identity:pj-empty" or "identity:pj-other" ? "ol:pan-jun" : Human, g.Revision, P(g)!.PromptId));
        for (var i = 0; i < 200; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return (g, r);
            if (p.PlayerSeat == 0 &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Guanwei) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the first play phase: " + P(g)?.Prompt);
    }

    private sealed class PanJunFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pj", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder b)
        {
            b.AddGeneral(new(Human, "潘濬夹具主", "fixture", "standard:none", "wei", 6, null));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:pj-plain-{i}", "fixture-plain-" + i, "supporter", "standard:none", "wei", 6, null));
            // CompleteSetup shuffles the pile, so every deck is defined by heart
            // kind counts only; the fixed per-mode seeds select hands that hold
            // the combinations each scenario plays.
            b.AddDeck(new("fixture:pj-deck-own", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("standard:alcohol", 32), ("standard:slash", 32)) });
            b.AddDeck(new("fixture:pj-deck-other", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("standard:offensive_horse", 24), ("standard:defensive_horse", 24), ("standard:slash", 16)) });
            b.AddDeck(new("fixture:pj-deck-mixed", "fixed", 4, 1, []) { PhysicalCards =
                Deck(("standard:alcohol", 32)).Concat(Enumerable.Range(0, 32).Select(
                    _ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5))).ToArray() });
            b.AddDeck(new("fixture:pj-deck-empty", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("standard:alcohol", 64)) });
            b.AddDeck(new("fixture:pj-deck-cap", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("standard:alcohol", 32), ("standard:slash", 32)) });
            b.AddDeck(new("fixture:pj-deck-equal", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("classic:qinglong-crescent-blade", 24), ("standard:alcohol", 20), ("standard:slash", 20)) });
            b.AddDeck(new("fixture:pj-deck-boost", "fixed", 4, 1, []) { PhysicalCards = Deck(
                ("classic:fangtian-halberd", 24), ("standard:alcohol", 20), ("standard:slash", 20)) });
            foreach (var (deck, mode, human) in new[]
                     {
                         ("fixture:pj-deck-own", "identity:pj-own", "ol:pan-jun"),
                         ("fixture:pj-deck-other", "identity:pj-other", "ol:pan-jun"),
                         ("fixture:pj-deck-mixed", "identity:pj-mixed", "ol:pan-jun"),
                         ("fixture:pj-deck-empty", "identity:pj-empty", "ol:pan-jun"),
                         ("fixture:pj-deck-cap", "identity:pj-cap", Human),
                         ("fixture:pj-deck-equal", "identity:pj-equal", Human),
                         ("fixture:pj-deck-boost", "identity:pj-boost", Human)
                     })
                b.AddMode(new(mode, mode, 4, 4,
                    new Dictionary<string, int>
                        { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                    deck, GeneralCandidateCount: 4,
                    GeneralPoolIds: human == "ol:pan-jun"
                        ? new[] { human, "fixture:pj-plain-1", "fixture:pj-plain-2", "fixture:pj-plain-3" }
                        : new[] { human, "ol:pan-jun", "fixture:pj-plain-1", "fixture:pj-plain-2" }));
        }

        // CompleteSetup shuffles the pile, so only the heart kind counts above
        // are guaranteed; the per-mode seed picks a workable dealt hand.
        private static ContentDeckPhysicalCard[] Deck(params (string Id, int Count)[] counts) =>
            counts.SelectMany(count => Enumerable.Range(0, count.Count).Select(
                _ => new ContentDeckPhysicalCard(count.Id, Suit.Heart, 5))).ToArray();
    }
}
