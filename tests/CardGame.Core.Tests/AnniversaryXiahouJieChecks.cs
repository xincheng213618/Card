using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryXiahouJieChecks
{
    private const string Hero="anniversary:xiahou-jie", Driver="fixture:ann-xhj-driver", Watch="fixture:ann-xhj-watch", Quiet="fixture:ann-xhj-quiet", Deck="fixture:ann-xhj-deck";
    public static void SequentialComparisonsOwnEndSuppressionAndFiveMarksCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var options=Options(false); var g=GameEngine.CreateStandard(options,registry); Accept(g,new StartGameCommand()); Play(g);
        Need(V(g).Hp==5 && V(g).MaxHp==5 && V(g).HandCount==1 && V(g).Equipment.Count==1,"The formal Wei general begins at genuine five HP, with a real hand and equipped armor.");
        End(g); Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.AnniversaryXiahouJie is not null); var id=root.Id;
        Need(root.AnniversaryXiahouJie is {Stage:AnniversaryXiahouJieStage.DrawChildren,Cursor:0,Comparisons.Count:1,Draws.Count:1} &&
            E<AnniversaryXiahouJieComparedEvent>(g).Where(e=>e.FrameId==id).ToArray() is [{Index:0,OwnerValue:1,OtherValue:0}] && V(g).Hp==5 && V(g).HandCount==2,
            "Only the first comparison is frozen when its real one-card native draw has paid and its gain child pauses."+Detail(g));
        g=Cold(g,registry); Continue(g); Play(g);
        Need(E<AnniversaryXiahouJieComparedEvent>(g).Where(e=>e.FrameId==id).OrderBy(e=>e.Index).Select(e=>(e.Index,e.OwnerValue,e.OtherValue)).SequenceEqual(new (int,int?,int?)[]{(0,1,0),(1,3,4),(2,1,0)}) &&
            E<AnniversaryXiahouJieDrawReturnedEvent>(g).Count(e=>e.FrameId==id && e.ActualCount==1)==2 &&
            E<AnniversaryXiahouJieMaximumEvent>(g).All(e=>e.FrameId!=id) && V(g).Hp==3 && V(g).MaxHp==5 && V(g).HandCount==3,
            "The paid first draw resumes once; its real child HP loss is seen by the later HP comparison, and only the hand/equipment comparisons draw."+Detail(g));
        var grants=E<AnniversaryXiahouJieSuppressedEvent>(g);
        Need(grants.Length>0 && grants.All(e=>e.Source.OwnerSeat==0 && e.TurnOwner!=0) && E<AnniversaryXiahouJieSuppressionExpiredEvent>(g).Length==0 &&
            E<AnniversaryXiahouJieStartedEvent>(g).Length==1 && E<TurnStartedEvent>(g).Last().ActorSeat==0,
            "Unique-most hand at each real foreign turn end suppresses only Liedan through the following foreign preparations and the next own preparation; it has not expired at own turn start."+Detail(g));
        Accept(g,new UseProgramSkillCommand(0,Driver,"heal",[],[],g.Revision,P(g)!.PromptId)); Play(g); Need(V(g).Hp==5,"The fixed real healing activation restores two HP before the next preparation.");
        End(g); Play(g);
        var next=E<AnniversaryXiahouJieStartedEvent>(g).Single(e=>e.FrameId!=id);
        Need(E<AnniversaryXiahouJieSuppressionExpiredEvent>(g).Count(e=>grants.Any(x=>x.FrameId==e.FrameId))==grants.Length &&
            E<AnniversaryXiahouJieComparedEvent>(g).Where(e=>e.FrameId==next.FrameId).OrderBy(e=>e.Index).Select(e=>e.OwnerValue>e.OtherValue).All(x=>x) &&
            E<AnniversaryXiahouJieDrawReturnedEvent>(g).Count(e=>e.FrameId==next.FrameId && e.ActualCount==1)==3 &&
            E<AnniversaryXiahouJieMaximumEvent>(g).Where(e=>e.FrameId==next.FrameId).ToArray() is [{Before:5,After:6}] && V(g).MaxHp==6 && V(g).Hp==5,
            "Every issued suppression expires only at the owner's actual turn end; the next actual comparison is enabled and all three genuine successes pay three draws then one maximum HP."+Detail(g));
        Need(State(g)==State(GameReplay.Replay(options,g.AcceptedCommands,registry)),"Accepted commands replay the sequential native draw, changed later comparison and actual-own-end qualification restoration exactly.");

        // The same ordinary general accumulates marks through genuine all-non-greater comparisons.
        // No marker, HP, pending frame or death event is injected into engine state.
        var deathOptions=Options(true); var d=GameEngine.CreateStandard(deathOptions,registry); Accept(d,new StartGameCommand()); Play(d); End(d); Play(d);
        Need(V(d).Hp==4 && E<AnniversaryXiahouJieHpPaidEvent>(d).Length==3 && E<PlayerMarkerChangedEvent>(d).Count(e=>e.PlayerSeat==0 && e.Marker==PlayerMarkerKind.XiahouJieLie && e.Delta==1)==3,
            "Three genuine foreign preparations each lose one real HP and pay exactly one crack mark."+Detail(d));
        End(d); Reach(d,_=>!V(d).IsAlive,allowDead:true);
        var direct=E<AnniversaryXiahouJieStartedEvent>(d).Single(e=>e.OwnPreparation);
        Need(!V(d).IsAlive && direct.Marks==6 && E<AnniversaryXiahouJieHpPaidEvent>(d).Length==6 && E<AnniversaryXiahouJieHpPaidEvent>(d).Last().After==1 &&
            E<ProgramSkillCauseDeathDeclaredEvent>(d).Count(e=>e.FrameId==direct.FrameId && e.SourceSeat==0 && e.TargetSeat==0)==1 &&
            E<PlayerDiedEvent>(d).Count(e=>e.VictimSeat==0 && e.KillerSeat is null)==1 && E<PlayerDyingEvent>(d).All(e=>e.VictimSeat!=0) && E<DamageAppliedEvent>(d).Length==0,
            "At the real own preparation, six paid marks cause native direct death while the last paid HP was still positive; this is neither damage nor a dying rescue shortcut."+Detail(d));
        Need(State(d)==State(GameReplay.Replay(deathOptions,d.AcceptedCommands,registry)),"The short all-non-greater scene replays genuine HP payments, mark accumulation and direct-death parent return.");
    }
    private static GameOptions Options(bool death)
    {
        ShanheCombatantDefinition PeerSetup(int s)=>new(Peer(s),Hp:death?8:4){StartingHand=death?[new(CardKind.Dodge,Suit.Heart,2)]:[]};
        var setup=new ShanheBattleSetup(7,1,death?"xhj-marks":"xhj-sequential",Hero,death?7:5,death?2:0,0,0,0,
            death?[]:[Driver,Watch],death?[]:[new(CardKind.Dodge,Suit.Heart,2)],death?[]:[new(CardKind.BaguaFormation,Suit.Spade,2)],
            [PeerSetup(2),PeerSetup(3)],[PeerSetup(1)],new string('a',64));
        return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,
            UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12};
    }
    private static string Peer(int s)=>"fixture:ann-xhj-peer-"+s;
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Need(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Xiahou Jie command.");}
    private static void Continue(GameEngine g){var p=P(g)!;var c=p.Choices.Single(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,p=>p is {Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Reach(GameEngine g,Func<PendingDecision,bool> ready,bool allowDead=false)
    {
        for(var i=0;i<180;i++)
        {
            var p=P(g);
            if(allowDead && !V(g).IsAlive)return;
            if(p is not null)
            {
                if(ready(p))return;
                if(p.SkillPrompt?.SkillId==Watch){Continue(g);continue;}
                if(p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Xiahou Jie native boundary: "+JsonSerializer.Serialize(p)+Detail(g));
            }
            Need(g.State.Winner==Winner.None,"The short Xiahou Jie scene retains a genuine native boundary."+Detail(g)); Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Xiahou Jie missed the bounded native boundary."+Detail(g));
    }
    private static string Detail(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).MaxHp,V(g,s).HandCount,V(g,s).IsAlive}),
        Compared=E<AnniversaryXiahouJieComparedEvent>(g),Draws=E<AnniversaryXiahouJieDrawReturnedEvent>(g),Maximum=E<AnniversaryXiahouJieMaximumEvent>(g),HpPaid=E<AnniversaryXiahouJieHpPaidEvent>(g),Suppressed=E<AnniversaryXiahouJieSuppressedEvent>(g),Expired=E<AnniversaryXiahouJieSuppressionExpiredEvent>(g)});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry registry){var r=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(r),"The first paid comparison draw and its native gain child cold-restore identically.");return r;}
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("fixture-anniversary-xiahou-jie",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-first-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:liedan.draw"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{
                [Driver]=new{name="真实回复",description="以原生回复恢复两点体力。"},[Watch]=new{name="已支付比较摸牌",description="首次摸牌子窗真实失去体力，后项需读取新值。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Quiet]=new{name="安静同桌",description="跳过普通出牌。"}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            for(var s=1;s<4;s++)b.AddGeneral(new(Peer(s),"固定同桌", "",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定小闪牌堆",0,0,[]){PhysicalCards=Enumerable.Range(0,24).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"裂胆依次比较与壮胆期限",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
