using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WuTuGuChecks
{
    private const string Owner="fixture:wt-owner",Driver="fixture:wt-driver",Quiet="fixture:wt-quiet",Watch="fixture:wt-watch",Source="fixture:wt-source",RecoverWatch="fixture:wt-committed-recover",LossWatch="fixture:wt-committed-loss",LateWatch="fixture:wt-finalized-loss",Mode="identity:classic-wu-tu-gu";
    private const string Hanyong="ol:hanyong",Ranshang="ol:ranshang";
    public static void ActualFirePointsPreventionChainAndPersistentMarksCold()
    {
        var(g,r)=Start("fire");Use(g,"fire-two");Play(g);
        Need(Ran(g)==2&&E<WuTuGuFirePointAddedEvent>(g).Select(e=>e.Point).SequenceEqual([0,1])&&E<WuTuGuFirePointAddedEvent>(g).All(e=>e.Amount==2),"Two truly applied fire points add two public long-lived marks, rather than one per damage event.");g=Cold(g,r);
        Use(g,"normal");Play(g);Use(g,"thunder");Play(g);Need(Ran(g)==2,"Normal and thunder damage cannot add fire marks.");
        Use(g,"chain-self");Play(g);Use(g,"chain-target",[1]);Play(g);Use(g,"fire-target",[1]);Play(g);
        Need(Ran(g)==3&&E<WuTuGuFirePointAddedEvent>(g).Length==3&&E<DamageAppliedEvent>(g).Count(e=>e.TargetSeat==0&&e.Amount==1)>=3,"A real elemental propagation onto the chained owner adds its one actual fire point.");
        var hp=V(g).Hp;var turn=g.State.TurnNumber;End(g);Reach(g,()=>E<WuTuGuRanshangCompletedEvent>(g).Length==1);
        Need(V(g).Hp==hp-3&&Ran(g)==3&&E<WuTuGuRanshangPaidEvent>(g)is[{Amount:3}]&&E<ProgramSkillHpLostEvent>(g).Count(e=>e.SkillId==Ranshang&&e.Amount==3)==1,"Ending pays one complete three-point native HP loss and keeps all marks.");g=Cold(g,r);
        NextPlay(g,turn);End(g);Reach(g,()=>E<WuTuGuRanshangCompletedEvent>(g).Length==2);
        Need(Ran(g)==3&&E<WuTuGuRanshangPaidEvent>(g).Select(e=>e.Amount).SequenceEqual([3,3]),"The next genuine ending pays the same persistent marks again without clearing them.");_=Cold(g,r);
        (g,r)=Start("prevent");Use(g,"fire-two");Play(g);
        Need(Ran(g)==0&&E<WuTuGuFirePointAddedEvent>(g).Length==0&&E<DamageAppliedEvent>(g).Length==0,"A real BeforeDamageApplied fire prevention creates no applied points and therefore no marks.");_=Cold(g,r);
    }
    public static void HanyongActualRoundAndDeclarationFreezeCold()
    {
        var(g,r)=Start("hanyong");Use(g,"lower-fifteen");Play(g);var damage=E<DamageAppliedEvent>(g).Length;NativeGroup(g);Play(g);
        Need(E<WuTuGuHanyongArmedEvent>(g).Length==0&&E<WuTuGuHanyongQualifiedEvent>(g).Last()is{Round:1,HpAtUse:1}&&E<DamageAppliedEvent>(g).Skip(damage).Select(e=>e.Amount).SequenceEqual([1,1,1]),"HP equal to actual round one cannot offer Hanyong; the physical group card still resolves normally.");
        var turn=g.State.TurnNumber;End(g);NextPlay(g,turn);Use(g,"grant-recover");Play(g);NativeGroup(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==RecoverWatch);g=Cold(g,r);Continue(g);Reach(g,()=>Offer(g,Hanyong));
        Need(V(g).Hp==2&&E<WuTuGuHanyongQualifiedEvent>(g).Last()is{Round:2,HpAtUse:1},"Qualification freezes HP at declaration before an earlier committed-use child recovers the owner to the current round.");
        var p=P(g)!;var state=State(g);Need(g.CreateSnapshot(1).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"Only the actual optional skill owner may answer the native use window.");
        damage=E<DamageAppliedEvent>(g).Length;g=Cold(g,r);Activate(g);Play(g);
        Need(E<WuTuGuHanyongArmedEvent>(g)is[{Grant.Round:2,Grant.HpAtUse:1}]&&E<DamageAppliedEvent>(g).Skip(damage).Select(e=>e.Amount).SequenceEqual([2,2,2]),"One accepted native-use grant adds exactly one to every group recipient after cold recovery.");
        damage=E<DamageAppliedEvent>(g).Length;NativeGroup(g);Play(g);Need(E<WuTuGuHanyongArmedEvent>(g).Length==1&&E<DamageAppliedEvent>(g).Skip(damage).All(e=>e.Amount==1),"A later group use at HP equal to round cannot inherit a prior use's enhancement.");
        Use(g,"lose-one");Play(g);NativeGroup(g);Reach(g,()=>Offer(g,Hanyong));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Play(g);Need(E<WuTuGuHanyongArmedEvent>(g).Length==1,"A genuine optional decline creates no grant.");_=Cold(g,r);
        (g,r)=Start("hanyong");Use(g,"lower-fourteen");Play(g);turn=g.State.TurnNumber;End(g);NextPlay(g,turn);Use(g,"grant-loss");Play(g);damage=E<DamageAppliedEvent>(g).Length;NativeGroup(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==LossWatch);g=Cold(g,r);Continue(g);Play(g);
        Need(V(g).Hp==1&&E<WuTuGuHanyongQualifiedEvent>(g).Last()is{Round:2,HpAtUse:2}&&E<WuTuGuHanyongArmedEvent>(g).Length==0&&E<DamageAppliedEvent>(g).Skip(damage).All(e=>e.Amount==1),"A post-declaration HP loss cannot retroactively qualify an initially ineligible native use.");_=Cold(g,r);
    }
    public static void NativeGroupHuoshouZeroUseAndPaidSourceLossCold()
    {
        var(g,r)=Start("huoshou");Use(g,"lower-fifteen");Play(g);var turn=g.State.TurnNumber;End(g);NextPlay(g,turn);var meng=Enumerable.Range(1,3).Single(s=>Has(g,s,"boundary:huoshou-current"));var damage=E<DamageAppliedEvent>(g).Length;
        NativeGroup(g,CardKind.BarbarianAssault);Reach(g,()=>Offer(g,Hanyong));g=Cold(g,r);Activate(g);Play(g);
        var actual=E<DamageAppliedEvent>(g).Skip(damage).ToArray();Need(actual.Length==2&&actual.All(e=>e.Amount==2&&e.SourceSeat==meng)&&actual.All(e=>e.TargetSeat!=meng)&&E<WuTuGuHanyongArmedEvent>(g).Length==1,"Huoshou's real immunity and damage-source attribution retain the original card's Hanyong plus one.");_=Cold(g,r);
        (g,r)=Start("paid-loss");Use(g,"lower-fourteen");Play(g);turn=g.State.TurnNumber;End(g);NextPlay(g,turn);turn=g.State.TurnNumber;End(g);NextPlay(g,turn);Use(g,"grant-late");Play(g);NativeGroup(g);Reach(g,()=>Offer(g,Hanyong));Activate(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==LateWatch);
        Need(E<WuTuGuHanyongArmedEvent>(g)is[{Grant.Round:3,Grant.HpAtUse:2}],"The current round is native round three before the real paid source-loss child.");damage=E<DamageAppliedEvent>(g).Length;g=Cold(g,r);Continue(g);Play(g);
        Need(!Has(g,0,Hanyong)&&E<DamageAppliedEvent>(g).Skip(damage).Select(e=>e.Amount).SequenceEqual([2,2,2])&&E<WuTuGuHanyongArmedEvent>(g).Length==1,"The exact use-owned enhancement completes once after native finalized-target children remove its conditional skill source.");_=Cold(g,r);
        (g,r)=Start("zero");Use(g,"lower-fourteen");Play(g);turn=g.State.TurnNumber;End(g);NextPlay(g,turn);Use(g,"normal");Reach(g,()=>Offer(g,"ol:xianying"));Activate(g);Reach(g,()=>Qin(g)?.Stage==QinLangXianyingStage.DiscardChoice);
        var costs=Qin(g)!.Cards.Where(c=>c.Kind==CardKind.ArrowBarrage).Take(2).ToArray();Need(costs.Length==2,"The fixed real hand contains two identical physical group cards.");foreach(var c in costs)Answer(g,pick=>pick.Cards.SequenceEqual([c.CardId]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");Play(g);End(g);Reach(g,()=>Qin(g)?.Stage==QinLangXianyingStage.EndChoice);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use");Reach(g,()=>Offer(g,Hanyong));
        var use=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.WuTuGuHanyong is null&&u.QinLangXianying is not null);Need(use.CardId==0&&use.PhysicalCardIds is{Count:0},"The real paid Xianying debt issued a native zero-entity group use, rather than a fake card or direct damage.");damage=E<DamageAppliedEvent>(g).Length;g=Cold(g,r);Activate(g);Reach(g,()=>E<QinLangXianyingUseReturnedEvent>(g).Length==1);
        Need(E<WuTuGuHanyongArmedEvent>(g)is[{Grant.HpAtUse:1,Grant.Round:2}]&&E<DamageAppliedEvent>(g).Skip(damage).Select(e=>e.Amount).SequenceEqual([2,2,2]),"A genuine zero-entity group use shares the same frozen qualification and native per-target enhancement.");_=Cold(g,r);
    }
    public static void EndingSingleUnclampedLossDyingChildrenAndSourceLossCold()
    {
        var(g,r)=Start("ending-loss");Use(g,"fire-two");Play(g);Need(V(g).Hp==14&&Has(g,0,Ranshang),"The actual two-point fire leaves the conditional source alive before ending.");End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
        var owner=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.WuTuGuRanshang is not null);Need(owner.WuTuGuRanshang is{Stage:WuTuGuRanshangStage.HpChildren,Amount:2}&&!Has(g,0,Ranshang)&&E<WuTuGuRanshangPaidEvent>(g)is[{Amount:2,RemainingHp:12}],"The complete native loss is receipted before the actual HP child and before source-loss cancellation checks.");g=Cold(g,r);Continue(g);Reach(g,()=>E<WuTuGuRanshangCompletedEvent>(g).Any(e=>e.ProgramFrameId==owner.Id));
        Need(V(g).Hp==11&&Ran(g)==2&&E<WuTuGuRanshangPaidEvent>(g).Length==1&&E<ProgramSkillHpLostEvent>(g).Count(e=>e.FrameId==owner.Id)==1&&E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"A nested real damage child returns once to the already-paid ending despite loss of Ranshang; historical marks remain.");_=Cold(g,r);
        (g,r)=Start("rescue");Use(g,"fire-three");Play(g);Use(g,"lower-twelve");Play(g);Need(V(g).Hp==1&&Ran(g)==3,"Three real fire points exist before a one-HP ending.");End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying});var dying=E<PlayerDyingEvent>(g).Last();
        Need(V(g).Hp==-2&&E<WuTuGuRanshangPaidEvent>(g)is[{Amount:3,RemainingHp:-2}]&&g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.WuTuGuRanshang is not null).WuTuGuRanshang!.Stage==WuTuGuRanshangStage.HpChildren,"One unclamped loss reaches minus two and starts one native dying process with its paid parent already valid.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="peach");Reach(g,()=>E<DyingResolvedEvent>(g).Any(e=>e.ResolutionId==dying.ResolutionId));
        Need(E<DyingResolvedEvent>(g).Single(e=>e.ResolutionId==dying.ResolutionId).Survived&&E<DyingResponseEvent>(g).Count(e=>e.ResolutionId==dying.ResolutionId&&e.UsedPeach)==3&&E<PlayerDyingEvent>(g).Length==1&&E<WuTuGuRanshangPaidEvent>(g).Length==1,"Three separate real Peach uses rescue the one minus-two loss, rather than three one-point loss/rescue windows.");Reach(g,()=>E<WuTuGuRanshangCompletedEvent>(g).Length==1);_=Cold(g,r);
        (g,r)=Start("death");Use(g,"fire-three");Play(g);Use(g,"lower-eleven");Play(g);End(g);Reach(g,()=>!V(g).IsAlive);
        Need(E<WuTuGuRanshangPaidEvent>(g)is[{Amount:3,RemainingHp:-2}]&&E<PlayerDyingEvent>(g).Length==1&&E<DyingResolvedEvent>(g)is[{VictimSeat:0,Survived:false}]&&E<WuTuGuRanshangCompletedEvent>(g).Length==1,"Unrescued non-Lord death completes the original one-payment ending continuation once.");_=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Has(GameEngine g,int seat,string skill)=>V(g,seat).Skills!.Any(s=>s.ContentId==skill);
    private static int Ran(GameEngine g)=>V(g).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.RanshangRan)?.Count??0;
    private static QinLangXianyingReceipt? Qin(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.QinLangXianying is not null)?.QinLangXianying;
    private static bool Offer(GameEngine g,string skill)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Wu Tu Gu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Actual AI answers advance through the native brain.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing exact Wu Tu Gu choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void NativeGroup(GameEngine g,CardKind? kind=null){var a=g.GetHumanLegalActions().First(a=>a.CardId is{}id&&V(g).Hand.Any(c=>c.Id==id&&c.Kind is(CardKind.BarbarianAssault or CardKind.ArrowBarrage)&&(kind is null||c.Kind==kind)));Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void NextPlay(GameEngine g,int after)=>Reach(g,()=>g.State.TurnNumber>after&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};hp={V(g).Hp};ran={Ran(g)};qualified={JsonSerializer.Serialize(E<WuTuGuHanyongQualifiedEvent>(g))};paid={JsonSerializer.Serialize(E<WuTuGuRanshangPaidEvent>(g))}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var step=0;step<280;step++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual Wu Tu Gu boundary: "+Diagnostic(g));var p=P(g);
            if(p is null||!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(Qin(g)?.Stage==QinLangXianyingStage.EndChoice)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else throw new InvalidOperationException("Unexpected Wu Tu Gu prompt: "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded Wu Tu Gu boundary: "+Diagnostic(g));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s)))+JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CardMovements)+g.Events.Count;
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var next=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(next),"Cold replay preserves exact native-use grants, public fire points and once-paid ending children.");return next;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wu-tu-gu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:wu-tu-gu");if(scene=="huoshou")CharacterFixtureContent.Register(b,"boundary:meng-huo");if(scene=="zero")CharacterFixtureContent.Register(b,"ol:qin-lang");
            var definitions=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
            {"id":"fire-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2,"nature":"fire"}]},
            {"id":"fire-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":3,"nature":"fire"}]},
            {"id":"normal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
            {"id":"thunder","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"thunder"}]},
            {"id":"fire-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
            {"id":"chain-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
            {"id":"chain-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var driver=JsonNode.Parse(definitions[0])!;foreach(var(id,amount)in new[]{("lower-fifteen",15),("lower-fourteen",14),("lower-twelve",12),("lower-eleven",11),("lose-one",1)})driver["activations"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":{{amount}}}]}"""));
            void Add(string id,string body)=>definitions.Add("{\"id\":\""+id+"\",\"revision\":1,"+body+"}");
            foreach(var(id,skill)in new[]{("grant-recover",RecoverWatch),("grant-loss",LossWatch),("grant-late",LateWatch)})driver["activations"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{skill}}"]}]}"""));definitions[0]=driver.ToJsonString();
            foreach(var(skill,window,tail)in new[]{(RecoverWatch,"cardUseCommitted","recover"),(LossWatch,"cardUseCommitted","loseHp"),(LateWatch,"cardUseTargetsFinalized","loseHp")})Add(skill,"\"triggers\":[{\"id\":\"earlier-native-child\",\"window\":\""+window+"\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"barbarianAssault\",\"arrowBarrage\"],\"priority\":100,\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"continue\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\""+tail+"\",\"target\":\"owner\",\"amount\":1}] }]");
            var skills=new List<string>{Driver};
            if(scene is"hanyong"or"huoshou"or"zero")skills.Add(Hanyong);else if(scene=="paid-loss"){Add(Source,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:hanyong\",\"minimumHp\":2}]");skills.Add(Source);}else if(scene=="ending-loss"){Add(Source,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:ranshang\",\"minimumHp\":14}]");skills.Add(Source);}else skills.Add(Ranshang);
            if(scene=="zero")skills.Add("ol:xianying");
            if(scene=="prevent"){Add("fixture:wt-prevent","\"triggers\":[{\"id\":\"native-fire-prevention\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"condition\":{\"kind\":\"damageNatureIs\",\"damageNature\":\"fire\"},\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]");skills.Add("fixture:wt-prevent");}
            if(scene=="ending-loss"){Add(Watch,"\"triggers\":[{\"id\":\"actual-ending-hp-child\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"optional\":false,\"hpChangeOccurrence\":\"perEvent\",\"usageScope\":\"game\",\"usageLimit\":1,\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"currentHp\"},\"operator\":\"equal\",\"right\":{\"kind\":\"integerConstant\",\"value\":12}},\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"continue\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]");skills.Add(Watch);}
            var labels=definitions.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生兀突骨夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',definitions)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,labels[id].name,"原生行为夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:wt-owner-pick","本人","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});b.AddGeneral(new(Owner,"真实兀突骨","ol-wu-tu-gu","fixture:wt-owner-pick","qun",15,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>"fixture:wt-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i))){var pick="fixture:wt-pick-"+i;b.AddSkill(new(pick,"其他角色","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"qun",20,scene=="huoshou"&&i==0?[Quiet,"boundary:huoshou-current"]:[Quiet]));}
            foreach(var(id,kind)in new[]{("barbarian",CardKind.BarbarianAssault),("arrows",CardKind.ArrowBarrage)})b.AddCard(new("fixture:wt-"+id,id,"真实锦囊","原生实体",kind));var cards=scene=="rescue"?new[]{"standard:peach"}:scene is"hanyong"or"huoshou"or"paid-loss"or"zero"?new[]{"fixture:wt-barbarian","fixture:wt-arrows"}:new[]{"standard:dodge"};
            b.AddDeck(new("fixture:wt-deck","小固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard(cards[i%cards.Length],new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i/cards.Length%4],i%13+1)).ToArray()});
            var roles=scene=="rescue"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=3}:scene=="death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3};b.AddMode(new(Mode,"原生兀突骨",4,4,roles,"fixture:wt-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
