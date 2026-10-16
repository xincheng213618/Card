using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YaDanChecks
{
    private const string Skill="ol:qingya",Distance="ol:tielun",Driver="fixture:yd-driver",Quiet="fixture:yd-quiet",Watch="fixture:yd-health",Peer="fixture:yd-peer-watch",Owner="fixture:yd-owner",Mode="identity:classic-ya-dan-fixture";
    private const string DiscardReason="skill-program.ya-dan.qingya.discard";
    public static void QingyaShortestLivingArcTieOpaqueAndNativeDiscardsCold()
    {
        foreach(var scene in new[]{"tie","zero","dead-gap"})
        {
            var(g,r)=Start(scene);Equip(g);
            if(scene=="dead-gap"){Use(g,"kill",[1]);Play(g);Need(!V(g,1).IsAlive&&g.State.Winner==Winner.None,"A real nonterminal death removes a seat from the live shortest arc.");}
            var target=scene=="zero"?1:2;var one=V(g,1).HandCount;var three=V(g,3).HandCount;
            StartSweep(g,target);
            if(scene=="tie")
            {
                Need(Branch(g,"side")&&P(g)!.Choices.Count==2&&P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([1]))&&P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([3])),
                    "Equal living-seat arcs publish two separate real sides, never their union.");
                g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Branch(g,"hand"));
                var p=P(g)!;var before=State(g);
                Need(p.IsPrivate&&p.ValidCardIds.Count==0&&p.Choices.All(c=>c.Cards.Count==0)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&
                    Enumerable.Range(0,4).All(s=>g.CreateSnapshot(s).Players.Where(v=>v.Seat!=s).All(v=>v.Hand.Count==0)),
                    "The actual discard chooser receives only original opaque Hand slots; other viewers receive no foreign private entities or prompt.");
                Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before&&
                    !g.Submit(new AnswerPromptCommand(0,p.PromptId,new("invented-original-slot"),g.Revision)).Accepted&&State(g)==before,
                    "Wrong actors and invented private slots leave every view, RNG and issuing cursor unchanged.");
                var receipt=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YaDan is not null).YaDan!;
                Need(receipt.Arcs is IList<QingyaArc>{IsReadOnly:true}&&receipt.Arcs.All(a=>a.Seats is IList<int>{IsReadOnly:true})&&receipt.Materials is IList<QingyaHandMaterial>{IsReadOnly:true},
                    "Both outer and nested original geometry and private material collections are frozen.");
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);
                var paid=E<QingyaHandDiscardedEvent>(g).Single();var grant=E<QingyaGrantIssuedEvent>(g).Single();
                Need(paid.Material.HolderSeat==1&&V(g,1).HandCount==one-1&&V(g,3).HandCount==three&&grant.EntityCount==1&&
                    E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.ParentFrameId==paid.FrameId&&e.InitiatorSeat==0&&e.ExecutorSeat==0&&e.SubjectSeat==1&&e.ActualCount==1&&e.HandCount==1),
                    "Only the chosen side loses one original Hand entity, with the real skill owner as both discard initiator and executor: "+JsonSerializer.Serialize(new{paid.Material.HolderSeat,CurrentOne=V(g,1).HandCount,one,CurrentThree=V(g,3).HandCount,three,grant.EntityCount,Logical=E<NativeLogicalDiscardInvocationRecordedEvent>(g)}));
            }
            else
            {
                Play(g);
                Need(!E<QingyaHandDiscardedEvent>(g).Any()&&!E<QingyaGrantIssuedEvent>(g).Any()&&V(g,3).HandCount==three,
                    "An adjacent target or an actually dead intervening seat gives an empty shortest side without fake movement or a future material.");
            }
            _=Cold(g,r);
        }
    }
    public static void QingyaNextActualPhaseOriginalCardNativeUsesAndExpiryCold()
    {
        foreach(var scene in new[]{"equipment","iron-chain","skip-discard","inserted","slash","alcohol","offturn-alcohol","residency"})
        {
            var(g,r)=Start(scene);
            if(scene=="offturn-alcohol")
            {
                Native(g,a=>a.Kind==LegalActionKind.Alcohol);Play(g);End(g);
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yd-offturn"&&Action(g,"select-target"));
                Answer(g,c=>c.Targets.SequenceEqual([2]));Reach(g,()=>Offer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            }
            else
            {
                if(scene is not("iron-chain"or"slash"or"alcohol"))Equip(g);
                StartSweep(g,2);
            }
            PickSide(g);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
            if(scene=="residency")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yd-reclaim"&&Action(g,"select-owned-cards"));
                var original=E<QingyaHandDiscardedEvent>(g).Single().Material.CardId;
                Answer(g,c=>c.Cards.SequenceEqual([original]));Play(g);var grant=E<QingyaGrantIssuedEvent>(g).Single();var turn=g.State.TurnNumber;
                Need(Location(g,original)==CardLocation.DiscardPile&&g.CardMovements.Any(m=>m.CardId==original&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(0)),
                    "A genuine discard child claims the original material and really discards that same entity again.");
                g=Cold(g,r);End(g);Reach(g,()=>E<ActualPhaseEndItemResolvedEvent>(g).Any(e=>e.EntitlementId==grant.GrantId)||g.State.TurnNumber>turn);
                Need(E<ActualPhaseEndItemResolvedEvent>(g).Any(e=>e.EntitlementId==grant.GrantId)&&!E<QingyaNativeIssuedEvent>(g).Any(),
                    "Leaving and reentering Discard terminates the original continuous-residency invoice; a same-ID return cannot be used.");
                _=Cold(g,r);continue;
            }
            if(scene=="offturn-alcohol")Reach(g,()=>E<QingyaGrantIssuedEvent>(g).Length==1);else Play(g);
            var issued=E<QingyaGrantIssuedEvent>(g).Single();var card=E<QingyaHandDiscardedEvent>(g).Single().Material.CardId;
            var expected=scene=="skip-discard"?ActualDiscardRecoveryPhaseKind.Ending:scene=="inserted"?ActualDiscardRecoveryPhaseKind.Judgment:
                scene=="offturn-alcohol"?ActualDiscardRecoveryPhaseKind.Play:ActualDiscardRecoveryPhaseKind.Discard;
            g=Cold(g,r);if(scene!="offturn-alcohol")End(g);Reach(g,()=>Branch(g,"use"),boundary:scene+" next truly executed phase");
            var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YaDan?.GrantId==issued.GrantId);var due=f.YaDan!.DuePhase!;
            Need(due.Kind==expected&&due.TurnNumber==issued.ActualTurn&&due.Token!=issued.IssuedPhase?.Token&&
                E<ActualDiscardRecoveryPhaseStartedEvent>(g).Any(e=>e.Phase==due)&&E<ActualPhaseEndingEvent>(g).Any(e=>e.Phase==due)&&
                P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("branch")=="use").All(c=>c.Cards.SequenceEqual([card])),
                "Only the next genuinely executed phase closes the obligation; skipped phases and replacement materials cannot satisfy it.");
            if(scene=="inserted")Need(E<ActualDiscardRecoveryPhaseRestoredEvent>(g).Any(e=>e.Phase.TurnNumber==issued.ActualTurn&&e.Phase.Kind==ActualDiscardRecoveryPhaseKind.Preparation)&&
                E<ActualPhaseEndingEvent>(g).Any(e=>e.Phase.TurnNumber==issued.ActualTurn&&e.Phase.Kind==ActualDiscardRecoveryPhaseKind.Preparation)&&
                E<QingyaGrantConsumedEvent>(g).Single(e=>e.GrantId==issued.GrantId).Phase.Kind==ActualDiscardRecoveryPhaseKind.Judgment,
                "Returning from the inserted Play restores an already started Preparation token; its later closure cannot steal the opportunity from the next new Judgment phase.");
            var expiredBefore=E<AlcoholExpiredEvent>(g).Count(e=>e.PlayerSeat==0);
            g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&(scene=="slash"?c.Targets.SequenceEqual([2]):true));
            Reach(g,()=>E<QingyaNativeReturnedEvent>(g).Any(e=>e.Origin.GrantId==issued.GrantId));
            var native=E<QingyaNativeIssuedEvent>(g).Single(e=>e.Origin.GrantId==issued.GrantId).Origin;
            Need(native.CardId==card&&E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==native.UseId&&e.CardId==card)==1&&
                E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==native.UseId)==1&&E<QingyaNativeReturnedEvent>(g).Count(e=>e.Origin==native)==1&&
                g.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Processing)==1&&
                !g.CardMovements.Any(m=>m.CardId==card&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(0)),
                "The frozen original printed entity uses its full native request directly from Discard exactly once, with one whole typed return and no intermediate Hand gain.");
            if(scene=="iron-chain")Need(E<CardUseDeclaredEvent>(g).Single(e=>e.ResolutionId==native.UseId).CardKind==CardKind.IronChain,"A legal original Iron Chain is used as Iron Chain, rather than narrowed to Slash or recast.");
            if(scene=="equipment")Need(V(g,0).Equipment.Any(c=>c.Id==card),"The original equipment completes a genuine native equipment use.");
            if(scene is"alcohol"or"offturn-alcohol")Need(E<JianjiOrdinaryAlcoholPaidEvent>(g).Any(e=>e.NativeUseId==native.UseId&&e.CountsQuota&&e.ActualTurn==issued.ActualTurn),
                "Off-Play ordinary Alcohol retains the real actual-turn quota invoice.");
            if(scene is"alcohol"or"offturn-alcohol")
            {
                Need(V(g,0).HasAlcoholEffect,"The genuinely used original Alcohol applies its native turn effect before the actual cursor returns.");
                if(scene=="offturn-alcohol")Need(due.ActualTurnOwnerSeat==1&&E<JianjiOrdinaryAlcoholPaidEvent>(g).Any(e=>e.ActorSeat==0&&e.ActualTurn<issued.ActualTurn)&&expiredBefore==1&&
                    E<TurnEndedEvent>(g).Count(e=>e.TurnNumber==issued.ActualTurn&&e.ActorSeat==1)==1&&issued.IssuedPhase?.Kind==ActualDiscardRecoveryPhaseKind.Ending,
                    "The previous own turn really spent and expired Alcohol; the later foreign turn's raw TurnEnded cannot cancel its issued right in Dianjun's genuine tail Play or reuse the old owner quota flag.");
                g=Cold(g,r);Reach(g,()=>E<ActualTurnCursorReturnedEvent>(g).Any(e=>e.ActualTurn==issued.ActualTurn));
                Need(!V(g,0).HasAlcoholEffect&&E<AlcoholExpiredEvent>(g).Count(e=>e.PlayerSeat==0)==expiredBefore+1,
                    "The real actual-turn return has no surviving Alcohol effect and never expires the same native effect twice.");
            }
            _=Cold(g,r);
        }
    }
    public static void QingyaPaidChildrenLossDeathAndRecursiveSlashCold()
    {
        foreach(var scene in new[]{"loss","death","lion","ox","recursive"})
        {
            var(g,r)=Start(scene);if(scene!="recursive")Equip(g);
            if(scene=="lion"){Use(g,"hurt");Play(g);}
            if(scene=="ox"){var grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);}
            var oldEquipment=V(g,0).Equipment.FirstOrDefault()?.Id;
            StartSweep(g,2);PickSide(g);g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
            if(scene is"loss"or"death")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch||scene=="death"&&(P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying}||!V(g,0).IsAlive),boundary:scene+" genuine paid HP, dying or automatic no-rescue completion");
                Need(E<QingyaHandDiscardedEvent>(g).Length==1&&(scene=="death"||!E<QingyaGrantIssuedEvent>(g).Any()),
                    "A real discarded-Hand child can change the original source before the paid sweep and future grant finish.");
                g=Cold(g,r);if(P(g)?.SkillPrompt?.SkillId==Watch)Continue(g);
                if(scene=="death")
                {
                    Reach(g,()=>!V(g,0).IsAlive&&E<YaDanCompletedEvent>(g).Any());
                    var paid=E<QingyaHandDiscardedEvent>(g).Single();
                    Need(g.State.Winner==Winner.None&&E<PlayerDyingEvent>(g).Count(e=>e.VictimSeat==0)==1&&
                        E<DyingResolvedEvent>(g).Count(e=>e.VictimSeat==0&&!e.Survived)==1&&E<PlayerDiedEvent>(g).Count(e=>e.VictimSeat==0)==1&&
                        E<YaDanCompletedEvent>(g).Count(e=>e.FrameId==paid.FrameId)==1&&E<QingyaHandDiscardedEvent>(g).Length==1&&!E<QingyaNativeIssuedEvent>(g).Any(),
                        "A paid discard and its genuine HP/death subtree finish without repaying after the non-Lord source dies.");
                    _=Cold(g,r);continue;
                }
            }
            Play(g);var grant=E<QingyaGrantIssuedEvent>(g).Single();var original=E<QingyaHandDiscardedEvent>(g).Single().Material.CardId;
            if(scene=="loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Skill),"A genuine paid child removes the original conditional Qingya instance.");
            var hp=V(g,0).Hp;End(g);Reach(g,()=>Branch(g,"use"),boundary:scene+" issued next-phase use");g=Cold(g,r);
            Answer(g,c=>c.Cards.SequenceEqual([original])&&c.Parameters.GetValueOrDefault("branch")=="use"&&(scene=="recursive"?c.Targets.SequenceEqual([2]):true));
            if(scene=="lion")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
                Need(V(g,0).Hp==hp+1&&g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.QingyaOrigin?.CardId==original),
                    "The native original Lion use owns the real replaced-armor recovery and its HP child under the exact Use.");
                g=Cold(g,r);Continue(g);
            }
            if(scene=="recursive")
            {
                Reach(g,()=>Offer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Branch(g,"side"));PickSide(g);
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
                Reach(g,()=>E<QingyaGrantIssuedEvent>(g).Length==2);
                Need(E<QingyaGrantIssuedEvent>(g).Select(e=>e.GrantId).Distinct().Count()==2&&E<QingyaHandDiscardedEvent>(g).Length==2&&
                    E<QingyaGrantIssuedEvent>(g).Last().IssuedPhase?.Kind==ActualDiscardRecoveryPhaseKind.Discard,
                    "A genuine later Slash creates a new independent sweep and next-phase obligation under its own native Use.");
            }
            Reach(g,()=>E<QingyaNativeReturnedEvent>(g).Any(e=>e.Origin.GrantId==grant.GrantId));
            Need(E<QingyaHandDiscardedEvent>(g).Count(e=>e.FrameId==grant.GrantId)==1&&E<QingyaNativeIssuedEvent>(g).Count(e=>e.Origin.GrantId==grant.GrantId)==1,
                "Paid source loss and nested native child returns never repay the original discard or use.");
            if(scene=="lion")Need(g.CardMovements.Count(m=>m.CardId==oldEquipment&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile)==1&&E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==0)==1,"Cold Lion replacement and recovery complete once.");
            if(scene=="ox")Need(g.CardMovements.Any(m=>m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile)&&V(g,0).Equipment.Any(c=>c.Id==original),"Native Ox replacement cleans its stored grain before the exact parent returns.");
            _=Cold(g,r);
        }
    }
    public static void TielunWholeRoundTrueUsesActorReplacementAndResetCold()
    {
        foreach(var scene in new[]{"round","actor","legacy","zero-actor","late","duel"})
        {
            var(g,r)=Start(scene);
            if(scene=="legacy")
            {
                Need(RoundCount(g,0)==1&&E<CardUseDeclaredEvent>(g).Any(e=>e.CardId==0&&e.CardKind==CardKind.Slash),
                    "An isolated legacy zero-entity native Use counts without inventing an accepted physical action.");
                _=Cold(g,r);continue;
            }
            if(scene=="duel")
            {
                Use(g,"wound-peer",[1]);Play(g);
                Native(g,a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([1]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});
                g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
                Need(E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0&&e.EffectiveCardKind==CardKind.Slash)&&RoundCount(g,0)==1,
                    "A genuinely played Slash inside native Duel is excluded while the enclosing true Duel Use counts once.");
                _=Cold(g,r);continue;
            }
            if(scene=="zero-actor")
            {
                End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yd-zero"&&Action(g,"select-target"));
                Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yd-actor"&&Action(g,"select-target"));
                var use=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.CardId==0&&u.CardKind==CardKind.Slash);var action=use.Action!;
                Need(use.PhysicalCardIds is{Count:0}&&!E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ActionId==action.ActionId)&&
                    E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==use.Id&&e.CardId==0&&e.SourceSeat==0)==1,
                    "The real triggered zero-entity Use declares and opens native target windows without inventing a CardActionAccepted event: "+JsonSerializer.Serialize(new{NoPhysical=use.PhysicalCardIds is{Count:0},NoAccepted=!E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ActionId==action.ActionId),OneDeclaration=E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==use.Id&&e.CardId==0&&e.SourceSeat==0)==1,UseId=use.Id,action.ActionId,use.PhysicalCardIds}));
                g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([2]));Reach(g,()=>E<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==use.Id));
                Need(E<ProgramCardUseActorReplacedEvent>(g).Count(e=>e.CardUseFrameId==use.Id&&e.PreviousActorSeat==0&&e.ActorSeat==2&&e.ProviderSeat==0)==1&&
                    !E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ActionId==action.ActionId)&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==use.Id)==1&&
                    RoundCount(g,0)==0&&RoundCount(g,2)==1,
                    "Without any accepted-action event, the same genuine zero-entity Use follows its actual replacement chain and counts once for the final actor.");
                _=Cold(g,r);continue;
            }
            Equip(g);
            if(scene=="late")
            {
                Need(V(g,0).Skills!.All(s=>s.ContentId!=Distance)&&RoundCount(g,0)==1,"Whole-round history exists before acquiring the distance skill.");
                Use(g,"grant-distance");Play(g);Need(V(g,0).Skills!.Any(s=>s.ContentId==Distance)&&RoundCount(g,0)==1,"Acquiring Tielun reads existing real uses without fabricating another use.");_=Cold(g,r);continue;
            }
            Need(RoundCount(g,0)==1&&g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])),
                "One real equipment Use reduces the distance to the opposite living seat before a first Slash is declared.");
            Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([scene=="actor"?1:2]));
            if(scene=="actor")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:yd-actor"&&Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([2]));Play(g);
                Need(E<ProgramCardUseActorReplacedEvent>(g).Any(e=>e.PreviousActorSeat==0&&e.ActorSeat==2&&e.ProviderSeat==0)&&RoundCount(g,0)==1&&RoundCount(g,2)==1,
                    "A genuine same-Use actor replacement moves one round-use entitlement to the final actor while keeping the physical provider unchanged.");
                _=Cold(g,r);continue;
            }
            Play(g);Need(RoundCount(g,0)==2,"A true converted Slash counts once, separately from its physical materials.");
            Use(g,"incoming",[1]);Reach(g,()=>P(g)?.PlayerSeat==0&&Action(g,"virtual-slash-offer")&&P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0])));Answer(g,c=>c.Targets.SequenceEqual([0]));
            Reach(g,()=>P(g)?.Kind==DecisionKind.RespondDodge);g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0&&e.EffectiveCardKind==CardKind.Dodge)&&RoundCount(g,0)==3&&RoundCount(g,1)==1,
                "Slash-defense Dodge is a real out-of-turn Use for the defender; the zero-entity incoming Slash belongs to its real actor.");
            var oldRound=E<RoundStartedEvent>(g).Last().RoundNumber;End(g);Reach(g,()=>E<RoundStartedEvent>(g).Last().RoundNumber>oldRound&&P(g)?.Kind==DecisionKind.PlayCard);
            Need(RoundCount(g,0)==0&&RoundCount(g,1)==0,"The next real round resets the dynamic use count without erasing previous receipts.");_=Cold(g,r);
        }
    }

    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static CardLocation Location(GameEngine g,int id)=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
    private static int RoundCount(GameEngine g,int seat)=>(int)typeof(GameEngine).GetMethod("CurrentRoundActualUseCount",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,[seat])!;
    private static bool Action(GameEngine g,string a)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==a)==true;
    private static bool Branch(GameEngine g,string b)=>Action(g,"ya-dan-qingya")&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==b);
    private static bool Offer(GameEngine g)=>P(g)?.PlayerSeat==0&&P(g)?.SkillPrompt?.SkillId==Skill&&Action(g,"activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected genuine Ya Dan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick)
    {var p=P(g);var c=p?.Choices.FirstOrDefault(pick);Need(p?.PlayerSeat==0&&c is not null,"Missing real human Ya Dan choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p!.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick)
    {var a=g.GetHumanLegalActions().FirstOrDefault(pick);Need(a is not null,"Missing real Ya Dan native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Equip(GameEngine g){Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);}
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},caller,line,"actual human Play");
    private static void StartSweep(GameEngine g,int target)
    {Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([target]));Reach(g,()=>Offer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void PickSide(GameEngine g)
    {Reach(g,()=>Branch(g,"side")||Branch(g,"hand"));if(Branch(g,"side")){Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Branch(g,"hand"));}}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? boundary=null)
    {
        for(var i=0;i<220;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Ya Dan ended at {caller}:{line} {boundary}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId==Watch)Continue(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(Branch(g,"decline"))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="decline");
            else throw new InvalidOperationException($"Unexpected Ya Dan {caller}:{line} {boundary}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Ya Dan {caller}:{line} {boundary}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.YaDan?.Stage}),Facts=g.Events.TakeLast(5).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves every permitted view, frozen original entity, phase obligation, native payment and typed child cursor.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(scene=="legacy"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));}
        Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ya-dan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ya-dan");
            if(scene=="offturn-alcohol")CharacterFixtureContent.Register(b,"ol:zhang-yi");
            var kind=scene=="iron-chain"?CardKind.IronChain:scene is"slash"or"recursive"or"legacy"?CardKind.Slash:scene is"alcohol"or"offturn-alcohol"?CardKind.Alcohol:scene=="lion"?CardKind.SilverLion:scene=="ox"?CardKind.WoodenOx:scene=="duel"?CardKind.Duel:CardKind.Crossbow;
            var kindText=char.ToLowerInvariant(kind.ToString()[0])+kind.ToString()[1..];
            var loss=scene=="loss";var death=scene=="death";
            static string A(string id,string effects,int targets=0)=>$$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"otherLiving","usesPerTurn":null,"effects":[{{effects}}]}""";
            var activations=new[]{A("hurt","""{"op":"loseHp","target":"owner","amount":1}"""),A("kill","""{"op":"damage","target":"selectedTarget","amount":20}""",1),
                A("wound-peer","""{"op":"loseHp","target":"selectedTarget","amount":2}""",1),
                A("incoming","""{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""",1),
                A("grant-distance",$$"""{"op":"grantSkills","target":"owner","skillIds":["{{Distance}}"]}""")};
            var conversions=new List<object>();
            if(kind!=CardKind.Slash)conversions.Add(new{id="slash",inputKinds=new[]{kindText},inputSuits=Array.Empty<string>(),outputKind="slash",forPlay=true,forResponse=scene=="duel"});
            if(scene=="round")conversions.Add(new{id="dodge",inputKinds=new[]{"crossbow"},inputSuits=Array.Empty<string>(),outputKind="dodge",forPlay=false,forResponse=true});
            var viewAs=conversions.Count==0?"":"\"viewAs\":"+JsonSerializer.Serialize(conversions)+",";
            var range=scene is"iron-chain"or"slash"or"alcohol"or"offturn-alcohol"or"recursive"or"duel"?""",{"id":"range","query":"outgoingDistance","operation":"add","value":-5,"priority":0}""":"";
            var defs=new List<string>{
                $$"""{"id":"{{Driver}}","revision":1,{{viewAs}}"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}{{range}}],"activations":[{{string.Join(',',activations)}}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""
            };
            if(scene=="duel")defs.Add("""{"id":"fixture:yd-slash-response","revision":1,"viewAs":[{"id":"slash","inputKinds":["duel"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}]}""");
            if(scene=="skip-discard")defs.Add("""{"id":"fixture:yd-skip-discard","revision":1,"triggers":[{"id":"skip","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]}""");
            if(scene=="inserted")defs.Add("""{"id":"fixture:yd-insert","revision":1,"triggers":[{"id":"real-inserted-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""");
            if(scene=="offturn-alcohol")defs.Add("""{"id":"fixture:yd-offturn","revision":1,"triggers":[{"id":"real-foreign-ending-zero-use","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}""");
            if(scene=="zero-actor")defs.Add("""{"id":"fixture:yd-zero","revision":1,"triggers":[{"id":"real-ending-zero-use","window":"turnEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}""");
            if(loss)defs.Add($$"""{"id":"fixture:yd-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-qingya","skillId":"{{Skill}}","minimumHp":13}]}""");
            if(loss||death)
                defs.Add($$"""{"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-hand-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DiscardReason}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"loseHp","target":"selectedTarget","amount":{{(death?20:1)}}}]}]}""");
            if(loss||death||scene=="lion")
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-health-child","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"{{(scene=="lion"?"recovery":"loss")}}"},"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health-child","options":[{"id":"continue"}]}]}]}""");
            if(scene=="residency")
                defs.Add($$"""{"id":"fixture:yd-reclaim","revision":1,"triggers":[{"id":"actual-claim-and-discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perCard","movementReasons":["{{DiscardReason}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"},{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"returned"},{"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"discardPile","awaitMovementTriggers":true}]}]}""");
            if(scene is"actor"or"zero-actor")
                defs.Add("""{"id":"fixture:yd-actor","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
            if(scene=="legacy")
                defs.Add("""{"id":"fixture:yd-legacy","revision":1,"triggers":[{"id":"real-old-zero-use","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"normalSlashTarget","useCardActionWindows":false}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="雅丹原生真实行为小夹具",
                optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"原生用牌、弃置和阶段子链"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:yd-pick","实际雅丹","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var skills=new List<string>{Driver};if(scene!="late")skills.Add(Distance);skills.Add(loss?"fixture:yd-conditional":Skill);
            if(loss||death||scene=="lion")skills.Add(Watch);
            if(scene=="skip-discard")skills.Add("fixture:yd-skip-discard");if(scene=="residency")skills.Add("fixture:yd-reclaim");if(scene is"actor"or"zero-actor")skills.Add("fixture:yd-actor");if(scene=="legacy")skills.Add("fixture:yd-legacy");
            if(scene=="inserted")skills.Add("fixture:yd-insert");if(scene=="offturn-alcohol")skills.Add("fixture:yd-offturn");if(scene=="zero-actor")skills.Add("fixture:yd-zero");
            b.AddGeneral(new(Owner,"实际雅丹","ol-ya-dan","fixture:yd-pick","qun",12,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:yd-peer-{i}").ToArray();
            foreach(var peer in peers)
            {
                var extra=new List<string>();if(loss||death)extra.Add(Peer);if(scene=="duel")extra.Add("fixture:yd-slash-response");if(scene=="offturn-alcohol")extra.Add("ol:dianjun");
                b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"wei",12,extra));
            }
            b.AddCard(new("fixture:yd-card","真实"+kind,CardCatalog.Get(kind).CategoryName,"固定原生实体",kind));
            b.AddDeck(new("fixture:yd-deck","雅丹同质真实原生牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:yd-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"雅丹真实阶段小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:yd-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}

