using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using static LastZoneConversionScenario;

internal static class LastZoneConversionChecks
{

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
                VerifyJudgmentAdviceScoring(g, id);
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
    private static void VerifyJudgmentAdviceScoring(GameEngine game, int id)
    {
        var view = game.CreateSnapshot(0);
        var before = State(game);
        var actions = game.GetHumanLegalActions();
        var regional = actions.First(a => a.CardId == id && a.Kind == LegalActionKind.Slash);
        var ordinary = actions.Where(a => a.CardId != id).ToArray();
        Require(view.Seed is null && view.Players.Where(p => p.Seat != 0).All(p => p.Hand.Count == 0) &&
            view.Players[0].Judgment.Single().Kind == CardKind.Lightning && !view.Players[0].Hand.Any(c => c.Id == id),
            "Advice scoring must use the actual printed Judgment material and a player-filtered view.");
        foreach (var policy in new[] { 1, 2, 3 })
        {
            var legacy = new SimpleAiBrain(0, 721019, policy).ChoosePlay(view, ordinary, 1);
            var enabled = new SimpleAiBrain(0, 721019, policy, includeJudgmentMaterials: true).ChoosePlay(view, ordinary, 1);
            Require(JsonSerializer.Serialize(new { legacy.Action, legacy.Thought }) ==
                JsonSerializer.Serialize(new { enabled.Action, enabled.Thought }),
                "Opting into Judgment material must preserve every ordinary candidate score, reason and seeded choice.");
            var selected = new SimpleAiBrain(0, 721019, policy, includeJudgmentMaterials: true).ChoosePlay(view, [regional], 1);
            Require(ReferenceEquals(selected.Action, regional) && selected.Thought.Candidates is [var candidate] &&
                ReferenceEquals(candidate.Action, regional) && double.IsFinite(candidate.Score),
                "Advice must score and retain the exact native Judgment Slash action rather than discard or rewrite it.");
            var complete = new SimpleAiBrain(0, 721019, policy, includeJudgmentMaterials: true).ChoosePlay(view, actions, 1);
            Require(actions.Contains(complete.Action) && complete.Thought.Candidates.Count == actions.Count &&
                complete.Thought.Candidates.All(c => double.IsFinite(c.Score)) &&
                complete.Thought.Candidates.Select(c => c.Action).ToHashSet().SetEquals(actions),
                "Advice must evaluate all current legal actions alongside the real Judgment conversion.");
        }
        Require(State(game) == before, "Independent advice scoring must not change the match, payment, commands, prompts or replay state.");
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
}
