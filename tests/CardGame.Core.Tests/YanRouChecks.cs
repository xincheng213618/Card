using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YanRouChecks
{
    private const string Chou="ol:choutao",Xiang="ol:xiangshu",Owner="fixture:yr-owner",Driver="fixture:yr-driver",Quiet="fixture:yr-quiet",Peer="fixture:yr-peer",Health="fixture:yr-health",Watch="fixture:yr-watch",Conditional="fixture:yr-conditional",Prep="fixture:yr-prep",Mode="identity:classic-yan-rou-fixture";
    public static void PaidOwnSlashRefundAndUnpaidQuotaRemainExactCold()
    {
        var(g,r)=Start();var hp=V(g,1).Hp;
        for(var i=0;i<2;i++)
        {
            Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Optional(P(g),Chou));g=Cold(g,r);Activate(g);
            Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});var paid=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([paid]));Play(g);
            Need(E<YanRouChoutaoAppliedEvent>(g).Count()==i+1&&E<YanRouChoutaoAppliedEvent>(g).Last().Refunded&&g.CardMovements.Count(m=>m.CardId==paid&&m.Reason.Value=="skill-program.yan-rou.choutao.discard")==1,
                "Each actual native Slash pays one owned entity, becomes uncancelable and refunds only its own recorded quota once.");
        }
        Need(V(g,1).Hp==hp-2&&E<CardUseDebitRecordedEvent>(g).Count()==2&&E<CardUseDebitRefundedEvent>(g).Count()==2,"Paying the first real use permits a second true use with separate physical cost and debit identities.");_=Cold(g,r);

        (g,r)=Start();Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Optional(P(g),Chou));Skip(g);Play(g);
        Need(!E<YanRouStartedEvent>(g).Any()&&!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"Declining the optional post-declaration skill spends ordinary quota and cannot borrow a future unpaid Choutao.");
        var before=State(g);var result=g.Submit(new PlayCardCommand(0,V(g).Hand.First(c=>c.Kind==CardKind.Slash).Id,[1],g.Revision,P(g)!.PromptId));
        Need(!result.Accepted&&before==State(g)&&!E<CardUseDebitRefundedEvent>(g).Any(),"A second quota-exhausted real command is rejected without mutation or an invented future cost.");_=Cold(g,r);
    }
    public static void TargetedOpaqueDiscardRealDodgeLionAndOxChildrenCold()
    {
        var(g,r)=Start("retaliate");Use(g,"poke",targets:[1]);Reach(g,()=>Optional(P(g),Chou));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});
        Need(Current(g)!.Payer==1&&P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand").All(c=>c.Cards.Count==0)&&g.CreateSnapshot(1).PendingDecision is null,
            "The targeted direction makes the real defender privately choose an opaque slot in the actual Slash user's hand.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
        var hp=V(g).Hp;var dodge=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([dodge]));Play(g);
        var trueDodge=E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0&&e.CardId==dodge&&e.EffectiveCardKind==CardKind.Dodge)&&
            E<CardActionAcceptedEvent>(g).Any(e=>e.Action is{Type:CardActionType.Response,ActorSeat:0,ProviderSeat:0,EffectiveKind:CardKind.Dodge}&&e.Action.PhysicalCards.Any(c=>c.CardId==dodge))&&
            g.CardMovements.Any(m=>m.CardId==dodge&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)&&g.CardMovements.Any(m=>m.CardId==dodge&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile);
        Need(V(g).Hp==hp-1&&trueDodge&&E<YanRouChoutaoAppliedEvent>(g).Single()is{Payer:1,Refunded:false},
            "Uncancelable retains a genuine paid Dodge response yet applies real Slash damage; a defender cannot refund the foreign actor's use. "+JsonSerializer.Serialize(new{BeforeHp=hp,ActualHp=V(g).Hp,trueDodge,Dodge=dodge,Applied=E<YanRouChoutaoAppliedEvent>(g).ToArray(),Responses=E<CardRespondedEvent>(g).ToArray(),Damage=E<DamageAppliedEvent>(g).ToArray()}));_=Cold(g,r);

        (g,r)=Start("lion");Use(g,"equip",targets:[1]);Play(g);var lion=V(g,1).Equipment.Single(c=>c.Kind==CardKind.SilverLion).Id;Use(g,"poke",targets:[1]);
        Reach(g,()=>Optional(P(g),Chou));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});hp=V(g,1).Hp;
        g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([lion]));Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);g=Cold(g,r);Play(g);
        Need(V(g,1).Hp==hp+1&&E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==1)&&g.CardMovements.Any(m=>m.CardId==lion&&m.From==CardLocation.Equipment(1)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.yan-rou.choutao.discard")&&E<YanRouChoutaoAppliedEvent>(g).Count()==1,
            "Discarding the actual foreign equipped Lion drains its native recovery/HP window before applying the paid enhancement once.");_=Cold(g,r);

        (g,r)=Start("ox");Use(g,"equip",targets:[0]);Play(g);var ox=V(g).Equipment.Single(c=>c.Kind==CardKind.WoodenOx).Id;
        var storage=g.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.UseEquipmentEffect&&a.EquipmentKind==CardKind.WoodenOx);var grain=storage.SelectableCardIds.First();
        Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);Need(V(g).WoodenOxGrain!.Single().Id==grain,"A real equipment command stores one original hand entity.");
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Optional(P(g),Chou));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([ox]));Play(g);
        Need(g.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)==1&&E<YanRouMovementIssuedEvent>(g).Last(e=>e.Stage==YanRouStage.DiscardChildren).Count==2&&E<YanRouChoutaoAppliedEvent>(g).Single().CardId==ox,
            "One real chosen equipped Ox preserves its native attached grain spill without charging a second chosen discard or refund.");_=Cold(g,r);
    }
    public static void ActualTurnDamageCappedAidSelfZeroAndPermanentLimitedCold()
    {
        var(g,r)=Start("prep");Need(E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==0),"The genuine preparation child deals source-owned damage before Play.");
        Use(g,"three",targets:[1]);Play(g);Use(g,"three",targets:[1]);Play(g);End(g);Reach(g,()=>Optional(P(g),Xiang));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.TargetChoice});
        Need(Current(g)!.Amount==5&&E<DamageAppliedEvent>(g).Where(e=>e.SourceSeat==0).Sum(e=>e.Amount)==7,"X freezes all actual source-owned damage in this turn, including preparation and separate occurrences, capped at five.");
        var hand=V(g,1).HandCount;var hp=V(g,1).Hp;g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>E<YanRouCompletedEvent>(g).Any()&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.YanRou is not null));
        Need(V(g,1).Hp==Math.Min(V(g,1).MaxHp,hp+5)&&V(g,1).HandCount==hand+5&&E<YanRouXiangshuSpentEvent>(g).Single()is{Amount:5,Target:1},"The selected wounded foreign participant really recovers then draws five through native children.");g=Cold(g,r);Play(g);
        Use(g,"lose-aid");Play(g);Need(V(g).Skills!.All(s=>s.Id!=Xiang),"The source is actually lost after its permanent limited payment.");Use(g,"grant-aid");Play(g);Need(V(g).Skills!.Any(s=>s.Id==Xiang),"A fresh source instance is truly granted.");
        var regrantedTurn=g.State.TurnNumber;End(g);Reach(g,()=>Optional(P(g),Xiang)||E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==regrantedTurn&&e.ActorSeat==0));
        Need(!Optional(P(g),Xiang)&&E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==regrantedTurn&&e.ActorSeat==0)&&E<YanRouXiangshuSpentEvent>(g).Count()==1&&E<YanRouStartedEvent>(g).Count(e=>e.Operation==SkillProgramEffectOp.RecoverAndDrawByTurnDamage)==1,
            "Losing and regranting the skill never restores an already-paid game-limited aid or even offers it at the next actual Ending.");_=Cold(g,r);

        (g,r)=Start();Use(g,"lose");Play(g);End(g);Reach(g,()=>Optional(P(g),Xiang));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.TargetChoice});
        Need(Current(g)!.Amount==0&&!E<DamageAppliedEvent>(g).Any(),"Current OL omits a positive-damage prerequisite: true HP loss leaves a wounded legal target with X zero.");hp=V(g).Hp;hand=V(g).HandCount;g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([0]));
        Reach(g,()=>E<YanRouCompletedEvent>(g).Any());Need(V(g).Hp==hp&&V(g).HandCount==hand&&E<YanRouXiangshuSpentEvent>(g).Single()is{Amount:0,Target:0}&&!E<RecoveryAppliedEvent>(g).Any()&&!E<YanRouMovementIssuedEvent>(g).Any(e=>e.Stage==YanRouStage.DrawChildren),
            "A zero-effect self choice still spends the limited skill once without inventing a recovery or native zero-card Draw.");_=Cold(g,r);
    }
    public static void PaidCostSourceLossDeathAndNativeDrawReshuffleCold()
    {
        var(g,r)=Start("cost-loss");Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Optional(P(g),Chou));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});
        Answer(g,c=>c.Cards.Count==1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);Play(g);
        Need(V(g).Skills!.All(s=>s.Id!=Chou)&&E<YanRouChoutaoAppliedEvent>(g).Single().Refunded&&E<YanRouMovementIssuedEvent>(g).Count(e=>e.Stage==YanRouStage.DiscardChildren)==1,"A genuine paid discard child removes the source grant, yet the original use is enhanced/refunded once after its exact native tail.");_=Cold(g,r);

        (g,r)=Start("cost-death");Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Optional(P(g),Chou));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.DiscardChoice});Answer(g,c=>c.Cards.Count==1);
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);Reach(g,()=>g.State.Status==EngineStatus.Completed);
        Need(!V(g).IsAlive&&E<YanRouMovementIssuedEvent>(g).Count(e=>e.Stage==YanRouStage.DiscardChildren)==1&&E<YanRouChoutaoAppliedEvent>(g).Count()==1,"A real paid source death drains native Dying/death without re-paying or abandoning the already-issued use modification.");_=Cold(g,r);

        (g,r)=Start("aid-loss");Use(g,"self");Play(g);End(g);Reach(g,()=>Optional(P(g),Xiang));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.TargetChoice});Answer(g,c=>c.Targets.SequenceEqual([0]));
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);Reach(g,()=>E<YanRouCompletedEvent>(g).Any());
        Need(V(g).Skills!.All(s=>s.Id!=Xiang)&&E<YanRouXiangshuSpentEvent>(g).Count()==1&&E<YanRouMovementIssuedEvent>(g).Count(e=>e.Stage==YanRouStage.DrawChildren)==1,"A real Draw child damages the recovered owner below its source threshold; the paid limited fact survives and the native draw is never reissued.");_=Cold(g,r);

        (g,r)=Start("reshuffle");Use(g,"draw-ten");Play(g);Use(g,"five",targets:[1]);Play(g);End(g);Reach(g,()=>Optional(P(g),Xiang));Activate(g);Reach(g,()=>Current(g)is{Stage:YanRouStage.TargetChoice});g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));
        Reach(g,()=>E<YanRouCompletedEvent>(g).Any());var draw=E<YanRouMovementIssuedEvent>(g).Single(e=>e.Stage==YanRouStage.DrawChildren);var shuffle=E<YanRouReshuffleCapturedEvent>(g).Single();
        Need(draw.Count==5&&shuffle.Count>0&&shuffle.Before>=draw.Before&&shuffle.After<=draw.After&&g.CardMovements.Count(m=>m.Sequence>draw.Before&&m.Sequence<=draw.After&&m.Reason.Value=="skill-program.yan-rou.xiangshu.draw")==5,
            "A genuinely exhausted small deck reshuffles only the exact native discarded entities, counted separately from the five actual draws.");_=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static YanRouReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.YanRou is not null)?.YanRou;
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,x.Error?.Message??"The real command was rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human can Answer; AI advances natively.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]?cards=null,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<500;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Yan Rou ended before expected boundary: "+Diagnostic(g));var p=P(g);
            if(p is{PlayerSeat:0}&&(Optional(p,Chou)||Optional(p,Xiang)))Skip(g);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0}&&p.SkillPrompt?.SkillId==Health)Continue(g);
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Yan Rou prompt: "+Diagnostic(g));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Yan Rou did not reach its true native boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Status,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive,Skills=V(g,s).Skills?.Select(k=>k.Id).ToArray()}).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves private opaque choices, paid movement/HP tails, real use/debit identities and permanent limited facts.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-yan-rou",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:yan-rou");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}],"activations":[
              {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]},
              {"id":"five","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5}]},
              {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"draw-ten","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":10}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"lose-aid","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Xiang}}"],"sourceBind":"standard:none"}]},
              {"id":"grant-aid","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Xiang}}"]}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Peer}}","revision":1,"triggers":[{"id":"retaliate","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
             {"id":"{{Prep}}","revision":1,"triggers":[{"id":"preparation","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{(scenario=="aid-loss"?Xiang:Chou)}}","minimumHp":9}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-child","window":"{{(scenario=="aid-loss"?"cardsGained":"cardsMoved")}}","subject":"owner",{{(scenario=="aid-loss"?"\"destinationZones\":[\"hand\"]":"\"sourceZones\":[\"hand\"]")}},"movementOccurrence":"perBatch","movementReasons":["{{(scenario=="aid-loss"?"skill-program.yan-rou.xiangshu.draw":"skill-program.yan-rou.choutao.discard")}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scenario=="cost-death"?20:scenario=="aid-loss"?2:1)}}}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Peer,Health,Prep,Conditional,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="阎柔原生命令边界夹具",optionLabels=id==Health||id==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:yr-pick-owner","本人公开评分","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var skills=new List<string>{Driver};skills.Add(scenario=="cost-loss"?Conditional:Chou);skills.Add(Xiang);if(scenario is"cost-loss" or"cost-death" or"aid-loss")skills.Add(Watch);if(scenario=="aid-loss"){skills.Remove(Xiang);skills.Add(Conditional);}if(scenario=="prep")skills.Add(Prep);
            b.AddGeneral(new(Owner,"阎柔真实本人","ol-yan-rou","fixture:yr-pick-owner","wei",scenario=="aid-loss"?9:8,skills,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:yr-peer-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:yr-pick-"+index;b.AddSkill(new(pick,"其他公开评分","固定顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他真实角色","supporter",pick,"wei",8,scenario=="lion"?[Quiet,Peer,Health]:scenario=="retaliate"?[Quiet,Peer]:[Quiet]));}
            b.AddCard(new("fixture:yr-lion","白银狮子","装备牌","真实原生移除回复",CardKind.SilverLion));b.AddCard(new("fixture:yr-ox","木牛流马","装备牌","真实原生存粮与移除",CardKind.WoodenOx));
            b.AddDeck(new("fixture:yr-deck","固定小实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario=="reshuffle"?28:96).Select(i=>new ContentDeckPhysicalCard((scenario is"lion" or"ox")&&i%5==0?scenario=="ox"?"fixture:yr-ox":"fixture:yr-lion":"standard:slash",Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"阎柔原生规则",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:yr-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
