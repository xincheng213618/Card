using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CuiYanChecks
{
    private const string Owner="fixture:cui-owner",Mode="identity:classic-cui",Driver="fixture:cui-driver",Peer="fixture:cui-peer-response",Quiet="fixture:cui-quiet",Watch="fixture:cui-watch",Health="fixture:cui-health",Conditional="fixture:cui-conditional",Rescue="fixture:cui-rescue",Foreign="fixture:cui-foreign",Shutong="ol:shutong",Zhijie="ol:zhijie";
    private static string Scene="";
    public static void ShutongSelfEquipmentMultiMaterialAndNextUseCold()
    {
        var(g,r)=Start("equipment");var hand=V(g,0).HandCount;
        Native(g,a=>a.ConversionSource is null&&a.Kind==LegalActionKind.Equip);Reach(g,()=>At(g,Watch));
        var first=E<CuiYanStartedEvent>(g).Single();Need(first.DrawQualified&&first.Suit==Suit.Spade&&E<ShutongNextUseGrantedEvent>(g)is[{RecipientSeat:0,Suit:Suit.Spade}]&&V(g,0).HandCount==hand,"Real native self equipment is a designated self use, draws once, then issues one deduplicated exact-suit next-use restriction.");
        var receipt=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CuiYan is not null).CuiYan!;var frozen=false;try{((IList<int>)receipt.Targets).Add(3);}catch(NotSupportedException){frozen=true;}Need(frozen&&g.CreateSnapshot(1).Players[0].Hand.Count==0,"The own receipt targets are deeply frozen and other viewers cannot inspect the actual hand/draw entity.");
        g=Cold(g,r);Continue(g);Play(g);Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null),"The same printed/native suit cannot be used as the next physical equipment.");
        var blocked=V(g,0).Hand.First().Id;var state=State(g);var rejected=g.Submit(new PlayCardCommand(0,blocked,[],g.Revision,P(g)!.PromptId));Need(!rejected.Accepted&&State(g)==state,"An unpublished forbidden native use is rejected without paying or mutating committed state.");
        Pair(g);Reach(g,()=>At(g,Watch));var pair=g.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.Action?.PhysicalCards.Count==2);var current=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CuiYan is not null).CuiYan!;
        Need(pair.Action!.EffectiveSuit==Suit.Spade&&current.Suit==Suit.None&&current.DrawQualified&&E<ShutongNextUseConsumedEvent>(g).Count(e=>e.GrantFrameId==first.FrameId&&e.RecipientSeat==0&&!e.ResponseUse)==1&&E<ShutongNextUseGrantedEvent>(g).All(e=>e.FrameId!=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CuiYan is not null).Id),"Two same-Spade physical materials retain the old shared appearance, are no-suit for Shutong, consume the prior next-use once, and do not invent a new suit ban.");
        g=Cold(g,r);Continue(g);Play(g);Need(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null),"After the genuine no-suit use, another same-suit physical card is legal again.");_=Cold(g,r);
    }
    public static void ShutongTrueResponsesPlayedAndTurnExpiryCold()
    {
        foreach(var scene in new[]{"played","dodge","nullification","expiry","rescue-native","rescue-converted"})
        {
            var(g,r)=Start(scene);Use(g,"stock");Play(g);NativeConverted(g,CardKind.Alcohol,Suit.Heart);Reach(g,()=>At(g,Watch));g=Cold(g,r);Continue(g);Play(g);var grant=E<ShutongNextUseGrantedEvent>(g).Single(e=>e.RecipientSeat==0);
            Need(grant.Suit==Suit.Heart,"The issued suit is Heart, not a red/black prohibition.");
            if(scene.StartsWith("rescue-",StringComparison.Ordinal))
            {
                Need(V(g,0).Hand.Any(c=>c.Suit==Suit.Heart)&&V(g,0).Hand.Any(c=>c.Suit==Suit.Spade),"Both forbidden and allowed physical suits really remain before the dying request.");
                Use(g,"dying");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying});var rescue=P(g)!;
                var choices=rescue.Choices.Where(c=>c.Parameters.GetValueOrDefault("response")is"peach"or"alcohol").ToArray();
                Need(V(g,0).Hp==0&&choices.All(c=>c.Cards.Count==1&&V(g,0).Hand.Single(h=>h.Id==c.Cards[0]).Suit==Suit.Spade)&&new[]{"peach","alcohol"}.All(name=>choices.Any(c=>c.Parameters.GetValueOrDefault("response")==name))&&choices.All(c=>(c.Parameters.ContainsKey("conversion-skill-id"))==(scene=="rescue-converted")),"The genuine dying menu suppresses Heart Peach and Wine for native cards and exact conversions, while both legal Spade rescue kinds remain: "+Diagnostic(g));
                var paid=choices.First(c=>c.Parameters.GetValueOrDefault("response")=="peach");g=Cold(g,r);Answer(g,c=>c.Id==paid.Id);Reach(g,()=>At(g,Watch));
                Need(E<ShutongNextUseConsumedEvent>(g).Count(e=>e.GrantFrameId==grant.FrameId&&e.RecipientSeat==0&&!e.ResponseUse)==1&&V(g,0).Hp==0,"A real accepted Spade rescue Use consumes the previous Heart restriction before its target-finalized draw and native recovery.");g=Cold(g,r);Continue(g);Play(g);
                Need(V(g,0).Hp==1&&E<RecoveryAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==0&&e.Amount==1)==1,"Cold rescue return applies the paid native recovery once after its genuine draw child.");_=Cold(g,r);continue;
            }
            if(scene=="expiry")
            {End(g);Reach(g,()=>g.State.CurrentSeat==0&&g.State.TurnNumber>grant.ActualTurn&&P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});Need(!E<ShutongNextUseConsumedEvent>(g).Any(e=>e.GrantFrameId==grant.FrameId)&&g.GetHumanLegalActions().Any(a=>a.CardId is{}id&&V(g,0).Hand.Any(c=>c.Id==id&&c.Suit==Suit.Heart)),"An unused grant expires only when its actual turn cursor returns; no fake use consumption is emitted.");_=Cold(g,r);continue;}
            Use(g,scene=="dodge"?"incoming":"incoming-duel",[1]);
            if(scene=="dodge")
            {Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-",StringComparison.Ordinal)==true)==true);Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});}
            else if(scene=="nullification")Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification});
            else Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});
            var prompt=P(g)!;var heart=prompt.Choices.Where(c=>c.Cards.Count==1&&V(g,0).Hand.Any(h=>h.Id==c.Cards[0]&&h.Suit==Suit.Heart)).ToArray();var spade=prompt.Choices.FirstOrDefault(c=>c.Cards.Count==1&&V(g,0).Hand.Any(h=>h.Id==c.Cards[0]&&h.Suit==Suit.Spade));
            Need(scene=="played"?heart.Length>0:heart.Length==0&&spade is not null,"Pure Duel played cards remain legal; actual own Dodge/Nullification rejects only the exact forbidden Heart suit: "+Diagnostic(g));g=Cold(g,r);
            Answer(g,c=>scene=="played"?c.Id==heart[0].Id:c.Id==spade!.Id);Play(g);
            Need(E<ShutongNextUseConsumedEvent>(g).Count(e=>e.GrantFrameId==grant.FrameId&&e.RecipientSeat==0&&e.ResponseUse)==(scene=="played"?0:1),"True response use consumes once; pure played does not consume a use-only obligation.");
            Need(E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0),"The response was genuinely paid and executed by the human through the native request.");_=Cold(g,r);
        }
    }
    public static void ZhijiePreSkillHistoryActualEndAndRangeCold()
    {
        foreach(var scene in new[]{"history","missing-target"})
        {
            var(g,r)=Start(scene);
            if(scene=="history")Native(g,a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null);else NativeConverted(g,CardKind.Slash,Suit.Spade);
            Play(g);Need(!V(g,0).Skills!.Any(s=>s.Id==Zhijie)&&E<CuiYanActualUseActorEvent>(g).Any(e=>e.ActorSeat==0),"Real native actual-turn history exists before the owner acquires Zhijie.");Use(g,"grant-zhijie");Play(g);End(g);
            if(scene=="missing-target")
            {Reach(g,()=>g.State.CurrentSeat==0&&g.State.TurnNumber>1&&P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});Need(E<CuiYanEndQualifiedEvent>(g).Any(e=>e.ActualTurn==1&&e.OwnerSeat==0&&e.UsedOrPlayed&&!e.WasTargeted)&&!E<CuiYanStartedEvent>(g).Any(e=>e.Op==SkillProgramEffectOp.ResolveZhijieActualTurnEnd),"Using a card alone does not invent the separate ever-targeted condition.");_=Cold(g,r);continue;}
            Reach(g,()=>At(g,Zhijie));Need(P(g)!.Choices.SelectMany(c=>c.Targets).Order().SequenceEqual([1,3])&&E<CuiYanEndQualifiedEvent>(g).Single(e=>e.ActualTurn==1&&e.OwnerSeat==0)is{UsedOrPlayed:true,WasTargeted:true},"Native self equipment counts as targeting; the real end choice contains only the two current range-one neighbours, excluding self and seat2.");g=Cold(g,r);var hp=V(g,1).Hp;Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>At(g,Health));
            Need(E<CuiYanDamageIssuedEvent>(g)is[{SourceSeat:0,TargetSeat:1}]&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.CuiYan?.Stage==CuiYanStage.DamageChildren),"Zhijie issues one real cardless damage before its native after-damage child.");g=Cold(g,r);Continue(g);Reach(g,()=>E<CuiYanCompletedEvent>(g).Any());Need(V(g,1).Hp==hp-1&&E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==1)==1,"Cold native damage return preserves the one issued effect without repeating it.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("dead-turn-owner");while(V(g,0).HandCount>1){var id=V(g,0).Hand[0].Id;Accept(g,new UseProgramSkillCommand(0,Driver,"trim",[id],[],g.Revision,P(g)!.PromptId));Play(g);}End(g);
            Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});var turn=g.State.TurnNumber;var foreign=g.State.CurrentSeat;
            Need(foreign!=0&&E<CuiYanEverTargetedEvent>(g).Any(e=>e.ActualTurn==turn&&e.TurnOwner==foreign&&e.TargetSeat==0),"The foreign actual turn genuinely targets Cui Yan before the turn owner dies.");Answer(g,c=>c.Cards.Count==1);Reach(g,()=>At(g,Zhijie));
            Need(!V(g,foreign).IsAlive&&g.State.Winner==Winner.None&&E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==turn&&e.ActorSeat==foreign)&&E<CuiYanEndQualifiedEvent>(g).Any(e=>e.ActualTurn==turn&&e.TurnOwner==foreign&&e.OwnerSeat==0&&e.UsedOrPlayed&&e.WasTargeted),"A real Dodge Use followed by the non-lord actual turn owner's death still opens the living observer's exact AfterTurnEnded Zhijie.");
            var target=P(g)!.Choices.First().Targets.Single();var hp=V(g,target).Hp;g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([target]));Reach(g,()=>E<CuiYanCompletedEvent>(g).Length==1);
            Need(V(g,target).Hp==hp-1&&E<CuiYanDamageIssuedEvent>(g)is[{SourceSeat:0}]&&E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==target&&e.Amount==1)==1,"Dead actual-turn-owner typed return and cold replay preserve precisely one genuine Zhijie damage.");_=Cold(g,r);
        }
    }
    public static void ShutongIssuedLossDeathAndLegacyAlcoholCold()
    {
        foreach(var scene in new[]{"loss","death","legacy"})
        {
            var(g,r)=Start(scene);
            if(scene=="legacy")
            {Use(g,"dying");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying}&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="program-trigger"&&c.Parameters.GetValueOrDefault("skill-id")==Rescue));Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="program-trigger"&&c.Parameters.GetValueOrDefault("skill-id")==Rescue);Reach(g,()=>At(g,Watch));var use=g.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.CardKind==CardKind.Alcohol);Need(use is{CardId:0,PhysicalCardIds.Count:0,CuiYanLegacyAlcohol.Stage:CuiYanLegacyAlcoholStage.Targets}&&V(g,0).Hp==0&&E<CuiYanDrawIssuedEvent>(g).Single().ActualCount==1,"The real zero-entity self-rescue Wine enters actual target-finalized Draw1 before native recovery.");g=Cold(g,r);Continue(g);Play(g);Need(V(g,0).Hp==1&&E<RecoveryAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==0&&e.Amount==1)==1&&E<CuiYanLegacyAlcoholStageEvent>(g).Count(e=>e.Stage==CuiYanLegacyAlcoholStage.Recovery)==1,"Original Dying and legacy Wine return recover exactly once after the real draw children.");_=Cold(g,r);continue;}
            Native(g,a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null);Reach(g,()=>At(g,Watch));var grant=E<ShutongNextUseGrantedEvent>(g).Single(e=>e.RecipientSeat==0);Need(grant.Suit==Suit.Spade&&E<CuiYanDrawIssuedEvent>(g).Single().ActualCount==1,"Next-use entitlement is issued before the draw child mutates health/source.");g=Cold(g,r);Continue(g);
            if(scene=="death")
            {Reach(g,()=>!V(g,0).IsAlive);Need(g.State.Winner==Winner.None,"A real non-lord issuer death leaves a live native game.");g=Cold(g,r);Reach(g,()=>E<CuiYanCompletedEvent>(g).Any(e=>e.FrameId==grant.FrameId));Need(E<CuiYanDrawIssuedEvent>(g).Count(e=>e.FrameId==grant.FrameId)==1,"A paid draw source's death drains children and finishes its original receipt without another draw.");_=Cold(g,r);continue;}
            Play(g);Need(!V(g,0).Skills!.Any(s=>s.Id==Shutong)&&!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null),"Actual HP-conditional skill loss cannot revoke an already-issued suit prohibition.");Pair(g);Play(g);Need(E<ShutongNextUseConsumedEvent>(g).Count(e=>e.GrantFrameId==grant.FrameId&&e.RecipientSeat==0)==1&&E<CuiYanStartedEvent>(g).Length==1&&g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Equip&&a.ConversionSource is null),"A later legal no-suit true use consumes the retained grant once and lost ShuTong cannot issue another.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool At(GameEngine g,string skill)=>P(g)?.SkillPrompt?.SkillId==skill;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+" Scene="+Scene);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Cui Yan native command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var choice=p.Choices.FirstOrDefault(pick);Need(p.PlayerSeat==0&&choice is not null,"Missing actual human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,choice!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Pair(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,Driver,"pair",V(g,0).Hand.Take(2).Select(c=>c.Id).ToArray(),[1],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().First(pick);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});}
    private static void NativeConverted(GameEngine g,CardKind kind,Suit suit)=>Native(g,a=>a.ConversionSource?.SkillId==Driver&&a.PlayedCardKind==kind&&a.CardId is{}id&&V(g,0).Hand.Any(h=>h.Id==id&&h.Suit==suit));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<450;i++)
        {if(done())return;Need(g.State.Winner==Winner.None,"Unexpected native game completion: "+Diagnostic(g));var p=P(g);
         if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));
         else if(p.Kind==DecisionKind.PlayCard)End(g);
         else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
         else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
         else if(At(g,Watch)||At(g,Health))Continue(g);
         else if(At(g,Zhijie))Answer(g,_=>true);
         else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
         else throw new InvalidOperationException($"Unexpected Cui Yan prompt {method}:{line}: "+Diagnostic(g));}
        throw new InvalidOperationException($"Missing Cui Yan boundary {method}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Facts=g.Events.TakeLast(10).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay must preserve every private view, frozen frame, real native invoice and once-consumed grant.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        Scene=scene;var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,ModeId=Mode,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Renegade:Role.Lord,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral,PlayerSeat:0});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-cui-yan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:cui-yan");var defs=new List<JsonNode>();
            void Skill(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
            string A(string id,string effects,int targets=0,string more="")=>"{\"id\":\""+id+"\",\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,"+more+"\"effects\":["+effects+"]}";
            var acts=new[]{A("stock","{\"op\":\"draw\",\"target\":\"owner\",\"amount\":16}"),A("grant-zhijie","{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"ol:zhijie\"]}"),A("dying","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":9}"),A("incoming","{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",1),A("incoming-duel","{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}",1,"\"usesPerPhase\":2,"),"{\"id\":\"pair\",\"minCards\":2,\"maxCards\":2,\"sourceZones\":[\"hand\"],\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"useSelectedCardsAs\",\"target\":\"selectedTarget\",\"sourceBind\":\"pair\",\"outputKind\":\"slash\"}]}","{\"id\":\"trim\",\"minCards\":1,\"maxCards\":1,\"sourceZones\":[\"hand\"],\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}]}"};
            var kinds=new List<string>{"slash","duel","drawTwo","alcohol","dodge","nullification"};if(scene=="rescue-converted")kinds.Add("peach");
            var conversions=kinds.Select(k=>"{\"id\":\""+k+"\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"inputCount\":1,\"outputKind\":\""+k+"\",\"forPlay\":"+(k is"dodge"or"nullification"or"peach"?"false":"true")+",\"forResponse\":"+(k is"slash"or"dodge"or"nullification"or"peach"||k=="alcohol"&&scene=="rescue-converted"?"true":"false")+",\"allowSameKind\":true"+(k is"duel"or"drawTwo"?",\"singleCardTrickUse\":true":"")+"}").Append("{\"id\":\"pair\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"inputCount\":2,\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false,\"allowSameKind\":true}");
            Skill(Driver,"\"activations\":["+string.Join(',',acts)+"],\"viewAs\":["+string.Join(',',conversions)+"],\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0},{\"id\":\"slash-room\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":8,\"priority\":0},{\"id\":\"slash-range\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0}]");
            Skill(Peer,"\"viewAs\":[{\"id\":\"real-played-slash\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]");
            Skill(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            Skill(Foreign,"\"triggers\":[{\"id\":\"true-foreign-use-and-death\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingLeastHandCount\"},{\"op\":\"useVirtualCard\",\"target\":\"selectedTarget\",\"outputKind\":\"slash\",\"targetRestriction\":\"distanceUnlimitedAgainstTarget\",\"useCardActionWindows\":true},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}]}]");
            Skill(Conditional,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:shutong\",\"minimumHp\":9}]");
            Skill(Watch,"\"triggers\":[{\"id\":\"native-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.cui-yan.shutong.draw\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}"+(scene is"loss"or"death"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+(scene=="loss"?1:20)+"}":"")+"]}]");
            Skill(Health,"\"triggers\":[{\"id\":\"real-damage-child\",\"window\":\"afterDamageApplied\",\"subject\":\"damageSource\",\"damageOccurrence\":\"perDamage\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}]}]");
            Skill(Rescue,"\"triggers\":[{\"id\":\"true-self-wine\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"optional\":true,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualDyingAlcohol\",\"target\":\"owner\"}]}]");
            var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="真实崔琰原生边界夹具",optionLabels=n["id"]!.GetValue<string>()is Watch or Health?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()});
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实崔琰边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            var skills=new List<string>{Driver,Watch};if(scene=="dead-turn-owner")skills.Add(Zhijie);else if(scene is"history"or"missing-target"){}else skills.Add(scene=="loss"?Conditional:Shutong);if(scene=="history")skills.Add(Health);if(scene=="legacy")skills.Add(Rescue);
            b.AddSkill(new("fixture:cui-owner-pick","本人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"实际崔琰","ol-cui-yan","fixture:cui-owner-pick","wei",8,skills));
            var peers=Enumerable.Range(1,3).Select(i=>"fixture:cui-other-"+i).ToArray();foreach(var(id,i)in peers.Select((id,i)=>(id,i))){var pick="fixture:cui-pick-"+i;b.AddSkill(new(pick,"他人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(4-i))});b.AddGeneral(new(id,"真实其他角色","supporter",pick,"qun",9,scene=="dead-turn-owner"?[Quiet,Peer,Foreign]:[Quiet,Peer]));}
            b.AddCard(new("fixture:cui-card","原生弩","装备牌","真实固定同质实体",CardKind.Crossbow));b.AddCard(new("fixture:cui-peach","桃","基本牌","真实濒死救援实体",CardKind.Peach));b.AddCard(new("fixture:cui-wine","酒","基本牌","真实濒死自救实体",CardKind.Alcohol));var mixed=scene is"played"or"dodge"or"nullification"or"expiry"||scene.StartsWith("rescue-",StringComparison.Ordinal);b.AddDeck(new("fixture:cui-deck","崔琰真实小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(scene=="rescue-native"?(i%2==0?"fixture:cui-peach":"fixture:cui-wine"):"fixture:cui-card",scene=="rescue-native"?((i/2)%2==0?Suit.Heart:Suit.Spade):mixed&&i%2==0?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});var roles=new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}};if(scene=="death"){roles[nameof(Role.Rebel)]=2;roles[nameof(Role.Renegade)]=1;}b.AddMode(new(Mode,"崔琰实际原生模式",4,4,roles,"fixture:cui-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
