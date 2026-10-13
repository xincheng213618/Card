using System.Reflection;
using CardGame.Core;

internal static partial class PairedHandRecastChecks
{
    public static void ObserverSubtreeRejectsUnrelatedEdgesAndResumesExactlyOnce()
    {
        var (game, registry) = Create();
        var ownerCard = V(game, 0).Hand[0].Id;
        var partnerCard = V(game, 1).Hand[0].Id;
        var issued = Begin(game, ownerCard, partnerCard);
        SelectPartner(game, partnerCard);
        CostBoundary(game, issued, 0, ownerCard, partnerCard);

        var root = Root(game, issued.FrameId);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>()
            .Single(frame => frame.Id == root.PairedHandRecast!.Owner.CostBatchId);
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Cost);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var findRoot = typeof(GameEngine).GetMethod("PairedHandRecastObserverRoot", flags)!;
        var advance = typeof(GameEngine).GetMethod("TryAdvancePairedHandRecastSubtree", flags)!;
        var replace = typeof(GameEngine).GetMethod("ReplaceRuntimeFrame", flags)!;
        ProgramSkillFrame? FindRoot() => (ProgramSkillFrame?)findRoot.Invoke(game, null);
        void Replace(ResolutionFrame frame) => replace.Invoke(game, [frame.Id, frame]);

        Require(FindRoot()?.Id == root.Id, "A real paid movement and its published native observer form an admitted subtree.");
        var before = State(game);
        Require(!(bool)advance.Invoke(game, null)! && State(game) == before,
            "Shared subtree advancement must leave a live private choice, its cursor, events and all player views untouched.");

        void RejectEdge(ResolutionFrame original, ResolutionFrame invalid)
        {
            try
            {
                Replace(invalid);
                Require(FindRoot() is null,
                    "Sharing traversal must not admit a different payer, first-child owner, movement source or native observer parent.");
            }
            finally
            {
                Replace(original);
            }
            Require(FindRoot()?.Id == root.Id && State(game) == before,
                "Read-only root validation and restoration must preserve the exact paused command boundary.");
        }

        RejectEdge(movement, movement with { Batch = movement.Batch with { ParentFrameId = root.Id + 1000 } });
        RejectEdge(movement, movement with { Batch = movement.Batch with { AwaitingProgramFrameId = null } });
        RejectEdge(movement, movement with { Batch = movement.Batch with { OriginOwnerSeat = 1 } });
        RejectEdge(movement, movement with { Batch = movement.Batch with { OriginSkillId = Gain } });
        RejectEdge(observer, observer with { WindowContext = observer.WindowContext! with { ParentFrameId = root.Id } });
        RejectEdge(root, root with
        {
            PairedHandRecast = root.PairedHandRecast! with
            {
                Owner = root.PairedHandRecast.Owner with { CostIssued = false }
            }
        });

        game = Cold(game, registry);
        Continue(game);
        ReachGain(game, 0);
        GainBoundary(game, issued, 0, ownerCard, partnerCard);
        FinishGain(game);
        CostBoundary(game, issued, 1, ownerCard, partnerCard);
        Continue(game);
        ReachGain(game, 1);
        GainBoundary(game, issued, 1, ownerCard, partnerCard);
        FinishGain(game);
        Play(game);
        Completed(game, issued, ownerCard, partnerCard, true);
        _ = Cold(game, registry);
    }
}
