using CardGame.Core;

internal static class NativeDiscardClassificationChecks
{
    public static void ExactPaidProducersExcludeNeighborReasons()
    {
        foreach (var reason in new[]
        {
            "skill-program.huaman.discard",
            "skill-program.hao-pu.zhenying.discard",
            "skill-program.qian-zhao.weifu.discard",
            "skill-program.fei-yi.yanru.discard",
            "skill-program.ding-shang-wan.fudao.discard",
            "skill-program.ding-shang-wan.fengyan.discard",
            "skill-program.he-jin.yanhuo.discard",
            "skill-program.yan-rou.choutao.discard",
            "skill-program.lu-yu-sheng.cangxin.discard",
            "skill-program.lu-yu-sheng.runwei.discard",
            "skill-program.yuan-huan.linjie.discard",
            "skill-program.han-fu.kuangshou.discard",
            "skill-program.wang-kuang.renxia.discard",
            "skill-program.duan-jiong.saogu.discard",
            "skill-program.ma-mi-di.bingjie.discard",
            "skill-program.cheng-gong-ying.kuangxiang.discard",
            "skill-program.xiang-chong.guying.debt",
            "skill-program.yang-feng.jiawei.discard",
            "skill-program.qin-lang.xianying.discard",
            "skill-program.fixture:refund.draw-discard-category.discard",
            "skill-program.fixture:reciprocity.last-damage-source.discard",
            "program.phase-name-prediction.cost",
            "skill-program.xingluan.recipient.discard",
            "skill-program.fixture:outside.outside-phase-discard"
            , "skill-program.fixture:demand.same-name-hand.discard"
            , "skill-program.fixture:reveal.completed-undamaged-target.discard"
        })
            Require(GameEngine.IsDiscardMovementReason(new(reason)), $"Actual discard producer must qualify: {reason}");
        foreach (var reason in new[]
        {
            "skill-program.huaman.draw",
            "skill-program.hao-pu.zhenying.draw",
            "skill-program.qian-zhao.weifu.draw",
            "skill-program.fei-yi.draw",
            "skill-program.ding-shang-wan.fudao.discard-finished",
            "skill-program.ding-shang-wan.fengyan.give",
            "skill-program.he-jin.yanhuo.discard-finished",
            "skill-program.he-jin.mouzhu.give",
            "skill-program.yan-rou.choutao.discard-finished",
            "skill-program.yan-rou.xiangshu.draw",
            "skill-program.lu-yu-sheng.cangxin.discard-finished",
            "skill-program.lu-yu-sheng.runwei.draw",
            "skill-program.yuan-huan.deru.gain",
            "skill-program.yuan-huan.linjie.discard-finished",
            "skill-program.han-fu.kuangshou.discard-finished",
            "skill-program.han-fu.kuangshou.draw",
            "skill-program.han-fu.shuzi.give",
            "skill-program.wang-kuang.renxia.discard-finished",
            "skill-program.wang-kuang.renxia.draw",
            "skill-program.duan-jiong.saogu.draw",
            "skill-program.duan-jiong.saogu.discard-finished",
            "skill-program.ma-mi-di.bingjie.discard-finished",
            "skill-program.cheng-gong-ying.kuangxiang.discard-finished",
            "skill-program.cheng-gong-ying.kuangxiang.draw",
            "skill-program.xiang-chong.guying.debt-finished",
            "skill-program.xiang-chong.guying.gift",
            "skill-program.xiang-chong.guying.return",
            "skill-program.xiang-chong.muzhen.claim",
            "skill-program.yang-feng.jiawei.discard-finished",
            "skill-program.yang-feng.jiawei.reveal",
            "skill-program.yang-feng.qujia.slash-gain",
            "skill-program.qin-lang.xianying.discard-finished",
            "skill-program.qin-lang.xianying.draw",
            "skill-program.tian-chou.shandao.top",
            "skill-program.ma-mi-di.zhengding.grow",
            "skill-program.fei-yi.yanru.discard-finished",
            "skill-program.qian-zhao.weifu.discard-finished",
            "skill-program.hao-pu.zhenying.discard-finished",
            "skill-program.huaman.claim",
            "skill-program.liehou.hand-relay",
            "skill-program.fixture:refund.draw-discard-category.draw",
            "skill-program.fixture:refund.draw-discard-category.bonus",
            "skill-program.fixture:reciprocity.last-damage-source.draw",
            "program.phase-name-prediction.reveal",
            "skill-program.xingluan.field-gain",
            "skill-program.xingluan.deck-gain",
            "skill-program.xingluan.recipient-gift",
            "skill-program.xingluan.recipient.discard-finished",
            "program.phase-name-prediction.cost-finished",
            "skill-program.fixture:outside.outside-phase-draw",
            "skill-program.fixture:demand.same-name-hand.damage",
            "skill-program.fixture:reveal.completed-undamaged-target.reveal",
            "skill-program.fixture:unknown.cost",
            "card.recast.discard",
            "card.use.finished",
            "card.response.finished",
            "equipment.replacement",
            "equipment.wooden-ox.grain-discard",
            "player.death-discard",
            "card.harvest-discard"
        })
            Require(!GameEngine.IsDiscardMovementReason(new(reason)), $"Adjacent gain, recast and cleanup must stay excluded: {reason}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
