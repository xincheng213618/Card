using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhangZhaoZhangHongChecks
{
    private const string Mode="identity:classic-boundary-two-zhangs";
    private const string Driver="fixture:zz-driver", Zhijian="boundary:zhijian", Guzheng="boundary:guzheng";

    public static void CapturedEquipmentReplacementDrainsSourceAndRecipientChildrenBeforeDraw()
    {
        var (g,r)=Create(equipment:true);var recipient=Peer(g);
        var first=g.State.Players[0].Hand.First().Id;
        var publicPlacement=g.GetHumanActionView().LegalActions.Single(a=>a.ProgramSkillId==Zhijian &&a.ProgramActivationId=="place-equipment");
        Require(publicPlacement.SelectableCardTargets is { } associations &&associations.Contains(new(first,recipient)) &&
            associations.All(pair=>publicPlacement.SelectableCardIds.Contains(pair.CardId) &&publicPlacement.SelectableTargetSeats.Contains(pair.TargetSeat)),
            "The frozen public captured-equipment action keeps each exact payable material/recipient association.");
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

    private static IEnumerable<T> Facts<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame EquipmentRoot(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CapturedEquipmentDraw is not null);
    private static int Peer(GameEngine g)=>g.State.Players.First(p=>p.Seat!=0 &&p.Role!=Role.Lord).Seat;
    private static void Place(GameEngine g,int card,int target)=>Accept(g,new UseProgramSkillCommand(0,Zhijian,"place-equipment",[card],[target],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,p=>p is { Kind:DecisionKind.PlayCard,PlayerSeat:0 });
    private static void ContinueCurrent(GameEngine g)
    {if(P(g)!.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose)
    {var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> done)
    {for(var i=0;i<160;i++){if(P(g) is { } p &&done(p))return;Step(g);}throw new InvalidOperationException("Two Zhangs actual prompt missing: "+JsonSerializer.Serialize(P(g)));}
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
                    JsonNode.Parse("""{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"owner"},"nature":"normal"}""");
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
     {"id":"fixture:zz-discard-start","revision":1,"triggers":[{"id":"discard-at-real-start","window":"discardPhaseStarting","subject":"owner","priority":20,"optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":2,"maximumCards":2,"zones":["hand"],"resultBind":"discard-phase-pair"},{"op":"moveBoundCards","target":"owner","sourceBind":"discard-phase-pair","destination":"discardPile","awaitMovementTriggers":true}]}]},
     {"id":"fixture:zz-fragile","revision":1,"triggers":[{"id":"return-cost-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:guzheng.actual-discard-return"],"priority":20,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":2}]}]},
     {"id":"fixture:zz-pulse","revision":1,"triggers":[{"id":"actual-self-response","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"self-seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]},
     {"id":"fixture:zz-extra","revision":1,"triggers":[{"id":"real-extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}
    ]}
    """;
}
