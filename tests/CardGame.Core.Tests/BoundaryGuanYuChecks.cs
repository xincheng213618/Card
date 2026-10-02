using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryGuanYuChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void BlackActualPaymentChooserPrivacyAndReplay()
    {
        var(g,r)=Start("black"); var payment=g.State.Players[0].Hand.First().Id;
        var revision=g.Revision;var before=SnapshotJson.Serialize(g.CreateSnapshot(0));
        var illegal=g.Submit(new UseProgramSkillCommand(0,"boundary:yijue","discard-and-show",[payment],[0],g.Revision,P(g)!.PromptId));
        Require(!illegal.Accepted&&revision==g.Revision&&before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"Self target rejects atomically.");
        Yijue(g,payment,1);var p=P(g)!;Require(p.PlayerSeat==1&&p.IsPrivate&&p.Choices.All(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"),"Other target owns mandatory single Hand chooser.");
        Require(g.CardMovements.Count(m=>m.CardId==payment&&m.To==CardLocation.DiscardPile)==1,"Real HE cost precedes target choice once.");
        for(var viewer=0;viewer<4;viewer++)Require(viewer==1||g.CreateSnapshot(viewer).PendingDecision is null,"Other viewers do not see private target identities.");
        Replay(g,r);var shown=p.Choices.First().Cards.Single();Answer(g,c=>c.Cards.SequenceEqual(new[]{shown}));Settle(g);
        Require(g.Events.Any(e=>e.Payload is ProgramCardsRevealedEvent reveal&&reveal.OwnerSeat==1&&reveal.Cards.Single().Id==shown),"Actual public reveal identifies target, not actor.");
        var target=g.CreateSnapshot(0).Players[1];Require(target.Skills!.Any(s=>s.ContentId=="fixture:locked")&&!target.Skills!.Any(s=>s.ContentId=="fixture:ordinary"),"Black suppresses nonlocked only in public qualification.");
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:yijue"),"Named phase quota consumed before pause.");
        var hp=g.State.Players[1].Hp;var slash=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);
        Play(g,slash,1);Settle(g);Require(g.State.Players[1].Hp==hp-2,"Actor's effective Heart Slash gains target-specific one damage.");Replay(g,r);
        var turn=g.CreateSnapshot(0).TurnNumber;Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,p=>g.Events.Any(e=>e.Payload is CurrentTurnNonLockedSkillSuppressionsExpiredEvent x&&x.TurnNumber==turn&&x.TurnOwnerSeat==0));
        Require(g.Events.Any(e=>e.Payload is CurrentTurnNonLockedSkillSuppressionsExpiredEvent x&&x.TurnOwnerSeat==0)&&g.CreateSnapshot(0).Players[1].Skills!.Any(s=>s.ContentId=="fixture:ordinary"),"Expiry follows actual actor turn end.");Replay(g,r);
    }
    public static void RedActualGainOptionalRecoveryAndReplay()
    {
        foreach(var heal in new[]{false,true})
        {
            var(g,r)=Start("red");Accept(g,new UseProgramSkillCommand(0,"fixture:driver","wound",[],[1],g.Revision,P(g)!.PromptId));Settle(g);
            var hp=g.State.Players[1].Hp;var cost=g.State.Players[0].Hand.First().Id;Yijue(g,cost,1);var shown=P(g)!.Choices.First().Cards.Single();Replay(g,r);
            Answer(g,c=>c.Cards.SequenceEqual(new[]{shown}));Require(P(g)!.PlayerSeat==0&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="recover"),"Actor owns optional recovery after gain.");
            Require(g.CreateCardZoneDiagnostics().Single(x=>x.CardId==shown).Location==CardLocation.Hand(0)&&g.CardMovements.Count(m=>m.CardId==shown&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0))==1,"Red revealed entity really changes ownership once before heal.");
            Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")== (heal?"recover":"decline"));Settle(g);
            Require(g.State.Players[1].Hp==hp+(heal?1:0)&&!g.Events.Any(e=>e.Payload is CurrentTurnNonLockedSkillSuppressionIssuedEvent),"Optional real recovery only on accepted heal branch.");Replay(g,r);
        }
    }
    public static void DiamondDistanceAndEffectiveRedEntityAudit()
    {
        foreach(var scenario in new[]{"diamond","heart","rewrite"})
        {
            var(g,r)=Start(scenario);var far=g.GetHumanLegalActions().Where(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==2).ToArray();
            Require(scenario=="diamond"?far.Length>0:far.Length==0,"Only effective Diamond Slash ignores distance.");
            if(scenario=="rewrite")
            {
                var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource?.SkillId=="boundary:wusheng");
                Require(g.State.Players[0].Hand.Single(c=>c.Id==action.CardId).Suit==Suit.Spade,"Physical black card is currently effective Heart.");
                Play(g,action,action.TargetSeat!.Value);Settle(g);Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ConversionChain.Any(c=>c.SkillId=="boundary:wusheng")&&e.Action.PhysicalCards.Count==1),"Effective red Wusheng pays a real entity.");Replay(g,r);
            }
        }
    }
    public static void PaymentGainChildrenAndEquipmentCost()
    {
        foreach(var scenario in new[]{"cost-child","gain-child","equipment"})
        {
            var(g,r)=Start(scenario);var cost=g.State.Players[0].Hand.First().Id;
            if(scenario=="equipment") { var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);cost=equip.CardId!.Value;Accept(g,new PlayCardCommand(0,cost,[],g.Revision,P(g)!.PromptId));Settle(g); }
            Yijue(g,cost,1);
            if(scenario=="cost-child")
            {
                Require(P(g)!.PlayerSeat==0&&P(g)!.SkillPrompt?.SkillId=="fixture:child"&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:yijue"&&f.PendingMovementContinuation is not null),"Actual payment movement child suspends owning Yijue before target choice.");
                Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            }
            Require(P(g)!.PlayerSeat==1,"Target choice follows completed cost child.");var shown=P(g)!.Choices.First().Cards.Single();Replay(g,r);Answer(g,c=>c.Cards.SequenceEqual(new[]{shown}));
            if(scenario=="gain-child")
            {
                Require(P(g)!.SkillPrompt?.SkillId=="fixture:child"&&g.CreateCardZoneDiagnostics().Single(x=>x.CardId==shown).Location==CardLocation.Hand(0),"Real gain precedes its complete movement child and optional recovery.");
                Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            }
            Settle(g);Require(g.CardMovements.Count(m=>m.CardId==cost&&m.To==CardLocation.DiscardPile)==1&&g.CardMovements.Count(m=>m.CardId==shown&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0))==1,"Child resume never repeats payment or gain.");
            Require(scenario!="equipment"||g.CardMovements.Any(m=>m.CardId==cost&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile),"HE cost accepts real Equipment.");Replay(g,r);
        }
    }
    public static void ActualPlayedWushengAndBlackHandRejection()
    {
        var(g,r)=Start("red");Accept(g,new UseProgramSkillCommand(0,"fixture:driver","duel",[],[1,0],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);var p=P(g)!;var c=p.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="boundary:wusheng");var cost=c.Cards.Single();Replay(g,r);
        Accept(g,new AnswerPromptCommand(0,p.PromptId,c.Id,g.Revision));Settle(g);
        Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Slash&&e.Action.PhysicalCards.Any(x=>x.CardId==cost)),"Wusheng truly Played Slash returns to actual Duel.");
        Require(g.CardMovements.Count(m=>m.CardId==cost&&m.To==CardLocation.DiscardPile)==1,"Played entity conserved once.");Replay(g,r);
        var(black,br)=Start("black");Yijue(black,black.State.Players[0].Hand.First().Id,1);Answer(black,c=>c.Cards.Count==1);Settle(black);var hp=black.State.Players[1].Hp;
        Accept(black,new UseProgramSkillCommand(0,"fixture:driver","duel",[],[0,1],black.Revision,P(black)!.PromptId));Settle(black);
        Require(black.State.Players[1].Hp==hp-1&&!black.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Response&&e.Action.ProviderSeat==1),"Black Hand prohibition prevents true Played Slash, not only Play menu.");Replay(black,br);
    }
    public static void ActualDiamondProviderUse()
    {
        var(g,r)=Start("provider");Accept(g,new UseProgramSkillCommand(0,"fixture:driver","request",[],[2],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat!=0&&p.RequiredCardKind==CardKind.Slash);var p=P(g)!;
        Require(p.PlayerSeat!=0&&p.Choices.Any(c=>c.Cards.Count==1),"Diamond far target reaches real provider payment.");
        var provider=p.PlayerSeat;var selected=p.Choices.First(c=>c.Cards.Count==1);var id=selected.Cards.Single();Replay(g,r);
        Settle(g);
        Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Use&&e.Action.ActorSeat==0&&e.Action.ProviderSeat!=0&&e.Action.PhysicalCards.Count==1),"Requester really Uses its own Diamond policy with another provider's entity.");Replay(g,r);
    }
    public static void ExtraPlayQuotaAndNamedHeartBenefit()
    {
        var(g,r)=Start("extra");var turn=g.CreateSnapshot(0).TurnNumber;
        Yijue(g,g.State.Players[0].Hand.First().Id,1);Answer(g,c=>c.Cards.Count==1);Settle(g);Replay(g,r);
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:yijue"),"One accepted use consumes first actual phase.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Settle(g);
        Require(g.CreateSnapshot(0).TurnNumber==turn&&g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:yijue"),"Extra and normal Play share actual turn but reset phase quota.");
        Yijue(g,g.State.Players[0].Hand.First().Id,1);Answer(g,c=>c.Cards.Count==1);Settle(g);
        var hp=g.State.Players[1].Hp;var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);Play(g,a,1);Settle(g);
        Require(g.State.Players[1].Hp==hp-2&&g.Events.Count(e=>e.Payload is CurrentTurnDirectedHeartSlashBonusGrantedEvent)==2,"Same owner named skill actual turn target yields only +1 after two real issues.");Replay(g,r);
        // Host source lifecycle audit; direct mutations below do not claim replay.
        var(host,_)=Start("black");Yijue(host,host.State.Players[0].Hand.First().Id,1);Answer(host,c=>c.Cards.Count==1);Settle(host);
        var owner=((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(host)!)[0];var source=owner.SkillGrants.Grants.Single(x=>x.SkillId=="boundary:yijue");owner.SkillGrants.RemoveGrant(source.GrantId);
        owner.SkillGrants.Grant(source with{GrantId="audit-regrant",SkillInstanceId="audit-regrant",SourceId="acquired:audit"});
        Require(!host.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:yijue"),"Regrant cannot refresh named phase acceptance quota.");
        owner.SkillGrants.RemoveGrant("audit-regrant");hp=host.State.Players[1].Hp;a=host.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);Play(host,a,1);Settle(host);
        Require(host.State.Players[1].Hp==hp-2,"Issued Heart fact survives exact source loss.");
    }
    private static void Play(GameEngine g,LegalAction a,int target)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,[target],g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    private static void Yijue(GameEngine g,int id,int target)=>Accept(g,new UseProgramSkillCommand(0,"boundary:yijue","discard-and-show",[id],[target],g.Revision,P(g)!.PromptId));
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var c=p.Choices.First(pick);Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var i=0;i<80;i++){if(P(g) is {} p&&stop(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Boundary not reached: "+P(g)?.Kind);}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(restored.CreateSnapshot(s)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack),"Four viewers, movement and exact typed stack cold restore from commands.");}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(x.Accepted,x.Error?.Message??"Rejected");}
    private static void Require(bool b,string text){if(!b)throw new InvalidOperationException(text);}
    private static (GameEngine,ContentRegistry) Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:guan-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:guan-owner",g.Revision,P(g)!.PromptId));Settle(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:guan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryGuanYuContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[b]);
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:driver","revision":1,"cardPolicies":[{"id":"providers","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"wei","ownerRole":"lord"}],"activations":[{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"wei","requiredKind":"slash"}]}]},{"id":"fixture:heart","revision":1,"cardPolicies":[{"id":"heart","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]}]}""","""{"schemaVersion":3,"skills":{"fixture:driver":{"name":"driver","description":"driver"},"fixture:heart":{"name":"heart","description":"heart"}}}""");
            foreach(var pair in c.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});b.AddSkill(new("fixture:ordinary","ordinary","ordinary"));b.AddSkill(new("fixture:locked","locked","locked"){Tags=SkillTag.Locked});b.AddSkill(new("fixture:pick","pick","pick"){SelectionWeights=new Dictionary<Role,double>{[Role.Lord]=-1000}});
            if(scenario is "cost-child" or "gain-child")
            {
                var child=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:child","revision":1,"triggers":[{"id":"child","window":"{{(scenario=="cost-child"?"cardsMoved":"cardsGained")}}","subject":"owner",{{(scenario=="cost-child"?"\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",":"\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perSourceOwner\",\"movementReasons\":[\"skill-program.boundary:yijue.MoveBoundCards\"],")}}"ignoreOwnSkillMovements":true,"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:child":{"name":"child","description":"child"}}}""");b.AddSkill(new("fixture:child","child","child"){Program=child.Programs["fixture:child"]});
            }
            if(scenario=="extra")
            {
                var extra=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:extra","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:extra":{"name":"extra","description":"extra"}}}""");b.AddSkill(new("fixture:extra","extra","extra"){Program=extra.Programs["fixture:extra"]});
            }
            b.AddGeneral(new("fixture:guan-owner","owner","supporter","boundary:wusheng","shu",4,["boundary:yijue","fixture:driver","fixture:pick",..(scenario is "black" or "rewrite" or "extra"?new[]{"fixture:heart"}:Array.Empty<string>()),..(scenario is "cost-child" or "gain-child"?new[]{"fixture:child"}:Array.Empty<string>()),..(scenario=="extra"?new[]{"fixture:extra"}:Array.Empty<string>())]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:guan-{i}","target","supporter","fixture:ordinary","wei",4,["fixture:locked"]));
            var suit=scenario is "diamond" or "provider"?Suit.Diamond:scenario is "black" or "rewrite" or "extra"?Suit.Spade:Suit.Heart;
            var card=scenario=="equipment"?"standard:crossbow":scenario is "red" or "rewrite" or "cost-child" or "gain-child"?"standard:peach":"standard:slash";
            b.AddDeck(new("fixture:guan-deck","fixed",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(card,suit,i%13+1)).ToArray()});
            b.AddMode(new("identity:guan-fixture","guan",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:guan-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:guan-owner","fixture:guan-1","fixture:guan-2","fixture:guan-3"]));
        }
    }
}
