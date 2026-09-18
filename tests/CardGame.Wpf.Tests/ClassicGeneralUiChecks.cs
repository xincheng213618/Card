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
