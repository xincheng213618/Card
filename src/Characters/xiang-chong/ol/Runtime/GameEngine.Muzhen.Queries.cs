using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MuzhenUsed(int owner,string branch,long token)=>ProgramEventHistory<XiangChongBranchPaidEvent>().Any(e=>e.OwnerSeat==owner&&e.Branch==branch&&e.PhaseToken==token);
    private bool MuzhenTarget(ProgramSkillFrame? f,int owner,int target,string branch)=>target!=owner&&IsValidPlayerSeat(target)&&_players[target].IsAlive&&
        (branch=="gift"?GetEquipment(target).Count>0&&XiangOwned(owner,f).Count>=2:XiangOwned(owner,f).Any(c=>EquipmentCatalog.IsEquipment(c.Kind)&&CanPlaceOwnedEquipment(owner,target,GetAdvancedCard(c.CardId),c.From)));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,27140,SkillProgramEffectOp.ResolveMuzhenExchange)]
    private bool AllowsMuzhen(ProgramActivationLegalityContext gate)=>XiangPlayToken(gate.OwnerSeat)is>0 and var token&&gate.Activation.Effects is[{StateId:{}branch}]&&!MuzhenUsed(gate.OwnerSeat,branch,token)&&_players.Any(t=>MuzhenTarget(null,gate.OwnerSeat,t.Seat,branch));
    private void PlaceMuzhen(ProgramSkillFrame f)
    {
        var r=f.XiangChong!;var c=r.Selected.Single();var card=GetAdvancedCard(c.CardId);if(card.IsGeneralWeapon||!CanPlaceOwnedEquipment(f.OwnerSeat,r.Recipient,card,c.From))throw new InvalidOperationException("Muzhen's true equipment placement is no longer legal.");
        var slot=EquipmentCatalog.Get(card.Kind).Slot;var equipped=GetEquipment(r.Recipient).Where(x=>EquipmentCatalog.Get(x.Kind).Slot==slot).ToArray();var old=equipped.Length>=_players[r.Recipient].EquipmentSlotCapacity(slot)?equipped[0]:null;
        var all=r.Selected.Concat(old is null?Array.Empty<XiangChongCard>():[new(old.Id,old.Kind,CardLocation.Equipment(r.Recipient))]);
        var grain=all.Where(x=>x.Kind==CardKind.WoodenOx&&x.From.OwnerSeat is not null&&x.From.Zone==CardZoneKind.Equipment).SelectMany(x=>GetWoodenOxGrain(_players[x.From.OwnerSeat!.Value]).Select(y=>new XiangChongCard(y.Id,y.Kind,CardLocation.WoodenOxGrain(x.From.OwnerSeat.Value)))).ToArray();
        ReplaceRuntimeTop(f=f with{XiangChong=r with{Paid=true,Stage=XiangChongStage.PaymentChildren,Before=_movementSequence,After=_movementSequence,Payments=r.Selected,Replaced=old is null?null:new(old.Id,old.Kind,CardLocation.Equipment(r.Recipient)),Auxiliaries=grain}});
        if(old is not null){CopyFirstReplacedWeapon(card,old);MoveCard(old,CardLocation.Equipment(r.Recipient),CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace);}
        MoveCard(card,c.From,CardLocation.Equipment(r.Recipient),CardMoveReasons.EquipmentEnter);
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{XiangChong=f.XiangChong! with{After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id,r.Recipient,slot,card.Id,card.Kind,old?.Id));
        foreach(var m in _cardMovements.Where(m=>m.Sequence>f.XiangChong!.Before&&m.Sequence<=_movementSequence))AdvanceEventRulesAndQueueFact(new XiangChongEntityMovedEvent(f.Id,"placement",m.CardId,m.CardKind,m.From,m.To,m.Sequence));
        AdvanceEventRulesAndQueueFact(new XiangChongBranchPaidEvent(f.Id,f.OwnerSeat,r.Recipient,r.Branch,r.PhaseToken,f.XiangChong!.Before,f.XiangChong.After));
    }
}
