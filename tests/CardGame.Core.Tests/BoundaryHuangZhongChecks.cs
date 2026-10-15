using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class BoundaryHuangZhongChecks
{
    private const string Skill="boundary:liegong-current";
    private static readonly Lazy<ContentRegistry> Classic=new(()=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage()));
    public static void ComparisonsAndActualDodge()
    {
        foreach(var hand in new[]{false,true})foreach(var hp in new[]{false,true})
        {
            var(g,r)=Start();if(!hand){Driver(g,"target-draw",[1]);Settle(g);}if(!hp){Driver(g,"target-hurt",[1]);Settle(g);}
            var before=g.State.Players[1].Hp;Play(g,g.State.Players[0].Hand.First(c=>c.Kind==CardKind.Slash).Id,[1]);Finish(g);
            var facts=g.Events.Select(e=>e.Payload).OfType<ProgramTargetSlashReceiptIssuedEvent>().Select(e=>e.Receipt).ToArray();
            Require(facts.Count(e=>e.PreventCancellation)==(hand?1:0)&&facts.Sum(e=>e.DamageBonus)==(hp?1:0),"Each op reads its own frozen hand/HP comparison.");
            Require(g.State.Players[1].Hp==before-(hp?2:1),"Only actual matching HP adds one direct target damage.");Cold(g,r);
        }
        var(d,dr)=Start(dodge:true);var act=d.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);Play(d,act.CardId!.Value,[1],act);Finish(d);
        Require(d.Events.Select(e=>e.Payload).OfType<CardRespondedEvent>().Any(e=>e.EffectiveCardKind==CardKind.Dodge),"A real Dodge is paid while the new effect prevents cancellation.");
        Require(d.State.Players[1].Hp==3,"Dodge payment cannot cancel this Slash; HP benefit applies.");Cold(d,dr);Evidence(d,"actual-paid-dodge");
        var(f,fr)=Start(child:"change-facts");Play(f,f.State.Players[0].Hand[0].Id,[1]);ReachPrompt(f,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));Answer(f,c=>c.Targets.SequenceEqual([1]));ReachPrompt(f,p=>p.SkillPrompt?.SkillId==Skill);Require(f.State.Players[1].Hp<f.State.Players[0].Hp&&f.State.Players[1].HandCount>f.State.Players[0].HandCount,"The actual higher-priority child makes both current comparisons false after the original facts were frozen.");Cold(f,fr);Activate(f);Finish(f);
        Require(f.Events.Select(e=>e.Payload).OfType<ProgramTargetSlashReceiptIssuedEvent>().Count()==2,"Real higher-priority child changes HP/hand after frozen comparison without changing either receipt.");Cold(f,fr);
    }
    public static void MultipleTargetsAndDamageBoundaries()
    {
        var(m,mr)=Start(weapon:"classic:fangtian-halberd");Driver(m,"equip");Settle(m);var slash=m.State.Players[0].Hand.First(c=>c.Kind==CardKind.Slash).Id;
        Accept(m,new UseProgramSkillCommand(0,"fixture:hz-driver","trim",m.State.Players[0].Hand.Where(c=>c.Id!=slash).Select(c=>c.Id).ToArray(),[],m.Revision,P(m)!.PromptId));Settle(m);
        Require(m.GetHumanLegalActions().Any(a=>a.CardId==slash&&a.TargetSeats.SequenceEqual([1,2])),"Real final-hand Fangtian offers the multi-target actual use.");Play(m,slash,[1,2]);ReachPrompt(m,p=>p.SkillPrompt?.SkillId==Skill);Cold(m,mr);Activate(m);
        ReachPrompt(m,p=>p.SkillPrompt?.SkillId==Skill);var owning=m.ResolutionStack.OfType<CardUseFrame>().Single(f=>f.FinalTargetSlashReceipts!=null);
        Require(owning.FinalTargetSlashReceipts is IList<ProgramTargetSlashReceipt>{IsReadOnly:true}&&owning.FinalTargetSlashReceipts.All(item=>item.TargetSeat==1),"First target's immutable issued receipt stays on the real owning prepared use while the second offer waits.");Cold(m,mr);Evidence(m,"prepared-second-target-pause");Activate(m);Finish(m);
        var rs=m.Events.Select(e=>e.Payload).OfType<ProgramTargetSlashReceiptIssuedEvent>().Select(e=>e.Receipt).ToArray();
        Require(rs.Select(e=>e.TargetSeat).Distinct().Order().SequenceEqual([1,2])&&rs.GroupBy(e=>e.TargetSeat).All(group=>group.Sum(e=>e.DamageBonus)==1),"Actual prepared multi-target attacks retain separate receipts and one bonus each.");
        Require(m.State.Players[1].Hp==3&&m.State.Players[2].Hp==3,"Two actual targets each take two direct damage.");Cold(m,mr);Evidence(m,"prepared-multiple-targets");
        var(s,sr)=Start(weapon:"classic:silver-lion",child:"armor");Driver(s,"equip-target",[1]);Settle(s);var before=s.State.Players[1].Hp;Play(s,s.State.Players[0].Hand.First(c=>c.Kind==CardKind.Slash).Id,[1]);Finish(s);
        Require(s.State.Players[1].Hp==before-1,"Silver Lion caps the resulting real damage after the additive receipt.");Cold(s,sr);
        var(chain,cr)=Start(fire:true);Driver(chain,"chain",[1]);Settle(chain);Driver(chain,"chain",[2]);Settle(chain);Play(chain,chain.State.Players[0].Hand.First(c=>c.Kind==CardKind.FireSlash).Id,[1]);Finish(chain);
        Require(chain.State.Players[1].Hp==3&&chain.State.Players[2].Hp==3,"Real propagated elemental damage inherits the direct two damage without another Liegong bonus.");Cold(chain,cr);Evidence(chain,"actual-chain-once");
        var(skip,skr)=Start();Play(skip,skip.State.Players[0].Hand[0].Id,[1]);Finish(skip,activate:false);
        Require(!skip.Events.Select(e=>e.Payload).OfType<ProgramTargetSlashReceiptIssuedEvent>().Any()&&skip.State.Players[1].Hp==4,"Optional skip creates no receipt or bonus.");Cold(skip,skr);
    }
    private static void Cold(GameEngine g,ContentRegistry r)
    {
        ReplayFourViews(g,r);var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);
        Require(JsonSerializer.Serialize(g.CreateSnapshot(-1))==JsonSerializer.Serialize(restored.CreateSnapshot(-1)),"Actual spectator cold projection matches.");
        var revision=g.Revision;foreach(var viewer in Enumerable.Range(0,4))foreach(var player in g.CreateSnapshot(viewer).Players)if(player.Seat!=viewer)Require(player.Hand.Count==0,"Other seats' hand entities remain private.");Require(g.Revision==revision,"Five read projections are pure.");
    }
    private static void Evidence(GameEngine g,string label){if(Environment.GetEnvironmentVariable("CARD_HZ_EVIDENCE") is not{Length:>0} root)return;var p=Path.Combine(root,label);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));File.WriteAllText(Path.Combine(p,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));for(int i=-1;i<4;i++)File.WriteAllText(Path.Combine(p,"view-"+i+".json"),JsonSerializer.Serialize(g.CreateSnapshot(i)));}
    private static int Attempt;
    private static void Accept(GameEngine g,GameCommand command){var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());try{BoundaryLiDianChecks.Accept(g,command);}catch(Exception ex){if(Environment.GetEnvironmentVariable("CARD_HZ_EVIDENCE")is{Length:>0}root){var p=Path.Combine(root,"submit-failure-"+Interlocked.Increment(ref Attempt).ToString("D3"));Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"checkpoint-before.json"),before);File.WriteAllText(Path.Combine(p,"attempt.json"),JsonSerializer.Serialize(command,command.GetType()));File.WriteAllText(Path.Combine(p,"faulted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));File.WriteAllText(Path.Combine(p,"exception.txt"),ex.ToString());}throw;}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> goal){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(goal).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Driver(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:hz-driver",id,[],targets??(id=="equip"?[0]:[]),g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,int card,int[]targets,LegalAction? a=null)=>Accept(g,new PlayCardCommand(0,card,targets,g.Revision,P(g)!.PromptId,a?.PlayedCardKind){ConversionSource=a?.ConversionSource,AdditionalConversionSources=a?.AdditionalConversionSources});
    private static void ReachPrompt(GameEngine g,Func<PendingDecision,bool> goal){for(var n=0;n<100;n++){if(P(g)is{}p&&goal(p))return;if(P(g)is{Kind:DecisionKind.DiscardCards,PlayerSeat:0}discard)Accept(g,new DiscardCardsCommand(0,discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),discard.PromptId,g.Revision));else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Small fixed fixture did not reach expected prompt: "+P(g)?.Prompt);}
    private static void Settle(GameEngine g)=>ReachPrompt(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Finish(GameEngine g,bool activate=true){for(var n=0;n<130;n++){if(P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return;if(P(g)is{Kind:DecisionKind.ProgramTrigger,PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")==((p.SkillPrompt?.SkillId==Skill&&activate)?"activate":"skip"));else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Finite Slash fixture did not settle: "+P(g)?.Prompt);}
    private static (GameEngine,ContentRegistry) Start(int rank=13,string?weapon=null,bool liegong=true,bool dodge=false,bool ignore=false,string child="",bool native=false,bool canonical=false,bool fire=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(rank,weapon,liegong,dodge,ignore,child,native,canonical,fire));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=native?3:0,HumanRole=native?Role.Rebel:Role.Lord,ModeId="identity:classic-hz",UseInteractiveSetup=!native,AdvanceAfterHumanCommands=false,MaxTurns=5},r);Accept(g,new StartGameCommand());if(!native){ReachPrompt(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:hz",g.Revision,P(g)!.PromptId));Settle(g);}return(g,r);
    }
    private static string Activation(string id,string effects,int targets=0,int cards=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":"+(cards>0?1:0)+",\"maxCards\":"+cards+",\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"固定准备\",\"description\":\"固定准备\"}}}").Programs[id];
    private sealed class Fixture(int rank,string?weapon,bool liegong,bool dodge,bool ignore,string child,bool native,bool canonical,bool fire):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-hz",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "boundary:huang-zhong");
            b.AddSkill(Classic.Value.GetSkill("boundary:rende"));if(dodge)b.AddSkill(Classic.Value.GetSkill("classic:wusheng"));if(weapon!=null)b.AddCard(Classic.Value.GetCard(weapon));if(canonical)b.AddCard(Classic.Value.GetCard("classic:borrowed-sword"));
            if(ignore)b.AddSkill(new("fixture:hz-ignore","无距离","无距离"){Program=Load("fixture:hz-ignore","\"cardPolicies\":[{\"id\":\"ignore\",\"kind\":\"ignoreSlashUseDistanceBySuit\",\"cardKinds\":[\"slash\"],\"inputSuit\":\"heart\"}]")});
            if(canonical)b.AddSkill(new("fixture:hz-tax","距离边界","距离边界"){Program=Load("fixture:hz-tax","\"modifiers\":[{\"id\":\"tax\",\"query\":\"outgoingDistance\",\"operation\":\"add\",\"value\":4,\"priority\":0}]")});
            var acts=Activation("equip","[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipment\"}]",1)+","+Activation("equip-target","[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipment\"}]",1)+","+Activation("target-hurt","[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}]",1)+","+Activation("target-draw","[{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":5}]",1)+","+Activation("supply","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":5}]")+","+Activation("request","[{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"requested\"}]",1)+","+Activation("chain","[{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}]",1)+","+Activation("trim","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"trim\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"trim\",\"destination\":\"discardPile\"}]",cards:20);
            b.AddSkill(new("fixture:hz-driver","真实准备","真实准备"){Program=Load("fixture:hz-driver","\"activations\":["+acts+"]")});
            if(child is "extra" or "role" or "change-facts" or "lose-source")
            {
                var effects=child=="extra"?"[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"addCurrentCardUseTarget\",\"target\":\"selectedTarget\"}]":child=="role"?"[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]":child=="change-facts"?"[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventTarget\"},{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":5},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}]":"[{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"sourceBind\":\"boundary:liegong-current\",\"skillIds\":[\"standard:none\"]}]";
                var priority=child is "extra" or "change-facts"?10:-10;
                b.AddSkill(new("fixture:hz-child","实际子链","实际子链"){Program=Load("fixture:hz-child","\"triggers\":[{\"id\":\"child\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"priority\":"+priority+",\"effects\":"+effects+"}]")});
            }
            var extra=new[]{"boundary:rende"}.Concat(canonical?["fixture:hz-tax"]:Array.Empty<string>()).Concat(dodge?new[]{"classic:wusheng"}:Array.Empty<string>()).Concat(ignore?["fixture:hz-ignore"]:Array.Empty<string>()).Concat(child is "extra" or "role" or "change-facts" or "lose-source"?["fixture:hz-child"]:Array.Empty<string>()).Concat(!native?["fixture:hz-driver"]:Array.Empty<string>()).ToArray();
            b.AddGeneral(new("fixture:hz","黄忠","supporter",liegong?Skill:"standard:none","shu",4,extra));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:hz-{i}","固定目标","supporter",native||canonical?Skill:"standard:none","wei",5,(native&&dodge?new[]{"classic:wusheng"}:Array.Empty<string>()).Concat(canonical?["fixture:hz-tax"]:Array.Empty<string>()).ToArray()));
            b.AddDeck(new("fixture:hz-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(canonical&&i%4==0?"classic:borrowed-sword":dodge?"standard:dodge":fire?"standard:fire_slash":"standard:slash",Suit.Heart,rank)).Concat(weapon!=null?Enumerable.Range(0,4).Select(i=>new ContentDeckPhysicalCard(weapon,Suit.Heart,rank)):[]).ToArray()});
            b.AddMode(new("identity:classic-hz","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:hz-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:hz","fixture:hz-1","fixture:hz-2","fixture:hz-3"]));
        }
    }
}
