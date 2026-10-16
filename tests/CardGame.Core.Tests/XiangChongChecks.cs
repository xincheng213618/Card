using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class XiangChongChecks
{
    private const string Guying="ol:guying",Muzhen="ol:muzhen",Owner="fixture:xc-owner",Peer="fixture:xc-peer",Driver="fixture:xc-driver",Quiet="fixture:xc-quiet",Watch="fixture:xc-watch",Health="fixture:xc-health",Mode="identity:classic-xiang-chong-fixture";
    public static void GuyingAtomicLossChoiceDebtAndCold()
    {
        foreach(var branch in new[]{"give","return"})
        {
            var(g,r)=Start("guying");Draw(g);DiscardForeign(g,1);Reach(g,()=>Stage(g,XiangChongStage.Choice));var issued=E<XiangChongGuyingInvokedEvent>(g).Single();var lost=Current(g)!.Lost!.CardId;var p=P(g)!;var state=State(g);
            Need(p.PlayerSeat==0&&p.IsPrivate&&g.CreateSnapshot(1).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"The actual turn actor alone sees and answers Guying's private compensation choice.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);Play(g);
            var move=E<XiangChongEntityMovedEvent>(g).Single(e=>e.FrameId==issued.FrameId);Need(move.To==CardLocation.Hand(1)&&(branch=="give"?move.From.OwnerSeat==0:move.CardId==lost&&move.From==CardLocation.DiscardPile),"Guying either transfers one actual random HE entity or regains its exact original discarded entity.");
            DiscardForeign(g,1);Play(g);Need(E<XiangChongGuyingInvokedEvent>(g).Length==1,"A second genuine atomic loss in the same actual turn cannot invoke another source generation of Guying.");End(g);Reach(g,()=>E<XiangChongDebtConsumedEvent>(g).Any());var debt=E<XiangChongDebtConsumedEvent>(g).Single();Need(debt.OwnerSeat==1&&debt.Count==1,"The next real preparation freezes the complete true invocation debt exactly once.");g=Cold(g,r);Reach(g,()=>E<XiangChongCompletedEvent>(g).Any(e=>e.FrameId==debt.FrameId));
            Need(E<XiangChongEntityMovedEvent>(g).Count(e=>e.FrameId==debt.FrameId&&e.To==CardLocation.DiscardPile)==1&&E<XiangChongDebtConsumedEvent>(g).Count(e=>e.OwnerSeat==1)==1,"Preparation pays exactly one legal original HE card and consumes its accumulated debt once.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("guying-multi");Draw(g);Use(g,"discard-two",[1]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            var batch=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
            Need(batch.Movements.Count==2&&batch.Movements.All(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.fixture:xc-driver.MoveBoundCards")&&!E<XiangChongGuyingInvokedEvent>(g).Any(),"The real recipient selects two original hand cards and pays one atomic two-entity discard batch, which must not be split into a single-card Guying loss.");g=Cold(g,r);Play(g);Need(!E<XiangChongGuyingInvokedEvent>(g).Any(),"The whole two-entity batch remains ineligible after its real movement observer returns.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("guying-source");Draw(g);DiscardForeign(g,1);Reach(g,()=>Stage(g,XiangChongStage.Choice));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="give");Play(g);Need(V(g,1).Skills!.All(s=>s.ContentId!=Guying),"The actual compensation-gain child removes the old HP-conditional Guying source after its true invocation.");g=Cold(g,r);Use(g,"heal",[1]);Play(g);Need(V(g,1).Skills!.Any(s=>s.ContentId==Guying),"Real recovery reacquires Guying without erasing its outstanding old-source debt.");End(g);Reach(g,()=>E<XiangChongDebtConsumedEvent>(g).Any());Need(E<XiangChongDebtConsumedEvent>(g).Single().Count==1,"Preparation consumes the genuine prior-source invocation after loss and reacquisition.");_=Cold(g,r);
        }
    }
    public static void MuzhenIndependentPlayBranchesAndNativePlacementCold()
    {
        var(g,r)=Start("plain");Draw(g);Place(g,1);Begin(g,"gift");SelectTarget(g,1);Pay(g,2);Reach(g,()=>Stage(g,XiangChongStage.ClaimChoice));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="claim");Play(g);
        Need(!Can(g,"gift")&&Can(g,"place"),"Muzhen's two true actual-Play quotas are independent, and the paid gift branch cannot be reused.");Begin(g,"place");SelectTarget(g,2);Pay(g,1);Reach(g,()=>Stage(g,XiangChongStage.ClaimChoice));var p=P(g)!;var state=State(g);Need(p.Choices.All(c=>c.Cards.Count==0)&&!g.Submit(new AnswerPromptCommand(2,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"The post-placement hand seizure uses opaque actual slots and rejects another seat.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="claim");Play(g);
        var paid=E<XiangChongBranchPaidEvent>(g);Need(paid.Length==2&&paid.Select(e=>e.Branch).SequenceEqual(["gift","place"])&&paid.Select(e=>e.PhaseToken).Distinct().Count()==1&&!Can(g,"gift")&&!Can(g,"place"),"Both branches pay their actual original entities once in the same real Play phase.");Need(E<XiangChongEntityMovedEvent>(g).Count(e=>e.Segment=="payment"&&e.To==CardLocation.Hand(1))==2&&E<XiangChongEntityMovedEvent>(g).Any(e=>e.Segment=="placement"&&e.To==CardLocation.Equipment(2)),"The first branch truly gives two cards and the second directly places one equipment, without using it.");
        End(g);Play(g);Need(Can(g,"gift")&&Can(g,"place"),"The next actual Play receives new independent branch quotas.");_=Cold(g,r);
        var(b,br)=Start("abolished");Draw(b);Begin(b,"place");Need(P(b)!.Choices.All(c=>!c.Targets.Contains(1)),"A genuinely abolished equipment area is excluded from native placement targets.");_=Cold(b,br);
    }
    public static void GuyingOriginalEquipmentUseAndProvenanceCold()
    {
        var(g,r)=Start("guying-equipment");Draw(g);Place(g,1);var id=V(g,1).Equipment.Single().Id;Use(g,"discard-equipment",[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.SequenceEqual([id]));Reach(g,()=>Stage(g,XiangChongStage.Choice));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="return");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(E<XiangChongEquipmentIssuedEvent>(g) is[var native]&&native.Origin.CardId==id&&native.Origin.From==CardLocation.DiscardPile&&!E<XiangChongEquipmentReturnedEvent>(g).Any(),"The original discarded equipment, rather than a replacement or virtual copy, enters one true native equipment Use before its genuine child.");g=Cold(g,r);Reach(g,()=>E<XiangChongEquipmentReturnedEvent>(g).Any());Play(g);
        Need(V(g,1).Equipment.Any(c=>c.Id==id)&&E<XiangChongEquipmentReturnedEvent>(g).Single().Issued&&E<CardUseDeclaredEvent>(g).Any(e=>e.CardId==id&&e.SourceSeat==1&&e.CardKind==CardKind.SilverLion),"The exact original armor returns through native target, payment, equipment and completion windows once.");_=Cold(g,r);
        foreach(var scene in new[]{"guying-converted","guying-converted-abolished"})
        {
            var(c,cr)=Start(scene);Draw(c);Place(c,1);Native(c,a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats.SequenceEqual([1,0]));Reach(c,()=>Stage(c,XiangChongStage.Choice));
            var original=Current(c)!;var material=original.Lost!.CardId;var issuer=c.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.XiangChong==original);var slash=c.ResolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Id==issuer.WindowContext?.MovementBatch?.ParentFrameId);
            Need(original.LossDestination==CardLocation.Processing&&original.Lost.Kind==CardKind.Crossbow&&slash is{SourceSeat:1,CardKind:CardKind.Slash,Action:{Type:CardActionType.Use,ActorSeat:1,EffectiveKind:CardKind.Slash}action}&&action.PhysicalCards is[var cost]&&cost.CardId==material&&cost.CardKind==CardKind.Crossbow&&cost.From==CardLocation.Hand(1)&&c.CardMovements.Any(m=>m.Sequence==original.LossSequence&&m.CardId==material&&m.CardKind==CardKind.Crossbow&&m.From==cost.From&&m.To==CardLocation.Processing),"A genuine Borrowed Sword Slash pays the printed equipment entity into Processing before its off-turn atomic Guying loss: "+JsonSerializer.Serialize(new{original.Lost,original.LossDestination,original.LossSequence,Use=slash is null?null:new{slash.Id,slash.SourceSeat,slash.CardKind,slash.Action},State=Diagnostic(c)}));
            c=Cold(c,cr);Answer(c,x=>x.Parameters.GetValueOrDefault("branch")=="return");Reach(c,()=>P(c)?.SkillPrompt?.SkillId==Watch);var claim=E<XiangChongEquipmentMaterialClaimIssuedEvent>(c).Single().Claim;
            Need(claim.Equipment.CardId==material&&claim.Equipment.From==CardLocation.Processing&&claim.OriginalUseId!=claim.Equipment.CardUseFrameId&&c.CardMovements.All(m=>m.CardId!=material||m.From!=CardLocation.Processing||m.To!=CardLocation.Processing),"The exact original Slash material enters a distinct real equipment Use with its own typed claim; no same-zone movement is fabricated.");
            c=Cold(c,cr);Reach(c,()=>E<XiangChongEquipmentReturnedEvent>(c).Any());Play(c);
            Need(E<CardActionAcceptedEvent>(c).Any(e=>e.Action.ActionId==claim.OriginalActionId&&e.Action.ActorSeat==1&&e.Action.Type==CardActionType.Use&&e.Action.EffectiveKind==CardKind.Slash&&e.Action.PhysicalCards.Any(x=>x.CardId==material))&&E<DamageAppliedEvent>(c).Any(x=>x.SourceSeat==1&&x.TargetSeat==0)&&c.ResolutionStack.All(f=>f.Id!=claim.OriginalUseId&&f.Id!=claim.Equipment.CardUseFrameId),"The original paid Slash resumes through target acceptance, damage and completion with its exact claimed material and no repeated cost.");
            if(scene=="guying-converted")Need(V(c,1).Equipment.Any(x=>x.Id==material)&&E<XiangChongEquipmentMaterialEnteredEvent>(c).Count(x=>x.Claim==claim)==1,"The nested equipment actually equips the original entity once.");
            else
            {
                var result=E<XiangChongEquipmentResultEvent>(c).Single().Result;
                Need(V(c,1).IsEquipmentAreaAbolished&&V(c,1).Equipment.Count==0&&result.Origin==claim.Equipment&&result.EntrySequence is null&&result.Termination is{Reason:"area.abolished.equipment",ActorDead:false}terminal&&c.CardMovements.Count(m=>m.CardId==material&&m.Sequence>claim.LossSequence)==1&&c.CardMovements.Any(m=>m.Sequence==terminal.Sequence&&m.CardId==material&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason.Value==terminal.Reason)&&!E<XiangChongEquipmentMaterialEnteredEvent>(c).Any()&&E<XiangChongEquipmentReturnedEvent>(c).Count(x=>x.Origin==claim.Equipment)==1,"A real finalized child clears the actual old equipment and abolishes the slot after issuance: exactly one native terminal discard replaces entry, the original Slash still completes, and cold cannot fake entry, repeat cost or return twice.");
            }
            _=Cold(c,cr);
        }
        {
            var(a,ar)=Start("guying-actor");Draw(a);Place(a,0);var replaced=V(a).Equipment.Single().Id;var grain=V(a).Hand[0].Id;Accept(a,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],a.Revision,P(a)!.PromptId));Play(a);Place(a,1);var returned=V(a,1).Equipment.Single().Id;
            Use(a,"discard-equipment",[1]);Reach(a,()=>Action(a,"select-and-move-owned-card"));Answer(a,c=>c.Cards.SequenceEqual([returned]));Reach(a,()=>Stage(a,XiangChongStage.Choice));Answer(a,c=>c.Parameters.GetValueOrDefault("branch")=="return");Reach(a,()=>P(a)?.SkillPrompt?.SkillId==Watch);
            var issued=E<XiangChongEquipmentIssuedEvent>(a).Single().Origin;Need(E<ProgramCardUseActorReplacedEvent>(a).Any(e=>e.CardUseFrameId==issued.CardUseFrameId&&e.PreviousActorSeat==1&&e.ActorSeat==0),"The actual equipment finalized window changes the native user while retaining the original Guying issuer and payer.");a=Cold(a,ar);Continue(a);Play(a);
            var result=E<XiangChongEquipmentResultEvent>(a).Single().Result;Need(result.Origin==issued&&result.ActorSeat==0&&result.ReplacedCardId==replaced&&V(a).Equipment.Any(c=>c.Id==returned)&&V(a).WoodenOxGrainCount==0&&a.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile)==1&&E<DamageAppliedEvent>(a).Any(e=>e.SourceSeat==0&&e.TargetSeat==0)&&E<XiangChongEquipmentReturnedEvent>(a).Single().Origin.ProgramFrameId==issued.ProgramFrameId,"The final native user receives the original Ox, replaces that user's actual old Ox and spills its actual grain once; the paid child returns to the original Guying frame after cold recovery.");_=Cold(a,ar);
        }
        var(h,hr)=Start("guying-moved");Draw(h);Use(h,"obtain-hand",[1]);Reach(h,()=>Action(h,"select-and-move-owned-card"));Answer(h,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand));Reach(h,()=>Stage(h,XiangChongStage.Choice));var unavailable=Current(h)!;
        Need(unavailable.LossDestination==CardLocation.Hand(0)&&E<ProgramBindingResolvedEvent>(h).Any(e=>e.SkillId=="fixture:xc-move"&&e.OwnerSeat==0&&e.Completed)&&h.CardMovements.Any(m=>m.CardId==unavailable.Lost!.CardId&&m.Sequence>unavailable.LossSequence&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.fixture:xc-move.DiscardOwnedZoneCards")&&P(h)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="return"),"An earlier actual gain observer has moved the exact lost entity out of its original Hand0 destination, so Guying cannot create or retake that unavailable entity: "+JsonSerializer.Serialize(new{unavailable.Lost,unavailable.LossDestination,unavailable.LossSequence,Claims=E<ProgramBindingResolvedEvent>(h).Where(e=>e.SkillId=="fixture:xc-move"),State=Diagnostic(h)}));h=Cold(h,hr);Answer(h,c=>c.Parameters.GetValueOrDefault("branch")=="give");Play(h);_=Cold(h,hr);
    }
    public static void PaidChildrenSourceLossDeathAndPrivacyCold()
    {
        foreach(var scene in new[]{"loss","death","lion","ox"})
        {
            var(g,r)=Start(scene);Draw(g);int?eq=null,grain=null;
            if(scene is"lion"or"ox")
            {
                Place(g,0);eq=V(g).Equipment.Single().Id;
                if(scene=="lion"){Use(g,"lose");Play(g);}else{Place(g,1);grain=V(g).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);}
                Begin(g,scene=="ox"?"gift":"place");SelectTarget(g,1);Answer(g,c=>c.Cards.SequenceEqual([eq.Value]));if(scene=="ox")Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="card");Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");
            }
            else{Place(g,1);Begin(g,"gift");SelectTarget(g,1);Pay(g,2);}
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==(scene=="lion"?Health:Watch));var frame=Current(g) is{}cur?g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.XiangChong==cur).Id:E<XiangChongBranchPaidEvent>(g).Last().FrameId;Need(E<XiangChongBranchPaidEvent>(g).Count(e=>e.FrameId==frame)==1,"The original Muzhen branch is already truly paid before its native recovery/movement child.");g=Cold(g,r);Continue(g);
            if(scene=="death")
            {Reach(g,()=>!V(g).IsAlive);Need(g.State.Winner==Winner.None&&E<XiangChongEntityMovedEvent>(g).All(e=>e.FrameId!=frame||e.Segment!="claim"),"A real non-Lord owner's death drains the paid gift subtree and stops the later seizure.");}
            else
            {Reach(g,()=>Stage(g,XiangChongStage.ClaimChoice));if(scene=="loss")Need(V(g).Skills!.All(s=>s.ContentId!=Muzhen),"The genuine HP child has removed the conditional source before its already-paid acquisition continues.");Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="claim");Play(g);Need(E<XiangChongBranchPaidEvent>(g).Count(e=>e.FrameId==frame)==1&&E<XiangChongCompletedEvent>(g).Count(e=>e.FrameId==frame)==1,"Cold native children cannot repeat their original payment or completion.");}
            if(scene=="lion")Need(V(g).Hp==V(g).MaxHp,"Real SilverLion removal heals its owner before post-placement acquisition.");
            if(scene=="ox")Need(V(g).WoodenOxGrainCount==0&&g.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile)==1&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==0),"The original Ox grain spills once and its native nested damage completes before the later hand seizure.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static XiangChongReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.XiangChong is not null)?.XiangChong;
    private static bool Stage(GameEngine g,XiangChongStage s)=>Current(g)?.Stage==s&&P(g)?.SkillPrompt?.SkillId is Guying or Muzhen;
    private static bool Action(GameEngine g,string a)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==a)==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real XiangChong command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the real human answers its own published prompt.");var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing XiangChong choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)?.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Use(GameEngine g,string id,int[]?targets=null,int[]?cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Draw(GameEngine g){Use(g,"draw");Play(g);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static bool Can(GameEngine g,string branch)=>g.GetHumanLegalActions().Any(a=>a.ProgramSkillId==Muzhen&&a.ProgramActivationId==(branch=="gift"?"gift-and-obtain-equipment":"place-and-obtain-hand"));
    private static void Begin(GameEngine g,string branch)=>Accept(g,new UseProgramSkillCommand(0,Muzhen,branch=="gift"?"gift-and-obtain-equipment":"place-and-obtain-hand",[],[],g.Revision,P(g)!.PromptId));
    private static void SelectTarget(GameEngine g,int target)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="target"&&c.Targets.SequenceEqual([target]));
    private static void Pay(GameEngine g,int count){for(var n=0;n<count;n++)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="card");Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");}
    private static void Place(GameEngine g,int target){var id=V(g).Hand.First(c=>EquipmentCatalog.IsEquipment(c.Kind)).Id;Use(g,"place",[target],[id]);Play(g);}
    private static void DiscardForeign(GameEngine g,int target){Use(g,"discard-hand",[target]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand));}
    private static void Native(GameEngine g,Func<LegalAction,bool> choose){var a=g.GetHumanLegalActions().FirstOrDefault(choose);Need(a is not null,"Missing genuine native card: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Reach(GameEngine g,Func<bool> done,[CallerLineNumber]int line=0)
    {
        for(var n=0;n<240;n++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"XiangChong completed before line {line}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else if(Stage(g,XiangChongStage.Choice))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="give");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected XiangChong prompt line {line}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded XiangChong line {line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Stage=Current(g)?.Stage,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Owners=g.CreateSnapshot(0).Players.Select(x=>new{x.Seat,Skills=x.Skills?.Where(s=>s.ContentId is Guying or Muzhen).Select(s=>s.ContentId)}),ActualLosses=g.CardMovements.Where(m=>m.From.OwnerSeat==1&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment).TakeLast(6),GuyingBindings=E<ProgramBindingStartedEvent>(g).Where(e=>e.SkillId==Guying).TakeLast(4),Invocations=E<XiangChongGuyingInvokedEvent>(g).Length,Debt=E<XiangChongDebtConsumedEvent>(g).Length,Tail=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves each private chooser, exact lost entity, original source, actual phase/debt and once-paid native return.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-xiang-chong",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:xiang-chong");var foreign=scene.StartsWith("guying",StringComparison.Ordinal);
            var root=JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":60,"priority":0}],"viewAs":[{"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"heal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"discard-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"obtain-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},
              {"id":"discard-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","amount":2,"zones":["hand"],"resultBind":"two"},{"op":"moveBoundCards","target":"owner","sourceBind":"two","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"discard-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"native-hp","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            void Add(string id,string body)=>root["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}"));
            if(scene=="loss")Add("fixture:xc-source","\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:muzhen\",\"minimumHp\":9}]");
            if(scene is"loss"or"death")Add(Watch,"\"triggers\":[{\"id\":\"paid\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"skill-program.xiang-chong.muzhen.gift\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":"+(scene=="death"?20:1)+"}]}]");
            if(scene=="ox")Add(Watch,"\"triggers\":[{\"id\":\"grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]");
            if(scene=="guying-actor")
            {
                Add("fixture:xc-role","\"triggers\":[{\"id\":\"actual-equipment-role\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardCategories\":[\"equipment\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]}]");
                Add(Watch,"\"triggers\":[{\"id\":\"actual-other-user-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]");
            }
            if(scene is"guying-equipment"or"guying-converted")Add(Watch,"\"triggers\":[{\"id\":\"native-equipment\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardCategories\":[\"equipment\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"native\",\"options\":[{\"id\":\"continue\"}]}]}]");
            if(scene=="guying-converted-abolished")Add(Watch,"\"triggers\":[{\"id\":\"actual-finalized-abolition\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardCategories\":[\"equipment\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"native\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"equipment\"]},{\"op\":\"abolishOwnerAreas\",\"target\":\"owner\",\"zones\":[\"equipment\"]}]}]");
            if(scene=="guying-multi")Add(Watch,"\"triggers\":[{\"id\":\"actual-two-owner-batch\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"skill-program.fixture:xc-driver.MoveBoundCards\"],\"movementOccurrence\":\"perOwnerBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"two\",\"options\":[{\"id\":\"continue\"}]}]}]");
            if(scene.StartsWith("guying-converted",StringComparison.Ordinal))Add("fixture:xc-equipment-slash","\"viewAs\":[{\"id\":\"equipment-slash\",\"inputKinds\":[\"crossbow\"],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false,\"useOnly\":true}]");
            if(scene=="guying-source")
            {Add("fixture:xc-peer-source","\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:guying\",\"minimumHp\":12}]");Add(Watch,"\"triggers\":[{\"id\":\"compensation-loss\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.xiang-chong.guying.gift\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"loss\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]");}
            if(scene=="guying-moved")Add("fixture:xc-move","\"triggers\":[{\"id\":\"prior-gain-movement\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.fixture:xc-driver.SelectAndMoveOwnedCard\"],\"priority\":100,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]");
            if(scene=="abolished")Add("fixture:xc-abolished","\"triggers\":[{\"id\":\"actual-area\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"abolishOwnerAreas\",\"target\":\"owner\",\"zones\":[\"equipment\"]}]}]");
            var labels=root["skills"]!.AsArray().Select(n=>n!).ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="真实原生向宠夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>n[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var cat=SkillProgramCatalog.Load(root.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,id,"真实原生夹具"){Program=p,ProgramPresentation=cat.Presentations[id]});
            var skills=new List<string>{Driver,Health};if(!foreign){skills.Add(Guying);skills.Add(scene=="loss"?"fixture:xc-source":Muzhen);}if(scene is"loss"or"death"or"ox"or"guying-actor")skills.Add(Watch);if(scene=="guying-moved")skills.Add("fixture:xc-move");
            b.AddSkill(new("fixture:xc-pick","本人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-1000000d)});b.AddGeneral(new(Owner,"真实向宠本人","ol-xiang-chong","fixture:xc-pick","shu",8,skills,GeneralGender.Male));
            var peers=new[]{Peer,"fixture:xc-b","fixture:xc-c"};foreach(var(peer,i)in peers.Select((p,i)=>(p,i))){var pick="fixture:xc-pick-"+i;b.AddSkill(new(pick,"实际其他角色","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>100000d*(3-i))});var other=new List<string>{Quiet,Health};if(peer==Peer&&foreign){other.Add(scene=="guying-source"?"fixture:xc-peer-source":Guying);if(scene.StartsWith("guying-converted",StringComparison.Ordinal))other.Add("fixture:xc-equipment-slash");if(scene=="guying-actor")other.Add("fixture:xc-role");if(scene is"guying-equipment"or"guying-source"or"guying-multi"||scene.StartsWith("guying-converted",StringComparison.Ordinal))other.Add(Watch);}if(peer==Peer&&scene=="abolished")other.Add("fixture:xc-abolished");b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"wei",12,other));}
            var kind=scene is"guying"or"guying-moved"?CardKind.Dodge:scene is"lion"or"guying-equipment"?CardKind.SilverLion:scene is"ox"or"guying-actor"?CardKind.WoodenOx:CardKind.Crossbow;
            b.AddCard(new("fixture:xc-card","真实"+kind,EquipmentCatalog.IsEquipment(kind)?"装备牌":"基本牌","真实实体",kind));b.AddDeck(new("fixture:xc-deck","固定真实小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,90).Select(i=>new ContentDeckPhysicalCard("fixture:xc-card",Suit.Spade,i%13+1)).ToArray()});var identities=new Dictionary<string,int>{[nameof(Role.Lord)]=1,[scene.StartsWith("guying-converted",StringComparison.Ordinal)?nameof(Role.Rebel):nameof(Role.Renegade)]=3};b.AddMode(new(Mode,"向宠真实原生边界",4,4,identities,"fixture:xc-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
