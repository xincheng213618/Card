using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryLeJiuChecks
{
    private const string Hero="anniversary:le-jiu", Skill="anniversary:cuijin", Driver="fixture:ann-le-jiu-driver", Watch="fixture:ann-le-jiu-watch", Deck="fixture:ann-le-jiu-deck";
    private const string CostReason="skill-program.anniversary:cuijin.SelectAndMoveOwnedCard";
    public static void PaidWholeUseBonusDuelReverseAndUndamagedNativeReturnCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var g=GameEngine.CreateStandard(Options(),registry); Accept(g,new StartGameCommand()); Play(g);
        var slash=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1,3]));
        Native(g,slash); Pay(g); Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        var root=Root(g)!; var stamp=root.LeJiuCuijin!; var paid=E<NativeLogicalDiscardInvocationRecordedEvent>(g).Single(e=>e.ParentFrameId==root.Id);
        Need(stamp.Grant is null&&root.InstructionIndex==2&&root.PendingMovementContinuation is not null&&paid.ActualCount==1&&
            E<LeJiuCuijinArmedEvent>(g).Length==0&&V(g,1).Hp==8&&V(g,3).Hp==8,
            "Cuijin genuinely discards before arming or native damage and keeps its original paid movement child.");
        g=Cold(g,registry); Continue(g); Play(g);
        var first=E<LeJiuCuijinArmedEvent>(g).Single().Grant;
        Need(first.UseId==stamp.UseId&&first.InvocationId==paid.InvocationId&&V(g,1).Hp==6&&V(g,3).Hp==6&&
            E<LeJiuCuijinRewardBoundEvent>(g).Length==0&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==first.UseId)==1&&
            g.CardMovements.Count(m=>m.Reason.Value==CostReason)==1,
            "One real cost adds one damage to both targets of the same physical Slash; any actual whole-use damage excludes the no-damage reward.");
        var hp=V(g).Hp; var targetHp=V(g,2).Hp;
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([2])));
        Pay(g); Play(g);
        Need(V(g).Hp==hp-2&&V(g,2).Hp==targetHp&&E<LeJiuCuijinArmedEvent>(g).Length==2&&E<LeJiuCuijinRewardBoundEvent>(g).Length==0&&
            E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==2&&e.TargetSeat==0&&e.Amount==2)&&
            E<CardMovedEvent>(g).Any(e=>e.From==CardLocation.Hand(2)&&e.CardKind==CardKind.Slash),
            "A real Duel counter-Slash reverses the damage source; the paid original-use bonus still makes its user take two damage and does not falsely reward a damaging Duel.");
        var before=V(g).HandCount; var hpBefore=V(g).Hp;
        Accept(g,new UseProgramSkillCommand(0,Driver,"legacy",[],[2],g.Revision,P(g)!.PromptId)); Pay(g);
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch&&Root(g)?.LeJiuCuijin?.Reward==true);
        var reward=Root(g)!; var grant=reward.LeJiuCuijin!.Grant!; var producer=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Driver).Id;
        Need(reward.InstructionIndex==2&&V(g).HandCount==before+1&&V(g).Hp==hpBefore&&V(g,2).Hp==targetHp&&
            E<LeJiuCuijinRewardBoundEvent>(g).Count(e=>e.GrantFrameId==grant.FrameId)==1&&
            g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==grant.UseId) is{CardId:0,PhysicalCardIds.Count:0,Step:ResolutionFrameStep.Completed,CausedDamage:false},
            "The legacy false-window producer reaches the actual native Use, is genuinely Dodged, then draws two after its paid one-card cost before the one-point consequence.");
        g=Cold(g,registry); Continue(g); Play(g);
        Need(V(g).Hp==hpBefore-1&&V(g,2).Hp==targetHp&&E<LeJiuCuijinArmedEvent>(g).Length==3&&E<LeJiuCuijinRewardBoundEvent>(g).Length==1&&
            E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==grant.UseId)==1&&
            E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==producer&&e.SkillId==Driver&&e.ActivationId=="legacy"&&e.Completed)==1&&
            E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==0&&e.Amount==1)==1&&
            g.CardMovements.Count(m=>m.Reason.Value==CostReason)==3,
            "The paid reward cold return deals only one new native skill damage, never inherits the card's bonus, and resumes the original producer once. "+JsonSerializer.Serialize(new{
                HpBefore=hpBefore,HpNow=V(g).Hp,TargetHpBefore=targetHp,TargetHpNow=V(g,2).Hp,
                Armed=E<LeJiuCuijinArmedEvent>(g).Length,Rewards=E<LeJiuCuijinRewardBoundEvent>(g).Length,
                UseId=grant.UseId,Finished=E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==grant.UseId),ProducerId=producer,
                ProducerReturns=E<ProgramSkillResolvedEvent>(g).Where(e=>e.FrameId==producer&&e.ActivationId=="legacy").ToArray(),
                SelfDamage=E<DamageAppliedEvent>(g).Where(e=>e.SourceSeat==0&&e.TargetSeat==0).ToArray(),
                CostMoves=g.CardMovements.Count(m=>m.Reason.Value==CostReason),Frames=g.ResolutionStack.Select(f=>new{f.Id,Kind=f.GetType().Name}).ToArray()}));
        Need(State(g)==State(GameReplay.Replay(Options(),g.AcceptedCommands,registry)),"Commands replay exact real costs, all-target native damage, reverse Duel damage, no-damage draw child and original native producer return.");
    }
    private static GameOptions Options()
    {
        var setup=new ShanheBattleSetup(7,1,"anniversary-le-jiu",Hero,4,0,0,0,0,[Driver,Watch],
            [new(CardKind.Slash,Suit.Heart,7),new(CardKind.Duel,Suit.Spade,1),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Club,3),new(CardKind.Dodge,Suit.Diamond,4)],[],
            [new(Peer(2),Hp:4){StartingHand=[new(CardKind.Slash,Suit.Club,7),new(CardKind.Dodge,Suit.Heart,9)]},new(Peer(3)){StartingHand=[]}],
            [new(Peer(1)){StartingHand=[]}],new string('d',64));
        return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=4};
    }
    private static string Peer(int s)=>"fixture:ann-le-jiu-peer-"+s;
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players.Single(p=>p.Seat==s);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.LeJiuCuijin is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool b,string why){if(!b)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected native Le Jiu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool>pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing Le Jiu choice: "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    private static void Pay(GameEngine g)
    {
        Reach(g,p=>p.SkillPrompt?.SkillId==Skill&&p.Choices.Any(c=>Key(c)=="activate")); Answer(g,c=>Key(c)=="activate");
        Reach(g,p=>p.SkillPrompt?.SkillId==Skill&&p.Choices.Any(c=>Key(c)=="select-and-move-owned-card"));
        var id=V(g).Hand.First(c=>c.Kind==CardKind.Dodge).Id; Answer(g,c=>c.Cards.SequenceEqual([id]));
    }
    private static void Play(GameEngine g)=>Reach(g,p=>p is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Reach(GameEngine g,Func<PendingDecision,bool>ready)
    {
        for(var i=0;i<100;i++)
        {
            if(P(g)is{}p)
            {
                if(ready(p))return;
                if(p.SkillPrompt?.SkillId==Watch){Continue(g);continue;}
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Le Jiu boundary: "+JsonSerializer.Serialize(new{P=p,Root=Root(g)?.LeJiuCuijin,Hp=Enumerable.Range(0,4).Select(s=>V(g,s).Hp),Armed=E<LeJiuCuijinArmedEvent>(g),Reward=E<LeJiuCuijinRewardBoundEvent>(g)}));
            }
            Need(g.State.Winner==Winner.None,"The short Le Jiu scene must retain its real native boundary.");Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Le Jiu missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold restore retains exact original Use, paid invoice, whole-use grant, pending native child and every viewer.");return restored;}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Zones=g.CreateCardZoneDiagnostics(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-anniversary-le-jiu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"real-two-targets","query":"cardTargetCount","operation":"add","value":1,"priority":0,"cardKinds":["slash"]},{"id":"real-range","query":"attackRange","operation":"set","value":4,"priority":0},{"id":"real-quota","query":"slashLimit","operation":"add","value":20,"priority":0}],"activations":[{"id":"legacy","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"cost-paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{CostReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},{"id":"draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:cuijin.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-paid","options":[{"id":"continue"}]}]}]}]}
            """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实多目标及旧生产者",description="已有范围、目标数与原生虚拟杀能力。"},[Watch]=new{name="原生付款子窗",description="实际付款或摸牌后暂停。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            for(var s=1;s<4;s++)b.AddGeneral(new(Peer(s),"真实固定角色","supporter",Watch,"wei",8));
            b.AddDeck(new(Deck,"固定无额外杀牌堆",0,0,[]){PhysicalCards=Enumerable.Range(0,50).Select(i=>new ContentDeckPhysicalCard("standard:dodge",Suit.Diamond,2+i%5)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"周年乐就真实原牌结算",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
