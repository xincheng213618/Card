using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ClassicGeneralUiChecks
{
    public static void SelectableDeckExpansion(string output)
    {
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: 137160,
            showSetup: true,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        Program.Assert(viewModel.IsClassicIdentityModeSelection &&
                       viewModel.DeckOptions.Select(option => option.DeckId).SequenceEqual([
                           "classic:standard-deck",
                           "classic:standard-108"]) &&
                       viewModel.SelectedDeck?.DeckId == "classic:standard-deck",
            "Expanded classic setup must default to military 160 and expose standard 108 as an explicit alternative.");

        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        Program.Render(
            (FrameworkElement)window.Content,
            1120,
            740,
            Path.Combine(output, "125-classic-deck-selection.png"));

        viewModel.SelectedDeck = viewModel.DeckOptions.Single(option =>
            option.DeckId == "classic:standard-108");
        viewModel.StartNewGameCommand.Execute(null);
        Program.Assert(Program.Engine(viewModel).CreateCheckpoint().Options.DeckId == "classic:standard-108",
            "Starting from the standard option must persist its exact deck id in GameOptions and checkpoints.");

        viewModel.NewGameCommand.Execute(null);
        viewModel.SelectedDeck = viewModel.DeckOptions.Single(option =>
            option.DeckId == "classic:standard-deck");
        viewModel.StartNewGameCommand.Execute(null);
        Program.Assert(Program.Engine(viewModel).CreateCheckpoint().Options.DeckId == "classic:standard-deck",
            "Starting from the military option must persist its exact 160-card deck id.");
        window.Content = null;
        window.Close();
    }

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
        using var qixiDescriptionViewModel = FindGeneralChoice("classic:gan-ning");
        var ganNing = qixiDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:gan-ning");
        Program.Assert(ganNing.Name == "甘宁" &&
                       ganNing.Kingdom == "吴" &&
                       ganNing.SkillName == "奇袭" &&
                       ganNing.SkillDescription.Contains("黑色牌", StringComparison.Ordinal) &&
                       ganNing.SkillDescription.Contains("过河拆桥", StringComparison.Ordinal) &&
                       ganNing.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(ganNing.GeneralId),
            "The current classic Gan Ning card must render Wu, Qixi, Lord health and portrait aliasing.");
        var qixiDescriptionWindow = new MainWindow(qixiDescriptionViewModel);
        qixiDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)qixiDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "82-classic-gan-ning-card.png"));
        qixiDescriptionWindow.Content = null;
        qixiDescriptionWindow.Close();
        using var kejiViewModel = FindGeneralChoice("classic:lu-meng");
        var luMeng = kejiViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:lu-meng");
        Program.Assert(luMeng.Name == "吕蒙" &&
                       luMeng.Kingdom == "吴" &&
                       luMeng.SkillName == "克己" &&
                       luMeng.SkillDescription.Contains("未于本回合出牌阶段", StringComparison.Ordinal) &&
                       luMeng.SkillDescription.Contains("跳过弃牌阶段", StringComparison.Ordinal) &&
                       luMeng.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(luMeng.GeneralId),
            "The current classic Lu Meng card must render Wu, Keji, Lord health and portrait aliasing.");
        var kejiWindow = new MainWindow(kejiViewModel);
        kejiWindow.ApplyTemplate();
        var kejiRoot = (FrameworkElement)kejiWindow.Content;
        Program.Render(
            kejiRoot,
            1120,
            740,
            Path.Combine(output, "84-classic-lu-meng-card.png"));
        kejiViewModel.SelectGeneralChoiceCommand.Execute(luMeng);
        Program.AdvanceToDecision(kejiViewModel);
        var kejiEngine = Program.Engine(kejiViewModel);
        var handBeforeKeji = kejiEngine.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        Program.Assert(kejiEngine.PendingDecision?.Kind == DecisionKind.PlayCard &&
                       kejiViewModel.CanEndTurn,
            "The Lu Meng WPF fixture must reach the human play phase.");
        kejiViewModel.EndTurnCommand.Execute(null);
        Program.Assert(kejiViewModel.IsSkillSelectionPending &&
                       kejiEngine.PendingDecision?.Kind == DecisionKind.Keji &&
                       kejiViewModel.SkillChoices.Count == 2 &&
                       kejiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "keji-use") &&
                       kejiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "keji-skip") &&
                       kejiViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("保留全部手牌", StringComparison.Ordinal)),
            "The WPF must render both Keji choices and explain the retained-hand result.");
        Program.Render(
            kejiRoot,
            1120,
            740,
            Path.Combine(output, "85-classic-keji-choice.png"));
        var useKeji = kejiViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "keji-use");
        kejiViewModel.SelectSkillChoiceCommand.Execute(useKeji);
        Program.Assert(!kejiViewModel.IsSkillSelectionPending &&
                       kejiEngine.PendingDecision is null &&
                       kejiEngine.State.Phase == TurnPhase.NotStarted &&
                       kejiEngine.CreateSnapshot(0, revealAll: true)
                           .Players.Single(player => player.Seat == 0).HandCount == handBeforeKeji &&
                       kejiEngine.Events.Any(item => item.Payload is PhaseSkillResolvedEvent
                       {
                           SourceSeat: 0,
                           Skill: SkillKind.Keji,
                           Phase: TurnPhase.Discard,
                           Used: true
                       }),
            "The WPF Keji choice must retain the full hand and end the turn through the shared command boundary.");
        kejiWindow.Content = null;
        kejiWindow.Close();
        using var tuxiViewModel = FindGeneralChoice("classic:zhang-liao");
        var zhangLiao = tuxiViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhang-liao");
        Program.Assert(zhangLiao.Name == "张辽" &&
                       zhangLiao.Kingdom == "魏" &&
                       zhangLiao.SkillName == "突袭" &&
                       zhangLiao.SkillDescription.Contains("至多两名其他角色", StringComparison.Ordinal) &&
                       zhangLiao.SkillDescription.Contains("各一张手牌", StringComparison.Ordinal) &&
                       zhangLiao.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(zhangLiao.GeneralId),
            "The current classic Zhang Liao card must render Wei, Tuxi, Lord health and portrait aliasing.");
        var tuxiWindow = new MainWindow(tuxiViewModel);
        tuxiWindow.ApplyTemplate();
        var tuxiRoot = (FrameworkElement)tuxiWindow.Content;
        Program.Render(
            tuxiRoot,
            1120,
            740,
            Path.Combine(output, "86-classic-zhang-liao-card.png"));
        tuxiViewModel.SelectGeneralChoiceCommand.Execute(zhangLiao);
        Program.AdvanceToDecision(tuxiViewModel);
        var tuxiEngine = Program.Engine(tuxiViewModel);
        var tuxiPrompt = tuxiEngine.PendingDecision;
        var useTuxi = tuxiViewModel.SkillChoices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tuxi-use" &&
            choice.Targets.Count == 2);
        var tuxiBefore = tuxiEngine.CreateSnapshot(0, revealAll: true);
        var tuxiSourceBefore = tuxiBefore.Players.Single(player => player.Seat == 0).HandCount;
        var tuxiTargetsBefore = useTuxi.Targets.ToDictionary(
            seat => seat,
            seat => tuxiBefore.Players.Single(player => player.Seat == seat).HandCount);
        Program.Assert(tuxiViewModel.IsSkillSelectionPending &&
                       tuxiPrompt?.Kind == DecisionKind.Tuxi &&
                       tuxiViewModel.SkillChoices.Count(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "tuxi-use" &&
                           choice.Targets.Count is 1 or 2) > 0 &&
                       tuxiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "tuxi-skip") &&
                       tuxiViewModel.CurrentGuideTitle == "选择突袭目标" &&
                       tuxiViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("一至两名", StringComparison.Ordinal)),
            "The WPF must render Tuxi target combinations, ordinary draw and private guidance.");
        Program.Render(
            tuxiRoot,
            1120,
            740,
            Path.Combine(output, "87-classic-tuxi-choice.png"));
        tuxiViewModel.SelectSkillChoiceCommand.Execute(useTuxi);
        var tuxiAfter = tuxiEngine.CreateSnapshot(0, revealAll: true);
        Program.Assert(!tuxiViewModel.IsSkillSelectionPending &&
                       tuxiEngine.PendingDecision is null &&
                       tuxiEngine.State.Phase == TurnPhase.Play &&
                       tuxiAfter.Players.Single(player => player.Seat == 0).HandCount == tuxiSourceBefore + 2 &&
                       useTuxi.Targets.All(seat =>
                           tuxiAfter.Players.Single(player => player.Seat == seat).HandCount ==
                           tuxiTargetsBefore[seat] - 1) &&
                       tuxiEngine.Events.Any(item => item.Payload is HandCardsGainedBySkillEvent
                       {
                           SourceSeat: 0,
                           Skill: SkillKind.Tuxi,
                           Used: true,
                           CardCount: 2
                       }),
            "The WPF Tuxi choice must gain one hidden hand card from each selected target.");
        tuxiWindow.Content = null;
        tuxiWindow.Close();
        using var luoyiViewModel = FindGeneralChoice("classic:xu-chu");
        var xuChu = luoyiViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:xu-chu");
        Program.Assert(xuChu.Name == "许褚" &&
                       xuChu.Kingdom == "魏" &&
                       xuChu.SkillName == "裸衣" &&
                       xuChu.SkillDescription.Contains("少摸一张牌", StringComparison.Ordinal) &&
                       xuChu.SkillDescription.Contains("伤害+1", StringComparison.Ordinal) &&
                       xuChu.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(xuChu.GeneralId),
            "The current classic Xu Chu card must render Wei, Luoyi, Lord health and portrait aliasing.");
        var luoyiWindow = new MainWindow(luoyiViewModel);
        luoyiWindow.ApplyTemplate();
        var luoyiRoot = (FrameworkElement)luoyiWindow.Content;
        Program.Render(
            luoyiRoot,
            1120,
            740,
            Path.Combine(output, "88-classic-xu-chu-card.png"));
        luoyiViewModel.SelectGeneralChoiceCommand.Execute(xuChu);
        Program.AdvanceToDecision(luoyiViewModel);
        var luoyiEngine = Program.Engine(luoyiViewModel);
        var luoyiBefore = luoyiEngine.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        Program.Assert(luoyiViewModel.IsSkillSelectionPending &&
                       luoyiEngine.PendingDecision?.Kind == DecisionKind.Luoyi &&
                       luoyiViewModel.SkillChoices.Count == 2 &&
                       luoyiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "luoyi-use") &&
                       luoyiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "luoyi-skip") &&
                       luoyiViewModel.CurrentGuideTitle == "决定是否发动裸衣" &&
                       luoyiViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("对方造成的伤害不会增加", StringComparison.Ordinal)),
            "The WPF must render both Luoyi choices and explain Duel damage attribution.");
        Program.Render(
            luoyiRoot,
            1120,
            740,
            Path.Combine(output, "89-classic-luoyi-choice.png"));
        var useLuoyi = luoyiViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "luoyi-use");
        luoyiViewModel.SelectSkillChoiceCommand.Execute(useLuoyi);
        Program.Assert(!luoyiViewModel.IsSkillSelectionPending &&
                       luoyiEngine.PendingDecision is null &&
                       luoyiEngine.State.Phase == TurnPhase.Play &&
                       luoyiEngine.CreateSnapshot(0, revealAll: true)
                           .Players.Single(player => player.Seat == 0).HandCount == luoyiBefore + 1 &&
                       luoyiEngine.Events.Any(item => item.Payload is DrawSkillResolvedEvent
                       {
                           SourceSeat: 0,
                           Skill: SkillKind.Luoyi,
                           Used: true,
                           DrawCount: 1
                       }),
            "The WPF Luoyi choice must draw one fewer card and preserve the play boundary.");
        luoyiWindow.Content = null;
        luoyiWindow.Close();
        using var qiangxiViewModel = FindGeneralChoice("classic:dian-wei");
        var dianWei = qiangxiViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:dian-wei");
        Program.Assert(dianWei.Name == "典韦" &&
                       dianWei.Kingdom == "魏" &&
                       dianWei.SkillName == "强袭" &&
                       dianWei.SkillDescription.Contains("失去1点体力", StringComparison.Ordinal) &&
                       dianWei.SkillDescription.Contains("弃置一张武器牌", StringComparison.Ordinal) &&
                       dianWei.SkillDescription.Contains("攻击范围内", StringComparison.Ordinal) &&
                       dianWei.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(dianWei.GeneralId),
            "The current classic Dian Wei card must render Wei, Qiangxi, Lord health and portrait aliasing.");
        var qiangxiWindow = new MainWindow(qiangxiViewModel);
        qiangxiWindow.ApplyTemplate();
        var qiangxiRoot = (FrameworkElement)qiangxiWindow.Content;
        Program.Render(
            qiangxiRoot,
            1120,
            740,
            Path.Combine(output, "90-classic-dian-wei-card.png"));
        qiangxiViewModel.SelectGeneralChoiceCommand.Execute(dianWei);
        Program.AdvanceToDecision(qiangxiViewModel);
        var qiangxiEngine = Program.Engine(qiangxiViewModel);
        var qiangxiAction = qiangxiEngine.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Qiangxi);
        var qiangxiBefore = qiangxiEngine.CreateSnapshot(0, revealAll: true);
        var damageTriggerSkills = new HashSet<SkillKind>
        {
            SkillKind.Feedback,
            SkillKind.Yiji,
            SkillKind.Jieming,
            SkillKind.Yuanhu,
            SkillKind.Ganglie
        };
        var qiangxiTargetSeat = qiangxiBefore.Players
            .Where(player => qiangxiAction.SelectableTargetSeats.Contains(player.Seat))
            .First(player => player.Skills?.All(skill => !damageTriggerSkills.Contains(skill.Kind)) != false)
            .Seat;
        var qiangxiSourceHp = qiangxiBefore.Players.Single(player => player.Seat == 0).Hp;
        var qiangxiTargetHp = qiangxiBefore.Players.Single(player => player.Seat == qiangxiTargetSeat).Hp;
        Program.Assert(qiangxiViewModel.CanUseActiveSkill &&
                       qiangxiViewModel.ActiveSkillEntryText.Contains("强袭", StringComparison.Ordinal),
            "Classic Dian Wei must expose Qiangxi at the WPF play boundary.");
        qiangxiViewModel.UseActiveSkillCommand.Execute(null);
        var qiangxiTarget = qiangxiViewModel.Seats.Single(seat => seat.Seat == qiangxiTargetSeat);
        qiangxiViewModel.SelectTargetCommand.Execute(qiangxiTarget);
        Program.Assert(qiangxiViewModel.IsActiveSkillSelectionPending &&
                       qiangxiViewModel.CanConfirmActiveSkill &&
                       qiangxiTarget.IsSelectedTarget &&
                       qiangxiViewModel.PlayButtonText == "发动强袭" &&
                       qiangxiViewModel.CurrentGuideTitle == "确认发动【强袭】" &&
                       qiangxiViewModel.CurrentGuideBody.Contains("武器牌", StringComparison.Ordinal),
            "Qiangxi must reuse the optional-card, exact-one-target active-skill draft.");
        Program.Render(
            qiangxiRoot,
            1120,
            740,
            Path.Combine(output, "91-classic-qiangxi-target.png"));
        qiangxiViewModel.ConfirmSelectedCommand.Execute(null);
        var qiangxiAfter = qiangxiEngine.CreateSnapshot(0, revealAll: true);
        Program.Assert(qiangxiAfter.Players.Single(player => player.Seat == 0).Hp == qiangxiSourceHp - 1 &&
                       qiangxiAfter.Players.Single(player => player.Seat == qiangxiTargetSeat).Hp == qiangxiTargetHp - 1 &&
                       qiangxiEngine.Events.Any(item => item.Payload is DamageRequestedEvent
                       {
                           SourceSeat: 0,
                           TargetSeat: var eventTarget,
                           Amount: 1,
                           SourceCard: null
                       } && eventTarget == qiangxiTargetSeat),
            "The WPF Qiangxi draft must submit the HP-cost branch as cardless direct damage.");
        qiangxiWindow.Content = null;
        qiangxiWindow.Close();
        using var duanliangDescriptionViewModel = FindGeneralChoice("classic:xu-huang");
        var xuHuang = duanliangDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:xu-huang");
        Program.Assert(xuHuang.Name == "徐晃" &&
                       xuHuang.Kingdom == "魏" &&
                       xuHuang.SkillName == "断粮" &&
                       xuHuang.SkillDescription.Contains("黑色基本牌或黑色装备牌", StringComparison.Ordinal) &&
                       xuHuang.SkillDescription.Contains("距离为2", StringComparison.Ordinal) &&
                       xuHuang.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(xuHuang.GeneralId),
            "The current classic Xu Huang card must render Wei, Duanliang, Lord health and portrait aliasing.");
        var duanliangDescriptionWindow = new MainWindow(duanliangDescriptionViewModel);
        duanliangDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)duanliangDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "92-classic-xu-huang-card.png"));
        duanliangDescriptionWindow.Content = null;
        duanliangDescriptionWindow.Close();

        using var duanliangViewModel = FindClassicDuanliangViewModel();
        var duanliangWindow = new MainWindow(duanliangViewModel);
        duanliangWindow.ApplyTemplate();
        var duanliangRoot = (FrameworkElement)duanliangWindow.Content;
        var duanliangEngine = Program.Engine(duanliangViewModel);
        var duanliangAction = duanliangEngine.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.SupplyShortage &&
            action.PlayedCardKind == CardKind.SupplyShortage &&
            action.CardId is { } cardId &&
            duanliangViewModel.Hand.Any(card => card.Id == cardId) &&
            action.TargetSeat is { } seat &&
            duanliangEngine.GetCombatDistance(0, seat) == 2);
        var duanliangCard = duanliangViewModel.Hand.Single(card => card.Id == duanliangAction.CardId);
        var duanliangTarget = duanliangViewModel.Seats.Single(seat => seat.Seat == duanliangAction.TargetSeat);
        duanliangViewModel.SelectCardCommand.Execute(duanliangCard);
        duanliangViewModel.SelectTargetCommand.Execute(duanliangTarget);
        Program.Assert(duanliangViewModel.CanPlaySelectedAsSlash &&
                       duanliangViewModel.PlayButtonText == "当作兵粮寸断使用" &&
                       duanliangTarget.IsLegalTarget &&
                       duanliangTarget.IsSelectedTarget &&
                       duanliangEngine.CreateSnapshot(0, revealAll: true).Players[0].Hand.Single(card =>
                           card.Id == duanliangCard.Id).Suit is Suit.Spade or Suit.Club,
            "Duanliang must expose a black hand card as Supply Shortage against a distance-two WPF target.");
        Program.Render(
            duanliangRoot,
            1120,
            740,
            Path.Combine(output, "93-classic-duanliang-target.png"));
        var duanliangRevision = duanliangEngine.Revision;
        duanliangViewModel.PlaySelectedAsSlashCommand.Execute(null);
        Program.Assert(duanliangEngine.Revision == duanliangRevision + 1 &&
                       duanliangEngine.Events.Any(item => item.Payload is DelayedCardPlacedEvent placed &&
                           placed.CardId == duanliangCard.Id &&
                           placed.CardKind == CardKind.SupplyShortage &&
                           placed.TargetSeat == duanliangTarget.Seat) &&
                       duanliangEngine.CreateSnapshot(0, revealAll: true).Players
                           .Single(player => player.Seat == duanliangTarget.Seat).Judgment.Any(card =>
                               card.Id == duanliangCard.Id && card.Kind == CardKind.SupplyShortage),
            "The WPF Duanliang action must place the physical black card with persistent Supply Shortage semantics.");
        duanliangWindow.Content = null;
        duanliangWindow.Close();

        using var luoshenViewModel = FindGeneralChoice("classic:zhen-ji");
        var zhenJi = luoshenViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhen-ji");
        Program.Assert(zhenJi.Name == "甄姬" &&
                       zhenJi.Kingdom == "魏" &&
                       zhenJi.SkillName == "洛神 / 倾国" &&
                       zhenJi.SkillDescription.Contains("直到出现红色", StringComparison.Ordinal) &&
                       zhenJi.SkillDescription.Contains("黑色手牌当【闪】", StringComparison.Ordinal) &&
                       zhenJi.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(zhenJi.GeneralId),
            "The current classic Zhen Ji card must render Wei, Luoshen, Qingguo, Lord health and portrait aliasing.");
        var luoshenWindow = new MainWindow(luoshenViewModel);
        luoshenWindow.ApplyTemplate();
        var luoshenRoot = (FrameworkElement)luoshenWindow.Content;
        Program.Render(
            luoshenRoot,
            1120,
            740,
            Path.Combine(output, "94-classic-zhen-ji-card.png"));
        var zhenJiDescription = Program.Find<System.Windows.Controls.TextBlock>(luoshenRoot)
            .Single(text => text.Text == zhenJi.SkillDescription);
        Program.Assert(zhenJiDescription.ActualHeight >= 108,
            "The Zhen Ji general card clipped one of the six rendered skill-description lines.");
        luoshenViewModel.SelectGeneralChoiceCommand.Execute(zhenJi);
        Program.AdvanceToDecision(luoshenViewModel);
        var luoshenEngine = Program.Engine(luoshenViewModel);
        Program.Assert(luoshenViewModel.IsSkillSelectionPending &&
                       luoshenEngine.PendingDecision is
                       {
                           Kind: DecisionKind.Luoshen,
                           PlayerSeat: 0,
                           IsPrivate: true,
                           Choices.Count: 2
                       } &&
                       luoshenViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "luoshen-use") &&
                       luoshenViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "luoshen-skip") &&
                       luoshenViewModel.CurrentGuideTitle == "决定是否发动洛神" &&
                       luoshenViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("出现红色结果时自动停止", StringComparison.Ordinal)),
            "The WPF must render both initial Luoshen choices and explain the repeated black-judgment chain.");
        Program.Render(
            luoshenRoot,
            1120,
            740,
            Path.Combine(output, "95-classic-luoshen-choice.png"));
        luoshenViewModel.SelectSkillChoiceCommand.Execute(luoshenViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "luoshen-skip"));
        Program.Assert(!luoshenViewModel.IsSkillSelectionPending &&
                       luoshenEngine.Events.Any(item => item.Payload is LuoshenChoiceResolvedEvent
                       {
                           SourceSeat: 0,
                           Used: false,
                           IsRepeat: false
                       }),
            "The WPF Luoshen skip choice must commit through the typed preparation-stage event.");
        luoshenWindow.Content = null;
        luoshenWindow.Close();

        var qingguoEngine = WushengResponseScenario.FindQingguoDodge();
        var qingguoPrompt = qingguoEngine.PendingDecision!;
        var qingguoHand = qingguoEngine.CreateSnapshot(0).Players[0].Hand;
        var qingguoChoice = qingguoPrompt.Choices.First(choice =>
            choice.Cards.Count == 1 &&
            qingguoHand.Single(card => card.Id == choice.Cards[0]).Kind != CardKind.Dodge);
        var qingguoStore = new MemorySaveStore();
        qingguoStore.Write(GameSaveSlot.Manual,
            new(1, DateTimeOffset.UtcNow, false, qingguoEngine.CreateCheckpoint()));
        using var qingguoViewModel = new MainViewModel(
            autoAdvance: false,
            seed: qingguoEngine.Seed,
            showSetup: true,
            saveStore: qingguoStore,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        qingguoViewModel.LoadManualGameCommand.Execute(null);
        var qingguoCard = qingguoViewModel.Hand.Single(card => card.Id == qingguoChoice.Cards[0]);
        qingguoViewModel.SelectCardCommand.Execute(qingguoCard);
        var qingguoWindow = new MainWindow(qingguoViewModel);
        qingguoWindow.ApplyTemplate();
        Program.Assert(!qingguoViewModel.HasSaveError &&
                       qingguoViewModel.IsResponseSelectionPending &&
                       qingguoCard.IsPlayable &&
                       qingguoCard.IsSelected &&
                       qingguoViewModel.CanConfirmSelected &&
                       qingguoViewModel.PlayButtonText == "当作闪打出" &&
                       qingguoViewModel.ActionHint.Contains("当作【闪】", StringComparison.Ordinal),
            qingguoViewModel.SaveStatus);
        Program.Render(
            (FrameworkElement)qingguoWindow.Content,
            1120,
            740,
            Path.Combine(output, "96-classic-qingguo-response.png"));
        qingguoViewModel.ConfirmSelectedCommand.Execute(null);
        Program.Assert(Program.Engine(qingguoViewModel).Events.Any(item =>
                           item.Payload is CardRespondedEvent responded &&
                           responded.CardId == qingguoCard.Id &&
                           responded.EffectiveCardKind == CardKind.Dodge),
            "The WPF Qingguo response must commit the selected black physical card as an effective Dodge.");
        qingguoWindow.Content = null;
        qingguoWindow.Close();

        using var jizhiDescriptionViewModel = FindGeneralChoice("classic:huang-yueying");
        var huangYueying = jizhiDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:huang-yueying");
        Program.Assert(huangYueying.Name == "黄月英" &&
                       huangYueying.Kingdom == "蜀" &&
                       huangYueying.SkillName == "集智 / 奇才" &&
                       huangYueying.SkillDescription.Contains("普通锦囊牌", StringComparison.Ordinal) &&
                       huangYueying.SkillDescription.Contains("无距离限制", StringComparison.Ordinal) &&
                       huangYueying.HealthText == "体力上限 4" &&
                       GeneralArt.HasPortrait(huangYueying.GeneralId),
            "The current classic Huang Yueying card must render Shu, Jizhi, Qicai, Lord health and portrait aliasing.");
        var jizhiDescriptionWindow = new MainWindow(jizhiDescriptionViewModel);
        jizhiDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)jizhiDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "97-classic-huang-yueying-card.png"));
        jizhiDescriptionWindow.Content = null;
        jizhiDescriptionWindow.Close();

        using var jizhiViewModel = FindClassicJizhiViewModel();
        var jizhiEngine = Program.Engine(jizhiViewModel);
        var drawTwo = jizhiEngine.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.DrawTwo && action.CardId is not null);
        var jizhiCard = jizhiViewModel.Hand.Single(card => card.Id == drawTwo.CardId);
        jizhiViewModel.SelectCardCommand.Execute(jizhiCard);
        Program.Assert(jizhiViewModel.CanPlaySelected,
            "The WPF Huang Yueying fixture must expose its ordinary trick through normal hand confirmation.");
        jizhiViewModel.PlaySelectedCardCommand.Execute(null);
        var jizhiPrompt = jizhiEngine.PendingDecision;
        Program.Assert(jizhiViewModel.IsSkillSelectionPending &&
                       jizhiPrompt is
                       {
                           Kind: DecisionKind.Jizhi,
                           PlayerSeat: 0,
                           IsPrivate: true,
                           Choices.Count: 2
                       } &&
                       jizhiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "jizhi-use") &&
                       jizhiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "jizhi-skip") &&
                       jizhiViewModel.CurrentGuideTitle == "决定是否发动集智" &&
                       jizhiViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("原处继续", StringComparison.Ordinal)),
            "The WPF must render both private Jizhi choices before resuming the ordinary trick.");
        var jizhiWindow = new MainWindow(jizhiViewModel);
        jizhiWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)jizhiWindow.Content,
            1120,
            740,
            Path.Combine(output, "98-classic-jizhi-choice.png"));
        jizhiViewModel.SelectSkillChoiceCommand.Execute(jizhiViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jizhi-use"));
        Program.Assert(jizhiEngine.Events.Any(item => item.Payload is DrawSkillResolvedEvent
        {
            SourceSeat: 0,
            Skill: SkillKind.Jizhi,
            Used: true,
            DrawCount: 1
        }) &&
                       jizhiEngine.CardMovements.Count(movement =>
                           movement.To == CardLocation.Hand(0) &&
                           movement.Reason == CardMoveReasons.JizhiDraw) == 1 &&
                       jizhiEngine.PendingDecision?.Kind != DecisionKind.Jizhi,
            "The WPF Jizhi choice must draw exactly one card and resume the original trick.");
        jizhiWindow.Content = null;
        jizhiWindow.Close();

        using var tieqiDescriptionViewModel = FindGeneralChoice("classic:ma-chao");
        var maChao = tieqiDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:ma-chao");
        Program.Assert(maChao.Name == "马超" &&
                       maChao.Kingdom == "蜀" &&
                       maChao.SkillName == "铁骑 / 马术" &&
                       maChao.SkillDescription.Contains("不能使用【闪】", StringComparison.Ordinal) &&
                       maChao.SkillDescription.Contains("距离始终 -1", StringComparison.Ordinal) &&
                       maChao.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(maChao.GeneralId),
            "The current classic Ma Chao card must render Shu, Tieqi, Mashu, Lord health and portrait aliasing.");
        var tieqiDescriptionWindow = new MainWindow(tieqiDescriptionViewModel);
        tieqiDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)tieqiDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "99-classic-ma-chao-card.png"));
        tieqiDescriptionWindow.Content = null;
        tieqiDescriptionWindow.Close();

        using var tieqiViewModel = FindClassicTieqiViewModel();
        var tieqiEngine = Program.Engine(tieqiViewModel);
        var slashAction = tieqiEngine.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Slash &&
                             action.CardId is not null &&
                             action.TargetSeat is not null)
            .OrderBy(action => action.CardId)
            .ThenBy(action => action.TargetSeat)
            .First();
        var tieqiSlash = tieqiViewModel.Hand.Single(card => card.Id == slashAction.CardId);
        tieqiViewModel.SelectCardCommand.Execute(tieqiSlash);
        var tieqiTarget = tieqiViewModel.Seats.Single(seat => seat.Seat == slashAction.TargetSeat);
        tieqiViewModel.SelectTargetCommand.Execute(tieqiTarget);
        Program.Assert(tieqiTarget.IsSelectedTarget && tieqiViewModel.CanPlaySelected,
            "The WPF Ma Chao fixture must select its Slash and exact target through normal controls.");
        tieqiViewModel.PlaySelectedCardCommand.Execute(null);
        var tieqiPrompt = tieqiEngine.PendingDecision;
        Program.Assert(tieqiViewModel.IsSkillSelectionPending &&
                       tieqiPrompt is
                       {
                           Kind: DecisionKind.Tieqi,
                           PlayerSeat: 0,
                           IsPrivate: true,
                           Choices.Count: 2
                       } &&
                       tieqiPrompt.TargetSeat == tieqiTarget.Seat &&
                       tieqiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "tieqi-use") &&
                       tieqiViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "tieqi-skip") &&
                       tieqiViewModel.CurrentGuideTitle == "决定是否发动铁骑" &&
                       tieqiViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("红色结果", StringComparison.Ordinal)),
            "The WPF must render both private Tieqi choices before the target receives a Dodge response.");
        var tieqiWindow = new MainWindow(tieqiViewModel);
        tieqiWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)tieqiWindow.Content,
            1120,
            740,
            Path.Combine(output, "100-classic-tieqi-choice.png"));
        tieqiViewModel.SelectSkillChoiceCommand.Execute(tieqiViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tieqi-use"));
        Program.Assert(tieqiEngine.Events.Any(item => item.Payload is TieqiChoiceResolvedEvent
        {
            SourceSeat: 0,
            Used: true
        } resolved && resolved.TargetSeat == tieqiTarget.Seat) &&
                       tieqiEngine.Events.Any(item => item.Payload is JudgmentRequestedEvent requested &&
                           requested.Reason == JudgmentReasons.Tieqi && requested.TargetSeat == 0),
            "The WPF Tieqi choice must commit a typed choice and public attacker judgment.");
        tieqiWindow.Content = null;
        tieqiWindow.Close();

        using var liegongDescriptionViewModel = FindGeneralChoice("classic:huang-zhong");
        var huangZhong = liegongDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:huang-zhong");
        Program.Assert(huangZhong.Name == "黄忠" &&
                       huangZhong.Kingdom == "蜀" &&
                       huangZhong.SkillName == "烈弓" &&
                       huangZhong.SkillDescription.Contains("手牌数不小于你的体力值", StringComparison.Ordinal) &&
                       huangZhong.SkillDescription.Contains("或不大于你的攻击范围", StringComparison.Ordinal) &&
                       huangZhong.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(huangZhong.GeneralId),
            $"The current classic Huang Zhong card must render Shu, Liegong, Lord health and portrait aliasing " +
            $"(name={huangZhong.Name}, kingdom={huangZhong.Kingdom}, skill={huangZhong.SkillName}, " +
            $"health={huangZhong.HealthText}, portrait={GeneralArt.HasPortrait(huangZhong.GeneralId)}, " +
            $"description={huangZhong.SkillDescription}).");
        var liegongDescriptionWindow = new MainWindow(liegongDescriptionViewModel);
        liegongDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)liegongDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "101-classic-huang-zhong-card.png"));
        liegongDescriptionWindow.Content = null;
        liegongDescriptionWindow.Close();

        var liegongFixture = FindLiegongFixture();
        var liegongStore = new MemorySaveStore();
        liegongStore.Write(GameSaveSlot.Manual,
            new(1, DateTimeOffset.UtcNow, false, liegongFixture.CreateCheckpoint()));
        using var liegongViewModel = new MainViewModel(
            autoAdvance: false,
            seed: liegongFixture.Seed,
            showSetup: true,
            saveStore: liegongStore,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        liegongViewModel.LoadManualGameCommand.Execute(null);
        var liegongEngine = Program.Engine(liegongViewModel);
        var liegongPrompt = liegongEngine.PendingDecision;
        Program.Assert(!liegongViewModel.HasSaveError &&
                       liegongViewModel.IsSkillSelectionPending &&
                       liegongPrompt is
                       {
                           Kind: DecisionKind.Liegong,
                           PlayerSeat: 0,
                           IsPrivate: true,
                           Choices.Count: 2
                       } &&
                       liegongViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "liegong-use") &&
                       liegongViewModel.SkillChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "liegong-skip") &&
                       liegongViewModel.CurrentGuideTitle == "决定是否发动烈弓" &&
                       liegongViewModel.CurrentGuideSteps.Any(step =>
                           step.Text.Contains("不能用闪", StringComparison.Ordinal)),
            liegongViewModel.SaveStatus);
        var liegongTargetSeat = liegongPrompt?.TargetSeat ??
            throw new InvalidOperationException("The restored Liegong prompt has no Slash target.");
        var liegongWindow = new MainWindow(liegongViewModel);
        liegongWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)liegongWindow.Content,
            1120,
            740,
            Path.Combine(output, "102-classic-liegong-choice.png"));
        var liegongEventCount = liegongEngine.Events.Count;
        liegongViewModel.SelectSkillChoiceCommand.Execute(liegongViewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "liegong-use"));
        Program.Assert(liegongEngine.Events.Skip(liegongEventCount).Any(item => item.Payload is LiegongChoiceResolvedEvent
        {
            SourceSeat: 0,
            Used: true
        } resolved && resolved.TargetSeat == liegongTargetSeat) &&
                       liegongEngine.Events.Skip(liegongEventCount).Select(item => item.Payload).OfType<ResponseRequestedEvent>().All(requested =>
                           requested.TargetSeat != liegongTargetSeat ||
                           requested.RequiredCardKind != CardKind.Dodge),
            "The WPF Liegong choice must commit a typed result without publishing a Dodge response.");
        liegongWindow.Content = null;
        liegongWindow.Close();

        using var kuangguDescriptionViewModel = FindGeneralChoice("classic:wei-yan");
        var weiYan = kuangguDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:wei-yan");
        Program.Assert(weiYan.Name == "魏延" &&
                       weiYan.Kingdom == "蜀" &&
                       weiYan.SkillName == "狂骨" &&
                       weiYan.SkillDescription.Contains("锁定技", StringComparison.Ordinal) &&
                       weiYan.SkillDescription.Contains("距离1以内", StringComparison.Ordinal) &&
                       weiYan.SkillDescription.Contains("回复1点体力", StringComparison.Ordinal) &&
                       weiYan.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(weiYan.GeneralId),
            "The current classic Wei Yan card must render Shu, locked Kuanggu, Lord health and portrait aliasing.");
        var kuangguDescriptionWindow = new MainWindow(kuangguDescriptionViewModel);
        kuangguDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)kuangguDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "103-classic-wei-yan-card.png"));
        kuangguDescriptionWindow.Content = null;
        kuangguDescriptionWindow.Close();

        using var wushuangDescriptionViewModel = FindGeneralChoice("classic:lu-bu");
        var luBu = wushuangDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:lu-bu");
        Program.Assert(luBu.Name == "吕布" &&
                       luBu.Kingdom == "群" &&
                       luBu.SkillName == "无双" &&
                       luBu.SkillDescription.Contains("锁定技", StringComparison.Ordinal) &&
                       luBu.SkillDescription.Contains("两张【闪】", StringComparison.Ordinal) &&
                       luBu.SkillDescription.Contains("两张【杀】", StringComparison.Ordinal) &&
                       luBu.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(luBu.GeneralId),
            "The current classic Lu Bu card must render Qun, locked Wushuang, Lord health and portrait aliasing.");
        var wushuangDescriptionWindow = new MainWindow(wushuangDescriptionViewModel);
        wushuangDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)wushuangDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "104-classic-lu-bu-card.png"));
        wushuangDescriptionWindow.Content = null;
        wushuangDescriptionWindow.Close();

        using var paoxiaoDescriptionViewModel = FindGeneralChoice("classic:zhang-fei");
        var zhangFei = paoxiaoDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhang-fei");
        Program.Assert(zhangFei.Name == "张飞" &&
                       zhangFei.Kingdom == "蜀" &&
                       zhangFei.SkillName == "咆哮" &&
                       zhangFei.SkillDescription.Contains("锁定技", StringComparison.Ordinal) &&
                       zhangFei.SkillDescription.Contains("无次数限制", StringComparison.Ordinal) &&
                       zhangFei.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(zhangFei.GeneralId),
            "The current classic Zhang Fei card must render Shu, locked Paoxiao, Lord health and portrait art.");
        var paoxiaoDescriptionWindow = new MainWindow(paoxiaoDescriptionViewModel);
        paoxiaoDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)paoxiaoDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "105-classic-zhang-fei-card.png"));
        paoxiaoDescriptionWindow.Content = null;
        paoxiaoDescriptionWindow.Close();

        using var longdanDescriptionViewModel = FindGeneralChoice("classic:zhao-yun");
        var zhaoYun = longdanDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:zhao-yun");
        Program.Assert(zhaoYun.Name == "赵云" &&
                       zhaoYun.Kingdom == "蜀" &&
                       zhaoYun.SkillName == "龙胆" &&
                       zhaoYun.SkillDescription.Contains("【杀】当【闪】", StringComparison.Ordinal) &&
                       zhaoYun.SkillDescription.Contains("【闪】当【杀】", StringComparison.Ordinal) &&
                       zhaoYun.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(zhaoYun.GeneralId),
            "The current classic Zhao Yun card must render Shu, bidirectional Longdan, Lord health and portrait art.");
        var longdanDescriptionWindow = new MainWindow(longdanDescriptionViewModel);
        longdanDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)longdanDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "106-classic-zhao-yun-card.png"));
        longdanDescriptionWindow.Content = null;
        longdanDescriptionWindow.Close();

        using var wushengDescriptionViewModel = FindGeneralChoice("classic:guan-yu");
        var guanYu = wushengDescriptionViewModel.GeneralChoices.Single(choice =>
            choice.GeneralId == "classic:guan-yu");
        Program.Assert(guanYu.Name == "关羽" &&
                       guanYu.Kingdom == "蜀" &&
                       guanYu.SkillName == "武圣" &&
                       guanYu.SkillDescription.Contains("红色牌", StringComparison.Ordinal) &&
                       guanYu.SkillDescription.Contains("使用或打出", StringComparison.Ordinal) &&
                       guanYu.HealthText == "体力上限 5" &&
                       GeneralArt.HasPortrait(guanYu.GeneralId),
            "The current classic Guan Yu card must render Shu, formal Wusheng, Lord health and portrait art.");
        var wushengDescriptionWindow = new MainWindow(wushengDescriptionViewModel);
        wushengDescriptionWindow.ApplyTemplate();
        Program.Render(
            (FrameworkElement)wushengDescriptionWindow.Content,
            1120,
            740,
            Path.Combine(output, "107-classic-guan-yu-card.png"));
        wushengDescriptionWindow.Content = null;
        wushengDescriptionWindow.Close();

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

        using var qixiViewModel = FindClassicQixiEquipmentViewModel();
        var qixiWindow = new MainWindow(qixiViewModel);
        qixiWindow.ApplyTemplate();
        var qixiRoot = (FrameworkElement)qixiWindow.Content;
        var qixiEngine = Program.Engine(qixiViewModel);
        var blackEquipmentAction = qixiEngine.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Equip &&
            action.CardId is { } cardId &&
            qixiViewModel.Hand.Any(card => card.Id == cardId &&
                qixiEngine.CreateSnapshot(0, revealAll: true).Players[0].Hand
                    .Any(snapshot => snapshot.Id == cardId && snapshot.Suit is Suit.Spade or Suit.Club)));
        var blackEquipmentCard = qixiViewModel.Hand.Single(card => card.Id == blackEquipmentAction.CardId);
        qixiViewModel.SelectCardCommand.Execute(blackEquipmentCard);
        qixiViewModel.PlaySelectedCardCommand.Execute(null);
        Program.AdvanceToDecision(qixiViewModel);
        var equipmentPlayChoice = qixiViewModel.EquipmentPlayChoices.Single(choice =>
            choice.Cards.SequenceEqual([blackEquipmentCard.Id]));
        Program.Assert(equipmentPlayChoice.Description.Contains("装备", StringComparison.Ordinal) &&
                       equipmentPlayChoice.Description.Contains("牌型转化", StringComparison.Ordinal),
            "Formal Qixi must render a dedicated equipment conversion entry.");
        qixiViewModel.SelectEquipmentPlayChoiceCommand.Execute(equipmentPlayChoice);
        var qixiTarget = qixiViewModel.Seats.First(seat => seat.IsLegalTarget);
        qixiViewModel.SelectTargetCommand.Execute(qixiTarget);
        Program.Assert(qixiViewModel.CanPlaySelectedAsSlash &&
                       qixiViewModel.PlayButtonText == "当作过河拆桥使用" &&
                       qixiViewModel.EquipmentPlayChoices.Single().Description.Contains("已选择", StringComparison.Ordinal),
            "Selecting equipped Qixi cost must enable target selection and name the effective Dismantlement action.");
        Program.Render(qixiRoot, 1120, 740,
            Path.Combine(output, "83-classic-qixi-equipment.png"));
        var qixiRevision = qixiEngine.Revision;
        qixiViewModel.PlaySelectedAsSlashCommand.Execute(null);
        Program.Assert(qixiEngine.Revision == qixiRevision + 1 &&
                       qixiEngine.Events.Any(item => item.Payload is CardUseDeclaredEvent declared &&
                           declared.CardId == blackEquipmentCard.Id &&
                           declared.CardKind == CardKind.Dismantlement) &&
                       qixiEngine.CardMovements.Any(movement =>
                           movement.CardId == blackEquipmentCard.Id &&
                           movement.From == CardLocation.Equipment(0) &&
                           movement.To == CardLocation.Processing),
            "The WPF Qixi equipment action must submit the physical equipment with Dismantlement semantics.");
        qixiWindow.Content = null;
        qixiWindow.Close();

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
        var claimedPromptId = Program.Engine(viewModel).PendingDecision!.PromptId;
        viewModel.SelectSkillChoiceCommand.Execute(claim);
        var engine = Program.Engine(viewModel);
        var claimedSnapshot = engine.CreateSnapshot(0, revealAll: true);
        var claimedHandIds = claimedSnapshot.Players.Single(player => player.Seat == 0).Hand
            .Select(card => card.Id)
            .ToArray();
        var claimedEvent = engine.Events.Select(item => item.Payload).OfType<JudgmentCardClaimedEvent>()
            .LastOrDefault();
        Program.Assert(engine.PendingDecision?.PromptId != claimedPromptId &&
                       claimedHandIds.Contains(judgmentCardId) &&
                       claimedEvent is
                       {
                           OwnerSeat: 0,
                           Skill: SkillKind.Tiandu,
                           Used: true
                       },
            $"The WPF Tiandu choice must claim the exact resolved judgment card " +
            $"(expected={judgmentCardId}, hand={string.Join(',', claimedHandIds)}, event={claimedEvent}).");
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

    private static MainViewModel FindClassicQixiEquipmentViewModel()
    {
        for (var seed = 1; seed <= 4_096; seed++)
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
            var ganNing = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:gan-ning");
            if (ganNing is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(ganNing);
                Program.AdvanceToDecision(candidate);
                var engine = Program.Engine(candidate);
                var self = engine.CreateSnapshot(0, revealAll: true).Players[0];
                var blackEquipmentIds = self.Hand
                    .Where(card => EquipmentCatalog.IsEquipment(card.Kind) &&
                                   card.Suit is Suit.Spade or Suit.Club)
                    .Select(card => card.Id)
                    .ToHashSet();
                if (engine.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Equip &&
                        action.CardId is { } cardId &&
                        blackEquipmentIds.Contains(cardId)))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Qixi equipment WPF fixture.");
    }

    private static MainViewModel FindClassicDuanliangViewModel()
    {
        for (var seed = 1; seed <= 8_192; seed++)
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
            var xuHuang = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:xu-huang");
            if (xuHuang is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(xuHuang);
                Program.AdvanceToDecision(candidate);
                var engine = Program.Engine(candidate);
                var snapshot = engine.CreateSnapshot(0, revealAll: true);
                if (engine.PendingDecision?.Kind == DecisionKind.PlayCard &&
                    snapshot.Players.SelectMany(player => player.Hand)
                        .All(card => card.Kind != CardKind.Nullification) &&
                    engine.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.SupplyShortage &&
                        action.PlayedCardKind == CardKind.SupplyShortage &&
                        action.CardId is { } cardId &&
                        candidate.Hand.Any(card => card.Id == cardId) &&
                        action.TargetSeat is { } seat &&
                        engine.GetCombatDistance(0, seat) == 2))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Duanliang WPF fixture.");
    }

    private static MainViewModel FindClassicJizhiViewModel()
    {
        for (var seed = 1; seed <= 8_192; seed++)
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
            var huangYueying = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:huang-yueying");
            if (huangYueying is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(huangYueying);
                Program.AdvanceToDecision(candidate);
                var engine = Program.Engine(candidate);
                if (engine.PendingDecision?.Kind == DecisionKind.PlayCard &&
                    engine.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.DrawTwo && action.CardId is not null))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Jizhi WPF fixture.");
    }

    private static MainViewModel FindClassicTieqiViewModel()
    {
        for (var seed = 1; seed <= 8_192; seed++)
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
            var maChao = candidate.GeneralChoices.SingleOrDefault(choice =>
                choice.GeneralId == "classic:ma-chao");
            if (maChao is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(maChao);
                Program.AdvanceToDecision(candidate);
                var engine = Program.Engine(candidate);
                if (engine.PendingDecision?.Kind == DecisionKind.PlayCard &&
                    engine.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Slash &&
                        action.CardId is not null &&
                        action.TargetSeat is not null))
                {
                    return candidate;
                }
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic classic Tieqi WPF fixture.");
    }

    private static GameEngine FindLiegongFixture()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Rebel,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision?.ValidContentIds.Contains("classic:huang-zhong") != true)
            {
                continue;
            }

            var selection = game.PendingDecision!;
            if (!game.Submit(new SelectGeneralCommand(
                    0,
                    "classic:huang-zhong",
                    game.Revision,
                    selection.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted)
            {
                continue;
            }

            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    var attackRange = game.GetAttackRange(0);
                    var slash = game.GetHumanLegalActions()
                        .Where(action => action.Kind == LegalActionKind.Slash &&
                                         action.CardId is not null &&
                                         action.TargetSeat is not null)
                        .FirstOrDefault(action =>
                        {
                            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
                            return target.Hand.Any(card => card.Kind == CardKind.Dodge) &&
                                   (target.Hand.Count >= source.Hp || target.Hand.Count <= attackRange);
                        });
                    if (slash is not null)
                    {
                        var played = game.Submit(new PlayCardCommand(
                            0,
                            slash.CardId!.Value,
                            slash.TargetSeats,
                            game.Revision,
                            prompt.PromptId,
                            slash.PlayedCardKind,
                            slash.TargetCardId));
                        if (played.Accepted && game.PendingDecision?.Kind == DecisionKind.Liegong)
                        {
                            return game;
                        }
                        break;
                    }
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
                    _ when prompt.PlayerSeat == 0 && prompt.Choices.Count > 0 =>
                        new AnswerPromptCommand(
                            0,
                            prompt.PromptId,
                            prompt.Choices.FirstOrDefault(choice =>
                                choice.Parameters.Values.Any(value =>
                                    value.StartsWith("skip", StringComparison.Ordinal) ||
                                    value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))?.Id ??
                                prompt.Choices.First().Id,
                            game.Revision),
                    _ => new AdvanceOneStepCommand(game.Revision)
                };
                if (!game.Submit(command).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic WPF Liegong fixture.");
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
