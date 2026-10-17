using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string WangYunPlanMoveReason="program.wang-yun.plan-payment";
    private bool HasWangYunPlanCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash);
    private bool HasWangYunTrickCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial);
    private static bool WangYunMaterial(Card card)=>IsSlashCard(card.Kind) || EquipmentCatalog.IsEquipment(card.Kind);
    private bool WangYunActivationCardAllowed(SkillProgramActivation a,Card card)=>WangYunActivationCardAllowed(_currentSeat,a,card);
    private bool WangYunActivationCardAllowed(int ownerSeat,SkillProgramActivation a,Card card)
    {
        var features=ProgramInstructionResolver.Default.Features(a);
        if(features.HasOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash))return _cardZones.GetLocation(card.Id)==CardLocation.Hand(ownerSeat) &&
            !IsSelfHandCategoryDiscardForbidden(ownerSeat,card,CardLocation.Hand(ownerSeat),OwnedCardMoveIntent.Discard);
        return !features.HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) || WangYunMaterial(card);
    }
    private CardConversionSource WangYunSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private SkillProgramEffect? WangYunPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool WangYunActivationMatches(ProgramSkillFrame f,SkillProgramEffectOp op)=>f.TriggerId is null && f.WindowContext is null && f.InstructionIndex==1 &&
        f.OwnerSeat==_currentSeat && _phase==TurnPhase.Play && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash &&
        ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is[var instruction] && instruction.Op==op;
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
    private static Dictionary<string,string> WangYunArgs(ProgramSkillFrame f,string branch)=>new(){["program-action"]="wang-yun-plan",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch};
    private int WangYunChooser(ProgramSkillFrame f)=>f.WangYunPlan?.Stage=="branch"?f.WangYunPlan.Actor:f.OwnerSeat;
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
    private void PublishWangYun(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive || f.WangYunPlan is{}r && !_players[r.Actor].IsAlive || _winner!=Winner.None){FinishWangYun(f);return;}
        var choices=f.WangYunPlan is not null?WangYunPlanChoices(f):WangYunTrickChoices(f);if(choices.Count==0){FinishWangYun(f);return;}
        var chooser=WangYunChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,skill.Name+"：选择本次真实结算的后续效果。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("wang-yun-plan")]
    private void ResolveWangYunChoice(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.WangYunPlan is null && f.WangYunTrick is null || _pendingDecision?.PlayerSeat!=WangYunChooser(f) ||
            !AssistedChoicesEqual([selected],(f.WangYunPlan is not null?WangYunPlanChoices(f):WangYunTrickChoices(f)).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Wang Yun lost the exact actual chooser or current material/target choice.");
        AssertWangYun(f);ClearPendingDecision();
        if(f.WangYunTrick is not null){IssueWangYunTrick(f,selected);return;}
        var r=f.WangYunPlan!;switch(selected.Parameters["branch"])
        {
            case "target":ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="branch",SlashTarget=selected.Targets.Single()}});PublishWangYun(f);return;
            case "transfer":ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="transfer"}});PublishWangYun(f);return;
            case "give-weapon":
                var card=selected.Cards.Single();var recipient=selected.Targets.Single();ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Recipient=recipient}});
                PayWangYunPlanMovement(f,card,CardLocation.Equipment(r.Actor),CardLocation.Hand(recipient),"complete");return;
            case "slash":IssueWangYunSlash(f,selected);return;
            default:throw new InvalidOperationException("Unknown paid plan choice.");
        }
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
    private CardKind[] WangYunRandomTrickPool()=>_contentRegistry.Cards.Values.Where(c=>c.LegacyKind is{}kind && CardCatalog.Get(kind).CategoryName=="锦囊牌" && kind!=CardKind.Nullification &&
        (kind==CardKind.Lightning || ActivityRuntimeProgram(_contentRegistry.GetSkill("ol:jingong").Program!).ViewAs.Any(r=>r.OutputKind==kind))).Select(c=>c.LegacyKind!.Value).Distinct().Order().ToArray();
    private bool IsWangYunRandomConversionAvailable(CharacterState owner,Card card,CardConversionSource source,bool forResponse)
    {
        if(!HasWangYunTrickCapability || !_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) ||
            !_contentRegistry.Skills.TryGetValue(source.SkillId,out var definition) || definition.Program is not{}p ||
            !p.Activations.Any(a=>ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial)))return true;
        return !forResponse && source.OwnerSeat==owner.Seat && WangYunMaterial(card) &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame{WangYunTrick:{Stage:"choice" or "issuing-trick"}r}f && f.OwnerSeat==owner.Seat && f.SkillId==source.SkillId &&
            f.SkillInstanceId==source.SkillInstanceId && r.CardId==card.Id && r.From==_cardZones.GetLocation(card.Id) &&
            ActivityRuntimeProgram(p).ViewAs.SingleOrDefault(v=>v.Id==source.BindingId) is{}rule && r.Samples.Contains(rule.OutputKind);
    }
    private static string WangYunSamplesFingerprint(IReadOnlyList<CardKind> samples)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',samples.Select(k=>(int)k)))));
    private SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.WangYunTrick is not null || !WangYunActivationMatches(f,SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) || f.SelectedCardIds is not[var id] || f.SelectedTargetSeats.Count!=0)
            throw new InvalidOperationException("Random tricks lost the original actual material activation.");
        var from=_cardZones.GetLocation(id);var card=_cardZones.CardsAt(from).Single(c=>c.Id==id);var pool=WangYunRandomTrickPool().ToList();
        if(from.OwnerSeat!=f.OwnerSeat || from.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment) || !WangYunMaterial(card) || pool.Count<3)
            throw new InvalidOperationException("Random tricks require one real Slash/equipment entity and at least three real proactive native trick templates.");
        var samples=new List<CardKind>();for(var i=0;i<3;i++){var at=_random.Next(pool.Count);samples.Add(pool[at]);pool.RemoveAt(at);}
        var r=new WangYunTrickReceipt{Source=WangYunSource(f),GameplayHash=f.GameplayHash,Turn=_turnNumber,PhaseInstance=_cardUseDebitPhaseInstanceId,InstructionIndex=1,
            CardId=id,PrintedKind=card.Kind,From=from,Samples=samples,Stage="choice"};ReplaceRuntimeTop(f=f with{WangYunTrick=r});
        AdvanceEventRulesAndQueueFact(new WangYunRandomTricksStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.PhaseInstance,id,card.Kind,from,WangYunSamplesFingerprint(samples)));
        PublishWangYun(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<LegalAction> WangYunTrickActions(ProgramSkillFrame f)=>BuildLegalActions(_players[f.OwnerSeat],includeProgramActions:false,selectedPhysicalCardId:f.WangYunTrick!.CardId)
        .Where(a=>a.ConversionSource is{}s && s.SkillId==f.SkillId && s.SkillInstanceId==f.SkillInstanceId && a.PlayedCardKind is{}k && f.WangYunTrick.Samples.Contains(k)).ToArray();
    private bool CanWangYunLightning(ProgramSkillFrame f)
    {
        var r=f.WangYunTrick!;var owner=_players[f.OwnerSeat];var card=_cardZones.CardsAt(r.From).SingleOrDefault(c=>c.Id==r.CardId);
        return r.Samples.Contains(CardKind.Lightning) && owner.IsAlive && card is not null && WangYunMaterial(card) && !owner.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(owner,CardKind.Lightning) &&
            !IsCardUseForbidden(owner.Seat,CardKind.Lightning,CardActionType.Use) && !IsSelfTargetForbiddenAction(owner,CardKind.Lightning,[owner.Seat]) &&
            !IsTurnPhysicalUseForbidden(owner.Seat,[card.Id]) && !IsTurnHandCardRestricted(owner,card) && !IsResponseEntityRestricted(owner.Seat,card.Id) &&
            !IsCardTargetProhibited(owner,CardKind.Lightning,EffectiveSuit(owner,card),SuitColor(EffectiveSuit(owner,card)));
    }
    private IReadOnlyList<PromptChoice> WangYunTrickChoices(ProgramSkillFrame f)
    {
        var result=new List<PromptChoice>();var index=0;
        foreach(var a in WangYunTrickActions(f))
        {
            var args=WangYunArgs(f,"trick");args["choice-index"]=(index++).ToString(CultureInfo.InvariantCulture);args["effective-kind"]=a.PlayedCardKind!.Value.ToString();
            result.Add(new(new($"wang-yun.{f.Id}.trick-{index}"),a.Description,[f.WangYunTrick!.CardId],a.TargetSeats,args));
        }
        if(CanWangYunLightning(f)){var args=WangYunArgs(f,"lightning");args["effective-kind"]=CardKind.Lightning.ToString();result.Add(new(new($"wang-yun.{f.Id}.lightning"),"将本次材料当【闪电】对自己使用",[f.WangYunTrick!.CardId],[f.OwnerSeat],args));}
        return Array.AsReadOnly(result.ToArray());
    }
    private void IssueWangYunTrick(ProgramSkillFrame f,PromptChoice selected)
    {
        var r=f.WangYunTrick!;var kind=Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);var owner=_players[f.OwnerSeat];var card=_cardZones.CardsAt(r.From).Single(c=>c.Id==r.CardId);
        LegalAction? action=null;if(kind!=CardKind.Lightning)action=WangYunTrickActions(f)[int.Parse(selected.Parameters["choice-index"],CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f=f with{WangYunTrick=r with{Stage="issuing-trick",SelectedKind=kind}});
        if(action is not null){ExecuteAction(owner,action);return;}
        if(!CanWangYunLightning(f))throw new InvalidOperationException("The sampled Lightning lost its actual native self-placement legality.");
        var id=BeginCardUse(card,owner.Seat,[owner.Seat],CardKind.Lightning,conversionSource:r.Source);MoveCard(card,r.From,CardLocation.Processing,CardMoveReasons.Use);CompleteWangYunNativePayment(id);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id,f.SkillId,f.ActivationId,f.OwnerSeat,[card.Id],CardKind.Lightning,true,[owner.Seat]));
        if(!HasSoleTargetProtectionCapability && TryBeginDelayedCardUsePrograms(id))return;
        BeginJizhiOrNullificationWindow(id,card,owner.Seat,[owner.Seat],LegalActionKind.Lightning,playedCardKind:CardKind.Lightning);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 360)]
    private bool CanAwakenWangYun(int owner,SkillProgramTrigger trigger)=>!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage) ||
        CompleteProgramEventHistory().OfType<WangYunLianjiDamageEvent>().Any(e=>e.PlanOwner==owner && e.Amount>0);
    private SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;
        if(f.WangYunAwakening is not null || f.InstructionIndex!=1 || f.TriggerId is null || c is not{Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow} || f.OwnerSeat!=_currentSeat ||
            !CanAwakenWangYun(f.OwnerSeat,GetProgramTrigger(f)) || _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==c.ParentFrameId) is not{}parent ||
            parent.OwnerSeat!=f.OwnerSeat || parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count || !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("Moucheng lost its actual issued plan Slash damage and own preparation.");
        ReplaceRuntimeTop(f=f with{WangYunAwakening=new(WangYunSource(f),f.GameplayHash,c.ParentFrameId,_turnNumber,1)});
        GrantProgramSkills(f,["ol:jingong"]);foreach(var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId=="ol:lianji").ToArray())_players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
        AdvanceEventRulesAndQueueFact(new WangYunAwakenedEvent(f.Id,f.OwnerSeat,"ol:lianji","ol:jingong"));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,22900)]
    private bool ResumeWangYun(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.WangYunPlan is null && f.WangYunTrick is null && f.WangYunAwakening is null)return false;
        AssertWangYun(f);if(GainGiftHasFinalGameEnd())return true;
        if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnWangYunMovement(f);return true;}
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id))return true;
        f=GetActiveProgramFrame(id);
        if(f.WangYunAwakening is not null || f.WangYunTrick is{NativeReturned:true}){FinishWangYun(f);return true;}
        if(f.WangYunPlan is{}r && r.NativeReturned)
        {
            if(r.NativeUse!.Kind=="slash"){FinishWangYun(f);return true;}
            ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="target",NativeUse=null,NativeReturned=false,IssuingIds=Array.Empty<int>(),IssuingKind=null}});
        }
        if(!_players[f.OwnerSeat].IsAlive || f.WangYunPlan is{}p && !_players[p.Actor].IsAlive){ClearPendingDecision();FinishWangYun(f);return true;}
        if(f.WangYunPlan?.Stage=="weapon"){IssueWangYunWeapon(f);return true;}
        if(_pendingDecision is null)PublishWangYun(f);return true;
    }
    private bool IsWangYunMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash &&
        f.WangYunPlan is{Stage:"movement"} && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,22900)]
    private bool ReturnWangYunMovement(ProgramSkillFrame f)
    {
        if(f.WangYunPlan is not{}r)return false;if(f.PendingMovementContinuation is not{}p || !IsWangYunMovement(f,WangYunPausedEffect(f),p))throw new InvalidOperationException("Lianji consumed a foreign movement return.");
        AssertWangYun(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;
        ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,WangYunPlan=r with{Stage=r.ReturnStage}});
        if(r.ReturnStage=="complete" || !_players[f.OwnerSeat].IsAlive || !_players[r.Actor].IsAlive)FinishWangYun(f);else IssueWangYunWeapon(f);return true;
    }
    private void FinishWangYun(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{WangYunPlan=null,WangYunTrick=null,WangYunAwakening=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramCapability]
    private sealed class WangYunProgramCapability(GameEngine engine) : IWangYunProgramHost
    {
        public SkillProgramStepOutcome ResolveLianjiPaidWeaponAndSlash(ProgramSkillFrame f)=>engine.ResolveLianjiPaidWeaponAndSlash(f);
        public SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame f)=>engine.AwakenFromIssuedLianjiSlashDamage(f);
        public SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame f)=>engine.UseThreeRandomTricksFromSelectedMaterial(f);
    }
}
