using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Lü Kai: docs/content/sources/ol-lv-kai-2026-10-06.json.
internal static class OrdinaryLvKaiChecks
{
    private const string Tunan = "ol:tunan";
    private const string Bijing = "ol:bijing";
    private const string Driver = "fixture:lk-driver";
    private const string Steal = "fixture:lk-steal-skill";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:lv-kai"];
        Require(general.Name == "吕凯" && general.FactionId == "shu" && general.BaseHp == 3 &&
            general.SkillIds.SequenceEqual([Tunan, Bijing]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Lü Kai general registers the current shu 3HP skill pair.");
        var tunan = registry.GetSkill(Tunan).Program!.Triggers.Single();
        Require(tunan.Window == SkillProgramTriggerWindow.PlayPhaseStarting && tunan.Optional &&
            tunan.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.RevealTopCards,
                SkillProgramEffectOp.TunanUseRevealedCard]) &&
            tunan.Effects[1].Amount == 1 && tunan.Effects[1].ResultBind == "tunan-card" &&
            tunan.Effects[2].SourceBind == "tunan-card",
            "Tunan reveals one top card and hands the use choice to the selected counterpart.");
        var bijing = registry.GetSkill(Bijing).Program!.Triggers;
        Require(bijing.Count == 3, "Bijing registers mark, recast and punish triggers.");
        var mark = bijing.Single(t => t.Id == "bijing-mark");
        Require(mark.Window == SkillProgramTriggerWindow.TurnEnding && mark.Optional &&
            mark.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectOwnedCards, SkillProgramEffectOp.BijingMarkHandCards]) &&
            mark.Effects[0].MinimumCards == 0 && mark.Effects[0].MaximumCards == 2 &&
            mark.Effects[0].Zones.SequenceEqual([CardZoneKind.Hand]),
            "The mark trigger selects up to two hand cards at the owner's turn ending.");
        var recast = bijing.Single(t => t.Id == "bijing-recast");
        Require(recast.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && !recast.Optional &&
            recast.Effects.Single().Op == SkillProgramEffectOp.BijingRecastMarkedCards,
            "The recast trigger runs at the owner's preparation phase.");
        var punish = bijing.Single(t => t.Id == "bijing-punish");
        Require(punish.Window == SkillProgramTriggerWindow.DiscardPhaseStarting &&
            punish.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && !punish.Optional &&
            punish.Effects.Single().Op == SkillProgramEffectOp.BijingPunishDiscardPhase,
            "The punish trigger observes other characters' discard phases.");
        Require(registry.GetSkill(Tunan).ProgramPresentation!.Name == "图南" &&
            registry.GetSkill(Bijing).ProgramPresentation!.Name == "闭境",
            "The presentation carries the current OL wording.");
    }

    public static void TunanConvertsDodgeIntoSlash()
    {
        var (g, r) = Start("identity:lk-dodge", waitTunan: true);
        ActivateTunan(g, target: 1);
        // Dodge is not proactively usable, so the counterpart only sees the
        // Slash conversion; its nearest victim is seat 0.
        var dodgeId = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single().Cards.Single().Id;
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage"));
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        SettleToNextOwnPlayPhase(g);
        var damage = g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Single(d => d.SourceSeat == 1 && d.TargetSeat == 0);
        Require(damage.Amount == 1, "The converted Slash deals one damage to the owner.");
        Require(g.CardMovements.Any(m => m.CardId == dodgeId && m.To == CardLocation.DiscardPile),
            "The converted dodge reaches the discard pile as the played card.");
        Require(!g.Events.Select(e => e.Payload).OfType<ProgramBijingMarkedEvent>().Any(),
            "No Bijing marks exist without the mark trigger.");
        Replay(g, r);
    }

    public static void TunanUsesRevealedSlashWithoutDistanceLimit()
    {
        var (g, r) = Start("identity:lk-slash", waitTunan: true);
        ActivateTunan(g, target: 1);
        var slashId = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single().Cards.Single().Id;
        SettleToNextOwnPlayPhase(g);
        var used = g.Events.Select(e => e.Payload).OfType<CardUsedEvent>()
            .Single(u => u.SourceSeat == 1 && u.TargetSeat == 0 && u.CardKind == CardKind.Slash);
        Require(used.CardId == slashId,
            "The real use keeps the physical slash identity. used=" + used.CardId + " revealed=" + slashId);
        Require(g.CardMovements.Any(m => m.CardId == slashId && m.To == CardLocation.DiscardPile),
            "The used slash reaches the discard pile.");
        var damage = g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Single(d => d.SourceSeat == 1 && d.TargetSeat == 0);
        Require(damage.Amount == 1, "The unlimited-distance real slash deals one damage.");
        Replay(g, r);
    }

    public static void TunanPeachUseHealsWoundedCounterpart()
    {
        var (g, r) = Start("identity:lk-peach", waitTunan: false);
        AcceptDriver(g, "hurt-target", [1]);
        var maxHp = g.CreateSnapshot(0).Players[1].MaxHp;
        var wounded = g.CreateSnapshot(0).Players[1].Hp;
        Require(wounded == maxHp - 1, "The driver wounds the counterpart. hp=" + wounded + "/" + maxHp);
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } wait && wait.Kind == DecisionKind.PlayCard && wait.PlayerSeat == 0)
            {
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, wait.PromptId));
                break;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        SettleToNextOwnPlayPhase(g, stopAtTunan: true);
        ActivateTunan(g, target: 1);
        var peachId = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single().Cards.Single().Id;
        SettleToNextOwnPlayPhase(g);
        Require(g.CreateSnapshot(0).Players[1].Hp == maxHp,
            "The counterpart recovers to full. hp=" + g.CreateSnapshot(0).Players[1].Hp + "/" + maxHp);
        Require(g.CardMovements.Any(m => m.CardId == peachId && m.To == CardLocation.DiscardPile),
            "The used peach reaches the discard pile.");
        Require(!g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
            .Any(d => d.SourceSeat == 1), "The peach branch deals no damage.");
        Replay(g, r);
    }

    public static void BijingMarksAndRecasts()
    {
        var (g, r) = Start("identity:lk-recast", waitTunan: false);
        var kept = EndTurnKeepingOneAndMarking(g, markCount: 2, keepHp: 2);
        Require(g.Events.Select(e => e.Payload).OfType<ProgramBijingMarkedEvent>().Single().CardIds
            .Order().SequenceEqual(kept.Order()), "The mark trigger registers exactly the kept cards.");
        var handBefore = CardsAt(g, CardLocation.Hand(0)).Count;
        SettleToNextOwnPlayPhase(g);
        var recast = g.Events.Select(e => e.Payload).OfType<ProgramBijingRecastEvent>().ToList();
        Require(recast.Count == 1 && recast[0].DiscardedCardIds.Count == 2 && recast[0].DrawnCount == 2,
            "The preparation recast replaces the two marked cards. " + Describe(recast));
        var handAfter = CardsAt(g, CardLocation.Hand(0)).Count;
        Require(handAfter == handBefore, "The recast keeps the hand size.");
        Replay(g, r);
    }

    public static void BijingPunishesLostMarkedCard()
    {
        var (g, r) = Start("identity:lk-punish", waitTunan: false);
        var kept = EndTurnKeepingOneAndMarking(g, markCount: 1, keepHp: 1);
        var marked = kept.Single();
        for (var i = 0; i < 300; i++)
        {
            if (g.Events.Select(e => e.Payload).OfType<ProgramBijingPunishEvent>().Any()) break;
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var punish = g.Events.Select(e => e.Payload).OfType<ProgramBijingPunishEvent>().SingleOrDefault() ??
            throw new InvalidOperationException("The Bijing punish never fired. last=" + P(g)?.Prompt);
        Require(punish.TurnOwnerSeat != 0 && punish.LostCardIds.SequenceEqual([marked]),
            "The punish binds the turn owner to the lost marked card. " + DescribePunish(punish));
        Require(punish.DiscardedCardIds.Count == 2,
            "The turn owner discards two cards. " + DescribePunish(punish));
        Require(g.CardMovements.Count(m => punish.DiscardedCardIds.Contains(m.CardId) &&
                m.To == CardLocation.DiscardPile &&
                m.Reason.Value.Contains("bijing-punish", StringComparison.Ordinal)) == 2,
            "Both punished discards carry the Bijing reason.");
        Replay(g, r);
    }

    // Ends the owner's first turn: hurts self down to a one-card hand limit,
    // keeps the lowest-id card, and marks it through the Bijing mark prompt.
    private static int[] EndTurnKeepingOneAndMarking(GameEngine g, int markCount, int keepHp)
    {
        while (g.CreateSnapshot(0).Players[0].Hp > keepHp)
            AcceptDriver(g, "hurt-self", []);
        var ownerHp = g.CreateSnapshot(0).Players[0].Hp;
        Require(ownerHp == keepHp,
            "Self-hurts reach the keep HP. hp=" + ownerHp + "/" + g.CreateSnapshot(0).Players[0].MaxHp);
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } wait && wait.Kind == DecisionKind.PlayCard && wait.PlayerSeat == 0)
            {
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, wait.PromptId));
                break;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var pickedIds = new List<int>();
        var kept = new List<int>();
        var picked = 0;
        for (var done = false; !done;)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                if (kept.Count == 0)
                {
                    kept.AddRange(p.ValidCardIds.OrderByDescending(id => id)
                        .Take(p.ValidCardIds.Count - p.RequiredCardCount));
                    var ids = p.ValidCardIds.Where(id => !kept.Contains(id)).ToArray();
                    Accept(g, new DiscardCardsCommand(0, ids, p.PromptId, g.Revision));
                    continue;
                }
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Bijing) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Tunan) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            {
                done = true;
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
            {
                if (picked >= markCount)
                {
                    Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
                    done = true;
                    continue;
                }
                var remaining = kept.Where(id => !pickedIds.Contains(id) &&
                    CardsAt(g, CardLocation.Hand(0)).Any(card => card.Id == id)).ToArray();
                if (remaining.Length == 0)
                {
                    Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
                    done = true;
                    continue;
                }
                pickedIds.Add(remaining.First());
                picked++;
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" &&
                    c.Cards.Contains(remaining.First()));
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        return pickedIds.ToArray();
    }

    private static void AcceptDriver(GameEngine g, string activation, int[] targets)
    {
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } p && p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            {
                Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, p.PromptId));
                return;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The driver never reached the play prompt: " + P(g)?.Prompt);
    }

    private static void ActivateTunan(GameEngine g, int target)
    {
        Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Tunan &&
            c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([target]));
    }

    private static void SettleToNextOwnPlayPhase(GameEngine g, bool stopAtTunan = false)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Tunan) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                if (stopAtTunan) return;
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Bijing) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "select-owned-cards" or "finish-owned-cards"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
                continue;
            }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                return;
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the owner's play phase: " + P(g)?.Prompt);
    }

    private static string Describe(IReadOnlyList<ProgramBijingRecastEvent> events) =>
        string.Join(";", events.Select(e => "owner" + e.OwnerSeat + ":" + e.DiscardedCardIds.Count + "->" + e.DrawnCount));

    private static string DescribePunish(ProgramBijingPunishEvent e) =>
        "turn" + e.TurnOwnerSeat + " lost=[" + string.Join(",", e.LostCardIds) + "] discarded=[" +
        string.Join(",", e.DiscardedCardIds) + "]";

    private static IReadOnlyList<Card> CardsAt(GameEngine g, CardLocation location) =>
        ((CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(g)!).CardsAt(location);

    private static (GameEngine, ContentRegistry) Start(string modeId, bool waitTunan)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new LvKaiFixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 24
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:lk", g.Revision, P(g)!.PromptId));
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return (g, r);
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Tunan) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                if (waitTunan) return (g, r);
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the first play phase: " + P(g)?.Prompt);
    }

    private sealed class LvKaiFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-lk", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder b)
        {
            typeof(StandardContentPackage).Assembly
                .GetType("CardGame.Content.Standard.OrdinaryLvKaiContent")!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [b]);
            b.AddSkill(new(Driver, "图南夹具", "fixture")
            {
                Program = SkillProgramCatalog.Load(
                    "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                    ",\"skills\":[{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[" +
                    "{\"id\":\"hurt-self\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}," +
                    "{\"id\":\"hurt-target\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"effects\":[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]}]}]}",
                    "{\"schemaVersion\":3,\"skills\":{\"" + Driver + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[Driver]
            });
            // The steal peer deterministically strips the owner's single marked
            // hand card at its preparation phase, arming the Bijing punish.
            b.AddSkill(new(Steal, "闭境夹具", "fixture")
            {
                Program = SkillProgramCatalog.Load(
                    "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                    ",\"skills\":[{\"id\":\"" + Steal + "\",\"revision\":1,\"triggers\":" +
                    "[{\"id\":\"lk-steal\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false," +
                    "\"effects\":[{\"op\":\"takeRandomCardFromEveryOtherCharacter\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]}]}",
                    "{\"schemaVersion\":3,\"skills\":{\"" + Steal + "\":{\"name\":\"fixture-steal\",\"description\":\"fixture\"}}}").Programs[Steal]
            });
            b.AddGeneral(new("fixture:lk", "吕凯", "ol-lv-kai", Tunan, "shu", 3,
                new[] { Bijing, Driver }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:lk-{i}", "fixture-peer-" + i, "supporter", "standard:none", "wei", 6, null));
            for (var i = 1; i <= 3; i++)
                b.AddGeneral(new($"fixture:lk-stealer-{i}", "fixture-stealer-" + i, "supporter", Steal, "wei", 6, null));
            b.AddDeck(new("fixture:lk-deck-dodge", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:dodge")
            });
            b.AddDeck(new("fixture:lk-deck-slash", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:slash")
            });
            b.AddDeck(new("fixture:lk-deck-peach", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:peach")
            });
            b.AddDeck(new("fixture:lk-deck-recast", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:peach")
            });
            b.AddDeck(new("fixture:lk-deck-punish", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:peach")
            });
            string[] pool = ["fixture:lk", "fixture:lk-1", "fixture:lk-2", "fixture:lk-3"];
            // Every AI slot holds the steal skill no matter how the seeded
            // shuffle assigns generals, so the marked card is lost on the
            // first peer turn.
            string[] punishPool = ["fixture:lk", "fixture:lk-stealer-1", "fixture:lk-stealer-2", "fixture:lk-stealer-3"];
            foreach (var (deck, id, modePool) in new[]
                     {
                         ("fixture:lk-deck-dodge", "identity:lk-dodge", pool),
                         ("fixture:lk-deck-slash", "identity:lk-slash", pool),
                         ("fixture:lk-deck-peach", "identity:lk-peach", pool),
                         ("fixture:lk-deck-recast", "identity:lk-recast", pool),
                         ("fixture:lk-deck-punish", "identity:lk-punish", punishPool)
                     })
                b.AddMode(new(id, id, 4, 4,
                    new Dictionary<string, int>
                        { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                    deck, GeneralCandidateCount: 4, GeneralPoolIds: modePool));
        }

        private static ContentDeckPhysicalCard[] UniformDeck(string kind) =>
            Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(kind, Suit.Heart, 5)).ToArray();
    }
}
