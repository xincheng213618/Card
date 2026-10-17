using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuBaChecks
{
    private const string Owner="fixture:liu-ba-owner",Mode="identity:classic-liu-ba",Driver="fixture:lb-driver",Quiet="fixture:lb-quiet",Watch="fixture:lb-watch",Health="fixture:lb-health",Conditional="fixture:lb-conditional",Tongdu="ol:tongdu",Zhubi="ol:zhubi";
    private static string Scene="";
    public static void TongduOriginalEntityTruePlayEndDebtAndCold()
    {
        foreach(var scene in new[]{"self","foreign","spent","source-loss","skipped-play"})
        {
            var(g,r)=Start(scene,true);Activate(g,Tongdu);Reach(g,()=>Branch(g,"target"));Choose(g,"target",seat:scene=="foreign"?1:0);
            if(scene=="foreign")
            {Reach(g,()=>P(g)is{PlayerSeat:1});var p=P(g)!;Need(g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(2).PendingDecision is null,"Only the real donor sees the original Hand-card menu.");Need(!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted,"A different seat cannot answer the donor's private gift.");g=Cold(g,r);}
            else{Reach(g,()=>Branch(g,"card"));g=Cold(g,r);Choose(g,"card");}
            if(scene=="skipped-play")
            {Reach(g,()=>E<TongduGiftIssuedEvent>(g).Length==1);var skipped=E<TongduGiftIssuedEvent>(g).Single();Reach(g,()=>g.State.TurnNumber>skipped.ActualTurn&&Offer(g,Tongdu));Need(!E<TongduDebtConsumedEvent>(g).Any(e=>e.DebtId==skipped.FrameId)&&!g.CardMovements.Any(m=>m.CardId==skipped.CardId&&m.Reason.Value=="skill-program.liu-ba.tongdu.return"),"A genuinely skipped Play has no fabricated ending, and its original debt cannot migrate into a later actual turn.");_=Cold(g,r);continue;}
            Play(g);var gift=E<TongduGiftIssuedEvent>(g).Single();Need(gift.Source.OwnerSeat==0&&V(g,0).Hand.Any(c=>c.Id==gift.CardId)&&g.CardMovements.Count(m=>m.CardId==gift.CardId&&m.Reason.Value=="skill-program.liu-ba.tongdu.gift")== (scene=="foreign"?1:0),"Tongdu commits the exact chosen donor entity; self-gift has no fake same-zone move.");
            if(scene=="spent"){Use(g,"discard",cards:[gift.CardId]);Play(g);}if(scene=="source-loss"){Use(g,"lose-tongdu");Play(g);Need(!Has(g,0,Tongdu),"The real original Tongdu source was lost before the debt became due.");}
            g=Cold(g,r);End(g);Reach(g,()=>E<TongduDebtConsumedEvent>(g).Any());var due=E<TongduDebtConsumedEvent>(g).Single();var returned=g.CardMovements.Where(m=>m.CardId==gift.CardId&&m.Reason.Value=="skill-program.liu-ba.tongdu.return").ToArray();
            Need(due.DebtId==gift.FrameId&&due.ActualTurn==gift.ActualTurn&&E<LiuBaStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd).ParentId==due.ParentId&&(scene=="spent"?returned.Length==0:returned is[{To.Zone:CardZoneKind.DrawPile}]&&Deck(g)[^1]==gift.CardId),"The first genuine PlayEnding consumes exactly the issued same-turn original-card debt, without reclaiming a spent card or needing a live source.");_=Cold(g,r);
        }
    }
    public static void ZhubiDynamicMaximumRecastAndMintChildrenCold()
    {
        {
            var(g,r)=Start("quota");var limit=V(g,0).MaxHp;for(var i=0;i<limit;i++)Recast(ref g,r,0);
            Need(V(g,0).MaxHp==limit&&E<LiuBaStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)==limit&&!g.GetHumanLegalActions().Any(a=>a.SkillId==Zhubi),$"The initial real Play quota equals current maximum HP: initial={limit}, current={V(g,0).MaxHp}, actualRecasts={E<LiuBaStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)}.");
            Use(g,"max-plus");Play(g);Need(V(g,0).MaxHp==limit+1&&g.GetHumanLegalActions().Any(a=>a.SkillId==Zhubi),"A true same-Play maximum-HP increase opens exactly the new dynamic allowance.");Recast(ref g,r,0);Need(!g.GetHumanLegalActions().Any(a=>a.SkillId==Zhubi),"The one additional recast exhausts the increased current maximum-HP quota.");
            Use(g,"max-minus");Play(g);Need(V(g,0).MaxHp==limit&&!g.GetHumanLegalActions().Any(a=>a.SkillId==Zhubi),"A later maximum-HP decrease does not reset prior actual Play expenditure.");
            var coins=E<LiuBaCoinMintedEvent>(g);Need(coins.Length==limit+1&&coins.Select(e=>e.CardId).Distinct().Count()==coins.Length&&coins.All(e=>g.CardMovements.Count(m=>m.Sequence==e.GainSequence&&m.CardId==e.CardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)&&m.Reason==CardMoveReasons.RecastDraw)==1)&&E<CardRecastEvent>(g).Count()==limit+1,"Every true recast rewards exactly its real new Draw entity and exactly one native recast fact.");Need(coins.All(e=>V(g,0).Hand.Single(c=>c.Id==e.CardId).SkillMarkLabel?.Contains("币",StringComparison.Ordinal)==true),"Only actual reward entities carry the physical coin label.");_=Cold(g,r);
        }
        foreach(var scene in new[]{"lion","ox"})
        {
            var(g,r)=Start(scene);var kind=scene=="lion"?CardKind.SilverLion:CardKind.WoodenOx;var gear=V(g,0).Hand.First(c=>c.Kind==kind).Id;Use(g,"place",[0],[gear]);Play(g);
            int? grain=null;if(scene=="lion"){Use(g,"lose");Play(g);}else{grain=V(g,0).Hand.First().Id;Use(g,"grain",cards:[grain.Value]);Play(g);}
            Use(g,"recast-coin",skill:Zhubi);Choose(g,"target",seat:0);Choose(g,"card",gear);
            if(scene=="lion"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(E<LiuBaCoinMintedEvent>(g).Length==0&&V(g,0).Hp==V(g,0).MaxHp,"Native Silver Lion recovery occurs before the recast reward.");g=Cold(g,r);Continue(g);}
            Play(g);Need(E<LiuBaCoinMintedEvent>(g).Length==1&&E<CardRecastEvent>(g).Single().CardId==gear,"A real equipped recast still mints exactly one reward after native removal children.");if(grain is{}id)Need(g.CardMovements.Any(m=>m.CardId==id&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard),"True Wooden Ox grain disposal is inherited from native equipment removal, without minting grain as coins.");_=Cold(g,r);
        }
    }
    public static void CoinEndingPrivateBottomSlotsAtomicExchangeCold()
    {
        foreach(var scene in new[]{"exchange","zero","short"})
        {
            var(g,r)=Start(scene);var count=scene=="short"?1:2;for(var i=0;i<count;i++)Recast(ref g,r,0);var marked=E<LiuBaCoinMintedEvent>(g).Select(e=>e.CardId).ToArray();var before=Deck(g);End(g);Reach(g,()=>Branch(g,"coin")||Branch(g,"done"));
            var holder=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LiuBa is{Stage:LiuBaStage.Coins});var view=holder.LiuBa!.ViewedBottom.ToArray();Need(view.SequenceEqual(before.Take(5))&&g.CreateSnapshot(0).PrivateRevealedCards!.Select(c=>c.Id).SequenceEqual(view)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PrivateRevealedCards is null)&&Enumerable.Range(0,4).All(s=>!g.CreateSnapshot(s).PublicRevealedCards!.Any(c=>view.Contains(c.Id))),"Only the actual holder privately views the true available bottom five in order; none are publicly revealed, drawn or moved.");
            var p=P(g)!;Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted,"A different player cannot choose another holder's coin or bottom entity.");g=Cold(g,r);
            if(scene=="zero"){Choose(g,"done");Reach(g,()=>E<LiuBaCompletedEvent>(g).Any(e=>e.FrameId==holder.Id));Need(before.SequenceEqual(Deck(g))&&!E<LiuBaEntityMovedEvent>(g).Any(e=>e.FrameId==holder.Id),"Zero exchange leaves every deck slot and physical coin unchanged.");}
            else
            {
                foreach(var id in marked)Choose(g,"coin",id);Choose(g,"done");var chosen=view.Take(count).Reverse().ToArray();foreach(var id in chosen){g=Cold(g,r);Choose(g,"bottom",id);}Reach(g,()=>E<LiuBaCompletedEvent>(g).Any(e=>e.FrameId==holder.Id));
                var moved=E<LiuBaEntityMovedEvent>(g).Where(e=>e.FrameId==holder.Id).ToArray();var expected=before.Select(id=>Array.IndexOf(chosen,id)is var index&&index>=0?marked[index]:id).ToArray();Need(moved.Length==count*2&&moved.Select(e=>e.BatchId).Distinct().Count()==1&&moved.Count(e=>e.To==CardLocation.DrawPile)==count&&moved.Count(e=>e.From==CardLocation.DrawPile&&e.To==CardLocation.Hand(0))==count&&expected.SequenceEqual(Deck(g)),"Equal complete sets exchange in one actual native batch, replacing the chosen bottom slots while preserving all other deck order.");
                Need(chosen.All(id=>V(g,0).Hand.Single(c=>c.Id==id).SkillMarkLabel?.Contains("币",StringComparison.Ordinal)!=true)&&E<LiuBaCoinMintedEvent>(g).Length==marked.Length,"Unmarked obtained bottom cards are not minted as coins by an exchange.");
            }
            _=Cold(g,r);
        }
    }
    public static void PermanentTransferredCoinsPaidLossDeathAndRecoveryCold()
    {
        foreach(var scene in new[]{"transfer","transfer-death"})
        {
            var(g,r)=Start(scene);Recast(ref g,r,0);var coin=E<LiuBaCoinMintedEvent>(g).Single();Use(g,"gift",[1],[coin.CardId]);Play(g);
            if(scene=="transfer"){Use(g,"lose-zhubi");Play(g);Need(!Has(g,0,Zhubi)&&V(g,1).HandCount>0,"The original skill source is genuinely lost after a real transfer.");End(g);}else Use(g,"die");
            Reach(g,()=>P(g)is{PlayerSeat:1}&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnerSeat==1&&f.LiuBa is{Stage:LiuBaStage.Coins}));Need(g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(1).PrivateRevealedCards is{Count:>0}&&E<LiuBaCoinMintedEvent>(g).Length==1&&g.CreateCardZoneDiagnostics().Single(z=>z.CardId==coin.CardId).Location==CardLocation.Hand(1),"The original physical coin follows transfer and issues the actual new holder's private Ending even after original source loss/death; no second mint occurs.");if(scene=="transfer-death")Need(!V(g,0).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0),"The original coin issuer truly died before the independent holder's next Ending.");_=Cold(g,r);
        }
        foreach(var scene in new[]{"paid-loss","paid-death"})
        {
            var(g,r)=Start(scene);Use(g,"recast-coin",skill:Zhubi);Choose(g,"target",seat:0);Choose(g,"card");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var paid=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LiuBa is not null).Id;Need(!E<LiuBaCoinMintedEvent>(g).Any()&&E<LiuBaEntityMovedEvent>(g).Count(e=>e.FrameId==paid&&e.Step==1)==1,"The real recast payment opens its native child before any reward.");g=Cold(g,r);Continue(g);Reach(g,()=>E<LiuBaCompletedEvent>(g).Any(e=>e.FrameId==paid));
            Need(E<LiuBaEntityMovedEvent>(g).Count(e=>e.FrameId==paid&&e.Step==1)==1&&E<LiuBaCompletedEvent>(g).Count(e=>e.FrameId==paid)==1,"Paid recovery, source-loss or death children return once without repaying the original cost.");if(scene=="paid-loss")Need(!Has(g,0,Zhubi)&&E<LiuBaCoinMintedEvent>(g).Count(e=>e.FrameId==paid)==1,"A genuine HP-conditional source loss cannot revoke the paid living recipient's reward.");else Need(!V(g,0).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&!E<LiuBaCoinMintedEvent>(g).Any(e=>e.FrameId==paid),"A genuinely dead recipient receives no fabricated draw or coin after native dying/death returns.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static bool Has(GameEngine g,int seat,string skill)=>V(g,seat).Skills!.Any(s=>s.Id==skill);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static int[] Deck(GameEngine g)=>g.CreateCardZoneDiagnostics().Where(z=>z.Location==CardLocation.DrawPile).OrderBy(z=>z.ZoneIndex).Select(z=>z.CardId).ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="liu-ba"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static bool Offer(GameEngine g,string skill)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool test,string text){if(!test)throw new InvalidOperationException(text);}
    private static void Accept(GameEngine g,GameCommand command){try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Liu Ba command.");}catch(Exception ex){throw new InvalidOperationException(Scene+" "+command.GetType().Name+": "+Diagnostic(g),ex);}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var choice=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&choice is not null,"Missing actual human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,choice!.Id,g.Revision));}
    private static void Choose(GameEngine g,string branch,int? card=null,int? seat=null)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch&&(card is null||c.Cards.SequenceEqual([card.Value]))&&(seat is null||c.Targets.SequenceEqual([seat.Value])));
    private static void Activate(GameEngine g,string skill){Reach(g,()=>Offer(g,skill));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string activation,int[]? seats=null,int[]? cards=null,string skill=Driver)=>Accept(g,new UseProgramSkillCommand(0,skill,activation,cards??[],seats??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Recast(ref GameEngine g,ContentRegistry r,int target){Use(g,"recast-coin",skill:Zhubi);Choose(g,"target",seat:target);if(target==0){g=Cold(g,r);var marked=E<LiuBaCoinMintedEvent>(g).Select(e=>e.CardId).ToHashSet();var card=P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="card"&&!marked.Contains(c.Cards.Single())).Cards.Single();Choose(g,"card",card);}Play(g);}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<420;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Unexpected completion {method}:{line} "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId is Health or Watch)Continue(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(Branch(g,"done"))Choose(g,"done");
            else throw new InvalidOperationException($"Unexpected prompt {method}:{line} "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing boundary {method}:{line} "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).MaxHp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,LiuBa=(f as ProgramSkillFrame)?.LiuBa?.Stage}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold replay preserves all private donor/bottom views, exact coin identities, dynamic phase usage and once-paid native child returns.");return cold;}
    private static(GameEngine,ContentRegistry)Start(string scene,bool preparation=false)
    {
        Scene=scene;var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"paid-death"or"transfer-death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(preparation)Reach(g,()=>Offer(g,Tongdu));else Play(g);return(g,registry);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-liu-ba",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:liu-ba");var defs=new List<JsonNode>();
            void Skill(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
            string Act(string id,string effects,int cards=0,int targets=0,string more="")=>"{\"id\":\""+id+"\",\"minCards\":"+cards+",\"maxCards\":"+cards+",\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,"+more+"\"effects\":"+effects+"}";
            var acts=new[]{Act("die","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}]"),Act("max-plus","[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":1}]"),Act("max-minus","[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":-1}]"),Act("lose","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]"),Act("discard","[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}]",1,0,"\"sourceZones\":[\"hand\"],"),Act("lose-tongdu","[{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"ol:tongdu\"],\"sourceBind\":\"standard:none\"}]"),Act("lose-zhubi","[{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"ol:zhubi\"],\"sourceBind\":\"standard:none\"}]"),Act("place","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}]",1,1,"\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"),Act("grain","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"grain\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"grain\",\"destination\":\"ownerPersistentZone\",\"destinationZone\":\"woodenOxGrain\"}]",1,0,"\"sourceZones\":[\"hand\"],"),Act("gift","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gift\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"gift\",\"destination\":\"selectedTargetHand\"}]",1,1,"\"sourceZones\":[\"hand\"],")};
            Skill(Driver,"\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0}],\"activations\":["+string.Join(',',acts)+"]");
            Skill(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            if(scene=="lion")Skill(Health,"\"triggers\":[{\"id\":\"real-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"health\",\"options\":[{\"id\":\"continue\"}]}]}]");
            if(scene=="paid-loss")Skill(Conditional,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:zhubi\",\"minimumHp\":9}]");
            if(scene is"paid-loss"or"paid-death")Skill(Watch,"\"triggers\":[{\"id\":\"real-paid-recast\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\""+CardMoveReasons.RecastDiscard.Value+"\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+(scene=="paid-death"?20:1)+"}]}]");
            var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="刘巴真实原生边界夹具",optionLabels=new[]{"triggers","activations"}.SelectMany(k=>n[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var programs=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in programs.Programs)b.AddSkill(new(id,id,"真实刘巴夹具"){Program=p,ProgramPresentation=programs.Presentations[id]});
            var skills=new List<string>{Driver,Tongdu,scene=="paid-loss"?Conditional:Zhubi};if(scene=="skipped-play")skills.Add(Quiet);if(scene=="lion")skills.Add(Health);if(scene is"paid-loss"or"paid-death")skills.Add(Watch);
            b.AddSkill(new("fixture:lb-owner-pick","本人","固定公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});b.AddGeneral(new(Owner,"刘巴真实本人","ol-liu-ba","fixture:lb-owner-pick","shu",scene is"paid-loss"or"paid-death"?8:3,skills));var peers=Enumerable.Range(1,3).Select(i=>"fixture:lb-peer-"+i).ToArray();foreach(var(id,index)in peers.Select((id,index)=>(id,index))){var pick="fixture:lb-pick-"+index;b.AddSkill(new(pick,"其他角色","固定公开顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(3-index))});b.AddGeneral(new(id,"真实其他角色","supporter",pick,"shu",12,[Quiet]));}
            var kind=scene=="lion"?CardKind.SilverLion:scene=="ox"?CardKind.WoodenOx:CardKind.Dodge;var cardId="fixture:lb-card";b.AddCard(new(cardId,"实体","真实原生牌","独立原实体",kind));b.AddDeck(new("fixture:lb-deck","刘巴小固定牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene=="short"?22:80).Select(i=>new ContentDeckPhysicalCard(cardId,new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});b.AddMode(new(Mode,"刘巴真实四人",4,4,scene is"paid-death"or"transfer-death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:lb-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
