using System.Reflection;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class DecisionContextChecks
{

    public static void VerifyLive(MainViewModel vm)
    {
        var engine = Program.Engine(vm);
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        var view = engine.CreateSnapshot(0);
        var expected = DecisionContext.From(view);
        Program.Assert(vm.CurrentDecisionContext == expected, "Displayed context does not match the actual private prompt.");
        Program.Assert(expected == DecisionContext.From(engine.CreateSnapshot(0, true)), "Hidden identities or opponent hands changed response context.");
        foreach (var seat in vm.Seats)
        {
            var label = expected?.TargetSeat == seat.Seat ? expected.TargetLabel : expected?.SourceSeat == seat.Seat ? "效果来源" : string.Empty;
            Program.Assert(seat.DecisionRoleLabel == label, "A seat retained a stale effect marker.");
            var response = view.PendingDecision;
            var questionOrCost = response is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } &&
                response.Choices.Any(choice => choice.Parameters.GetValueOrDefault("declaration-answer") is "pass" or "challenge" ||
                    choice.Parameters.GetValueOrDefault("response") == "faction-request-cost");
            var status = response is not null && !questionOrCost && response.PlayerSeat == view.HumanSeat && response.PlayerSeat == seat.Seat
                ? response.Kind switch
                {
                    DecisionKind.RespondDodge or DecisionKind.RespondSlash =>
                        (response.RequiredCardKind ?? (response.Kind == DecisionKind.RespondDodge ? CardKind.Dodge : CardKind.Slash)) switch
                        { CardKind.Dodge => "待打闪", CardKind.Slash => "待打杀", _ => string.Empty },
                    DecisionKind.RescueDying => "待救援", DecisionKind.Nullification => "待无懈",
                    DecisionKind.FireAttackReveal => "待展示", DecisionKind.FireAttackDiscard => "待弃牌", _ => string.Empty
                } : string.Empty;
            Program.Assert(seat.ResponseStatusText == status && seat.HasResponseStatus == (status.Length > 0) &&
                seat.IsCurrent == (seat.Seat == view.CurrentSeat && view.Status != EngineStatus.Completed),
                "The live persistent responder status must follow only the safe current prompt without changing the active turn seat.");
        }
        Program.Assert(state == SnapshotJson.Serialize(engine.CreateSnapshot(0, true)), "Reading response context changed the match.");
    }
}
