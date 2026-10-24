using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static bool IsMobileShiZhuangshiPrompt(GameSnapshot? view) =>
        view?.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "mobile:shi-zhuangshi", Choices.Count: > 0 } prompt &&
        prompt.PlayerSeat == view.HumanSeat && prompt.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "mobile-shi-wei-yan");

    public bool IsMobileShiZhuangshiSelectionPending => IsMobileShiZhuangshiPrompt(_snapshot);
    private PromptChoice? MobileShiZhuangshiPayChoice => IsMobileShiZhuangshiSelectionPending
        ? _snapshot.PendingDecision!.Choices.SingleOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "pay") : null;
    private bool HasMobileShiZhuangshiSelection => IsMobileShiZhuangshiSelectionPending &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("selected") == "true" &&
            (choice.Cards.Count == 1 || choice.Parameters.GetValueOrDefault("branch") != "hp-0"));
    private bool IsMobileShiZhuangshiMaterial(int id) => IsMobileShiZhuangshiSelectionPending &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Cards.SequenceEqual([id]) &&
            choice.Parameters.GetValueOrDefault("branch") == "card-" + id);
    private bool IsMobileShiZhuangshiCardSelected(int id) => IsMobileShiZhuangshiSelectionPending &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Cards.SequenceEqual([id]) &&
            choice.Parameters.GetValueOrDefault("selected") == "true");
    private string MobileShiZhuangshiSelectionHint => _snapshot.PendingDecision!.Prompt +
        "可连续多选手牌、同时选择失去体力，最后点击「确定」一起支付；再次点牌反选，Esc 清空。";

    private void ToggleMobileShiZhuangshiCard(int id)
    {
        var choice = IsMobileShiZhuangshiSelectionPending ? _snapshot.PendingDecision!.Choices.SingleOrDefault(choice =>
            choice.Cards.SequenceEqual([id]) && choice.Parameters.GetValueOrDefault("branch") == "card-" + id) : null;
        if (choice is not null) SubmitPublishedPromptChoice(choice, true, "壮誓选牌未执行");
    }

    private void ConfirmMobileShiZhuangshi()
    {
        if (MobileShiZhuangshiPayChoice is { } choice) SubmitPublishedPromptChoice(choice, true, "壮誓未执行");
    }

    private void ClearMobileShiZhuangshiSelection()
    {
        if (!IsMobileShiZhuangshiSelectionPending) return;
        var initial = _snapshot.PendingDecision!;
        var frame = initial.Choices[0].Parameters.GetValueOrDefault("frame-id");
        ExecuteSafely(() =>
        {
            // Each toggle stays on the original native frame. Requery the safe
            // player view after every answer; no selected card is paid here.
            for (var attempt = 0; attempt <= initial.Choices.Count; attempt++)
            {
                var view = _game.CreateSnapshot(initial.PlayerSeat);
                if (!IsMobileShiZhuangshiPrompt(view) || view.PendingDecision!.Choices[0].Parameters.GetValueOrDefault("frame-id") != frame) break;
                var prompt = view.PendingDecision;
                var selected = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 1 &&
                    choice.Parameters.GetValueOrDefault("selected") == "true");
                selected ??= prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("selected") == "true" &&
                    choice.Parameters.GetValueOrDefault("branch") is { } branch && branch.StartsWith("hp-", StringComparison.Ordinal) && branch != "hp-0")
                    ? prompt.Choices.SingleOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "hp-0") : null;
                if (selected is null || !SubmitCommand(new AnswerPromptCommand(view.HumanSeat, prompt.PromptId, selected.Id, view.Revision)).Accepted) break;
            }
            RefreshCurrentView();
        });
    }
}
