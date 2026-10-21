using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenZhangFeiChecks
{
    private const string General="anniversary:shen-zhang-fei", Driver="fixture:shen-zf-driver", Quiet="fixture:shen-zf-quiet", Pause="fixture:shen-zf-pause", Claim="fixture:shen-zf-final-claim", Armor="fixture:shen-zf-black-armor", Shadow="fixture:shen-zf-black-unarmored", ReplacementDeath="fixture:shen-zf-replacement-death", Mode="identity:classic-shen-zf-fixture";
    public static void NativeJudgmentOverwriteAndPersistentPunishments()
    {
        var (g,r)=Start(); Fuel(g); Raise(g);
        Top(g,CardKind.Duel); Sentence(g,1); Reach(g,p=>IsPause(p,"judge-gained"));
        var first=E<ShenZhangFeiJudgmentGainEvent>(g).Single();
        Need(V(g,1).Skills!.Any(s=>s.ContentId==Claim)&&g.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single(w=>w.ParentFrameId==first.JudgmentId).Candidates.Any(c=>c.OwnerSeat==1&&c.SkillId==Claim),"The judged role really owns the ordinary final-judgment claim skill and its eligible candidate remains behind Shencai's innate gain.");
        Need(g.CardMovements.Any(m=>m.CardId==first.CardId&&m.From==CardLocation.Judgment(1)&&m.To==CardLocation.Hand(0)),"Shencai's real final judgment moves directly to its owner before the judged role's native claim opportunity.");
        g=Cold(g,r); Continue(g); Play(g);
        Need(Mark(g,1,PlayerMarkerKind.ShencaiTu)==1&&!E<ProgramJudgmentCardClaimedEvent>(g).Any(e=>e.JudgmentFrameId==first.JudgmentId),"A genuine Duel judgment gives Tu and the already obtained result cannot be claimed a second time.");
        Top(g,CardKind.ZhangbaSerpentSpear); Sentence(g,1); Play(g);
        Need(Mark(g,1,PlayerMarkerKind.ShencaiTu)==1&&Mark(g,1,PlayerMarkerKind.ShencaiZhang)==1,"One printed Zhangba result simultaneously assigns Weapon and Play punishments, replacing the previous four-punishment set.");
        var hand=V(g,1).HandCount; Use(g,CardKind.Dismantlement,[1]); Play(g);
        Need(V(g,1).HandCount==hand-2&&E<ShenZhangFeiPunishmentPaidEvent>(g).Count(e=>e.Binding=="tu-hand-loss")==1,"An actual external hand loss causes one real random hand discard, whose own movement cannot recursively trigger Tu. "+RuleCounts(g,new{Before=hand,After=V(g,1).HandCount}));
        Top(g,CardKind.Peach); Sentence(g,1); Play(g);
        Need(Mark(g,1,PlayerMarkerKind.ShencaiChi)==1&&Mark(g,1,PlayerMarkerKind.ShencaiTu)==0&&Mark(g,1,PlayerMarkerKind.ShencaiZhang)==0,"A later Hp result removes both old overlapping punishments and installs only Chi.");
        var hp=V(g,1).Hp; Skill(g,Driver,"hit",[1]); Play(g);
        Need(V(g,1).Hp==hp-4&&E<ShenZhangFeiPunishmentPaidEvent>(g).Single(e=>e.Binding=="chi-damage").Amount==2,"One real two-point damage event causes a separate exact two-point native HP loss.");
        Top(g,CardKind.Slash); Sentence(g,1); Reach(g,p=>p.SkillPrompt?.SkillId=="anniversary:shencai"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shen-zf-take"));
        Need(Mark(g,1,PlayerMarkerKind.ShencaiSi)==1&&Mark(g,1,PlayerMarkerKind.ShencaiChi)==1,"A no-keyword result adds Si while preserving the existing four-punishment set.");
        g=Cold(g,r); Answer(g,_=>true); Play(g);
        Need(Limit(g,1)==Math.Max(0,V(g,1).Hp-Mark(g,1,PlayerMarkerKind.ShencaiSi)),"While the source lives, Si reduces the actual native hand-limit query by its current count exactly once.");
        Top(g,CardKind.Snatch); Sentence(g,1); Play(g);
        Need(Mark(g,1,PlayerMarkerKind.ShencaiLiu)==1&&Mark(g,1,PlayerMarkerKind.ShencaiChi)==0&&Mark(g,1,PlayerMarkerKind.ShencaiSi)==1,"A Distance result replaces Chi with Liu without clearing persistent Si.");
        Skill(g,Driver,"die",[]); Reach(g,_=>false,()=>!V(g,0).IsAlive&&V(g,1).IsFaceDown);
        Need(Limit(g,1)==Math.Max(0,V(g,1).Hp-Mark(g,1,PlayerMarkerKind.ShencaiSi)),"After the source dies, Si still reduces the actual native hand-limit query by its current count exactly once.");
        Need(Mark(g,1,PlayerMarkerKind.ShencaiLiu)==1&&Mark(g,1,PlayerMarkerKind.ShencaiSi)==1&&E<ShenZhangFeiPunishmentPaidEvent>(g).Any(e=>e.Binding=="liu-turn-ending"&&e.TargetSeat==1),"After the original source really dies, its existing issued Liu still turns the marked role over at that role's ending phase.");
        _=Cold(g,r);
        var (unclaimed,_)=Start(unclaimed:true);
        Sentence(unclaimed,1); Reach(unclaimed,p=>p is {PlayerSeat:0,Kind:DecisionKind.ProgramJudgmentReplacement});
        var judgment=unclaimed.ResolutionStack.OfType<JudgmentFrame>().Single().Id;
        var replacement=P(unclaimed)!.Choices.First(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-replace"&&c.Cards.Count==1).Cards.Single();
        Answer(unclaimed,c=>c.Cards.SequenceEqual([replacement]));
        Reach(unclaimed,_=>false,()=>!V(unclaimed,0).IsAlive&&E<ShenZhangFeiJudgmentReturnedEvent>(unclaimed).Any(e=>e.JudgmentId==judgment)&&unclaimed.ResolutionStack.Count==0);
        var facts=unclaimed.Events.Select(e=>e.Payload).ToArray();
        var died=Array.FindIndex(facts,e=>e is PlayerDiedEvent {VictimSeat:0});
        var finalized=Array.FindIndex(facts,e=>e is JudgmentResolvedEvent j&&j.ResolutionId==judgment);
        var handoff=unclaimed.CardMovements.Single(m=>m.CardId==replacement&&m.From==CardLocation.Judgment(1)&&m.To==CardLocation.Processing&&m.Reason.Value=="skill-program.judgment.result");
        Need(died>=0&&finalized>died&&
            E<ProgramJudgmentReplacementResolvedEvent>(unclaimed).Single(e=>e.JudgmentFrameId==judgment).Activated&&!E<ShenZhangFeiJudgmentGainEvent>(unclaimed).Any()&&
            unclaimed.CardMovements.Any(m=>m.Sequence>handoff.Sequence&&m.CardId==replacement&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.anniversary:shencai.judgment-unclaimed")&&
            unclaimed.CreateCardZoneDiagnostics().Single(c=>c.CardId==replacement).Location==CardLocation.DiscardPile&&!unclaimed.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.Processing),
            "A real replacement-payment hand-loss child kills the source before final judgment; its unclaimed final entity follows the exact native Judgment-to-Processing return and once-paid discard, leaving no processing residue.");
    }
    public static void NativeColorlessTargetsQuotaAndDirectDeath()
    {
        var (g,r)=Start(); Fuel(g);
        var initial=g.GetHumanLegalActions().Where(a=>a.SkillId=="anniversary:shencai").ToArray(); Need(initial.Length>0,"The initial sentence allowance is available.");
        var id=V(g).Hand.First(c=>c.Kind==CardKind.IronChain).Id;
        Need(V(g).Hand.Single(c=>c.Id==id) is { Suit:Suit.None,IdentityKind:CardKind.Slash },"The mandatory hand identity exposes a genuinely colorless Slash while retaining its actual printed entity.");
        Need(!g.GetHumanLegalActions().Any(a=>a.CardId==id&&a.Kind==LegalActionKind.Recast),"A forced Slash identity cannot retain IronChain's recast command.");
        var armor=g.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:shen-zf-peer-1").Seat;
        var shadow=g.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:shen-zf-peer-2").Seat;
        Need(armor!=shadow&&V(g,armor).Skills!.Any(s=>s.ContentId==Armor)&&V(g,shadow).Skills!.Any(s=>s.ContentId==Shadow),"The two actual targets separately possess native Renwang and Yizhong policies despite random general allocation.");
        var hp1=V(g,armor).Hp; var hp2=V(g,shadow).Hp;
        Use(g,CardKind.IronChain,new[]{armor,shadow}.Order().ToArray()); Play(g);
        var issued=E<ShenZhangFeiColorlessIssuedEvent>(g).Single();
        var accepted=E<CardActionAcceptedEvent>(g).Select(e=>e.Action).First(a=>a.ActionId==issued.Receipt.ActionId);
        Need(accepted is { EffectiveKind:CardKind.Slash,EffectiveSuit:Suit.None,EffectiveIsRed:null }&&accepted.PhysicalCards.Single().CardId==id&&V(g,armor).Hp==hp1-1&&V(g,shadow).Hp==hp2-1,"One physically paid colorless Slash follows both real native target attacks, bypasses Renwang and Yizhong's black-only immunity, and keeps the printed black IronChain material identity.");
        Raise(g,3); Need(E<ShenZhangFeiXunshiEvent>(g).Length==4&&E<ShenZhangFeiXunshiEvent>(g).Last().Limit==5,"Four separately issued colorless uses permanently raise the per-Play allowance to five.");
        for(var n=0;n<5;n++) { Top(g,CardKind.Slash); Sentence(g,2); Play(g); }
        Need(Mark(g,2,PlayerMarkerKind.ShencaiSi)==5&&!g.GetHumanLegalActions().Any(a=>a.SkillId=="anniversary:shencai"),"Five actual independent no-keyword judgments consume the current five-use phase budget and stack five Si marks.");
        End(g); Reach(g,_=>false,()=>!V(g,2).IsAlive);
        Need(E<ShenZhangFeiPunishmentPaidEvent>(g).Single(e=>e.Binding=="si-turn-ended") is {TargetSeat:2}&&E<ProgramSkillCauseDeathDeclaredEvent>(g).Any(e=>e.TargetSeat==2),"Si directly kills its role only at the actual turn-ended boundary when five marks exceed the current four living roles.");
        _=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players.Single(p=>p.Seat==s);
    private static int Limit(GameEngine g,int seat)
    {
        var players=(System.Collections.IList)typeof(GameEngine).GetField("_players",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(g)!;
        return(int)typeof(GameEngine).GetMethod("GetHandLimit",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[players[seat]])!;
    }
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static int Mark(GameEngine g,int s,PlayerMarkerKind m)=>V(g,s).Markers?.Where(x=>x.Kind==m).Sum(x=>x.Count)??0;
    private static bool IsPause(PendingDecision p,string id)=>p.SkillPrompt?.SkillId==Pause&&p.Choices.Any(c=>c.Id.Value.Contains(id,StringComparison.Ordinal));
    private static void Fuel(GameEngine g){Skill(g,Driver,"draw",[]);Play(g);Skill(g,Driver,"draw",[]);Play(g);}
    private static void Raise(GameEngine g,int times=4){for(var n=0;n<times;n++){Use(g,CardKind.IronChain,[3]);Play(g);}}
    private static void Top(GameEngine g,CardKind kind){var card=V(g).Hand.First(c=>c.Kind==kind);Skill(g,Driver,"top",[],[card.Id]);Play(g);}
    private static void Sentence(GameEngine g,int seat)=>Skill(g,"anniversary:shencai","sentence",[seat]);
    private static void Skill(GameEngine g,string skill,string activation,int[] targets,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,skill,activation,cards??[],targets,g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,CardKind kind,int[] targets)
    {
        var ids=V(g).Hand.Where(c=>c.Kind==kind).Select(c=>c.Id).ToHashSet();
        var a=g.GetHumanLegalActions().First(a=>a.CardId is {} id&&ids.Contains(id)&&a.TargetSeats.SequenceEqual(targets));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Play(GameEngine g)=>Reach(g,p=>p is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<PendingDecision,bool> ready,Func<bool>? state=null)
    {
        for(var n=0;n<700;n++)
        {
            if(state?.Invoke()==true)return;
            if(P(g) is {} p)
            {
                if(ready(p))return;
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.SkillPrompt?.SkillId==Pause){Continue(g);continue;}
                if(p.SkillPrompt?.SkillId=="anniversary:shencai"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shen-zf-take")){Answer(g,_=>true);continue;}
                if(p.Kind==DecisionKind.SelectTargetCard){Answer(g,_=>true);continue;}
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification){Answer(g,c=>c.Cards.Count==0);continue;}
            }
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Zhang Fei missed native boundary: "+RuleCounts(g,new{Prompt=P(g)?.Kind,Seat=P(g)?.PlayerSeat,Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name})}));
    }
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Shen Zhang Fei command.");}
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static string RuleCounts(GameEngine g,object detail)=>JsonSerializer.Serialize(new{Detail=detail,TuLoss=E<ShenZhangFeiTuLossEvent>(g),Paid=E<ShenZhangFeiPunishmentPaidEvent>(g),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId=="anniversary:shencai").Select(e=>new{e.OwnerSeat,e.BindingId,e.Activated,e.Completed}).TakeLast(8),TurnEnds=E<TurnEndedEvent>(g).TakeLast(4),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).IsAlive,V(g,s).Hp,Si=Mark(g,s,PlayerMarkerKind.ShencaiSi)})});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Private views, physical movements and exact native parents must cold-restore identically.");return restored;}
    private static (GameEngine,ContentRegistry) Start(bool unclaimed=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(unclaimed));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Loyalist,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,General,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(bool unclaimed):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-shen-zf",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b,unclaimed),General);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"top","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]},
                {"id":"hit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Claim}}","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","optional":false,"suits":["spade","heart","club","diamond"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"effects":[{"op":"claimJudgmentCard","target":"owner"}]}]},
              {"id":"{{Armor}}","revision":1,"cardPolicies":[{"id":"renwang","kind":"virtualEquipment","cardKinds":["renwangShield"]}]},
              {"id":"{{Shadow}}","revision":1,"cardPolicies":[{"id":"yizhong","kind":"nullifyBlackSlashWithoutArmor","cardKinds":["slash","fireSlash","thunderSlash"]}]},
              {"id":"{{ReplacementDeath}}","revision":1,"triggers":[
                {"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],"optional":true,"effects":[{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","club","heart","diamond","none"],"oldCardDestination":"discardPile"}]},
                {"id":"replacement-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{CardMoveReasons.ProgramJudgmentReplace.Value}}"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
              {"id":"{{Pause}}","revision":1,"triggers":[{"id":"judge-gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:shencai.judgment-gain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"judge-gained","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Claim,Pause,Armor,Shadow,ReplacementDeath};var cat=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="Native Shen Zhang Fei fixture.",optionLabels=id==Pause?new Dictionary<string,string>{{"continue","继续"}}:[]})}));
            foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,id,"Native fixture"){Program=p,ProgramPresentation=cat.Presentations[id]});
            for(var seat=1;seat<4;seat++)b.AddGeneral(new($"fixture:shen-zf-peer-{seat}","Quiet participant","supporter",Quiet,"wei",20){AdditionalSkillIds=unclaimed?(seat==1?[Armor]:seat==2?[Shadow]:[]):seat==1?[Claim,Armor]:seat==2?[Claim,Shadow]:[Claim]});
            b.AddCard(new("fixture:shen-zf-spear","丈八蛇矛","装备牌","真实原生武器实体",CardKind.ZhangbaSerpentSpear));
            var kinds=new[]{"standard:iron_chain","standard:slash","standard:duel","fixture:shen-zf-spear","standard:peach","standard:snatch","standard:dismantlement","standard:draw_two","standard:iron_chain","standard:crossbow"};
            b.AddDeck(new("fixture:shen-zf-deck","Real mixed printed deck",20,2,[]){PhysicalCards=Enumerable.Range(0,360).Select(i=>new ContentDeckPhysicalCard(kinds[i%kinds.Length],kinds[i%kinds.Length]=="standard:iron_chain"?Suit.Spade:i%2==0?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"Native Shen Zhang Fei",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:shen-zf-deck",GeneralCandidateCount:4,GeneralPoolIds:[General,"fixture:shen-zf-peer-1","fixture:shen-zf-peer-2","fixture:shen-zf-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b,bool unclaimed):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==General?d with{AdditionalSkillIds=unclaimed?[..d.AdditionalSkillIds!,Driver,Pause,ReplacementDeath]:[..d.AdditionalSkillIds!,Driver,Pause]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
