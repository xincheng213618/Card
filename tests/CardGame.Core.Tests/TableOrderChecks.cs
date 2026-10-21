using CardGame.Core;

internal static class TableOrderChecks
{
    internal static void NativeMoveChangesDistanceAndNextNormalTurn()
    {
        var (game, registry) = CardGame.Core.Tests.ShenDengAiChecks.CreateAwakenedTableFixture();
        var snapshot = game.CreateSnapshot(0);
        Need(snapshot.TableOrder!.SequenceEqual([1, 2, 0, 3]) && game.GetSeatDistance(0, 1) == 2 && game.GetCombatDistance(0, 1) == 2 &&
            game.GetSeatDistance(0, 2) == 1 && game.GetCombatDistance(0, 2) == 1,
            "A genuine Qijing move must update both raw and living combat distance without renumbering participants.");
        Need(snapshot.TableOrder is IList<int> { IsReadOnly: true }, "Public table order must be immutable before observers receive it.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))),
            "The real moved table and extra turn must replay identically for every viewer.");
        game = restored;
        var turn = game.State.TurnNumber;
        Accept(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 120 && game.State.TurnNumber == turn; step++)
        {
            var pending = game.PendingDecision;
            if (pending is { PlayerSeat: 0, SkillPrompt: not null, Choices.Count: > 0 })
                Accept(new AnswerPromptCommand(0, pending.PromptId, pending.Choices[0].Id, game.Revision));
            else Accept(new AdvanceOneStepCommand(game.Revision));
        }
        Need(game.State.TurnNumber == turn + 1 && game.State.CurrentSeat == 3,
            "After the real extra turn ends, the next normal participant must follow the moved ring rather than numeric seat one.");
        void Accept(GameCommand command)
        { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Expected native table progression command."); }
    }

    internal static void TriggerPrioritiesUseMovedParticipantOrder()
    {
        IReadOnlyList<int> order = Array.AsReadOnly(new[] { 0, 2, 1, 3 });
        var candidates = Enumerable.Range(0, 4).Select(seat =>
            new DamageTriggerCandidate(seat, "candidate", seat == 3 ? 10 : 0,
                ProgramId: "fixture", ProgramTriggerId: "binding", SkillInstanceId: "instance", GameplayHash: "hash")).ToArray();
        var damage = DamageTriggerOrdering.Order(candidates, 0, 4, order);
        var judgment = JudgmentTriggerOrdering.Order(candidates.Select(candidate =>
            new JudgmentTriggerCandidate(candidate.OwnerSeat, candidate.CandidateId, candidate.Priority,
                candidate.ProgramId, candidate.ProgramTriggerId, candidate.SkillInstanceId, candidate.GameplayHash)), 0, 4, order);
        Need(damage.Select(candidate => candidate.OwnerSeat).SequenceEqual([3, 0, 2, 1]) &&
            judgment.Select(candidate => candidate.OwnerSeat).SequenceEqual([3, 0, 2, 1]),
            "Priority remains primary; ties must follow the moved public table rather than participant ids.");
        Need(DamageTriggerOrdering.GetRelativeSeatOrder(2, 0, 4, order) == 3 &&
            DamageTriggerOrdering.GetRelativeSeatOrder(2, 1, 4, order) == 1,
            "A nonzero current actor anchors clockwise ordering in the moved ring.");
        try { _ = DamageTriggerOrdering.Order(candidates, 0, 4, [0, 2, 2, 3]); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("A malformed participant permutation must fail before sorting.");
    }

    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
