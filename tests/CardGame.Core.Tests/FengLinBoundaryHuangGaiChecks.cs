using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinBoundaryHuangGaiChecks
{
    private static ContentRegistry Catalog=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());

    public static void MultiCardColorAndSourceLifecycle()
    {
        foreach(var mixed in new[]{false,true})
        {
            var(g,r)=Start(scenario:mixed?"spear-mixed":"spear-red");Driver(g,"draw");Settle(g);
            var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip&&g.State.Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.ZhangbaSerpentSpear);
            Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,P(g)!.PromptId));Settle(g);Driver(g,"loss2");Settle(g);
            var hand=g.State.Players[0].Hand;var pair=new[]{hand.First(c=>c.Suit==Suit.Heart),hand.First(c=>c.Suit==(mixed?Suit.Spade:Suit.Diamond))}.Select(c=>c.Id).ToArray();
            Accept(g,new UseEquipmentEffectCommand(0,CardKind.ZhangbaSerpentSpear,pair,[2],g.Revision,P(g)!.PromptId));Replay(g,r);
            var used=g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Single(e=>e.Action.ActorSeat==0&&e.Action.Type==CardActionType.Use&&e.Action.EffectiveKind==CardKind.Slash).Action;
            Require(used.EffectiveSuit is null&&used.EffectiveIsRed==(mixed?(bool?)null:true),"Two red different suits are red; mixed red black is unknown.");Settle(g);
            Require(g.Events.Select(e=>e.Payload).OfType<ResponseRequestedEvent>().Any(e=>e.TargetSeat==2&&e.RequiredCardKind==CardKind.Dodge)==mixed,"Only the two-red Spear Slash receives the Dodge-skip benefit.");Replay(g,r);
        }
        // Direct host lifecycle audit is deliberately separate from accepted-command replay.
        var(host,_)=Start();Driver(host,"loss2");Settle(host);
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var players=(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",flags)!.GetValue(host)!;var owner=players[0];var grant=owner.SkillGrants.Grants.Single(g=>g.SkillId=="boundary:zhaxiang");
        owner.SkillGrants.SetEnabled(grant.GrantId,false);
        Require(host.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==2),"Issued policy remains effective after source disable.");
        owner.SkillGrants.RemoveGrant(grant.GrantId);owner.SkillGrants.Grant(grant with {GrantId="fixture:new-source",SkillInstanceId="fixture:new-instance",SourceId="fixture:replacement"});
        Require(host.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==2)&&host.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Count()==2,"Loss/regrant neither removes nor duplicates issued benefits.");
        var(late,_late)=Start(scenario:"dying");Driver(late,"dying");Reach(late,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);
        var changes=(IReadOnlyList<HpChangeContext>)typeof(GameEngine).GetField("_pendingHpChanges",flags)!.GetValue(late)!;var loss=changes.Single(c=>c.Kind==HpChangeKind.Loss);
        Require(loss.LossOccurrence is {Phase:TurnPhase.Play,TurnOwnerSeat:0}&&loss.HpBefore-loss.HpAfter==loss.Amount&&loss.FrozenLossCandidates is IList<ProgramTriggerCandidate> list&&list.IsReadOnly,"Original Loss scalar context and candidate source collection are frozen before dying.");
        var latePlayers=(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",flags)!.GetValue(late)!;var original=latePlayers[0].SkillGrants.Grants.Single(g=>g.SkillId=="boundary:zhaxiang");latePlayers[0].SkillGrants.RemoveGrant(original.GrantId);latePlayers[0].SkillGrants.Grant(original with {GrantId="fixture:late",SkillInstanceId="fixture:late-instance",SourceId="fixture:late-source"});
        var rescue=P(late)!;Accept(late,new AnswerPromptCommand(0,rescue.PromptId,rescue.Choices.First(c=>c.Cards.Count==1).Id,late.Revision));Settle(late);
        Require(!late.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"New source instance acquired during dying cannot claim original Loss.");

    }

    private static (GameEngine,ContentRegistry) Start(bool red=true,string scenario="normal",bool capability=true)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(red,scenario,capability));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-hg",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:hg",g.Revision,P(g)!.PromptId));Settle(g);return(g,r);
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Driver(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,"fixture:hg-driver",id,[],[],g.Revision,P(g)!.PromptId));
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> goal){for(var i=0;i<150;i++){if(P(g) is {} p&&goal(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixture boundary not reached: "+P(g)?.Kind);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static void Replay(GameEngine g,ContentRegistry r){var h=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(h.CreateSnapshot(s)))&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(h.ResolutionStack),"Four viewer and exact typed stack restore.");Require(g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==80,"Entity conservation.");}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private sealed class Fixture(bool red,string scenario,bool capability):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-hg",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(Catalog.GetSkill("boundary:kurou"));if(capability)b.AddSkill(Catalog.GetSkill("boundary:zhaxiang"));
            if(scenario is "outside" or "child")
            {
                var effects=scenario=="outside"?"[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]":"[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]";
                var triggers=scenario=="outside"?"{\"id\":\"outside\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":"+effects+"}":"{\"id\":\"child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"optional\":true,\"effects\":"+effects+"}";
                var child=SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\"fixture:hg-child\",\"revision\":1,\"triggers\":["+triggers+"]}]}","{\"schemaVersion\":3,\"skills\":{\"fixture:hg-child\":{\"name\":\"子链\",\"description\":\"子链\"}}}");
                b.AddSkill(new("fixture:hg-child","子链","子链"){Program=child.Programs["fixture:hg-child"]});
            }
            if(scenario=="extra")b.AddSkill(Catalog.GetSkill("classic:dangxian"));
            if(scenario=="wusheng")b.AddSkill(Catalog.GetSkill("classic:wusheng"));
            if(scenario=="fan")b.AddCard(Catalog.GetCard("classic:zhuque-fan"));
            if(scenario.StartsWith("spear"))b.AddCard(Catalog.GetCard("classic:zhangba-serpent-spear"));
            var program=SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:hg-driver","revision":1,"cardPolicies":[{"id":"request-policy","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"qun","ownerRole":"lord"}],"activations":[{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"qun","requiredKind":"slash"}]},{"id":"dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},{"id":"loss2","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},{"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:hg-driver":{"name":"实际链","description":"实际链"}}}""");b.AddSkill(new("fixture:hg-driver","实际链","实际链"){Program=program.Programs["fixture:hg-driver"]});
            b.AddSkill(new("fixture:hg-marker","固定","固定"){SelectionWeights=new Dictionary<Role,double>{[Role.Lord]=-1000}});
            b.AddGeneral(new("fixture:hg","黄盖","supporter","boundary:kurou","wu",4,new[]{"fixture:hg-driver","fixture:hg-marker"}.Concat(capability?["boundary:zhaxiang"]:Array.Empty<string>()).Concat(scenario=="extra"?["classic:dangxian"]:Array.Empty<string>()).Concat(scenario=="wusheng"?["classic:wusheng"]:Array.Empty<string>()).Concat(scenario is "outside" or "child"?["fixture:hg-child"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:hg-{i}","其他"+i,"supporter","standard:none",scenario=="provider"?"qun":"wei",scenario=="provider"?1:i==1?2:4));
            b.AddDeck(new("fixture:hg-deck","固定",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(scenario=="provider"?"standard:slash":scenario.StartsWith("spear")?(i%3==0?"classic:zhangba-serpent-spear":"standard:dodge"):scenario=="dying"?"standard:peach":scenario=="wusheng"?"standard:dodge":scenario=="fan"?(i%3==0?"classic:zhuque-fan":i%3==1?"standard:slash":"standard:dodge"):i%2==0?"standard:slash":"standard:dodge",scenario=="spear-red"?(i%2==0?Suit.Heart:Suit.Diamond):scenario=="spear-mixed"?(i%2==0?Suit.Heart:Suit.Spade):red?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-hg","固定",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:hg-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:hg","fixture:hg-1","fixture:hg-2","fixture:hg-3"]));
        }
    }
}
