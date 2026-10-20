namespace CardGame.Core;
public sealed partial class GameEngine
{
    private CardConversionSource MouGszHuAccess(PublicPersistentPileSource p)=>new(p.SkillId,"hu-original",p.OwnerSeat,p.SkillInstanceId);
    private IReadOnlyList<Card> AppendMouGszHuPlayable(CharacterState owner,IReadOnlyList<Card> cards)=>
        AvailableDeclarationCard(owner) is not null?cards:cards.Concat(MouGszHuCards(owner)).DistinctBy(c=>c.Id).ToArray();
    private IReadOnlyList<NativePublicPileCardSelection> MouGszHuOriginalChoices(NativePublicPileCardRequest request)=>
        HasMouGszMobile&&IsValidPlayerSeat(request.ProviderSeat)&&IsValidPlayerSeat(request.ActorSeat)&&_players[request.ActorSeat].IsAlive&&Enum.IsDefined(request.Direction)
        ?MouGszHuPiles(request.ProviderSeat).Where(IsLiveMouGszHuPile).SelectMany(p=>PublicPileCards(p).Where(c=>request.RequiredKind is null||MatchesRequiredCard(c.Kind,request.RequiredKind.Value)).Select(c=>new NativePublicPileCardSelection(p,MouGszHuAccess(p),c,request))).ToArray():[];
    private bool IsMouGszHuOriginalChoice(NativePublicPileCardRequest request,NativePublicPileCardSelection choice)=>choice.Request==request&&choice.AccessSource.SkillId==MouGongsunZanMobileComposition.Yicong&&choice.AccessSource.BindingId=="hu-original"&&MouGszHuOriginalChoices(request).Contains(choice);
    private bool IsMouGszHuOriginalPayment(NativePublicPileCardPayment p)=>p.Source.SkillId==MouGongsunZanMobileComposition.Yicong&&p.Source.BindingId=="hu-original"&&IsIssuedMouGszHuPile(p.Pile)&&p.Source==MouGszHuAccess(p.Pile)&&
        _cardMovements.Any(m=>m.Sequence==p.SequenceAfter&&m.CardId==p.Card.Id&&m.CardKind==p.Card.Kind&&m.From==p.Pile.Location&&m.To==CardLocation.Processing);
    private bool MouGszHuHandSource(CharacterState owner,Card card,IReadOnlyList<CardZoneKind> zones)=>zones.Contains(CardZoneKind.Hand)&&IsMouGszHuCard(owner,card.Id);
    private bool ValidateMouGszHuConversion(CharacterState provider,IReadOnlyList<Card> materials,CardConversionSource? conversion,bool forResponse,CardKind? effectiveKind)
    {
        if(!materials.Any(c=>IsMouGszHuCard(provider,c.Id)))return false;
        if(materials.Any(c=>_cardZones.GetLocation(c.Id).Zone==CardZoneKind.PublicPersistentPile&&!IsMouGszHuCard(provider,c.Id)))throw new InvalidOperationException("Hu cannot authorize another public pile.");
        if(effectiveKind is not{}kind)throw new InvalidOperationException("Hu needs the actual native output kind at payment.");
        if(conversion is null)
        {
            if(materials is[var original]&&(MatchesRequiredCard(original.Kind,kind)||original.Kind==CardKind.Slash&&kind==CardKind.FireSlash&&HasZhuqueFan(provider)))return true;
            if(!forResponse&&IsSlashCard(kind)&&materials.Count==2&&CanUseZhangbaSerpentSpear(provider)&&materials.All(c=>IsOwnedPlayableLocation(provider,_cardZones.GetLocation(c.Id))||IsMouGszHuCard(provider,c.Id)))return true;
            throw new InvalidOperationException("Only an original Hu or the native Zhangba equipment path may omit its conversion source.");
        }
        if(conversion.OwnerSeat!=provider.Seat||!HasRuntimeSkillInstance(provider,conversion.SkillId,conversion.SkillInstanceId))throw new InvalidOperationException("Hu keeps the native conversion's actual live provider.");
        var response=forResponse||kind is CardKind.Peach or CardKind.Alcohol&&ActiveDying?.ResponderSeat==provider.Seat;
        var dying=response&&kind is CardKind.Peach or CardKind.Alcohol&&ActiveDying?.ResponderSeat==provider.Seat;
        if(materials is[var single]&&(GetProgramViewAsConversions(provider,single,kind,response,dying).Contains(conversion)||GetProgramCardIdentitySources(provider,single,kind,response).Contains(conversion)))return true;
        if(FindProgramMultiCardViewAsSelection(provider,materials.Select(c=>c.Id).ToArray(),kind,response,conversion) is not null)return true;
        throw new InvalidOperationException("The exact Hu single, multiple or variable conversion is no longer a native legal candidate.");
    }
    [ProgramPipelineHook(ProgramPipelinePhase.FilterPlayPhaseActions,33600)]
    private IReadOnlyList<LegalAction> FilterMouGszHuActions(CharacterState owner,IReadOnlyList<LegalAction> actions)
    {
        if(!HasMouGszMobile)return actions;
        var hu=actions.Where(a=>a.CardId is{}id&&IsMouGszHuCard(owner,id)).ToArray();
        var ordinary=actions.Where(a=>a.CardId is not{}id||!IsMouGszHuCard(owner,id));
        return ordinary.Concat(hu.Where(a=>a.Kind!=LegalActionKind.Recast)
            .Select(a=>a.CardId is{}id&&IsMouGszHuCard(owner,id)&&a.ConversionSource is null&&a.AdditionalConversionSources is not{Count:>0}&&a.Kind is not(LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect)
                ?a with{NativePublicPileSource=MouGszHuAccess(MouGszHuPiles(owner.Seat).Single(p=>p.Location==_cardZones.GetLocation(id)))}:a)
            .DistinctBy(a=>GuidlessNonHuActionKey(a))
            ).ToArray();
    }
    private static string GuidlessNonHuActionKey(LegalAction a)=>$"ordinary:{a.Kind}/{a.CardId}/{a.PlayedCardKind}/{a.TargetCardId}/{string.Join(",",a.TargetSeats)}/{a.ProgramSkillId}/{a.ProgramActivationId}/{a.ConversionSource}/{string.Join(",",a.AdditionalConversionSources??[])}";
    private void PrepareMouGszHuUse(CardUseFrame use)
    {
        if(use.Action is not{}a||!a.PhysicalCards.Any(c=>IsMouGszHuCard(_players[a.ProviderSeat],c.CardId)))return;
        // The original single card uses the existing original-pile invoice;
        // converted/multiple materials have this independent owning receipt.
        if(a.ConversionChain.Count==0&&a.PhysicalCards.Count==1)return;
        PrepareMouGszHuPayment(use,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,a.PhysicalCards.Select(c=>GetAdvancedCard(c.CardId)).ToArray(),a.ConversionChain,a.ActionId);
    }
    private bool PrepareMouGszHuResponse(CharacterState provider,IReadOnlyList<Card> cards,CardKind kind,CardConversionSource? conversion,int? actor=null)
    {
        if(conversion is null&&cards.Count==1||!cards.Any(c=>IsMouGszHuCard(provider,c.Id)))return false;
        var request=NativePublicPileResponseRequest(provider,kind);
        if(request.ParentFrameId is not{}parentId||_resolutionStack.SingleOrDefault(f=>f.Id==parentId) is not{}parent)throw new InvalidOperationException("Converted Hu needs its true native response owner.");
        var actualActor=actor??request.ActorSeat;
        PrepareMouGszHuPayment(parent,actualActor,provider.Seat,actualActor==provider.Seat?null:actualActor,cards,conversion is null?[]:[conversion],null);
        return true;
    }
    private void PrepareMouGszHuPayment(ResolutionFrame parent,int actor,int provider,int? requester,IReadOnlyList<Card> cards,IReadOnlyList<CardConversionSource> conversions,long? actionId)
    {
        var materials=cards.Where(c=>IsMouGszHuCard(_players[provider],c.Id)).Select(c=>
        {
            var pile=MouGszHuPiles(provider).Single(p=>p.Location==_cardZones.GetLocation(c.Id)&&IsLiveMouGszHuPile(p));
            return new MouGongsunZanHuMaterial(c.Id,c.Kind,pile.Location,MouGszHuAccess(pile),_contentRegistry.GetSkill(pile.SkillId).Program!.GameplayHash);
        }).ToArray();
        if(materials.Length==0)return;
        if(parent.MouGongsunZanHuPayment is{}old&&old.Materials.Any(m=>m.Sequence==0))throw new InvalidOperationException("Hu cannot overwrite an unpaid native invoice.");
        var r=new MouGongsunZanHuPayment(parent.Id,actor,provider,requester,_turnNumber,_movementSequence){Materials=materials,Conversions=conversions,ActionId=actionId};
        ReplaceRuntimeFrame(parent.Id,parent with{MouGongsunZanHuPayment=r});
        for(var i=0;i<materials.Length;i++){var m=materials[i];AdvanceEventRulesAndQueueFact(new MouGongsunZanHuMaterialIssuedEvent(parent.Id,i,m.CardId,m.Kind,m.From,m.Source,m.GameplayHash,actor,provider,requester,r.Turn,r.Before));}
    }
    private void CaptureMouGszHuPaymentBatch(CardMovementBatchContext batch)
    {
        if(batch.ParentFrameId is not{}id||_resolutionStack.SingleOrDefault(f=>f.Id==id) is not{MouGongsunZanHuPayment:{}r}parent)return;
        var materials=r.Materials.ToArray();var changed=false;
        for(var i=0;i<materials.Length;i++)
        {
            var p=materials[i];if(p.Sequence>0)continue;
            var entries=batch.Movements.Where(m=>m.CardId==p.CardId&&m.CardKind==p.Kind&&m.From==p.From&&m.To==CardLocation.Processing&&m.Sequence>r.Before&&
                (m.Reason==CardMoveReasons.Use||m.Reason==CardMoveReasons.Respond||m.Reason==CardMoveReasons.Nullification||m.Reason==CardMoveReasons.IronChainUse||m.Reason==CardMoveReasons.EquipmentUse)).ToArray();
            if(entries.Length==0)continue;if(entries is not[var entry])throw new InvalidOperationException("A Hu material has more than one native payment.");
            materials[i]=p with{Sequence=entry.Sequence};changed=true;
            AdvanceEventRulesAndQueueFact(new MouGongsunZanHuMaterialPaidEvent(id,i,p.CardId,entry.Sequence));
        }
        if(changed)ReplaceRuntimeFrame(id,parent with{MouGongsunZanHuPayment=r with{Materials=materials}});
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,33601)]
    private void BindMouGszHuAction(IGameEvent payload)
    {
        if(payload is not CardActionAcceptedEvent{Action:{}a})return;
        foreach(var parent in _resolutionStack.ToArray())
        {
            if(parent.MouGongsunZanHuPayment is not{}r||r.ActorSeat!=a.ActorSeat||r.ProviderSeat!=a.ProviderSeat||r.RequesterSeat!=a.RequesterSeat||!r.Conversions.SequenceEqual(a.ConversionChain)||r.Materials.Any(m=>m.Sequence<=0||!a.PhysicalCards.Any(c=>c.CardId==m.CardId&&c.CardKind==m.Kind&&c.From==m.From))||r.ActionId is{}old&&old!=a.ActionId)continue;
            ReplaceRuntimeFrame(parent.Id,parent with{MouGongsunZanHuPayment=r with{ActionId=a.ActionId}});
            foreach(var m in r.Materials)if(!ProgramEventHistory<MouGongsunZanHuActionBoundEvent>().Any(e=>e.ParentFrameId==parent.Id&&e.CardId==m.CardId&&e.Sequence==m.Sequence&&e.ActionId==a.ActionId))
                AdvanceEventRulesAndQueueFact(new MouGongsunZanHuActionBoundEvent(parent.Id,m.CardId,m.Sequence,a.ActionId));
        }
    }
    private bool IsMouGszHuActionCost(CardActionContext action,CardActionCost cost)
    {
        if(cost.From.Zone!=CardZoneKind.PublicPersistentPile||cost.From.OwnerSeat!=action.ProviderSeat)return false;
        return _resolutionStack.Any(f=>f.MouGongsunZanHuPayment is{}r&&r.ActionId==action.ActionId&&r.ActorSeat==action.ActorSeat&&r.ProviderSeat==action.ProviderSeat&&r.RequesterSeat==action.RequesterSeat&&r.Materials.Any(m=>m.CardId==cost.CardId&&m.Kind==cost.CardKind&&m.From==cost.From))||
            ProgramEventHistory<MouGongsunZanHuActionBoundEvent>().Any(b=>b.ActionId==action.ActionId&&b.CardId==cost.CardId&&ProgramEventHistory<MouGongsunZanHuMaterialIssuedEvent>().Any(e=>e.ParentFrameId==b.ParentFrameId&&e.CardId==cost.CardId&&e.Kind==cost.CardKind&&e.From==cost.From&&e.ActorSeat==action.ActorSeat&&e.ProviderSeat==action.ProviderSeat&&e.RequesterSeat==action.RequesterSeat)&&ProgramEventHistory<MouGongsunZanHuMaterialPaidEvent>().Any(p=>p.ParentFrameId==b.ParentFrameId&&p.CardId==b.CardId&&p.Sequence==b.Sequence));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish,33601)]
    private void AssertMouGszHuPayments()
    {
        foreach(var f in _resolutionStack)
        {
            if(f.MouGongsunZanHuPayment is not{}r)continue;
            if(r.ParentFrameId!=f.Id||r.Before<0||r.Before>_movementSequence||r.Materials.Count==0||r.Materials.Select(m=>m.CardId).Distinct().Count()!=r.Materials.Count)throw new InvalidOperationException("Hu lost its actual native owning frame.");
            for(var i=0;i<r.Materials.Count;i++)
            {
                var m=r.Materials[i];
                if(m.Source.OwnerSeat!=r.ProviderSeat||m.From.OwnerSeat!=r.ProviderSeat||m.Source.SkillId!=MouGongsunZanMobileComposition.Yicong||
                    ProgramEventHistory<MouGongsunZanHuMaterialIssuedEvent>().Count(e=>e==new MouGongsunZanHuMaterialIssuedEvent(f.Id,i,m.CardId,m.Kind,m.From,m.Source,m.GameplayHash,r.ActorSeat,r.ProviderSeat,r.RequesterSeat,r.Turn,r.Before))!=1||
                    m.Sequence>0&&(_cardMovements.Count(move=>move.Sequence==m.Sequence&&move.CardId==m.CardId&&move.CardKind==m.Kind&&move.From==m.From&&move.To==CardLocation.Processing)!=1||!ProgramEventHistory<MouGongsunZanHuMaterialPaidEvent>().Contains(new(f.Id,i,m.CardId,m.Sequence))))throw new InvalidOperationException("Hu lost its exact source/material/native payment.");
            }
        }
    }
}
