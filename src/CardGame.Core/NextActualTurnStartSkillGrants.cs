using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Expires at the recipient's first real TurnStarted after this creation turn.</summary>
public sealed record SkillGrantNextActualTurnStartExpiry(
    int GrantedTurnNumber, int GrantedTurnOwnerSeat, int RecipientSeat);

public sealed partial record SkillGrant
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillGrantNextActualTurnStartExpiry? NextActualTurnStartExpiry { get; init; }
}

public sealed record ProgramNextActualTurnStartSkillGrantExpiredEvent(
    int RecipientSeat, SkillGrant Grant, int TurnNumber, int TurnOwnerSeat) : IGameEvent;
