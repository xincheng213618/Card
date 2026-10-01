namespace CardGame.Core;

public sealed partial class GameEngine
{

    /// <summary>
    /// An internal rules failure may have changed mutable state before it threw.
    /// A faulted session must be reconstructed from a trusted command prefix.
    /// </summary>
    public bool IsFaulted => _fatalRuleFailure is not null;

    /// <summary>
    /// The last complete command prefix captured before a failing operation,
    /// when this session was driven entirely through Submit.
    /// </summary>
    public GameCheckpoint? LastTrustedCheckpoint => IsFaulted ? _lastTrustedCheckpoint : null;

    private void EnsureSessionHealthy()
    {
        if (_fatalRuleFailure is not null)
        {
            throw new InvalidOperationException(
                "The match stopped after an internal rules failure. Restore its last trusted checkpoint before continuing.",
                _fatalRuleFailure);
        }
    }

    private GameCheckpoint BuildTrustedCheckpoint(long revision)
    {
        return new GameCheckpoint(
            GameCheckpoint.CurrentSchemaVersion,
            _options,
            Array.AsReadOnly(_acceptedCommands.ToArray()),
            revision,
            _modeDefinition.Id,
            GameReplay.GetPackageSignatures(_contentRegistry),
            GameReplay.GetContentHash(_contentRegistry))
        {
            RulesVersion = GameCheckpoint.CurrentRulesVersion
        };
    }

    private void MarkSessionFaulted(Exception failure)
    {
        _fatalRuleFailure ??= failure;
        _status = EngineStatus.Faulted;
    }
}
