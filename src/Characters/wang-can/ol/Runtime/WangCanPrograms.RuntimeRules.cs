namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wang_can_ol_WangCanPrograms
{
    internal static CardConversionSource WangCanSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);

    internal static CardLocation WangCanLease(int owner)=>new(CardZoneKind.PrivateReserve,owner);

    internal static string WangCanReason(WangCanStage stage)=>stage switch{WangCanStage.SanwenDiscard=>"skill-program.sanwen.discard",WangCanStage.SanwenDraw=>"skill-program.sanwen.draw",WangCanStage.QiaiGift=>"skill-program.qiai.give",WangCanStage.DenglouViewChildren=>"skill-program.denglou.private-lease",WangCanStage.DenglouGain=>"skill-program.denglou.obtain",WangCanStage.DenglouDiscard=>"skill-program.denglou.unusable-discard",WangCanStage.DenglouCancel=>"skill-program.denglou.cancel-cleanup",_=>"skill-program.wangcan.no-movement"};
}
