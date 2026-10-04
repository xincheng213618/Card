using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryZhugeKeChecks
{
    private const string Mode="identity:classic-ordinary-zhuge-ke-fixture", Driver="fixture:zk-driver";
    private const string Hp="fixture:zk-hp", Pulse="fixture:zk-pulse", Loss="fixture:zk-loss";
    private const string CostReason="program.dynamic-target-hp.discard";

    public static void EmptyHandDodgeViewsFourPrivatelyAndPaysOriginalResponseOnce()
    {
        var (g,r)=Create("dodge");var peer=Peer(g);Empty(g,peer);
        Require(g.CreateSnapshot(peer).Players[peer].Hand.Count==0,"Actual foreign discard leaves an empty-hand responder, without writing host state.");
        Slash(g,peer);Reach(g,p=>p.PlayerSeat==peer && p.Kind==DecisionKind.RespondDodge && DeckActivate(p));
        var original=P(g)!;var parent=g.ResolutionStack.OfType<CardUseFrame>().Last();
        Answer(g,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");
        var view=View(g);var top=view.CardIds.ToArray();
        Require(view.Receipt.ViewedCount==4 && view.OriginalDecision.PromptId==original.PromptId &&
            view.Receipt.OwnerFrameId==parent.Id && g.ResolutionStack.Any(f=>f.Id==view.ParentFrameId && f is ResponseWindowFrame),
            "No ordinary Dodge still publishes the exact original response; empty hand views four with its real response-window parent.");
        Private(g,view);Reject(g);Freeze(view);g=Cold(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="card");Play(g);
        var paid=Facts<RequestedDeckBasicPaidEvent>(g).Single(e=>e.OwnerFrameId==parent.Id);
        Require(paid.ActorSeat==peer && paid.CardKind==CardKind.Dodge && top.Contains(paid.CardId) &&
            g.CardMovements.Count(m=>m.CardId==paid.CardId && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing)==1 &&
            g.CardMovements.All(m=>!top.Where(id=>id!=paid.CardId).Contains(m.CardId) || m.From!=CardLocation.DrawPile) &&
            Facts<CardRespondedEvent>(g).Count(e=>e.ResponderSeat==peer && e.CardId==paid.CardId && e.EffectiveCardKind==CardKind.Dodge)==1,
            "The same native response pays one real DrawPile material; unchosen top entities stay in their original order/zone.");
        g=Cold(g,r);
        var (decline,dr)=Create("dodge");var declinedPeer=Peer(decline);Empty(decline,declinedPeer);Slash(decline,declinedPeer);
        Reach(decline,p=>p.PlayerSeat==declinedPeer && DeckActivate(p));Answer(decline,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");
        var request=View(decline).Receipt;decline=Cold(decline,dr);Answer(decline,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="decline");
        Require(!DeckActivate(P(decline)!),"A declined private view returns to the same native choice without offering another look.");
        Play(decline);
        Require(Facts<RequestedDeckBasicViewedEvent>(decline).Count(e=>e.OwnerFrameId==request.OwnerFrameId)==1 &&
            !Facts<RequestedDeckBasicPaidEvent>(decline).Any(e=>e.OwnerFrameId==request.OwnerFrameId),
            "Native AI completes the original failed response after decline, without paying or opening the same request again.");
        decline=Cold(decline,dr);
    }

    public static void EmptyHandRequestedSlashAndDyingPeachUseKeepOriginalProducers()
    {
        var (g,r)=Create("slash");var peer=Peer(g);Empty(g,peer);Use(g,"request",[peer]);
        Reach(g,p=>p.PlayerSeat==peer && DeckActivate(p));Answer(g,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");
        var view=View(g);var parent=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==view.Receipt.OwnerFrameId);
        Require(view.Receipt.Intent==RequestedDeckBasicIntent.ProgramSlash && view.Receipt.ViewedCount==4 && parent.SkillId==Driver,
            "The zero-hand forced Slash keeps its exact suspended request program, rather than inserting a temporary hand card.");
        Private(g,view);g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="card");Play(g);
        var paid=Facts<RequestedDeckBasicPaidEvent>(g).Single(e=>e.OwnerFrameId==parent.Id);
        Require(Facts<CardUseDeclaredEvent>(g).Count(e=>e.SourceSeat==peer && e.CardId==paid.CardId && e.CardKind==CardKind.Slash)==1 &&
            g.CardMovements.Count(m=>m.CardId==paid.CardId && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing)==1 &&
            g.CardMovements.All(m=>m.CardId!=paid.CardId || m.To!=CardLocation.Hand(peer)),
            "The existing requested-Slash producer issues a genuine use with one DrawPile cost and its original typed program return.");g=Cold(g,r);

        var (rescue,rr)=Create("peach",peerHp:1,rescueHpObserver:true);var victim=Peer(rescue);Empty(rescue,victim);Use(rescue,"damage",[victim]);
        Reach(rescue,p=>p.Kind==DecisionKind.RescueDying && p.PlayerSeat==victim && DeckActivate(p));
        var dying=rescue.ResolutionStack.OfType<DyingFrame>().Single(d=>d.VictimSeat==victim);
        Answer(rescue,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");var rescueView=View(rescue);
        Require(rescueView.Receipt.Intent==RequestedDeckBasicIntent.Dying && rescueView.Receipt.OwnerFrameId==dying.Id && rescueView.CardIds.Count==4,
            "A zero-hand native rescue publishes an Aocai need before auto-failure and owns the private view on the real Dying cursor.");
        Private(rescue,rescueView);rescue=Cold(rescue,rr);Answer(rescue,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="card");
        Reach(rescue,p=>p.SkillPrompt?.SkillId==Hp && p.PlayerSeat==victim);
        var use=rescue.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.DyingResponse?.ResolutionId==dying.Id);
        var material=rescue.ResolutionStack.OfType<DyingFrame>().Single(d=>d.Id==dying.Id).RequestedDeckBasicMaterial!;
        Require(use.Action is { } action && action.ActorSeat==victim && action.PhysicalCards is [var cost] &&
            cost.CardId==material.SelectedCardId && cost.From==CardLocation.DrawPile && material.PaidMovementSequence is not null &&
            rescue.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h=>h.Change.TargetSeat==victim),
            "The real Peach recovery child retains the original DyingResponse use and full DrawPile provenance until its HP observer returns.");
        rescue=Cold(rescue,rr);Continue(rescue);Play(rescue);
        Require(Facts<DyingResolvedEvent>(rescue).Single(e=>e.ResolutionId==dying.Id).Survived &&
            Facts<RequestedDeckBasicPaidEvent>(rescue).Count(e=>e.OwnerFrameId==dying.Id)==1,
            "Actual Peach/HP children resolve the same Dying once; cold continuation cannot repay the top card.");rescue=Cold(rescue,rr);
    }

    public static void FrozenSilverLionCostDrainsChildrenAndSurvivorPenaltyDisablesCurrentTurn()
    {
        var (g,r)=Create("silver",peerHp:1,pulse:true);var target=Peer(g);Equip(g);var lion=g.CreateSnapshot(0).Players[0].Equipment.Single();
        var hp=g.State.Players[0].Hp;Duwu(g,target);Pay(g,lion.Id);Reach(g,p=>p.SkillPrompt?.SkillId==Hp && p.PlayerSeat==0);
        var root=Root(g);var receipt=root.DynamicDiscardDamage!;
        Require(receipt.Stage==DynamicDiscardDamageStage.Paid && receipt.FrozenTargetHp==1 && receipt.CardIds.SequenceEqual(new[]{lion.Id}) &&
            receipt.OriginalLocations.SequenceEqual(new[]{CardLocation.Equipment(0)}) && g.State.Players[0].Hp==hp+1 && g.State.Players[target].Hp==1 &&
            !Facts<DamageRequestedEvent>(g).Any(e=>e.SourceSeat==0 && e.TargetSeat==target) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h=>h.Change.ParentFrameId==root.Id && h.Continuation==PostEventContinuation.AwaitedProgramMovement),
            "The complete frozen cost heals through SilverLion and its real HP child before issuing damage; loss of range cannot reprice it.");
        Reject(g);g=Cold(g,r);Continue(g);Reach(g,p=>p.SkillPrompt?.SkillId==Pulse);
        var exactRoot=Root(g);var exactDamage=g.ResolutionStack.OfType<DamageFrame>().Single(d=>d.Id==exactRoot.DynamicDiscardDamage!.DamageFrameId);
        var dying=g.ResolutionStack.OfType<DyingFrame>().Single(d=>d.Id==exactRoot.DynamicDiscardDamage!.DyingFrameId);
        Require(exactDamage.ParentFrameId==root.Id && exactDamage.TargetSeat==target && exactDamage.Amount==1 && dying.ParentFrameId==exactDamage.Id &&
            exactRoot.DynamicDiscardDamage!.DyingSurvived is null && g.State.Players[0].Hp==hp+1,
            "Only the Dying caused by this one native damage is recorded, and the survivor penalty waits for its actual completion.");
        g=Cold(g,r);Continue(g);Play(g);
        var penalty=Facts<DynamicDiscardDamagePenaltyEvent>(g).Single();
        Require(penalty.FrameId==root.Id && penalty.DamageFrameId==exactDamage.Id && penalty.DyingFrameId==dying.Id &&
            Facts<DyingResolvedEvent>(g).Single(e=>e.ResolutionId==dying.Id).Survived && g.State.Players[0].Hp==hp &&
            g.GetHumanLegalActions().All(a=>a.SkillId!="ol:duwu") &&
            g.CardMovements.Count(m=>m.CardId==lion.Id && m.Reason.Value==CostReason)==1 &&
            Facts<ProgramSkillResolvedEvent>(g).Any(e=>e.SkillId=="ol:duwu" && e.Completed),
            "A true survivor costs one HP and disables this skill for the actual turn, after exactly one physical cost and one damage/Dying return.");
        g=Cold(g,r);
    }

    public static void NativeDeclinedRescueAndPaidSourceLossFinishWithoutRepayment()
    {
        var (g,r)=Create("slash",peerHp:1);var target=Peer(g);Duwu(g,target);Pay(g,P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="card").Cards.Single());
        Reach(g,p=>p.Kind==DecisionKind.RescueDying && p.PlayerSeat!=0 && DeckActivate(p));
        var original=P(g)!;Answer(g,c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");var view=View(g);
        Require(view.Receipt.ViewedCount==2 && P(g)!.Choices.Count==1 && P(g)!.Choices.Single().Parameters.GetValueOrDefault("deck-basic-source")=="decline",
            "The native responder has hand cards but the top two printed Slashes cannot satisfy the original Peach/Alcohol rescue.");
        // Native AI consumes this private decline then the original let-die route.
        g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));
        Until(g,()=>!g.ResolutionStack.OfType<DyingFrame>().Any());Play(g);
        Require(!g.State.Players[target].IsAlive && !Facts<DynamicDiscardDamagePenaltyEvent>(g).Any() &&
            Facts<RequestedDeckBasicViewedEvent>(g).Count(e=>e.OwnerFrameId==view.Receipt.OwnerFrameId && e.ActorSeat==original.PlayerSeat)==1 &&
            !Facts<RequestedDeckBasicPaidEvent>(g).Any(e=>e.OwnerFrameId==view.Receipt.OwnerFrameId) &&
            g.GetHumanLegalActions().Any(a=>a.SkillId=="ol:duwu"),
            "All native failed Aocai rescue requests advance their original responder cursor once; dead target neither penalizes nor disables Duwu.");g=Cold(g,r);

        var (lost,lr)=Create("slash",peerHp:2,sourceLoss:true);var other=Peer(lost);Duwu(lost,other);
        var ids=P(lost)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("branch")=="card").Take(2).Select(c=>c.Cards.Single()).ToArray();
        Pay(lost,ids[0]);Pay(lost,ids[1]);Reach(lost,p=>p.SkillPrompt?.SkillId==Loss);var paidRoot=Root(lost);lost=Cold(lost,lr);Continue(lost);
        Reach(lost,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target" && c.Targets.SequenceEqual(new[]{0})));
        Answer(lost,c=>c.Parameters.GetValueOrDefault("program-action")=="select-target" && c.Targets.SequenceEqual(new[]{0}));Play(lost);
        Require(lost.State.Players[other].Hp==1 && Facts<DynamicDiscardDamagePaidEvent>(lost).Single().FrameId==paidRoot.Id &&
            ids.All(id=>lost.CardMovements.Count(m=>m.CardId==id && m.Reason.Value==CostReason)==1) &&
            Facts<ProgramSkillResolvedEvent>(lost).Any(e=>e.SkillId=="ol:duwu" && e.Completed),
            "A real cost-movement observer suppresses the source after atomic payment, while the exact paid tail still deals its frozen damage and completes without repayment.");lost=Cold(lost,lr);
        StrictContracts();
    }

    private static void Freeze(RequestedDeckBasicFrame view)
    {
        var caller=view.CardIds.ToList();var clone=view with {CardIds=caller};caller.Clear();
        var json=JsonSerializer.Deserialize<RequestedDeckBasicFrame>(JsonSerializer.Serialize(clone))!;
        Require(clone.CardIds.Count==view.CardIds.Count && clone.CardIds is IList<int> {IsReadOnly:true} && json.CardIds is IList<int> {IsReadOnly:true} &&
            json.OriginalDecision.Choices is IList<PromptChoice> {IsReadOnly:true},"Constructor/init/with/JSON all freeze top IDs and the complete original prompt choice collections.");
    }
    private static void Private(GameEngine g,RequestedDeckBasicFrame view)
    {
        Require(P(g)!.IsPrivate && g.CreateSnapshot(view.Receipt.ActorSeat).PrivateRevealedCards!.Select(c=>c.Id).SequenceEqual(view.CardIds),"Only the acting viewer receives the actual ordered top cards.");
        for(var seat=0;seat<4;seat++)if(seat!=view.Receipt.ActorSeat)Require(g.CreateSnapshot(seat).PendingDecision is null &&
            g.CreateSnapshot(seat).PrivateRevealedCards is null,"Other prepared views contain neither private card choices nor looked-at IDs.");
    }
    private static void StrictContracts()
    {
        var rejected=false;try { SkillProgramCatalog.Load($$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:zk-invalid","revision":1,"triggers":[{"id":"bad","window":"afterDamageApplied","subject":"owner","optional":false,"effects":[{"op":"discardTargetHpCardsAndDamage","target":"selectedTarget"}]}]}]}
        """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{["fixture:zk-invalid"]=new{name="拒收",description="新操作仅独立主动入口"}}})); }
        catch(InvalidOperationException){rejected=true;}Require(rejected,"The standalone paid-damage instruction cannot silently appear in an arbitrary trigger recipe.");
    }
    private static RequestedDeckBasicFrame View(GameEngine g)=>g.ResolutionStack.OfType<RequestedDeckBasicFrame>().Single();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DynamicDiscardDamage is not null);
    private static PendingDecision? P(GameEngine g)=>g.State.PendingDecision;
    private static IEnumerable<T> Facts<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static int Peer(GameEngine g)=>g.State.Players.First(p=>p.Seat!=0 && p.IsAlive && p.Seat is 1 or 3).Seat;
    private static bool DeckActivate(PendingDecision p)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");
    private static void Empty(GameEngine g,int peer)
    {
        while(g.CreateSnapshot(peer).Players[peer].Hand.Count>0)
        {Use(g,"empty",[peer]);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));
         Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card");Play(g);}
    }
    private static void Use(GameEngine g,string binding,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,binding,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Duwu(GameEngine g,int target)=>Accept(g,new UseProgramSkillCommand(0,"ol:duwu","target-hp-cost",[],[target],g.Revision,P(g)!.PromptId));
    private static void Pay(GameEngine g,int id)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="dynamic-discard-damage" && c.Cards.SequenceEqual(new[]{id}));
    private static void Equip(GameEngine g){PlayAction(g,a=>a.Kind==LegalActionKind.Equip);Play(g);}
    private static void Slash(GameEngine g,int target)=>PlayAction(g,a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.SequenceEqual(new[]{target}));
    private static void PlayAction(GameEngine g,Func<LegalAction,bool> predicate)
    {var a=g.GetHumanLegalActions().First(predicate);var id=a.CardId??throw new InvalidOperationException("Expected physical material.");
     Accept(g,new PlayCardCommand(0,id,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate)
    {var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0 && p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {for(var i=0;i<180;i++){if(P(g)is{}p && predicate(p))return;Step(g);}throw new InvalidOperationException("Zhuge Ke exact boundary missing: "+JsonSerializer.Serialize(P(g)));}
    private static void Until(GameEngine g,Func<bool> predicate)
    {for(var i=0;i<180;i++){if(predicate())return;Step(g);}throw new InvalidOperationException("Zhuge Ke exact native return missing.");}
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
        else if(p is {PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash})Answer(g,c=>c.Cards.Count==0 && !c.Parameters.ContainsKey("deck-basic-source"));
        else if(p is {PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
        else if(p is {PlayerSeat:0,Kind:DecisionKind.DiscardCards})Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reject(GameEngine g)
    {var p=P(g)!;var before=State(g);Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("unpublished"),g.Revision)).Accepted && State(g)==before,"Rejected private choice changes no views, request, cost, facts or command journal.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected actual command.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),
        Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"Four prepared views, exact request/paid frames, movement ledger, facts and command journal cold-restore identically.");return restored;}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static (GameEngine,ContentRegistry)Create(string deck,int peerHp=6,bool pulse=false,bool sourceLoss=false,bool rescueHpObserver=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(deck,peerHp,pulse,sourceLoss,rescueHpObserver));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=4},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral && p.PlayerSeat==0);Accept(g,new SelectGeneralCommand(0,"fixture:zk-owner",g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string deck,int peerHp,bool pulse,bool sourceLoss,bool rescueHpObserver):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ordinary-zhuge-ke",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=FixtureRules.Replace("$SCHEMA$",SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var labels=new Dictionary<string,object>();foreach(var id in new[]{Driver,"fixture:zk-quiet",Hp,Pulse,Loss})labels[id]=id is Hp or Pulse or Loss?
                new{name=id,description="真正子帧",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}:(object)new{name=id,description="固定真实命令能力"};
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,program)in c.Programs)b.AddSkill(new(id,id,"原请求/费用子链小夹具"){Program=program,ProgramPresentation=c.Presentations[id],Tags=id=="fixture:zk-quiet"?SkillTag.Locked:SkillTag.None});
            b.AddSkill(new("fixture:zk-pick","固定其他角色","真实身份选将偏好"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>100000d)});
            var ownerSkills=new List<string>{"ol:aocai","ol:duwu","classic:longdan",Hp,"classic:mashu"};if(sourceLoss)ownerSkills.Add(Loss);
            b.AddGeneral(new("fixture:zk-owner","真实诸葛恪规则","supporter",Driver,"wu",6,ownerSkills){InitialHp=2});
            for(var i=1;i<4;i++)
            {var skills=new List<string>{"fixture:zk-quiet","ol:aocai"};if(pulse)skills.Add(Pulse);if(rescueHpObserver)skills.Add(Hp);
             b.AddGeneral(new($"fixture:zk-peer-{i}","实际回合外参与者","supporter","fixture:zk-pick","wu",6,skills){InitialHp=peerHp});}
            var id=deck switch{"dodge"=>"standard:dodge","peach"=>"standard:peach","silver"=>"classic:silver-lion",_=>"standard:slash"};
            b.AddDeck(new("fixture:zk-deck","固定印刷实体",6,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(_=>new ContentDeckPhysicalCard(id,Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"诸葛恪实际请求与冻结付款",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:zk-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:zk-owner","fixture:zk-peer-1","fixture:zk-peer-2","fixture:zk-peer-3"]));
        }
    }
    private const string FixtureRules="""
    {"schemaVersion":$SCHEMA$,"skills":[
      {"id":"fixture:zk-driver","revision":1,"activations":[
        {"id":"empty","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","resultBind":"removed","awaitMovementTriggers":true}]},
        {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},
        {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
      {"id":"fixture:zk-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
      {"id":"fixture:zk-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:zk-pulse","revision":1,"triggers":[{"id":"pulse","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"useVirtualDyingAlcohol","target":"owner"}]}]},
      {"id":"fixture:zk-loss","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["program.dynamic-target-hp.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}
    ]}
    """;
}
