using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ClassicGeneralUiChecks
{
    public static void MultiSkillSelectionAndRestore(string output)
    {
        MainViewModel? selected = null;
        MemorySaveStore? store = null;
        for (var seed = 1; seed <= 512; seed++)
        {
            var candidateStore = new MemorySaveStore();
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: candidateStore,
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (candidate.GeneralChoices.Any(choice => choice.GeneralId == "classic:sima-yi"))
            {
                selected = candidate;
                store = candidateStore;
                break;
            }
            candidate.Dispose();
        }

        using var viewModel = selected ??
            throw new InvalidOperationException("Could not find a deterministic classic Sima Yi WPF fixture.");
        Program.Assert(viewModel.TableModes[0].ModeId == "identity:classic-8",
            "Classic identity must be the default expanded WPF mode.");
        var simaYi = viewModel.GeneralChoices.Single(choice => choice.GeneralId == "classic:sima-yi");
        Program.Assert(simaYi.SkillName == "反馈 / 鬼才" &&
                       simaYi.SkillDescription.Contains("反馈：", StringComparison.Ordinal) &&
                       simaYi.SkillDescription.Contains("鬼才：", StringComparison.Ordinal),
            "The general card must render all ordered skills instead of only the primary skill.");
        Program.Assert(simaYi.HealthText == "体力上限 4" &&
                       simaYi.HealthDescription.Contains("基础 3 + 1", StringComparison.Ordinal),
            "The classic Lord selection must explain base HP plus the Lord bonus.");
        Program.Assert(GeneralArt.HasPortrait(simaYi.GeneralId),
            "The classic Sima Yi id must resolve through the existing portrait aliases.");

        using var kongchengViewModel = FindGeneralChoice("standard:zhuge-liang");
        var zhugeLiang = kongchengViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "standard:zhuge-liang");
        Program.Assert(zhugeLiang.SkillDescription.Contains("【杀】或【决斗】", StringComparison.Ordinal),
            "The current classic selection card must describe the formal Kongcheng target restriction.");
        using var jianxiongViewModel = FindGeneralChoice("standard:cao-cao");
        var caoCao = jianxiongViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "standard:cao-cao");
        Program.Assert(caoCao.SkillDescription.Contains("造成此伤害的牌", StringComparison.Ordinal),
            "The current classic selection card must describe formal Jianxiong's damage-card scope.");
        using var zhihengDescriptionViewModel = FindGeneralChoice("classic:sun-quan");
        var sunQuan = zhihengDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:sun-quan");
        Program.Assert(sunQuan.SkillDescription.Contains("限一次", StringComparison.Ordinal) &&
                       sunQuan.SkillDescription.Contains("任意张牌", StringComparison.Ordinal),
            "The current classic selection card must describe formal Zhiheng's limit and card scope.");
        using var tianduDescriptionViewModel = FindGeneralChoice("classic:guo-jia");
        var guoJia = tianduDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:guo-jia");
        Program.Assert(guoJia.SkillName == "天妒 / 遗计" &&
                       guoJia.SkillDescription.Contains("判定牌生效后", StringComparison.Ordinal) &&
                       guoJia.HealthText == "体力上限 4",
            "The current classic Guo Jia card must render Tiandu, Yiji and the Lord health bonus.");

        using var yingziViewModel = FindGeneralChoice("standard:zhou-yu");
        var zhouYu = yingziViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "standard:zhou-yu");
        Program.Assert(zhouYu.SkillDescription.Contains("可以多摸一张牌", StringComparison.Ordinal),
            "The current classic selection card must describe Yingzi as optional.");
        yingziViewModel.SelectGeneralChoiceCommand.Execute(zhouYu);
        Program.AdvanceToDecision(yingziViewModel);
        var yingziEngine = Program.Engine(yingziViewModel);
        var beforeYingzi = yingziEngine.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        Program.Assert(yingziViewModel.IsSkillSelectionPending &&
                       yingziEngine.PendingDecision?.Kind == DecisionKind.Yingzi &&
                       yingziViewModel.SkillChoices.Count == 2,
            "The WPF must render both complete Yingzi choices at the draw-phase boundary.");
        var yingziWindow = new MainWindow(yingziViewModel);
        yingziWindow.ApplyTemplate();
        Program.Render((FrameworkElement)yingziWindow.Content, 1120, 740,
            Path.Combine(output, "71-classic-yingzi-choice.png"));
        var skipYingzi = yingziViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "yingzi-skip");
        yingziViewModel.SelectSkillChoiceCommand.Execute(skipYingzi);
        Program.Assert(!yingziViewModel.IsSkillSelectionPending &&
                       yingziEngine.CreateSnapshot(0, revealAll: true)
                           .Players.Single(player => player.Seat == 0).HandCount == beforeYingzi + 2,
            "The WPF Yingzi skip choice must continue with the normal draw count.");
        yingziWindow.Content = null;
        yingziWindow.Close();

        using var zhihengViewModel = FindClassicZhihengEquipmentViewModel();
        var zhihengWindow = new MainWindow(zhihengViewModel);
        zhihengWindow.ApplyTemplate();
        var zhihengRoot = (FrameworkElement)zhihengWindow.Content;
        var zhihengEngine = Program.Engine(zhihengViewModel);
        var equipmentAction = zhihengEngine.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Equip && action.CardId is not null);
        var equipmentCard = zhihengViewModel.Hand.Single(card => card.Id == equipmentAction.CardId);
        zhihengViewModel.SelectCardCommand.Execute(equipmentCard);
        zhihengViewModel.PlaySelectedCardCommand.Execute(null);
        Program.AdvanceToDecision(zhihengViewModel);
        var beforeZhiheng = zhihengEngine.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Program.Assert(beforeZhiheng.Equipment.Any(card => card.Id == equipmentCard.Id),
            "The WPF Zhiheng fixture did not equip its selectable public card.");

        zhihengViewModel.UseActiveSkillCommand.Execute(null);
        var equipmentChoice = zhihengViewModel.ActiveSkillEquipmentChoices.Single(choice =>
            choice.Cards.SequenceEqual([equipmentCard.Id]));
        Program.Assert(equipmentChoice.Description.Contains("装备", StringComparison.Ordinal),
            "Formal Zhiheng must render a dedicated equipment selection button.");
        zhihengViewModel.SelectActiveSkillEquipmentChoiceCommand.Execute(equipmentChoice);
        Program.Assert(zhihengViewModel.ActiveSkillButtonText.Contains("已选 1 张牌", StringComparison.Ordinal) &&
                       zhihengViewModel.ActiveSkillEquipmentChoices.Single().Description.Contains("已选择", StringComparison.Ordinal),
            "The equipment selection must participate in the shared active-skill draft.");
        Program.Render(zhihengRoot, 1120, 740,
            Path.Combine(output, "70-classic-zhiheng-equipment.png"));

        var zhihengRevision = zhihengEngine.Revision;
        zhihengViewModel.UseActiveSkillCommand.Execute(null);
        var afterZhiheng = zhihengEngine.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Program.Assert(zhihengEngine.Revision == zhihengRevision + 1 &&
                       afterZhiheng.Equipment.All(card => card.Id != equipmentCard.Id) &&
                       afterZhiheng.Hand.Count == beforeZhiheng.Hand.Count + 1 &&
                       !zhihengViewModel.CanUseActiveSkill,
            "The WPF equipment Zhiheng command must discard, draw and enforce the once-per-phase limit.");
        zhihengWindow.Content = null;
        zhihengWindow.Close();

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "69-classic-multi-skill-selection.png"));
        viewModel.SelectGeneralChoiceCommand.Execute(simaYi);
        Program.AdvanceToDecision(viewModel);
        var human = viewModel.Seats.Single(seat => seat.Seat == 0);
        Program.Assert(human.SkillName == "反馈 / 鬼才" &&
                       human.SkillText.Contains("反馈：", StringComparison.Ordinal) &&
                       human.SkillText.Contains("鬼才：", StringComparison.Ordinal) &&
                       human.Hp == 4 && human.MaxHp == 4,
            "The table seat must retain both classic skills and the selected 4/4 health.");

        viewModel.SaveGameCommand.Execute(null);
        Program.Assert(!viewModel.HasSaveError && viewModel.FlushPendingSave(), viewModel.SaveStatus);
        using var restored = new MainViewModel(
            autoAdvance: false,
            seed: 999,
            showSetup: true,
            saveStore: store!,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        restored.LoadManualGameCommand.Execute(null);
        var restoredHuman = restored.Seats.Single(seat => seat.Seat == 0);
        Program.Assert(!restored.HasSaveError &&
                       Program.Engine(restored).ModeId == "identity:classic-8" &&
                       restoredHuman.SkillName == "反馈 / 鬼才" &&
                       restoredHuman.Hp == 4 && restoredHuman.MaxHp == 4,
            "The WPF save loader did not reconstruct the exact classic content package.");

        window.Content = null;
        window.Close();
    }

    public static void TianduChoiceAndRestore(string output)
    {
        var (fixture, judgmentCardId) = FindTianduFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual,
            new(1, DateTimeOffset.UtcNow, false, fixture.CreateCheckpoint()));
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

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       Program.Engine(viewModel).PendingDecision?.Kind == DecisionKind.Tiandu &&
                       viewModel.SkillChoices.Count == 2,
            viewModel.SaveStatus);
        Program.Assert(viewModel.CurrentGuideTitle == "决定是否发动天妒" &&
                       viewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("进入你的手牌", StringComparison.Ordinal)),
            "The player guide must explain the restored Tiandu choice.");

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "72-classic-tiandu-choice.png"));
        var claim = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tiandu-claim");
        viewModel.SelectSkillChoiceCommand.Execute(claim);
        var engine = Program.Engine(viewModel);
        Program.Assert(!viewModel.IsSkillSelectionPending &&
                       engine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                           .Hand.Any(card => card.Id == judgmentCardId) &&
                       engine.Events.Any(item => item.Payload is JudgmentCardClaimedEvent
                       {
                           OwnerSeat: 0,
                           Skill: SkillKind.Tiandu,
                           Used: true
                       }),
            "The WPF Tiandu choice must claim the exact resolved judgment card.");
        window.Content = null;
        window.Close();
    }

    private static MainViewModel FindGeneralChoice(string generalId)
    {
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            if (candidate.GeneralChoices.Any(choice => choice.GeneralId == generalId))
            {
                return candidate;
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException($"Could not find a deterministic {generalId} WPF fixture.");
    }

    private static MainViewModel FindClassicZhihengEquipmentViewModel()
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            var sunQuan = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:sun-quan");
            if (sunQuan is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(sunQuan);
                Program.AdvanceToDecision(candidate);
                if (Program.Engine(candidate).GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Equip && action.CardId is not null))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Zhiheng equipment WPF fixture.");
    }

    private static (GameEngine Game, int JudgmentCardId) FindTianduFixture()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision?.ValidContentIds.Contains("classic:guo-jia") != true)
            {
                continue;
            }

            var selection = game.PendingDecision;
            if (!game.Submit(new SelectGeneralCommand(
                    0,
                    "classic:guo-jia",
                    game.Revision,
                    selection!.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted ||
                game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var lightning = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Lightning && action.CardId is not null);
            if (lightning is null || !game.Submit(new PlayCardCommand(
                    0,
                    lightning.CardId!.Value,
                    lightning.TargetSeats,
                    game.Revision,
                    game.PendingDecision!.PromptId)).Accepted)
            {
                continue;
            }

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var judgment = game.Events.Select(item => item.Payload)
                    .OfType<JudgmentResolvedEvent>()
                    .LastOrDefault(item =>
                        item.TargetSeat == 0 && item.Reason == JudgmentReasons.Lightning);
                if (judgment is { Succeeded: false, CardId: { } judgmentCardId } &&
                    game.PendingDecision?.Kind == DecisionKind.Tiandu)
                {
                    return (game, judgmentCardId);
                }

                var prompt = game.PendingDecision;
                GameCommand command = prompt?.Kind switch
                {
                    null => new AdvanceOneStepCommand(game.Revision),
                    DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                    DecisionKind.DiscardCards => new DiscardCardsCommand(
                        0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId,
                        game.Revision),
                    _ when prompt.PlayerSeat == 0 && prompt.Choices.Count > 0 =>
                        new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision),
                    _ => new AdvanceOneStepCommand(game.Revision)
                };
                if (!game.Submit(command).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Tiandu fixture.");
    }
}
