namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RoundDamageCardHistorySkillId = "system:round-damage-card-history";
    private const string RoundDamageCardUsagePrefix = "card-name";

    private readonly HashSet<int> _roundTurnSeats = [];
    private int _roundNumber;

    private bool UsesFormalMouLuMeng =>
        HasClassicGeneralPackage;

    private void BeginRoundForTurn(CharacterState current)
    {
        if ((!IsWushuangMode && !UsesFormalMouLuMeng && !_contentRegistry.ProgramDependencies.UsesRoundTracking && _modeDefinition.NianActivity is null && !IsDragonBoatActivity && !IsGuanduActivity) ||
            _turnProgression.Kind == ActualTurnKind.Extra) return;

        if (_roundNumber == 0 || _roundTurnSeats.Contains(current.Seat))
        {
            if (!TracksRoundEnding || !ProgramEventHistory<RoundEndedEvent>().Any(e => e.RoundNumber == _roundNumber))
                ReturnDemonRoundHolds();
            _roundNumber++;
            _roundTurnSeats.Clear();
            _skillRuntimeState.ResetRound();
            AdvanceEventRulesAndQueueFact(new RoundStartedEvent(_roundNumber, current.Seat));
            AddLog("RoundStarted", $"第 {_roundNumber} 轮开始。", current.Seat);
        }

        _roundTurnSeats.Add(current.Seat);
    }

    private static bool IsDamageCardKind(CardKind kind) => kind is
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
        CardKind.FireAttack or CardKind.WumiaoFlood;

    private static string GetRoundDamageCardUsageId(CardKind kind) =>
        $"{RoundDamageCardUsagePrefix}.{kind}";

}
