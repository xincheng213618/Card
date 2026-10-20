using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ZhongshenRoundBoundaryEvent(int RoundNumber, long MovementSequence) : IGameEvent;
