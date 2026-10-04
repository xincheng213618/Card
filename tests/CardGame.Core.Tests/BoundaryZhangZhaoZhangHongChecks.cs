using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
namespace CardGame.Core.Tests;

internal static class BoundaryZhangZhaoZhangHongChecks
{
    private const string Mode="identity:classic-boundary-two-zhangs";
    private const string Driver="fixture:zz-driver", Zhijian="boundary:zhijian", Guzheng="boundary:guzheng";

    public static void CapturedEquipmentReplacementDrainsSourceAndRecipientChildrenBeforeDraw()
    {
        var (g,r)=Create(equipment:true);var recipient=Peer(g);
        var first=g.State.Players[0].Hand.First().Id;
        Place(g,first,recipient);Play(g);
        var equipment=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        var incoming=equipment.CardId!.Value;
        Accept(g,new PlayCardCommand(0,incoming,equipment.TargetSeats,g.Revision,P(g)!.PromptId,equipment.PlayedCardKind));Play(g);
        var beforeDraw=Facts<CapturedEquipmentRewardDrawnEvent>(g).Count();var ownerHp=g.State.Players[0].Hp;
        Place(g,incoming,recipient);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:zz-hp");
        var parent=EquipmentRoot(g);var frameId=parent.Id;var receipt=parent.CapturedEquipmentDraw!;
        Require(receipt is { Stage:CapturedEquipmentDrawStage.Placed,ReplacedCardId:var replaced } &&replaced==first &&
            receipt.OriginalFrom==CardLocation.Equipment(0) &&receipt.CardId==incoming &&
            g.CreateCardZoneDiagnostics().Single(c=>c.CardId==incoming).Location==CardLocation.Equipment(recipient) &&
            g.CreateCardZoneDiagnostics().Single(c=>c.CardId==first).Location==CardLocation.DiscardPile &&
            Facts<CapturedEquipmentRewardDrawnEvent>(g).Count()==beforeDraw,
            "The actual occupied slot is replaced once, real source equipment leaves, and its HP children precede the one draw.");
        Require(g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w=>w.Change.ParentFrameId==frameId &&
            w.Continuation==PostEventContinuation.AwaitedProgramMovement),"SilverLion removal keeps its exact paid equipment producer.");
        Reject(g);g=Cold(g,r);ContinueCurrent(g);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:zz-gain" &&Facts<CapturedEquipmentRewardDrawnEvent>(g).Any(e=>e.ProgramFrameId==frameId));
        Require(g.State.Players[0].Hp==ownerHp+1 &&Facts<CapturedEquipmentRewardDrawnEvent>(g).Single(e=>e.ProgramFrameId==frameId).Actual==1 &&
            EquipmentRoot(g).CapturedEquipmentDraw!.Stage==CapturedEquipmentDrawStage.Drawn,
            "Source and replaced SilverLion children finish before one actual reward gain; the paid root survives reward observers.");
        g=Cold(g,r);ContinueCurrent(g);Play(g);
        Require(Facts<CapturedEquipmentPlacedEvent>(g).Count(e=>e.ProgramFrameId==frameId)==1 &&
            Facts<CapturedEquipmentRewardDrawnEvent>(g).Count(e=>e.ProgramFrameId==frameId)==1 &&
            g.CardMovements.Count(m=>m.CardId==incoming &&m.From==CardLocation.Equipment(0) &&m.To==CardLocation.Equipment(recipient))==1 &&
            g.CardMovements.Count(m=>m.CardId==first &&m.Reason==CardMoveReasons.EquipmentReplace)==1 &&
            !g.ResolutionStack.Any(f=>f.Id==frameId),"Cold children cannot reselect a slot, repay source equipment, replace twice, or draw twice.");
        _=Cold(g,r);
    }

    public static void GuzhengReturnsOneThenOptionallyClaimsExactBatchAndKeepsPhaseQuota()
    {
        foreach(var (claim,damageReturn) in new[]{(false,false),(true,false),(true,true)})
        {
            var (g,r)=Create(fragileReturn:true,damageReturn:damageReturn);var recipient=Peer(g);GivePair(g,recipient);
            Activate(g,Guzheng);ReachAction(g,"actual-discard-recovery");
            var frame=DiscardRoot(g);var frameId=frame.Id;var original=frame.ActualDiscardRecovery!;
            Require(original.Phase.Kind==ActualDiscardRecoveryPhaseKind.Play &&original.OriginalEntities.Count==2 &&
                original.DiscardOwnerSeat==recipient &&P(g)!.Choices.SelectMany(c=>c.Cards).All(id=>original.OriginalEntities.Any(e=>e.CardId==id)),
                "Guzheng responds during the real Play phase to one other owner's two-card true discard batch.");
            var selected=P(g)!.Choices.First().Cards.Single();Reject(g);g=Cold(g,r);
            Answer(g,c=>c.Cards.SequenceEqual([selected]));
            Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:zz-pulse");
            Require(DiscardRoot(g).ActualDiscardRecovery is { Stage:ActualDiscardRecoveryStage.ReturnPaid,ReturnedCardId:var id } &&id==selected &&
                g.ResolutionStack.OfType<DyingFrame>().Any(d=>d.VictimSeat==recipient) &&
                Facts<ActualDiscardRecoveryReturnedEvent>(g).Count(e=>e.ProgramFrameId==frameId)==1 &&
                !Facts<ActualDiscardRecoveryClaimedEvent>(g).Any(e=>e.ProgramFrameId==frameId),
                "The returned entity is paid once; its actual gain-loss-HP-Dying child finishes before the optional remaining claim.");
            if(damageReturn)
                Require(g.ResolutionStack.OfType<DyingFrame>().Any(d=>d.VictimSeat==recipient &&d.Continuation==DyingContinuationKind.Damage) &&
                    g.ResolutionStack.OfType<DamageFrame>().Any(d=>d.TargetSeat==recipient &&d.SourceSeat==recipient) &&
                    Facts<DamageAppliedEvent>(g).Count(d=>d.SourceSeat==recipient &&d.TargetSeat==recipient &&d.Amount==1)==1,
                    "The paid return's actual Gain observer owns one native self-damage and its Damage-Dying-SelfDying chain, not a forged HP-loss stand-in.");
            g=Cold(g,r);ContinueCurrent(g);
            Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="actual-discard-recovery" &&c.Parameters.GetValueOrDefault("option")=="claim"));
            Require(g.State.Players[recipient].IsAlive &&g.State.Players[recipient].Hp==3,"Actual self-dying response revives the original return recipient.");
            var rest=original.OriginalEntities.Single(e=>e.CardId!=selected).CardId;
            g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option")== (claim ? "claim" : "skip"));
            if(claim)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:zz-gain" &&DiscardRoot(g).ActualDiscardRecovery!.Stage==ActualDiscardRecoveryStage.ClaimPaid);
                Require(g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w=>w.Batch.ParentFrameId==frameId &&
                    (w.Batch.AwaitingProgramFrameId is null ||w.Batch.AwaitingProgramFrameId==frameId) &&w.Batch.Movements.Select(m=>m.CardId).SequenceEqual([rest])),
                    "The remaining original cohort is claimed atomically on the paid parent and waits for its true gain observer.");
                g=Cold(g,r);ContinueCurrent(g);
            }
            Play(g);
            var completed=Facts<ActualDiscardRecoveryClaimedEvent>(g).Single(e=>e.ProgramFrameId==frameId);
            Require(completed.CardIds.SequenceEqual(claim ? [rest] : []) &&
                g.CreateCardZoneDiagnostics().Single(c=>c.CardId==rest).Location==(claim ? CardLocation.Hand(0) : CardLocation.DiscardPile) &&
                Facts<ActualDiscardRecoveryReturnedEvent>(g).Count(e=>e.ProgramFrameId==frameId)==1,
                "Declining leaves the exact original entity; accepting obtains it once after return children, without moving any unrelated card.");
            GivePair(g,recipient);Play(g);
            Require(Facts<ActualDiscardRecoveryBatchQualifiedEvent>(g).Count(e=>e.Phase.Token==original.Phase.Token &&e.DiscardOwnerSeat==recipient)>=2 &&
                Facts<ActualDiscardRecoveryReturnedEvent>(g).Count(e=>e.Source.OwnerSeat==0 &&e.Source.SkillId==Guzheng &&e.Phase.Token==original.Phase.Token)==1,
                "A second genuine same-phase discarded batch cannot reopen the holder's consumed phase quota.");
            _=Cold(g,r);
        }
    }

    public static void GuzhengExtraPlayHasNewPhaseTokenAndNormalDiscardHasOwnCohort()
    {
        var (g,r)=Create(extraPlay:true);var recipient=Peer(g);var turn=g.State.TurnNumber;
        GivePair(g,recipient);FinishGuzhengWithoutClaim(g);Play(g);
        var first=Facts<ActualDiscardRecoveryReturnedEvent>(g).Single(e=>e.Source.OwnerSeat==0);
        Require(first.Phase.Kind==ActualDiscardRecoveryPhaseKind.Play,"Inserted actual Play owns its initial independent token.");
        End(g);Play(g);
        Require(g.State.TurnNumber==turn &&Facts<ActualDiscardRecoveryPhaseRestoredEvent>(g).Any() &&
            Facts<ActualDiscardRecoveryPhaseStartedEvent>(g).Where(e=>e.Phase.TurnNumber==turn &&e.Phase.Kind==ActualDiscardRecoveryPhaseKind.Play).Count()==2,
            "After the extra phase finishes, its exact interrupted Preparation token restores before the new normal Play phase starts.");
        GivePair(g,recipient);Activate(g,Guzheng);ReachAction(g,"actual-discard-recovery");
        var second=DiscardRoot(g).ActualDiscardRecovery!;
        Require(second.Phase.TurnNumber==turn &&second.Phase.Token!=first.Phase.Token &&second.Phase.Kind==ActualDiscardRecoveryPhaseKind.Play,
            "A same-turn new actual Play phase grants another opportunity; usage is not tied to the old play-debit token.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="return");
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option")=="skip" &&c.Parameters.GetValueOrDefault("program-action")=="actual-discard-recovery"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="skip");Play(g);End(g);
        Activate(g,Guzheng);ReachAction(g,"actual-discard-recovery");
        var discard=DiscardRoot(g).ActualDiscardRecovery!;
        Require(discard.Phase.Kind==ActualDiscardRecoveryPhaseKind.Discard &&discard.Phase.Token!=second.Phase.Token &&
            discard.OriginalEntities.All(e=>g.CardMovements.Any(m=>m.Sequence==e.MovementSequence &&m.To==CardLocation.DiscardPile &&
                m.Reason.Value.EndsWith("DiscardOwnedZoneCards",StringComparison.Ordinal))),
            "The next real other-character Discard starting batch has its own phase token and original genuine discard ledger.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="return");
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option")=="skip" &&c.Parameters.GetValueOrDefault("program-action")=="actual-discard-recovery"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="skip");_=Cold(g,r);
    }

    public static void GuzhengNativeAiAndFrozenReceiptsPreservePublicDiscardOrigins()
    {
        var (g,r)=Create(native:true);Use(g,"grow");Play(g);End(g);
        Reach(g,p=>p is { Kind:DecisionKind.DiscardCards,PlayerSeat:0 });
        Require(P(g)!.RequiredCardCount>=2,"The human's small real HP hand limit supplies a genuine two-or-more-card Discard batch.");
        var ids=P(g)!.ValidCardIds.Take(P(g)!.RequiredCardCount).ToArray();
        Accept(g,new DiscardCardsCommand(0,ids,P(g)!.PromptId,g.Revision));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:zz-gain" &&Facts<ActualDiscardRecoveryReturnedEvent>(g).Any(e=>e.Source.OwnerSeat!=0));
        var root=DiscardRoot(g);var receipt=root.ActualDiscardRecovery!;
        var mutable=receipt.OriginalEntities.ToList();var frozen=receipt with { OriginalEntities=mutable };mutable.Clear();
        Require(frozen.OriginalEntities.Count==ids.Length &&frozen.OriginalEntities is System.Collections.IList { IsReadOnly:true } &&
            receipt.Phase.Kind==ActualDiscardRecoveryPhaseKind.Discard &&receipt.DiscardOwnerSeat==0 &&
            receipt.OriginalEntities.Select(e=>e.CardId).Order().SequenceEqual(ids.Order()) &&
            receipt.OriginalEntities.All(e=>g.CardMovements.Any(m=>m.CardId==e.CardId &&m.Sequence==e.MovementSequence &&m.Reason==CardMoveReasons.HandLimitDiscard)) &&
            root.OwnerSeat!=0,"Native AI returns a real publicly discarded human entity and the readonly receipt keeps the entire exact original batch.");
        var frameId=root.Id;g=Cold(g,r);ContinueCurrent(g);
        Until(g,()=>Facts<ActualDiscardRecoveryClaimedEvent>(g).Any(e=>e.ProgramFrameId==frameId));
        var fact=Facts<ActualDiscardRecoveryClaimedEvent>(g).Single(e=>e.ProgramFrameId==frameId);var mutableIds=fact.CardIds.ToList();
        var frozenFact=fact with { CardIds=mutableIds };mutableIds.Clear();
        Require(frozenFact.CardIds.SequenceEqual(fact.CardIds) &&frozenFact.CardIds is System.Collections.IList { IsReadOnly:true } &&
            Facts<ActualDiscardRecoveryReturnedEvent>(g).Count(e=>e.ProgramFrameId==frameId)==1 &&
            fact.CardIds.All(id=>ids.Contains(id)) &&!fact.CardIds.Contains(receipt.ReturnedCardId!.Value),
            "Native accepted return/claim pays each original once and public collection-bearing history is detached through explicit init cloning.");
        _=Cold(g,r);
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame EquipmentRoot(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CapturedEquipmentDraw is not null);
    private static ProgramSkillFrame DiscardRoot(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.ActualDiscardRecovery is not null);
    private static int Peer(GameEngine g)=>g.State.Players.First(p=>p.Seat!=0 &&p.Role!=Role.Lord).Seat;
    private static void Place(GameEngine g,int card,int target)=>Accept(g,new UseProgramSkillCommand(0,Zhijian,"place-equipment",[card],[target],g.Revision,P(g)!.PromptId));
    private static void GivePair(GameEngine g,int target)=>Use(g,"give-pair",g.State.Players[0].Hand.Take(2).Select(c=>c.Id).ToArray(),[target]);
    private static void Use(GameEngine g,string binding,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,binding,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,p=>p is { Kind:DecisionKind.PlayCard,PlayerSeat:0 });
    private static void Activate(GameEngine g,string skill)
    {Reach(g,p=>p.SkillPrompt?.SkillId==skill &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void ReachAction(GameEngine g,string action)=>Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action));
    private static void FinishGuzhengWithoutClaim(GameEngine g)
    {Activate(g,Guzheng);ReachAction(g,"actual-discard-recovery");Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="return");Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="actual-discard-recovery" &&c.Parameters.GetValueOrDefault("option")=="skip"));Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="skip");}
    private static void ContinueCurrent(GameEngine g)
    {if(P(g)!.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose)
    {var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> done)
    {for(var i=0;i<160;i++){if(P(g) is { } p &&done(p))return;Step(g);}throw new InvalidOperationException("Two Zhangs actual prompt missing: "+JsonSerializer.Serialize(P(g)));}
    private static void Until(GameEngine g,Func<bool> done)
    {for(var i=0;i<180;i++){if(done())return;Step(g);}throw new InvalidOperationException("Two Zhangs actual fact boundary missing.");}
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p is { PlayerSeat:0,Kind:DecisionKind.DiscardCards })Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is { PlayerSeat:0 } &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))ContinueCurrent(g);
        else if(p is { PlayerSeat:0 } &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else if(p is { PlayerSeat:0,Kind:DecisionKind.RescueDying })Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
        else if(p is { PlayerSeat:0,Kind:DecisionKind.PlayCard })throw new InvalidOperationException("A requested skill boundary was passed into normal Play.");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g,GameCommand c)
    {var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected actual command.");}
    private static void Reject(GameEngine g)
    {var before=State(g);var p=P(g)!;Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("not-published"),g.Revision)).Accepted &&State(g)==before,"Unpublished choices leave actual cost, benefit, phase quota and four prepared views unchanged.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new { Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"Four private/public views, exact owning frames, original movements, phase history and accepted commands cold restore identically.");return restored;}
    private static void Require(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}
    private static (GameEngine,ContentRegistry) Create(bool equipment=false,bool fragileReturn=false,bool extraPlay=false,bool native=false,bool damageReturn=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(equipment,fragileReturn,extraPlay,native,damageReturn));
        var g=GameEngine.CreateStandard(new GameOptions { Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Renegade,ModeId=Mode,
            UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=8 },r);
        Accept(g,new StartGameCommand());Reach(g,p=>p is { Kind:DecisionKind.SelectGeneral,PlayerSeat:0 });
        Accept(g,new SelectGeneralCommand(0,"fixture:zz-owner",g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(bool equipment,bool fragileReturn,bool extraPlay,bool native,bool damageReturn):IGameContentPackage
    {
        public PackageManifest Manifest { get; }=new("fixture-boundary-two-zhangs",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var data=JsonNode.Parse(FixtureRules.Replace("$SCHEMA$",SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;
            if(damageReturn)
                data["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()=="fixture:zz-fragile")!["triggers"]![0]!["effects"]![0]=
                    JsonNode.Parse("""{"op":"damage","target":"owner","amount":1,"sourceRef":"owner","nature":"normal"}""");
            var labels=new Dictionary<string,object>();
            foreach(var node in data["skills"]!.AsArray())
            {
                var id=node!["id"]!.GetValue<string>();
                labels[id]=id is Driver or "fixture:zz-hp" or "fixture:zz-gain" or "fixture:zz-pulse"
                    ? (object)new { name=id,description="实际拥有帧夹具",optionLabels=new Dictionary<string,string>{["continue"]="继续"} }
                    : new { name=id,description="实际拥有帧夹具" };
            }
            var programs=SkillProgramCatalog.Load(data.ToJsonString(),JsonSerializer.Serialize(new { schemaVersion=3,skills=labels })).Programs;
            foreach(var (id,program) in programs)b.AddSkill(new(id,id,"实际拥有帧夹具"){Program=program,Tags=id is "fixture:zz-quiet" or "fixture:zz-extra" ? SkillTag.Locked : SkillTag.None});
            b.AddSkill(new("fixture:zz-selection","固定角色","公开选将偏好"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>100000d)});
            var owner=new List<string>{Zhijian,"fixture:zz-gain","fixture:zz-hp"};if(!native)owner.Add(Guzheng);if(extraPlay)owner.Add("fixture:zz-extra");
            b.AddGeneral(new("fixture:zz-owner","当前界二张机制","supporter",Driver,"wu",native?1:3,owner){InitialHp=native?1:2});
            for(var i=1;i<4;i++)
            {
                var skills=new List<string>{"fixture:zz-quiet","fixture:zz-gain","fixture:zz-hp"};
                if(!equipment &&!native)skills.Add("fixture:zz-discard-on-gift");if(fragileReturn)skills.AddRange(["fixture:zz-fragile","fixture:zz-pulse"]);if(native)skills.Add(Guzheng);
                b.AddGeneral(new($"fixture:zz-peer-{i}","固定其他角色","supporter","fixture:zz-selection","wei",8,skills){InitialHp=fragileReturn?1:3});
            }
            b.AddDeck(new("fixture:zz-deck","固定真实实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(equipment?"classic:silver-lion":"standard:slash",Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"界二张实际命令",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:zz-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:zz-owner","fixture:zz-peer-1","fixture:zz-peer-2","fixture:zz-peer-3"]));
        }
    }
    private const string FixtureRules="""
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:zz-driver","revision":1,"activations":[
      {"id":"give-pair","usesPerTurn":null,"minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[{"op":"giveSelected","target":"selectedTarget","amount":2},{"op":"chooseOption","target":"owner","resultBind":"gift-finished","options":[{"id":"continue"}]}]},
      {"id":"grow","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":[{"op":"draw","target":"owner","amount":3}]}]},
     {"id":"fixture:zz-quiet","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
     {"id":"fixture:zz-hp","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:zz-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:zhijian.captured-equipment-reward","skill-program.boundary:guzheng.actual-discard-return","skill-program.boundary:guzheng.actual-discard-claim"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:zz-discard-on-gift","revision":1,"triggers":[{"id":"discard-two","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:zz-driver.GiveSelected"],"optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":2,"maximumCards":2,"zones":["hand"],"resultBind":"actual-two"},{"op":"moveBoundCards","target":"owner","sourceBind":"actual-two","destination":"discardPile","awaitMovementTriggers":true},{"op":"grantSkills","target":"owner","skillIds":["fixture:zz-discard-start"]}]}]},
     {"id":"fixture:zz-discard-start","revision":1,"triggers":[{"id":"discard-at-real-start","window":"discardPhaseStarting","subject":"owner","priority":20,"optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]},
     {"id":"fixture:zz-fragile","revision":1,"triggers":[{"id":"return-cost-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:guzheng.actual-discard-return"],"priority":20,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":2}]}]},
     {"id":"fixture:zz-pulse","revision":1,"triggers":[{"id":"actual-self-response","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"self-seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]},
     {"id":"fixture:zz-extra","revision":1,"triggers":[{"id":"real-extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}
    ]}
    """;
}
