using System.Collections;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static GuanduCardChecks;

internal static class GuanduModeChecks
{
    public static void RealPackageFreezesRosterRotatesPrivateDraftAndDealsTierFiveMilitaryDeck()
    {
        var r = ComposedSkillContentRegistry.CreateShowcase(includeGuanduActivities: true);
        var mode = r.Modes["activity:guandu-2023"]; var a = mode.GuanduActivity!;
        Need(a.PlayerTier == 5 && a.Events.Count == 9 && a.GeneralPools.Values.All(pool => pool.Count == 19) &&
            a.Commanders["team:cao"] == "guandu:cao-cao" && a.Commanders["team:yuan"] == "guandu:yuan-shao" &&
            a.GeneralPools is IDictionary { IsReadOnly: true } && a.GeneralPools.Values.All(pool => pool is IList { IsReadOnly: true }) &&
            a.Events is IList { IsReadOnly: true } && a.SeatLayouts is IList { IsReadOnly: true } && a.SeatLayouts.All(layout => layout is IList { IsReadOnly: true }),
            "The actual package freezes both nineteen-general pools, commanders, nine events and every nested local seat layout.");
        const int human = 5;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = human, HumanRole = null,
            HumanTeamId = "team:cao", HumanGuanduRole = GuanduSeatRole.General, ModeId = mode.Id, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false }, r);
        Accept(g, new StartGameCommand()); Drive(g, () => Prompt(g)?.Kind == DecisionKind.SelectGeneral);
        var p = Prompt(g)!; var config = E<GuanduBattleConfiguredEvent>(g).Single();
        Need(p.PlayerSeat == human && p.IsPrivate && p.Choices.Count == 6 && p.Choices.SelectMany(c => c.ContentIds).All(id => a.GeneralPools["team:cao"].Contains(id) && !a.Commanders.Values.Contains(id)) &&
            config.CaoCommanderSeat != human && config.PlayerTeamId == "team:cao" &&
            Enumerable.Range(0, 8).Where(seat => seat != human).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "A rotated general-seat human receives only their six private allied candidates while both fixed commanders and foreign offers remain unavailable.");
        Cold(g, r);
        Accept(g, new SelectGeneralCommand(human, p.Choices.First().ContentIds.Single(), g.Revision, p.PromptId));
        Drive(g, () => E<SetupCompletedEvent>(g).Any());
        var view = g.CreateSnapshot(human);
        Need(view.Players[config.CaoCommanderSeat].GeneralId == a.Commanders["team:cao"] &&
            view.Players[config.YuanCommanderSeat].GeneralId == a.Commanders["team:yuan"] &&
            view.Players.All(player => player.MaxHp == r.Generals[player.GeneralId].BaseHp + 2 +
                (player.Seat == config.CaoCommanderSeat || player.Seat == config.YuanCommanderSeat ? 1 : 0)) &&
            Enumerable.Range(0, 8).All(seat => g.CardMovements.Count(m => m.To == CardLocation.Hand(seat) && m.Reason == CardMoveReasons.InitialDeal) == 5),
            "The real default tier-five setup grants two maximum HP and one opening card, with a separate commander HP bonus and original fixed commander versions.");
        var generated = E<GuanduChariotCreatedEvent>(g).Select(e => e.CardId).ToHashSet();
        var deck = g.CreateCardZoneDiagnostics().Where(c => !generated.Contains(c.CardId)).ToArray();
        var sourceDeck = r.Decks[mode.DeckId].PhysicalCards!;
        var kinds = string.Join(',', deck.GroupBy(c => c.CardKind).Select(group => group.Key + ":" + group.Count()));
        Need(sourceDeck.Count == 160 && deck.Length == sourceDeck.Count && deck.Select(c => c.CardId).Distinct().Count() == sourceDeck.Count &&
            deck.All(c => c.CardKind is not (CardKind.WoodenOx or CardKind.Alcohol or CardKind.FiveGrains or CardKind.PeachGarden or CardKind.Lightning or CardKind.BarbarianAssault)) &&
            new[] { CardKind.Intoxication, CardKind.StoreGrain, CardKind.Reinforcements, CardKind.UnitedEffort }.All(kind => deck.Any(c => c.CardKind == kind)),
            $"The actual military recipe already has no Wooden Ox and retains its 160 unique original entities after every configured replacement, independently of native generated chariots. recipe={sourceDeck.Count}, original={deck.Length}, generated={generated.Count}, kinds={kinds}");
        Drive(g, () => E<TurnStartedEvent>(g).Any());
        Need(E<TurnStartedEvent>(g).First().ActorSeat == config.CaoCommanderSeat,
            "Cao Cao begins the real turn cycle even when the human plays an unrelated rotated seat."); Cold(g, r);
    }

    public static void GuanduOptionsRemainScopedAndOldCheckpointShapeStaysUnchanged()
    {
        var plain = ComposedSkillContentRegistry.CreateShowcase();
        Need(!plain.Modes.ContainsKey("activity:guandu-2023") && !plain.Generals.Keys.Any(id => id.StartsWith("guandu:", StringComparison.Ordinal)) &&
            !JsonSerializer.Serialize(new GameOptions()).Contains("HumanGuanduRole", StringComparison.Ordinal),
            "An unselected activity adds no content registration or optional Guandu role property to an ordinary match's checkpoint options.");
        ExpectArgument(() => GameEngine.CreateStandard(new GameOptions { PlayerCount = 8, HumanGuanduRole = GuanduSeatRole.Commander }, plain));
        var r = ComposedSkillContentRegistry.CreateShowcase(includeGuanduActivities: true);
        ExpectArgument(() => GameEngine.CreateStandard(new GameOptions { PlayerCount = 8, ModeId = "activity:guandu-2023", HumanGuanduRole = (GuanduSeatRole)99 }, r));
        ExpectArgument(() => GameEngine.CreateStandard(new GameOptions { PlayerCount = 8, HumanSeat = -1, ModeId = "activity:guandu-2023", HumanGuanduRole = GuanduSeatRole.Commander }, r));
    }

    public static void NativeKillsTransferEnemyHandPunishFriendlyKillsAndDeferCommanderVictory()
    {
        var (g, r) = Start(CardKind.Slash, deathWatch: true);
        var enemyCards = V(g, 7).HandCount; var before = V(g, 0).HandCount; var movementStart = g.CardMovements.Count;
        Use(g, "kill", 7);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:guandu-death-watch");
        Need(g.CreateSnapshot(0).Winner == Winner.None && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
            "A real death child remains pending before mode reward and battle completion can pass its native parent."); Cold(g, r); Play(g);
        Need(!V(g, 7).IsAlive && V(g, 0).HandCount == before + enemyCards &&
            g.CardMovements.Count(m => m.From == CardLocation.Hand(7) && m.To == CardLocation.Hand(0) && m.Reason.Value == "guandu.enemy-kill-hand") == enemyCards &&
            !g.CardMovements.Skip(movementStart).Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)),
            "The enemy's genuine hand transfers to the living killer, rather than becoming the identity-mode three-card rebel reward.");
        var owned = V(g, 0).Hand.Select(c => c.Id).ToArray(); Use(g, "kill", 1); Play(g);
        Need(!V(g, 1).IsAlive && V(g, 0).HandCount == 0 && owned.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Reason.Value == "guandu.friendly-kill") == 1),
            "A real friendly kill discards the living killer's complete current hand through the native logical-discard ledger."); Cold(g, r);
        Use(g, "kill", 4); Until(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:guandu-death-watch");
        Need(g.CreateSnapshot(0).Winner == Winner.None && !E<WinnerDeterminedEvent>(g).Any(),
            "Killing the enemy commander does not commit victory while a real death observer still owns pending resolution."); Cold(g, r);
        Until(g, () => g.CreateSnapshot(0).Winner != Winner.None);
        Need(g.CreateSnapshot(0).WinnerTeamId == "team:cao" && g.ResolutionStack.Count == 0 && E<WinnerDeterminedEvent>(g).Count() == 1,
            "The commander's actual death awards the correct army once, after all native death and movement children finish."); Cold(g, r);
    }

    public static void ActualDeckExhaustionWaitsForDrawChildrenAndAwardsAiArmy()
    {
        var (g, r) = Start(CardKind.Slash, deckSize: 60);
        Use(g, "draw"); Until(g, () => g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
        Need(g.CreateSnapshot(0).Winner == Winner.None && !E<GuanduDeckExhaustedEvent>(g).Any(),
            "A request to draw beyond the remaining pile does not decide exhaustion before its genuine draw-start child returns."); Cold(g, r);
        Until(g, () => g.CreateSnapshot(0).Winner != Winner.None);
        Need(E<GuanduDeckExhaustedEvent>(g).Count() == 1 && g.CreateSnapshot(0).WinnerTeamId == "team:yuan" &&
            g.ResolutionStack.Count == 0 && g.CreateCardZoneDiagnostics().Count == 60 &&
            !g.CardMovements.Any(m => m.Reason == CardMoveReasons.Reshuffle),
            "The actual final draw-pile entity ends the battle in the AI army's favor after native children, without reshuffling the exhausted Guandu pile or duplicating entities."); Cold(g, r);
    }

    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 8).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 240; i++)
        {
            if (done()) return;
            var p = Prompt(g);
            if (p?.Kind == DecisionKind.SelectGeneral) throw new InvalidOperationException("Unexpected second human general draft.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Real Guandu setup did not reach its bounded native boundary: " + Prompt(g)?.Prompt);
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("An invalid or out-of-mode Guandu role must be rejected before setup.");
    }
}
