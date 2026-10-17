namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsSayingFactionOrigin(ProgramSkillFrame f,int actor,int target,string resultBind)=>IsSayingSlashRequest(f)&&TryGetSayingSlashRequest(f,out var actual,out var targets)&&actual==actor&&targets.SequenceEqual([target])&&resultBind=="saying-slash"&&GetFactionResponsePolicy(_players[actor],CardKind.Slash)is not null;
    private void BeginSayingFactionRequest(ProgramSkillFrame f,int actor,int target)
    {
        if(ActiveFactionCardRequest is not null||!IsSayingSlashRequest(f)||!TryGetSayingSlashRequest(f,out var actual,out var targets)||actual!=actor||!targets.SequenceEqual([target])||!CanRequestAssistedProgramFactionSlash(actor,target)||GetFactionResponsePolicy(_players[actor],CardKind.Slash)is not{}policy)throw new InvalidOperationException("Saying lost its actual requesting holder, native faction policy or ordinary Slash target.");
        var candidates=GetFactionProviderSeats(actor,policy.FactionId);AdvanceEventRulesAndQueueFact(new SayingFactionRequestedEvent(f.Id,actor,target,policy));
        ActiveFactionCardRequest=new FactionCardRequestHandle(this,f.Id,FactionCardRequestPurpose.AssistedProgramUse,actor,candidates,policy.FactionId,targetSeat:target,programSkillFrameId:f.Id,policySource:policy,assistedResultBind:"saying-slash");ClearPendingDecision();_status=EngineStatus.Running;AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id,actor,candidates,policy.SkillId,IsActiveUse:true,target));AdvanceFactionSlashCandidate();
    }
    private bool ValidateSayingFactionSlashParent(FactionCardRequestHandle pending,ProgramSkillFrame f)=>TryGetSayingSlashRequest(f,out var actor,out var targets)&&pending.IsAssistedProgramUse&&pending.OwnerFrameId==f.Id&&pending.ProgramSkillFrameId==f.Id&&pending.OwnerSeat==actor&&targets is[var target]&&pending.TargetSeat==target&&pending.AssistedResultBind=="saying-slash"&&pending.PolicySource is{}policy&&pending.ProviderFactionId==policy.FactionId&&policy.RequiredKind==CardKind.Slash&&ProgramEventHistory<SayingFactionRequestedEvent>().Count(e=>e.FrameId==f.Id&&e.ActorSeat==actor&&e.TargetSeat==target&&e.Source==policy)==1;
    private void PrepareSayingSuppliedSlash(ProgramSkillFrame supplied,int actorSeat,int providerSeat,int targetSeat,CardKind kind,IReadOnlyList<Card> materials,CardConversionSource? conversionSource,IReadOnlyList<CardConversionSource>? additionalConversionSources=null)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(!IsSayingSlashRequest(f)||!TryGetSayingSlashRequest(f,out var actor,out var targets)||actor!=actorSeat||!targets.SequenceEqual([targetSeat])||!IsSlashCard(kind)||!_players[actorSeat].IsAlive||!_players[targetSeat].IsAlive||!IsValidPlayerSeat(providerSeat)||materials.Select(c=>c.Id).Distinct().Count()!=materials.Count||materials.Any(c=>c.Id<=0||!_cardZones.CardsAt(_cardZones.GetLocation(c.Id)).Any(actual=>actual.Id==c.Id&&actual.Kind==c.Kind)))throw new InvalidOperationException("Saying's supplied Slash lost its exact current holder, target or primary group.");
        var r=f.SunYuan!;var sources=(conversionSource is{}source?new[]{source}:Array.Empty<CardConversionSource>()).Concat(additionalConversionSources??[]).ToArray();
        if(materials.Count==0&&sources.Length==0)throw new InvalidOperationException("Saying cannot invent an unprovided zero-material Slash.");
        var selection=new SayingSelection(actorSeat,providerSeat,targetSeat,kind){Materials=materials.Select(c=>new SunYuanEntity(c.Id,c.Kind,c.Kind,_cardZones.GetLocation(c.Id),c.IsGeneralWeapon)).ToArray(),Sources=sources};
        if(ProgramEventHistory<SayingSlashPreparedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("One Saying request cannot issue two native Slashes.");
        ReplaceRuntimeTop(f with{SunYuan=r with{Stage=SunYuanStage.SlashIssuing,Selection=selection}});AdvanceEventRulesAndQueueFact(new SayingSlashPreparedEvent(f.Id,actorSeat,providerSeat,targetSeat,kind,SunYuanHash(selection)));
    }
    private bool FinishSayingSuppliedSlashFailure(ProgramSkillFrame supplied)
    {
        if(!IsSayingSlashRequest(supplied))return false;var f=GetActiveProgramFrame(supplied.Id);if(f.SunYuan!.Native is not null)throw new InvalidOperationException("A completed Saying Slash cannot become an unpaid cancellation.");ClearPendingDecision();FinishSunYuan(f,true);return true;
    }
    private void BindSayingSlashUse(CardUseFrame supplied)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==supplied.Id);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame{SunYuan:{Stage:SunYuanStage.SlashIssuing,Selection:{}selection}r}parent)return;
        var use=LifecycleCardUse(supplied.Id)!;
        if(use.SayingSlash is not null||r.Native is not null||use.Action is not{Type:CardActionType.Use}a||use.SourceSeat!=selection.ActorSeat||a.ActorSeat!=selection.ActorSeat||a.ProviderSeat!=selection.ProviderSeat||use.CardKind!=selection.Kind||a.EffectiveKind!=selection.Kind||!use.TargetSeats.SequenceEqual([selection.TargetSeat])||!a.TargetSeats.SequenceEqual(use.TargetSeats)||!a.PhysicalCards.Select(c=>c.CardId).SequenceEqual(selection.Materials.Select(c=>c.CardId))||!a.ConversionChain.SequenceEqual(selection.Sources)||ProgramEventHistory<SayingSlashPreparedEvent>().Count(e=>e.FrameId==parent.Id&&e.SelectionHash==SunYuanHash(selection))!=1)throw new InvalidOperationException("Saying's native child changed its once-prepared actor, provider, conversion or payment.");
        var origin=new SayingNativeOrigin(parent.Id,r.Source,r.GameplayHash,r.ActualTurn,use.Id,a.ActionId,selection,a,_movementSequence);
        ReplaceRuntimeFrame(parent.Id,parent with{SunYuan=r with{Stage=SunYuanStage.NativeUse,Native=origin,NativeReturned=false,Before=_movementSequence,After=_movementSequence}});UpdateLifecycleCardUse(use.Id,u=>u with{SayingSlash=origin});AdvanceEventRulesAndQueueFact(new SayingSlashIssuedEvent(parent.Id,use.Id,a.ActionId,SunYuanHash(a)));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27490)]
    private void RecordSayingSlashPayment(CardMovedEvent e)
    {
        if(!HasSunYuanCapability||_cardMovements.LastOrDefault()is not{}m||m.CardId!=e.CardId||m.From!=e.From||m.To!=e.To||m.Reason!=e.Reason||m.Reason!=CardMoveReasons.Use)return;
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.SayingSlash is not null).ToArray())
        {
            var r=use.SayingSlash!;var material=r.OriginalAction.PhysicalCards.SingleOrDefault(c=>c.CardId==m.CardId);if(material is null||material.From!=m.From||m.Sequence<=r.BeforePayment||ProgramEventHistory<SayingSlashMaterialPaidEvent>().Any(p=>p.UseId==use.Id&&p.CardId==m.CardId))continue;
            var to=r.Selection.Materials.Single(c=>c.CardId==m.CardId).GeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:IsProgramAlternativeCost(r.OriginalAction,m.CardId)?CardLocation.DrawPile:CardLocation.Processing;
            if(m.To==to)AdvanceEventRulesAndQueueFact(new SayingSlashMaterialPaidEvent(use.Id,r.ActionId,m.CardId,m.From,m.To,m.Sequence));
        }
    }
    private bool IsIssuedSayingUse(CardUseFrame use)
    {
        if(use.SayingSlash is not{}o||use.Action is not{Type:CardActionType.Use}a||use.Id!=o.UseId||a.ActionId!=o.ActionId||!IsSlashCard(use.CardKind)||!ShownEntityUseActorMatches(use,o.Selection.ActorSeat,o.Selection.ProviderSeat)||a.ProviderSeat!=o.Selection.ProviderSeat||!a.PhysicalCards.SequenceEqual(o.OriginalAction.PhysicalCards)||!use.PhysicalCardIds!.SequenceEqual(o.Selection.Materials.Select(c=>c.CardId))||ProgramEventHistory<SayingSlashIssuedEvent>().Count(e=>e.FrameId==o.ProgramFrameId&&e.UseId==use.Id&&e.ActionId==o.ActionId&&e.OriginalActionHash==SunYuanHash(o.OriginalAction))!=1)return false;
        var original=a;if(use.CurrentSlashFirePolicy is{}fire){AssertCurrentSlashFirePolicy(use);original=fire.OriginalAction;}else if(use.CardKind!=o.Selection.Kind)return false;
        if(original.EffectiveKind!=o.Selection.Kind||!original.ConversionChain.SequenceEqual(o.Selection.Sources)||original.ActorSeat!=o.Selection.ActorSeat&&ProgramEventHistory<ProgramCardUseActorReplacedEvent>().All(e=>e.CardUseFrameId!=use.Id)||_resolutionStack.SingleOrDefault(f=>f.Id==o.ProgramFrameId)is not ProgramSkillFrame{SunYuan:{}r}parent||r.Native!=o||r.Source!=o.Source||r.GameplayHash!=o.GameplayHash||r.NativeReturned||r.Stage!=SunYuanStage.NativeUse)return false;
        foreach(var cost in o.OriginalAction.PhysicalCards)
        {
            var paid=ProgramEventHistory<SayingSlashMaterialPaidEvent>().Where(e=>e.UseId==use.Id&&e.CardId==cost.CardId).ToArray();
            if(paid.Length==0){if(_cardZones.GetLocation(cost.CardId)!=cost.From)return false;continue;}
            if(paid is not[var p]||p.ActionId!=o.ActionId||p.From!=cost.From||p.Sequence<=o.BeforePayment||_cardMovements.Count(m=>m.Sequence==p.Sequence&&m.CardId==p.CardId&&m.From==p.From&&m.To==p.To&&m.Reason==CardMoveReasons.Use)!=1)return false;
        }
        return true;
    }
    private bool IsSayingRemovedMaterial(long useId,int cardId)=>LifecycleCardUse(useId)is{SayingSlash:{}r}use&&IsIssuedSayingUse(use)&&_cardZones.GetLocation(cardId)==CardLocation.OutsideGame&&r.Selection.Materials.Any(c=>c.CardId==cardId&&c.GeneralWeapon&&c.From.Zone==CardZoneKind.Equipment)&&ProgramEventHistory<SayingSlashMaterialPaidEvent>().Count(e=>e.UseId==useId&&e.CardId==cardId&&e.To==CardLocation.OutsideGame)==1;
    [NativeUseValidation(27490)]private void ValidateSayingNative(CardUseFrame use){if(use.SayingSlash is not null&&!IsIssuedSayingUse(use))throw new InvalidOperationException("Saying lost its issued native Slash, once-paid materials or typed parent.");}
    [NativeUseReturn(27490)]private void ReturnSayingNative(CardUseFrame use)
    {
        if(use.SayingSlash is not{}o)return;var f=GetActiveProgramFrame(o.ProgramFrameId);var r=f.SunYuan;
        if(r is not{Stage:SunYuanStage.NativeUse,NativeReturned:false}||r.Native!=o||use.Step!=ResolutionFrameStep.Completed||LifecycleCardUse(use.Id)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1)throw new InvalidOperationException("Saying cannot return before or twice after the whole native Use.");
        ReplaceRuntimeTop(f with{SunYuan=r with{NativeReturned=true}});if(use.CardAttack is not{Active:true})SettleSayingNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27490)]private bool ReturnSayingAttack(AttackCompletionReceipt completion)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SunYuan:{NativeReturned:true,Native:{}o}}||completion.ResolutionId!=o.UseId)return false;
        if(completion.ProgramFrameId is{}parent&&parent!=o.ProgramFrameId)throw new InvalidOperationException("Saying's native attack returned to another program.");SettleSayingNative(o);return true;
    }
    private void SettleSayingNative(SayingNativeOrigin o)
    {
        var f=GetActiveProgramFrame(o.ProgramFrameId);var r=f.SunYuan!;
        if(r.Native!=o||!r.NativeReturned||LifecycleCardUse(o.UseId)is not null||ProgramEventHistory<SayingSlashReturnedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Saying lost its unique complete native return.");
        ReplaceRuntimeTop(f with{SunYuan=r with{Stage=SunYuanStage.NativeChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new SayingSlashReturnedEvent(f.Id,o.UseId,o.ActionId));
    }
    private bool ResumeSayingSuppliedSlash(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.SunYuan is not{}r)return false;if(r.Native is{}o&&r.NativeReturned&&r.Stage==SunYuanStage.NativeUse)SettleSayingNative(o);ContinueSunYuan(f.Id);return true;
    }
    private bool IsSayingNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)=>f.SunYuan is{Stage:SunYuanStage.NativeChildren,NativeReturned:true,Native:{}o}r&&batch.ParentFrameId==o.UseId&&batch.AwaitingProgramFrameId is null&&batch.Movements.Count>0&&ProgramEventHistory<SayingSlashReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==o.UseId)==1&&batch.Movements.All(m=>m.Sequence>r.After&&_cardMovements.Contains(m)&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished&&o.Selection.Materials.Any(c=>c.CardId==m.CardId&&c.Kind==m.CardKind));
    private bool HasSayingWeijiePayment(ProgramSkillFrame f,CardUseFrame? use=null)
    {
        if(f.SunYuan is not{}s||f.WeijieReturn is not{}r||!ValidWeijiePayment(r.Origin)||!TryGetSayingSlashRequest(f,out var actor,out var targets)||r.Origin.Need.ActorSeat!=actor||!targets.SequenceEqual([r.Origin.TargetSeat])||r.Origin.Need.OwnerFrameId!=f.Id||r.Origin.Need.RequestFrameId!=f.Id||r.Origin.Need.InstructionIndex!=f.InstructionIndex||s.Stage is not(SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)||s.Selection is not{Materials.Count:0,Sources:[var source]}selection||source!=r.Origin.Source||selection.ActorSeat!=actor||selection.ProviderSeat!=r.Origin.Need.ProviderSeat||selection.Kind!=r.Origin.OutputKind||ProgramEventHistory<WeijieAttemptResolvedEvent>().Count(e=>e.FrameId==r.Origin.AttemptId&&e.Matched&&e.NativeLegal)!=1)return false;
        if(use is not null&&(use.WeijieOrigin!=r.Origin||r.UseId!=use.Id||r.ActionId!=use.Action?.ActionId||!IsIssuedWeijieUse(use)))return false;
        return s.Stage!=SunYuanStage.NativeChildren||r.Returned&&s.NativeReturned&&s.Native is{}native&&native.UseId==r.UseId&&native.ActionId==r.ActionId&&LifecycleCardUse(r.UseId)is null&&ProgramEventHistory<SayingSlashReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1;
    }
}
