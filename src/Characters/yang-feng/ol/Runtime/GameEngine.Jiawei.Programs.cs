namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool IsJiaweiDamageCard(CardKind kind) => IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.Lightning or CardKind.UnexpectedAssault;
    private IReadOnlyList<YangFengMaterial> JiaweiDiscardCandidates(ProgramSkillFrame f,int target) => Array.AsReadOnly(
        BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,target,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Discard,
            canSelect:(zone,card)=>!IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,card,new(zone,target),OwnedCardMoveIntent.Discard))
        .Select(c => { var from = new CardLocation(Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]),target);var slot = int.Parse(c.Parameters["slot-index"]);var card = _cardZones.CardsAt(from)[slot];return new YangFengMaterial(card.Id,card.Kind,from,slot,card.IsGeneralWeapon); }).ToArray());
    private void PayJiaweiDiscard(ProgramSkillFrame f)
    {
        var r=f.YangFeng!;if(r.Selected.Count!=r.RequiredDiscard || r.Selected.Any(m=>_cardZones.GetLocation(m.CardId)!=m.From)) throw new InvalidOperationException("Jiawei cannot substitute a frozen original discard entity.");
        ReplaceRuntimeTop(f=f with{YangFeng=r with{Stage=YangFengStage.DiscardChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveYangFengEntities(f,r.Selected,CardLocation.DiscardPile,JiaweiDiscard,YangFengStage.DiscardChildren,_movementSequence);
    }
    private void RevealJiawei(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f=f with{YangFeng=f.YangFeng! with{Stage=YangFengStage.RevealChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var before=_movementSequence;RevealProgramTopCards(f.Id,f.OwnerSeat,checked(f.YangFeng!.X+1),null,JiaweiRevealBind,SkillProgramCardSetVisibility.Public);
        f=GetActiveProgramFrame(f.Id);var entities=GetProgramCardSet(f,JiaweiRevealBind).CardIds.Select((id,index)=>
        {var card=GetAdvancedCard(id);return new JiaweiRevealedEntity(new(id,card.Kind,CardLocation.Processing,index),IsJiaweiDamageCard(card.Kind),_cardMovements.Last(m=>m.CardId==id).Sequence);}).ToArray();
        ReplaceRuntimeTop(f with{YangFeng=f.YangFeng! with{Revealed=entities}});
        for(var i=0;i<entities.Length;i++) AdvanceEventRulesAndQueueFact(new JiaweiEntityRevealedEvent(f.Id,i,entities[i]));
        RecordYangFengMovement(f.Id,YangFengStage.RevealChildren,$"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.reveal",before,entities.Length);
    }
    private bool IsJiaweiOriginalProcessing(JiaweiRevealedEntity e) => _cardZones.GetLocation(e.Material.CardId)==CardLocation.Processing && _cardMovements.Last(m=>m.CardId==e.Material.CardId).Sequence==e.RevealSequence;
    private void SettleJiawei(ProgramSkillFrame f,YangFengStage stage)
    {
        var r=f.YangFeng!;var cleanup=stage==YangFengStage.CleanupChildren;var recipient=stage==YangFengStage.OwnerGainChildren?f.OwnerSeat:r.TargetSeat;
        var entities=r.Revealed.Where(e=>IsJiaweiOriginalProcessing(e)&&(cleanup||e.DamageCard==(stage==YangFengStage.OwnerGainChildren))).Select(e=>e.Material).ToArray();
        if(!cleanup&&!_players[recipient].IsAlive) entities=[];
        ReplaceRuntimeTop(f=f with{YangFeng=r with{Stage=stage},PendingMovementContinuation=new(f.OwnerSeat,0,null),CardSetBindings=Array.AsReadOnly(f.CardSetBindings.Where(b=>b.Name!=JiaweiRevealBind).ToArray())});
        MoveYangFengEntities(f,entities,cleanup?CardLocation.DiscardPile:CardLocation.Hand(recipient),cleanup?JiaweiCleanup:stage==YangFengStage.OwnerGainChildren?JiaweiOwnerGain:JiaweiTargetGain,stage,_movementSequence);
    }
}
