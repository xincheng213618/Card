namespace CardGame.Core;
public sealed record XiaHuiCardRestriction(CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId, long MovementSequence, Suit OriginalSuit);
public sealed record XiaHuiRestrictionGrantedEvent(XiaHuiCardRestriction Restriction) : IGameEvent;
public sealed record XiaHuiRestrictionReleasedEvent(int RecipientSeat, int CardId, long MovementSequence, string Reason) : IGameEvent;
