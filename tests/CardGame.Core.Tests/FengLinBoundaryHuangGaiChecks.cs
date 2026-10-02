using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinBoundaryHuangGaiChecks
{
    private static ContentRegistry Catalog=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
    public static void LossPaymentAndPerPoint()
    {
        var(g,r)=Start();var hand=g.State.Players[0].HandCount;var hp=g.State.Players[0].Hp;
        var id=g.State.Players[0].Hand.First().Id;
        Accept(g,new UseProgramSkillCommand(0,"boundary:kurou","discard-one-and-lose-hp",[id],[],g.Revision,P(g)!.PromptId));Settle(g);
        Require(g.State.Players[0].Hp==hp-1&&g.State.Players[0].HandCount==hand+2,"Real discard then Loss draws three.");
        Require(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile)==1,"HE payment once.");
        var consumed=g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().ToArray();Require(consumed.Length==1&&consumed[0].Modifier.Amount==1,"One point one exact source grant.");
        Replay(g,r);hand=g.State.Players[0].HandCount;Driver(g,"loss2");Settle(g);
        Require(g.State.Players[0].HandCount==hand+6&&g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Count()==3,"Loss2 independently draws six and stacks two allowances.");Replay(g,r);
        hand=g.State.Players[0].HandCount;Driver(g,"damage");Settle(g);Require(g.State.Players[0].HandCount==hand,"Damage is not Loss.");Replay(g,r);
    }
    public static void RedSlashDistanceDodgeAndColor()
    {
        foreach(var red in new[]{false,true})
        {
            var(g,r)=Start(red);Driver(g,"loss2");Settle(g);
            var far=g.GetHumanLegalActions().Where(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==2).ToArray();
            Require(red?far.Length>0:far.Length==0,"Red only ignores real distance to seat2.");
            var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);
            var before=g.State.Players[1].Hp;
            Accept(g,new PlayCardCommand(0,action.CardId!.Value,[1],g.Revision,P(g)!.PromptId,action.PlayedCardKind){ConversionSource=action.ConversionSource,AdditionalConversionSources=action.AdditionalConversionSources});Replay(g,r);
            Settle(g);Require(g.Events.Select(e=>e.Payload).OfType<CardRespondedEvent>().Any(e=>e.ResponderSeat==1&&e.EffectiveCardKind==CardKind.Dodge),"Dodge remains a paid real response.");
            Require(g.State.Players[1].Hp==before-(red?1:0),"Uncancelable retains damage after Dodge; black is canceled.");
            Require(!red||g.Log.Any(l=>l.Message.Contains("此杀不能被抵消")),"Public Dodge log reports response without false cancellation.");
            var accepted=g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().First(e=>e.Action.ActorSeat==0&&e.Action.EffectiveKind==CardKind.Slash).Action;
            Require(accepted.EffectiveIsRed==red,"Minimal 307-only catalog captures effective color.");Replay(g,r);
        }
    }
    public static void DyingExtraPlayAndLegacy()
    {
        var(g,r)=Start(scenario:"dying");var hp=g.State.Players[0].Hp;var before=g.State.Players[0].HandCount;
        Driver(g,"dying");Reach(g,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);
        Require(!g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"Loss first enters dying without prematurely granting benefits.");Replay(g,r);
        var pending=P(g)!;var peach=pending.Choices.First(c=>c.Cards.Count==1);Accept(g,new AnswerPromptCommand(0,pending.PromptId,peach.Id,g.Revision));Settle(g);
        Require(g.State.Players[0].Hp==1&&g.State.Players[0].HandCount==before-1+3*hp&&g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Count()==hp,"Rescue resumes each original Loss point exactly once.");Replay(g,r);
        var(extra,er)=Start(scenario:"extra");var turn=extra.CreateSnapshot(0).TurnNumber;
        var paid=extra.State.Players[0].Hand.First().Id;Accept(extra,new UseProgramSkillCommand(0,"boundary:kurou","discard-one-and-lose-hp",[paid],[],extra.Revision,P(extra)!.PromptId));Settle(extra);
        var end=extra.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.EndPlay);Accept(extra,new EndPlayPhaseCommand(0,extra.Revision,P(extra)!.PromptId));
        Settle(extra);Require(extra.CreateSnapshot(0).TurnNumber==turn,"Inserted and normal Play share the actual turn.");
        paid=extra.State.Players[0].Hand.First().Id;Accept(extra,new UseProgramSkillCommand(0,"boundary:kurou","discard-one-and-lose-hp",[paid],[],extra.Revision,P(extra)!.PromptId));Settle(extra);
        Require(extra.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Count()==2,"Kurou phase quota resets but actual turn bonuses accumulate.");Replay(extra,er);
        var(old,or)=Start(capability:false);var hand=old.State.Players[0].HandCount;Driver(old,"loss2");Settle(old);
        Require(old.State.Players[0].HandCount==hand&&!old.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"Old registry retains no new Loss or color behavior.");Replay(old,or);
        Require(!JsonSerializer.Serialize(new HpChangeContext(1,null,null,0,HpChangeKind.Loss,1,2,1)).Contains("LossOccurrence"),"Old nullable fields omitted.");
        foreach(var invalid in new[]{"\"activations\":[{\"id\":\"bad\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"grantTurnRedSlashBenefits\",\"target\":\"owner\"}]}]","\"triggers\":[{\"id\":\"bad\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"effects\":[{\"op\":\"grantTurnRedSlashBenefits\",\"target\":\"owner\"}]}]"})
        {
            var rejected=false;try{SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\"fixture:bad\",\"revision\":1,"+invalid+"}]}","{\"schemaVersion\":3,\"skills\":{\"fixture:bad\":{\"name\":\"bad\",\"description\":\"bad\"}}}");}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"New benefit op rejects activation and Damage contexts.");
        }

    }

    public static void ConvertedSlashAndExpiry()
    {
        foreach(var scenario in new[]{"wusheng","fan"})
        {
            var(g,r)=Start(scenario:scenario);
            if(scenario=="fan")
            {
                Driver(g,"draw");Settle(g);var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip&&g.State.Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.ZhuqueFan);
                Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,P(g)!.PromptId));Settle(g);
            }
            Driver(g,"loss2");Settle(g);
            var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==2&&(scenario=="fan"?a.PlayedCardKind==CardKind.FireSlash:a.ConversionSource?.SkillId=="classic:wusheng"));
            Accept(g,new PlayCardCommand(0,action.CardId!.Value,[2],g.Revision,P(g)!.PromptId,action.PlayedCardKind){ConversionSource=action.ConversionSource,AdditionalConversionSources=action.AdditionalConversionSources});Replay(g,r);
            var use=g.ResolutionStack.OfType<CardUseFrame>().Single();Require(use.Action?.EffectiveIsRed==true&&use.Enhancements.HasFlag(CurrentCardEnhancement.Uncancelable),"Converted and Fan final Slash share frozen red color and uncancelability.");Settle(g);Replay(g,r);
        }
        var(expiry,er)=Start();Driver(expiry,"loss2");Settle(expiry);var turn=expiry.CreateSnapshot(0).TurnNumber;
        Accept(expiry,new EndPlayPhaseCommand(0,expiry.Revision,P(expiry)!.PromptId));
        for(var i=0;i<100&&expiry.CreateSnapshot(0).TurnNumber==turn;i++)
        {
            if(P(expiry) is {Kind:DecisionKind.DiscardCards,PlayerSeat:0} discard)
            {
                var snap=expiry.CreateSnapshot(0);Accept(expiry,new DiscardCardsCommand(0,snap.Players[0].Hand.Take(discard.RequiredCardCount).Select(c=>c.Id).ToArray(),discard.PromptId,expiry.Revision));
            }
            else Accept(expiry,new AdvanceOneStepCommand(expiry.Revision));
        }
        Require(expiry.CreateSnapshot(0).TurnNumber>turn&&expiry.Events.Select(e=>e.Payload).OfType<TurnCardUseEffectsExpiredEvent>().Any(e=>e.GrantSequences.Count==4),"Two source bonuses and two policies expire together at actual turn end.");Replay(expiry,er);
    }

    public static void PaymentChildOutsideAndDeath()
    {
        var(g,r)=Start(scenario:"child");var hand=g.State.Players[0].HandCount;var hp=g.State.Players[0].Hp;var id=g.State.Players[0].Hand.First().Id;
        Accept(g,new UseProgramSkillCommand(0,"boundary:kurou","discard-one-and-lose-hp",[id],[],g.Revision,P(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:hg-child");
        Require(g.State.Players[0].Hp==hp,"Paid discard movement child finishes before Loss.");Replay(g,r);
        var frozen=GameCheckpointJson.Serialize(g.CreateCheckpoint());var revision=g.Revision;var reject=g.Submit(new AnswerPromptCommand(0,P(g)!.PromptId,new ChoiceId("foreign"),revision));
        Require(!reject.Accepted&&g.Revision==revision&&GameCheckpointJson.Serialize(g.CreateCheckpoint())==frozen,"Illegal child input rejected atomically.");
        var prompt=P(g)!;Accept(g,new AnswerPromptCommand(0,prompt.PromptId,prompt.Choices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="activate").Id,g.Revision));Settle(g);
        Require(g.State.Players[0].HandCount==hand+3&&g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.DiscardPile)==1,"Cost and nested draw run once before original Loss draw.");Replay(g,r);
        var(outside,or)=Start(scenario:"outside");Require(outside.State.Players[0].HandCount==9&&outside.State.Players[0].Hp==outside.State.Players[0].MaxHp-1&&!outside.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"Draw-phase Loss draws three but never grants Play combat benefits: "+outside.State.Players[0].HandCount+":"+outside.State.Players[0].Hp+":"+outside.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Count());Replay(outside,or);
        var(dead,dr)=Start();Driver(dead,"dying");
        for(var i=0;i<100&&dead.State.Players[0].IsAlive;i++)
        {
            if(P(dead) is {Kind:DecisionKind.RescueDying,PlayerSeat:0} rescue)Accept(dead,new AnswerPromptCommand(0,rescue.PromptId,rescue.Choices.First(c=>c.Cards.Count==0).Id,dead.Revision));
            else Accept(dead,new AdvanceOneStepCommand(dead.Revision));
        }
        Require(!dead.State.Players[0].IsAlive&&!dead.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"Dead owner receives no deferred Loss benefit.");Replay(dead,dr);
    }

    public static void MultiCardColorAndSourceLifecycle()
    {
        foreach(var mixed in new[]{false,true})
        {
            var(g,r)=Start(scenario:mixed?"spear-mixed":"spear-red");Driver(g,"draw");Settle(g);
            var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip&&g.State.Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.ZhangbaSerpentSpear);
            Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,P(g)!.PromptId));Settle(g);Driver(g,"loss2");Settle(g);
            var hand=g.State.Players[0].Hand;var pair=new[]{hand.First(c=>c.Suit==Suit.Heart),hand.First(c=>c.Suit==(mixed?Suit.Spade:Suit.Diamond))}.Select(c=>c.Id).ToArray();
            Accept(g,new UseEquipmentEffectCommand(0,CardKind.ZhangbaSerpentSpear,pair,[2],g.Revision,P(g)!.PromptId));Replay(g,r);
            var use=g.ResolutionStack.OfType<CardUseFrame>().Single();Require(use.Action!.EffectiveSuit is null&&use.Action.EffectiveIsRed==(mixed?(bool?)null:true)&&use.Enhancements.HasFlag(CurrentCardEnhancement.Uncancelable)==!mixed,"Two red different suits are red; mixed red black is unknown and never acquires red benefits.");Settle(g);Replay(g,r);
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

    public static void ActualProviderColor()
    {
        foreach(var red in new[]{false,true})
        {
            var(g,r)=Start(red,scenario:"provider");Driver(g,"loss2");Settle(g);
            Accept(g,new UseProgramSkillCommand(0,"fixture:hg-driver","request",[],[2],g.Revision,P(g)!.PromptId));
            for(var i=0;i<100 && (g.ResolutionStack.Count>0 || !(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}));i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
            var action=g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().FirstOrDefault(e=>e.Action.ActorSeat==0&&e.Action.ProviderSeat!=0&&e.Action.EffectiveKind==CardKind.Slash);
            Require(red?action?.Action.EffectiveIsRed==true:action is null,"Actual far faction provider accepts red only and preserves actual actor color: "+red+":"+JsonSerializer.Serialize(action)+":"+string.Join(";",g.Events.Select(e=>e.Payload).OfType<FactionSlashResolvedEvent>().Select(e=>e.Succeeded+":"+e.ProviderSeat))+":"+string.Join(";",g.Log.TakeLast(10).Select(l=>l.Message)));Replay(g,r);
        }
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
