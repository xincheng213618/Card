using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class DecisionContextChecks
{
    public static void Semantics()
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var snapshot = Program.Engine(vm).CreateSnapshot(0);
        // These presentation-only variants cover different meanings of the optional source/target fields.
        var prompt = new PendingDecision(DecisionKind.RescueDying, 0, "AI 2 进入濒死状态。", [], [], SourceSeat: 1) { TargetSeat = 1 };
        var context = DecisionContext.From(snapshot with { PendingDecision = prompt })!;
        Program.Assert(context.SourceSeat is null && context.TargetSeat == 1 && context.TargetLabel == "等待救援",
            "Rescue source must not be presented as the attacker.");
        Program.Assert(context.Title.Contains(snapshot.Players[1].GeneralName) && !context.Description.Contains("AI 2"),
            "Public general and seat names must identify the rescue target.");
        var draw = prompt with
        {
            Kind = DecisionKind.Nullification,
            SourceSeat = 1,
            TargetSeat = null,
            IncomingCard = CardKind.DrawTwo,
            Prompt = "上一张【无懈可击】使【无中生有】暂时失效，是否继续使用【无懈可击】？"
        };
        context = DecisionContext.From(snapshot with { PendingDecision = draw })!;
        Program.Assert(context.TargetSeat == 1 && !context.Title.Contains("群体") && context.Description == draw.Prompt,
            "DrawTwo must identify its beneficiary and preserve the counter-Nullification explanation.");
        var group = draw with { IncomingCard = CardKind.PeachGarden };
        context = DecisionContext.From(snapshot with { PendingDecision = group })!;
        Program.Assert(context.TargetSeat is null && context.Title.Contains("群体效果"), "A group window must not imply the responder is its only target.");
        var duel = prompt with { Kind = DecisionKind.RespondSlash, IncomingCard = CardKind.Duel, SourceSeat = 2, TargetSeat = null };
        context = DecisionContext.From(snapshot with { PendingDecision = duel })!;
        Program.Assert(context.SourceSeat == 2 && context.TargetSeat == 0, "Duel must use the current opposing responder, not the active turn seat.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = duel with { PlayerSeat = 3 } }) is null,
            "Another player's private prompt was projected into the local context.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = null }) is null, "Resolved prompts must clear context.");
    }

    public static void VerifyLive(MainViewModel vm)
    {
        var engine = Program.Engine(vm);
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        var expected = DecisionContext.From(engine.CreateSnapshot(0));
        Program.Assert(vm.CurrentDecisionContext == expected, "Displayed context does not match the actual private prompt.");
        Program.Assert(expected == DecisionContext.From(engine.CreateSnapshot(0, true)), "Hidden identities or opponent hands changed response context.");
        foreach (var seat in vm.Seats)
        {
            var label = expected?.TargetSeat == seat.Seat ? expected.TargetLabel : expected?.SourceSeat == seat.Seat ? "效果来源" : string.Empty;
            Program.Assert(seat.DecisionRoleLabel == label, "A seat retained a stale effect marker.");
        }
        Program.Assert(state == SnapshotJson.Serialize(engine.CreateSnapshot(0, true)), "Reading response context changed the match.");
    }
}
