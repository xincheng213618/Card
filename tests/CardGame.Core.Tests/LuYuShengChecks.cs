using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuYuShengChecks
{
    private const string Cang="ol:cangxin",Run="ol:runwei",Owner="fixture:lys-owner",Driver="fixture:lys-driver",Quiet="fixture:lys-quiet",Watch="fixture:lys-watch",Conditional="fixture:lys-conditional",Health="fixture:lys-health",Mode="identity:classic-lu-yu-sheng-fixture",Legacy="identity:lu-yu-sheng-legacy-fixture";
    public static void BottomRevealNativeDrawAndZeroHeartsCold()
    {
        var(g,r)=Start("draw-watch",true);var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LuYuSheng is not null);var bottom=f.LuYuSheng!.BottomCards.ToArray();
        Need(bottom.Length==3&&f.LuYuSheng is{Stage:LuYuShengStage.DrawChildren,Count:3,ActualCount:3}&&bottom.All(id=>g.CreateCardZoneDiagnostics().Any(z=>z.CardId==id&&z.Location==CardLocation.DrawPile)),"Cangxin publicly shows the three real bottom entities and draws three different cards from the native top without repositioning the bottom.");
        Need(Enumerable.Range(0,4).All(s=>bottom.All(id=>g.CreateSnapshot(s).PublicRevealedCards.Any(c=>c.Id==id)))&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"Original bottom cards are public to every viewer while the real native child choice stays private.");
        g=Cold(g,r);Continue(g);Play(g);var draw=E<LuYuShengMovementIssuedEvent>(g).Single(e=>e.Stage==LuYuShengStage.DrawChildren);
        Need(V(g).HandCount==9&&draw.Count==3&&g.CardMovements.Where(m=>m.Sequence>draw.Before&&m.Sequence<=draw.After).All(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)&&!bottom.Contains(m.CardId)),"The additive skill draw is followed by the independent normal Draw2 exactly once.");_=Cold(g,r);
        (g,r)=Start("spades");Need(V(g).HandCount==6&&E<LuYuShengBottomRevealedEvent>(g).Count()==3&&E<LuYuShengMovementIssuedEvent>(g).Single().Count==0,"Zero actual shown Hearts is a real reveal with zero skill draws, followed by normal Draw2.");_=Cold(g,r);
    }
    public static void FirstDamageAnySubsetZeroAndChainRecipientsCold()
    {
        var(g,r)=Start();var hp=V(g).Hp;Use(g,"self-two");Reach(g,()=>Current(g)is{Stage:LuYuShengStage.BottomChoice});
        Need(P(g)!.PlayerSeat==0&&g.CreateSnapshot(1).PendingDecision is null&&Current(g)!.BottomCards.Count==3,"The first native before-damage instance freezes the public bottom group and the owner's private arbitrary subset.");
        Pick(g,2);g=Cold(g,r);Finish(g);Play(g);Need(V(g).Hp==hp&&E<LuYuShengDamageAdjustedEvent>(g).Single()is{Hearts:2,Receipt.After:0,Applied:true}&&!E<DamageAppliedEvent>(g).Any(),"Two genuinely discarded Hearts reduce two native damage to zero.");
        Use(g,"self-two");Play(g);Need(V(g).Hp==hp-2&&E<LuYuShengStartedEvent>(g).Count(e=>e.Operation==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction)==1,"Even a fully prevented first attempt consumes this actual turn's locked opportunity.");
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&g.State.CurrentSeat==0&&P(g)?.Kind==DecisionKind.PlayCard);Use(g,"self-two");Reach(g,()=>Current(g)is{Stage:LuYuShengStage.BottomChoice});
        g=Cold(g,r);Finish(g);Play(g);Need(E<LuYuShengDamageAdjustedEvent>(g).Last()is{Hearts:0,Receipt.After:2}&&E<LuYuShengStartedEvent>(g).Count(e=>e.Operation==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction)==2,"A new actual turn resets the opportunity; choosing no card is legal and keeps the real two-damage amount.");_=Cold(g,r);
        (g,r)=Start("spades");Use(g,"self-two");Reach(g,()=>Current(g)is{Stage:LuYuShengStage.BottomChoice});Pick(g,1);g=Cold(g,r);Finish(g);Play(g);
        Need(E<LuYuShengDamageAdjustedEvent>(g).Single()is{Hearts:0,Receipt.After:2}&&g.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.lu-yu-sheng.cangxin.discard")==1,"Any non-Heart original bottom card may really be discarded without reducing the damage.");_=Cold(g,r);
        foreach(var scenario in new[]{"chain-formal","chain-legacy"})
        {
            (g,r)=Start(scenario);foreach(var seat in new[]{0,1,3}){Use(g,"chain",targets:[seat]);Play(g);}var hp0=V(g).Hp;var hp1=V(g,1).Hp;var hp3=V(g,3).Hp;
            Use(g,"fire-three",targets:[3]);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.BottomChoice});Need(V(g,3).Hp==hp3-3,"The initial actual elemental recipient is damaged before the chained owner opens Cangxin.");
            Pick(g,1);g=Cold(g,r);Finish(g);Play(g);
            Need(V(g).Hp==hp0-2&&V(g,1).Hp==hp1-3&&E<LuYuShengDamageRebasedEvent>(g).Single().Amount==3,"A later chained owner's local one-Heart reduction is restored before the next living recipient under both native armor paths: "+Diagnostic(g));_=Cold(g,r);
        }
        (g,r)=Start("chain-formal");foreach(var seat in new[]{0,1}){Use(g,"chain",targets:[seat]);Play(g);}var other=V(g,1).Hp;Use(g,"fire-three",targets:[0]);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.BottomChoice});Pick(g,1);Finish(g);Play(g);
        Need(V(g,1).Hp==other-2&&!E<LuYuShengDamageRebasedEvent>(g).Any(),"A reduction on the first actual elemental recipient naturally enters the native propagation base.");_=Cold(g,r);
    }
    public static void RunweiOwnForeignPhaseLionAndOxCold()
    {
        var(g,r)=Start();Use(g,"lose");Play(g);End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});g=Cold(g,r);Branch(g,"draw");
        Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));var first=E<LuYuShengRunweiPhaseIssuedEvent>(g).Single();
        Need(first.Delta==-1&&first.Phase.ActorSeat==0&&V(g).HandCount==7&&g.CardMovements.Count(m=>m.Reason==CardMoveReasons.HandLimitDiscard&&m.From==CardLocation.Hand(0))==3,"Own wounded Discard really draws one and applies only this phase's minus-one hand limit.");
        Play(g);Use(g,"draw-seven");Play(g);End(g);Reach(g,()=>Optional(P(g),Run));Skip(g);Reach(g,()=>E<TurnEndedEvent>(g).Count(e=>e.ActorSeat==0)==2);
        Need(V(g).HandCount==8&&E<LuYuShengRunweiPhaseIssuedEvent>(g).Count()==1,"The next actual Discard has the normal eight-card limit after declining; the old phase adjustment is expired.");_=Cold(g,r);
        (g,r)=Start();Use(g,"hurt-peer",targets:[1]);Play(g);End(g);Reach(g,()=>Optional(P(g),Run)&&g.State.CurrentSeat==1);Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});
        Need(Current(g)!.Payer==1&&g.CreateSnapshot(1).PendingDecision is null,"The skill owner chooses privately for the actual wounded foreign Discard owner.");g=Cold(g,r);Branch(g,"draw");Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==1));
        Need(E<LuYuShengRunweiPhaseIssuedEvent>(g).Single()is{Delta:-1,Phase.ActorSeat:1}&&V(g,1).HandCount==6,"The actual foreign participant draws once and discards against its own reduced limit.");_=Cold(g,r);
        foreach(var scenario in new[]{"lion","ox"})
        {
            (g,r)=Start(scenario);Use(g,"equip",targets:[0]);Play(g);var kind=scenario=="lion"?CardKind.SilverLion:CardKind.WoodenOx;var gear=V(g).Equipment.Single(c=>c.Kind==kind).Id;int? grain=null;
            if(scenario=="ox"){var storage=g.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.UseEquipmentEffect&&a.EquipmentKind==CardKind.WoodenOx);grain=storage.SelectableCardIds.First();Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);}
            Use(g,"lose");Play(g);var hp=V(g).Hp;End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});Branch(g,"discard");Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiDiscardChoice});
            Need(P(g)!.PlayerSeat==0&&g.CreateSnapshot(1).PendingDecision is null,"The actual payer privately chooses one true HE entity.");g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([gear]));
            if(scenario=="lion"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g).Hp==hp+1,"The equipped Lion's real removal heals its payer before the paid phase effect returns.");g=Cold(g,r);Continue(g);}
            Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
            Need(E<LuYuShengRunweiPhaseIssuedEvent>(g).Single().Delta==1&&g.CardMovements.Any(m=>m.CardId==gear&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.lu-yu-sheng.runwei.discard"),"Discarding the selected real equipment grants plus one once, retained even after native recovery makes its participant full HP.");
            if(grain is{}id)Need(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)==1&&E<LuYuShengMovementIssuedEvent>(g).Last().Count==2,"An Ox payment preserves its real grain spill as an attached native move, not a second chosen discard.");_=Cold(g,r);
        }
        (g,r)=Start("empty");while(V(g).HandCount>0){Use(g,"discard",cards:[V(g).Hand[0].Id]);Play(g);}Use(g,"lose");Play(g);End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});g=Cold(g,r);Branch(g,"discard");Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
        Need(E<LuYuShengRunweiPhaseIssuedEvent>(g).Single().Delta==1&&!E<LuYuShengMovementIssuedEvent>(g).Any(e=>e.Stage==LuYuShengStage.RunweiDiscardChildren),"Empty HE does not cancel the chosen plus-one phase consequence or invent a zero-card discard movement.");_=Cold(g,r);
    }
    public static void PaidSourceLossDeathAndSmallDeckReshuffleCold()
    {
        foreach(var scenario in new[]{"paid-loss","paid-death","draw-loss"})
        {
            var(g,r)=Start(scenario);Use(g,"lose");Play(g);Need(V(g).Skills!.Any(s=>s.Id==Run),"The exact HP-granted source is still active when the wounded actual Discard starts.");End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});Branch(g,scenario=="draw-loss"?"draw":"discard");
            if(scenario!="draw-loss"){Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiDiscardChoice});Answer(g,c=>c.Cards.Count==1);}
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(E<LuYuShengRunweiPhaseIssuedEvent>(g).Count()==1,"The real native effect and phase consequence are issued once before its pausable child.");g=Cold(g,r);Continue(g);
            if(scenario=="paid-death")Reach(g,()=>g.State.Status==EngineStatus.Completed);else Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
            Need((scenario=="paid-death"?!V(g).IsAlive:V(g).Skills!.All(s=>s.Id!=Run))&&E<LuYuShengRunweiPhaseIssuedEvent>(g).Count()==1&&E<LuYuShengMovementIssuedEvent>(g).Count(e=>E<LuYuShengStartedEvent>(g).Any(start=>start.FrameId==e.FrameId&&start.Operation==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase))==1,"A genuinely paid native movement/HP/death child preserves the issued effect after source loss or death without repeating its movement: "+Diagnostic(g));_=Cold(g,r);
        }
        {
            var(g,r)=Start("reshuffle");Use(g,"draw-seven");Play(g);Need(!g.CreateCardZoneDiagnostics().Any(z=>z.Location==CardLocation.DrawPile),"The fixed small deck is genuinely exhausted by real commands.");Use(g,"discard",cards:[V(g).Hand[0].Id]);Play(g);Use(g,"lose");Play(g);End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});g=Cold(g,r);Branch(g,"draw");Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
            var draw=E<LuYuShengMovementIssuedEvent>(g).Last(e=>e.Stage==LuYuShengStage.DrawChildren);var shuffle=E<LuYuShengReshuffleCapturedEvent>(g).Single();
            Need(draw.Count==1&&shuffle.Count==1&&shuffle.Before>=draw.Before&&shuffle.After<=draw.After&&E<LuYuShengRunweiPhaseIssuedEvent>(g).Single().Delta==-1,"The native empty-deck draw reshuffles its exact one discarded entity and counts only the one actual card drawn.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("zero-draw");Use(g,"draw-seven");Play(g);Need(!g.CreateCardZoneDiagnostics().Any(z=>z.Location==CardLocation.DrawPile||z.Location==CardLocation.DiscardPile),"The zero-draw fixture has no physical entity available to draw or reshuffle.");Use(g,"lose");Play(g);End(g);Reach(g,()=>Optional(P(g),Run));Activate(g);Reach(g,()=>Current(g)is{Stage:LuYuShengStage.RunweiChoice});Branch(g,"draw");Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
            Need(E<LuYuShengMovementIssuedEvent>(g).Last().Count==0&&E<LuYuShengRunweiPhaseIssuedEvent>(g).Single().Delta==-1,"An actually exhausted draw still applies the already-chosen minus-one phase consequence.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static LuYuShengReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.LuYuSheng is not null)?.LuYuSheng;
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,x.Error?.Message??"The real command was rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human can Answer; AI advances natively.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Branch(GameEngine g,string branch)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option")==branch);
    private static void Pick(GameEngine g,int count){for(var i=0;i<count;i++)Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="pick");}
    private static void Finish(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="finish");
    private static void Use(GameEngine g,string id,int[]?cards=null,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<700;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Lu Yu Sheng ended before expected boundary: "+Diagnostic(g));var p=P(g);
            if(p is{PlayerSeat:0}&&Optional(p,Run))Skip(g);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0}&&p.SkillPrompt?.SkillId==Health)Continue(g);
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Lu Yu Sheng prompt: "+Diagnostic(g));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Lu Yu Sheng did not reach its true native boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Status,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves public bottom entities, private payer choices, exact native tails, actual-phase grants and damage-instance reductions.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal",bool early=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(early)Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);else Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-lu-yu-sheng",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:lu-yu-sheng");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"hurt-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"draw-seven","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":7}]},
              {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
              {"id":"fire-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3,"nature":"fire"}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{Run}}","minimumHp":8}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-child","window":"{{(scenario is"draw-watch" or"draw-loss"?"cardsGained":"cardsMoved")}}","subject":"owner",{{(scenario is"draw-watch" or"draw-loss"?"\"destinationZones\":[\"hand\"]":"\"sourceZones\":[\"hand\"]")}},"movementOccurrence":"perBatch","movementReasons":["{{(scenario is"draw-watch" or"draw-loss"?"skill-program.lu-yu-sheng.draw":"skill-program.lu-yu-sheng.runwei.discard")}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}{{(scenario=="draw-watch"?"":",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+(scenario=="paid-death"?"20":"1")+"}")}}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Health,Conditional,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="陆郁生原生命令边界夹具",optionLabels=id==Health||id==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:lys-pick-owner","本人公开评分","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var skills=new List<string>{Driver};if(scenario!="draw-loss")skills.Add(Cang);skills.Add(scenario is"paid-loss" or"draw-loss"?Conditional:Run);if(scenario is"draw-watch" or"paid-loss" or"paid-death" or"draw-loss")skills.Add(Watch);if(scenario=="lion")skills.Add(Health);
            b.AddGeneral(new(Owner,"陆郁生真实本人","ol-lu-yu-sheng","fixture:lys-pick-owner","wu",8,skills,GeneralGender.Female));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:lys-peer-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:lys-pick-"+index;b.AddSkill(new(pick,"其他公开评分","固定顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他真实角色","supporter",pick,"wu",8,[Quiet]));}
            b.AddCard(new("fixture:lys-lion","白银狮子","装备牌","真实原生移除回复",CardKind.SilverLion));b.AddCard(new("fixture:lys-ox","木牛流马","装备牌","真实原生存粮与移除",CardKind.WoodenOx));
            b.AddDeck(new("fixture:lys-deck","固定小实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario is"reshuffle" or"zero-draw"?28:96).Select(i=>new ContentDeckPhysicalCard((scenario is"lion" or"ox")&&i%5==0?scenario=="ox"?"fixture:lys-ox":"fixture:lys-lion":"standard:slash",scenario=="spades"?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new(scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,"陆郁生原生规则",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:lys-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
