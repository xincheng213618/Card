using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class JijiuChecks
{
    public static void ControlsAndDying()
    {
        var (viewModel, general) = FindFixture();
        using (viewModel)
        {
            var window = new MainWindow(viewModel);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;

            Program.Assert(general.SkillDescription.Contains("红色牌", StringComparison.Ordinal),
                "The WPF general choice must expose Jijiu's red-card rescue rule.");

            var engine = Program.Engine(viewModel);
            Program.Assert(engine.State.Players.Single(player => player.Seat == 0).Skills?
                    .Any(skill => skill.Kind == SkillKind.Jijiu) == true,
                "The selected WPF general must project Jijiu into the Core engine.");
            var prompt = viewModel.DyingChoices.First(choice =>
                choice.Parameters.GetValueOrDefault("response") == "peach" &&
                choice.Description.Contains("当作【桃】", StringComparison.Ordinal));
            var physicalCard = viewModel.Hand.Single(card => prompt.Cards.Contains(card.Id));
            Program.Assert(physicalCard.Name != "桃" &&
                           physicalCard.SuitGlyph is "♥" or "♦",
                "The WPF rescue choice must point at a red physical non-Peach card.");

            var output = Path.Combine(Path.GetTempPath(), "card-ui-check");
            Directory.CreateDirectory(output);
            Program.Render(root, 1120, 740, Path.Combine(output, "jijiu-dying.png"));
            Program.Assert(Program.Find<TextBlock>(root).Any(text =>
                    text.Text.Contains("当作【桃】", StringComparison.Ordinal)),
                "The converted Jijiu action was not rendered in the dying choice panel.");

            var revision = engine.Revision;
            viewModel.SelectDyingChoiceCommand.Execute(prompt);
            Program.Assert(engine.Revision == revision + 1 && !viewModel.IsDyingSelectionPending,
                "The WPF Jijiu choice did not commit through the normal prompt command.");
            Program.Assert(viewModel.BattleCues.Any(cue =>
                    cue.Kind == CardGame.Wpf.Presentation.BattleCueKind.Response &&
                    cue.Label == $"打出{physicalCard.Name}"),
                "WPF battle feedback must identify the converted physical response card.");
            Program.Assert(!viewModel.PromptText.Contains("未执行", StringComparison.Ordinal),
                viewModel.PromptText);

            window.Content = null;
            window.Close();
        }
    }

    public static void EquipmentControls(string output)
    {
        var fixture = JijiuEquipmentScenario.Find();
        var owner = fixture.CreateSnapshot(0, revealAll: true).Players[0];
        var equipmentChoice = fixture.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "peach" &&
            choice.Cards.Count == 1 &&
            owner.Equipment.Any(card => card.Id == choice.Cards[0]));
        var equipment = owner.Equipment.Single(card => card.Id == equipmentChoice.Cards[0]);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            fixture.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: fixture.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsDyingSelectionPending &&
                       viewModel.Hand.All(card => card.Id != equipment.Id) &&
                       viewModel.DyingChoices.Any(choice => choice.Id == equipmentChoice.Id),
            "The WPF must expose equipped Jijiu as a central private dying choice, not a hand card.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "109-classic-jijiu-equipment-response.png"));
        Program.Assert(Program.Find<TextBlock>(root).Any(text =>
                text.Text.Contains(equipment.DisplayName, StringComparison.Ordinal) &&
                text.Text.Contains("当作【桃】", StringComparison.Ordinal)),
            "The equipped Jijiu conversion text was not rendered in the dying choice panel.");

        viewModel.SelectDyingChoiceCommand.Execute(
            viewModel.DyingChoices.Single(choice => choice.Id == equipmentChoice.Id));
        var engine = Program.Engine(viewModel);
        Program.Assert(engine.Events.Any(item =>
                           item.Payload is DyingResponseEvent response &&
                           response.ResponderSeat == 0 &&
                           response.PeachCardId == equipment.Id &&
                           response.UsedPeachPhysicalCardKind == equipment.Kind) &&
                       engine.CardMovements.Any(movement =>
                           movement.CardId == equipment.Id &&
                           movement.From == CardLocation.Equipment(0) &&
                           movement.To == CardLocation.Processing &&
                           movement.Reason == CardMoveReasons.Use),
            "The WPF central dying choice did not commit the equipped Jijiu physical card.");

        window.Content = null;
        window.Close();
    }

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
