using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private (GameEngine Game, PromptId PromptId)? _queuedUnavailableRescue;
    private (GameEngine Game, PromptId PromptId)? _queuedOnlyTargetCard;

    private static PendingDecision? OnlyTargetHandCardPrompt(GameSnapshot snapshot) =>
        snapshot.PendingDecision is
        {
            Kind: DecisionKind.SelectTargetCard, Choices.Count: 1,
            IncomingCard: CardKind.Dismantlement or CardKind.Snatch
        } prompt && prompt.PlayerSeat == snapshot.HumanSeat &&
        prompt.Choices[0].Cards.Count == 0 &&
        prompt.Choices[0].Parameters.GetValueOrDefault("action") == "target-card-slot" &&
        prompt.Choices[0].Parameters.GetValueOrDefault("slot-index") == "0" ? prompt : null;

    private void QueueOnlyTargetHandCard()
    {
        if (_disposed || _initializing || IsTutorialActive || OnlyTargetHandCardPrompt(_snapshot) is not { } prompt) return;
        var work = (Game: _game, PromptId: prompt.PromptId);
        if (_queuedOnlyTargetCard == work) return;
        _queuedOnlyTargetCard = work;
        var humanSeat = _snapshot.HumanSeat;
        // Keep the normal effect-time prompt and command in Core, but answer the
        // sole opaque slot after commit and before WPF renders a second picker.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            if (_queuedOnlyTargetCard == work) _queuedOnlyTargetCard = null;
            if (_disposed || _initializing || IsTutorialActive || !ReferenceEquals(_game, work.Game)) return;
            var current = _game.CreateSnapshot(humanSeat);
            if (OnlyTargetHandCardPrompt(current) is not { } pending || pending.PromptId != work.PromptId) return;
            ExecuteSafely(() =>
            {
                var result = SubmitCommand(new AnswerPromptCommand(humanSeat, pending.PromptId,
                    pending.Choices[0].Id, current.Revision));
                RefreshCommandResult(result.State);
            });
        }));
    }

    private void SelectOnlyCardTarget()
    {
        if (IsTutorialActive || IsActiveSkillSelectionPending || IsMultiTargetCardSelected ||
            _snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt ||
            prompt.PlayerSeat != _snapshot.HumanSeat || _selectedCardId is not { } cardId) return;
        var actions = GetViewLegalActions().Where(action => action.CardId == cardId &&
            action.Kind != LegalActionKind.Recast && MatchesSelectedPlayConversion(action)).ToArray();
        // Different card-region choices can still share one recipient. Targetless
        // alternatives and ordered/multiple recipients retain their own drafts.
        if (actions.Length == 0 || actions.Any(action => action.TargetSeats.Count != 1)) return;
        var targets = actions.Select(action => action.TargetSeats[0]).Distinct().Take(2).ToArray();
        if (targets is [var target]) _selectedTargetSeat = target;
    }

    private void SelectOnlyActiveSkillTarget()
    {
        if (IsTutorialActive || !IsActiveSkillTargetSelectionPending ||
            HumanActiveSkillAction is not { } action || ActiveSkillMaximumTargets(action) != 1) return;
        // Target-only active skills require a portrait click even when only one recipient is legal.
        if (action.MaxCardCount == 0) return;
        // Cardless view-as actions publish each exact target separately. The
        // first rail entry alone does not make its recipient the only legal one.
        if (IsTieredRoundZeroAction(action) && HumanActiveSkillActions.Any(candidate =>
                SameTieredRoundZeroAction(candidate with { TargetSeat = action.TargetSeat, TargetSeats = action.TargetSeats }, action) &&
                !candidate.TargetSeats.SequenceEqual(action.TargetSeats))) return;
        if (_selectedActiveSkillTargetSeats.Count != 0 &&
            _selectedActiveSkillTargetSeats.All(seat => CanSelectActiveSkillTarget(action, seat))) return;
        var targets = ActiveSkillSelectableTargets(action).Where(seat => CanSelectActiveSkillTarget(action, seat))
            .Distinct().Take(2).ToArray();
        if (targets is [var target])
        {
            _selectedActiveSkillTargetSeats.Clear();
            _selectedActiveSkillTargetSeats.Add(target);
        }
    }

    private void SelectOnlyPromptSkillTarget()
    {
        if (IsTutorialActive || _selectedSkillTargetSeats.Count != 0 || _skillTargetChoices.Count == 0 ||
            _skillTargetChoices.Any(choice => choice.Targets.Count != 1)) return;
        var targets = _skillTargetChoices.Select(choice => choice.Targets[0]).Distinct().Take(2).ToArray();
        if (targets is [var target]) _selectedSkillTargetSeats.Add(target);
    }

    private static PendingDecision? UnavailableRescuePrompt(GameSnapshot snapshot) =>
        snapshot.PendingDecision is { Kind: DecisionKind.RescueDying, Choices.Count: 1 } prompt &&
        prompt.PlayerSeat == snapshot.HumanSeat &&
        prompt.Choices[0].Parameters.GetValueOrDefault("response") == "let-die" ? prompt : null;

    private void QueueUnavailableRescuePass()
    {
        if (_disposed || _initializing || IsTutorialActive || UnavailableRescuePrompt(_snapshot) is not { } prompt) return;
        var work = (Game: _game, PromptId: prompt.PromptId);
        if (_queuedUnavailableRescue == work) return;
        _queuedUnavailableRescue = work;
        var humanSeat = _snapshot.HumanSeat;
        // StateChanged is delivered inside the committed command session. Answer
        // on the next UI turn, then re-resolve this exact prompt and its choices.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_queuedUnavailableRescue == work) _queuedUnavailableRescue = null;
            if (_disposed || _initializing || IsTutorialActive || !ReferenceEquals(_game, work.Game)) return;
            var current = _game.CreateSnapshot(humanSeat);
            if (UnavailableRescuePrompt(current) is not { } pending || pending.PromptId != work.PromptId) return;
            ExecuteSafely(() =>
            {
                var result = SubmitCommand(new AnswerPromptCommand(humanSeat, pending.PromptId,
                    pending.Choices[0].Id, current.Revision));
                RefreshCommandResult(result.State);
            });
        }));
    }
}
