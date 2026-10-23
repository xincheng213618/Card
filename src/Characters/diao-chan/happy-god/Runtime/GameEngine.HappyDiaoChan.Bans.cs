namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasHappyDiaoChan=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveHappyDiaoChanMeihun);
    private IEnumerable<HappyDiaoChanCharmIssuedEvent> HappyDcActiveCharms(int seat)=>ProgramEventHistory<HappyDiaoChanCharmIssuedEvent>().Where(e=>e.TargetSeat==seat&&
        !ProgramEventHistory<HappyDiaoChanCharmExpiredEvent>().Any(x=>x.FrameId==e.FrameId&&x.TargetSeat==seat&&x.Suit==e.Suit));
    private bool IsHappyDiaoChanSuitForbidden(int seat,Suit suit)=>HasHappyDiaoChan&&suit!=Suit.None&&HappyDcActiveCharms(seat).Any(e=>e.Suit==suit);
    private static PlayerMarkerKind HappyDcMarker(Suit suit)=>suit switch
    {Suit.Spade=>PlayerMarkerKind.HappyCharmSpade,Suit.Heart=>PlayerMarkerKind.HappyCharmHeart,Suit.Club=>PlayerMarkerKind.HappyCharmClub,Suit.Diamond=>PlayerMarkerKind.HappyCharmDiamond,_=>throw new InvalidOperationException("Charm requires a real declared suit.")};
    private void SyncHappyDcCharmMarker(long frameId,int seat,Suit suit)
    {
        var p=_players[seat];var kind=HappyDcMarker(suit);var before=p.Markers.GetValueOrDefault(kind);var after=HappyDcActiveCharms(seat).Any(e=>e.Suit==suit)?1:0;
        if(before==after)return;if(after==0)p.Markers.Remove(kind);else p.Markers[kind]=after;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frameId,seat,kind,after-before,after,null,"skill-program.happy:huoxin.charm"));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,42220)]private void ExpireHappyDcCharm(IGameEvent payload)
    {
        if(!HasHappyDiaoChan||payload is not TurnEndedEvent ended)return;
        var issued=HappyDcActiveCharms(ended.ActorSeat).ToArray();
        foreach(var grant in issued)AdvanceEventRulesAndQueueFact(new HappyDiaoChanCharmExpiredEvent(grant.FrameId,grant.TargetSeat,grant.Suit,ended.TurnNumber));
        foreach(var suit in issued.Select(e=>e.Suit).Distinct())SyncHappyDcCharmMarker(0,ended.ActorSeat,suit);
    }
    private bool HappyDcMaterialsForbidden(int seat,IEnumerable<int> material)
    {
        if(!HasHappyDiaoChan)return false;var ids=material.Distinct().ToArray();if(ids.Length==0)return false;
        var suits=ids.Select(id=>EffectiveSuit(_players[seat],GetAdvancedCard(id))).Distinct().ToArray();
        return suits is[var suit]&&IsHappyDiaoChanSuitForbidden(seat,suit);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan,42220)]private bool HappyDcMaterialBan(ProgramCardMaterialQuery q)=>HappyDcMaterialsForbidden(q.ActorSeat,q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan,42220)]private bool HappyDcResponseBan(ProgramNativeResponseQuery q)=>HappyDcMaterialsForbidden(q.Provider.Seat,q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,42220)]private bool HappyDcViewAsBan(ProgramViewAsLegalityQuery q)=>HappyDcMaterialsForbidden(q.Owner.Seat,q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation,42220)]private void HappyDcSubmittedUse(ProgramSubmittedUseQuery q)
    {if(HappyDcMaterialsForbidden(q.ActorSeat,q.CardIds))throw new InvalidOperationException("The recipient's active Charm prohibits using this effective suit until their actual turn ends.");}
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind,42220)]private bool HappyDcProjectedUse(ProgramLegalActionQuery q)=>
        q.Action.Kind is not(LegalActionKind.Recast or LegalActionKind.UseProgramSkill)&&q.Action.MaxCardCount<=1&&q.Action.CardId is>0&&HappyDcMaterialsForbidden(q.Actor.Seat,[q.Action.CardId.Value]);
}
