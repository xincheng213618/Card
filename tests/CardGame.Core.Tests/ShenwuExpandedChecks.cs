using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class ShenwuNpcChecks
{
    public static void ExpandedRecipesAndDyingAwakening()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShenwuActivityPackage(), new StandardShenwu2020ActivityPackage());
        Require(registry.Modes["activity:shenwu-2017"].ShenwuActivity!.LeaderPoolIds.Count == 4 &&
            Enumerable.Range(1, 5).All(tier => registry.Modes[$"activity:shenwu-2020-tier-{tier}"].ShenwuActivity is
                { Edition: 2020, LeaderPoolIds.Count: 8 } activity && activity.EnemyTier == tier), "The old recipe stays isolated and each 2020 enemy tier has eight real leaders.");
        var (g, content) = Start([], ["shenwu:kunshou"], expanded: true);
        Use(g, "kill", 1); Play(g);
        Require(V(g, 1).IsAlive && V(g, 1).Hp == 4 && V(g, 1).HandCount == 5 &&
            V(g, 1).Skills!.Any(skill => skill.ContentId == "classic:benghuai") &&
            E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == "shenwu:kunshou") == 1,
            "Kunshou rescues before death, clamps the recovery to maximum HP, refills only to five and permanently grants Benghuai.");
        Cold(g, content);
        Use(g, "kill", 1); Play(g);
        Require(!V(g, 1).IsAlive && E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == "shenwu:kunshou") == 1,
            "A second dying cannot awaken again.");
        Cold(g, content);
    }

    public static void ExpandedFireNineHeadsAndFreeze()
    {
        {
            var (g, content) = Start(["shenwu:zhaohuo", "shenwu:jiushou"], ["shenwu:zhaohuo"], expanded: true);
            Require(V(g, 0).HandCount == 9 && V(g, 0).HandLimit == 9 &&
                !g.CardMovements.Any(move => move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.Draw),
                "Nine heads skips actual normal draw and refills at the Play boundary.");
            var slash = g.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.SequenceEqual([1]));
            var revision = g.Revision;
            Require(g.PredictPublicActionDamage(0, slash, 1).DamageAmount == 0 && g.Revision == revision,
                "Visible fire conversion and Zhaohuo immunity produce a pure zero-damage public estimate.");
            Use(g, "damage", 1); Play(g);
            Require(V(g, 1).Hp == 4 && E<ProgramDamagePreventedEvent>(g).Any(e => e.SkillId == "shenwu:zhaohuo"),
                "Zhaohuo turns native skill damage into fire and prevents fire on its holder.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:fengdong"], ["shenwu:kunshou"], expanded: true);
            Require(V(g, 1).Skills!.Any(skill => skill.ContentId == "shenwu:kunshou"), "Locked awakening remains enabled under Fengdong.");
            Cold(g, content);
        }
    }

    public static void ExpandedPoisonPaymentsAndYanyuJudgments()
    {
        {
            var (g, content) = Start([], ["shenwu:duqu"], expanded: true);
            Use(g, "damage", 1); Play(g); Use(g, "one-damage", 1); Play(g);
            int Poison() => V(g, 0).Markers!.Where(m => m.Kind == PlayerMarkerKind.ShenwuPoison).Sum(m => m.Count);
            Require(Poison() == 2 && V(g, 1).Hp == 1, "Each actual damage attempt adds one persistent poison, independently of damage points.");
            Cold(g, content);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "shenwu:poison");
            Cold(g, content);
            Answer(g, c => c.Parameters.GetValueOrDefault("poison-action") == "discard");
            var before = g.CardMovements.Count;
            Answer(g, c => c.Parameters.GetValueOrDefault("poison-action") == "card");
            Cold(g, content);
            Answer(g, c => c.Parameters.GetValueOrDefault("poison-action") == "card");
            Until(g, () => Poison() == 1);
            Require(V(g, 0).Hp == 4 && g.CardMovements.Skip(before).Count(m => m.Reason.Value == "shenwu.poison.discard" && m.To == CardLocation.DiscardPile) == 2,
                "Poison pays the frozen two-card cost once, waits for movement children and removes exactly one mark.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:yanyu"], [], expanded: true);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<JudgmentResolvedEvent>(g).Count(e => e.Reason == "shenwu.yanyu") >= 1);
            Cold(g, content);
            Until(g, () => E<JudgmentResolvedEvent>(g).Count(e => e.Reason == "shenwu.yanyu") >= 3);
            Until(g, () => V(g, 1).Hp == 1);
            Require(E<JudgmentResolvedEvent>(g).Count(e => e.Reason == "shenwu.yanyu") == 3 &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Nature == DamageNature.Fire) == 3,
                "Yanyu uses three separate finalized red judgments and three native fire damage attempts, then stops.");
            Cold(g, content);
        }
    }

    public static void ExpandedRandomReturnsXunyouAndBingxian()
    {
        {
            var (g, content) = Start(["shenwu:honglian", "shenwu:xushi"], [], expanded: true);
            Require(E<ShenwuExpandedRandomEvent>(g).Single(e => e.SkillId == "shenwu:honglian") is { } roll &&
                roll.ObtainedCount == roll.Roll && roll.Damage.Count == 3 - roll.Roll &&
                roll.Damage is IList<ShenwuExpandedDamage> { IsReadOnly: true },
                "Honglian freezes one roll, real red deck gains and complementary random damage targets.");
            Cold(g, content);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => V(g, 0).IsFaceDown);
            Cold(g, content);
            Until(g, () => E<ShenwuExpandedRandomEvent>(g).Any(e => e.SkillId == "shenwu:xushi"));
            var burst = E<ShenwuExpandedRandomEvent>(g).Single(e => e.SkillId == "shenwu:xushi");
            Require(burst.Damage.All(damage => damage.Amount is 1 or 2) && burst.Damage.Select(damage => damage.Seat).Distinct().Count() == burst.Damage.Count,
                "Face-up Xushi freezes an independent one-or-two damage for each other living character.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:xunyou", "shenwu:bingxian"], [], "standard:qinggang_sword", expanded: true);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => g.CardMovements.Any(m => m.Reason.Value == "shenwu.xunyou.obtain"));
            Cold(g, content);
            Until(g, () => E<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.QinggangSword));
            Require(V(g, 0).Equipment.Single().Kind == CardKind.QinggangSword &&
                g.CardMovements.Any(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse),
                "Xunyou actually uses the obtained equipment entity after acquisition children, instead of directly placing or duplicating it.");
            Cold(g, content);
            Until(g, () => E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == "shenwu:bingxian"));
            Cold(g, content);
            Until(g, () => E<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash && e.CardId == 0));
            Cold(g, content);
        }
    }

    public static void ExpandedBlackJudgmentAndEchouActualUse()
    {
        {
            var (g, content) = Start(["shenwu:yanyu"], [], expanded: true, suit: Suit.Spade);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<TurnEndedEvent>(g).Any(turn => turn.ActorSeat == 1));
            Require(E<JudgmentResolvedEvent>(g).Count(e => e.Reason == "shenwu.yanyu") == 1 &&
                !E<DamageAppliedEvent>(g).Any() && V(g, 1).Hp == 4,
                "A finalized black Yanyu judgment stops immediately without damage or extra judgment requests.");
            Cold(g, content);
        }
        {
            var (g, content) = Start([], ["shenwu:echou"], "standard:peach", expanded: true);
            Use(g, "damage", 0); Play(g); Use(g, "one-damage", 0); Play(g); Use(g, "one-damage", 0);
            Until(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach");
            Until(g, () => E<PlayerMarkerChangedEvent>(g).Any(e => e.Marker == PlayerMarkerKind.ShenwuPoison));
            Require(E<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Peach) &&
                E<PlayerMarkerChangedEvent>(g).All(e => e.Marker != PlayerMarkerKind.ShenwuPoison || e.SkillOwnerSeat != 0 && e.PlayerSeat == 0),
                "Echou poisons the actor of a real foreign Peach Use, including native dying rescue.");
            Cold(g, content);
        }
    }

    public static void ExpandedSipuAndRealTierInitialization()
    {
        {
            var (g, content) = Start(["shenwu:sipu"], [], "standard:nullification", expanded: true);
            void Dismantle()
            {
                var action = g.GetHumanLegalActions().First(a => a.ConversionSource is { SkillId: Driver, BindingId: "fixed-dismantlement" } && a.TargetSeats.SequenceEqual([1]));
                Accept(g, new PlayCardCommand(0, action.CardId!.Value, [1], g.Revision, P(g)!.PromptId, CardKind.Dismantlement) { ConversionSource = action.ConversionSource });
            }
            for (var use = 0; use < 2; use++)
            {
                Dismantle();
                Until(g, () => P(g) is { Kind: DecisionKind.SelectTargetCard });
                Answer(g, _ => true); Play(g);
            }
            Require(!E<NullificationRespondedEvent>(g).Any(), "Other players cannot play Nullification against Sipu's first two actual Play uses.");
            Dismantle();
            Until(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: not 0 });
            Require(P(g)!.Choices.Any(choice => choice.Cards.Count > 0), "The third actual use reopens real card responses.");
            Cold(g, content);
        }
        var registry = CardGame.Content.Standard.ComposedSkillContentRegistry.CreateShowcase(includeShenwuActivities: true);
        foreach (var tier in new[] { 1, 5 })
        {
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 6, HumanSeat = 0,
                HumanRole = null, HumanTeamId = "team:allies", ModeId = $"activity:shenwu-2020-tier-{tier}",
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(g, new StartGameCommand());
            for (var step = 0; step < 80 && !g.CreateSnapshot(0).Players.All(player => player.HandCount > 0); step++)
                Accept(g, new AdvanceOneStepCommand(g.Revision));
            var view = g.CreateSnapshot(0);
            var leader = view.Players[4];
            Require(view.Players.Count == 6 && leader.MaxHp == registry.Generals[leader.GeneralId].BaseHp + (tier == 5 ? 2 : 0) &&
                view.Players[3].MaxHp == 4 + (tier == 5 ? 2 : 0) &&
                view.Players.Where(player => player.TeamId == "team:beasts").All(player =>
                    player.Skills!.Any(skill => skill.ContentId == "shenwu:kunshou") == (tier == 5)),
                "Real selected tiers initialize leader and independent minion health and native Kunshou grants from the 2020 table.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(g.CreateSnapshot(0)) == SnapshotJson.Serialize(cold.CreateSnapshot(0)), "The real version and enemy tier survive cold replay.");
        }
    }

    public static void ExpandedBurstWaitsForEachDyingChild()
    {
        var (g, content) = Start(["shenwu:xushi"], ["shenwu:kunshou"], expanded: true);
        foreach (var seat in new[] { 1, 2, 3 }) { Use(g, "damage", seat); Play(g); Use(g, "one-damage", seat); Play(g); }
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => V(g, 0).IsFaceDown);
        Until(g, () => E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == "shenwu:kunshou"));
        Cold(g, content);
        Until(g, () => E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == "shenwu:kunshou") == 3);
        Play(g);
        Require(Enumerable.Range(1, 3).All(seat => V(g, seat).IsAlive && V(g, seat).Hp == 4) &&
            E<ShenwuExpandedRandomEvent>(g).Count(e => e.SkillId == "shenwu:xushi") == 1,
            "One frozen Xushi burst waits for each independent Kunshou recovery before advancing its recipient cursor.");
        Cold(g, content);
    }

    public static void ExpandedContinuousPoliciesAndPoisonIdentity()
    {
        foreach (var frozen in new[] { false, true })
        {
            var (g, content) = Start(frozen ? ["shenwu:fengdong"] : [], [NonLockedReward, "shenwu:zhue"], expanded: true);
            var count = V(g, 1).HandCount;
            Use(g, "one-damage", 1); Play(g);
            Require(V(g, 1).HandCount == count + (frozen ? 1 : 2),
                "Fengdong suppresses a real non-locked damage reward while locked Zhue remains live in the same warmed window.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:duqu"], [], "standard:peach", expanded: true);
            Require(V(g, 0).Hand.All(card => card.Kind == CardKind.Peach && card.IdentityKind == CardKind.Slash) &&
                g.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Peach),
                "Duqu forces actual Peach hand identities to Slash, rather than exposing an optional conversion.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:honglian"], [], expanded: true);
            var hand = V(g, 0).HandCount;
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<TurnEndedEvent>(g).Any(turn => turn.ActorSeat == 0));
            Require(V(g, 0).HandCount == hand && !g.CardMovements.Any(move => move.From == CardLocation.Hand(0) && move.Reason == CardMoveReasons.HandLimitDiscard),
                "Honglian red cards are excluded from the actual mandatory discard count.");
            Cold(g, content);
        }
        {
            var (g, content) = Start(["shenwu:juyuan"], [], expanded: true);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<ShenwuTurnEndHealthEvent>(g).Any()); Play(g);
            Use(g, "one-damage", 0); Play(g);
            var multi = g.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 2);
            Require(multi is not null, "Juyuan compares against the previous actual own turn-end health and exposes a real additional Slash target.");
            Accept(g, new PlayCardCommand(0, multi!.CardId!.Value, multi.TargetSeats, g.Revision, P(g)!.PromptId, multi.PlayedCardKind, multi.TargetCardId)
            { ConversionSource = multi.ConversionSource });
            Play(g);
            Require(multi.TargetSeats.All(seat => V(g, seat).Hp == 3), "The actual extra-target Slash resolves both native recipients.");
            Cold(g, content);
        }
    }
}
