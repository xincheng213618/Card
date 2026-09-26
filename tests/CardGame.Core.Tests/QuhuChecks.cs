using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QuhuChecks
{
    public static void PindianWinLossDamageAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var win = QuhuScenario.Find(sourceWins: true);
        var pindianStage = GameReplay.Restore(RoundTrip(win.AtPindianPrompt), registry);
        var opponentPrompt = pindianStage.CreateSnapshot(win.OpponentSeat).PendingDecision;
        var opponentHand = pindianStage.CreateSnapshot(win.OpponentSeat).Players[win.OpponentSeat].Hand;
        Require(opponentPrompt is
                {
                    Kind: DecisionKind.SkillModule,
                    PlayerSeat: var opponentSeat,
                    IsPrivate: true
                } &&
                opponentSeat == win.OpponentSeat &&
                opponentPrompt.ValidCardIds.SequenceEqual(opponentHand.Select(card => card.Id)) &&
                opponentPrompt.Choices.SelectMany(choice => choice.Cards)
                    .SequenceEqual(opponentHand.Select(card => card.Id)) &&
                pindianStage.CreateSnapshot(win.SourceSeat).PendingDecision is null,
            "Quhu must ask only the opponent for one exact private hand card before revealing either Pindian card.");
        var winGame = win.Game;
        var targetPrompt = winGame.PendingDecision ??
            throw new InvalidOperationException("Winning Quhu lost its damage-target prompt.");
        var pindian = winGame.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>()
            .Last(item => item.SkillId == "classic:quhu").Result;
        Require(targetPrompt is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    SkillPrompt.SkillId: "classic:quhu",
                    PlayerSeat: var sourceSeat,
                    IsPrivate: true
                } &&
                sourceSeat == win.SourceSeat &&
                targetPrompt.ValidTargetSeats.Count > 0 &&
                targetPrompt.ValidTargetSeats.All(seat => seat != win.OpponentSeat) &&
                pindian.SourceSeat == win.SourceSeat &&
                pindian.OpponentSeat == win.OpponentSeat &&
                pindian.SourceCardId == win.SourceCardId &&
                pindian.SourceWon &&
                pindian.SourceRank > pindian.OpponentRank &&
                winGame.CreateSnapshot(win.OpponentSeat).PendingDecision is null,
            "Winning Quhu must publicly reveal both committed cards, then privately ask Xun Yu for a legal damage target.");

        var paused = RoundTrip(winGame.CreateCheckpoint());
        var restored = GameReplay.Restore(paused, registry);
        Require(State(restored) == State(winGame) && Events(restored).SequenceEqual(Events(winGame)),
            "A paused winning Quhu target choice must restore exactly.");

        var victim = targetPrompt.ValidTargetSeats[0];
        var victimHp = winGame.CreateSnapshot(win.SourceSeat, revealAll: true).Players[victim].Hp;
        var choice = targetPrompt.Choices.Single(item => item.Targets.SequenceEqual([victim]));
        var resolved = winGame.Submit(new AnswerPromptCommand(
            win.SourceSeat, targetPrompt.PromptId, choice.Id, winGame.Revision));
        for (var step = 0; step < 32 &&
             !winGame.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                 .Any(item => item.SkillId == "classic:quhu"); step++)
        {
            var continuation = winGame.PendingDecision;
            var command = continuation is { PlayerSeat: var seat } && seat == win.SourceSeat
                ? (GameCommand)new AnswerPromptCommand(
                    seat,
                    continuation.PromptId,
                    continuation.Choices.Last().Id,
                    winGame.Revision)
                : new AdvanceOneStepCommand(winGame.Revision);
            Require(winGame.Submit(command).Accepted, "Winning Quhu could not finish its damage triggers.");
        }
        Require(resolved.Accepted &&
                winGame.CreateSnapshot(win.SourceSeat, revealAll: true).Players[victim].Hp == victimHp - 1 &&
                winGame.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == win.OpponentSeat && item.TargetSeat == victim) &&
                winGame.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.SkillId == "classic:quhu"),
            resolved.Error?.Message ?? "Winning Quhu must attribute normal damage to the Pindian opponent.");
        var replayed = GameReplay.Restore(RoundTrip(winGame.CreateCheckpoint()), registry);
        Require(State(replayed) == State(winGame) && Events(replayed).SequenceEqual(Events(winGame)),
            "Completed winning Quhu must replay exactly.");

        var loss = QuhuScenario.Find(sourceWins: false);
        var lossState = loss.Game.CreateSnapshot(loss.SourceSeat, revealAll: true);
        var lossPindian = loss.Game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>()
            .Last(item => item.SkillId == "classic:quhu").Result;
        Require(!lossPindian.SourceWon &&
                lossPindian.SourceRank <= lossPindian.OpponentRank &&
                loss.Game.PendingDecision?.Kind != DecisionKind.ProgramTrigger &&
                loss.Game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == loss.OpponentSeat && item.TargetSeat == loss.SourceSeat) &&
                lossState.Players[loss.SourceSeat].Hp == loss.SourceHpBefore - 1,
            $"Losing or tying Quhu must make the Pindian opponent deal one normal damage to Xun Yu. " +
            $"ranks={lossPindian.SourceRank}/{lossPindian.OpponentRank}; hp={lossState.Players[loss.SourceSeat].Hp}; " +
            $"pending={loss.Game.PendingDecision?.Kind}; damage={string.Join(',', loss.Game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Select(item => $"{item.SourceSeat}>{item.TargetSeat}"))}.");

        var tie = QuhuScenario.FindTie();
        var tiePindian = tie.Game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>()
            .Last(item => item.SkillId == "classic:quhu").Result;
        Require(!tiePindian.SourceWon &&
                tiePindian.SourceRank == tiePindian.OpponentRank &&
                tie.Game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == tie.OpponentSeat && item.TargetSeat == tie.SourceSeat),
            "A tied Pindian must count as Xun Yu not winning and damage Xun Yu from the opponent.");

    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
