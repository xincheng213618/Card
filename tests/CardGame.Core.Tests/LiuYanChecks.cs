using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuYanChecks
{
    private const string Owner="fixture:ly-owner",Driver="fixture:ly-driver",Quiet="fixture:ly-quiet",Health="fixture:ly-health",Watch="fixture:ly-watch",Source="fixture:ly-source",Mode="identity:classic-liu-yan-fixture";
    public static void ActualTargetsPostPaymentAndEffectiveHandCold()
    {
        foreach(var scenario in new[]{"draw","barrage"})
        {
            var(g,r)=Start(scenario);var kind=scenario=="draw"?CardKind.DrawTwo:CardKind.ArrowBarrage;var before=V(g).HandCount;Native(g,kind);
            Reach(g,()=>Optional(g));var parent=g.ResolutionStack.OfType<CardUseFrame>().Last();var declared=parent.Action!.EffectiveDesignatedTargetSeats.Count;
            Need(declared==(scenario=="draw"?1:3),"The native declaration has its real self or global role targets.");g=Cold(g,r);Activate(g);Play(g);
            var drawn=E<LiuYanTusheDrawnEvent>(g).Single();Need(drawn.Requested==declared&&drawn.Count==declared&&E<LiuYanStartedEvent>(g).Single().OriginalUseId==parent.Id,"Tushe draws once from the actual finalized role-target count after the physical use has been paid.");
            Need(V(g).HandCount>=before-1+declared&&g.CardMovements.Any(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use),"The actual native cost and reward are real card movements.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("diamond");var card=V(g).Hand.First().Id;Limu(g,card);Play(g);
            Need(E<LiuYanTusheDrawnEvent>(g).Length==0,"Unpaid remaining effective basic hand cards block Tushe even after a basic diamond was used as Indulgence.");
            Use(g,"clear-judgment");Play(g);foreach(var id in V(g).Hand.Skip(1).Select(c=>c.Id).ToArray()){Use(g,"drop",[id]);Play(g);}
            var last=V(g).Hand.Single().Id;Limu(g,last);Reach(g,()=>Optional(g));Need(V(g).HandCount==0,"Qualification reads the actual hand after the last basic material leaves it.");g=Cold(g,r);Activate(g);Play(g);
            Need(E<LiuYanTusheDrawnEvent>(g).Single().Requested==1&&E<LiuYanLimuReturnedEvent>(g).Length==2,"Self-targeted Limu can truly trigger Tushe, and both native uses return once.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("equipment");Native(g,CardKind.Crossbow);Play(g);Need(E<LiuYanStartedEvent>(g).Length==0,"An actual equipment use with no basic hand cards does not trigger Tushe.");_=Cold(g,r);
        }
    }
    public static void LimuHandEquipmentGrainNativeRecoveryCold()
    {
        foreach(var scenario in new[]{"diamond","lion","ox"})
        {
            var(g,r)=Start(scenario);Use(g,"lose");Play(g);Use(g,"lose");Play(g);
            int card;CardLocation from;int? grain=null;
            if(scenario=="diamond"){card=V(g).Hand.First().Id;from=CardLocation.Hand(0);}
            else
            {
                var kind=scenario=="lion"?CardKind.SilverLion:CardKind.WoodenOx;Native(g,kind);Play(g);card=V(g).Equipment.Single(c=>c.Kind==kind).Id;from=CardLocation.Equipment(0);
                if(scenario=="ox")
                {var storage=g.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.UseEquipmentEffect&&a.EquipmentKind==CardKind.WoodenOx);grain=storage.SelectableCardIds.First();Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);}
            }
            var hp=V(g).Hp;Limu(g,card);Play(g);var o=E<LiuYanLimuIssuedEvent>(g).Single().Origin;
            Need(o.CardId==card&&o.From==from&&E<LiuYanLimuPaidEvent>(g).Single().Origin==o&&g.CardMovements.Count(m=>m.CardId==card&&m.From==from&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)==1,"Limu really pays exactly its original hand or equipment entity once.");
            Need(V(g).Judgment.Any(c=>c.Id==card)&&E<DelayedCardPlacedEvent>(g).Any(e=>e.ResolutionId==o.CardUseFrameId&&e.CardKind==CardKind.Indulgence)&&E<LiuYanLimuReturnedEvent>(g).Length==1&&E<LiuYanLimuRecoveryIssuedEvent>(g).Length==1,"The actual physical Indulgence is placed before the once-only native recovery continuation.");
            Need(V(g).Hp==hp+(scenario=="lion"?2:1),"Native Silver Lion removal and the separate one-point Limu recovery both keep their actual HP effects.");
            if(grain is{}id)Need(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)==1,"Using the actual equipped Ox preserves its genuine grain spill.");
            var state=State(g);var result=g.Submit(new UseProgramSkillCommand(0,"ol:limu","self-indulgence",[V(g).Hand.First().Id],[],g.Revision,P(g)!.PromptId));Need(!result.Accepted&&State(g)==state,"An existing native Indulgence rejects another Limu without a second payment.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("grain");Native(g,CardKind.WoodenOx);Play(g);var a=g.GetHumanLegalActions().Single(x=>x.Kind==LegalActionKind.UseEquipmentEffect&&x.EquipmentKind==CardKind.WoodenOx);
            var id=a.SelectableCardIds.First();Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[id],[],g.Revision,P(g)!.PromptId));Play(g);
            g=Cold(g,r);Limu(g,id);Play(g);
            Need(E<LiuYanLimuIssuedEvent>(g).Single().Origin.From==CardLocation.WoodenOxGrain(0)&&V(g).Judgment.Any(c=>c.Id==id)&&V(g).Equipment.Any(c=>c.Kind==CardKind.WoodenOx),"A genuine accessible diamond Ox grain is eligible native Limu material; the Ox itself remains equipped.");_=Cold(g,r);
        }
    }
    public static void LiveJudgmentRangeAndPerTargetQuotaCold()
    {
        var(g,r)=Start("range");Use(g,"draw");Play(g);var diamond=V(g).Hand.First(c=>c.Kind==CardKind.Dodge).Id;Limu(g,diamond);Play(g);
        Native(g,CardKind.Slash,1);Play(g);Need(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])),"A live judgment card allows another real Slash only against a current attack-range other character.");
        Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])),"Limu does not turn a distance-two character outside the current attack range into a legal Slash target.");
        g=Cold(g,r);Native(g,CardKind.Slash,1);Play(g);Need(E<CardUsedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.CardKind==CardKind.Slash)==2,"Two independently paid native Slashes execute under the exact per-target allowance.");
        Use(g,"clear-judgment");Play(g);Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"Removing the actual judgment card immediately restores the ordinary already-spent Slash quota.");_=Cold(g,r);
        {
            (g,r)=Start("distance");Use(g,"draw");Play(g);Native(g,CardKind.QinggangSword);Play(g);diamond=V(g).Hand.First(c=>c.Kind==CardKind.Dodge).Id;Limu(g,diamond);Play(g);
            Need(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Snatch&&a.TargetSeats.SequenceEqual([2])),"A current range-two weapon plus judgment makes the distant in-range other character a native Snatch target.");
            var weapon=V(g).Equipment.Single(c=>c.Kind==CardKind.QinggangSword).Id;Use(g,"drop",[weapon]);Play(g);
            Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Snatch&&a.TargetSeats.SequenceEqual([2])),"Actual equipment loss removes Limu's distance allowance as soon as that character falls outside the current range.");_=Cold(g,r);
        }
    }
    public static void CounterspellPaidChildrenLossDeathAndPrivateCold()
    {
        {
            var(g,r)=Start("counterspell");Limu(g,V(g).Hand.First().Id);
            Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification}p&&p.Choices.Any(c=>c.Cards.Count==1));
            var p=P(g)!;Need(g.CreateSnapshot(1).PendingDecision is null,"A real responder's counterspell cards remain private to that responder.");
            var state=State(g);var wrong=g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices.First(c=>c.Cards.Count==1).Id,g.Revision));Need(!wrong.Accepted&&State(g)==state,"A foreign seat cannot answer the current private native counterspell prompt.");
            g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(E<NullificationRespondedEvent>(g).Any()&&E<LiuYanLimuReturnedEvent>(g).Length==1&&E<LiuYanLimuRecoveryIssuedEvent>(g).Length==1,"Native counterspell processing and all response descendants still return to Limu's independent then-recover step once.");_=Cold(g,r);
        }
        foreach(var scenario in new[]{"loss","death"})
        {
            var(g,r)=Start(scenario);var id=V(g).Hand.First().Id;Limu(g,id);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            var use=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.LiuYanLimu is not null);Need(g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)==1,"The exact physical payment has already happened before the native child.");g=Cold(g,r);Continue(g);PlayOrCompleted(g);
            Need(E<LiuYanLimuReturnedEvent>(g).Count(e=>e.Origin.CardUseFrameId==use.Id)==1&&E<LiuYanCompletedEvent>(g).Count(e=>e.Operation==SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)==1,"Paid native children preserve the original owning frame and return once after real source loss or death.");
            if(scenario=="death")Need(!V(g).IsAlive&&g.State.Winner==Winner.None&&E<LiuYanLimuRecoveryIssuedEvent>(g).Length==0,"A genuinely dead non-Lord payer cannot receive a new recovery, while its paid native use still terminates.");
            else Need(E<HpConditionalSkillGrantChangedEvent>(g).Any(e=>e.OwnerSeat==0&&e.SkillId=="ol:limu"&&!e.Added)&&E<LiuYanLimuRecoveryIssuedEvent>(g).Length==1,"Actual HP loss removes the original conditional skill generation, and its already-issued then-recover right survives.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Optional(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:tushe"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CreateSnapshot(0))+g.Events.Count;
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Liu Yan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"The real AI advances its own decisions.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing actual Liu Yan choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Limu(GameEngine g,int id)=>Accept(g,new UseProgramSkillCommand(0,"ol:limu","self-indulgence",[id],[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,CardKind kind,int? target=null)
    {var a=g.GetHumanLegalActions().FirstOrDefault(a=>(a.PlayedCardKind??(a.CardId is{}id?V(g).Hand.FirstOrDefault(c=>c.Id==id)?.Kind:null))==kind&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));Need(a is not null,"Missing native "+kind+": "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void PlayOrCompleted(GameEngine g)=>Reach(g,()=>E<LiuYanCompletedEvent>(g).Any(e=>e.Operation==SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.LiuYan is not null));
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.LiuYan is not null)?.LiuYan?.Stage};hand={string.Join(',',V(g).Hand.Select(c=>c.Kind))}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual Liu Yan boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p is{Kind:DecisionKind.PlayCard})Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
            else if(p is{Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is not null)throw new InvalidOperationException("Unexpected actual Liu Yan boundary: "+Diagnostic(g));
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Liu Yan boundary: "+Diagnostic(g));
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var next=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(next),"Cold replay preserves the exact original source, physical material, target count and once-only native return.");return next;}
    private static(GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-liu-yan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:liu-yan");
            var defs=new List<string>
            {
                $$$"""
                {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"drop","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"clear-judgment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["judgment"]}]}]}
                """,
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-hp","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}"""
            };
            var skills=new List<string>{Driver,Health,"ol:tushe"};
            if(scenario=="loss"){defs.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"full","skillId":"ol:limu","minimumHp":9}]}""");skills.Add(Source);}else skills.Add("ol:limu");
            if(scenario is"loss"or"death")
            {
                var tail=scenario=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":"{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-paid","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["indulgence"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{{tail}}]}]}""");skills.Add(Watch);
            }
            var nodes=defs.Select(d=>JsonNode.Parse(d)!).ToArray();var labels=nodes.ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生刘焉小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,labels[id].name,"实际原生夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:ly-pick-owner","本人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"实际刘焉","ol-liu-yan","fixture:ly-pick-owner","qun",8,skills));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:ly-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var pick="fixture:ly-pick-"+i;b.AddSkill(new(pick,"其他角色","固定公开选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"实际其他角色","supporter",pick,"wei",12,[Quiet]));}
            foreach(var(id,kind)in new[]{("barrage",CardKind.ArrowBarrage),("lion",CardKind.SilverLion),("ox",CardKind.WoodenOx),("crossbow",CardKind.Crossbow),("sword",CardKind.QinggangSword),("nullification",CardKind.Nullification),("snatch",CardKind.Snatch)})b.AddCard(new("fixture:ly-"+id,id,"真实牌","原生实体",kind));
            var ids=scenario switch{"draw"=>new[]{"standard:draw_two"},"barrage"=>["fixture:ly-barrage"],"equipment"=>["fixture:ly-crossbow"],"lion"=>["fixture:ly-lion"],"ox"or"grain"=>["fixture:ly-ox"],"range"=>["standard:slash","standard:dodge"],"distance"=>["fixture:ly-sword","standard:dodge","fixture:ly-snatch"],"counterspell"=>["fixture:ly-nullification"],_=>["standard:dodge"]};
            b.AddDeck(new("fixture:ly-deck","固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(ids[i%ids.Length],Suit.Diamond,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"真实图射立牧",4,4,scenario=="death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:ly-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
