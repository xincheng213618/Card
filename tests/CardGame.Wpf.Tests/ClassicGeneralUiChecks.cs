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

        using var kongchengViewModel = FindGeneralChoice("classic:zhuge-liang");
        var zhugeLiang = kongchengViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhuge-liang");
        Program.Assert(zhugeLiang.SkillName == "观星 / 空城" &&
                       zhugeLiang.SkillDescription.Contains("牌堆顶", StringComparison.Ordinal) &&
                       zhugeLiang.SkillDescription.Contains("【杀】或【决斗】", StringComparison.Ordinal) &&
                       zhugeLiang.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(zhugeLiang.GeneralId),
            "The current classic Zhuge Liang card must render Guanxing, Kongcheng, Lord health and portrait aliasing.");
        using var jianxiongViewModel = FindGeneralChoice("classic:cao-cao");
        var caoCao = jianxiongViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:cao-cao");
        Program.Assert(caoCao.SkillName == "奸雄 / 护驾" &&
                       caoCao.SkillDescription.Contains("造成此伤害的牌", StringComparison.Ordinal) &&
                       caoCao.SkillDescription.Contains("其他魏势力角色", StringComparison.Ordinal) &&
                       caoCao.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(caoCao.GeneralId),
            "The current classic Cao Cao card must render Jianxiong, Hujia, Lord health and portrait aliasing.");
        using var jijiangDescriptionViewModel = FindGeneralChoice("classic:liu-bei");
        var liuBei = jijiangDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:liu-bei");
        Program.Assert(liuBei.SkillName == "仁德 / 激将" &&
                       liuBei.SkillDescription.Contains("交给一名其他角色", StringComparison.Ordinal) &&
                       liuBei.SkillDescription.Contains("其他蜀势力角色", StringComparison.Ordinal) &&
                       liuBei.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(liuBei.GeneralId),
            "The current classic Liu Bei card must render Rende, Jijiang, Lord health and portrait aliasing.");
        using var zhihengDescriptionViewModel = FindGeneralChoice("classic:sun-quan");
        var sunQuan = zhihengDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:sun-quan");
        Program.Assert(sunQuan.SkillName == "制衡 / 救援" &&
                       sunQuan.SkillDescription.Contains("限一次", StringComparison.Ordinal) &&
                       sunQuan.SkillDescription.Contains("任意张牌", StringComparison.Ordinal) &&
                       sunQuan.SkillDescription.Contains("其他吴势力角色", StringComparison.Ordinal) &&
                       sunQuan.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(sunQuan.GeneralId),
            "The current classic Sun Quan card must render Zhiheng, Jiuyuan, Lord health and portrait aliasing.");
        var jiuyuanWindow = new MainWindow(zhihengDescriptionViewModel);
        jiuyuanWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)jiuyuanWindow.Content,
            1120,
            740,
            Path.Combine(output, "80-classic-jiuyuan-card.png"));
        using var kujinDescriptionViewModel = FindGeneralChoice("classic:huang-gai");
        var huangGai = kujinDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:huang-gai");
        Program.Assert(huangGai.Name == "黄盖" &&
                       huangGai.Kingdom == "吴" &&
                       huangGai.SkillName == "苦肉" &&
                       huangGai.SkillDescription.Contains("体力大于 0", StringComparison.Ordinal) &&
                       huangGai.SkillDescription.Contains("摸两张牌", StringComparison.Ordinal) &&
                       huangGai.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(huangGai.GeneralId),
            "The current classic Huang Gai card must render Wu, Kujin, Lord health and portrait aliasing.");
        var kujinWindow = new MainWindow(kujinDescriptionViewModel);
        kujinWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)kujinWindow.Content,
            1120,
            740,
            Path.Combine(output, "81-classic-huang-gai-card.png"));
        kujinWindow.Content = null;
        kujinWindow.Close();
        using var tianduDescriptionViewModel = FindGeneralChoice("classic:guo-jia");
        var guoJia = tianduDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:guo-jia");
        Program.Assert(guoJia.SkillName == "天妒 / 遗计" &&
                       guoJia.SkillDescription.Contains("判定牌生效后", StringComparison.Ordinal) &&
                       guoJia.HealthText == "体力上限 4",
            "The current classic Guo Jia card must render Tiandu, Yiji and the Lord health bonus.");

        using var yingziViewModel = FindGeneralChoice("classic:zhou-yu");
        var zhouYu = yingziViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhou-yu");
        Program.Assert(zhouYu.SkillName == "英姿 / 反间" &&
                       zhouYu.SkillDescription.Contains("可以多摸一张牌", StringComparison.Ordinal) &&
                       zhouYu.SkillDescription.Contains("选择一种花色", StringComparison.Ordinal),
            "The current classic selection card must describe optional Yingzi and formal Fanjian.");
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
        Program.AdvanceToDecision(yingziViewModel);
        Program.Assert(yingziViewModel.CanUseActiveSkill &&
                       yingziViewModel.ActiveSkillEntryText.Contains("反间", StringComparison.Ordinal),
            "Classic Zhou Yu must expose Fanjian at the WPF play boundary.");
        yingziViewModel.UseActiveSkillCommand.Execute(null);
        var fanjianTarget = yingziViewModel.Seats.First(seat => seat.IsLegalTarget);
        yingziViewModel.SelectTargetCommand.Execute(fanjianTarget);
        Program.Assert(yingziViewModel.CanConfirmActiveSkill &&
                       fanjianTarget.IsSelectedTarget &&
                       yingziViewModel.PlayButtonText == "发动反间" &&
                       yingziViewModel.ActiveSkillButtonText.Contains("1 个目标", StringComparison.Ordinal),
            "Fanjian must reuse the target-only active-skill draft without asking for a source card.");
        Program.Render((FrameworkElement)yingziWindow.Content, 1120, 740,
            Path.Combine(output, "73-classic-fanjian-target.png"));
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

    public static void FanjianChoiceAndRestore(string output)
    {
        var fixture = FindFanjianTargetFixture();
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

        var engine = Program.Engine(viewModel);
        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision?.Kind == DecisionKind.Fanjian &&
                       viewModel.SkillChoices.Count == 4 &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Cards.Count == 0 &&
                           choice.Parameters.GetValueOrDefault("action") == "fanjian-choose-suit"),
            viewModel.SaveStatus);
        Program.Assert(viewModel.CurrentGuideTitle == "为反间选择一种花色" &&
                       viewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("尚未公开", StringComparison.Ordinal)),
            "The player guide must explain that Fanjian chooses a suit before the random reveal.");

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "74-classic-fanjian-choice.png"));
        var chosen = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("suit") == "Diamond");
        viewModel.SelectSkillChoiceCommand.Execute(chosen);
        var reveal = engine.Events.Select(item => item.Payload)
            .OfType<FanjianCardRevealedEvent>()
            .LastOrDefault();
        Program.Assert(reveal is { TargetSeat: 0, ChosenSuit: Suit.Diamond } &&
                       engine.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                           .Hand.Any(card => card.Id == reveal.CardId),
            "The WPF Fanjian suit button must receive and reveal the exact transferred card.");
        window.Content = null;
        window.Close();
    }

    public static void GuanxingChoiceAndRestore(string output)
    {
        var fixture = FindGuanxingFixture();
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

        var engine = Program.Engine(viewModel);
        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision?.Kind == DecisionKind.Guanxing &&
                       viewModel.SkillChoices.Count == 2 &&
                       viewModel.SkillChoices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                           .OrderBy(action => action, StringComparer.Ordinal)
                           .SequenceEqual(["guanxing-skip", "guanxing-use"]),
            viewModel.SaveStatus);
        Program.Assert(viewModel.CurrentGuideTitle == "排列观星看到的牌" &&
                       viewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("最底层", StringComparison.Ordinal)),
            "The player guide must explain Guanxing top-first and bottom-first ordering.");

        var offerWindow = new MainWindow(viewModel);
        offerWindow.ApplyTemplate();
        Program.Render((FrameworkElement)offerWindow.Content, 1120, 740,
            Path.Combine(output, "75-classic-guanxing-offer.png"));
        var use = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "guanxing-use");
        viewModel.SelectSkillChoiceCommand.Execute(use);
        var viewedIds = engine.PendingDecision?.ValidCardIds.ToArray() ?? [];
        Program.Assert(viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.Guanxing,
                           IsPrivate: true,
                           ValidCardIds.Count: 5
                       } &&
                       viewModel.SkillChoices.Count == 6 &&
                       viewModel.SkillChoices.Count(choice => choice.Cards.Count == 1) == 5 &&
                       viewModel.SkillChoices.Count(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "guanxing-finish-top") == 1,
            "Using Guanxing must render five exact private card choices plus the finish-top action.");
        viewModel.SaveGameCommand.Execute(null);
        Program.Assert(!viewModel.HasSaveError && viewModel.FlushPendingSave(), viewModel.SaveStatus);
        offerWindow.Content = null;
        offerWindow.Close();

        using var restored = new MainViewModel(
            autoAdvance: false,
            seed: 999,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        restored.LoadManualGameCommand.Execute(null);
        var restoredEngine = Program.Engine(restored);
        Program.Assert(!restored.HasSaveError &&
                       restored.IsSkillSelectionPending &&
                       restoredEngine.PendingDecision?.Kind == DecisionKind.Guanxing &&
                       restoredEngine.PendingDecision.ValidCardIds.SequenceEqual(viewedIds) &&
                       restored.SkillChoices.Where(choice => choice.Cards.Count == 1)
                           .SelectMany(choice => choice.Cards)
                           .SequenceEqual(viewedIds),
            "A WPF manual save must restore the exact private Guanxing ordering prompt.");

        var orderingWindow = new MainWindow(restored);
        orderingWindow.ApplyTemplate();
        Program.Render((FrameworkElement)orderingWindow.Content, 1120, 740,
            Path.Combine(output, "76-classic-guanxing-order.png"));
        var firstTop = restored.SkillChoices.First(choice => choice.Cards.Count == 1);
        restored.SelectSkillChoiceCommand.Execute(firstTop);
        Program.Assert(restoredEngine.PendingDecision is
        {
            Kind: DecisionKind.Guanxing,
            ValidCardIds.Count: 4
        } &&
                       restored.SkillChoices.Count == 5 &&
                       restored.SkillChoices.Count(choice => choice.Cards.Count == 1) == 4,
            "Selecting one top card through WPF must advance the private Guanxing ordering draft.");
        orderingWindow.Content = null;
        orderingWindow.Close();
    }

    public static void HujiaChoiceAndRestore(string output)
    {
        var fixture = FindHujiaFixture();
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

        var engine = Program.Engine(viewModel);
        var choice = viewModel.ResponseChoices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "hujia-request");
        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsResponseSelectionPending &&
                       engine.PendingDecision?.Kind == DecisionKind.RespondDodge &&
                       viewModel.CurrentGuideTitle == "决定是否发动护驾" &&
                       viewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("仍可使用自己的闪", StringComparison.Ordinal)) &&
                       viewModel.TableDecisionTitle.Contains("响应护驾", StringComparison.Ordinal),
            viewModel.SaveStatus);

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "77-classic-hujia-request.png"));
        viewModel.SelectResponseChoiceCommand.Execute(choice);
        Program.Assert(viewModel.ResponseChoices.All(candidate =>
                           candidate.Parameters.GetValueOrDefault("response") != "hujia-request") &&
                       engine.Events.Any(item => item.Payload is HujiaRequestedEvent
                       {
                           OwnerSeat: 0,
                           CandidateSeats: { Count: > 0 }
                       }),
            "The WPF Hujia action must enter the ordered private Wei-response continuation.");
        window.Content = null;
        window.Close();
    }

    public static void JijiangActiveAction(string output)
    {
        using var viewModel = FindJijiangViewModel();
        var engine = Program.Engine(viewModel);
        var actions = viewModel.HumanActiveSkillActions;
        Program.Assert(actions.Select(action => action.Skill)
                           .SequenceEqual([SkillKind.Rende, SkillKind.Jijiang]) &&
                       viewModel.AdditionalActiveSkillActions is [{ Skill: SkillKind.Jijiang }],
            "Classic Liu Bei must publish separate Rende and Jijiang toolbar actions in stable order.");
        var jijiang = actions.Single(action => action.Skill == SkillKind.Jijiang);
        Program.Assert(jijiang.SelectableCardIds.Count == 0 &&
                       jijiang.SelectableTargetSeats.Count > 0 &&
                       jijiang is { MinTargetCount: 1, MaxTargetCount: 1 },
            "The WPF Jijiang action must expose a target-only typed draft.");

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "78-classic-jijiang-entry.png"));
        var visibleSkillButtons = Program.Find<System.Windows.Controls.Button>(root)
            .Where(button => button.Visibility == Visibility.Visible && button.ActualHeight > 0)
            .Select(button => button.Content as string)
            .Where(text => text is not null)
            .ToArray();
        Program.Assert(visibleSkillButtons.Contains("发动【仁德】", StringComparer.Ordinal) &&
                       visibleSkillButtons.Contains("发动【激将】", StringComparer.Ordinal),
            "The play toolbar must keep both of Liu Bei's active skills directly visible.");

        viewModel.SelectActiveSkillCommand.Execute(jijiang);
        Program.Assert(viewModel.IsActiveSkillSelectionPending &&
                       viewModel.PlayButtonText == "发动激将" &&
                       viewModel.CurrentGuideTitle == "选择【激将】的牌和目标" &&
                       viewModel.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat)
                           .Order()
                           .SequenceEqual(jijiang.SelectableTargetSeats.Order()),
            "Choosing the Jijiang entry must switch the shared draft to Jijiang's own target set.");
        var target = viewModel.Seats.First(seat => seat.IsLegalTarget);
        viewModel.SelectTargetCommand.Execute(target);
        Program.Assert(viewModel.CanConfirmActiveSkill && target.IsSelectedTarget &&
                       viewModel.ActiveSkillButtonText.Contains("1 个目标", StringComparison.Ordinal),
            "Selecting one Jijiang target must enable the shared primary confirmation.");
        Program.Render(root, 1120, 740, Path.Combine(output, "79-classic-jijiang-target.png"));

        var revision = engine.Revision;
        viewModel.ConfirmSelectedCommand.Execute(null);
        Program.Assert(engine.Revision == revision + 1 &&
                       engine.AcceptedCommands.Last() is UseSkillCommand
                       {
                           Skill: SkillKind.Jijiang,
                           CardIds.Count: 0,
                           TargetSeats.Count: 1
                       } command &&
                       command.TargetSeats[0] == target.Seat &&
                       engine.Events.Any(item => item.Payload is JijiangRequestedEvent
                       {
                           OwnerSeat: 0,
                           IsActiveUse: true,
                           CandidateSeats.Count: > 0
                       }) &&
                       !viewModel.IsActiveSkillSelectionPending,
            "The Jijiang target draft must submit exactly one typed request without paying a Liu Bei hand card.");
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

    private static MainViewModel FindJijiangViewModel()
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
            var liuBei = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:liu-bei");
            if (liuBei is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(liuBei);
                Program.AdvanceToDecision(candidate);
                if (candidate.HumanActiveSkillActions.Any(action =>
                        action.Skill == SkillKind.Jijiang && action.SelectableTargetSeats.Count > 0))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Jijiang WPF fixture.");
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

    private static GameEngine FindFanjianTargetFixture()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision?.Kind == DecisionKind.Fanjian)
                {
                    return game;
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
                    _ when prompt.Choices.Count > 0 => new AnswerPromptCommand(
                        0,
                        prompt.PromptId,
                        prompt.Choices[0].Id,
                        game.Revision),
                    _ => new AdvanceOneStepCommand(game.Revision)
                };
                if (!game.Submit(command).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic human-target Fanjian WPF fixture.");
    }

    private static GameEngine FindGuanxingFixture()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 4_096; seed++)
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
                game.PendingDecision?.ValidContentIds.Contains("classic:zhuge-liang") != true)
            {
                continue;
            }

            var selection = game.PendingDecision;
            if (game.Submit(new SelectGeneralCommand(
                    0,
                    "classic:zhuge-liang",
                    game.Revision,
                    selection!.PromptId)).Accepted &&
                game.Submit(new AdvanceCommand(game.Revision)).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.Guanxing)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Guanxing fixture.");
    }

    private static GameEngine FindHujiaFixture()
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
                game.PendingDecision?.ValidContentIds.Contains("classic:cao-cao") != true)
            {
                continue;
            }

            var selection = game.PendingDecision!;
            if (!game.Submit(new SelectGeneralCommand(
                    0,
                    "classic:cao-cao",
                    game.Revision,
                    selection.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted)
            {
                continue;
            }

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "hujia-request"))
                {
                    return game;
                }

                GameCommand command = prompt?.Kind switch
                {
                    null => new AdvanceOneStepCommand(game.Revision),
                    DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                    DecisionKind.DiscardCards => new DiscardCardsCommand(
                        0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId,
                        game.Revision),
                    _ => new AnswerPromptCommand(
                        0,
                        prompt.PromptId,
                        prompt.Choices.FirstOrDefault(choice =>
                            choice.Parameters.Values.Any(value =>
                                value.StartsWith("skip", StringComparison.Ordinal) ||
                                value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))?.Id ??
                            prompt.Choices.First().Id,
                        game.Revision)
                };
                if (!game.Submit(command).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Hujia fixture.");
    }
}
