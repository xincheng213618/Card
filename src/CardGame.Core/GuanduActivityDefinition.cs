namespace CardGame.Core;

public enum GuanduBattleEvent
{
    TenWinsTenLosses, WuchaoFire, GrainShortage, SlayTheGenerals, HoldTheLine,
    Standoff, SlowAdvance, Underdog, Arrogance
}
public enum GuanduSeatRole { General, Commander }

/// <summary>OL June 2023. Unpublished seat arrangements and draft size are explicit local conventions.</summary>
public sealed record GuanduActivityDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> GeneralPools,
    IReadOnlyDictionary<string, string> Commanders,
    IReadOnlyList<GuanduBattleEvent> Events,
    IReadOnlyList<IReadOnlyList<string>> SeatLayouts,
    int PlayerTier = 5, int CandidateCount = 6);

public sealed record GuanduActivitySnapshot(GuanduBattleEvent Event, int RoundNumber,
    int PlayerCommanderSeat, int OpponentCommanderSeat, bool DeckExhausted,
    string PlayerTeamId, GuanduSeatRole HumanRole, int LayoutIndex);

public sealed record GuanduBattleConfiguredEvent(GuanduBattleEvent Event, int LayoutIndex,
    int CaoCommanderSeat, int YuanCommanderSeat, string PlayerTeamId, GuanduSeatRole HumanRole) : IGameEvent;
public sealed record GuanduDeckExhaustedEvent(int ActualTurnNumber) : IGameEvent;
