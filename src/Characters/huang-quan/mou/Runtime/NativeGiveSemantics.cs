namespace CardGame.Core;
// The legacy generic Move instructions also implement obtain, exchange and placement.
// Only these individually audited Give bindings opt in; reason text is never inspected.
internal static class NativeGiveSemantics
{
    internal static bool Selected(ProgramSkillFrame f,SkillProgramEffect e)=>e.Op==SkillProgramEffectOp.SelectAndMoveOwnedCard&&(f.SkillId,f.TriggerId??f.ActivationId)is
        ("classic:qiuyuan","share-the-slash")or("classic:enyuan","repay-each-damage")or("classic:kangkai","nearby-slash-target")or
        ("ol:panshi","preparation-tribute")or("classic:danshou","escalating-discard")or("classic:zenhui","single-target-role-choice")or
        ("boundary:fanjian","show-and-give")or("ol:fenglve","contest")or("ol:moushi","gift");
    internal static int? BoundGiver(ProgramSkillFrame f,string bind,IReadOnlyList<CardLocation> sources,CardLocation destination)
    {
        if(destination.Zone!=CardZoneKind.Hand)return null;
        if(f.SkillId=="ol:yanjiao"&&bind is "group-owner" or "group-chooser")return f.SelectedTargetSeats.Single();
        if((f.SkillId,bind)is("classic:zhige","payment")or("classic:feijun","given")or("shenwu:transplant-wood-resolution","given-cards"))
            return sources.Select(x=>x.OwnerSeat).Distinct().ToArray()is[var seat]?seat:null;
        return(f.SkillId,bind)is("classic:mingce","command-gift")or("boundary:wangxi","gift")or("classic:mingjian","all-hand")or("classic:haoshi-give","haoshi-gift")?f.OwnerSeat:null;
    }
}
