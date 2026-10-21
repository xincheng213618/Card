namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShenZfAdjustment="shencai-colorless-targets";
    private bool IsShenZfAdjustmentAction(CharacterState actor,LegalAction action) => action.ProgramSkillId == ShenZhangFeiComposition.Xunshi &&
        action.ProgramActivationId == ShenZfAdjustment && HasShenZfXunshi(actor) && action.CardId is not null &&
        (action.CardId == 0 || ShenZfMaterialsColorless(actor,[action.CardId.Value]));
    private IEnumerable<IReadOnlyList<int>> ShenZfTargetSupersets(IReadOnlyList<int> normal,IReadOnlyList<int> extra)
    {
        for (var mask=1;mask<(1<<extra.Count);mask++)
            yield return Array.AsReadOnly(normal.Concat(extra.Where((_,i)=>(mask&(1<<i))!=0)).ToArray());
    }
    private IEnumerable<IReadOnlyList<int>> ShenZfBorrowedSupersets(CharacterState actor,IReadOnlyList<int> normal,Suit? suit,bool? color,Suit? shield)
    {
        var holders=_players.Where(p=>p.IsAlive&&p.Seat!=actor.Seat&&!normal.Where((_,i)=>i%2==0).Contains(p.Seat)&&GetWeapon(p)is not null&&
            !IsDirectedCardTargetProhibited(actor.Seat,p.Seat,CardKind.BorrowedSword)&&!IsCardTargetProhibited(p,CardKind.BorrowedSword,suit,color)&&
            !HasBeneficiarySuitShield(actor.Seat,p.Seat,shield)).OrderBy(p=>RelativeTableOrder(actor.Seat,p.Seat)).ToArray();
        return Visit(0,normal,false);
        IEnumerable<IReadOnlyList<int>> Visit(int index,IReadOnlyList<int> targets,bool added)
        {
            if(index==holders.Length){if(added)yield return Array.AsReadOnly(targets.ToArray());yield break;}
            foreach(var result in Visit(index+1,targets,added))yield return result;
            foreach(var victim in _players.Where(p=>IsLegalBorrowedSwordSlashTarget(holders[index],p)))
                foreach(var result in Visit(index+1,[..targets,holders[index].Seat,victim.Seat],true))yield return result;
        }
    }
    [ProgramCardAction(ProgramCardActionPhase.AfterProgram,38600)] private void AddShenZfOtherColorlessActions(List<LegalAction> actions,CharacterState actor)
    {
        if(!HasShenZfXunshi(actor))return;
        var keys=actions.Select(a=>(a.CardId,a.Kind,a.PlayedCardKind,a.ConversionSource,a.TargetCardId,Targets:string.Join(',',a.TargetSeats))).ToHashSet();
        foreach(var action in actions.ToArray().Where(a=>a.CardId is not null&&a.ProgramActivationId is null&&a.Kind!=LegalActionKind.Recast))
        {
            var(kind,suit)=TargetAdjustmentAppearance(actor,action);
            if(IsSlashCard(kind)||suit!=Suit.None||EquipmentCatalog.IsEquipment(kind)||IsDelayedCard(kind))continue;
            var normal=action.Kind is LegalActionKind.DrawTwo or LegalActionKind.Peach or LegalActionKind.Alcohol ? new[] { actor.Seat } : GetDeclaredCardTargets(actor,action.Kind,action.TargetSeats).ToArray();
            if(normal.Length==0)continue;
            IEnumerable<IReadOnlyList<int>> targets;
            if(kind==CardKind.BorrowedSword)targets=ShenZfBorrowedSupersets(actor,action.TargetSeats,Suit.None,null,Suit.None);
            else
            {
                var extra=_players.Where(p=>!normal.Contains(p.Seat)&&CanBeExtraTargetAdjustmentTarget(actor,p,action,kind,Suit.None)&&
                    !(p.Seat==actor.Seat&&action.ConversionSource is{}source&&ViewAsRule(source)?.ExcludeOwnerEffects==true))
                    .OrderBy(p=>RelativeTableOrder(actor.Seat,p.Seat)).Select(p=>p.Seat).ToArray();
                targets=ShenZfTargetSupersets(normal,extra);
            }
            foreach(var seats in targets)
                if(keys.Add((action.CardId,action.Kind,action.PlayedCardKind,action.ConversionSource,action.TargetCardId,string.Join(',',seats))))
                    actions.Add(action with{TargetSeat=seats[0],TargetSeats=seats,ProgramSkillId=ShenZhangFeiComposition.Xunshi,ProgramActivationId=ShenZfAdjustment,Description=action.Description+"（巡使增加目标）"});
        }
    }
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> ShenZfOrdinaryTrickOptions(CharacterState actor,IReadOnlyList<ProgramOrdinaryTrickUseOption> ordinary,Suit? suit,bool? color,Suit? shield,bool excludeOwner)
    {
        if(!HasShenZfXunshi(actor)||suit is not(null or Suit.None)||suit is null&&color is not null)return ordinary;
        var options=ordinary.ToList();var keys=ordinary.Select(o=>(o.EffectiveCardKind,o.TargetCardId,Targets:string.Join(',',o.TargetSeats))).ToHashSet();
        foreach(var option in ordinary)
        {
            var kind=option.EffectiveCardKind;var normal=option.TargetSeats.Count==0&&kind==CardKind.DrawTwo ? new[] { actor.Seat } : option.TargetSeats.ToArray();
            if(normal.Length==0)continue;
            IEnumerable<IReadOnlyList<int>> targets;
            if(kind==CardKind.BorrowedSword)targets=ShenZfBorrowedSupersets(actor,normal,Suit.None,null,shield);
            else
            {
                var extra=_players.Where(p=>p.IsAlive&&!normal.Contains(p.Seat)&&(!excludeOwner||p.Seat!=actor.Seat)&&
                    !IsDirectedCardTargetProhibited(actor.Seat,p.Seat,kind)&&!IsCardTargetProhibited(p,kind,Suit.None,null)&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,shield)&&
                    (kind switch{CardKind.DrawTwo or CardKind.IronChain or CardKind.WumiaoFlood=>true,CardKind.Duel or CardKind.QizhengXiangsheng=>p.Seat!=actor.Seat,
                        CardKind.FireAttack=>GetHand(p).Count>0,CardKind.UnexpectedAssault=>p.Seat!=actor.Seat&&GetHand(p).Count>0,
                        CardKind.Snatch or CardKind.Dismantlement=>p.Seat!=actor.Seat&&GetHand(p).Count+GetEquipment(p).Count+GetJudgment(p).Count>0,_=>false}))
                    .OrderBy(p=>RelativeTableOrder(actor.Seat,p.Seat)).Select(p=>p.Seat).ToArray();
                targets=ShenZfTargetSupersets(normal,extra);
            }
            foreach(var seats in targets)
                if(keys.Add((kind,option.TargetCardId,string.Join(',',seats))))options.Add(option with{Id=new(option.Id.Value+".xunshi.targets-"+string.Join('-',seats)),
                    TargetSeats=seats,NextActualUseAdjusted=true,Description=option.Description+"（巡使增加目标）"});
        }
        return Array.AsReadOnly(options.ToArray());
    }
}
