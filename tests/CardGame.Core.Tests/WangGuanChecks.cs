using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangGuanChecks
{
    private const string Miu="ol:miuyan",Shilu="ol:shilu",Owner="fixture:wg-owner",Driver="fixture:wg-driver",Quiet="fixture:wg-quiet",
        Watch="fixture:wg-watch",Health="fixture:wg-health",Inspect="fixture:wg-inspect",Mode="identity:classic-wang-guan-fixture";
    public static void MiuyanNativeBlackMaterialsPolarityAndWholeDamageCold()
    {
        foreach(var scene in new[]{"hand","equipment","lion","grain","ox-equipment"})
        {
            var(g,r)=Start(scene);int material;
            if(scene=="hand")material=V(g,0).Hand[0].Id;
            else
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);
                material=V(g,0).Equipment.Single().Id;
                if(scene=="lion"){Use(g,"lose");Play(g);}
                if(scene is"grain"or"ox-equipment")
                {
                    var grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);
                    if(scene=="grain")material=grain;
                }
            }
            Fire(g,1,material);Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackDiscard);
            var issued=E<MiuyanUseIssuedEvent>(g).Single().Invoice;
            Need(issued.PreviousState==SkillPolarity.Yang&&issued.CurrentState==SkillPolarity.Yin&&issued.Material.CardId==material&&
                issued.Material.From.Zone==(scene=="grain"?CardZoneKind.WoodenOxGrain:scene=="hand"?CardZoneKind.Hand:CardZoneKind.Equipment)&&
                !E<CompletedUndamagedUseDamageRecordedEvent>(g).Any(e=>e.CardUseFrameId==issued.UseId),
                "The actual accepted native black conversion switches once before effect damage and retains its real Hand/Equipment/Ox-grain cost.");
            g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(E<CompletedUndamagedUseDamageRecordedEvent>(g).Where(e=>e.CardUseFrameId==issued.UseId).Sum(e=>e.Amount)==1&&
                E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1&&
                E<WangGuanStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse&&e.NativeId==issued.UseId)==1&&
                E<MiuyanEntityObtainedEvent>(g).Any()&&g.CardMovements.Count(m=>m.CardId==material&&m.From==issued.Material.From&&m.Reason==CardMoveReasons.Use)==1,
                "Only real whole-Use positive damage yields Yang's exact shown-hand gain; every native cost is paid once.");
            if(scene=="lion")Need(E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"The converted equipped Lion runs its actual recovery before the native Fire Attack.");
            if(scene=="ox-equipment")Need(g.CardMovements.Any(m=>m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile),"Using the equipped Ox runs its actual stored-grain cleanup.");
            if(scene=="hand")
            {
                var count=E<MiuyanUseIssuedEvent>(g).Length;Fire(g,1,source:Driver);PayFire(g);Play(g);
                Need(E<MiuyanUseIssuedEvent>(g).Length==count,"Another real Fire Attack conversion does not issue or toggle Miuyan.");
                Fire(g,1);PayFire(g);Play(g);
                Need(E<MiuyanUseIssuedEvent>(g).Last().Invoice.PreviousState==SkillPolarity.Yin&&!E<MiuyanRoundDisabledEvent>(g).Any(),"A damaging Yin Use returns to Yang and is not suppressed.");
            }
            _=Cold(g,r);
        }
    }
    public static void MiuyanYangActualPhaseRevealsAndShiluSelfFireCold()
    {
        foreach(var expired in new[]{false,true})
        {
            var(g,r)=Start("hand");var shown=V(g,0).Hand[0].Id;Show(g,shown);Use(g,"gift",[shown],[2]);Play(g);
            Use(g,"top");Play(g);var top=E<ProgramCardsRevealedEvent>(g).Last(e=>e.Bind=="top").Cards.Select(c=>c.Id).ToArray();
            Need(top.Length==2&&top.All(id=>!E<MiuyanPhaseHandRevealedEvent>(g).Any(e=>e.CardId==id)),"Public top cards shown while in Processing never become hand-show history merely by entering Hand later.");
            var oldPhase=E<MiuyanPhaseHandRevealedEvent>(g).Single(e=>e.CardId==shown).Phase;
            if(expired){var turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);}
            Fire(g,1);PayFire(g);Play(g);
            Need(E<MiuyanEntityObtainedEvent>(g).Any(e=>e.Entity.CardId==shown)==!expired&&
                (expired?V(g,2).Hand.Any(c=>c.Id==shown):V(g,0).Hand.Any(c=>c.Id==shown)),
                "Yang uses the exact actual phase token: a card truly shown in Hand can change holder and still be obtained, while an old phase cannot claim it.");
            Need(E<MiuyanUseIssuedEvent>(g).Single().Invoice.Phase.Token==oldPhase.Token==!expired,"Each actual phase has an independent frozen native token.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("hand");Fire(g,0);Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackReveal);Answer(g,c=>c.Cards.Count==1);PayFire(g);
            Reach(g,()=>Branch(g,"target"));var draw=E<WangGuanStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveShiluDrawRevealIdentity);
            Need(draw.FrozenHp==V(g,0).Hp&&E<WangGuanMovementIssuedEvent>(g).Single(e=>e.FrameId==draw.FrameId).Count==draw.FrozenHp&&
                !E<WangGuanStartedEvent>(g).Any(e=>e.Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse),"Self Fire Attack completes the actual post-damage HP draw before any Yang whole-Use benefit.");
            Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Branch(g,"card"));Private(g);g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);
            var identity=E<ShiluHandIdentityGrantedEvent>(g).Single();var claim=E<MiuyanEntityObtainedEvent>(g).Single(e=>e.Entity.CardId==identity.CardId);
            var history=g.Events.Select(e=>e.Payload).ToArray();
            Need(Array.FindIndex(history,e=>Equals(e,identity))<Array.FindIndex(history,e=>Equals(e,claim))&&V(g,0).Hand.Any(c=>c.Id==identity.CardId&&c.Kind==CardKind.Crossbow&&c.IdentityKind is null)&&
                !g.CardMovements.Any(m=>m.From==CardLocation.Hand(0)&&m.To==m.From&&m.Reason.Value=="skill-program.wang-guan.miuyan.gain"),
                "The newly Shilu-revealed foreign hand entity is gained by this same Yang only after the full native damage subtree; its first departure ends the separate Slash identity and own shown cards create no fake gain.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("qingliang");Fire(g,1);PayFire(g);Play(g);
            var reveals=E<MiuyanPhaseHandRevealedEvent>(g).Where(e=>e.RevealKind==nameof(ProgramQingliangRevealEvent)).ToArray();
            Need(reveals.Length>=4&&reveals.All(e=>e.HolderSeat==1&&V(g,0).Hand.Any(c=>c.Id==e.CardId))&&
                reveals.All(e=>E<MiuyanEntityObtainedEvent>(g).Any(v=>v.Entity.CardId==e.CardId)),
                "The actual Qingliang public whole-Hand binding, rather than a later re-read of HandCount, supplies all original shown entities to this genuine Yang Use.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("draft");End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="classic:huimin"&&Action(g,"activate"));
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Action(g,"public-draft-show"));
            while(Action(g,"public-draft-show")){Answer(g,c=>c.Cards.Count==1);Reach(g,()=>Action(g,"public-draft-show")||Action(g,"public-draft-start"));}
            var draft=E<ProgramHandDraftRevealedEvent>(g).Single();var ledger=E<MiuyanPhaseHandRevealedEvent>(g).Where(e=>e.RevealFrameId==draft.FrameId).ToArray();
            Need(ledger.Select(e=>e.CardId).Order().SequenceEqual(draft.CardIds.Order())&&ledger.All(e=>e.HolderSeat==0&&e.Phase.Kind==ActualDiscardRecoveryPhaseKind.Ending),
                "The native Ending hand draft records the exact original public Hand entities in its true actual-phase token.");
            g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>E<ProgramHandDraftCardObtainedEvent>(g).Length==draft.CardIds.Count);
            Need(E<ProgramHandDraftCardObtainedEvent>(g).All(e=>e.RecipientSeat!=0&&V(g,e.RecipientSeat).Hand.Any(c=>c.Id==e.CardId))&&
                E<MiuyanPhaseHandRevealedEvent>(g).Where(e=>e.RevealFrameId==draft.FrameId).SequenceEqual(ledger),
                "Real hand-draft transfers change actual holders without deleting or replacing the phase's original entity reveal invoices.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("shandao");var selected=V(g,1).Hand[0].Id;
            Accept(g,new UseProgramSkillCommand(0,"ol:shandao","top-and-two-tricks",[],[],g.Revision,P(g)!.PromptId));
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:shandao"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="target"));
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="target"&&c.Targets.SequenceEqual([1]));
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="confirm");
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:shandao"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="card"));
            Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)&&c.Parameters.GetValueOrDefault("slot-index")=="0");
            Reach(g,()=>E<TianChouShandaoCompletedEvent>(g).Length==1);Play(g);
            Need(V(g,1).Hand.Any(c=>c.Id==selected)&&g.CardMovements.Any(m=>m.CardId==selected&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.DrawPile)&&
                g.CardMovements.Any(m=>m.CardId==selected&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(1))&&
                !E<MiuyanPhaseHandRevealedEvent>(g).Any(e=>e.CardId==selected),
                "The exact native Shandao top-placement movement reveal followed by genuine Five Grains reacquisition is not a rule instruction to show Hand.");
            g=Cold(g,r);Fire(g,2);PayFire(g);Play(g);
            Need(!E<MiuyanEntityObtainedEvent>(g).Any(e=>e.Entity.CardId==selected)&&V(g,1).Hand.Any(c=>c.Id==selected),
                "A real Yang cannot claim the unrelated unshown Hand entity merely because native public movement exposed its face.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("private");
            Need(Action(g,"private-hand-take")&&E<ProgramPrivateHandViewedEvent>(g).Length==1&&!E<MiuyanPhaseHandRevealedEvent>(g).Any()&&g.CreateSnapshot(2).PrivateRevealedCards is null,
                "A true native private inspection is excluded from public phase hand-show history and from other viewers.");
            g=Cold(g,r);Answer(g,_=>true);Play(g);Fire(g,1);PayFire(g);Play(g);
            Need(E<MiuyanPhaseHandRevealedEvent>(g).Count(e=>e.HolderSeat==1)==1,"Only the later actually public native Fire Attack hand reveal enters the phase ledger.");
        }
    }
    public static void MiuyanYinActualRoundSuppressionAndNewInstanceCold()
    {
        var(g,r)=Start("hand");Fire(g,1);PayFire(g,false);Play(g);
        Need(E<MiuyanUseIssuedEvent>(g).Single().Invoice.PreviousState==SkillPolarity.Yang&&!E<MiuyanRoundDisabledEvent>(g).Any(),"An undamaging Yang still switches, without Yin's penalty.");
        Fire(g,1);Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackDiscard);var invoice=E<MiuyanUseIssuedEvent>(g).Last().Invoice;g=Cold(g,r);Answer(g,c=>c.Cards.Count==0);Play(g);
        var disabled=E<MiuyanRoundDisabledEvent>(g).Single();
        Need(invoice.PreviousState==SkillPolarity.Yin&&disabled.UseId==invoice.UseId&&disabled.RoundNumber==invoice.RoundNumber&&
            !E<CompletedUndamagedUseDamageRecordedEvent>(g).Any(e=>e.CardUseFrameId==invoice.UseId)&&!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId==Miu),
            "Yin is suppressed only after the whole real native Use actually finishes without positive damage.");
        Use(g,"lose-miuyan");Play(g);Use(g,"regrant");Play(g);g=Cold(g,r);
        Need(!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId==Miu)&&V(g,0).Skills!.Any(s=>s.ContentId==Shilu),
            "The actual-round owner/skill prohibition survives a real new skill instance and does not suppress locked Shilu.");
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);
        Need(g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId==Miu),"The next genuine round releases the old round prohibition without rewriting skill enablement.");
        Fire(g,1);PayFire(g);Play(g);
        Need(E<MiuyanUseIssuedEvent>(g).Last().Invoice.RoundNumber>disabled.RoundNumber&&E<MiuyanRoundDisabledEvent>(g).Length==1,"The next round's real accepted Use has an independent issuance.");
        _=Cold(g,r);
    }
    public static void ShiluPerDamageOpaqueHandIdentityAndPaidChildrenCold()
    {
        foreach(var scene in new[]{"identity","draw-loss","draw-death","issuer-death"})
        {
            var(g,r)=Start(scene);var hp=V(g,0).Hp;Use(g,"damage-two",targets:[0]);
            if(scene is"draw-loss"or"draw-death")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
                var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.WangGuan is not null);
                Need(f.WangGuan!.FrozenHp==hp-2&&f.WangGuan.Stage==WangGuanStage.ShiluDrawChildren&&
                    E<WangGuanMovementIssuedEvent>(g).Single(e=>e.FrameId==f.Id).Count==hp-2&&E<ShiluHandIdentityGrantedEvent>(g).Length==0,
                    "One actual damage instance of two points draws the frozen current HP exactly once, before its real gain child and later hand choice.");
                g=Cold(g,r);Continue(g);
                if(scene=="draw-death")
                {
                    Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.WangGuan is not null));
                    Need(g.State.Winner==Winner.None&&E<WangGuanStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveShiluDrawRevealIdentity)==1&&
                        !E<ShiluHandIdentityGrantedEvent>(g).Any()&&g.CardMovements.Count(m=>m.Reason.Value=="skill-program.wang-guan.shilu.draw")==hp-2,
                        "True non-Lord death in the paid native Draw child returns exactly once without asking a dead owner to choose or creating a new hand entity.");
                    _=Cold(g,r);continue;
                }
            }
            Reach(g,()=>Branch(g,"target"));
            Need(!P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0]))&&!P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([2])),"The post-draw hand target uses real current attack range and excludes self.");
            Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Branch(g,"card"));Private(g);g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);
            var granted=E<ShiluHandIdentityGrantedEvent>(g).Single();
            Need(E<WangGuanStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveShiluDrawRevealIdentity)==1&&
                V(g,1).Hand.Single(c=>c.Id==granted.CardId).IdentityKind==CardKind.Slash&&g.CreateSnapshot(2).Players[1].Hand.Count==0,
                "The actually displayed single Hand entity becomes a real Slash identity without exposing the holder's other cards or triggering once per damage point.");
            if(scene=="issuer-death")
            {
                Use(g,"kill-owner");Reach(g,()=>!V(g,0).IsAlive);
                Need(g.State.Winner==Winner.None&&V(g,1).Hand.Single(c=>c.Id==granted.CardId).IdentityKind==CardKind.Slash,"An already-issued hand entity identity survives its original issuer's actual death.");
            }
            else if(scene=="identity")
            {
                Use(g,"request",targets:[1]);Reach(g,()=>Action(g,"assisted-physical-slash")&&P(g)!.Choices.Any(c=>c.Targets.Count==1));Answer(g,c=>c.Targets.SequenceEqual([0]));
                Reach(g,()=>E<CardUseDeclaredEvent>(g).Any(e=>e.SourceSeat==1&&e.CardId==granted.CardId&&e.CardKind==CardKind.Slash));g=Cold(g,r);Play(g);
                Need(E<CardActionAcceptedEvent>(g).Any(e=>e.Action.Type==CardActionType.Use&&e.Action.ActorSeat==1&&e.Action.PhysicalCards.Any(c=>c.CardId==granted.CardId)&&e.Action.ConversionChain.Any(s=>s.SkillId==Shilu)),
                    "The canonical hand identity supplies a genuine native requested Slash Use with its exact immutable original identity source.");
            }
            else
            {
                Need(V(g,0).Skills!.All(s=>s.ContentId!=Shilu),"The paid native draw child really removed the original Shilu source.");
                Use(g,"take",targets:[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);
                Need(V(g,0).Hand.Single(c=>c.Id==granted.CardId).IdentityKind is null,"The first genuine departure clears the identity even though the original skill is already lost.");
                Use(g,"gift",[granted.CardId],[1]);Play(g);
                Need(V(g,1).Hand.Single(c=>c.Id==granted.CardId).IdentityKind is null,"Returning the same physical entity to the same holder cannot revive an expired Hand residency.");
            }
            _=Cold(g,r);
        }
    }

    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="wang-guan"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Wang Guan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Only the real human chooser may answer.");Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(select).Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> select)
    {
        var a=g.GetHumanLegalActions().FirstOrDefault(select)??throw new InvalidOperationException("Missing actual Wang Guan action: "+Diagnostic(g));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    }
    private static void Fire(GameEngine g,int target,int? material=null,string source=Miu)=>Native(g,a=>a.Kind==LegalActionKind.FireAttack&&a.ConversionSource?.SkillId==source&&a.TargetSeats.SequenceEqual([target])&&(material is null||a.CardId==material));
    private static void PayFire(GameEngine g,bool damage=true){Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackDiscard);Answer(g,c=>damage?c.Cards.Count==1:c.Cards.Count==0);}
    private static void Show(GameEngine g,int card){Use(g,"show");Reach(g,()=>Action(g,"select-owned-cards"));Answer(g,c=>c.Cards.SequenceEqual([card]));Play(g);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<320;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Actual Wang Guan fixture ended before boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&V(g,p.PlayerSeat).IsHuman)
            {
                if(p.Kind==DecisionKind.PlayCard)End(g);
                else if(p.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard)Answer(g,c=>c.Cards.Count==1);
                else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
                else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
                else if(p.Kind==DecisionKind.RespondDodge)Answer(g,c=>c.Cards.Count==0);
                else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else if(p.SkillPrompt?.SkillId is Watch or Health)Continue(g);
                else if(Branch(g,"target"))Answer(g,c=>c.Targets.SequenceEqual([1]));
                else if(Branch(g,"card"))Answer(g,_=>true);
                else throw new InvalidOperationException("Unexpected actual Wang Guan prompt: "+Diagnostic(g));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Wang Guan boundary: "+Diagnostic(g));
    }
    private static void Private(GameEngine g)
    {
        var p=P(g)!;var state=State(g);
        Need(p.IsPrivate&&p.ValidCardIds.Count==0&&p.Choices.All(c=>c.Cards.Count==0)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&
            Enumerable.Range(0,4).All(s=>g.CreateSnapshot(s).Players.Where(v=>v.Seat!=s).All(v=>v.Hand.Count==0)),"Only the real owner sees opaque slots; no other Hand IDs leak in any viewer.");
        Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state&&
            !g.Submit(new AnswerPromptCommand(0,p.PromptId,new("invalid"),g.Revision)).Accepted&&State(g)==state,"Illegal chooser and invented opaque slot leave every view, RNG and paid cursor unchanged.");
        var receipt=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.WangGuan is not null).WangGuan!;
        Need(receipt.Entities is IList<WangGuanHandEntity>{IsReadOnly:true},"The owning private Hand candidate collection is frozen.");
        try{((IList<WangGuanHandEntity>)receipt.Entities).Add(default!);throw new InvalidOperationException("Mutable Shilu entities.");}catch(NotSupportedException){}
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).IsAlive,V(g,s).HandCount}),Frames=g.ResolutionStack.Select(f=>new{f.Id,Kind=f.GetType().Name}),Facts=g.Events.TakeLast(8).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold command replay preserves all private viewers, exact native Use/phase/round invoices, physical cards, permanent identities and once-paid child returns.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"draw-death"or"issuer-death"?Role.Rebel:Role.Lord,
            ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(scene=="private"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Action(g,"private-hand-take"));}else Play(g);
        return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wang-guan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:wang-guan");
            if(scene=="shandao")CharacterFixtureContent.Register(b,"ol:tian-chou");
            if(scene=="draft")CharacterFixtureContent.Register(b,"classic:cao-jie");
            if(scene=="qingliang")CharacterFixtureContent.Register(b,"ol:rui-ji");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[{"id":"control-fire","inputKinds":[],"inputSuits":[],"outputKind":"fireAttack","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
              {"id":"show","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"shown"},{"op":"revealBoundCards","target":"owner","sourceBind":"shown"}]},
              {"id":"gift","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
              {"id":"top","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"top","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"ownerHand","awaitMovementTriggers":true}]},
              {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
              {"id":"kill-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
              {"id":"lose-miuyan","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:miuyan"],"sourceBind":"standard:none"}]},
              {"id":"regrant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:miuyan"]}]},
              {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},
              {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"requested"}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
            $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}"""};
            if(scene is"draw-loss"or"draw-death")
            {
                var tail=scene=="draw-loss"?"""{"op":"loseHp","target":"owner","amount":1}""":"""{"op":"loseHp","target":"owner","amount":20}""";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-native-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.wang-guan.shilu.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{{tail}}]}]}""");
            }
            if(scene=="draw-loss")defs.Add("""{"id":"fixture:wg-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-shilu","skillId":"ol:shilu","minimumHp":3}]}""");
            if(scene=="private")defs.Add($$"""{"id":"{{Inspect}}","revision":1,"triggers":[{"id":"inspection","window":"drawPhaseEnded","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"viewAndTakeSelectedTargetHand","target":"selectedTarget","amount":1}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实王瓘原生夹具",
                optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生规则"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:wg-pick","实际本人","公开固定选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var skills=new List<string>{Miu,scene=="draw-loss"?"fixture:wg-conditional":Shilu,Driver,Health};if(scene is"draw-loss"or"draw-death")skills.Add(Watch);if(scene=="private")skills.Add(Inspect);if(scene=="draft")skills.Add("classic:huimin");if(scene=="shandao")skills.Add("ol:shandao");
            b.AddGeneral(new(Owner,"实际王瓘","ol-wang-guan","fixture:wg-pick","wei",4,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:wg-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"wei",12,scene=="qingliang"?["ol:qingliang"]:[]));
            var kind=scene=="lion"?CardKind.SilverLion:scene is"grain"or"ox-equipment"?CardKind.WoodenOx:CardKind.Crossbow;
            b.AddCard(new("fixture:wg-card","真实"+kind,"装备牌","真实原生实体",kind));
            b.AddDeck(new("fixture:wg-deck","固定同花真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:wg-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"王瓘原生小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:wg-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
