using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouGongsunZanMobileChecks
{
    private const string Hero="mou:gongsun-zan-mobile",Driver="fixture:mougsz-driver",Quiet="fixture:mougsz-quiet";
    public static void RealHuOriginalPlayedAndHandConversionsWithRoundChargeCold()
    {
        var s=Scene.Start();s.Reach(()=>s.Branch("offense"));s.Need(s.Charge()==2,"The formal mobile general initializes charge2/4.");s.Cold();s.Choose("offense",2);s.Play();
        var hu=s.Hu().ToArray();s.Need(hu.Length==2&&hu.All(c=>c.Kind==CardKind.Slash)&&s.Charge()==0&&s.V().Hand.All(c=>hu.All(h=>h.Id!=c.Id))&&s.Actions().Any(a=>a.Kind==LegalActionKind.Slash&&a.CardId==hu[0].Id&&a.TargetSeats.SequenceEqual([2])),
            "Real matching Deck entities become public Hu, never Hand. The issued offensive distance makes the opposite seat a legal native Slash target.");
        s.Accept(new UseProgramSkillCommand(0,Driver,"direct-duel",[],[3,0],s.G.Revision,s.P!.PromptId));
        s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});s.Answer(c=>c.Cards.SequenceEqual([hu[0].Id])&&!c.Parameters.ContainsKey("conversion-binding-id"));s.Play();
        s.Need(s.E<NativePublicPileOriginalPaidEvent>().Any(e=>e.CardId==hu[0].Id&&e.Source.SkillId=="mou-mobile:yicong"&&e.Direction==NativePublicPileDirection.Played)&&s.E<MouGongsunZanMobileStartedEvent>().All(e=>e.Op!=SkillProgramEffectOp.ResolveMouGongsunZanQiaomeng),
            "A real Duel counter-response consumes the actual original Hu Slash as pure Played, without borrowing Shuliang's quota or becoming Slash damage.");
        s.Native(hu[1].Id,CardKind.Slash,2);s.Reach(()=>s.Branch("charge"));s.Choose("charge");s.Play();
        s.Need(s.Charge()==3&&s.Hu().Length==0&&s.E<NativePublicPileOriginalPaidEvent>().Count(e=>e.Source.SkillId=="mou-mobile:yicong")==2&&s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1),
            "The same actual turn may pay another original Hu for true Slash Use. Qiaomeng gains3 real charge after native damage and shares no foreign skill's once-per-turn quota.");
        s.End();s.Reach(()=>s.G.State.TurnNumber>=5&&s.Branch("defense"));s.Choose("defense",3);s.Play();var defensive=s.Hu().ToArray();
        s.Need(defensive.Length==3&&defensive.All(c=>c.Kind==CardKind.Dodge)&&s.Charge()==0&&s.E<RoundEndedEvent>().Any(e=>e.RoundNumber==1)&&
            !s.Actions().Any(a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource is null&&a.TargetSeats.SequenceEqual([2])),
            "The next real Round closes the old offensive distance. Spending3 issues defense and obtains three actual Dodge Hu; it does not retain the prior outgoing bonus.");
        s.Native(defensive[0].Id,CardKind.Slash,1,"hu-as-slash");s.Reach(()=>s.Branch("discard"));s.Answer(c=>c.Parameters.GetValueOrDefault("branch")=="discard"&&c.Parameters.GetValueOrDefault("source-zone")=="Hand");s.Play();
        var discard=s.E<MouGongsunZanMobileDiscardedEvent>().Single();
        s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e=>e.InitiatorSeat==0&&e.ExecutorSeat==0&&e.ActualCount==1&&e.Reason.Value=="skill-program.mou-mobile:qiaomeng.discard")&&
            s.G.CardMovements.Count(m=>m.Sequence==discard.Sequence&&m.CardId==discard.CardId&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile)==1&&
            s.E<MouGongsunZanMobileDrawReturnedEvent>() is[{Requested:1,Actual:1}]&&
            s.E<MouGongsunZanHuActionBoundEvent>().Any(e=>e.CardId==defensive[0].Id),
            "An otherwise legal Hand-only single-card conversion pays the original Hu location. The other Qiaomeng branch performs one real foreign Hand logical discard and only then a native Draw1.");
        s.Incoming();s.Cold();s.Answer(c=>c.Cards.SequenceEqual([defensive[1].Id])&&!c.Parameters.ContainsKey("conversion-binding-id"));s.Play();
        s.Need(s.E<NativePublicPileOriginalPaidEvent>().Any(e=>e.CardId==defensive[1].Id&&e.Direction==NativePublicPileDirection.Use),
            "A true native Dodge response uses its exact Hu entity independently of the earlier Slash/Played payments.");
        s.Incoming();var handSlash=s.V().Hand.First(c=>c.Kind==CardKind.Slash).Id;var pair=new[]{defensive[2].Id,handSlash}.Order().ToArray();
        s.Answer(c=>c.Cards.Order().SequenceEqual(pair)&&c.Parameters.GetValueOrDefault("conversion-binding-id")=="two-card-dodge");s.Play();
        s.Need(s.Hu().Length==0&&s.E<MouGongsunZanHuMaterialPaidEvent>().Any(e=>e.CardId==defensive[2].Id)&&
            s.G.CardMovements.Count(m=>m.CardId==defensive[2].Id&&m.From.Zone==CardZoneKind.PublicPersistentPile&&m.To==CardLocation.Processing)==1&&
            s.G.CardMovements.Count(m=>m.CardId==handSlash&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1&&
            s.E<MouGongsunZanHuActionBoundEvent>().Count(e=>e.CardId==defensive[2].Id)==1,
            "A genuinely published multi-Hand-material response can combine Hu and true Hand, preserves their different physical origins, and binds the actual converted response action exactly once.");
        s.Cold();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G=game;public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat=0)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public CardSnapshot[] Hu()=> (V().PublicPersistentPileCards??[]).Concat(V().PublicPersistentPiles?.SelectMany(p=>p.Cards)??[]).DistinctBy(c=>c.Id).ToArray();
        public int Charge()=>V().Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.MouGongsunZanCharge)?.Count??0;
        public IReadOnlyList<LegalAction> Actions()=>G.GetHumanLegalActions();
        public bool Branch(string branch)=>P is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="mou-gongsun-zan-mobile"&&c.Parameters.GetValueOrDefault("branch")==branch);
        public void Choose(string branch,int? n=null)=>Answer(c=>c.Parameters.GetValueOrDefault("branch")==branch&&(n is null||c.Parameters.GetValueOrDefault("amount")==n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Native(int id,CardKind kind,int target,string? conversion=null)
        {
            var physical=V().Hand.Concat(Hu()).Single(c=>c.Id==id);
            var a=Actions().FirstOrDefault(a=>a.CardId==id&&(a.PlayedCardKind??physical.Kind)==kind&&a.TargetSeats.SequenceEqual([target])&&(conversion is null?a.ConversionSource is null:a.ConversionSource?.BindingId==conversion));
            Need(a is not null,"A real published action must match the actual entity, output kind, conversion and native target.");
            Accept(new PlayCardCommand(0,id,[target],G.Revision,P!.PromptId,a!.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
        }
        public void Incoming()
        {
            Accept(new UseProgramSkillCommand(0,Driver,"incoming",[],[1],G.Revision,P!.PromptId));
            Reach(()=>P is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Targets.SequenceEqual([0])));
            Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-slash-offer"&&c.Targets.SequenceEqual([0]));Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
        }
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Only an actually published human-owned answer is submitted.");Accept(new AnswerPromptCommand(0,p.PromptId,c!.Id,G.Revision));}
        public void Accept(GameCommand c){try{var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Actual native command rejected.");}catch(Exception e){throw new InvalidOperationException(e.Message+Diag(),e);}}
        public void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<220;i++)
            {
                if(done())return;Need(G.State.Winner==Winner.None,"Unexpected real cutoff at "+line);
                if(P is not{PlayerSeat:0}p)Accept(new AdvanceOneStepCommand(G.Revision));
                else if(p.Kind==DecisionKind.PlayCard)End();
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"))Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException("Unexpected native human boundary at "+line+Diag());
            }
            throw new InvalidOperationException("Bounded mobile Gongsun Zan flow not reached at "+line+Diag());
        }
        public void Cold(){var before=SnapshotJson.Serialize(G.CreateSnapshot(0));G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==SnapshotJson.Serialize(G.CreateSnapshot(0)),"The actual owning choice/payment and entity pile survive native Cold without moving or spending again.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diag());}
        private string Diag()=>JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.Phase,Prompt=P,Owner=V(),Actions=Actions(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=G.Events.Where(e=>e.Payload.GetType().Name.StartsWith("MouGongsunZan",StringComparison.Ordinal)||e.Payload is NativePublicPileOriginalPaidEvent or NativeLogicalDiscardInvocationRecordedEvent or CardUseDeclaredEvent or CardActionAcceptedEvent or RoundStartedEvent or RoundEndedEvent).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(7,1,"mougsz-real",Hero,4,0,0,0,0,[Driver],[new(CardKind.Duel,Suit.Spade,7),new(CardKind.Dodge,Suit.Heart,6),new(CardKind.Slash,Suit.Club,5)],[],
                [new("fixture:mougsz-enemy-a",Hp:12){StartingHand=[]},new("fixture:mougsz-enemy-b",Hp:12){StartingHand=[]}],[new("fixture:mougsz-ally",Hp:12){StartingHand=[new(CardKind.Slash,Suit.Spade,7)]}],new string('e',64));
            var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);var s=new Scene(g,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-gongsun-zan-mobile",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var k in new[]{CardKind.Slash,CardKind.Dodge,CardKind.Duel}){var c=CardCatalog.Get(k);b.AddCard(new("fixture:mougsz-"+k,c.DisplayName,c.CategoryName,c.Description,k));}
            var defs=new object[]{
                new{id=Quiet,revision=1,modifiers=new[]{new{id="offer-range",query="attackRange",operation="set",value=4,priority=0}},triggers=new[]{new{id="quiet",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}}}},
                new{id=Driver,revision=1,activations=new object[]{new{id="incoming",minCards=0,maxCards=0,sourceZones=new[]{"hand"},minTargets=1,maxTargets=1,targetKind="otherLiving",usesPerTurn=(int?)null,effects=new[]{new{op="offerVirtualSlashOrDraw",target="selectedTarget"}}},new{id="direct-duel",minCards=0,maxCards=0,sourceZones=new[]{"hand"},minTargets=2,maxTargets=2,targetKind="anyLiving",usesPerTurn=(int?)null,effects=new[]{new{op="startVirtualDuel",target="owner"}}}},viewAs=new object[]{
                    new{id="hu-as-slash",inputKinds=new[]{"dodge"},inputSuits=Array.Empty<string>(),sourceZones=new[]{"hand"},inputCount=1,outputKind="slash",forPlay=true,forResponse=false},
                    new{id="two-card-dodge",inputKinds=Array.Empty<string>(),inputSuits=Array.Empty<string>(),sourceZones=new[]{"hand"},inputCount=2,extendedUse=true,outputKind="dodge",forPlay=false,forResponse=true}
                }}
            };
            var labels=new Dictionary<string,object>{[Quiet]=new{name="安静实际同行",description="原生跳过出牌并保留实际供杀范围"},[Driver]=new{name="真实用牌夹具",description="原生来杀与合法手牌转换"}};
            var cata=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in cata.Programs)b.AddSkill(new(id,cata.Presentations[id].Name,cata.Presentations[id].Description){Program=p,ProgramPresentation=cata.Presentations[id]});
            foreach(var id in new[]{"fixture:mougsz-ally","fixture:mougsz-enemy-a","fixture:mougsz-enemy-b"})b.AddGeneral(new(id,"实际同行","","standard:none","qun",12,[Quiet]));
            b.AddDeck(new("fixture:mougsz-deck","固定真实扈候选牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("fixture:mougsz-"+(i<4?CardKind.Dodge:CardKind.Slash),Suit.Spade,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"移动谋公孙瓒真实主流程",4,4,new Dictionary<string,int>(),"fixture:mougsz-deck",GeneralPoolIds:[Hero,"fixture:mougsz-ally","fixture:mougsz-enemy-a","fixture:mougsz-enemy-b"],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
