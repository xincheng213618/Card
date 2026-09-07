namespace CardGame.Core;

/// <summary>
/// Replays a trusted-host command journal against a fresh deterministic engine.
/// Every command is submitted through the normal revision and prompt validation
/// boundary, so a corrupted or out-of-order journal fails instead of silently
/// changing the rules path.
/// </summary>
public static class GameReplay
{
    public static GameEngine Replay(
        GameOptions options,
        IEnumerable<GameCommand> commands,
        ContentRegistry? contentRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(commands);

        var engine = GameEngine.CreateStandard(options, contentRegistry);
        foreach (var command in commands)
        {
            var result = engine.Submit(command);
            if (!result.Accepted)
            {
                var error = result.Error;
                throw new InvalidOperationException(
                    $"Replay command was rejected ({error?.Code}): {error?.Message}");
            }
        }

        return engine;
    }
}
