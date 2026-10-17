using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RuanYuChecks
{
    private const string Owner="fixture:ry-owner",Mode="identity:classic-ruan-yu",Driver="fixture:ry-driver",Quiet="fixture:ry-quiet",Watch="fixture:ry-watch",Health="fixture:ry-health",Conditional="fixture:ry-conditional",Phase="fixture:ry-phase",Xing="ol:xingzuo",Miao="ol:miaoxian";
    private static string Scene="";
    public static void XingzuoPrivateFullBottomOrderZeroAndShortDeckCold()
    {
        foreach(var scene in new[]{"front","zero","cancel","short","empty"})
        {
            var(g,r)=Start(scene,true);Activate(g,Xing);Reach(g,()=>Branch(g,"done"));var f=Own(g);var original=f.RuanYu!.ViewedBottom.ToArray();var before=Deck(g);
            Need(original.SequenceEqual(before.Take(3))&&(g.CreateSnapshot(0).PrivateRevealedCards??[]).Select(c=>c.Id).SequenceEqual(original)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PrivateRevealedCards is null)&&Enumerable.Range(0,4).All(s=>!g.CreateSnapshot(s).PublicRevealedCards.Any(c=>original.Contains(c.Id))),"Only the actual owner privately sees the genuine available bottom three, without moving or revealing them publicly.");
            var p=P(g)!;Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted,"Another seat cannot answer the private original-card/order draft.");g=Cold(g,r);
            if(scene=="cancel")
            {Choose(g,"cancel");Play(g);Need(!E<XingzuoEndingIssuedEvent>(g).Any()&&before.SequenceEqual(Deck(g)),"Cancellation has no exchange, reorder or later Ending grant.");End(g);Reach(g,()=>g.State.TurnNumber>1);Need(!E<XingzuoEndingConsumedEvent>(g).Any(),"Cancellation cannot fabricate an Ending issuance.");continue;}
            int? hand=null;int? taken=null;if(scene is"front"or"short"){hand=V(g,0).Hand.First().Id;Choose(g,"hand",hand);}
            Choose(g,"done");if(hand is not null){taken=original.Last();Choose(g,"bottom",taken);}
            var ordered=hand is{}selectedHandId?new[]{selectedHandId}.Concat(original.Where(c=>c!=taken)).Reverse().ToArray():original.Reverse().ToArray();foreach(var id in ordered){g=Cold(g,r);Choose(g,"order",id);}Play(g);
            Need(Deck(g).SequenceEqual(ordered.Concat(before.Skip(original.Length)))&&E<XingzuoEndingIssuedEvent>(g).Length==1&&E<RuanYuCompletedEvent>(g).Count(e=>e.FrameId==f.Id)==1,"Accepted confirmation permits the complete final bottom permutation, including zero-Hand reorder and an empty/short deck, and issues exactly one Ending grant.");
            var moved=E<RuanYuEntityMovedEvent>(g).Where(e=>e.FrameId==f.Id).ToArray();Need(hand is null?moved.Length==0:moved.Length==2&&moved.Select(e=>e.BatchId).Distinct().Count()==1&&Zone(g,taken!.Value)==CardLocation.Hand(0)&&Zone(g,hand.Value)==CardLocation.DrawPile,"A real equal exchange moves precisely both original entities in one batch; a pure reorder has no fake card movement.");
            End(g);Reach(g,()=>Offer(g,Xing));Need(P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"),"Even a confirmed zero exchange genuinely offers the later optional Ending branch.");_=Cold(g,r);
        }
    }
    public static void XingzuoEndingAllHandRandomBottomDebtLossAndDeathCold()
    {
        foreach(var scene in new[]{"ending-self","ending-foreign","ending-source-loss","ending-paid-loss","ending-paid-death"})
        {
            var(g,r)=Start(scene,true);Activate(g,Xing);Choose(g,"done");OrderDefault(g);Play(g);var issued=E<XingzuoEndingIssuedEvent>(g).Single();
            if(scene=="ending-source-loss"){Use(g,"lose-xing");Play(g);Need(!Has(g,0,Xing),"The real source is lost after the front confirmation, before the issued Ending.");}
            var target=scene=="ending-foreign"?1:0;var oldHand=V(g,target).Hand.Select(c=>c.Id).ToArray();var before=Deck(g);var hp=V(g,0).Hp;End(g);Activate(g,Xing);Reach(g,()=>Branch(g,"target"));Need(P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0])),"A living nonempty-Hand owner is a legal Ending target.");g=Cold(g,r);Choose(g,"target",seat:target);
            if(scene is"ending-paid-loss"or"ending-paid-death")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(E<XingzuoEndingConsumedEvent>(g).Length==1&&E<RuanYuEntityMovedEvent>(g).Count(e=>e.FrameId==Own(g).Id)==oldHand.Length+Math.Min(3,before.Length),"All original exchange entities are paid before the genuine movement child; no HP penalty is paid early.");g=Cold(g,r);Continue(g);}
            Reach(g,()=>E<RuanYuCompletedEvent>(g).Any(e=>e.FrameId!=issued.DebtId));var due=E<XingzuoEndingConsumedEvent>(g).Single();var frame=E<RuanYuStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange).FrameId;var moves=E<RuanYuEntityMovedEvent>(g).Where(e=>e.FrameId==frame).ToArray();
            Need(due.DebtId==issued.DebtId&&due.FrameId==frame&&moves.Length==oldHand.Length+Math.Min(3,before.Length)&&moves.Select(e=>e.BatchId).Distinct().Count()==1&&moves.Where(e=>e.From==CardLocation.Hand(target)).Select(e=>e.CardId).Order().SequenceEqual(oldHand.Order()),"One exact issued source survives source loss and exchanges the frozen complete Hand against the actual available bottom, with no invented draws or discards.");
            if(scene=="ending-paid-death")Need(!V(g,0).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&!E<XingzuoHpLossPaidEvent>(g).Any(e=>e.FrameId==frame),"A real paid movement child may kill the owner; exchange/debt still return once and do not charge a dead owner again.");
            else
            {var placed=E<XingzuoBottomOrderedEvent>(g).Where(e=>e.FrameId==frame).OrderBy(e=>e.Index).Select(e=>e.CardId).ToArray();Need(placed.Order().SequenceEqual(oldHand.Order())&&Deck(g).Take(oldHand.Length).SequenceEqual(placed)&&V(g,target).Hand.Select(c=>c.Id).Order().SequenceEqual(before.Take(3).Order()),"Ending randomizes only the original Hand using replayed RNG and places that exact ordered set at the true bottom; the new Hand is the old actual bottom.");Need(V(g,0).Hp==hp-(scene=="ending-paid-loss"?1:0)-(oldHand.Length>3?1:0)&&E<XingzuoHpLossPaidEvent>(g).Count(e=>e.FrameId==frame)==(oldHand.Length>3?1:0),"The single real LoseHp equals the greater-than-three condition and follows the paid movement children.");if(scene=="ending-paid-loss")Need(!Has(g,0,Xing),"An actual HP-conditional source loss cannot revoke the paid Ending return.");}
            _=Cold(g,r);
        }
    }
    public static void MiaoxianBlackAllOrdinaryNativeUseAndForeignNullificationCold()
    {
        var(g,r)=Start("black");Use(g,"draw-eight");Play(g);var equipment=V(g,0).Hand.First(c=>c.Kind==CardKind.Crossbow).Id;Use(g,"place",[1],[equipment]);Play(g);var material=V(g,0).Hand.First(c=>Black(c)).Id;TrimTo(g,[material]);
        var options=g.GetHumanLegalActions().Where(a=>a.ConversionSource?.SkillId==Miao).ToArray();var expected=new[]{CardKind.DrawTwo,CardKind.Duel,CardKind.BarbarianAssault,CardKind.ArrowBarrage,CardKind.PeachGarden,CardKind.FiveGrains,CardKind.Dismantlement,CardKind.Snatch,CardKind.FireAttack,CardKind.IronChain,CardKind.BorrowedSword,CardKind.UnexpectedAssault};
        Need(expected.All(k=>options.Any(a=>a.PlayedCardKind==k))&&options.All(a=>a.CardId==material),"The unique actual black Hand entity exposes every currently legal ordinary-trick native family, including dedicated UnexpectedAssault and BorrowedSword.");g=Cold(g,r);PlayCard(g,options.First(a=>a.PlayedCardKind==CardKind.DrawTwo));Play(g);
        Need(E<MiaoxianBlackConsumedEvent>(g)is[{CardId:var paid}]&&paid==material&&Zone(g,material)==CardLocation.DiscardPile&&!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId==Miao),"A genuine native converted DrawTwo consumes the shared actual-turn black allowance once and cleans its real original entity.");
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&g.State.CurrentSeat==0&&P(g)is{Kind:DecisionKind.PlayCard});var next=V(g,0).Hand.First(c=>Black(c)).Id;TrimTo(g,[next]);Need(g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId==Miao),"A later genuine actual turn restores the black allowance, independent of view-as output names.");_=Cold(g,r);
        (g,r)=Start("black-nullification");material=V(g,0).Hand.First(Black).Id;TrimTo(g,[material]);End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification}p&&p.Choices.Any(c=>c.Cards.SequenceEqual([material])));Need(g.State.CurrentSeat!=0&&E<MiaoxianBlackConsumedEvent>(g).Length==0,"The unique black Hand remains usable as a true foreign-turn Nullification before acceptance.");g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([material]));Reach(g,()=>E<MiaoxianBlackConsumedEvent>(g).Any());
        Need(E<CardActionAcceptedEvent>(g).Any(e=>e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Nullification&&e.Action.ConversionChain.Any(s=>s.SkillId==Miao)&&e.Action.PhysicalCards.Any(c=>c.CardId==material))&&E<MiaoxianBlackConsumedEvent>(g).Single().ActualTurn==g.State.TurnNumber,"A real converted counterspell response spends the same actual-turn allowance with exact physical payment and original conversion identity.");_=Cold(g,r);
    }
    public static void MiaoxianRedUseResponsesWoodenOxAndPaidChildrenCold()
    {
        foreach(var scene in new[]{"red-use","red-paid-loss","red-paid-death"})
        {
            var(g,r)=Start(scene);var card=V(g,0).Hand.First().Id;TrimTo(g,[card]);PlayCard(g,g.GetHumanLegalActions().First(a=>a.CardId==card&&a.Kind==LegalActionKind.DrawTwo));Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            var own=Own(g);Need(E<MiaoxianRedDrawIssuedEvent>(g).Length==1&&V(g,0).HandCount==1&&!E<CardUseFinishedEvent>(g).Any(e=>e.CardId==card),"The original unique-red qualification is frozen before payment; its true one-card draw and native child precede DrawTwo's own two-card effect.");g=Cold(g,r);Continue(g);Reach(g,()=>E<RuanYuCompletedEvent>(g).Any(e=>e.FrameId==own.Id));
            if(scene=="red-paid-death")Need(!V(g,0).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0),"A paid draw child may genuinely kill its owner without repaying or manufacturing another reward.");
            else{Play(g);Need(V(g,0).HandCount==3&&E<MiaoxianRedDrawIssuedEvent>(g).Length==1&&g.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)==1,"Native DrawTwo resumes exactly once after the one-card reward child, retaining its two-card effect and exact single cost.");if(scene=="red-paid-loss")Need(!Has(g,0,Miao),"The actual HP-conditional original source is lost inside the already paid reward subtree.");else{card=V(g,0).Hand.First().Id;TrimTo(g,[card]);PlayCard(g,g.GetHumanLegalActions().First(a=>a.CardId==card&&a.Kind==LegalActionKind.DrawTwo));Play(g);Need(E<MiaoxianRedDrawIssuedEvent>(g).Length==2,"Distinct genuine unique-red Uses can each reward in one actual turn; the red branch has no once-per-turn cap.");}}
            _=Cold(g,r);
        }
        foreach(var scene in new[]{"red-dodge","red-played","red-nullification","ox-hand","ox-empty"})
        {
            var(g,r)=Start(scene);int card;
            if(scene.StartsWith("ox",StringComparison.Ordinal))
            {Use(g,"draw-eight");Play(g);var gear=V(g,0).Hand.First(c=>c.Kind==CardKind.WoodenOx).Id;Use(g,"place",[0],[gear]);Play(g);var grains=V(g,0).Hand.Where(c=>c.Kind==CardKind.Dodge).Take(2).Select(c=>c.Id).ToArray();foreach(var id in grains){Use(g,"grain",cards:[id]);Play(g);}card=grains[0];var keep=scene=="ox-hand"?new[]{V(g,0).Hand.First(c=>c.Kind==CardKind.Dodge).Id}:Array.Empty<int>();TrimTo(g,keep);Need(g.CreateSnapshot(0).Players[0].HandCount==keep.Length,"The Ox test uses the true remaining Hand separately from two real red grains.");}
            else{card=V(g,0).Hand.First().Id;TrimTo(g,[card]);}
            if(scene=="red-nullification")End(g);else if(scene=="red-played")Use(g,"duel",[1]);else Use(g,"incoming",[1]);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification}p&&p.Choices.Any(c=>c.Cards.SequenceEqual([card])));g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([card]));
            var reward=scene is"red-played"or"ox-hand"?0:1;Reach(g,()=>reward==0?g.CardMovements.Any(m=>m.CardId==card&&m.Reason==CardMoveReasons.Respond):E<MiaoxianRedDrawIssuedEvent>(g).Length==1);Need(E<MiaoxianRedDrawIssuedEvent>(g).Length==reward,"True Dodge Use triggers the red branch; Duel Slash is only played, and a red grain cannot substitute for an existing red Hand. With no red Hand, either red grain can trigger without counting other grains.");
            if(reward==1){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);}if(scene=="red-nullification"){Reach(g,()=>E<RuanYuCompletedEvent>(g).Any());Need(E<MiaoxianNativeCounterspellFinishedEvent>(g)is[var finished]&&finished.CardId==card&&Zone(g,card)==CardLocation.DiscardPile,"The single native counterspell's exact Nullification/NullificationFinished two-stage payment is frozen before its genuine red-use reward.");}else Play(g);Need(E<MiaoxianRedDrawIssuedEvent>(g).Length==reward,"No response-use completion or later native return repeats the same original reward.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static bool Black(CardSnapshot c)=>c.Suit is Suit.Spade or Suit.Club;
    private static bool Has(GameEngine g,int s,string skill)=>V(g,s).Skills!.Any(x=>x.Id==skill);
    private static CardLocation Zone(GameEngine g,int id)=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
    private static int[] Deck(GameEngine g)=>g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.DrawPile).OrderBy(c=>c.ZoneIndex).Select(c=>c.CardId).ToArray();
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame Own(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.RuanYu is not null);
    private static bool Branch(GameEngine g,string b)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="ruan-yu"&&c.Parameters.GetValueOrDefault("branch")==b)==true;
    private static bool Offer(GameEngine g,string s)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==s&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(Scene+": "+why);}
    private static void Accept(GameEngine g,GameCommand command){try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Ruan Yu command.");}catch(Exception ex){throw new InvalidOperationException(command.GetType().Name+" cause="+ex.GetType().Name+": "+ex.Message+" "+Diagnostic(g),ex);}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> test){var p=P(g)!;var c=p.Choices.FirstOrDefault(test);Need(p.PlayerSeat==0&&c is not null,"Missing actual human choice "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Choose(GameEngine g,string b,int? card=null,int? seat=null)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==b&&(card is null||c.Cards.SequenceEqual([card.Value]))&&(seat is null||c.Targets.SequenceEqual([seat.Value])));
    private static void Activate(GameEngine g,string s){Reach(g,()=>Offer(g,s));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? seats=null,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],seats??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void PlayCard(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    private static void TrimTo(GameEngine g,int[] keep){var ids=V(g,0).Hand.Where(c=>!keep.Contains(c.Id)).Select(c=>c.Id).ToArray();if(ids.Length>0){Use(g,"trim",cards:ids);Play(g);}}
    private static void OrderDefault(GameEngine g){while(Branch(g,"order"))Choose(g,"order",P(g)!.Choices.First().Cards.Single());}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<380;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Unexpected completion {method}:{line} "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId is Watch or Health)Continue(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Parameters.GetValueOrDefault("offer-option")=="target-0"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Parameters.GetValueOrDefault("offer-option")=="target-0");
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected prompt {method}:{line} "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing boundary {method}:{line} "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Winner,Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.RuanYu?.Stage}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves every private bottom/order view, original payment qualification, source generation, ordered physical exchange and native child return.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene,bool front=false)
    {
        Scene=scene;var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene.EndsWith("paid-death",StringComparison.Ordinal)?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(front)Reach(g,()=>Offer(g,Xing));else Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ruan-yu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ruan-yu");var defs=new List<JsonNode>();
            void Skill(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
            string Act(string id,string effects,int cards=0,int targets=0,string more="")=>"{\"id\":\""+id+"\",\"minCards\":"+cards+",\"maxCards\":"+cards+",\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,"+more+"\"effects\":"+effects+"}";
            var acts=new[]{Act("draw-eight","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":8}]"),Act("lose-xing","[{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"ol:xingzuo\"],\"sourceBind\":\"standard:none\"}]"),Act("place","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}]",1,1,"\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"),Act("grain","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"grain\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"grain\",\"destination\":\"ownerPersistentZone\",\"destinationZone\":\"woodenOxGrain\"}]",1,0,"\"sourceZones\":[\"hand\"],"),Act("incoming","[{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}]",0,1),Act("duel","[{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}]",0,1,"\"usesPerPhase\":2,").Replace("\"targetKind\":\"anyLiving\"","\"targetKind\":\"otherLiving\"")};
            var trim="{\"id\":\"trim\",\"minCards\":1,\"maxCards\":60,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"sourceZones\":[\"hand\"],\"usesPerTurn\":null,\"effects\":[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"trimmed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"trimmed\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}";
            Skill(Driver,"\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0}],\"activations\":["+string.Join(',',acts.Append(trim))+"]");
            Skill(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            Skill(Phase,"\"triggers\":[{\"id\":\"actual-foreign-trick\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"drawTwo\"}]}]");
            var red=scene.StartsWith("red-",StringComparison.Ordinal)||scene.StartsWith("ox-",StringComparison.Ordinal);var ending=scene.StartsWith("ending-paid",StringComparison.Ordinal);var paidLoss=scene.EndsWith("paid-loss",StringComparison.Ordinal);var paidDeath=scene.EndsWith("paid-death",StringComparison.Ordinal);
            if(paidLoss)Skill(Conditional,"\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\""+(ending?Xing:Miao)+"\",\"minimumHp\":9}]");
            if(red||ending)Skill(Watch,"\"triggers\":[{\"id\":\"real-paid-child\",\"window\":\""+(ending?"cardsMoved":"cardsGained")+"\",\"subject\":\"owner\",\""+(ending?"sourceZones":"destinationZones")+"\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\""+(ending?"skill-program.ruan-yu.xingzuo.exchange":"skill-program.ruan-yu.miaoxian.draw")+"\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}"+(paidLoss||paidDeath?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+(paidDeath?20:1)+"}":"")+"]}]");
            Skill(Health,"\"triggers\":[{\"id\":\"real-hp-child\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"hp\",\"options\":[{\"id\":\"continue\"}]}]}]");
            var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="阮瑀真实原生小夹具",optionLabels=new[]{"triggers","activations"}.SelectMany(k=>n[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生阮瑀夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            var skills=new List<string>{Driver,paidLoss&&ending?Conditional:Xing,paidLoss&&!ending?Conditional:Miao};if(red||ending)skills.Add(Watch);if(scene.StartsWith("ending",StringComparison.Ordinal)||paidLoss||paidDeath)skills.Add(Health);
            b.AddSkill(new("fixture:ry-owner-pick","本人","固定公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});b.AddGeneral(new(Owner,"阮瑀真实本人","ol-ruan-yu","fixture:ry-owner-pick","wei",paidLoss||paidDeath?8:12,skills));var peers=Enumerable.Range(1,3).Select(i=>"fixture:ry-peer-"+i).ToArray();foreach(var(id,index)in peers.Select((id,index)=>(id,index))){var pick="fixture:ry-pick-"+index;b.AddSkill(new(pick,"其他角色","固定公开顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(3-index))});b.AddGeneral(new(id,"真实其他角色","supporter",pick,"shu",12,scene.EndsWith("nullification",StringComparison.Ordinal)&&index==0?[Phase]:[Quiet]));}
            foreach(var(kind,id)in new[]{(CardKind.Dodge,"dodge"),(CardKind.Slash,"slash"),(CardKind.DrawTwo,"drawtwo"),(CardKind.Crossbow,"crossbow"),(CardKind.WoodenOx,"ox"),(CardKind.Nullification,"nullification")})b.AddCard(new("fixture:ry-"+id,id,"真实原生实体","不借视为身份",kind));
            var count=scene=="empty"?18:scene=="short"?20:112;b.AddDeck(new("fixture:ry-deck","小固定实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,count).Select(i=>new ContentDeckPhysicalCard("fixture:ry-"+(scene.StartsWith("ox-",StringComparison.Ordinal)?i%8==0?"ox":"dodge":scene=="black"?i%8==0?"crossbow":"dodge":scene=="red-played"?"slash":scene=="red-nullification"?"nullification":scene=="red-dodge"?"dodge":red?"drawtwo":"dodge"),red?Suit.Heart:i%2==0?Suit.Club:Suit.Heart,i%13+1)).ToArray()});b.AddMode(new(Mode,"阮瑀真实四人",4,4,paidDeath?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:ry-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
