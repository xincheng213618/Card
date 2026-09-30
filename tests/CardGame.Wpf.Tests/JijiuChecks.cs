using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class JijiuChecks
{

    internal static (MainViewModel ViewModel, GeneralChoiceViewModel General) FindFixture()
    {
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var viewModel = ActiveSkillChecks.CreateShowcase(seed);
            var general = viewModel.GeneralChoices.SingleOrDefault(choice => choice.SkillName == "急救");
            if (general is null)
            {
                viewModel.Dispose();
                continue;
            }

            viewModel.SelectGeneralChoiceCommand.Execute(general);
            Program.AdvanceToDecision(viewModel);
            if (!viewModel.Hand.Any(card => card.Name != "桃" && card.SuitGlyph is "♥" or "♦"))
            {
                viewModel.Dispose();
                continue;
            }

            if (AdvanceUntilConvertedDying(viewModel))
            {
                return (viewModel, general);
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Jijiu dying fixture.");
    }

    private static bool AdvanceUntilConvertedDying(MainViewModel viewModel)
    {
        for (var step = 0; step < 12_000 && !viewModel.HasGameOver; step++)
        {
            if (viewModel.IsDyingSelectionPending)
            {
                if (viewModel.DyingChoices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "peach" &&
                        choice.Description.Contains("当作【桃】", StringComparison.Ordinal)))
                {
                    return true;
                }

                var letDie = viewModel.DyingChoices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "let-die") ??
                    viewModel.DyingChoices.First();
                viewModel.SelectDyingChoiceCommand.Execute(letDie);
                continue;
            }

            if (viewModel.IsGeneralSelectionPending)
            {
                viewModel.SelectGeneralChoiceCommand.Execute(viewModel.GeneralChoices[0]);
                continue;
            }

            if (viewModel.IsDiscardSelectionPending)
            {
                Program.ResolveDiscard(viewModel);
                continue;
            }

            if (viewModel.IsResponseSelectionPending)
            {
                var pass = viewModel.ResponseChoices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "take-damage");
                if (pass is not null)
                {
                    viewModel.SelectResponseChoiceCommand.Execute(pass);
                }
                else
                {
                    viewModel.DeclineResponseCommand.Execute(null);
                }

                continue;
            }

            if (viewModel.IsHarvestSelectionPending)
            {
                viewModel.SelectHarvestChoiceCommand.Execute(viewModel.HarvestChoices.First());
                continue;
            }

            if (viewModel.IsTargetCardSelectionPending)
            {
                viewModel.SelectTargetCardChoiceCommand.Execute(viewModel.TargetCardChoices.First());
                continue;
            }

            if (viewModel.IsFireAttackSelectionPending)
            {
                viewModel.SelectFireAttackChoiceCommand.Execute(viewModel.FireAttackChoices.First());
                continue;
            }

            if (viewModel.IsNullificationSelectionPending)
            {
                viewModel.SelectNullificationChoiceCommand.Execute(viewModel.NullificationChoices.First());
                continue;
            }

            if (viewModel.IsSkillSelectionPending)
            {
                viewModel.SelectSkillChoiceCommand.Execute(viewModel.SkillChoices.First());
                continue;
            }

            if (viewModel.HasTargetCombinationChoices)
            {
                viewModel.SelectTargetCombinationChoiceCommand.Execute(viewModel.TargetCombinationChoices.First());
                continue;
            }

            if (viewModel.HasPublicTargetChoices)
            {
                viewModel.SelectPublicTargetChoiceCommand.Execute(viewModel.PublicTargetChoices.First());
                continue;
            }

            if (viewModel.CanEndTurn)
            {
                viewModel.EndTurnCommand.Execute(null);
                continue;
            }

            if (viewModel.CanStepAi)
            {
                viewModel.StepAiCommand.Execute(null);
                continue;
            }

            return false;
        }

        return false;
    }
}
