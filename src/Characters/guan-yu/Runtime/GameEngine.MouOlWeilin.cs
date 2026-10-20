namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool GuanConversionAvailable(int owner, string skill) => skill != "mou-ol:weilin" || !EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
        .OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == owner && e.Action.Type == CardActionType.Use &&
            e.Action.ConversionChain.Any(s => s.OwnerSeat == owner && s.SkillId == skill && s.BindingId != "weilin-hand-slash"));

    private ProgramCardIdentityMatch? WeilinIdentity(CharacterState owner, Card card)
    {
        if (_cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat)) return null;
        var suit = EffectiveSuit(owner, card);
        if (suit == Suit.None) return null;
        var policy = EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent).OfType<WeilinHandColorEvent>().LastOrDefault(e => e.TargetSeat == owner.Seat && e.TurnNumber == _turnNumber && e.IsRed == IsRedSuit(suit));
        return policy is null ? null : new(_contentRegistry.GetSkill(policy.Source.SkillId).Program!,
            new SkillProgramCardIdentity("weilin-hand-slash", [CardZoneKind.Hand], [], [], CardKind.Slash, new(SkillProgramConditionKind.Always, 0, [])),
            policy.Source with { BindingId = "weilin-hand-slash" });
    }

    [ProgramCardLegality(ProgramCardLegalityStage.NamedConversionBan, 20)]
    private bool GuanConversionUnavailable(ProgramConfiguredViewAsQuery query) =>
        !GuanConversionAvailable(query.Owner.Seat, query.Instance.SkillId);

}
