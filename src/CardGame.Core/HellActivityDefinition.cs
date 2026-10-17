namespace CardGame.Core;

/// <summary>OL April 2020 four-encounter campaign, with one auxiliary and a conditional final boss.</summary>
public sealed record HellActivityDefinition(int BossTier, string AuxiliaryGeneralId, string HiddenGeneralId);

public sealed record HellAuxiliaryScheduledEvent(int StageIndex, int AuxiliarySeat) : IGameEvent;
public sealed record HellNpcAppearedEvent(long AppearanceId, int StageIndex, int Seat, string GeneralId) : IGameEvent;
public sealed record HellAppearanceCompletedEvent(long AppearanceId) : IGameEvent;
