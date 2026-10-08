using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;
internal static class BoundaryWeiYanChecks
{
    private const string Skill="boundary:qimou-current",Binding="lose-hp-for-turn-benefits";
    private static readonly Lazy<ContentRegistry> Classic=new(()=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage()));
    public static void ReuseAndDefinitions()
    {
        var(g,r)=Start();Driver(g,"damage",[1]);
        for(var n=0;n<60&&P(g)?.Kind!=DecisionKind.PlayCard;n++)
        {
            if(P(g)?.SkillPrompt?.SkillId=="boundary:kuanggu" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"))Activate(g);
            else if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="draw")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="draw");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        Console.WriteLine("Kuanggu DamageEvents="+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>()));
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramOptionChosenEvent>().Count(e=>e.SkillId=="boundary:kuanggu")==2,"Current Kuanggu per point; programs="+string.Join(";",g.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Select(e=>e.SkillId))+" prompt="+P(g)?.Prompt);ReplayFourViews(g,r);
        var(f,fr)=Start();Driver(f,"damage",[2]);Settle(f);
        Require(!f.Events.Select(e=>e.Payload).OfType<ProgramOptionChosenEvent>().Any(e=>e.SkillId=="boundary:kuanggu"),"Distance two does not trigger the reused current Kuanggu.");
        Console.WriteLine("Kuanggu DamageEvents="+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>()));
        var chain="[{\"op\":\"chooseOwnerHpLoss\",\"target\":\"owner\",\"resultBind\":\"loss\"},{\"op\":\"drawPaidHpLoss\",\"target\":\"owner\",\"sourceBind\":\"loss\"},{\"op\":\"grantPaidHpLossDistance\",\"target\":\"owner\",\"sourceBind\":\"loss\"},{\"op\":\"grantPaidHpLossSlashLimit\",\"target\":\"owner\",\"sourceBind\":\"loss\"}]";
        var exact=Act("bad",chain).Replace("\"usesPerTurn\":null","\"usesPerTurn\":null,\"usesPerGame\":1");
        foreach(var invalid in new[]{exact.Replace("grantPaidHpLossDistance","drawPaidHpLoss"),exact.Replace("\"sourceBind\":\"loss\"","\"sourceBind\":\"other\""),exact.Replace("\"usesPerGame\":1","\"usesPerGame\":2"),exact.Replace("\"minTargets\":0,\"maxTargets\":0","\"minTargets\":1,\"maxTargets\":1")})
        {var rejected=false;try{Load("fixture:wy-bad","\"activations\":["+invalid+"]");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Paid loss rejects wrong producer/bind, repeated reader, target or limited usage.");}
        var triggerRejected=false;try{Load("fixture:wy-bad","\"triggers\":[{\"id\":\"bad\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"optional\":true,\"effects\":"+chain+"}]");}catch(InvalidOperationException){triggerRejected=true;}Require(triggerRejected,"Paid loss cannot originate in another window.");
        Use(f);var frame=f.ResolutionStack.OfType<ProgramSkillFrame>().Single(q=>q.SkillId==Skill);
        var legacyJson=JsonSerializer.Serialize(frame with {HpLossQuantity=null,PaidHpLossReceipt=null});Require(!legacyJson.Contains("HpLossQuantity")&&!legacyJson.Contains("PaidHpLossReceipt"),"Old null frame shape omits both additive fields.");

    }
    public static void QuantityAndNative()
    {
        var(g,r)=Start();var hand=g.State.Players[0].HandCount;Use(g);ReplayFourViews(g,r);Evidence(g,"quantity");
        Require(P(g)!.Choices.Count==4&&P(g)!.Choices.All(c=>c.Cards.Count==0&&c.Targets.Count==0),"Current HP determines one through four actual quantities.");
        var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());var prompt=P(g)!;
        foreach(var bad in new[]{"hp-loss.frame-0.0","hp-loss.frame-0.-1","hp-loss.frame-0.5"})
        {var x=g.Submit(new AnswerPromptCommand(0,prompt.PromptId,new ChoiceId(bad),g.Revision));Require(!x.Accepted&&GameCheckpointJson.Serialize(g.CreateCheckpoint())==before,"Nonoffered quantity rejects without quota, HP or RNG changes.");}
        Quantity(g,2);Settle(g);AssertBenefits(g,2);Require(g.State.Players[0].Hp==2&&g.State.Players[0].HandCount==hand+2,"Actual loss two produces a real two-card Draw.");
        Require(!Actions(g).Any(),"Limited Game usage is consumed once.");ReplayFourViews(g,r);Evidence(g,"paid-two");
        for(var i=0;i<3;i++){var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1);Accept(g,new PlayCardCommand(0,a.CardId!.Value,[1],g.Revision,P(g)!.PromptId));Settle(g);}
        Require(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"Actual Slash Uses consume the base one plus receipt two limit.");ReplayFourViews(g,r);
        var(large,lr)=Start(hp:20);Driver(large,"grow");Settle(large);Use(large);Require(P(large)!.Choices.Count==23,"Quantity has no old fixed four or twenty cap.");Quantity(large,21);Settle(large);AssertBenefits(large,21);ReplayFourViews(large,lr);
        var(native,nr)=Start(native:true);
        for(var i=0;i<80&&!native.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Any(e=>e.SkillId==Skill);i++)Accept(native,new AdvanceOneStepCommand(native.Revision));
        for(var i=0;i<60&&native.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill);i++)Accept(native,new AdvanceOneStepCommand(native.Revision));
        var issued=native.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().First(e=>e.SkillId==Skill);
        Require(issued.OwnerSeat!=3&&!native.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill),"Native ordinary optional activation actually starts and finitely completes.");
        Require(Grants(native).Count(m=>m.Source.OwnerSeat==issued.OwnerSeat)==2,"Actual native owner receives both once-issued benefits.");
        Console.WriteLine("Native ordinary Qimou completed: actor="+issued.OwnerSeat+" revision="+native.Revision);ReplayFourViews(native,nr);Evidence(native,"native");
        ExactLedgerAndLegacyBounds();
    }
    public static void RescueAndGain()
    {
        foreach(var activate in new[]{false,true})
        {
            var(g,r)=Start(peach:true,observer:true,gain:true);var peach=g.State.Players[0].Hand.First(c=>c.Kind==CardKind.Peach).Id;Use(g);Quantity(g,4);
            Reach(g,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);ReplayFourViews(g,r);Evidence(g,"dying-"+activate);
            var receipt=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Skill).PaidHpLossReceipt!;
            Require(receipt is{Requested:4,HpBefore:4,HpAfter:0,ActualLost:4,DrawIssued:false},"Real apply freezes loss before Peach or HP children.");
            Answer(g,c=>c.Cards.SequenceEqual([peach])&&c.Parameters.GetValueOrDefault("response")=="peach");
            for(var i=0;i<100&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill);i++)
            {
                if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")==true)
                {ReplayFourViews(g,r);if(activate)Activate(g);else Skip(g);}
                else if(P(g)?.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);
                else Accept(g,new AdvanceOneStepCommand(g.Revision));
            }
            Require(g.State.Players[0].IsAlive&&g.CardMovements.Count(m=>m.CardId==peach&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Entity Peach pays once across HP/gain child returns.");
            Require(g.Events.Select(e=>e.Payload).OfType<ProgramSkillHpLostEvent>().Count(e=>e.SkillId==Skill&&e.Amount==4)==1,"Loss is not reissued.");AssertBenefits(g,4);ReplayFourViews(g,r);Evidence(g,"rescued-"+activate);
        }
        var(dead,dr)=Start();Use(dead);Quantity(dead,4);
        for(var i=0;i<40&&dead.State.Status!=EngineStatus.Completed;i++)Accept(dead,new AdvanceOneStepCommand(dead.Revision));
        Require(!dead.State.Players[0].IsAlive&&!Grants(dead).Any(),"No legal rescue ends the owner before any remaining Draw/grant.");ReplayFourViews(dead,dr);Evidence(dead,"owner-dead");
    }
    public static void SourceAndLegacy()
    {
        foreach(var stage in new[]{"loss","draw"})
        {
            var(g,r)=Start(source:stage);Use(g);Quantity(g,2);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:wy-source"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
            ReplayFourViews(g,r);Evidence(g,"source-child-"+stage);Activate(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("advanced-value")==Skill));
            Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")==Skill);Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Settle(g);
            Require(!Players(g)[0].SkillGrants.Grants.Any(x=>x.SkillId==Skill),"Actual awakening child removed the issuing grant.");AssertBenefits(g,2);ReplayFourViews(g,r);Evidence(g,"source-lost-"+stage);
        }
        var(old,orr)=Start(source:"loss");Driver(old,"old");Reach(old,p=>p.SkillPrompt?.SkillId=="fixture:wy-source"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Activate(old);Reach(old,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:wy-driver"));
        var hand=old.State.Players[0].HandCount;Answer(old,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:wy-driver");Answer(old,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Settle(old);
        Require(old.State.Players[0].HandCount==hand&&!Grants(old).Any(),"Legacy LoseHp/Draw with no receipt still cancels after real source loss.");ReplayFourViews(old,orr);
        var(unpaid,ur)=Start();Use(unpaid);var grant=Players(unpaid)[0].SkillGrants.Grants.Single(x=>x.SkillId==Skill);Players(unpaid)[0].SkillGrants.RemoveGrant(grant.GrantId);
        Quantity(unpaid,2);Settle(unpaid);Require(!unpaid.Events.Select(e=>e.Payload).OfType<ProgramSkillHpLostEvent>().Any(e=>e.SkillId==Skill),"Host-only unpaid source-loss audit cancels payment, without restoring consumed Game use.");
    }
    public static void ActualTurnLifetime()
    {
        var(g,r)=Start(extraPlay:true);Use(g);Quantity(g,2);Settle(g);var turn=g.CreateSnapshot(0).TurnNumber;AssertBenefits(g,2);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Settle(g);
        Require(g.CreateSnapshot(0).TurnNumber==turn&&!Actions(g).Any()&&Limit(g)==3,"Actual Dangxian and normal Play share the same issued turn modifier and Game use.");ReplayFourViews(g,r);
        Driver(g,"extra");Settle(g);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        for(var i=0;i<120&&!(P(g) is{Kind:DecisionKind.PlayCard,PlayerSeat:0}&&g.CreateSnapshot(0).TurnNumber==turn+1);i++)
        {if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Skip(g);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        Require(g.CreateSnapshot(0).TurnNumber==turn+1&&g.CreateSnapshot(0).CurrentSeat==0&&Limit(g)==1&&!Actions(g).Any(),"Real extra actual turn expires both modifiers without resetting limited use.");ReplayFourViews(g,r);Evidence(g,"extra-turn");
    }
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static void ExactLedgerAndLegacyBounds()
    {
        var(g,r)=Start(gain:true);Use(g);Quantity(g,2);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:wy-gain"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Skill);var p=f.PaidHpLossReceipt!;
        Skip(g);Settle(g);var grants=Grants(g).ToArray();
        var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);
        var method=typeof(GameEngine).GetMethod("HasExactPaidHpLossGrant",BindingFlags.Instance|BindingFlags.NonPublic)!;
        bool Matches(long? seq)=>(bool)method.Invoke(cold,[f,p,seq,2,SkillRuleQuery.OutgoingDistance,-p.ActualLost])!;
        Require(Matches(grants[0].GrantSequence)&&!Matches(grants[1].GrantSequence)&&!Matches(long.MaxValue)&&!Matches(null),"Cold ledger cross-link rejects a wrong sequence, missing actual grant and hidden issued grant.");
        foreach(var amount in new[]{-20,20,-21,21})
        {
            var store=new TurnCardUseEffectStore();var source=new CardUseEffectSource("fixture:legacy","legacy",0,"legacy-instance");
            var modifier=store.GrantRuleModifier(1,0,101,0,source,SkillRuleQuery.OutgoingDistance,SkillRuleOperation.Add,amount);
            var rejected=false;try{typeof(TurnCardUseEffectStore).GetMethod("AssertInvariants",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(store,null);}catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException){rejected=true;}
            Require(rejected==(Math.Abs(amount)>20)&&modifier.PaidHpLossOrigin is null&&!JsonSerializer.Serialize(modifier).Contains("PaidHpLossOrigin"),"Legacy null modifier retains the original static +/-20 boundary and omitted JSON field.");
        }
    }
    private static int Limit(GameEngine g)=>(int)typeof(GameEngine).GetMethod("GetSlashUseLimit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,[Players(g)[0],null])!;
    private static IEnumerable<TurnRuleModifier> Grants(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Select(e=>e.Modifier).Where(m=>m.Source.SkillId==Skill);
    private static void AssertBenefits(GameEngine g,int n){var a=Grants(g).ToArray();Require(a.Length==2&&a[0].Amount==-n&&a[0].Query==SkillRuleQuery.OutgoingDistance&&a[1].Amount==n&&a[1].Query==SkillRuleQuery.SlashLimit&&a[0].EffectIndex!=a[1].EffectIndex,"Both actual X grants issue once from distinct effect keys.");Require(Limit(g)==n+1,"Current query uses actual X.");}
    private static IEnumerable<LegalAction> Actions(GameEngine g)=>g.GetHumanLegalActions().Where(a=>a.ProgramSkillId==Skill);
    private static void Use(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,Skill,Binding,[],[],g.Revision,P(g)!.PromptId));
    private static void Quantity(GameEngine g,int n)=>Answer(g,c=>c.Parameters.GetValueOrDefault("quantity")==n.ToString());
    private static void Driver(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:wy-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static int FailSequence;
    private static void Accept(GameEngine g,GameCommand cmd)
    {
        var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());CommandResult result;
        try{result=g.Submit(cmd);}catch(Exception ex)
        {
            if(Environment.GetEnvironmentVariable("CARD_WY_EVIDENCE") is{Length:>0} root)
            {var d=Path.Combine(root,"actual-submit-"+(++FailSequence));Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"checkpoint-before.json"),checkpoint);File.WriteAllText(Path.Combine(d,"attempt.json"),JsonSerializer.Serialize(cmd,cmd.GetType()));File.WriteAllText(Path.Combine(d,"faulted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));File.WriteAllText(Path.Combine(d,"exception.txt"),ex.ToString());}
            throw;
        }
        Require(result.Accepted,result.Error?.Message??"Command rejected.");
    }
    private static void Evidence(GameEngine g,string label){if(Environment.GetEnvironmentVariable("CARD_WY_EVIDENCE") is not{Length:>0} root)return;var d=Path.Combine(root,label);Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));File.WriteAllText(Path.Combine(d,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));for(var i=0;i<4;i++)File.WriteAllText(Path.Combine(d,"view-"+i+".json"),JsonSerializer.Serialize(g.CreateSnapshot(i)));}
    private static void Settle(GameEngine g){for(var i=0;i<100;i++){if(P(g) is{Kind:DecisionKind.PlayCard,PlayerSeat:0}||g.State.Status==EngineStatus.Completed)return;if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Skip(g);else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Small fixture did not settle: "+P(g)?.Prompt);}
    private static (GameEngine,ContentRegistry) Start(int hp=4,bool native=false,bool peach=false,bool observer=false,bool gain=false,string?source=null,bool extraPlay=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(hp,native,peach,observer,gain,source,extraPlay));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=native?3:0,HumanRole=native?Role.Rebel:Role.Lord,ModeId="identity:classic-wy",UseInteractiveSetup=!native,AdvanceAfterHumanCommands=false,MaxTurns=5,UseInteractiveDiscard=false},r);
        Accept(g,new StartGameCommand());if(!native){Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:wy",g.Revision,P(g)!.PromptId));Settle(g);}return(g,r);
    }
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Act(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":"+effects+"}";
    private sealed class Fixture(int hp,bool native,bool peach,bool observer,bool gain,string? source,bool extraPlay):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wy",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            try{typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryWeiYanContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[b]);}catch(TargetInvocationException ex){throw new InvalidOperationException(ex.InnerException?.ToString(),ex.InnerException);}
            if(extraPlay)b.AddSkill(Classic.Value.GetSkill("classic:dangxian"));
            b.AddSkill(new("fixture:wy-driver","实际准备","实际准备"){Program=Load("fixture:wy-driver","\"activations\":["+Act("damage","[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}]",1)+","+Act("old","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":3}]")+","+Act("extra","[{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}]")+","+Act("grow","[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":3},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":3}]")+"]")});
            if(observer)b.AddSkill(new("fixture:wy-hp","真实回复子窗","真实回复子窗"){Program=Load("fixture:wy-hp","\"triggers\":[{\"id\":\"hp\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"usageScope\":\"turn\",\"usageLimit\":1,\"optional\":true,\"effects\":[{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}]")});
            if(gain)b.AddSkill(new("fixture:wy-gain","真实gain子窗","真实gain子窗"){Program=Load("fixture:wy-gain","\"triggers\":[{\"id\":\"gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"turn\",\"usageLimit\":1,\"optional\":true,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]")});
            if(source is not null)
            {
                for(var i=0;i<6;i++)b.AddSkill(new("fixture:wy-dummy"+i,"拥有技能"+i,"拥有技能"));
                b.AddSkill(new("fixture:wy-source","真实source子窗","真实source子窗"){Program=Load("fixture:wy-source","\"triggers\":[{\"id\":\"source\",\"window\":\""+(source=="loss"?"afterHpLost":"cardsGained")+"\",\"subject\":\"owner\","+(source=="draw"?"\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",":"")+"\"usageScope\":\"turn\",\"usageLimit\":1,\"optional\":true,\"effects\":[{\"op\":\"replaceSkillsOnAwakening\",\"target\":\"owner\",\"skillIds\":[\"standard:none\"]}]}]")});
            }
            var additional=new[]{"boundary:kuanggu"}.Concat(!native?["fixture:wy-driver"]:Array.Empty<string>()).Concat(observer?["fixture:wy-hp"]:Array.Empty<string>()).Concat(gain?["fixture:wy-gain"]:Array.Empty<string>()).Concat(source is not null?new[]{"fixture:wy-source"}.Concat(Enumerable.Range(0,6).Select(i=>"fixture:wy-dummy"+i)):Array.Empty<string>()).Concat(extraPlay?["classic:dangxian"]:Array.Empty<string>()).ToArray();
            b.AddGeneral(new("fixture:wy","魏延","supporter",Skill,"shu",hp,additional,GeneralGender.Male){InitialHp=hp-1});
            for(var i=1;i<4;i++)b.AddGeneral(new("fixture:wy-"+i,"其他"+i,"supporter",native?Skill:"standard:none","wei",4));
            b.AddDeck(new("fixture:wy-deck","固定",4,0,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(peach?"standard:peach":native?"standard:dodge":"standard:slash",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-wy","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:wy-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:wy","fixture:wy-1","fixture:wy-2","fixture:wy-3"]));
        }
    }
}
