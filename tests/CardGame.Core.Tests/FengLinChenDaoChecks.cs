using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinChenDaoChecks
{
    public static void FirstUseAndIssuedPhaseBan()
    {
        var(g,r)=Create("slash");Grow(g);var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(2));var later=g.GetHumanLegalActions().First(x=>x.Kind==LegalActionKind.Snatch&&x.CardId!=a.CardId&&x.TargetSeats.Contains(1)&&x.ConversionSource is not null);
        Play(g,a);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Replay(g,r);Reject(g);
        Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Wanglie optional decision is private to the actual user.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1&&g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is [var ban]&&ban.ActorSeat==0,"One actual first Slash issues one phase-scoped use prohibition.");var published=g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions!;Require(published is System.Collections.Generic.IList<IssuedPlayPhaseUseProhibition> frozen&&frozen.IsReadOnly,"Prepared public issued phase facts cannot be rewritten by an observer.");
        Require(!g.GetHumanLegalActions().Any(a=>a.CardId is not null&&a.Kind!=LegalActionKind.Recast),"Remaining hand entities cannot be used in the issued phase.");Replay(g,r);
        var before=State(g);var res=g.Submit(new PlayCardCommand(0,later.CardId!.Value,later.TargetSeats,g.Revision,Prompt(g)!.PromptId,later.PlayedCardKind,later.TargetCardId){ConversionSource=later.ConversionSource});Require(!res.Accepted&&before==State(g),"A remaining owned entity and previously legal ordinary-trick conversion is rejected atomically by the issued phase ban.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);Require(g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is null,"The issued prohibition ends with the actual phase.");Replay(g,r);
    }
    public static void TrueResponseFamilies()
    {
        foreach(var mode in new[]{"duel","barbarian_assault","arrow_barrage","draw_two"})
        {
            var(g,r)=Create(mode);Grow(g);var kind=mode=="duel"?LegalActionKind.Duel:mode=="barbarian_assault"?LegalActionKind.BarbarianAssault:mode=="arrow_barrage"?LegalActionKind.ArrowBarrage:LegalActionKind.DrawTwo;
            var a=g.GetHumanLegalActions().First(a=>a.Kind==kind);Play(g,a);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
            Require(!g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Response),"An issued exact card suppresses genuine request-response actions across trick families.");Replay(g,r);
        }
    }
    public static void EquipmentDistanceAndBorrowedSword()
    {
        var(g,r)=Create("equipment");Grow(g);var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Play(g,equip);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1 && !g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(2)),"A real first equipment use consumes the distance benefit before later Slash payment.");Replay(g,r);
        var(b,br)=Create("borrowed_sword");Grow(b);Accept(b,new UseProgramSkillCommand(0,"fixture:driver","equip-target",[],[1],b.Revision,Prompt(b)!.PromptId));Reach(b,p=>p.SkillPrompt?.SkillId=="fixture:driver");Answer(b,c=>c.Cards.Count==1);Reach(b,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        var sword=b.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats[0]==1);Play(b,sword);Reach(b,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Replay(b,br);Answer(b,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(b,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(b.Events.Select(e=>e.Payload).OfType<BorrowedSwordResolvedEvent>().Any(e=>!e.UsedSlash&&e.TransferredWeaponCardId is not null) && !b.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActorSeat==1&&e.Action.EffectiveKind==CardKind.Slash),"Unrespondable Borrowed Sword skips the real forced Slash request and transfers the actual weapon.");Replay(b,br);
    }
    public static void NativeCommittedResponseUses()
    {
        var(g,r)=Create("slash-program-dodge");Grow(g);var donor=g.CreateCardZoneDiagnostics().First(c=>c.Location.Zone==CardZoneKind.Hand&&c.Location.OwnerSeat!=0&&c.CardKind==CardKind.Slash).Location.OwnerSeat!.Value;
        Accept(g,new UseProgramSkillCommand(0,"fixture:driver","request",[],[donor],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);Require(Prompt(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="program-dodge"),"A real configured virtual Dodge use is available before the issued phase ban.");Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().CompletedResponseReturn is { IsCommitted: true },"The genuine own Slash-defense Dodge pauses under its exact committed use parent.");Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Count(e=>e.Action.ActorSeat==0&&e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Dodge)==1,"The paid Dodge commits its accepted fact exactly once before the opt-in committed window and restores without resending.");Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1&&g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is [var ban],"Native AI's requested Slash is an out-of-phase use; the current actor's true Dodge is one own Play use.");Replay(g,r);
        var hp=g.CreateSnapshot(0).Players[0].Hp;
        Accept(g,new UseProgramSkillCommand(0,"fixture:driver","gift-slash",[],[donor],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:driver");Answer(g,c=>c.Cards.Count==1);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Accept(g,new UseProgramSkillCommand(0,"fixture:driver","request",[],[donor],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.CreateSnapshot(0).Players[0].Hp==hp-1&&g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Count(e=>e.Action.ActorSeat==0&&e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Dodge)==1,"The issued phase ban blocks a subsequent actual Dodge use while ordinary skill gifting/request decisions remain legal.");Replay(g,r);

        {
            var(m,mr)=Create("draw_two");Grow(m);var d=m.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.DrawTwo);Play(m,d);Reach(m,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Answer(m,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Reach(m,p=>p.Kind==DecisionKind.Nullification&&p.PlayerSeat==0);var choice=Prompt(m)!.Choices.First(c=>c.Parameters.GetValueOrDefault("response")=="extended-view-as");var ids=choice.Cards.ToArray();Answer(m,c=>c.Id==choice.Id);Reach(m,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Require(m.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==2&&ids.All(id=>m.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location.Zone==CardZoneKind.DiscardPile),"A real two-entity counterspell pays both entities and counts exactly one additional actual Use before its committed pause.");Replay(m,mr);var chainId=m.ResolutionStack.OfType<NullificationWindowFrame>().Last().Id;var responseCount=m.Events.Select(e=>e.Payload).OfType<NullificationRespondedEvent>().Count();Require(m.ResolutionStack.OfType<CardUseFrame>().All(f=>f.IssuedNoResponse is null),"The extended counterspell parent trick has no inherited no-response policy.");Answer(m,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(m,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(m.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is [var multiBan],"A multi-entity actual counterspell issues the exact phase fact once.");Replay(m,mr);Require(m.Events.Select(e=>e.Payload).OfType<NullificationRespondedEvent>().Count()==responseCount&&m.Events.Select(e=>e.Payload).OfType<IssuedCardNoResponseEvent>().Last().Effect.ParentFrameId==chainId,"The extended counterspell stops only its exact chain node and resumes its root once without another response.");
        }
        foreach(var mode in new[]{"duel","arrow_barrage"})
        {
        var(n,nr)=Create(mode);Grow(n);var target=n.CreateCardZoneDiagnostics().First(c=>c.Location.Zone==CardZoneKind.Hand&&c.Location.OwnerSeat!=0&&c.CardKind==CardKind.Nullification).Location.OwnerSeat!.Value;
        if(mode=="arrow_barrage")
        {
            foreach(var other in Enumerable.Range(1,3).Where(seat=>seat!=target))
            while(n.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.Hand(other)&&c.CardKind==CardKind.Nullification))
            {Accept(n,new UseProgramSkillCommand(0,"fixture:driver","strip-nullif",[],[other],n.Revision,Prompt(n)!.PromptId));Reach(n,p=>p.SkillPrompt?.SkillId=="fixture:driver");Answer(n,_=>true);Reach(n,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);}
        }
        Play(n,n.GetHumanLegalActions().First(a=>mode=="duel"?a.Kind==LegalActionKind.Duel&&a.TargetSeats.Contains(target):a.Kind==LegalActionKind.ArrowBarrage));Reach(n,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Answer(n,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Reach(n,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.Nullification&&n.ResolutionStack.OfType<NullificationWindowFrame>().Last().ChainDepth>0);var expectedDepth=n.ResolutionStack.OfType<NullificationWindowFrame>().Last().ChainDepth+1;Answer(n,c=>c.Cards.Count==1);
        Reach(n,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Replay(n,nr);Answer(n,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(n,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(n.Events.Select(e=>e.Payload).OfType<NullificationResolvedEvent>().Last().ChainDepth==expectedDepth&&n.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==2,"Only the accepted counterspell node becomes unrespondable, preserving the original two genuine Play uses. Actual depth="+n.Events.Select(e=>e.Payload).OfType<NullificationResolvedEvent>().Last().ChainDepth+", expected="+expectedDepth+", count="+n.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount+".");Replay(n,nr);
        if(mode=="arrow_barrage")Require(n.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Dodge),"The issued Nullification node does not make its root global trick unrespondable.");
        }
    }
    public static void ConversionsAndPlayedResponses()
    {
        var(g,r)=Create("conversion");Grow(g);var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Snatch&&a.ConversionSource is not null&&a.TargetSeats.Contains(2));Play(g,a);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.Kind==DecisionKind.SelectTargetCard&&p.PlayerSeat==0);Require(Prompt(g)!.Choices.All(c=>c.Cards.Count==0)&&Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Unrespondable converted Snatch preserves the ordinary private hidden-slot choice.");Replay(g,r);Reject(g);Answer(g,_=>true);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1,"A real single-entity Snatch conversion receives first-use distance before payment and is one use.");Replay(g,r);
        var(m,mr)=Create("draw_two");Grow(m);var hand=m.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.Hand(0)).Select(c=>c.CardId).ToArray();Accept(m,new UseProgramSkillCommand(0,"fixture:driver","all",hand,[],m.Revision,Prompt(m)!.PromptId));Reach(m,p=>p.SkillPrompt?.SkillId=="fixture:driver");Replay(m,mr);Answer(m,c=>c.Parameters.GetValueOrDefault("view-as-id")=="all");Reach(m,p=>p.SkillPrompt?.SkillId=="classic:wanglie");var action=m.ResolutionStack.OfType<CardUseFrame>().Last().Action!;Require(action.PhysicalCards.Count==hand.Length&&hand.Length>1,"The actual all-hand conversion pays distinct real entities once.");Answer(m,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(m,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(m.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1,"A multi-entity conversion is one genuine use, independent of cost count.");Replay(m,mr);
        var(d,dr)=Create("duel-played");Grow(d);var defender=d.CreateCardZoneDiagnostics().First(c=>c.Location.Zone==CardZoneKind.Hand&&c.Location.OwnerSeat!=0&&c.CardKind==CardKind.Slash).Location.OwnerSeat!.Value;Accept(d,new UseProgramSkillCommand(0,"fixture:driver","wound",[],[defender],d.Revision,Prompt(d)!.PromptId));Reach(d,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Play(d,d.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.Contains(defender)));Reach(d,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Answer(d,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Reach(d,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);Answer(d,c=>c.Cards.Count==1);Reach(d,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(d.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1&&d.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is null&&d.GetHumanLegalActions().Any(a=>a.CardId is not null),"A played Duel Slash neither counts as a Play use nor offers Wanglie; declining the original optional use preserves further card permissions.");Replay(d,dr);
    }
    public static void NestedPaymentAndStrictContracts()
    {
        var(g,r)=Create("nested");Grow(g);var card=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);Play(g,card);var observed=false;var issued=false;
        for(var step=0;step<120;step++)
        {
            if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.PlayCard})break;
            if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger,SkillPrompt:{SkillId:"classic:wanglie"}}){Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");issued=true;}
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger,SkillPrompt:{SkillId:"fixture:observer"}}){Replay(g,r);Reject(g);Answer(g,_=>true);observed=true;}
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        Require(observed&&issued&&g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1&&g.CardMovements.Count(m=>m.CardId==card.CardId&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Real use payment triggers a nested owned movement pause, with one entity payment and one phase-use debit.");Replay(g,r);
        var valid=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:issued-policy","revision":1,"triggers":[{"id":"issue","window":"cardUseCommitted","ownerRelation":"actor","includeResponseUses":true,"cardCategories":["basic","trick"],"optional":true,"condition":{"kind":"all","children":[{"kind":"cardActionActorIsCurrentTurn"},{"kind":"cardActionPhaseIsPlay"}]},"effects":[{"op":"issueCardNoResponseAndPlayUseBan","target":"owner"}]}]}]}""";
        const string presentation="""{"schemaVersion":3,"skills":{"fixture:issued-policy":{"name":"阶段使用策略","description":"共享契约"}}}""";
        SkillProgramCatalog.Load(valid,presentation);
        foreach(var bad in new[]{valid.Replace("\"ownerRelation\":\"actor\"","\"ownerRelation\":\"observer\""),valid.Replace("\"cardUseCommitted\"","\"cardUseCompleted\""),valid.Replace("\"issueCardNoResponseAndPlayUseBan\"","\"draw\",\"amount\":1"),valid.Replace("\"target\":\"owner\"","\"target\":\"eventTarget\""),valid.Replace("\"cardActionPhaseIsPlay\"","\"always\"")})
        {var failed=false;try{SkillProgramCatalog.Load(bad,presentation);}catch(InvalidOperationException){failed=true;}Require(failed,"Unsafe issued policy participant/window/response primitive/target/phase contract is rejected before runtime.");}
    }
    public static void IssuedBanSurvivesSourceLoss()
    {
        var(g,r)=Create("slash");Grow(g);Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Accept(g,new UseProgramSkillCommand(0,"fixture:driver","lose-source",[],[],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is [var ban]&&ban.Source.SkillId=="classic:wanglie"&&!g.GetHumanLegalActions().Any(a=>a.CardId is not null),"Issued use prohibition retains exact original source after actual grant loss.");Replay(g,r);
    }
    public static void NativeAiFirstDistance()
    {
        var(g,r)=Create("ai-slash");Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        var uses=g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Select(e=>e.Action).Where(a=>a.Type==CardActionType.Use&&a.ActorSeat==2&&a.EffectiveKind==CardKind.Slash&&a.TargetSeats.Contains(0)).ToArray();
        Require(uses.Length>0&&uses.All(a=>g.Events.Select(e=>e.Payload).OfType<ActualPlayPhaseCardUseRecordedEvent>().Any(e=>e.CardActionId==a.ActionId&&e.State.UseCount==1)),"Actual native AI uses its first Slash across the two-seat distance before real payment. Actions="+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Select(e=>new{e.Action.ActorSeat,e.Action.EffectiveKind,e.Action.TargetSeats})));
        Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Native AI distance and issued policy use no artificial answers.");Replay(g,r);
    }
    public static void NativeAiPrivateEffectChoice()
    {
        var(g,r)=Create("ai-fire");Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.FireAttackReveal);
        Require(g.Events.Select(e=>e.Payload).OfType<IssuedCardNoResponseEvent>().Any(e=>e.Effect.Source.OwnerSeat!=0)&&!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Actual AI accepts its offensive issued policy, without manufactured AI answers.");
        Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Unrespondable Fire Attack preserves the target's ordinary private reveal choice.");Replay(g,r);Reject(g);Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);Replay(g,r);
    }
    private static void Grow(GameEngine g){Accept(g,new UseProgramSkillCommand(0,"fixture:driver","grow",[],[],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);}
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> done)
    {
        for(var i=0;i<300;i++)
        {
            if(Prompt(g) is {} p&&done(p))return;
            if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.FireAttackReveal})Answer(g,c=>c.Cards.Count==1);
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.RescueDying})Answer(g,c=>c.Cards.Count==0);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed fixture prompt unreachable: "+JsonSerializer.Serialize(Prompt(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(v=>SnapshotJson.Serialize(g.CreateSnapshot(v))).ToArray(),Frames=g.ResolutionStack,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Movements=g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All observers, actual entity movements, typed parent frames and command JSON replay match.");}
    private static void Reject(GameEngine g){var s=State(g);var p=Prompt(g)!;Require(!g.Submit(new AnswerPromptCommand((p.PlayerSeat+1)%4,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&s==State(g),"Wrong actor answer is atomically rejected.");Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new ChoiceId("illegal"),g.Revision)).Accepted&&s==State(g),"Illegal choice is atomically rejected.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static (GameEngine,ContentRegistry)Create(string mode)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-chendao-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:owner",g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wanglie",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:driver","revision":1,"viewAs":[{"id":"snatch","inputKinds":[],"inputSuits":[],"outputKind":"snatch","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"multi-nullif","inputKinds":[],"inputSuits":[],"inputCount":2,"extendedUse":true,"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[{"id":"all","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all","outputKind":"arrowBarrage"}]},{"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},{"id":"strip-nullif","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"cardKinds":["nullification"],"count":1,"destination":"discardPile"}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},{"id":"gift-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardKinds":["slash"],"count":1,"destination":"selectedTargetHand"}]},{"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:wanglie"],"sourceBind":"standard:none"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:driver":{"name":"真实抽牌","description":"最小实体夹具"}}}""");b.AddSkill(new("fixture:driver","真实抽牌","实体夹具"){Program=c.Programs["fixture:driver"]});
            var observer=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:observer","revision":1,"triggers":[{"id":"payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:observer":{"name":"真实付款移动观察","description":"嵌套暂停","optionLabels":{"continue":"继续"}}}}""");b.AddSkill(new("fixture:observer","移动观察","实体窗口"){Program=observer.Programs["fixture:observer"]});
            b.AddGeneral(new("fixture:owner","陈到机制","supporter",mode is "ai-fire" or "ai-slash"?"standard:none":"classic:wanglie","shu",12,mode=="nested"?["fixture:driver","fixture:observer"]:mode=="slash-program-dodge"?["fixture:driver","classic:shifei"]:["fixture:driver"]));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:target-{i}","目标","supporter",mode is "ai-fire" or "ai-slash"?"classic:wanglie":"standard:none","wei",12,[]));
            b.AddDeck(new("fixture:deck","真实实体",3,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(mode=="duel-played"?i%2==0?"standard:duel":"standard:slash":mode is "equipment" or "borrowed_sword"? i%3==0?"standard:crossbow":i%3==1?"standard:slash":"classic:borrowed-sword":i%3==0?"standard:"+(mode=="ai-fire"?"fire_attack":mode=="conversion"?"dodge":mode is "nested" or "ai-slash" or "slash-program-dodge"?"slash":mode):i%3==1?"standard:dodge":"standard:nullification",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-chendao-fixture","机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:owner","fixture:target-1","fixture:target-2","fixture:target-3"]));
        }
    }
}
