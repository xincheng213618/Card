using CardGame.Core;

internal static class BoundaryQuanCongUiChecks
{
    public static void ChargedYaomingRailOptionsAndDamageMenuCold(string output)
    {
        const string skill = "boundary:yaoming-mobile";
        var scene = BoundaryQuanCongChecks.CreateScene(); var game = scene.Game;
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry); game = Program.Engine(model);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        var chip = model.HumanSkillCards.Single(c => c.ContentId == skill);
        Program.Assert(chip.IsAvailable && chip.ProgressText == "剩余 2/4" && chip.StateText.Contains("未记录选项", StringComparison.Ordinal),
            "The actual charged Yaoming rail starts with two of four uses and an explicit empty previous-option record.");
        for (var count = 1; count >= 0; count--)
        {
            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(c => c.ContentId == skill));
            ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "boundary-quan-cong-yaoming") == true);
            ShiFifthBatchUiChecks.Refresh(model);
            Program.Assert(model.IsSkillTargetSelectionPending && model.Seats.Single(s => s.Seat == 0).IsLegalTarget,
                "The unique self-draw choice is presented through the actual board target selector.");
            var self = model.Seats.Single(s => s.Seat == 0);
            // The sole unambiguous target is already selected by the shared UI.
            if (!self.IsSelectedTarget) model.SelectTargetCommand.Execute(self);
            Program.Assert(model.CanConfirmSelected, "The selected self-draw target enables its real confirmation command.");
            if (count == 1) host.Capture(output, "boundary-quan-cong-mobile-charges-and-target", 1120);
            model.ConfirmSelectedCommand.Execute(null);
            ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            ShiFifthBatchUiChecks.Refresh(model);
            chip = model.HumanSkillCards.Single(c => c.ContentId == skill);
            Program.Assert(chip.ProgressText == $"剩余 {count}/4" && chip.StateText.Contains("上次：摸牌", StringComparison.Ordinal) &&
                chip.IsAvailable == (count > 0), "Real board target confirmations update the charged rail and disable the empty activation.");
        }
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        ShiFifthBatchUiChecks.Accept(game, new UseProgramSkillCommand(0, "fixture:boundary-quan-cong-driver", "damage-three", [], [1], game.Revision, prompt.PromptId));
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "boundary-quan-cong-yaoming") == true);
        ShiFifthBatchUiChecks.Refresh(model);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BoundaryQuanCongYaoming is not null).Id;
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && model.HumanSkillCards.Single(c => c.ContentId == skill).ProgressText == "剩余 3/4" &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BoundaryQuanCongYaoming is not null).Id == root &&
            model.CanCancelAction && game.CreateSnapshot(0).PendingDecision!.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "skip"),
            "The actual save/load UI restores the original damage menu, its three charges and its decline choice.");
        model.CancelActionCommand.Execute(null);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(model.HumanSkillCards.Single(c => c.ContentId == skill) is { IsAvailable: true, ProgressText: "剩余 3/4" },
            "Declining the restored damage menu preserves all three charges for the next actual phase activation.");
    }
}
