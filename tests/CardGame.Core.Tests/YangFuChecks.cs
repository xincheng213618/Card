using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YangFuChecks
{
    private const string Bing="ol:bingzhong",Suyi="ol:suyi",Owner="fixture:yfu-owner",Driver="fixture:yfu-driver",Quiet="fixture:yfu-quiet",Watch="fixture:yfu-watch",Health="fixture:yfu-health",Mode="identity:classic-yang-fu-fixture";
    public static void BingzhongWholeActualTurnOrdinalDynamicTargetsAndCold()
    {
        foreach(var scene in new[]{"first","equipment-ordinal","empty","dodge","actor","tail"})
        {
            var(g,r)=Start(scene);
            if(scene=="equipment-ordinal")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Need(E<CurrentTurnCardUseKindsRecordedEvent>(g).Count(e=>e.ActorSeat==0)==1,"A real equipment Use occupies an ordinal even though it has no additional native equipment targets.");
                Slash(g,1);Play(g);Need(!E<YangFuStartedEvent>(g).Any(e=>e.Op==SkillProgramEffectOp.ResolveBingzhongExtraTargets),"The following second Use is outside dynamic X=1; equipment is not silently excluded from the first-X count.");_=Cold(g,r);continue;
            }
            if(scene=="dodge")
            {
                Use(g,"incoming",[1]);Reach(g,()=>P(g)?.PlayerSeat==0&&Action(g,"virtual-slash-offer")&&P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0])));Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
                g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);Need(E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0&&e.EffectiveCardKind==CardKind.Dodge),"The fixture actually uses one defensive Dodge against a genuine incoming zero-entity Slash.");
                Slash(g,1);Play(g);var own=E<CardActionAcceptedEvent>(g).Last(e=>e.Action.Type==CardActionType.Use&&e.Action.ActorSeat==0).Action;
                Need(Ordinal(g,own)==2&&!E<YangFuStartedEvent>(g).Any(e=>e.Op==SkillProgramEffectOp.ResolveBingzhongExtraTargets),"A real Slash-defense Dodge consumes the defender's first Use, independently of the attacking Use that shares its origin frame.");_=Cold(g,r);continue;
            }
            if(scene=="actor")
            {
                Slash(g,1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yfu-actor"&&Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([3]));Play(g);
                Need(E<ProgramCardUseActorReplacedEvent>(g).Any(e=>e.PreviousActorSeat==0&&e.ActorSeat==3&&e.ProviderSeat==0),"The first genuine native Use really changes to its legal actor while retaining provider zero.");
                Slash(g,1);Reach(g,()=>Targets(g));var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YangFu?.Stage==YangFuStage.Targets);
                Need(frame.YangFu!.Ordinal==1,"The next declared Use belongs to actor zero at ordinal one; it cannot inherit actor three's earlier final-actor history.");g=Cold(g,r);FinishTargets(g);Play(g);_=Cold(g,r);continue;
            }
            if(scene=="empty")
            {
                Use(g,"empty");Play(g);Need(V(g,0).HandCount==0,"The original owner really discarded all Hand cards through a native operation.");Use(g,"virtual",[1]);
            }
            else Slash(g,1);
            Reach(g,()=>Targets(g));var original=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YangFu?.Stage==YangFuStage.Targets);var receipt=original.YangFu!;
            Need(receipt.Ordinal==1&&receipt.X==(scene=="empty"?2:1)&&P(g)!.Choices.All(c=>!c.Targets.Contains(2)),"The instruction freezes live empty-Hand count plus one and keeps normal Slash range instead of granting distance to the opposite seat.");
            var before=State(g);var prompt=P(g)!;Need(!g.Submit(new AnswerPromptCommand(1,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"Another seat cannot commit the actual public extra-target choice or mutate its owning receipt.");
            g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));if(Targets(g))FinishTargets(g);Play(g);
            var applied=E<BingzhongTargetsAppliedEvent>(g).Single();Need(applied.X==receipt.X&&applied.Ordinal==1&&applied.AddedCount==1&&
                E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat is 1 or 3)==2&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==applied.UseId)==1,
                "The original Use resolves both genuine native target slots and finishes once across cold target selection.");
            if(scene=="tail")
            {
                var turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==turn));
                Need(g.State.TurnNumber==turn&&!E<ActualTurnCursorReturnedEvent>(g).Any(e=>e.ActualTurn==turn),"Dianjun starts a genuine tail Play after raw TurnEnded while the same actual turn cursor remains active.");
                Slash(g,1);Play(g);Need(E<YangFuStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveBingzhongExtraTargets)==1,"The tail Play does not reset whole-actual-turn uses or reissue the first-X allowance.");
            }
            _=Cold(g,r);
        }
    }
    public static void BingzhongNormalTargetPoliciesAndTypedTailCold()
    {
        foreach(var scene in new[]{"draw-two","peach","alcohol","surprise","issued-distance"})
        {
            var(g,r)=Start(scene);if(scene=="peach"){Use(g,"hurt");Play(g);Use(g,"hurt-other",[1]);Play(g);}
            var kind=scene=="draw-two"?LegalActionKind.DrawTwo:scene=="peach"?LegalActionKind.Peach:scene=="alcohol"?LegalActionKind.Alcohol:scene=="issued-distance"?LegalActionKind.Snatch:LegalActionKind.UnexpectedAssault;
            Native(g,a=>a.Kind==kind&&(scene!="surprise"||a.TargetSeats.SequenceEqual([1])));Reach(g,()=>Targets(g));
            Need(scene!="peach"||P(g)!.Choices.All(c=>c.Targets.Count==0||c.Targets.SequenceEqual([1])),"Peach's extra target must be genuinely wounded; the count allowance never bypasses native recovery legality.");
            if(scene=="issued-distance")Need(P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([2]))&&g.GetCombatDistance(0,2)>1,"The true first-use distance receipt remains valid after its Snatch material has been paid to Processing.");
            g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([scene=="surprise"?3:scene=="issued-distance"?2:1]));if(Targets(g))FinishTargets(g);Play(g);
            var applied=E<BingzhongTargetsAppliedEvent>(g).Single();Need(applied.AddedCount==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==applied.UseId)==1,"Each selected ordinary native card completes all original and extra target effects once.");
            if(scene=="draw-two")Need(g.CardMovements.Count(m=>m.To==CardLocation.Hand(1)&&m.From==CardLocation.DrawPile)>=2,"The normalized implicit-self Draw Two keeps its original draw and performs the extra target's real native draw.");
            if(scene=="peach")Need(E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat is 0 or 1)==2,"Both real wounded targets use the original native recovery continuation.");
            if(scene=="alcohol")Need(V(g,0).HasAlcoholEffect&&V(g,1).HasAlcoholEffect,"The original native Alcohol effect is applied separately to each legal target without replacing its simple-card continuation.");
            if(scene=="surprise")Need(E<UnexpectedAssaultRevealedEvent>(g).Length==2,"Unexpected Assault retains both genuine opaque Hand selections and their original native reveal/damage effects.");
            _=Cold(g,r);
        }
    }
    public static void SuyiNativeStockEquipmentChildrenAndSlashCold()
    {
        foreach(var scene in new[]{"suyi-crossbow","suyi-lion","suyi-ox","suyi-empty-hand","suyi-fire","suyi-no-stock","suyi-range","suyi-quota","suyi-abolished"})
        {
            var(g,r)=Start(scene);int? replaced=null,grain=null;
            if(scene=="suyi-lion"){Use(g,"equip-peer",[1]);Play(g);replaced=V(g,1).Equipment.Single().Id;Use(g,"hurt-other",[1]);Play(g);}
            if(scene=="suyi-ox")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);replaced=V(g,0).Equipment.Single().Id;grain=V(g,0).Hand[0].Id;
                Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[1],g.Revision,P(g)!.PromptId));Play(g);
                var ownerGrain=V(g,1).WoodenOxGrain;var thirdGrain=g.CreateSnapshot(2).Players[1].WoodenOxGrain;
                Need(V(g,1).Equipment.Any(c=>c.Id==replaced)&&ownerGrain?.Any(c=>c.Id==grain)==true&&(thirdGrain?.Count??0)==0,
                    "The real native Ox transfer moves its stored original grain to the foreign actor without exposing any grain entity to another viewer: "+JsonSerializer.Serialize(new{EquipmentMatches=V(g,1).Equipment.Any(c=>c.Id==replaced),OwnerGrain=ownerGrain?.Select(c=>c.Id),ThirdGrain=thirdGrain?.Select(c=>c.Id)}));
            }
            if(scene is"suyi-empty-hand"or"suyi-fire"){Use(g,"empty");Play(g);}
            if(scene=="suyi-quota"){Slash(g,1);Play(g);}
            var hp=V(g,1).Hp;var target=scene=="suyi-range"?2:1;SuyiOn(g,target);
            if(scene=="suyi-lion")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);var origin=E<SuyiNativeIssuedEvent>(g).Single().Origin;
                Need(V(g,1).Hp==hp+1&&!E<SuyiNativeIssuedEvent>(g).Any(e=>e.Origin.Kind==CardKind.Slash)&&g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==origin.UseId),"The real replaced Lion recovers under the foreign native equipment Use before Suyi issues its Slash.");g=Cold(g,r);
            }
            if(scene=="suyi-fire"){Reach(g,()=>Action(g,"current-slash-fire"));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="convert");}
            if(scene is"suyi-empty-hand"or"suyi-fire")
            {
                Reach(g,()=>Targets(g));var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YangFu?.Stage==YangFuStage.Targets);
                Need(f.YangFu!.Ordinal==1&&f.YangFu.X==2&&E<SuyiNativeIssuedEvent>(g).Count(e=>e.Origin.Kind==CardKind.Slash)==1,"The foreign equipment Use belongs to its real actor; Suyi's own zero-entity Slash is the owner's first Use and can receive Bingzhong.");
                g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));if(Targets(g))FinishTargets(g);
            }
            Play(g);var started=E<YangFuStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash);var native=E<SuyiNativeIssuedEvent>(g).Where(e=>e.Origin.ProgramFrameId==started.FrameId).Select(e=>e.Origin).ToArray();
            var noEquipment=scene is"suyi-no-stock"or"suyi-range"or"suyi-quota"or"suyi-abolished";
            Need(native.Count(o=>o.Kind!=CardKind.Slash)==(noEquipment?0:1)&&E<SuyiNoEquipmentEvent>(g).Count(e=>e.FrameId==started.FrameId)==(noEquipment?1:0),"The deterministic native stock selection uses one legal equipment or records real empty stock; it never manufactures a material.");
            var noSlash=scene is"suyi-range"or"suyi-quota";Need(native.Count(o=>o.Kind==CardKind.Slash)==(noSlash?0:1)&&native.All(o=>E<SuyiNativeReturnedEvent>(g).Count(e=>e.Origin==o)==1),"The following native zero-entity Slash obeys current ordinary range/quota and every issued whole native child returns once.");
            if(!noEquipment)Need(native.Single(o=>o.Kind!=CardKind.Slash)is{ActorSeat:1,ProviderSeat:1,CardId:>0}eq&&g.CardMovements.Count(m=>m.CardId==eq.CardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing)==1,"The selected foreign actor really uses its original random stock entity once instead of receiving it into Hand.");
            if(grain is{}gr)Need(g.CardMovements.Count(m=>m.CardId==gr&&m.From==CardLocation.WoodenOxGrain(1)&&m.To==CardLocation.DiscardPile)==1,"Replacement really spills the original foreign Ox grain through native cleanup once.");
            if(scene is"suyi-empty-hand"or"suyi-fire")Need(E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat is 1 or 3)==2&&E<CardUseDebitRecordedEvent>(g).Count(e=>e.Debit.ActorSeat==0&&e.Debit.Query==SkillRuleQuery.SlashLimit)==1,"The one paid Suyi Slash resolves both native targets, charges its normal quota only once, and fully returns to its original activation.");
            if(scene=="suyi-fire")Need(E<ProgramCurrentSlashFireChangedEvent>(g).Length==1&&E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.Nature==DamageNature.Fire)==2,"The native committed fire conversion keeps Suyi's exact original zero-material invoice while its added conversion source and both genuine fire targets complete.");
            var before=State(g);Need(!g.Submit(new UseProgramSkillCommand(0,Suyi,"equipment-then-slash",[],[target],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==before,"Suyi's actual once-per-Play activation cannot be paid or issued twice.");_=Cold(g,r);
        }
    }
    public static void BingzhongDamageDrawAndSuyiPaidLossDeathCold()
    {
        foreach(var scene in new[]{"draw","draw-loss","draw-death","suyi-loss","suyi-death","suyi-target-death"})
        {
            var(g,r)=Start(scene);var draw=scene.StartsWith("draw",StringComparison.Ordinal);
            if(draw){Use(g,"empty");Play(g);Use(g,"damage-self");}
            else SuyiOn(g,1);
            if(scene is"draw-loss"or"suyi-loss")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);
            }
            if(scene is"draw-death"or"suyi-death")
            {
                Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.YangFu is not null));
                Need(g.State.Winner==Winner.None&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&E<YangFuCompletedEvent>(g).Length==1,"A real non-Lord source death drains the paid native subtree and completes the owning instruction once without a dead actor's new Use.");
            }
            else Play(g);
            if(draw)
            {
                var paid=E<BingzhongDrawPaidEvent>(g).Single();Need(paid.X==2&&paid.ActualCount==2&&E<DamageAppliedEvent>(g).Count(e=>e.TargetSeat==0&&e.Amount==2)==1&&
                    g.CardMovements.Count(m=>m.Reason.Value=="skill-program.yang-fu.bingzhong.draw"&&m.To==CardLocation.Hand(0))==2,
                    "One actual two-point damage occurrence recalculates X with the genuinely empty owner, then performs one native draw of X rather than one draw per damage point.");
                if(scene=="draw-loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Bing)&&E<BingzhongDrawPaidEvent>(g).Length==1,"The genuine gained-card child removes the conditional source after payment; the draw never repeats across skill loss or cold replay.");
            }
            else
            {
                var native=E<SuyiNativeIssuedEvent>(g).Select(e=>e.Origin).ToArray();Need(native.Count(o=>o.Kind!=CardKind.Slash)==1&&native.Count(o=>o.Kind==CardKind.Slash)==(scene is"suyi-death"or"suyi-target-death"?0:1),"Paid equipment completes before the surviving original actors' remaining Slash, while a genuinely dead actor/recipient prevents new issuance.");
                if(scene=="suyi-loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Suyi)&&native.Count(o=>o.Kind==CardKind.Slash)==1,"The accepted original Suyi activation continues its true zero-entity Slash after losing the original skill instance inside native equipment children.");
                if(scene=="suyi-target-death")Need(!V(g,1).IsAlive&&native.All(o=>E<SuyiNativeReturnedEvent>(g).Count(e=>e.Origin==o)==1),"A target that dies in its actual equipment-use child leaves no orphan equipment or phantom follow-up Slash.");
            }
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static int? Ordinal(GameEngine g,CardActionContext a)=>(int?)typeof(GameEngine).GetMethod("CapturedWholeActualTurnActorUseOrdinal",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,[a]);
    private static bool Action(GameEngine g,string a)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==a)==true;
    private static bool Targets(GameEngine g)=>Action(g,"bingzhong-target");
    private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Yang Fu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g);var c=p?.Choices.FirstOrDefault(pick);Need(p?.PlayerSeat==0&&c is not null,"Missing actual human Yang Fu choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p!.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void FinishTargets(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void SuyiOn(GameEngine g,int target)=>Accept(g,new UseProgramSkillCommand(0,Suyi,"equipment-then-slash",[],[target],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().FirstOrDefault(pick);Need(a is not null,"Missing actual native Yang Fu action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Slash(GameEngine g,int target)=>Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([target]));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},caller,line);
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Yang Fu ended at {caller}:{line}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(Targets(g))FinishTargets(g);
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.Kind==DecisionKind.FireAttackDiscard)Answer(g,c=>c.Cards.Count==0);
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Kind==DecisionKind.SelectTargetCard)Answer(g,c=>true);
            else throw new InvalidOperationException($"Unexpected Yang Fu {caller}:{line}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Yang Fu {caller}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.YangFu?.Stage}),Tail=g.Events.TakeLast(5).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold restore preserves every permitted view, declaration prefix, frozen X, native material and typed child return.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"draw-death"or"suyi-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-yang-fu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:yang-fu");if(scene=="tail")CharacterFixtureContent.Register(b,"ol:zhang-yi");
            var kind=scene=="draw-two"?CardKind.DrawTwo:scene=="peach"?CardKind.Peach:scene=="alcohol"?CardKind.Alcohol:scene=="surprise"?CardKind.UnexpectedAssault:scene=="issued-distance"?CardKind.Snatch:
                scene=="suyi-lion"?CardKind.SilverLion:scene=="suyi-ox"?CardKind.WoodenOx:scene is"suyi-no-stock"or"suyi-range"?CardKind.Dodge:scene=="suyi-quota"?CardKind.Slash:CardKind.Crossbow;
            var text=char.ToLowerInvariant(kind.ToString()[0])+kind.ToString()[1..];
            static string A(string id,string effects,int targets=0)=>$$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"otherLiving","usesPerTurn":null,"effects":[{{effects}}]}""";
            var acts=new[]{A("empty","""{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}"""),A("hurt","""{"op":"loseHp","target":"owner","amount":2}"""),
                A("hurt-other","""{"op":"loseHp","target":"selectedTarget","amount":2}""",1),A("damage-self","""{"op":"damage","target":"owner","amount":2}"""),
                A("incoming","""{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""",1),A("virtual","""{"op":"useVirtualSlash","target":"selectedTarget"}""",1),
                A("equip-peer","""{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}""",1)};
            var conversions=new List<object>();if(kind!=CardKind.Slash)conversions.Add(new{id="slash",inputKinds=new[]{text},inputSuits=Array.Empty<string>(),outputKind="slash",forPlay=true,forResponse=false});
            if(kind!=CardKind.Dodge)conversions.Add(new{id="dodge",inputKinds=new[]{text},inputSuits=Array.Empty<string>(),outputKind="dodge",forPlay=false,forResponse=true});
            var view=conversions.Count==0?"":"\"viewAs\":"+JsonSerializer.Serialize(conversions)+",";
            var extraQuota=scene=="actor"?""",{"id":"two-real-debits","query":"slashLimit","operation":"add","value":1,"priority":0}""":"";
            var defs=new List<string>{$$"""{"id":"{{Driver}}","revision":1,{{view}}"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}{{extraQuota}}],"activations":[{{string.Join(',',acts)}}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(scene=="actor")defs.Add("""{"id":"fixture:yfu-actor","revision":1,"triggers":[{"id":"real-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":1000,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
            if(scene=="issued-distance")defs.Add("""{"id":"fixture:yfu-first-distance","revision":1,"cardPolicies":[{"id":"true-first-use-distance","kind":"firstActualPlayUseDistanceUnlimited"}]}""");
            if(scene=="suyi-fire")defs.Add("""{"id":"fixture:yfu-fire","revision":1,"triggers":[{"id":"real-fire","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}""");
            if(scene=="suyi-abolished")defs.Add("""{"id":"fixture:yfu-abolished","revision":1,"triggers":[{"id":"actual-area","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]}]}""");
            if(scene is"draw-loss"or"suyi-loss")defs.Add($$"""{"id":"fixture:yfu-conditional","revision":1,"hpSkillGrants":[{"id":"actual-source","skillId":"{{(scene=="draw-loss"?Bing:Suyi)}}","minimumHp":{{(scene=="draw-loss"?11:13)}}}]}""");
            if(scene.StartsWith("draw",StringComparison.Ordinal))
            {
                var amount=scene=="draw-loss"?1:scene=="draw-death"?20:0;
                defs.Add($$"""{"id":"fixture:yfu-gain","revision":1,"triggers":[{"id":"actual-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.yang-fu.bingzhong.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{(amount==0?"":",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+amount+"}")}}]}]}""");
            }
            if(scene is"draw-loss"or"suyi-loss")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-health-child","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"loss"},"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""");
            if(scene=="suyi-lion")defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"recovery"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lion","options":[{"id":"continue"}]}]}]}""");
            if(scene is"suyi-loss"or"suyi-death")defs.Add($$"""{"id":"fixture:yfu-equipment-child","revision":1,"triggers":[{"id":"actual-equipment-finalization","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"loseHp","target":"selectedTarget","amount":{{(scene=="suyi-loss"?1:20)}}}]}]}""");
            if(scene=="suyi-target-death")defs.Add("""{"id":"fixture:yfu-equipment-child","revision":1,"triggers":[{"id":"actual-equipment-owner-death","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="杨阜原生真实命令小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"杨阜原生真实用牌与付款子链"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:yfu-pick","真实杨阜","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var own=new List<string>{Driver};own.Add(scene=="draw-loss"?"fixture:yfu-conditional":Bing);own.Add(scene=="suyi-loss"?"fixture:yfu-conditional":Suyi);
            if(scene.StartsWith("draw",StringComparison.Ordinal))own.Add("fixture:yfu-gain");if(scene is"draw-loss"or"suyi-loss")own.Add(Watch);if(scene=="actor")own.Add("fixture:yfu-actor");if(scene=="tail")own.Add("ol:dianjun");if(scene=="issued-distance")own.Add("fixture:yfu-first-distance");if(scene=="suyi-fire")own.Add("fixture:yfu-fire");
            b.AddGeneral(new(Owner,"实际杨阜","ol-yang-fu","fixture:yfu-pick","wei",12,own,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:yfu-peer-{i}").ToArray();foreach(var peer in peers)
            {
                var extra=new List<string>();if(scene=="suyi-lion")extra.Add(Health);if(scene=="suyi-abolished")extra.Add("fixture:yfu-abolished");if(scene is"suyi-loss"or"suyi-death"or"suyi-target-death")extra.Add("fixture:yfu-equipment-child");
                b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"qun",12,extra));
            }
            b.AddCard(new("fixture:yfu-card","真实"+kind,CardCatalog.Get(kind).CategoryName,"固定原生实体",kind));
            b.AddDeck(new("fixture:yfu-deck","杨阜同质真实小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(_=>new ContentDeckPhysicalCard("fixture:yfu-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"杨阜真实实际回合小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:yfu-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
