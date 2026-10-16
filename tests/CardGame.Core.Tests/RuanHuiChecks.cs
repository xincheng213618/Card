using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RuanHuiChecks
{
    private const string Ming="ol:mingcha",Jing="ol:jingzhong",Source="fixture:rh-source",Player="fixture:rh-player",Driver="fixture:rh-driver",Watch="fixture:rh-watch",Health="fixture:rh-health",Mode="identity:classic-ruan-hui-fixture";
    public static void MingchaPublicRevealNormalReplacementZeroAndReshuffleCold()
    {
        foreach(var scene in new[]{"low","high","zero","short","reshuffle"})
        {
            var(g,r)=Start(scene);Reach(g,()=>Stage(g,RuanHuiStage.DrawChoice));var f=Root(g);var entities=f.RuanHui!.Entities.ToArray();var before=V(g,0).HandCount;
            Need(entities.Length==(scene=="short"?1:3)&&Enumerable.Range(0,4).All(s=>entities.All(e=>g.CreateSnapshot(s).PublicRevealedCards.Any(c=>c.Id==e.CardId))),"Mingcha really publishes its bounded top-three entities to every player before choosing whether to replace normal draw.");
            var normalOrder=g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.DrawPile).OrderByDescending(c=>c.ZoneIndex).Take(2).Select(c=>c.CardId).ToArray();
            var state=State(g);var p=P(g)!;Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"Another seat cannot choose the source's already-public draw replacement.");g=Cold(g,r);
            var replace=scene is"low"or"zero"or"short";Answer(g,c=>c.Parameters.GetValueOrDefault("branch")== (replace?"replace":"normal"));
            if(replace){Reach(g,()=>Stage(g,RuanHuiStage.TargetChoice));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")== (scene=="zero"?"target.1":"skip"));}
            Play(g);var gained=g.CardMovements.Where(m=>m.Reason.Value=="skill-program.ruan-hui.mingcha.gain"&&m.To==CardLocation.Hand(0)).ToArray();
            Need(gained.Length==(scene=="low"?3:scene=="short"?1:0)&&V(g,0).HandCount==before+(scene=="low"?3:scene is"zero"or"short"?1:2),"The real optional replacement obtains only actual <=8 reveal entities, while a zero-match replacement can take its separate current-text random HE benefit.");
            if(!replace){var actual=g.CardMovements.Where(m=>m.Reason==CardMoveReasons.Draw&&m.To==CardLocation.Hand(0)).Select(m=>m.CardId).ToArray();Need(actual.Take(normalOrder.Length).SequenceEqual(normalOrder)&&entities.All(e=>g.CardMovements.Any(m=>m.CardId==e.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile)),"Declining replacement settles the revealed cards and preserves the original native order of subsequent normal draws.");}
            if(scene=="reshuffle")Need(g.CardMovements.Any(m=>m.Reason==CardMoveReasons.Reshuffle),"The genuine normal draw reshuffles actual reveal cleanup cards when the original pile runs out.");
            Need(E<RuanHuiCompletedEvent>(g).Count(e=>e.FrameId==f.Id)==1,"The exact public reveal and draw plan finish once after cold recovery.");_=Cold(g,r);
        }
    }
    public static void MingchaRandomEquipmentPaidLossDeathLionOxChildrenCold()
    {
        foreach(var scene in new[]{"lion","ox","random-loss","random-death"})
        {
            var(g,r)=Start(scene);Reach(g,()=>Stage(g,RuanHuiStage.DrawChoice));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="normal");Play(g);
            if(scene=="ox")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);
                Use(g,"transfer",[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);Play(g);
            }
            else{Use(g,"equip-peer",[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);Play(g);Use(g,"lose",[1]);Play(g);}
            var equipment=V(g,1).Equipment.Single().Id;var firstTurn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>firstTurn&&Stage(g,RuanHuiStage.DrawChoice));
            Need(V(g,1).HandCount==0&&V(g,1).Equipment.Single().Id==equipment,"The target's HE pool is reached by genuine draws, discards and equipment movement, leaving one actual equipped entity.");
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="replace");Reach(g,()=>Stage(g,RuanHuiStage.TargetChoice));var prompt=P(g)!;
            Need(prompt.Choices.All(c=>c.Cards.Count==0)&&prompt.Choices.Any(c=>c.Targets.SequenceEqual([1])),"Random HE acquisition exposes only a target choice and never enumerates foreign hidden card IDs.");g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));
            if(scene!="ox"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g,1).Hp==V(g,1).MaxHp&&g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h=>h.Change.Kind==HpChangeKind.Recovery&&h.Change.TargetSeat==1),"Actual equipped Silver Lion removal opens the giver's genuine recovery child.");g=Cold(g,r);Continue(g);}
            if(scene is"random-loss"or"random-death"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);}
            if(scene=="random-death"){Reach(g,()=>g.ResolutionStack.OfType<DyingFrame>().Any());g=Cold(g,r);Reach(g,()=>!V(g,0).IsAlive);Need(g.State.Winner==Winner.None,"A paid random gain drains the real non-Lord owner's dying/death child.");}
            else{Play(g);Need(V(g,0).Hand.Any(c=>c.Id==equipment)&&(scene!="random-loss"||V(g,0).Skills!.All(s=>s.ContentId!=Ming)),"The already-paid original random equipment gain survives native source loss and cold child returns.");}
            Need(g.CardMovements.Count(m=>m.CardId==equipment&&m.From==CardLocation.Equipment(1)&&m.Reason.Value=="skill-program.ruan-hui.mingcha.random")==1,"The actual random entity is moved once without retrying the seeded choice.");
            if(scene=="ox")Need(g.CardMovements.Any(m=>m.From==CardLocation.WoodenOxGrain(1)&&m.To==CardLocation.DiscardPile),"The actual obtained Ox drains its original stored grain cleanup.");_=Cold(g,r);
        }
    }
    public static void JingzhongActualDiscardNextPlayWholeUseQuotaAndLossCold()
    {
        foreach(var scene in new[]{"jing","jing-loss","jing-death"})
        {
            var(g,r)=JingStart(scene);var source=SourceSeat(g);var lease=E<JingzhongLeaseEvent>(g).Single();
            Need(E<JingzhongDiscardEvent>(g).Count(e=>e.PhaseToken==lease.AfterPhaseToken)>=2&&lease.RecipientSeat==1,"A genuine AI discard phase pays at least two actual black discards before its next-Play lease is offered to the human.");
            Use(g,"draw");Play(g);var before=E<JingzhongClaimIssuedEvent>(g).Length;var pair=V(g,1).Hand.Take(2).Select(c=>c.Id).ToArray();
            Accept(g,new UseProgramSkillCommand(1,Driver,"pair",pair,[0],g.Revision,P(g)!.PromptId));
            if(scene is"jing-loss"or"jing-death"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);}
            if(scene=="jing-death"){Reach(g,()=>g.ResolutionStack.OfType<DyingFrame>().Any());g=Cold(g,r);Reach(g,()=>!V(g,source).IsAlive);Play(g);Need(g.State.Winner==Winner.None,"The real paid two-material claim drains its AI issuer's native death without inventing further gains.");}
            else
            {
                Play(g);if(scene=="jing-loss")Need(V(g,source).Skills!.All(s=>s.ContentId!=Jing),"The actual first claim's native gain child removes the issuing conditional source.");
                for(var i=0;i<3;i++){Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==0&&a.ConversionSource?.BindingId=="one");Play(g);}
                var claims=E<JingzhongClaimIssuedEvent>(g).Skip(before).ToArray();Need(claims.Length==3&&claims[0].EntityCount==2&&claims.Skip(1).All(e=>e.EntityCount==1)&&claims.Select(e=>e.ActionId).Distinct().Count()==3,"Three actual native Use opportunities obtain all original materials once each, survive source loss and reject a fourth use in the same real Play phase.");
                var count=claims.Length;Empty(g);Need(E<JingzhongClaimIssuedEvent>(g).Length==before+count,"Pure native discards never masquerade as used-card claims.");
            }
            Need(E<JingzhongClaimIssuedEvent>(g).First().EntityCount==2&&g.CardMovements.Count(m=>pair.Contains(m.CardId)&&m.Reason.Value=="skill-program.ruan-hui.jingzhong.gain")==2,"The first multi-material native cleanup is claimed as one exact whole-Use invoice.");_=Cold(g,r);
        }
    }
    public static void JingzhongResponseUseBoardResidencyAndActorOwnershipCold()
    {
        {
            var(g,r)=JingStart("responses");var source=SourceSeat(g);Use(g,"draw");Play(g);Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==source&&a.ConversionSource?.BindingId=="one");Play(g);
            Use(g,"incoming",[source]);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Targets.SequenceEqual([1]));
            Reach(g,()=>P(g)is{PlayerSeat:1,Kind:DecisionKind.RespondDodge});g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("conversion-skill-id")==Driver);Play(g);
            Need(E<JingzhongUseEntityEvent>(g).Any(e=>e.Response&&e.OriginalActor==1)&&E<JingzhongClaimIssuedEvent>(g).Length==2,"A real used Dodge within the leased actor's own Play is obtained separately from the original proactive Slash; the counter-Slash's source does not inherit response ownership.");
            End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:rh-trick"&&Action(g,"activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([source]));
            Reach(g,()=>P(g)is{PlayerSeat:1,Kind:DecisionKind.Nullification});g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("conversion-skill-id")==Driver);Reach(g,()=>E<JingzhongClaimIssuedEvent>(g).Length==3);_=Cold(g,r);
        }
        {
            var(g,r)=JingStart("board");Use(g,"draw");Play(g);Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var equipment=V(g,1).Equipment.Single().Id;
            Need(E<JingzhongClaimIssuedEvent>(g).Length==0,"An equipped material is not fabricated as an immediate discarded native Use.");Use(g,"discard-equipment");Reach(g,()=>Action(g,"select-owned-cards"));Answer(g,c=>c.Cards.SequenceEqual([equipment]));FinishOwned(g);Play(g);
            Need(g.CardMovements.Count(m=>m.CardId==equipment&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(SourceSeat(g))&&m.Reason.Value=="skill-program.ruan-hui.jingzhong.gain")==1,"The original actual equipment Use residency remains eligible when its same entity really enters discard later within that leased phase.");_=Cold(g,r);
        }
        {
            var(g,r)=JingStart("actor");Use(g,"draw");Play(g);Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==0&&a.ConversionSource?.BindingId=="one");Reach(g,()=>Action(g,"select-target"));var chosen=P(g)!.Choices.SelectMany(c=>c.Targets).First(s=>s!=1);g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([chosen]));Play(g);
            Need(E<ProgramCardUseActorReplacedEvent>(g).Any(e=>e.PreviousActorSeat==1&&e.ActorSeat==chosen&&e.ProviderSeat==1)&&E<JingzhongClaimIssuedEvent>(g).Length==0,"A genuine native actor replacement transfers actual Use ownership to the published legal actor while retaining original physical provider 1; the issuer cannot claim against the old actor's lease.");_=Cold(g,r);
        }
    }
    private static int Human(GameEngine g)=>Enumerable.Range(0,4).Single(s=>V(g,s).IsHuman);
    private static int SourceSeat(GameEngine g)=>Enumerable.Range(0,4).Single(s=>V(g,s).GeneralId==Source);
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.RuanHui is not null);
    private static bool Stage(GameEngine g,RuanHuiStage s)=>P(g)?.SkillPrompt?.SkillId is Ming or Jing&&g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.RuanHui?.Stage==s;
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected real RuanHui command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==Human(g)&&c is not null,"Missing exact human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)!.PlayerSeat==Human(g))Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(Human(g),Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Empty(GameEngine g)
    {
        var human=Human(g);var hand=V(g,human).Hand.Select(c=>c.Id).ToArray();var before=g.CardMovements.Count;Use(g,"empty");Play(g);
        Need(V(g,human).HandCount==0&&hand.All(id=>g.CardMovements.Skip(before).Any(m=>m.CardId==id&&m.From==CardLocation.Hand(human)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==$"skill-program.{Driver}.MoveBoundCards")),"The automatic all-owned selection really discards every original hand entity before returning to the same native Play.");
    }
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(Human(g),g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> predicate){var a=g.GetHumanLegalActions().FirstOrDefault(predicate);Need(a is not null,"Missing native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(Human(g),a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void FinishOwned(GameEngine g){for(var i=0;i<100&&(Action(g,"select-owned-cards")||Action(g,"finish-owned-cards"));i++){if(P(g)!.Choices.Any(c=>c.Cards.Count==1))Answer(g,c=>c.Cards.Count==1);else Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");} }
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard}p&&p.PlayerSeat==Human(g),$"{caller}:{line} actual human Play");
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? condition=null)
    {
        var boundary=$"{caller}:{line} {condition}";
        for(var i=0;i<320;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"RuanHui ended before boundary "+boundary+": "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=Human(g)){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId is Watch or Health)Continue(g);
            else if(Stage(g,RuanHuiStage.DrawChoice))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="normal");
            else if(Stage(g,RuanHuiStage.TargetChoice)&&p.SkillPrompt?.SkillId==Ming)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException("Unexpected RuanHui native choice while waiting for "+boundary+": "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded RuanHui boundary "+boundary+": "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).IsHuman,V(g,s).Hp,V(g,s).HandCount}),Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name}),Discards=E<JingzhongDiscardEvent>(g).TakeLast(8),Leases=E<JingzhongLeaseEvent>(g).TakeLast(4),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Jing).TakeLast(4),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var c=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(c),"Cold replay preserves all four privacy views, phase leases, original material invoices, seeded random choice and exact native child returns.");return c;}
    private static(GameEngine,ContentRegistry)Start(string scene,bool jing=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene,jing));var human=jing?1:0;var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=human,HumanRole=scene=="random-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral},$"Start[{scene}] select general");Accept(g,new SelectGeneralCommand(human,jing?Player:Source,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private static(GameEngine,ContentRegistry)JingStart(string scene)
    {
        var(g,r)=Start(scene,true);Play(g,$"JingStart[{scene}] first Play");Empty(g);var turn=g.State.TurnNumber;End(g);
        Reach(g,()=>g.State.TurnNumber>turn&&P(g)is{PlayerSeat:1,Kind:DecisionKind.PlayCard},$"JingStart[{scene}] next actual Play");
        var issuer=SourceSeat(g);Need(g.CardMovements.Count(m=>m.From==CardLocation.Hand(issuer)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.HandLimitDiscard)>=2,"JingStart["+scene+"] must reach genuine AI hand-limit discards, rather than letting its physical equipment be used up before discard: "+Diagnostic(g));
        Need(E<JingzhongLeaseEvent>(g).Any(e=>e.RecipientSeat==1),"The first actual AI discard phase must issue its next-Play lease to the genuinely empty human hand: "+JsonSerializer.Serialize(new{
            Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).Hp,V(g,s).HandCount,Skills=V(g,s).Skills?.Select(k=>k.ContentId)}),
            Discards=E<JingzhongDiscardEvent>(g),Leases=E<JingzhongLeaseEvent>(g),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Jing),
            Phases=E<ActualDiscardRecoveryPhaseStartedEvent>(g).Select(e=>e.Phase),Thoughts=g.AiThoughts.Where(t=>t.Decision.Contains("敬重",StringComparison.Ordinal)||t.Candidates.Any(c=>c.Action.Description.Contains("敬重",StringComparison.Ordinal))).Select(t=>new{t.ActorSeat,t.Decision,t.Summary})}));return(g,r);
    }
    private sealed class Fixture(string scene,bool jing):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ruan-hui",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ruan-hui");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"slash-room","query":"slashLimit","operation":"add","value":8,"priority":0}],"viewAs":[
             {"id":"one","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"two","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"dodge","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true},
             {"id":"nullification","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
             {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two","outputKind":"slash"}]},
             {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"discard-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["equipment"],"resultBind":"equipment"},{"op":"moveBoundCards","target":"owner","sourceBind":"equipment","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
             {"id":"transfer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["equipment"],"count":1,"destination":"selectedTargetCorrespondingZone","awaitMovementTriggers":true}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}"""};
            var emptyPeer=scene is"lion"or"ox"or"random-loss"or"random-death"?",{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"peer-hand\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"peer-hand\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}":"";
            defs.Add($$"""{"id":"fixture:rh-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}{{emptyPeer}}]}]}""");
            if(jing)defs.Add("""{"id":"fixture:rh-small-limit","revision":1,"modifiers":[{"id":"native-limit","query":"handLimit","operation":"add","value":-19,"priority":0}]}""");
            if(scene is"jing-loss"or"random-loss")defs.Add($$"""{"id":"fixture:rh-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{(jing?Jing:Ming)}}","minimumHp":{{(jing?20:21)}}}]}""");
            if(scene is"jing-loss"or"jing-death"or"random-loss"or"random-death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{(jing?"skill-program.ruan-hui.jingzhong.gain":"skill-program.ruan-hui.mingcha.random")}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scene.EndsWith("death",StringComparison.Ordinal)?20:1)}}}]}]}""");
            if(scene=="responses")
            {
                defs.Add("""{"id":"fixture:rh-trick","revision":1,"triggers":[{"id":"counter-trick","window":"playEnding","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"dismantlement"}]}]}""");
            }
            if(scene=="actor")defs.Add("""{"id":"fixture:rh-actor","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="阮慧真实原生夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:rh-pick","固定来源","固定选将公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>jing?10000000d:-10000000d)});
            var sourceSkills=new List<string>{scene=="random-loss"?"fixture:rh-conditional":Ming,scene=="jing-loss"?"fixture:rh-conditional":Jing,Health};
            if(jing){sourceSkills.Add("fixture:rh-small-limit");sourceSkills.Add("fixture:rh-quiet");}else sourceSkills.Add(Driver);if(scene is"jing-loss"or"jing-death"or"random-loss"or"random-death")sourceSkills.Add(Watch);
            b.AddGeneral(new(Source,"实际阮慧","ol-ruan-hui","fixture:rh-pick","wei",scene=="random-death"?19:20,sourceSkills,GeneralGender.Female));
            var humanSkills=new List<string>{Driver,Health};if(scene=="actor")humanSkills.Add("fixture:rh-actor");if(scene=="responses")humanSkills.Add("fixture:rh-trick");b.AddGeneral(new(Player,"实际受约束角色","supporter",jing?"standard:none":"fixture:rh-quiet","qun",20,jing?humanSkills:[Health]));
            var peers=new[]{"fixture:rh-peer-a","fixture:rh-peer-b"};foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter","fixture:rh-quiet","qun",20,[Health]));
            var kind=scene=="ox"?CardKind.WoodenOx:scene is"lion"or"random-loss"or"random-death"?CardKind.SilverLion:CardKind.Crossbow;var rank=scene is"high"or"zero"or"reshuffle"||jing?13:7;
            b.AddCard(new("fixture:rh-card","真实原始装备","装备牌","固定公开实体",kind));b.AddDeck(new("fixture:rh-deck","阮慧固定小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene=="short"?17:scene=="reshuffle"?20:100).Select(_=>new ContentDeckPhysicalCard("fixture:rh-card",Suit.Spade,rank)).ToArray()});
            b.AddMode(new(Mode,"阮慧原生行为",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:rh-deck",GeneralCandidateCount:4,GeneralPoolIds:[Source,Player,..peers]));
        }
    }
}
