using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasWangYunPlanCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash);
    private SkillProgramStepOutcome ResolveLianjiPaidWeaponAndSlash(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.WangYunPlan is not null || !WangYunActivationMatches(f,SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash) || f.SelectedCardIds is not[var card] ||
            f.SelectedTargetSeats is not[var actor] || actor==f.OwnerSeat || !_players[actor].IsAlive || _cardZones.GetLocation(card)!=CardLocation.Hand(f.OwnerSeat) ||
            IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,_cardZones.CardsAt(CardLocation.Hand(f.OwnerSeat)).Single(c=>c.Id==card),CardLocation.Hand(f.OwnerSeat),OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("Lianji lost its one real hand payment and other actual weapon user.");
        var r=new WangYunPlanReceipt{Source=WangYunSource(f),GameplayHash=f.GameplayHash,Turn=_turnNumber,PhaseInstance=_cardUseDebitPhaseInstanceId,InstructionIndex=1,
            Actor=actor,CostCardId=card,Stage="cost"};ReplaceRuntimeTop(f=f with{WangYunPlan=r});
        AdvanceEventRulesAndQueueFact(new WangYunPlanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.PhaseInstance,actor,card));
        PayWangYunPlanMovement(f,card,CardLocation.Hand(f.OwnerSeat),CardLocation.DiscardPile,"weapon");return SkillProgramStepOutcome.AwaitChild;
    }
    private void PayWangYunPlanMovement(ProgramSkillFrame f,int card,CardLocation from,CardLocation to,string next)
    {
        if(_cardZones.GetLocation(card)!=from)throw new InvalidOperationException("The paid plan lost its actual current entity.");
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{WangYunPlan=f.WangYunPlan! with{Stage="movement",ReturnStage=next,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveProgramCardsFromMultipleSources([card],to,new(WangYunPlanMoveReason),(_,records)=>
        {
            var active=GetActiveProgramFrame(f.Id);if(records.Count!=1 || records[0].CardId!=card || records[0].From!=from || records[0].To!=to && records[0].To!=CardLocation.OutsideGame)
                throw new InvalidOperationException("The plan payment did not move its exact accepted entity once.");
            ReplaceRuntimeTop(active with{WangYunPlan=active.WangYunPlan! with{After=_movementSequence,CostPaid=active.WangYunPlan.CostPaid || next=="weapon"}});
            AdvanceEventRulesAndQueueFact(new WangYunPlanMovementPaidEvent(f.Id,next,card,from,records[0].To,before,_movementSequence));
        });if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
    }
    private void IssueWangYunWeapon(ProgramSkillFrame f)
    {
        var r=f.WangYunPlan!;var actor=_players[r.Actor];
        if(!_players[f.OwnerSeat].IsAlive || !actor.IsAlive || _winner!=Winner.None){FinishWangYun(f);return;}
        var weapons=_cardZones.CardsAt(CardLocation.DrawPile).Where(c=>EquipmentCatalog.IsEquipment(c.Kind) && EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Weapon &&
            actor.EquipmentSlotCapacity(EquipmentSlot.Weapon)>0 && !IsCardUseForbidden(actor.Seat,c.Kind,CardActionType.Use) && !IsSelfTargetForbiddenAction(actor,c.Kind,[actor.Seat]) &&
            !IsResponseEntityRestricted(actor.Seat,c.Id) && !IsPlayPhasePhysicalCardRestricted(actor,c)).OrderBy(c=>c.Id).ToArray();
        if(weapons.Length==0){ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="target"}});PublishWangYun(f);return;}
        var card=weapons[_random.Next(weapons.Length)];ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="issuing-weapon",WeaponCardId=card.Id,IssuingIds=[card.Id],IssuingKind=card.Kind,NativeUse=null,NativeReturned=false}});
        var id=BeginCardUse(card,actor.Seat,[],conversionSource:r.Source);MoveCard(card,CardLocation.DrawPile,CardLocation.Processing,CardMoveReasons.EquipmentUse);CompleteWangYunNativePayment(id);
        if(!TryBeginEquipmentTargetPrograms(actor,card,id))CompleteEquipmentUse(actor,card,id);
    }
    private IReadOnlyList<PromptChoice> WangYunPlanChoices(ProgramSkillFrame f)
    {
        var r=f.WangYunPlan!;var actor=_players[r.Actor];var choices=new List<PromptChoice>();
        void Add(string branch,string key,string text,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>choices.Add(new(new($"wang-yun.{f.Id}.{key}"),text,cards,targets,WangYunArgs(f,branch)));
        if(r.Stage=="target")
            foreach(var p in _players.Where(p=>p.IsAlive && p.Seat!=actor.Seat))Add("target",$"target-{p.Seat}",$"指定 {p.Name} 为【杀】目标",[],[p.Seat]);
        if(r.Stage=="branch")
        {
            if(r.SlashTarget is{}target && _players[target].IsAlive)
            {
                bool Legal(Card c,CardKind k,CardConversionSource?source=null,IReadOnlyList<int>?ids=null)=>
                    CanUseSlashTarget(actor,_players[target],c,source,k,physicalCardIds:ids) && AssistedSlashPaymentHasRange(actor,target,ids?.Select(GetAttackCard).ToArray()??[c]);
                if(_contentRegistry.ProgramDependencies.UsesRoundOnceCardlessBasics)
                foreach(var(source,rule)in RoundOnceCardlessRules(actor,CardKind.Slash))
                {
                    var logical=new Card(0,CardKind.Slash,Suit.None,0);
                    if(!CanUseSlashTarget(actor,_players[target],logical,source,CardKind.Slash,physicalCardIds:[])||!IsLegalBorrowedSwordSlashTarget(actor,_players[target],CardKind.Slash,Suit.None,false,null,0,[]))continue;
                    var args=WangYunArgs(f,"slash");args["effective-kind"]=CardKind.Slash.ToString();args["round-once-cardless"]="true";AddConversionParameters(args,source);
                    choices.Add(new(new($"wang-yun.{f.Id}.cardless-{source.SkillInstanceId}-{source.BindingId}"),"以本轮实际卫境额度视为使用【杀】",[],[target],args));
                }
                foreach(var card in GetSlashUseCards(actor))foreach(var kind in GetSlashUseKinds(actor,IsSlashCard(card.Kind)?card.Kind:CardKind.Slash))
                {
                    if(!Legal(card,kind) || IsTurnPhysicalUseForbidden(actor.Seat,[card.Id]))continue;
                    var args=WangYunArgs(f,"slash");args["effective-kind"]=kind.ToString();
                    choices.AddRange(CreateConversionChoiceVariants(actor,card,kind,false,$"wang-yun.{f.Id}.slash-{card.Id}-{kind}",$"对 {_players[target].Name} 使用【{CardCatalog.Get(kind).DisplayName}】",[card.Id],[target],args));
                }
                foreach(var pair in GetZhangbaSlashPairs(actor))
                {
                    var ids=pair.Select(c=>c.Id).ToArray();if(!Legal(pair[0],CardKind.Slash,ids:ids) || IsTurnPhysicalUseForbidden(actor.Seat,ids))continue;
                    var args=WangYunArgs(f,"slash");args["effective-kind"]=CardKind.Slash.ToString();args["equipment"]=CardKind.ZhangbaSerpentSpear.ToString();
                    choices.Add(new(new($"wang-yun.{f.Id}.zhangba-{string.Join('-',ids)}"),"使用丈八蛇矛的实际两张手牌",ids,[target],args));
                }
                foreach(var kind in SlashKinds)foreach(var selected in GetProgramMultiCardViewAsSelections(actor,kind,false).Where(s => !IsLeiluanSource(s.Source) || s.OutputKind == kind))
                {
                    var ids=selected.Cards.Select(c=>c.Id).ToArray();if(!Legal(selected.Cards[0],kind,selected.Source,ids) || IsTurnPhysicalUseForbidden(actor.Seat,ids))continue;
                    var args=WangYunArgs(f,"slash");args["effective-kind"]=kind.ToString();AddConversionParameters(args,selected.Source);
                    choices.Add(new(new($"wang-yun.{f.Id}.multi-{selected.Source.SkillInstanceId}-{selected.Source.BindingId}-{string.Join('-',ids)}"),"将自己的实际材料当【杀】使用",ids,[target],args));
                }
            }
            Add("transfer","transfer","不使用【杀】，令王允转移你装备区的武器",[],[]);
        }
        if(r.Stage=="transfer")foreach(var card in GetEquipment(actor).Where(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Weapon))foreach(var p in _players.Where(p=>p.IsAlive))
            Add("give-weapon",$"give-{card.Id}-{p.Seat}",$"将【{card.DisplayName}】交给 {p.Name}",[card.Id],[p.Seat]);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void IssueWangYunSlash(ProgramSkillFrame f,PromptChoice selected)
    {
        var r=f.WangYunPlan!;var actor=_players[r.Actor];var kind=Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);
        var cards=selected.Cards.Select(id=>_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id)).ToArray();
        if(cards.Length==0)
        {
            if(selected.Parameters.GetValueOrDefault("round-once-cardless")!="true"||kind!=CardKind.Slash||!TryReadConversionSource(selected.Parameters,out var source)||source is null||source.OwnerSeat!=actor.Seat||!WangYunPlanChoices(f).Any(c=>AssistedChoicesEqual([c],[selected])))throw new InvalidOperationException("Lianji empty materials require an exact published independent cardless source.");
            var payment=ConsumeRoundOnceCardlessBasic(source,false);
            ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="issuing-slash",IssuingIds=[],IssuingKind=CardKind.Slash,NativeUse=null,NativeReturned=false,RoundOncePayment=payment}});
            var logical=new Card(0,CardKind.Slash,Suit.None,0);var target=_players[r.SlashTarget!.Value];
            var id=BeginCardUse(logical,actor.Seat,[target.Seat],CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,target,CardKind.Slash),physicalCardIds:[],conversionSource:source);IssueRoundOnceCardlessNativeUse(id,source);
            var attack=new CardAttackHandle(this,id,actor.Seat,target.Seat,card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,target,CardKind.Slash),programSkillCardUseFrameId:f.Id);
            ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,CardKind.Slash);
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,CardKind.Slash,actor.Seat,target.Seat,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,target);
            if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,[target.Seat],ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
            return;
        }
        if(cards.Any(c=>_cardZones.GetLocation(c.Id).OwnerSeat!=actor.Seat))throw new InvalidOperationException("The actor lost a real owned Slash material.");
        CardConversionSource?conversion=null;
        if(selected.Parameters.GetValueOrDefault("equipment")!=CardKind.ZhangbaSerpentSpear.ToString())
        {
            TryReadConversionSource(selected.Parameters,out _selectedUseConversion);_hasSelectedUseConversionChoice=true;
            if(cards.Length>1 || IsLeiluanSource(_selectedUseConversion))
            {
                conversion=_selectedUseConversion;if(conversion is null || FindProgramMultiCardViewAsSelection(actor,selected.Cards,kind,false,conversion) is null)
                    throw new InvalidOperationException("The multi-card Slash lost its exact native view-as payment.");
            }
            else conversion=GetSelectedUseConversion(actor,actor,cards[0],kind);
        }
        else{_selectedUseConversion=null;_hasSelectedUseConversionChoice=true;}
        ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="issuing-slash",IssuingIds=selected.Cards,IssuingKind=kind,NativeUse=null,NativeReturned=false}});
        ResolveSlashCore(actor,_players[r.SlashTarget!.Value],cards[0],kind,actor.Seat,physicalCards:cards,countsTowardSlashLimit:false,
            usesZhuqueFan:kind==CardKind.FireSlash && cards[0].Kind==CardKind.Slash && HasZhuqueFan(actor),conversionSource:conversion,programSkillCardUseFrameId:f.Id);
    }
}
