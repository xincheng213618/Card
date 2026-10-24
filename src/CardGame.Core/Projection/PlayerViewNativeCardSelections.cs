using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Native collectors edit an unpaid set on their original frame. The tags
    // describe published commands; they do not change their IDs or payments.
    private PendingDecision ProjectNativeCardSelections(PendingDecision decision)
    {
        if (decision.RequiredCardCount > 0 || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f ||
            !IsValidPlayerSeat(decision.PlayerSeat)) return decision;

        if (ProjectShiScCardSelection(decision, f) is { } sunChenChoice) return sunChenChoice;
        if (ProjectShiSjCardSelection(decision, f) is { } sunJunChoice) return sunJunChoice;
        if (ProjectShiZyCardSelection(decision, f) is { } zhangYanChoice) return zhangYanChoice;
        if (ProjectShiLsCardSelection(decision, f) is { } sharedHandChoice) return sharedHandChoice;

        if (f.BijingPunish is { } bijing && decision.PlayerSeat == bijing.TurnOwnerSeat &&
            decision.Choices.All(c => c.Parameters.GetValueOrDefault("program-action") == "bijing-punish-discard"))
            return ProjectCollectedOwnedCards(decision, f, "bijing-punish-discard", bijing.Remaining - bijing.SelectedCardIds.Count,
                selected: bijing.SelectedCardIds);
        if (f.MouGuoJia is { Stage: MouGuoJiaStage.Select, Yin: true } xianmou &&
            xianmou.Target == decision.PlayerSeat && decision.PlayerSeat == f.OwnerSeat)
            return NativeOwnedSelection(decision, f, $"{xianmou.Stage}:{xianmou.Target}", "branch", ["add"], ["done"],
                xianmou.Selected, 0, Math.Max(0, xianmou.X - xianmou.Selected.Count));
        if (f.SequentialDiscard is { Stage: ProgramSequentialDiscardStage.SelectingBatch } sequential)
            return NativeOwnedSelection(decision, f, $"{sequential.Stage}:{sequential.Cursor}", "branch", ["card"], ["finish"],
                sequential.SelectedCardIds, Math.Max(0, sequential.PreviousCount + 1 - sequential.SelectedCardIds.Count),
                decision.Choices.Count(c => c.Parameters.GetValueOrDefault("branch") == "card"));
        if (f.CaoXian is { } caoxian && (caoxian.Stage == CaoXianStage.AdjustCards ||
                caoxian.Stage == CaoXianStage.EffectCards && caoxian.Option == 2) &&
            decision.PlayerSeat == (caoxian.Stage == CaoXianStage.EffectCards ? caoxian.Target : f.OwnerSeat))
            return NativeOwnedSelection(decision, f, $"{caoxian.Stage}:{caoxian.Target}:{caoxian.Option}", "branch", ["card"], [],
                caoxian.Cards.Select(c => c.Id).ToArray(), caoxian.Required - caoxian.Cards.Count, caoxian.Required - caoxian.Cards.Count);
        if (f.MouCaoShuangAnniversary is { Stage: MouCaoShuangAnniversaryStage.Cards, Option: 1 or 4 } jianzhuan &&
            decision.PlayerSeat == jianzhuan.Payer)
            return NativeOwnedSelection(decision, f, $"{jianzhuan.Stage}:{jianzhuan.Repeat}:{jianzhuan.Option}:{jianzhuan.Payer}", "branch", ["card"], [],
                jianzhuan.Cards.Select(c => c.Id).ToArray(), jianzhuan.Required - jianzhuan.Cards.Count, jianzhuan.Required - jianzhuan.Cards.Count);
        if (f.Chanshuang is { Stage: ChanshuangStage.DiscardCards } chanshuang && decision.PlayerSeat == ChanshuangChooser(f))
        {
            var remaining = Math.Min(2 - chanshuang.Selected.Count, decision.Choices.Count(c => c.Cards.Count == 1));
            return NativeOwnedSelection(decision, f, $"{chanshuang.Stage}:{chanshuang.Cursor}", "option", ["card"], [],
                chanshuang.Selected, remaining, remaining);
        }
        if (f.YangFeng is { Stage: YangFengStage.DiscardChoice } jiawei && jiawei.TargetSeat == decision.PlayerSeat)
            return NativeOwnedSelection(decision, f, $"{jiawei.Stage}:{jiawei.TargetSeat}", "branch", ["discard"], [],
                jiawei.Selected.Select(c => c.CardId).ToArray(), jiawei.RequiredDiscard - jiawei.Selected.Count, jiawei.RequiredDiscard - jiawei.Selected.Count);
        if (f.SunYuan is { Stage: SunYuanStage.DiscardCards } dongxin && dongxin.Holder == decision.PlayerSeat && decision.PlayerSeat == f.OwnerSeat)
            return NativeOwnedSelection(decision, f, $"{dongxin.Stage}:{dongxin.Holder}", "branch", ["card"], [],
                dongxin.Selected, dongxin.Required - dongxin.Selected.Count, dongxin.Required - dongxin.Selected.Count);
        if (f.AnniversaryFengXi is { Stage: AnniversaryFengXiStage.DiscardChoice } yusui && decision.PlayerSeat == yusui.Target)
            return NativeOwnedSelection(decision, f, $"{yusui.Stage}:{yusui.Target}", "branch",
                decision.Choices.Select(c => c.Parameters.GetValueOrDefault("branch")).OfType<string>()
                    .Where(branch => branch.StartsWith("card-", StringComparison.Ordinal)).ToArray(), [],
                yusui.Cards, yusui.Amount - yusui.Cards.Count, yusui.Amount - yusui.Cards.Count);
        if (f.DaQiaoXiaoQiao is { Stage: DaQiaoXiaoQiaoStage.HandCards } xingwu && decision.PlayerSeat == f.OwnerSeat)
            return NativeOwnedSelection(decision, f, xingwu.Stage.ToString(), "branch", ["card"], [],
                xingwu.Selected.Select(c => c.Id).ToArray(), 2 - xingwu.Selected.Count, 2 - xingwu.Selected.Count);
        if (f.MouLuZhi is { Stage: MouLuZhiStage.CostChoice } zhenliang && decision.PlayerSeat == f.OwnerSeat)
            return NativeOwnedSelection(decision, f, $"{zhenliang.Stage}:{zhenliang.TargetSeat}", "branch", ["cost"], [],
                zhenliang.Cards, zhenliang.RequiredCards - zhenliang.Cards.Count, zhenliang.RequiredCards - zhenliang.Cards.Count);
        if (f.AnniversaryWangChang is { Stage: AnniversaryWangChangStage.OwnCards } kaiji && decision.PlayerSeat == f.OwnerSeat)
            return NativeOwnedSelection(decision, f, kaiji.Stage.ToString(), "verb", ["own"], ["pay"],
                kaiji.Cards, Math.Max(0, 1 - kaiji.Cards.Count), kaiji.Maximum - kaiji.Cards.Count);
        if (f.AnniversaryXunXu is { Stage: "discard" } boqia && decision.PlayerSeat == boqia.Target)
            return NativeOwnedSelection(decision, f, $"{boqia.Stage}:{boqia.Target}", "branch",
                decision.Choices.Select(c => c.Parameters.GetValueOrDefault("branch")).OfType<string>()
                    .Where(branch => branch.StartsWith("card-", StringComparison.Ordinal)).ToArray(), [],
                boqia.Cards, boqia.Count - boqia.Cards.Count, boqia.Count - boqia.Cards.Count);

        if (f.MouSunJianOlYipo is { Stage: MouSunJianOlYipoStage.Discard } yipo)
        {
            var remaining = Math.Min(yipo.DiscardCount, MouSunJianOlYipoDiscardable(decision.PlayerSeat).Length) - yipo.Selected.Count;
            return NativeOwnedSelection(decision, f, yipo.Stage.ToString(), "branch", ["card"], ["confirm"],
                yipo.Selected, remaining, remaining);
        }
        if (f.AnniversaryJiedao is { Stage: AnniversaryJiedaoStage.DiscardChoice } jiedao)
            return NativeOwnedSelection(decision, f, jiedao.Stage.ToString(), "option", ["pick"], ["finish"],
                jiedao.Selected, jiedao.Required - jiedao.Selected.Count, jiedao.Required - jiedao.Selected.Count);
        if (f.QinLangXianying is { Stage: QinLangXianyingStage.DiscardChoice } xianying)
        {
            var selected = xianying.Selected.Select(c => c.CardId).ToArray();
            var remaining = decision.Choices.Count(c => c.Cards is [var id] && !selected.Contains(id));
            var counts = Enumerable.Range(0, remaining + 1).Where(n =>
                !QinLangCountUsed(f.OwnerSeat, xianying.RoundNumber, selected.Length + n)).ToArray();
            return NativeOwnedSelection(decision, f, xianying.Stage.ToString(), "branch", ["card"], ["pay"],
                selected, counts.FirstOrDefault(), remaining, allowedCounts: counts);
        }
        if (f.MouDiaoChanMobile is { Stage: MouDiaoChanMobileStage.Cards } lijian)
            return NativeOwnedSelection(decision, f, lijian.Stage.ToString(), "branch", ["card"], ["pay"],
                lijian.Cards, lijian.Targets.Count - 1 - lijian.Cards.Count, lijian.Targets.Count - 1 - lijian.Cards.Count);
        if (f.MoDiaoChan is { Stage: MoDiaoChanStage.DiscardChoice, Op: SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound } huanhuo)
            return NativeOwnedSelection(decision, f, huanhuo.Stage.ToString(), "branch", ["card"], ["confirm"],
                huanhuo.Cards, 0, 2 - huanhuo.Cards.Count);
        if (f.XieLingYu is { Stage: XieLingYuStage.Cost } xinyou)
            return NativeOwnedSelection(decision, f, xinyou.Stage.ToString(), "branch", ["cost"], ["pay"],
                xinyou.Selected, Math.Max(0, 1 - xinyou.Selected.Count), int.MaxValue);
        if (f.SpZhangLiao is { Stage: SpZhangLiaoStage.Payment } mubing)
            return NativeOwnedSelection(decision, f, mubing.Stage.ToString(), "option", ["card"], ["confirm"],
                mubing.Selected, 0, int.MaxValue);
        if (f.MouXunYuAnniversary is { Stage: MouXyStage.Discard } shimou)
            return NativeOwnedSelection(decision, f, shimou.Stage.ToString(), "branch", ["add"], ["done"],
                shimou.Selected, shimou.Requested - shimou.Selected.Count, shimou.Requested - shimou.Selected.Count,
                resetValues: ["clear"]);
        if (f.TaoQian is { Stage: TaoQianStage.DiscardChoice } zongluan)
            return NativeOwnedSelection(decision, f, zongluan.Stage.ToString(), "tao-qian", ["discard-card"], ["discard-confirm"],
                zongluan.Selected, zongluan.DiscardRequired - zongluan.Selected.Count, zongluan.DiscardRequired - zongluan.Selected.Count);
        if (f.XiangChong is { Stage: XiangChongStage.Cards, Branch: "debt" } guying)
            return NativeOwnedSelection(decision, f, guying.Stage.ToString(), "branch", ["card"], ["pay"],
                guying.Selected.Select(c => c.CardId).ToArray(), guying.Required - guying.Selected.Count, guying.Required - guying.Selected.Count);
        if (f.KongShu is { Stage: KongShuStage.RoundDiscard } fuchao)
            return NativeOwnedSelection(decision, f, fuchao.Stage + ":" + fuchao.Cursor, "branch", ["pick"], ["discard"],
                fuchao.Selected, fuchao.DiscardCount - fuchao.Selected.Count, fuchao.DiscardCount - fuchao.Selected.Count);
        if (f.CaoYu is { Stage: CaoYuStage.XiangxuDiscardChoice } xiangxu)
            return NativeOwnedSelection(decision, f, xiangxu.Stage.ToString(), "branch", ["pick"], ["discard"],
                xiangxu.Selected, xiangxu.Requested - xiangxu.Selected.Count, xiangxu.Requested - xiangxu.Selected.Count);
        if (f.FeiYi is { Stage: FeiYiStage.DiscardChoice } yanru)
            return NativeOwnedSelection(decision, f, yanru.Stage.ToString(), "branch", ["pick"], ["finish"],
                yanru.Selected, Math.Max(0, yanru.Minimum - yanru.Selected.Count), int.MaxValue);
        if (f.ChengGongYing is { Stage: ChengGongYingStage.DiscardChoice } kuangxiang)
            return NativeOwnedSelection(decision, f, kuangxiang.Stage.ToString(), "branch", ["pick"], ["finish"],
                kuangxiang.Selected, kuangxiang.Count - kuangxiang.Selected.Count, kuangxiang.Count - kuangxiang.Selected.Count);
        if (f.Xianbi is { Stage: XianbiStage.DiscardChoice } xianbi)
            return NativeOwnedSelection(decision, f, xianbi.Stage.ToString(), "branch", ["pick"], ["pay"],
                xianbi.Selected, xianbi.Required - xianbi.Selected.Count, xianbi.Required - xianbi.Selected.Count);
        if (f.DingShangWan is { Stage: DingShangWanStage.OpeningDiscardChoice or DingShangWanStage.FengDiscardChoice } dsw)
            return NativeOwnedSelection(decision, f, dsw.Stage.ToString(), "branch", ["pick"], ["finish"],
                dsw.Selected, Math.Max(0, dsw.Minimum - dsw.Selected.Count), dsw.Maximum - dsw.Selected.Count);
        if (f.HanFu is { Stage: HanFuStage.DiscardChoice } kuangshou)
            return NativeOwnedSelection(decision, f, kuangshou.Stage.ToString(), "branch", ["discard"], [],
                kuangshou.SelectedDiscard ?? [], (kuangshou.RequiredDiscard ?? 0) - (kuangshou.SelectedDiscard?.Count ?? 0),
                (kuangshou.RequiredDiscard ?? 0) - (kuangshou.SelectedDiscard?.Count ?? 0));
        if (f.MouCaoHong is { Stage: MouCaoHongStage.DiscardCards } xianju)
        {
            var remaining = Math.Min(xianju.Requested, NativeCollectorMaterialCount(decision, "branch", ["add", "remove"])) - xianju.Selected.Count;
            return NativeOwnedSelection(decision, f, xianju.Stage.ToString(), "branch", ["add"], ["done"],
                xianju.Selected, remaining, remaining);
        }
        if (f.MouJiangJi is { Stage: MouJiangJiStage.Discard } yingshi)
        {
            var remaining = Math.Min(yingshi.Requested, NativeCollectorMaterialCount(decision, "branch", ["add", "remove"])) - yingshi.Selected.Count;
            return NativeOwnedSelection(decision, f, yingshi.Stage.ToString(), "branch", ["add"], ["done"],
                yingshi.Selected, remaining, remaining);
        }
        if (f.MouJiaXu is { Stage: MouJiaXuStage.Discard, Record: { } recorded } rushi)
        {
            var count = Math.Min(Math.Max(0, GetHand(_players[decision.PlayerSeat]).Count - recorded.HandCount),
                NativeCollectorMaterialCount(decision, "branch", ["add", "remove"])) - rushi.Selected.Count;
            return NativeOwnedSelection(decision, f, rushi.Stage.ToString(), "branch", ["add"], ["done"],
                rushi.Selected, count, count);
        }
        if (f.MouZhouYuAnniversary is { Stage: MouZhouYuStage.Discard } yingmou)
        {
            var count = Math.Min(Math.Max(0, GetHand(_players[decision.PlayerSeat]).Count - GetHand(_players[f.OwnerSeat]).Count),
                NativeCollectorMaterialCount(decision, "branch", ["add", "remove"])) - yingmou.Selected.Count;
            return NativeOwnedSelection(decision, f, yingmou.Stage.ToString(), "branch", ["add"], ["done"],
                yingmou.Selected, count, count);
        }
        if (f.MouLiuXie is { Stage: MouLiuXieStage.SelectingDiscard } zhanban)
            return NativeOwnedSelection(decision, f, $"{zhanban.Stage}:{zhanban.Cursor}:{zhanban.RewardCursor}:{zhanban.IsAdjustment}",
                "branch", ["card"], ["confirm"], zhanban.Selected,
                zhanban.Requested - zhanban.Selected.Count, zhanban.Requested - zhanban.Selected.Count);
        if (f.HuanShuJun is { Stage: HuanShuJunStage.AdjustHand } huanpei)
        {
            var remaining = Math.Max(0, GetHand(_players[f.OwnerSeat]).Count - Math.Max(0, _players[f.OwnerSeat].Hp)) - huanpei.Selected.Count;
            return NativeOwnedSelection(decision, f, huanpei.Stage.ToString(), "huan-shu-jun", ["cost-toggle"], ["cost-confirm"],
                huanpei.Selected, remaining, remaining);
        }
        if (f.MouGuoYouZhi is { Stage: MouGuoYouZhiStage.AdjustDiscard } bique)
        {
            var remaining = GetHand(_players[f.OwnerSeat]).Count - bique.DistinctNames - bique.Selected.Count;
            return NativeOwnedSelection(decision, f, bique.Stage.ToString(), "branch", ["discard-card"], ["discard-confirm"],
                bique.Selected, remaining, remaining);
        }
        if (f.MouLuZhiOl is { Stage: MouLuZhiOlStage.Red or MouLuZhiOlStage.TargetRed } sibing)
        {
            var remaining = sibing.Stage == MouLuZhiOlStage.TargetRed ? sibing.Required - sibing.Selected.Count : 0;
            return NativeOwnedSelection(decision, f, sibing.Stage.ToString(), "branch", ["card"], ["confirm"],
                sibing.Selected, remaining, sibing.Stage == MouLuZhiOlStage.Red ? int.MaxValue : remaining);
        }
        if (f.MouZhuRan is { Stage: MouZhuRanStage.Commit or MouZhuRanStage.HeyuanCost } zhenwei)
        {
            var remaining = zhenwei.Stage == MouZhuRanStage.HeyuanCost ? zhenwei.X - zhenwei.Selected.Count : 0;
            return NativeOwnedSelection(decision, f, zhenwei.Stage + ":" + zhenwei.Cursor, "branch", ["card"], ["confirm"],
                zhenwei.Selected.Select(c => c.CardId).ToArray(), remaining,
                zhenwei.Stage == MouZhuRanStage.Commit ? int.MaxValue : remaining);
        }
        if (f.DuanJiongSaogu is { Stage: DuanJiongSaoguStage.DiscardChoice } saogu)
            return NativeOwnedSelection(decision, f, saogu.Stage.ToString(), "branch", ["discard"], [],
                saogu.Selected.Select(c => c.CardId).ToArray(), saogu.RequiredCount - saogu.Selected.Count, saogu.RequiredCount - saogu.Selected.Count);
        if (f.WangKuangRenxia is { Stage: WangKuangRenxiaStage.DiscardChoice } renxia)
            return NativeOwnedSelection(decision, f, renxia.Stage + ":" + renxia.Cursor, "branch", ["card"], [],
                renxia.Selected.Select(c => c.CardId).ToArray(), renxia.RequiredCount - renxia.Selected.Count, renxia.RequiredCount - renxia.Selected.Count);
        return decision;
    }

    private static int NativeCollectorMaterialCount(PendingDecision decision, string key, IReadOnlyList<string> verbs) =>
        decision.Choices.Where(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault(key) is { } verb && verbs.Contains(verb))
            .Select(c => c.Cards[0]).Distinct().Count();

    private PendingDecision NativeOwnedSelection(PendingDecision decision, ProgramSkillFrame frame, string stage,
        string verbKey, IReadOnlyList<string> pickValues, IReadOnlyList<string> finishValues,
        IReadOnlyList<int> selected, int minimum, int maximum, IReadOnlyList<string>? resetValues = null,
        IReadOnlyList<int>? allowedCounts = null)
    {
        var selectedIds = selected.ToHashSet();
        var roles = decision.Choices.Select(choice =>
        {
            var verb = choice.Parameters.GetValueOrDefault(verbKey);
            var role = verb is not null && finishValues.Contains(verb) ? "finish" :
                verb is not null && resetValues?.Contains(verb) == true ? "reset" :
                verb is "unpick" or "remove" && choice.Cards is [var removed] && selectedIds.Contains(removed) ? "unpick" :
                verb is not null && pickValues.Contains(verb) && choice.Cards is [var id]
                    ? selectedIds.Contains(id) ? "unpick" : "pick" : null;
            return (Choice: choice, Role: role);
        }).ToArray();
        // Fail closed if a nominal self collector publishes another zone,
        // another target or content choice. A self target can label the payer.
        var tagged = roles.Where(c => c.Role is not null).ToArray();
        if (tagged.Length == 0 || tagged.Any(c => c.Choice.Targets.Any(seat => seat != decision.PlayerSeat) || c.Choice.ContentIds.Count != 0 ||
            c.Choice.Cards.Any(id => _cardZones.GetLocation(id) is not { Zone: CardZoneKind.Hand or CardZoneKind.Equipment } from ||
                from.OwnerSeat != decision.PlayerSeat)) || selected.Any(id =>
                _cardZones.GetLocation(id) is not { Zone: CardZoneKind.Hand or CardZoneKind.Equipment } from ||
                from.OwnerSeat != decision.PlayerSeat)) return decision;
        var picks = roles.Where(c => c.Role == "pick").ToArray();
        if (picks.Select(c => c.Choice.Cards[0]).Distinct().Count() != picks.Length) return decision;
        maximum = Math.Min(Math.Max(0, maximum), picks.Length);
        minimum = Math.Max(0, minimum);
        var allowed = allowedCounts?.Where(n => n >= minimum && n <= maximum).Distinct().Order().ToArray();
        var countsText = allowed is not null ? string.Join(',', allowed) : null;
        var choices = roles.Select(item =>
        {
            if (item.Role is null) return item.Choice;
            var parameters = new Dictionary<string, string>(item.Choice.Parameters)
            {
                ["owned-selection-role"] = item.Role,
                ["owned-selection-frame"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                ["owned-selection-stage"] = stage,
                ["owned-selection-selected"] = string.Join(',', selected)
            };
            if (countsText is not null) parameters["owned-selection-counts"] = countsText;
            return PromptChoiceProjection.Freeze(item.Choice with { Parameters = parameters });
        }).ToArray();
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        return decision with
        {
            RequiredCardCount = maximum,
            MinimumCardCount = minimum,
            Choices = Array.AsReadOnly(choices),
            SkillPrompt = decision.SkillPrompt ?? new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
    }
}
