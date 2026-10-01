using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinYuanShuChecks
{
    public static void NormalDrawAndDiscardChildren()
    {
        var (g,r)=Start();
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramNormalDrawAdjustedEvent>().Any(), "True normal draw plan must be adjusted.");
        var draw=g.CardMovements.Count(m=>m.To==CardLocation.Hand(0)&&m.Reason.Value=="card.draw");
        Require(g.State.Players[0].HandCount>=7,"Three live factions add three to the normal two draws.");
        Replay(g,r); Accept(g,new UseProgramSkillCommand(0,"fixture:ys-supply","supply",[],[],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0); Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:yongsi");
        var owner=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(owner.OwnedCardSelection?.RequiredCount==3,"DiscardStart freezes live faction count into an owned typed selection.");
        Require(g.CreateSnapshot(1,false).PendingDecision is null,"Other viewers cannot see HE selection.");
        var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());
        Require(!g.Submit(new AnswerPromptCommand(1,P(g)!.PromptId,P(g)!.Choices[0].Id,g.Revision)).Accepted&&checkpoint==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Wrong actor is atomically rejected.");
        Replay(g,r);
        for(var i=0;i<3;i++) Choose(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:ys-pause");
        Require(g.CardMovements.Count(m=>m.From.OwnerSeat==0&&m.To==CardLocation.DiscardPile&&m.Reason.Value.Contains("classic:yongsi"))==3,"Three real HE payments precede child pause.");
        Replay(g,r);Choose(g,_=>true);
        Reach(g,p=>p.Kind==DecisionKind.DiscardCards);
        Require(g.CardMovements.Count(m=>m.From.OwnerSeat==0&&m.To==CardLocation.DiscardPile&&m.Reason.Value.Contains("classic:yongsi"))==3,"Child return does not repay.");
        Replay(g,r);Conserve(g);
    }

    // Source API lifecycle unit boundary: real CharacterSkillSet mutations, not fabricated trigger facts.
    // Separate command-only fixtures above and below prove accepted-command checkpoint recovery.
    public static void ExactSourcesAndIndependentDisable()
    {
        var (g,r)=Start();var players=Players(g);var lord=players.Single(p=>p.Role==Role.Lord);var owner=players[0];
        Require(owner.Role==Role.Rebel&&owner.MaxHp==4,"Projection never changes real identity or identity HP.");
        var source=lord.SkillGrants.Grants.Single(s=>s.SkillId=="classic:xueyi");
        var copy=owner.SkillGrants.Grants.Single(s=>s.SkillId==source.SkillId&&s.LordProjection?.LordGrantId==source.GrantId);
        Require(Has(g,0,source.SkillId)&&copy.LordProjection?.LordSeat==lord.Seat,"Exact effective Lord source is projected.");
        var before=g.Events.Count; lord.SkillGrants.SetEnabled(source.GrantId,false);
        Require(!Has(g,0,source.SkillId)&&g.Events.Count==before,"Pure view instantly invalidates source without rule events.");
        lord.SkillGrants.SetEnabled(source.GrantId,true);Require(Has(g,0,source.SkillId),"Source enable invalidates cross-owner cache.");
        owner.SkillGrants.SetEnabled(copy.GrantId,false);Sync(g);lord.SkillGrants.SetEnabled(source.GrantId,false);Sync(g);lord.SkillGrants.SetEnabled(source.GrantId,true);Sync(g);
        Require(!Has(g,0,source.SkillId)&&!owner.SkillGrants.Grants.Single(s=>s.GrantId==copy.GrantId).IsEnabled,"Local disable survives upstream restart.");
        owner.SkillGrants.SetEnabled(copy.GrantId,true);Require(Has(g,0,source.SkillId),"Local explicit enable restores exact projection.");
        lord.SkillGrants.Grant(new("fixture:second-source",source.SkillId,"fixture:second-instance","fixture:source"));Sync(g);
        Require(owner.SkillGrants.Grants.Count(s=>s.SkillId==source.SkillId&&s.LordProjection is not null)==2,"Distinct true source instances retain distinct provenance.");
        lord.SkillGrants.RemoveGrant(source.GrantId);Sync(g);
        Require(Has(g,0,source.SkillId)&&owner.SkillGrants.Grants.All(s=>s.GrantId!=copy.GrantId),"Losing one source leaves another exact instance.");
        lord.IsAlive=false;Require(!Has(g,0,source.SkillId),"Death invalidates qualification even without Revision change.");Sync(g);
        Require(owner.SkillGrants.Grants.All(s=>s.LordProjection is null),"Rule boundary removes dead-source projections.");
        Require(!JsonSerializer.Serialize(new SkillGrant("a","standard:none","a","b")).Contains("LordProjection"),"Old grant JSON omits nullable ABI.");
    }

    public static void PassiveAndProviderQualified()
    {
        var (g,r)=Start(); var before=Invoke<int>(g,"GetHandLimit",Players(g)[0]);
        Require(Has(g,0,"classic:xueyi")&&Has(g,0,"classic:hujia"),"Both real passive and response skills are owned.");
        Require(before>=6,"Projected Xueyi policy contributes other living Qun hand limit.");
        var choices=Invoke<IEnumerable<int>>(g,"GetFactionDefenseCandidateSeats",0).ToArray();
        Require(choices.Length>0&&choices.All(s=>Players(g)[s].General.FactionId=="wei"&&s!=0),"Projected Hujia requests actual Wei providers for this non-Lord owner.");
        var owner=Players(g)[0];var hujia=owner.SkillGrants.Grants.Single(s=>s.SkillId=="classic:hujia"&&s.LordProjection is not null);
        Require(Invoke<bool>(g,"HasSkillRoleQualification",owner,hujia.SkillId,hujia.SkillInstanceId,Role.Lord),"Host exact projected instance qualifies.");
        Require(!Invoke<bool>(g,"HasSkillRoleQualification",owner,hujia.SkillId,"missing-instance",Role.Lord),"Another instance never borrows eligibility.");
        Replay(g,r);Conserve(g);
        var (plain,pr)=Start(false);
        Require(!Has(plain,0,"classic:hujia")&&!Has(plain,0,"classic:xueyi"),"Non-Lord printed skills remain unavailable without projection capability.");Replay(plain,pr);
    }

    public static void CommandLossRegrantAndSuppression()
    {
        var(g,r)=Start();var lord=Players(g).Single(p=>p.Role==Role.Lord);
        Driver(g,"loss");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(!Has(g,0,"classic:hujia")&&Players(g)[0].SkillGrants.Grants.All(s=>s.LordProjection is null),"Actual owner skill loss removes only derived relations.");Replay(g,r);
        Driver(g,"grant");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(Has(g,0,"classic:hujia"),"Actual acquired capability grant recreates the exact relation.");Replay(g,r);
        Driver(g,"suppress",[lord.Seat]);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("choice")=="classic:xueyi"));
        Choose(g,c=>c.Parameters.GetValueOrDefault("choice")=="classic:xueyi");
        Require(!Has(g,0,"classic:xueyi")&&Has(g,0,"classic:hujia"),"Real suppression command immediately invalidates just that Lord source.");Replay(g,r);Conserve(g);
    }

    public static void DynamicFactionRecountAndEmptyPayment()
    {
        var(g,r)=Start();var target=Players(g).First(p=>p.Seat!=0&&p.Role!=Role.Lord&&p.General.FactionId is "wei" or "wu");
        Driver(g,"death",[target.Seat]);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(!g.State.Players[target.Seat].IsAlive,"Real HP loss removes a unique living faction before DiscardStart.");Replay(g,r);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:yongsi");
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().OwnedCardSelection?.RequiredCount==2,"Discard start independently recounts after actual death.");Replay(g,r);
        (g,r)=Start();Driver(g,"empty");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramBindingResolvedEvent>().Any(e=>e.SkillId=="classic:yongsi"&&e.BindingId=="faction-discard"),"Empty legal HE payment completes instead of inventing cost.");Replay(g,r);Conserve(g);
    }

    public static void ActualResponseProviderChain()
    {
        var(g,r)=Start();var target=Players(g).Single(p=>p.Role==Role.Lord);
        Driver(g,"poke",[target.Seat]);Reach(g,p=>p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="faction-defense-request"));
        Require(g.ResolutionStack.OfType<CardUseFrame>().Any(),"Actual nested virtual Slash has its own real card-use frame.");Replay(g,r);
        Choose(g,c=>c.Parameters.GetValueOrDefault("response")=="faction-defense-request");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.Events.Select(e=>e.Payload).OfType<FactionDefenseRequestedEvent>().Any(),"Copied Hujia opens true provider request under non-Lord identity.");
        Require(g.CardMovements.Any(m=>m.From.OwnerSeat!=0&&m.To==CardLocation.Processing&&m.Reason.Value=="card.respond"),"Actual provider pays a physical response entity.");Replay(g,r);Conserve(g);
    }

    public static void ProjectionGameplayFingerprint()
    {
        var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:projection-hash","revision":1,"lordSkillProjection":true,"triggers":[{"id":"draw","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""";
        const string presentation="""{"schemaVersion":3,"skills":{"fixture:projection-hash":{"name":"投影","description":"真实规则能力"}}}""";
        var yes=SkillProgramCatalog.Load(rules,presentation).Programs["fixture:projection-hash"];
        var no=SkillProgramCatalog.Load(rules.Replace("\"lordSkillProjection\":true","\"lordSkillProjection\":false"),presentation).Programs["fixture:projection-hash"];
        Require(yes.LordSkillProjection&&!no.LordSkillProjection&&yes.GameplayHash!=no.GameplayHash,"Capability is parsed gameplay data and participates in canonical fingerprint.");
    }

    public static void ActualHostAndContribution()
    {
        var(g,r)=Start();var action=g.GetHumanLegalActions().First(a=>a.ProgramSkillId=="classic:zhaofu");
        Accept(g,new UseProgramSkillCommand(0,action.ProgramSkillId!,action.ProgramActivationId!,[],[1],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramGameFactionAttackRangeTargetsGrantedEvent>().Any(e=>e.OwnerSeat==0),"Actual projected Zhaofu passes both menu and execution host Lord gate.");Replay(g,r);
        var contribution=g.GetHumanLegalActions().First(a=>a.ProgramSkillId=="classic:huangtian"&&a.ProgramSkillOwnerSeat is {} seat&&g.State.Players[seat].Role!=Role.Lord);
        var recipient=contribution.ProgramSkillOwnerSeat!.Value;var card=contribution.SelectableCardIds!.First();
        Accept(g,new UseProgramSkillCommand(0,"classic:huangtian",contribution.ProgramActivationId!,[card],[recipient],g.Revision,P(g)!.PromptId){SkillOwnerSeat=recipient});
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.CardMovements.Any(m=>m.CardId==card&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(recipient)),"True contribution pays an entity to the copied skill owner, not to the actual Lord.");Replay(g,r);Conserve(g);
        var receipt=g.Events.Select(e=>e.Payload).OfType<ProgramSkillContributionResolvedEvent>().Last();
        Require(receipt.SkillInstanceId is {} instance&&Players(g)[recipient].SkillGrants.Grants.Any(s=>s.SkillInstanceId==instance&&s.LordProjection is not null),"Projected contribution freezes its exact instance before payment.");
        Require(!JsonSerializer.Serialize(new ProgramSkillContributionResolvedEvent(1,0,1,"classic:huangtian","contribute",1,CardKind.Dodge,Suit.Heart)).Contains("SkillInstanceId"),"Old contribution payload omits nullable field.");
    }

    public static void QualifiedAwakeningAndPersistentLocalLoss()
    {
        var(g,r)=Start();Driver(g,"suppress-awaken",[Players(g).Single(p=>p.Role==Role.Lord).Seat]);
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("choice")=="classic:zhaofu"));
        Choose(g,c=>c.Parameters.GetValueOrDefault("choice")=="classic:zhaofu");
        Reach(g,p=>p.SkillPrompt?.SkillId=="ol:dili");
        Require(Players(g)[0].SkillGrants.Grants.Any(s=>s.SkillId=="classic:zhaofu"&&s.LordProjection is not null&&s.IsEnabled),"Upstream suppression retains local enabled relationship.");
        Require(!Invoke<IReadOnlyList<string>>(g,"AdvancedOwnedSkillIds",Players(g)[0]).Contains("classic:zhaofu"),"Upstream-disabled relation is excluded from awakening count.");Replay(g,r);
        Reach(g,p=>p.SkillPrompt?.SkillId=="ol:dili");
        Require(P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("advanced-value")!="classic:zhaofu"),"Real awakening candidate list excludes the disabled relation.");Replay(g,r);
        Choose(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="classic:huangtian");
        Choose(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(!Has(g,0,"classic:huangtian")&&Players(g)[0].SkillGrants.Grants.Any(s=>s.SkillId=="classic:huangtian"&&s.LordProjection is not null&&!s.IsEnabled),"Actual awakening loss disables derived source locally rather than deleting then regranting.");
        Driver(g,"one");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(!Has(g,0,"classic:huangtian"),"Subsequent real rule synchronization does not restore the paid skill loss.");Replay(g,r);Conserve(g);
        var(plain,pr)=Start(false);var ordinary=Players(plain)[0];
        Require(Invoke<IReadOnlyList<string>>(plain,"AdvancedOwnedSkillIds",ordinary).SequenceEqual(ordinary.SkillGrants.EffectiveSkillIds),"Nonprojection ownership count preserves old semantics.");
    }

    public static void ShortfallUsesOnlyActualAvailableEntity()
    {
        var(g,r)=Start();Driver(g,"empty");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Driver(g,"one");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.State.Players[0].HandCount==1,"One actual entity remains for a three-faction cost.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:yongsi");
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().OwnedCardSelection?.RequiredCount==1,"Required cost is min(X,actual available), not an impossible three-card selection.");
        Replay(g,r);Choose(g,c=>c.Cards.Count==1);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:ys-pause");
        Require(g.CardMovements.Count(m=>m.From.OwnerSeat==0&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.classic:yongsi.MoveBoundCards")==1,"Only the available physical entity is paid.");Replay(g,r);Conserve(g);
    }

    private static bool Has(GameEngine g,int seat,string skill)=>g.CreateSnapshot(seat,true).Players[seat].Skills?.Any(s=>s.ContentId==skill)==true;
    private static List<CharacterState> Players(GameEngine g)=>(List<CharacterState>)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static T Invoke<T>(GameEngine g,string name,params object?[] args)=>(T)typeof(GameEngine).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,args)!;
    private static void Sync(GameEngine g)=>typeof(GameEngine).GetMethod("SynchronizeLordSkillProjections",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,null);
    private static (GameEngine,ContentRegistry) Start(bool projection=true)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(projection));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Rebel,ModeId="identity:classic-yuan-shu-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:ys-owner",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return(g,r);
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> goal){for(var i=0;i<100;i++){if(P(g) is {} p){if(goal(p))return;if(p.Kind==DecisionKind.ProgramTrigger)Choose(g,_=>true);else if(p.Kind==DecisionKind.DiscardCards&&p.PlayerSeat==0)Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));else if(p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Play; moves="+JsonSerializer.Serialize(g.CardMovements.TakeLast(8))+" events="+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().TakeLast(8)));else Accept(g,new AdvanceOneStepCommand(g.Revision));}else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixture boundary not reached");}
    private static void Choose(GameEngine g,Func<PromptChoice,bool> pred){var p=P(g)!;var c=p.Choices.First(pred);Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Driver(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:ys-supply",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(x.Accepted,x.Error?.Message??"Rejected fixture command");}
    private static void Replay(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s,false))==SnapshotJson.Serialize(copy.CreateSnapshot(s,false)))&&g.CardMovements.SequenceEqual(copy.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(copy.ResolutionStack),"Every viewer and typed child restore via commands");}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Count==80&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==80,"Entity zone uniqueness");
    private static void Require(bool x,string why){if(!x)throw new InvalidOperationException(why);}
    private sealed class Fixture(bool projection):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-yuan-shu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ys-supply","revision":1,"activations":[{"id":"suppress-awaken","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"suppressGeneralSkill","target":"selectedTarget"},{"op":"grantSkills","target":"owner","skillIds":["ol:dili"]}]},{"id":"awaken","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:dili"]}]},{"id":"one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},{"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:weidi"],"sourceBind":"standard:none"}]},{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:weidi"]}]},{"id":"suppress","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"suppressGeneralSkill","target":"selectedTarget"}]},{"id":"death","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":5}]},{"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand","equipment"]}]},{"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]}]},{"id":"fixture:ys-retaliate","revision":1,"triggers":[{"id":"retaliate","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},{"id":"fixture:ys-pause","revision":1,"triggers":[{"id":"pause","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","optional":false,"movementReasons":["skill-program.classic:yongsi.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","options":[{"id":"continue"}],"resultBind":"pause"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:ys-retaliate":{"name":"反击","description":"实际虚拟杀"},"fixture:ys-supply":{"name":"补牌","description":"实际补牌"},"fixture:ys-pause":{"name":"支付子链","description":"真实移动暂停","optionLabels":{"continue":"继续"}}}}""");
            foreach(var pair in catalog.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value,SelectionWeights=pair.Key=="fixture:ys-supply"?new Dictionary<Role,double>{[Role.Lord]=-1000}:null});
            b.AddGeneral(new("fixture:ys-owner","袁术机制","supporter","classic:yongsi","qun",4,projection?["classic:weidi","fixture:ys-pause","fixture:ys-supply","classic:xueyi","classic:hujia"]:["fixture:ys-pause","fixture:ys-supply","classic:xueyi","classic:hujia"]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:ys-{i}","源"+i,"supporter","standard:none",i==1?"qun":i==2?"wei":"wu",4,["classic:xueyi","classic:hujia","fixture:ys-retaliate","classic:zhaofu","classic:huangtian","classic:weidi"]));
            b.AddDeck(new("fixture:ys-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("standard:dodge",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-yuan-shu-check","袁术机制",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:ys-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:ys-owner","fixture:ys-1","fixture:ys-2","fixture:ys-3"]));
        }
    }
}
