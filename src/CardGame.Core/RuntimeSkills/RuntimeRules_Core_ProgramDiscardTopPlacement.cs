namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramDiscardTopPlacement
{
    internal const string FanChouDiscardReason = "skill-program.xingluan.recipient.discard";

    internal const string FeiYiDiscardReason = "skill-program.fei-yi.yanru.discard";

    internal const string FengyanDiscardReason = "skill-program.ding-shang-wan.fengyan.discard";

    internal const string FudaoDiscardReason = "skill-program.ding-shang-wan.fudao.discard";

    internal const string GoudeDiscardReason = "skill-program.meng-da.goude.discard";

    internal const string HeJinDiscardReason = "skill-program.he-jin.yanhuo.discard";

    internal const string MouHanDangDiscardReason = "skill-program.mou-mobile:han-dang.discard";

    private const string PairedColorDiscardReason = CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardOrigins.PairedColorDiscardReason;

    internal const string QingyaDiscardReason = "skill-program.ya-dan.qingya.discard";

    internal const string SoleProtectionCostReason = "skill-program.sole-target-protection.cost";

    internal const string WeifuDiscardReason = "skill-program.qian-zhao.weifu.discard";

    internal const string ZhenyingDiscardReason = "skill-program.hao-pu.zhenying.discard";

    internal static bool IsDiscardMovementReason(CardMoveReason reason)
    {
        var value = reason.Value;
        if (value is MouHanDangDiscardReason or "skill-program.le-xiao-qiao.weiwan.discard" or QingyaDiscardReason or "skill-program.guo-tu.weijie.discard" or "skill-program.li-yi.chanshuang.discard" or "skill-program.sun-yuan.dongxin.discard" or "skill-program.cao-yu.xiangxu.discard" or "skill-program.sun-hong.xianbi.discard") return true;
        if (value is GoudeDiscardReason or PairedColorDiscardReason or SoleProtectionCostReason or FanChouDiscardReason or ZhenyingDiscardReason or WeifuDiscardReason or FeiYiDiscardReason or FudaoDiscardReason or FengyanDiscardReason or HeJinDiscardReason or "skill-program.yan-rou.choutao.discard" or "skill-program.lu-yu-sheng.cangxin.discard" or "skill-program.lu-yu-sheng.runwei.discard" or "skill-program.yuan-huan.linjie.discard" or "skill-program.han-fu.kuangshou.discard" or "skill-program.wang-kuang.renxia.discard" or
            "skill-program.duan-jiong.saogu.discard" or "skill-program.ma-mi-di.bingjie.discard" or
            "skill-program.cheng-gong-ying.kuangxiang.discard" or "skill-program.xiang-chong.guying.debt" or
            "skill-program.yang-feng.jiawei.discard" or "skill-program.qin-lang.xianying.discard" or
            "skill-program.sun-hao.canshi.discard" or
            "skill-program.wen-yang.qingjiao.hand-discard" or "skill-program.wen-yang.qingjiao.ending-discard" or
            "skill-program.da-qiao-xiao-qiao.xingwu.hand-discard" or "skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard" or
            "skill-program.sanwen.discard" or "skill-program.denglou.unusable-discard" or
            "skill-program.shanjia.discard" or "skill-program.neifa.discard" or "skill-program.huaman.discard") return true;
        if (value is "card.effect.dismantlement" or "card.effect.dismantlement-judgment" or
            "mode.identity.lord-killed-loyalist" or "skill.gongqi.cost" or
            "program.phase-name-prediction.cost") return true;
        if (value.StartsWith("skill-program.", StringComparison.Ordinal))
        {
            // Only these exact paid producer suffixes are discards.
            if (value.EndsWith($".{nameof(SkillProgramEffectOp.DiscardTurnOverAndTakeHand)}.payment", StringComparison.Ordinal)) return true;
            if (value.EndsWith($".{nameof(SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach)}.discard", StringComparison.Ordinal) ||
                value.EndsWith($".{nameof(SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest)}.discard", StringComparison.Ordinal) ||
                value.EndsWith($".{nameof(SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp)}.discard", StringComparison.Ordinal) ||
                value.EndsWith(".draw-discard-category.discard", StringComparison.Ordinal) ||
                value.EndsWith(".recipient-category.discard", StringComparison.Ordinal) ||
                value.EndsWith(".last-damage-source.discard", StringComparison.Ordinal) ||
                value.EndsWith(".outside-phase-discard", StringComparison.Ordinal) ||
                value.EndsWith(".same-name-hand.discard", StringComparison.Ordinal) ||
                value.EndsWith(".completed-undamaged-target.discard", StringComparison.Ordinal) ||
                value.EndsWith(".kangge-death-price", StringComparison.Ordinal) ||
                value.EndsWith(".liejie-discard", StringComparison.Ordinal)) return true;
            var operation = value[(value.LastIndexOf('.') + 1)..];
            return operation is nameof(SkillProgramEffectOp.ResolveGameTargetHandHpChoice) or nameof(SkillProgramEffectOp.DiscardSelected) or nameof(SkillProgramEffectOp.MoveBoundCards) or
                nameof(SkillProgramEffectOp.DiscardOwnedZoneCards) or nameof(SkillProgramEffectOp.SelectAndMoveOwnedCard) or
                nameof(SkillProgramEffectOp.ChooseOwnCardDiscard) or nameof(SkillProgramEffectOp.ChooseOtherOwnedCardDiscard) or
                nameof(SkillProgramEffectOp.ChooseDifferentCategoryDiscard) or nameof(SkillProgramEffectOp.DiscardParticipantCards) or
                nameof(SkillProgramEffectOp.DiscardTargetEquipment) or nameof(SkillProgramEffectOp.TakeSelectedTargetCards) or
                nameof(SkillProgramEffectOp.ChooseCategoryAlternativeDiscard) or nameof(SkillProgramEffectOp.EscalatingDiscardOrDamage) or
                nameof(SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard) or nameof(SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected) or
                nameof(SkillProgramEffectOp.RequestAttackRangeAid) or nameof(SkillProgramEffectOp.PayEquipmentColorDiscard) or
                nameof(SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp) or
                nameof(SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage) or nameof(SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit) or nameof(SkillProgramEffectOp.PlaceMatchedJudgmentCard) or
                nameof(SkillProgramEffectOp.ResolvePrepDiscardOrEnding) or nameof(SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast);
        }
        // Recasting, use/response cleanup, death cleanup and replacement are not discards.
        if (value.StartsWith("card.recast.", StringComparison.Ordinal) || value.Contains("death", StringComparison.Ordinal) ||
            value.Contains("finished", StringComparison.Ordinal) || value is "card.harvest-discard" or
            "equipment.wooden-ox.grain-discard" or "skill.zaiqi.discard") return false;
        return value.EndsWith(".discard", StringComparison.Ordinal) ||
            value.EndsWith("-discard", StringComparison.Ordinal) || value.Contains(".discard-", StringComparison.Ordinal);
    }
}
