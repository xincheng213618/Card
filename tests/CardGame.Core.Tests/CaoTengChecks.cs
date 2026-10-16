using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoTengChecks
{
    private const string Owner="fixture:ct-owner",Receiver="fixture:ct-receiver",Driver="fixture:ct-driver",Quiet="fixture:ct-quiet",Watch="fixture:ct-watch",Source="fixture:ct-source",Mode="identity:classic-cao-teng";
    private const string Qingliu="ol:qingliu",Yongzu="ol:yongzu",Jianxiong="ol:caoteng-jianxiong";
    private const string GrantWatch="fixture:ct-first-grant-watch";
    public static void SetupAndFirstActualDyingRescueFactionCold()
    {
        var(g,r)=Start("rescue",opening:true);Need(P(g)!.IsPrivate&&P(g)!.Choices.Count==2&&E<NativeEffectiveFactionChangedEvent>(g).Length==0,"The mandatory opening faction choice precedes actual-turn changes.");
        g=Cold(g,r);Branch(g,"wei");Play(g);Need(V(g).FactionId=="wei"&&E<CaoTengFactionCompletedEvent>(g)is[{Setup:true,Faction:"wei"}]&&E<NativeEffectiveFactionChangedEvent>(g).Length==0,"Opening Wei is a real selected effective faction, without an in-turn change fact.");
        Use(g,"to-dying");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying});var first=E<PlayerDyingEvent>(g).Single();g=Cold(g,r);Answer(g,c=>c.Cards.Count==1&&V(g).Hand.Any(h=>h.Id==c.Cards[0]&&h.Kind==CardKind.Peach));Play(g);
        Need(E<DyingResolvedEvent>(g).Any(e=>e.ResolutionId==first.ResolutionId&&e.Survived)&&V(g).FactionId=="qun"&&E<CaoTengFactionCompletedEvent>(g).Count(e=>!e.Setup)==1&&E<NativeEffectiveFactionChangedEvent>(g)is[{TargetSeat:0,PreviousFactionId:"wei",CurrentFactionId:"qun"}],"The whole first genuine self-Peach rescue finishes before Qingliu switches once to the other faction.");
        Use(g,"hurt");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1&&V(g).Hand.Any(h=>h.Id==c.Cards[0]&&h.Kind==CardKind.Peach));Play(g);
        Need(E<PlayerDyingEvent>(g).Length==2&&E<DyingResolvedEvent>(g).Count(e=>e.VictimSeat==0&&e.Survived)==2&&V(g).FactionId=="qun"&&E<CaoTengFactionCompletedEvent>(g).Count(e=>!e.Setup)==1,"A second real rescued dying occurrence cannot repeat the first-entry faction switch.");_=Cold(g,r);
        {
            (g,r)=Start("plain",opening:true);Branch(g,"qun");Play(g);Need(V(g).FactionId=="qun"&&E<CaoTengFactionCompletedEvent>(g)is[{Setup:true,Faction:"qun"}]&&E<NativeEffectiveFactionChangedEvent>(g).Length==0,"Choosing the already printed Qun at setup is legal and creates no fabricated change.");_=Cold(g,r);
        }
    }
    public static void OrderedPrivateDistinctBenefitsRecoveryAndRestoreCold()
    {
        var(g,r)=Start("plain",benefit:true);Activate(g);Target(g,1);Need(Current(g)is{Cursor:0,SameFaction:true}&&P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="recover")&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="restore"),"A same-faction pair starts with the owner, cannot recover at full HP, but may choose a no-op restoration.");
        var hand=V(g).HandCount;g=Cold(g,r);Branch(g,"draw");Reach(g,()=>Current(g)is{Cursor:1,Stage:CaoTengStage.YongzuChoice});
        Need(P(g)!.PlayerSeat==1&&V(g).HandCount==hand+2&&P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="draw")&&g.CreateSnapshot(0).PendingDecision is null,"Only the real second chooser sees the remaining private options after the owner's actual two-card draw.");
        var state=State(g);var wrong=g.Submit(new AnswerPromptCommand(0,P(g)!.PromptId,P(g)!.Choices[0].Id,g.Revision));Need(!wrong.Accepted&&State(g)==state,"A different seat cannot answer or mutate the frozen recipient's choice.");g=Cold(g,r);Play(g);
        Need(E<CaoTengBenefitIssuedEvent>(g).Select(e=>e.Option).SequenceEqual(["draw","restore"])&&E<CaoTengBenefitIssuedEvent>(g).Select(e=>e.RecipientSeat).SequenceEqual([0,1])&&E<CaoTengCompletedEvent>(g).Count(e=>E<CaoTengStartedEvent>(g).Any(s=>s.FrameId==e.FrameId&&s.Op==SkillProgramEffectOp.ResolveYongzuDistinctBenefits))==1,"The real AI selects a different available item; a no-op restore completes the second benefit once.");_=Cold(g,r);
        {
            (g,r)=Start("foreign");Use(g,"lose");Play(g);var hp=V(g).Hp;End(g);Reach(g,()=>Current(g)is{Stage:CaoTengStage.YongzuChoice,Cursor:1,TargetSeat:0}&&P(g)?.PlayerSeat==0);
            Need(Frame(g).OwnerSeat!=0&&E<CaoTengBenefitIssuedEvent>(g).Single().Option=="draw"&&P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="draw"),"The actual loyalist AI selects its allied human Lord and pays its own first benefit through Advance.");
            g=Cold(g,r);Branch(g,"recover");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(V(g).Hp==hp+1&&E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0),"The foreign chooser receives native recovery, with its real after-recovery child still pending.");g=Cold(g,r);Continue(g);Reach(g,()=>Current(g)is null);
            Need(E<CaoTengBenefitIssuedEvent>(g).Last()is{Index:1,RecipientSeat:0,Option:"recover"}&&E<CaoTengCompletedEvent>(g).Any(),"Recovery returns to the original foreign Yongzu frame once.");_=Cold(g,r);
        }
        {
            (g,r)=Start("restore",benefit:true);Need(V(g,1).IsFaceDown&&V(g,1).IsChained,"Actual game-start native setup put the future recipient into both states before its own turn.");Activate(g);Target(g,1);Branch(g,"draw");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Play(g);
            Need(!V(g,1).IsFaceDown&&!V(g,1).IsChained&&E<CharacterStateChangedEvent>(g).Any(e=>e.Change.TargetSeat==1&&e.Change.Window==SkillProgramTriggerWindow.CharacterTurnedFaceUp),"The second actual restoration clears both native face-down and chained state, including its true character-state window.");_=Cold(g,r);
        }
    }
    public static void PermanentHandGrowthLinkedAndGrantOwnTurnExpiryCold()
    {
        {
            var(g,r)=Start("first-grant",opening:true);Branch(g,"wei");Reach(g,()=>Offer(g,Yongzu));Activate(g);Target(g,1);var frame=Frame(g).Id;
            Need(E<CaoTengBenefitIssuedEvent>(g).Length==0&&E<CaoTengSkillGrantedEvent>(g).Length==0,"The first native skill mutation is the first preparation's option-five grant, without a prior draw, limit benefit or HP change to initialize ownership observations.");
            Branch(g,"skill");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==GrantWatch);
            var window=g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w=>w.Window==SkillProgramTriggerWindow.SkillsChanged&&w.ResumeProgramFrameId==frame);
            var child=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            Need(window.OwnerSeat==0&&window.Continuation==ProgramLifecycleContinuation.ResumeParentProgram&&child.SkillId==GrantWatch&&child.WindowContext?.ParentFrameId==window.Id&&
                E<ProgramBindingStartedEvent>(g).Count(e=>e.FrameId==child.Id&&e.SkillId==GrantWatch&&e.Window==SkillProgramTriggerWindow.SkillsChanged)==1&&
                E<CaoTengSkillGrantedEvent>(g)is[{RecipientSeat:0,SkillId:Jianxiong}]&&E<CaoTengBenefitIssuedEvent>(g)is[{Index:0,Option:"skill"}]&&
                Current(g)is{Stage:CaoTengStage.YongzuChildren,Cursor:0,Option:"skill"}&&g.CardMovements.All(m=>m.Reason.Value!="skill-program.cao-teng.yongzu.draw"),
                "The very first grant opens the real native SkillsChanged window and exact watcher child before the second private chooser, without manufacturing a movement or a duplicate grant.");
            g=Cold(g,r);Continue(g);Reach(g,()=>Current(g)is{Stage:CaoTengStage.YongzuChoice,Cursor:1});Play(g);
            Need(E<CaoTengSkillGrantedEvent>(g).Length==1&&E<CaoTengBenefitIssuedEvent>(g).Count(e=>e.FrameId==frame)==2&&E<CaoTengCompletedEvent>(g).Count(e=>e.FrameId==frame)==1&&
                E<ProgramBindingStartedEvent>(g).Count(e=>e.SkillId==GrantWatch&&e.Window==SkillProgramTriggerWindow.SkillsChanged)==1,
                "Cold recovery returns the native first-grant watcher once to the original pair; it cannot grant twice, re-open the ownership change or skip the other beneficiary.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("plain",benefit:true);Activate(g);Target(g,1);Branch(g,"limit");Play(g);var limit=V(g).HandLimit;
            Need(E<CaoTengHandLimitGrantedEvent>(g)is[{RecipientSeat:0}]&&limit==V(g).Hp+101,"The unlimited-duration option adds one to the actual native hand-limit query.");Use(g,"lose-source");Play(g);
            Need(!Has(g,0,Yongzu)&&V(g).HandLimit==limit,"Losing the originating Yongzu cannot revoke an already granted permanent hand-limit increment.");_=Cold(g,r);
        }
        foreach(var permanent in new[]{false,true})
        {
            var(g,r)=Start(permanent?"permanent":"lease",opening:true);Branch(g,"wei");Reach(g,()=>Offer(g,Yongzu));Activate(g);Target(g,1);Branch(g,"skill");Play(g);var grant=E<CaoTengSkillGrantedEvent>(g).Single();
            Need(grant.RecipientSeat==0&&grant.SkillId==Jianxiong&&Has(g,0,Jianxiong),"A Wei beneficiary receives exact current linked717, not the old OR/claim-only variants.");
            var duel=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.Count==1&&V(g,a.TargetSeats[0]).Hand.Any(c=>c.Kind==CardKind.Slash));var card=duel.CardId!.Value;Use(g,"wound-target",duel.TargetSeats.ToArray());Play(g);var hand=V(g).HandCount;
            Native(g,duel);Reach(g,()=>Offer(g,Jianxiong));Need(E<DuelResponseEvent>(g).Any(e=>e.UsedSlash&&e.SlashCardId is not null)&&E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0),"An enemy's genuine physical Slash reverses native Duel damage onto the skill holder.");g=Cold(g,r);Activate(g);Play(g);
            Need(V(g).Hand.Any(c=>c.Id==card)&&g.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.cao-teng.jianxiong.claim")==1&&g.CardMovements.Count(m=>m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.cao-teng.jianxiong.draw")==1&&V(g).HandCount==hand+1,"Linked717 really draws one AND obtains the exact native damage card; it is not a choice between the two effects.");
            Use(g,"flip");Play(g);var turn=g.State.TurnNumber;g=Cold(g,r);End(g);Reach(g,()=>E<TurnStartedEvent>(g).Any(e=>e.ActorSeat==0&&e.TurnNumber>turn));
            Need(E<CaoTengSkillExpiredEvent>(g).Count(e=>e.GrantId==grant.GrantId)==1&&Has(g,0,Jianxiong)==permanent&&E<TurnStartedEvent>(g).Last(e=>e.ActorSeat==0).TurnNumber>grant.ActualTurn,"Only this exact grant expires at the beneficiary's next genuine TurnStarted, even for a face-down skipped turn; another permanent same-name source survives.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("qun-lease",benefit:true);Activate(g);Target(g,1);Branch(g,"skill");Play(g);Need(E<CaoTengSkillGrantedEvent>(g).Single().SkillId=="ol:tianming"&&Has(g,0,"ol:tianming"),"Qun maps to the actual registered linked604 Tianming leaf.");_=Cold(g,r);
        }
    }
    public static void PaidNativeChildrenSourceLossDeathAndExactClaimCold()
    {
        foreach(var scene in new[]{"paid-loss","paid-death"})
        {
            var(g,r)=Start(scene,benefit:true);Activate(g);Target(g,1);var hand=V(g).HandCount;Branch(g,"draw");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var frame=Frame(g).Id;
            Need(E<CaoTengBenefitIssuedEvent>(g).Single()is{Index:0,Option:"draw"}&&V(g).HandCount==hand+2,"The first benefit is genuinely paid before the real gain observer pauses.");g=Cold(g,r);Continue(g);Reach(g,()=>E<CaoTengCompletedEvent>(g).Any(e=>e.FrameId==frame));
            Need(E<CaoTengBenefitIssuedEvent>(g).Count(e=>e.FrameId==frame)==2&&E<CaoTengBenefitAppliedEvent>(g).Count(e=>e.FrameId==frame)==2&&E<CaoTengCompletedEvent>(g).Count(e=>e.FrameId==frame)==1&&g.CardMovements.Count(m=>m.Reason.Value=="skill-program.cao-teng.yongzu.draw"&&m.To==CardLocation.Hand(0))==2,"Already issued benefits and the original other chooser return once after source loss or non-Lord source death, without a second draw.");
            Need(scene=="paid-loss"?!Has(g,0,Yongzu)&&V(g).IsAlive:!V(g).IsAlive&&E<CaoTengFactionCompletedEvent>(g).All(e=>e.Setup),"The real loss/death occurred; an unrescued first dying event does not perform Qingliu's rescue switch.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("claim-loss");var duel=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.Count==1&&V(g,a.TargetSeats[0]).Hand.Any(c=>c.Kind==CardKind.Slash));var card=duel.CardId!.Value;Use(g,"wound-target",duel.TargetSeats.ToArray());Play(g);Native(g,duel);Reach(g,()=>Offer(g,Jianxiong));Activate(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var frame=Frame(g).Id;g=Cold(g,r);Continue(g);Play(g);
            Need(!Has(g,0,Jianxiong)&&V(g).Hand.Any(c=>c.Id==card)&&E<CaoTengJianxiongClaimedEvent>(g).Count(e=>e.FrameId==frame&&e.CardId==card)==1&&E<CaoTengCompletedEvent>(g).Count(e=>e.FrameId==frame)==1,"The original damage entity is still claimed once after the actual one-card draw child removes the exact conditional Jianxiong source.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Has(GameEngine g,int seat,string skill)=>V(g,seat).Skills!.Any(s=>s.ContentId==skill);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Frame(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CaoTeng is not null);
    private static CaoTengReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.CaoTeng is not null)?.CaoTeng;
    private static bool Offer(GameEngine g,string skill)=>P(g)?.PlayerSeat==0&&P(g)?.SkillPrompt?.SkillId==skill&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Cao Teng command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Actual AI choices advance through the native brain.");var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing Cao Teng choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Branch(GameEngine g,string branch)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);
    private static void Target(GameEngine g,int seat)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="target"&&c.Targets.SequenceEqual([seat]));
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};cursor={Current(g)?.Cursor};faction={V(g).FactionId};hp={V(g).Hp};benefits={JsonSerializer.Serialize(E<CaoTengBenefitIssuedEvent>(g))}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<240;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing Cao Teng boundary: "+Diagnostic(g));var p=P(g);
            if(p is null||!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.SkillPrompt?.SkillId=="fixture:ct-setup")Answer(g,c=>c.Targets.SequenceEqual([1]));
            else if(Current(g)is{Stage:CaoTengStage.FactionChoice})Branch(g,"qun");
            else throw new InvalidOperationException("Unexpected Cao Teng boundary: "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded Cao Teng boundary: "+Diagnostic(g));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s)))+JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CardMovements)+g.Events.Count;
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves exact native faction, chooser, original cause, paid children and source-specific lifetime.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene,bool opening=false,bool benefit=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="paid-death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,scene=="foreign"?Receiver:Owner,g.Revision,P(g)!.PromptId));
        if(opening)Reach(g,()=>Current(g)is{Stage:CaoTengStage.FactionChoice});else if(benefit)Reach(g,()=>Offer(g,Yongzu));else Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-cao-teng",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:cao-teng");CharacterFixtureContent.Register(b,"ol:liu-xie");
            var defs=new List<string>{$$$"""
            {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
            {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
            {"id":"to-dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]},
            {"id":"wound-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
            {"id":"flip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":true}]},
            {"id":"flip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"selectedTarget","faceDown":true}]},
            {"id":"chain-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
            {"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:yongzu"],"sourceBind":"standard:none"}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var skills=new List<string>{Driver,Qingliu};
            if(scene=="paid-loss"){defs.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"full","skillId":"ol:yongzu","minimumHp":4}]}""");skills.Add(Source);}else skills.Add(Yongzu);
            if(scene=="claim-loss"){defs.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"ol:caoteng-jianxiong","minimumHp":3}]}""");skills.Add(Source);}
            if(scene=="permanent")skills.Add(Jianxiong);
            if(scene=="first-grant")
            {
                defs.Add($$"""{"id":"{{GrantWatch}}","revision":1,"triggers":[{"id":"first-native-grant","window":"skillsChanged","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grant","options":[{"id":"continue"}]}]}]}""");skills.Add(GrantWatch);
            }
            if(scene is"paid-loss"or"paid-death"or"claim-loss"or"foreign")
            {
                var window=scene=="foreign"?"\"window\":\"afterHpRecovered\",\"subject\":\"owner\"":"\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.cao-teng."+(scene=="claim-loss"?"jianxiong":"yongzu")+".draw\"]";
                var tail=scene=="foreign"?"":scene=="paid-death"?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}":",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-child",{{window}},"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}{{tail}}]}]}""");skills.Add(Watch);
            }
            if(scene=="restore")
            {
                defs.Add("""{"id":"fixture:ct-setup","revision":1,"triggers":[{"id":"real-opening-states","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"setFaceState","target":"selectedTarget","faceDown":true},{"op":"setChainedState","target":"selectedTarget","chained":true}]}]}""");skills.Add("fixture:ct-setup");
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-face-up","window":"characterTurnedFaceUp","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}]}]}""");
            }
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>d["id"]!.GetValue<string>()is Watch or GrantWatch?(object)new{name="真实曹腾子窗",description="真实收益子窗",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}:new{name=d["id"]!.GetValue<string>(),description="真实曹腾夹具"});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            const string ownerPick="fixture:ct-owner-pick";b.AddSkill(new(ownerPick,"本人","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>scene=="foreign"&&role==Role.Loyalist?100000000d:-100000000d)});
            b.AddGeneral(new(Owner,"真实曹腾","ol-cao-teng",ownerPick,"qun",3,scene=="foreign"?[Qingliu,Yongzu,Quiet]:skills,GeneralGender.Male));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:ct-peer-"+i).ToArray();
            if(scene=="foreign")
            {
                b.AddGeneral(new(Receiver,"真实受益者","supporter",Driver,"wei",3,[Watch],GeneralGender.Male));
                foreach(var(peer,i)in peers.Take(2).Select((p,i)=>(p,i))){var pick="fixture:ct-pick-"+i;b.AddSkill(new(pick,"其他角色","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(2-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"shu",8,[Quiet]));}
            }
            else foreach(var(peer,i)in peers.Select((p,i)=>(p,i))){var pick="fixture:ct-pick-"+i;b.AddSkill(new(pick,"其他角色","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,scene is"lease"or"permanent"or"claim-loss"or"first-grant"?"wei":"qun",8,scene=="restore"&&i==0?[Quiet,Watch]:[Quiet]));}
            var ids=scene=="rescue"?new[]{"standard:peach"}:scene is"lease"or"permanent"or"claim-loss"?new[]{"standard:duel","standard:slash"}:new[]{"standard:dodge"};
            b.AddDeck(new("fixture:ct-deck","真实固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(ids[i%ids.Length],new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i/ids.Length%4],i%13+1)).ToArray()});
            var roles=scene=="foreign"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2}:scene=="paid-death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:scene is"lease"or"permanent"or"claim-loss"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3};
            b.AddMode(new(Mode,"真实清流拥族",4,4,roles,"fixture:ct-deck",GeneralCandidateCount:4,GeneralPoolIds:scene=="foreign"?[Receiver,Owner,..peers.Take(2)]:[Owner,..peers]));
        }
    }
}
