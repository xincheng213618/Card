using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private PromptId? _skillTargetPromptId;
    private IReadOnlyList<PromptChoice> _skillTargetChoices = [];
    private HashSet<ChoiceId> _skillTargetChoiceIds = [];
    private HashSet<ChoiceId> _orderedSkillTargetChoiceIds = [];
    private readonly List<int> _selectedSkillTargetSeats = [];

    public bool IsSkillTargetSelectionPending => _skillTargetChoices.Count > 0;

    // Only unambiguous, cardless target choices can use the portrait draft.
    // Keep other effects explicit and submit the original published choice.
    private void SyncSkillTargetSelection()
    {
        var prompt = _snapshot.PendingDecision;
        _skillTargetChoices = prompt is { SkillPrompt: not null } && prompt.PlayerSeat == _snapshot.HumanSeat &&
            !SupportsHandResponse(prompt.Kind)
            ? prompt.Choices.Where(choice => choice.Cards.Count == 0 && choice.ContentIds.Count == 0 &&
                  choice.Targets.Count > 0 && choice.Targets.Distinct().Count() == choice.Targets.Count &&
                  choice.Targets.All(seat => _snapshot.Players.Any(player => player.Seat == seat)))
                .GroupBy(choice => string.Join(",", choice.Targets))
                .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray()
            : [];
        _skillTargetChoiceIds = _skillTargetChoices.Select(choice => choice.Id).ToHashSet();
        // A published pair may distinguish the first and second targets.
        _orderedSkillTargetChoiceIds = _skillTargetChoices.GroupBy(choice => string.Join(",", choice.Targets.Order()))
            .Where(group => group.Count() > 1).SelectMany(group => group).Select(choice => choice.Id).ToHashSet();
        _orderedSkillTargetChoiceIds.UnionWith(_skillTargetChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-kind") is nameof(SkillProgramTargetKind.LivingPairDistinct) or
                nameof(SkillProgramTargetKind.OtherLivingMale)).Select(choice => choice.Id));
        if (_skillTargetPromptId != prompt?.PromptId || !IsSkillTargetSelectionPending ||
            !_skillTargetChoices.Any(choice => CanExtendSkillTargets(choice, _selectedSkillTargetSeats)))
            _selectedSkillTargetSeats.Clear();
        _skillTargetPromptId = IsSkillTargetSelectionPending ? prompt!.PromptId : null;
    }

    private PromptChoice? SkillTargetDeclineChoice
    {
        get
        {
            if (!IsSkillTargetSelectionPending) return null;
            var choices = _snapshot.PendingDecision!.Choices.Where(choice => choice.Cards.Count == 0 &&
                choice.ContentIds.Count == 0 && choice.Targets.Count == 0 &&
                (choice.Parameters.GetValueOrDefault("program-action") is "skip" or "select-targets" ||
                 choice.Parameters.GetValueOrDefault("option") == "skip")).Take(2).ToArray();
            return choices.Length == 1 ? choices[0] : null;
        }
    }

    public IReadOnlyList<PromptChoice> CenterSkillChoices => IsSkillTargetSelectionPending
        ? SkillChoices.Where(choice => !_skillTargetChoiceIds.Contains(choice.Id) &&
            choice.Id != SkillTargetDeclineChoice?.Id).ToArray()
        : SkillChoices;

    private bool CanExtendSkillTargets(PromptChoice choice, IReadOnlyList<int> selected) =>
        selected.Count <= choice.Targets.Count && (_orderedSkillTargetChoiceIds.Contains(choice.Id)
            ? choice.Targets.Take(selected.Count).SequenceEqual(selected)
            : selected.All(choice.Targets.Contains));

    private PromptChoice? SelectedSkillTargetChoice
    {
        get
        {
            var matches = _skillTargetChoices.Where(choice => choice.Targets.Count == _selectedSkillTargetSeats.Count &&
                CanExtendSkillTargets(choice, _selectedSkillTargetSeats)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }

    private void ToggleSkillTarget(SeatViewModel seat)
    {
        if (!seat.IsLegalTarget || !_skillTargetChoices.Any(choice => choice.Targets.Contains(seat.Seat))) return;
        if (!_selectedSkillTargetSeats.Remove(seat.Seat))
        {
            if (_skillTargetChoices.All(choice => choice.Targets.Count == 1)) _selectedSkillTargetSeats.Clear();
            var next = _selectedSkillTargetSeats.Append(seat.Seat).ToArray();
            if (!_skillTargetChoices.Any(choice => CanExtendSkillTargets(choice, next))) return;
            _selectedSkillTargetSeats.Add(seat.Seat);
        }
        RefreshTargetHighlights();
    }

    private void RefreshSkillTargetHighlights()
    {
        var singleTarget = _skillTargetChoices.All(choice => choice.Targets.Count == 1);
        foreach (var seat in Seats)
        {
            seat.IsSelectedTarget = _selectedSkillTargetSeats.Contains(seat.Seat);
            seat.IsLegalTarget = seat.IsSelectedTarget || _skillTargetChoices.Any(choice =>
                choice.Targets.Contains(seat.Seat) && (singleTarget ||
                    CanExtendSkillTargets(choice, _selectedSkillTargetSeats.Append(seat.Seat).ToArray())));
        }
        CanPlaySelected = false;
        CanPlaySelectedAsSlash = false;
        RefreshSelectionHint();
    }

    private string SkillTargetSelectionHint
    {
        get
        {
            if (_selectedSkillTargetSeats.Count == 0)
                return $"{_snapshot.PendingDecision!.Prompt} 点选亮起的武将，再点击「确定」。";
            var choice = SelectedSkillTargetChoice;
            var targets = choice?.Targets ?? _selectedSkillTargetSeats;
            var separator = choice is not null && _orderedSkillTargetChoiceIds.Contains(choice.Id) ? " → " : "、";
            return $"已选：{string.Join(separator, targets.Select(seat => Seats.Single(item => item.Seat == seat).GeneralName))} · " +
                (choice is not null ? "点击「确定」执行；再次点选可取消选择。" : "继续点选亮起的武将；再次点选可取消选择。");
        }
    }

    private void ConfirmSkillTarget()
    {
        if (SelectedSkillTargetChoice is { } choice) SelectSkillChoice(choice);
    }
}
