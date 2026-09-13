using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ActiveSkillChecks
{
    public static void Controls()
    {
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: 721019,
            showSetup: false,
            saveStore: new MemorySaveStore(),
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;

        Program.Assert(
            viewModel.TableModes.Any(mode => mode.ModeId == "identity:active-skills-8"),
            "The expanded WPF setup did not expose the active-skill mode.");
        var kujin = viewModel.GeneralChoices.Single(choice => choice.SkillName == "苦肉");
        viewModel.SelectGeneralChoiceCommand.Execute(kujin);
        Program.AdvanceToDecision(viewModel);
        Program.Assert(viewModel.CanUseActiveSkill, "The active skill was not exposed at the human play boundary.");

        Program.Render(root, 1120, 740, Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill.png"));
        var skillButton = Program.Find<Button>(root).SingleOrDefault(button =>
            button.Visibility == Visibility.Visible &&
            Equals(button.Content, viewModel.ActiveSkillButtonText));
        Program.Assert(skillButton is not null, "The active-skill action button was not rendered.");

        var engine = Program.Engine(viewModel);
        var before = engine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var revision = engine.Revision;
        viewModel.UseActiveSkillCommand.Execute(null);
        var after = engine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Program.Assert(engine.Revision == revision + 1, "The WPF active-skill button did not commit one command.");
        Program.Assert(after.Hp == before.Hp - 1 && after.Hand.Count == before.Hand.Count + 2,
            "The WPF active-skill command did not apply the Core effect.");

        window.Content = null;
        window.Close();

        using var selectionViewModel = FindZhihengViewModel();
        var selectionWindow = new MainWindow(selectionViewModel);
        selectionWindow.ApplyTemplate();
        var selectionRoot = (FrameworkElement)selectionWindow.Content;
        var zhiheng = selectionViewModel.GeneralChoices.Single(choice => choice.SkillName == "制衡");
        selectionViewModel.SelectGeneralChoiceCommand.Execute(zhiheng);
        Program.AdvanceToDecision(selectionViewModel);
        Program.Assert(selectionViewModel.CanUseActiveSkill,
            "The card-selection active skill was not exposed at the human play boundary.");
        Program.Assert(selectionViewModel.ActiveSkillButtonText.Contains("选择牌后", StringComparison.Ordinal),
            "Zhiheng should start with an explicit card-selection affordance.");

        var selectionEngine = Program.Engine(selectionViewModel);
        var selectionBefore = selectionEngine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        selectionViewModel.UseActiveSkillCommand.Execute(null);
        var selectedCards = selectionViewModel.Hand.OrderBy(card => card.Id).Take(2).ToArray();
        foreach (var card in selectedCards) selectionViewModel.SelectCardCommand.Execute(card);
        Program.Assert(selectedCards.All(card => card.IsSelected) && selectionViewModel.HasSelection,
            "Zhiheng card selection did not retain multiple selected cards.");
        Program.Assert(selectionViewModel.ActiveSkillButtonText.Contains("已选 2 张", StringComparison.Ordinal),
            "Zhiheng should expose the selected card count in its action button.");
        Program.Render(selectionRoot, 1120, 740,
            Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill-selection.png"));

        var selectionRevision = selectionEngine.Revision;
        selectionViewModel.UseActiveSkillCommand.Execute(null);
        var selectionAfter = selectionEngine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Program.Assert(selectionEngine.Revision == selectionRevision + 1,
            "The WPF Zhiheng selection did not commit one command.");
        Program.Assert(selectionAfter.Hp == selectionBefore.Hp &&
                      selectionAfter.Hand.Count == selectionBefore.Hand.Count &&
                      selectedCards.All(card => !selectionAfter.Hand.Any(afterCard => afterCard.Id == card.Id)),
            "The WPF Zhiheng selection did not apply the one-for-one card exchange.");

        selectionWindow.Content = null;
        selectionWindow.Close();

        using var targetSelectionViewModel = FindRendeViewModel();
        var targetSelectionWindow = new MainWindow(targetSelectionViewModel);
        targetSelectionWindow.ApplyTemplate();
        var targetSelectionRoot = (FrameworkElement)targetSelectionWindow.Content;
        var rende = targetSelectionViewModel.GeneralChoices.Single(choice => choice.SkillName == "仁德");
        targetSelectionViewModel.SelectGeneralChoiceCommand.Execute(rende);
        Program.AdvanceToDecision(targetSelectionViewModel);
        Program.Assert(targetSelectionViewModel.CanUseActiveSkill,
            "The target-selection active skill was not exposed at the human play boundary.");
        Program.Assert(targetSelectionViewModel.ActiveSkillButtonText.Contains("选择手牌和目标后", StringComparison.Ordinal),
            "Rende should start with an explicit hand-and-target selection affordance.");

        var targetSelectionEngine = Program.Engine(targetSelectionViewModel);
        var targetSelectionBefore = targetSelectionEngine.CreateSnapshot(0, revealAll: true);
        var rendeSourceBefore = targetSelectionBefore.Players.Single(player => player.Seat == 0);
        targetSelectionViewModel.UseActiveSkillCommand.Execute(null);
        var rendeCard = targetSelectionViewModel.Hand.OrderBy(card => card.Id).First();
        targetSelectionViewModel.SelectCardCommand.Execute(rendeCard);
        var rendeTarget = targetSelectionViewModel.Seats.First(seat => seat.IsLegalTarget);
        targetSelectionViewModel.SelectTargetCommand.Execute(rendeTarget);
        Program.Assert(rendeCard.IsSelected && rendeTarget.IsSelectedTarget && targetSelectionViewModel.HasSelection,
            "Rende hand and target selection did not retain both private parameters.");
        Program.Assert(targetSelectionViewModel.ActiveSkillButtonText.Contains("已选 1 张牌", StringComparison.Ordinal) &&
                      targetSelectionViewModel.ActiveSkillButtonText.Contains("1 个目标", StringComparison.Ordinal),
            "Rende should expose both selected parameter counts in its action button.");
        Program.Render(targetSelectionRoot, 1120, 740,
            Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill-target-selection.png"));

        var targetSelectionRevision = targetSelectionEngine.Revision;
        targetSelectionViewModel.UseActiveSkillCommand.Execute(null);
        var targetSelectionAfter = targetSelectionEngine.CreateSnapshot(0, revealAll: true);
        var rendeSourceAfter = targetSelectionAfter.Players.Single(player => player.Seat == 0);
        var rendeTargetAfter = targetSelectionAfter.Players.Single(player => player.Seat == rendeTarget.Seat);
        Program.Assert(targetSelectionEngine.Revision == targetSelectionRevision + 1,
            "The WPF Rende selection did not commit one command.");
        Program.Assert(rendeSourceAfter.Hand.Count == rendeSourceBefore.Hand.Count - 1 &&
                      rendeTargetAfter.Hand.Count == targetSelectionBefore.Players.Single(player => player.Seat == rendeTarget.Seat).HandCount + 1,
            "The WPF Rende selection did not transfer the selected card.");

        targetSelectionWindow.Content = null;
        targetSelectionWindow.Close();

        using var multiTargetViewModel = FindHuichunViewModel();
        var multiTargetWindow = new MainWindow(multiTargetViewModel);
        multiTargetWindow.ApplyTemplate();
        var multiTargetRoot = (FrameworkElement)multiTargetWindow.Content;
        var huichun = multiTargetViewModel.GeneralChoices.Single(choice => choice.SkillName == "回春");
        multiTargetViewModel.SelectGeneralChoiceCommand.Execute(huichun);
        AdvanceHuichunToPlay(multiTargetViewModel);
        var multiTargetEngine = Program.Engine(multiTargetViewModel);
        Program.Assert(ReachHuichunTargets(multiTargetViewModel),
            $"The WPF Huichun fixture did not expose two wounded targets (status={multiTargetEngine.State.Status}, turn={multiTargetEngine.State.TurnNumber}, hand={multiTargetViewModel.Hand.Count}, wounded={multiTargetEngine.CreateSnapshot(0, revealAll: true).Players.Count(player => player.IsAlive && player.Hp < player.MaxHp)}, prompt={multiTargetEngine.PendingDecision?.Kind}).");
        Program.Assert(multiTargetViewModel.ActiveSkillButtonText.Contains("选择手牌和目标后", StringComparison.Ordinal),
            "Huichun should start with an explicit hand-and-multi-target selection affordance.");
        var multiTargetBefore = multiTargetEngine.CreateSnapshot(0, revealAll: true);
        var multiTargetCards = multiTargetViewModel.Hand.OrderBy(card => card.Id).Take(2).ToArray();
        multiTargetViewModel.UseActiveSkillCommand.Execute(null);
        var multiTargetSeats = multiTargetViewModel.Seats
            .Where(seat => seat.IsLegalTarget)
            .OrderBy(seat => seat.Seat)
            .Take(2)
            .ToArray();
        Program.Assert(multiTargetSeats.Length == 2, "Huichun should publish two clickable target seats.");
        foreach (var card in multiTargetCards) multiTargetViewModel.SelectCardCommand.Execute(card);
        foreach (var seat in multiTargetSeats) multiTargetViewModel.SelectTargetCommand.Execute(seat);
        Program.Assert(multiTargetCards.All(card => card.IsSelected) &&
                       multiTargetSeats.All(seat => seat.IsSelectedTarget) &&
                       multiTargetViewModel.ActiveSkillButtonText.Contains("已选 2 张牌", StringComparison.Ordinal) &&
                       multiTargetViewModel.ActiveSkillButtonText.Contains("2 个目标", StringComparison.Ordinal),
            "Huichun did not retain two private cards and two selected targets.");
        Program.Render(multiTargetRoot, 1120, 740,
            Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill-multi-target-selection.png"));
        var multiTargetRevision = multiTargetEngine.Revision;
        multiTargetViewModel.UseActiveSkillCommand.Execute(null);
        var multiTargetAfter = multiTargetEngine.CreateSnapshot(0, revealAll: true);
        Program.Assert(multiTargetEngine.Revision == multiTargetRevision + 1 &&
                       multiTargetAfter.Players.Single(player => player.Seat == 0).Hand.Count ==
                       multiTargetBefore.Players.Single(player => player.Seat == 0).Hand.Count - 2,
            "The WPF Huichun selection did not commit the two-card multi-target command.");

        multiTargetWindow.Content = null;
        multiTargetWindow.Close();

        using var mashuViewModel = FindMashuViewModel();
        var mashuWindow = new MainWindow(mashuViewModel);
        mashuWindow.ApplyTemplate();
        var mashu = mashuViewModel.GeneralChoices.Single(choice => choice.SkillName == "马术");
        Program.Assert(
            mashu.SkillDescription.Contains("距离", StringComparison.Ordinal),
            "The WPF general choice must expose Mashu's public distance rule.");
        mashuViewModel.SelectGeneralChoiceCommand.Execute(mashu);
        Program.AdvanceToDecision(mashuViewModel);
        var humanSeat = mashuViewModel.Seats.Single(seat => seat.Seat == 0);
        var distanceTwoSeat = mashuViewModel.Seats.Single(seat => seat.Seat == 2);
        Program.Assert(humanSeat.SkillText.Contains("马术", StringComparison.Ordinal),
            "The table must keep the selected Mashu skill visible on the human seat.");
        Program.Assert(distanceTwoSeat.DistanceText == "距你 1",
            "The WPF public distance projection must reflect Mashu at the target seat.");
        Program.Render(
            (FrameworkElement)mashuWindow.Content,
            1120,
            740,
            Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill-mashu-distance.png"));
        mashuWindow.Content = null;
        mashuWindow.Close();

        using var qicaiViewModel = FindQicaiViewModel();
        var qicaiWindow = new MainWindow(qicaiViewModel);
        qicaiWindow.ApplyTemplate();
        var qicai = qicaiViewModel.GeneralChoices.Single(choice => choice.SkillName == "奇才");
        Program.Assert(
            qicai.SkillDescription.Contains("锦囊", StringComparison.Ordinal) &&
            qicai.SkillDescription.Contains("距离", StringComparison.Ordinal),
            "The WPF general choice must expose Qicai's trick-distance rule.");
        qicaiViewModel.SelectGeneralChoiceCommand.Execute(qicai);
        Program.AdvanceToDecision(qicaiViewModel);
        var qicaiEngine = Program.Engine(qicaiViewModel);
        var longRangeSnatch = qicaiEngine.GetHumanLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Snatch &&
            action.TargetSeats.SequenceEqual([2]) &&
            qicaiEngine.GetCombatDistance(0, 2) > 1);
        Program.Assert(longRangeSnatch is not null,
            "The WPF-backed Core must expose Qicai's distance-free Snatch target.");
        var qicaiCard = qicaiViewModel.Hand.Single(card => card.Id == longRangeSnatch!.CardId);
        qicaiViewModel.SelectCardCommand.Execute(qicaiCard);
        var qicaiTarget = qicaiViewModel.Seats.Single(seat => seat.Seat == 2);
        qicaiViewModel.SelectTargetCommand.Execute(qicaiTarget);
        Program.Assert(qicaiTarget.IsSelectedTarget && qicaiViewModel.CanPlaySelected,
            "The WPF target selector must accept Qicai's distance-free Snatch target.");
        Program.Render(
            (FrameworkElement)qicaiWindow.Content,
            1120,
            740,
            Path.Combine(Path.GetTempPath(), "card-ui-check", "active-skill-qicai-distance.png"));
        qicaiWindow.Content = null;
        qicaiWindow.Close();
    }

    internal static MainViewModel FindZhihengViewModel()
    {
        for (var seed = 1; seed <= 128; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (viewModel.GeneralChoices.Any(choice => choice.SkillName == "制衡"))
            {
                return viewModel;
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Zhiheng setup fixture.");
    }

    internal static MainViewModel FindRendeViewModel()
    {
        for (var seed = 1; seed <= 128; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (viewModel.GeneralChoices.Any(choice => choice.SkillName == "仁德"))
            {
                return viewModel;
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Rende setup fixture.");
    }

    internal static MainViewModel FindHuichunViewModel()
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (viewModel.GeneralChoices.Any(choice => choice.SkillName == "回春"))
            {
                viewModel.SelectGeneralChoiceCommand.Execute(
                    viewModel.GeneralChoices.Single(choice => choice.SkillName == "回春"));
                AdvanceHuichunToPlay(viewModel);
                if (ReachHuichunTargets(viewModel))
                {
                    viewModel.Dispose();
                    return new MainViewModel(
                        autoAdvance: false,
                        seed: seed,
                        showSetup: false,
                        saveStore: new MemorySaveStore(),
                        useExpandedContent: true)
                    {
                        IsMotionEnabled = false
                    };
                }
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Huichun setup fixture.");
    }

    internal static bool ReachHuichunTargets(MainViewModel viewModel)
    {
        var engine = Program.Engine(viewModel);
        for (var turn = 0; turn < 512 && !viewModel.HasGameOver; turn++)
        {
            if (ResolveHuichunPrompt(viewModel))
            {
                continue;
            }

            if (viewModel.CanStepAi)
            {
                viewModel.StepAiCommand.Execute(null);
                continue;
            }

            if (!viewModel.IsPlayPhase || !viewModel.CanEndTurn)
            {
                break;
            }

            var state = engine.CreateSnapshot(0, revealAll: true);
            var wounded = state.Players
                .Where(player => player.IsAlive && player.Hp < player.MaxHp)
                .ToArray();
            if (wounded.Length >= 2 && viewModel.CanUseActiveSkill)
            {
                return true;
            }

            var slash = engine.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.Slash &&
                    action.CardId is not null &&
                    action.TargetSeats.Count == 1 &&
                    action.PlayedCardKind is null)
                .OrderBy(action => action.TargetSeats[0])
                .ThenBy(action => action.CardId)
                .FirstOrDefault();
            if (slash is null)
            {
                viewModel.EndTurnCommand.Execute(null);
                continue;
            }

            var slashCard = viewModel.Hand.Single(card => card.Id == slash.CardId);
            viewModel.SelectCardCommand.Execute(slashCard);
            viewModel.SelectTargetCommand.Execute(
                viewModel.Seats.Single(seat => seat.Seat == slash.TargetSeats[0]));
            if (viewModel.CanPlaySelected)
            {
                viewModel.PlaySelectedCardCommand.Execute(null);
            }
            else
            {
                viewModel.ClearSelectionCommand.Execute(null);
                viewModel.EndTurnCommand.Execute(null);
            }
        }

        return false;
    }

    private static MainViewModel FindMashuViewModel()
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (viewModel.GeneralChoices.Any(choice => choice.SkillName == "马术"))
            {
                return viewModel;
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Mashu setup fixture.");
    }

    private static MainViewModel FindQicaiViewModel()
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            var qicai = viewModel.GeneralChoices.SingleOrDefault(choice => choice.SkillName == "奇才");
            if (qicai is not null)
            {
                viewModel.SelectGeneralChoiceCommand.Execute(qicai);
                Program.AdvanceToDecision(viewModel);
                var engine = Program.Engine(viewModel);
                if (engine.PendingDecision?.Kind == DecisionKind.PlayCard &&
                    engine.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Snatch &&
                        action.TargetSeats.SequenceEqual([2]) &&
                        engine.GetCombatDistance(0, 2) > 1))
                {
                    viewModel.Dispose();
                    return new MainViewModel(
                        autoAdvance: false,
                        seed: seed,
                        showSetup: false,
                        saveStore: new MemorySaveStore(),
                        useExpandedContent: true)
                    {
                        IsMotionEnabled = false
                    };
                }
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Qicai setup fixture.");
    }

    internal static void AdvanceHuichunToPlay(MainViewModel viewModel)
    {
        for (var step = 0; step < 10000 && viewModel.CanStepAi; step++)
        {
            viewModel.StepAiCommand.Execute(null);
        }
    }

    private static bool ResolveHuichunPrompt(MainViewModel viewModel)
    {
        if (viewModel.IsDyingSelectionPending)
        {
            viewModel.SelectDyingChoiceCommand.Execute(viewModel.DyingChoices.First());
            return true;
        }

        if (viewModel.IsHarvestSelectionPending)
        {
            viewModel.SelectHarvestChoiceCommand.Execute(viewModel.HarvestChoices.First());
            return true;
        }

        if (viewModel.IsTargetCardSelectionPending)
        {
            viewModel.SelectTargetCardChoiceCommand.Execute(viewModel.TargetCardChoices.First());
            return true;
        }

        if (viewModel.IsFireAttackSelectionPending)
        {
            viewModel.SelectFireAttackChoiceCommand.Execute(viewModel.FireAttackChoices.First());
            return true;
        }

        if (viewModel.IsNullificationSelectionPending)
        {
            viewModel.SelectNullificationChoiceCommand.Execute(viewModel.NullificationChoices.First());
            return true;
        }

        if (viewModel.IsSkillSelectionPending)
        {
            viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.First());
            return true;
        }

        if (viewModel.IsResponseSelectionPending)
        {
            if (viewModel.CanRespondDodge)
            {
                viewModel.RespondDodgeCommand.Execute(null);
            }
            else
            {
                viewModel.DeclineResponseCommand.Execute(null);
            }

            return true;
        }

        if (viewModel.IsDiscardSelectionPending)
        {
            Program.ResolveDiscard(viewModel);
            return true;
        }

        return false;
    }
}
