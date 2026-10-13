using System.Reflection;
using System.Text.Json;
using CardGame.Core;

internal static partial class SoleTargetProtectionChecks
{
    private static void VerifyExactProtectedRedirectProofs(GameEngine g, CardUseFrame use,
        IReadOnlyList<SoleTargetAfterProtectionRedirectedEvent> facts)
    {
        var reader = typeof(GameEngine).GetMethod("IsSoleTargetAfterProtectionRedirectFact",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The native protected-redirect proof reader is missing.");
        var checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint());
        var frames = JsonSerializer.Serialize(g.ResolutionStack);
        var views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false))).ToArray();
        Require(facts.Count == 3 && facts.All(HasIssuedProof),
            "All three real protected redirects retain their own exact issued-event proof after target reentry.");

        var first = facts[0];
        var alternate = g.CreateSnapshot(0).Players.Single(p => p.Seat != use.SourceSeat &&
            p.Seat != first.PreviousTargetSeat && p.Seat != first.NewTargetSeat).Seat;
        // Preserve the genuine window/program/instruction keys and a legal
        // replacement shape, but change the target that this event never issued.
        var forged = first with
        {
            NewTargetSeat = alternate,
            AfterTargets = Array.AsReadOnly(first.BeforeTargets.Select((seat, index) =>
                index == first.TargetIndex ? alternate : seat).ToArray())
        };
        Require(!HasIssuedProof(forged),
            "A different valid target cannot borrow an issued redirect's window, program and instruction keys.");
        Require(checkpoint == GameCheckpointJson.Serialize(g.CreateCheckpoint()) &&
            frames == JsonSerializer.Serialize(g.ResolutionStack) &&
            views.SequenceEqual(Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false)))),
            "Reading valid and forged redirect proofs leaves commands, owning frames and every player view unchanged.");

        bool HasIssuedProof(SoleTargetAfterProtectionRedirectedEvent fact) =>
            (bool)reader.Invoke(g, [use, fact, fact.BeforeTargets])!;
    }
}
