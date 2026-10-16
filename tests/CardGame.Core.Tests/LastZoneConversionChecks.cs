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

    private static bool IsConversion(PromptChoice c) => c.Parameters.GetValueOrDefault("conversion-skill-id") == Conversion;
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
