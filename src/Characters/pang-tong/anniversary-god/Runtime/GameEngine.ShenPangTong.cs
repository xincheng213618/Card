namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShenPangTongRemovalReason="skill-program.anniversary:kunyu.remove-fire-card";
    private static CardConversionSource ShenPtSource(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class ShenPangTongCapability(GameEngine e):IShenPangTongHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginShenPt(f,op); }
    private CharacterState? ShenPtLuanOwner=>_turnNumber>0&&_phase is not (TurnPhase.NotStarted or TurnPhase.Finished)&&IsValidPlayerSeat(_currentSeat)&&
        _players[_currentSeat].IsAlive&&HasRuntimeSkill(_players[_currentSeat],ShenPangTongComposition.Luansuo)?_players[_currentSeat]:null;
    private bool IsShenPangTongHandDiscardForbidden(int actor,Card card,CardLocation from,OwnedCardMoveIntent intent)=>
        intent==OwnedCardMoveIntent.Discard&&from.Zone==CardZoneKind.Hand&&from.OwnerSeat is{} seat&&IsValidPlayerSeat(seat)&&
        _cardZones.GetLocation(card.Id)==from&&ShenPtLuanOwner is not null;
    private ProgramCardIdentityMatch? ShenPangTongLuansuoIdentity(CharacterState holder,Card card)
    {
        if(ShenPtLuanOwner is not{} owner||_cardZones.GetLocation(card.Id)!=CardLocation.Hand(holder.Seat))return null;
        var suit=EffectiveSuit(holder,card);
        if(_cardMovements.Any(m=>m.TurnNumber==_turnNumber&&m.To==CardLocation.DiscardPile&&GetAdvancedCard(m.CardId).Suit==suit))return null;
        var program=_contentRegistry.GetSkill(ShenPangTongComposition.Luansuo).Program!;
        var identity=program.CardIdentities.Single();
        return new(program,identity,new(ShenPangTongComposition.Luansuo,identity.Id,owner.Seat,GetRuntimeSkillInstanceId(owner,ShenPangTongComposition.Luansuo)));
    }
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram,39200)]
    private void AddShenPtChainActions(List<LegalAction> actions,CharacterState actor,IReadOnlyList<Card> cards,int? selectedPhysicalCardId=null)
    {
        var targets=_players.Where(p=>p.IsAlive).OrderBy(p=>p.Seat).ToArray();
        foreach(var card in cards.Where(c=>selectedPhysicalCardId is null||c.Id==selectedPhysicalCardId))
        {
            if(ShenPangTongLuansuoIdentity(actor,card) is not{}identity)continue;
            // A mandatory card identity has Iron Chain's native recast option;
            // recasting remains a non-use and never changes Fengliao's state.
            actions.Add(new(LegalActionKind.Recast,card.Id,null,"重铸【铁索连环】，摸一张牌",PlayedCardKind:CardKind.IronChain){ConversionSource=identity.Source});
            if(!IsTurnHandCardRestricted(actor,card))AddProgramIronChainUseActions(actions,actor,card,targets,CardKind.IronChain,identity.Source);
        }
    }
    private ProgramCardTriggerWindowFrame? ShenPtUse(ProgramSkillWindowContext w)=>_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId);
    private static int[] ShenPtTargets(CardActionContext a)=>
        (a.EffectiveKind==CardKind.BorrowedSword?a.TargetSeats.Where((_,i)=>i%2==0):a.EffectiveDesignatedTargetSeats).Distinct().ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,39200,SkillProgramEffectOp.ResolveShenPangTongFengliao,SkillProgramEffectOp.ResolveShenPangTongKunyu)]
    private bool ShenPtEligible(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
    {
        if(t.Effects[0].Op==SkillProgramEffectOp.ResolveShenPangTongFengliao)
            return ShenPtUse(w) is{} use&&use.Action.Type==CardActionType.Use&&use.Action.ActorSeat==c.OwnerSeat&&ShenPtTargets(use.Action) is[var target]&&
                _players[target].IsAlive&&!ProgramEventHistory<ShenPangTongStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.Operation==SkillProgramEffectOp.ResolveShenPangTongFengliao&&e.ActionId==use.Action.ActionId);
        return _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId) is{} window&&window.OwnerSeat==c.OwnerSeat&&
            _resolutionStack.OfType<DeathFrame>().SingleOrDefault(p=>p.Id==window.DeathFrameId) is{ShenPangTongOutcomeDeferred:true,ShenPangTongRevival:null} death&&death.VictimSeat==c.OwnerSeat&&!_players[c.OwnerSeat].IsAlive;
    }
    private SkillProgramStepOutcome BeginShenPt(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.ShenPangTong is not null||f.InstructionIndex!=1)throw new InvalidOperationException("Shen Pang Tong requires his original unpaid native instruction.");
        var use=op==SkillProgramEffectOp.ResolveShenPangTongFengliao?ShenPtUse(f.WindowContext!):null;
        var death=op==SkillProgramEffectOp.ResolveShenPangTongKunyu?
            GetCurrentDeathFrame(_resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(p=>p.Id==f.WindowContext!.ParentFrameId).DeathFrameId):null;
        var polarity=use is not null?CommitProgramConversionPolarity(f):SkillPolarity.Yang;
        f=GetActiveProgramFrame(f.Id);
        var target=use is not null?ShenPtTargets(use.Action).Single():f.OwnerSeat;
        var r=new ShenPangTongReceipt(ShenPtSource(f),f.GameplayHash,op,f.WindowContext!.ParentFrameId,
            use is null?ShenPangTongStage.RemovalChildren:polarity==SkillPolarity.Yang?ShenPangTongStage.DrawAttempt:ShenPangTongStage.DamageChildren)
            {ActionId=use?.Action.ActionId??0,TargetSeat=target,Polarity=polarity,DeathId=death?.Id??0,Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{ShenPangTong=r});
        AdvanceEventRulesAndQueueFact(new ShenPangTongStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActionId,target,polarity,r.DeathId));
        if(use is not null)
        {
            if(!_players[target].IsAlive){FinishShenPt(f);return SkillProgramStepOutcome.AwaitChild;}
            if(polarity==SkillPolarity.Yang)BeginOperationProgramDraw(f,target,1,new("skill-program.anniversary:fengliao.draw"));
            else BeginProgramSkillDamage(f,target,1,nature:DamageNature.Fire);
        }
        else
        {
            var fire=_cardZones.CardsAt(CardLocation.DrawPile).FirstOrDefault(c=>c.Kind is CardKind.FireSlash or CardKind.FireAttack);
            if(fire is null){FinishShenPt(f);return SkillProgramStepOutcome.AwaitChild;}
            ReplaceRuntimeTop(f=f with{ShenPangTong=r with{RemovedCardId=fire.Id},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            MoveProgramCardsFromMultipleSources([fire.Id],CardLocation.OutsideGame,new(ShenPangTongRemovalReason),(batch,moves)=>
            {
                var parent=GetActiveProgramFrame(f.Id);
                if(moves is not[var m]||m.CardId!=fire.Id||m.From!=CardLocation.DrawPile||m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Kunyu requires an actual fire damage card removed from the real draw pile.");
                ReplaceRuntimeTop(parent with{ShenPangTong=parent.ShenPangTong! with{After=m.Sequence,BatchId=batch}});
                AdvanceEventRulesAndQueueFact(new ShenPangTongRemovalPaidEvent(f.Id,r.DeathId,batch,r.Before,m.Sequence));
            },disclosure:CardMovementDisclosure.FaceDown);
            ContinueShenPt(f.Id);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenPt(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||
        TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||
        TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id);
    private void ContinueShenPt(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ShenPangTong:{} r} f||f.Id!=id)return;
        RequireShenPt(f);if(DrainShenPt(id))return;
        f=GetActiveProgramFrame(id);r=f.ShenPangTong!;
        if(r.Stage==ShenPangTongStage.DrawAttempt)return;
        if(r.Stage==ShenPangTongStage.RemovalChildren)
        {
            var death=GetCurrentDeathFrame(r.DeathId);
            if(!death.ShenPangTongOutcomeDeferred||death.VictimSeat!=f.OwnerSeat||death.ShenPangTongRevival is not null||r.RemovedCardId<=0||r.After<=r.Before)
                throw new InvalidOperationException("Kunyu lost its original paid death-return grant.");
            UpdateDeathFrame(death.Id,d=>d with{ShenPangTongRevival=new(f.Id,r.Source,r.GameplayHash,r.ParentId,r.RemovedCardId,r.Before,r.After,r.BatchId)});
            AdvanceEventRulesAndQueueFact(new ShenPangTongRevivalGrantedEvent(f.Id,death.Id,f.OwnerSeat));
        }
        FinishShenPt(f);
    }
    private void FinishShenPt(ProgramSkillFrame f)
    {ReplaceRuntimeTop(f=f with{ShenPangTong=null,PendingMovementContinuation=null});AdvanceRuntimeProgram(f.Id);}
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShenPangTongFengliao)]
    private void ReturnShenPtDraw(ProgramSkillFrame supplied,NativeDrawOutcome o)
    {
        var f=GetActiveProgramFrame(supplied.Id);RequireShenPt(f);var r=f.ShenPangTong!;
        if(r.Stage!=ShenPangTongStage.DrawAttempt||r.DrawReturned||f.NativeDraw is not null||o.RequestedCount!=1||o.RecipientSeat!=r.TargetSeat)
            throw new InvalidOperationException("Fengliao lost its exact typed target draw return.");
        ReplaceRuntimeTop(f with{ShenPangTong=r with{Stage=ShenPangTongStage.DrawChildren,DrawReturned=true,ActualDraw=o.ActualDrawIds.Count,Before=o.SequenceBefore,After=o.SequenceAfter}});
        AdvanceEventRulesAndQueueFact(new ShenPangTongDrawReturnedEvent(f.Id,r.TargetSeat,o.ActualDrawIds.Count,o.AttemptFrameId,o.SequenceBefore,o.SequenceAfter));
        ContinueShenPt(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,39200)] private bool ResumeShenPt(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ShenPangTong:not null}f||f.Id!=id)return false;ContinueShenPt(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,39200)] private bool ReturnShenPtMovement(ProgramSkillFrame f)=>f.ShenPangTong is not null&&ResumeShenPt(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,39200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,39200)] private bool KeepPaidShenPt(ProgramSkillFrame f)=>f.ShenPangTong is not null;

    private bool TryBeginShenPangTongKunyuDeath(DeathFrame supplied)
    {
        var death=GetCurrentDeathFrame(supplied.Id);
        if(!_players[death.VictimSeat].HasShenPangTongKunyu||death.ShenPangTongOutcomeDeferred||death.OwnerDiedProgramsResolved)return false;
        UpdateDeathFrame(death.Id,d=>d with{ShenPangTongOutcomeDeferred=true});
        if(TryBeginOwnerDiedProgramWindow(GetCurrentDeathFrame(death.Id),_players[death.VictimSeat]))return true;
        UpdateDeathFrame(death.Id,d=>d with{ShenPangTongOutcomeDeferred=false});return false;
    }
    private void CompleteShenPangTongKunyuDeath(DeathFrame supplied)
    {
        var death=GetCurrentDeathFrame(supplied.Id);
        if(!death.ShenPangTongOutcomeDeferred||!death.OwnerDiedProgramsResolved||_resolutionStack.LastOrDefault()?.Id!=death.Id)
            throw new InvalidOperationException("Kunyu must return through its original retired owner-death window.");
        UpdateDeathFrame(death.Id,d=>d with{ShenPangTongOutcomeDeferred=false});
        if(death.ShenPangTongRevival is not{} r)return;
        RequireShenPtRevival(death,r);
        var p=_players[death.VictimSeat];var faceDown=p.IsFaceDown;
        p.IsAlive=true;p.MaxHp=1;p.Hp=1;p.IsFaceDown=false;p.AlcoholDamageBonus=0;p.UsedPlayPhaseAlcoholThisTurn=false;
        if(p.IsChained){p.IsChained=false;AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(death.Id,p.Seat,p.Seat,false));}
        AdvanceEventRulesAndQueueFact(new ShenPangTongRevivedEvent(death.Id,r.ProgramFrameId,p.Seat,p.Hp));
        if(faceDown)RecordCharacterStateChange(p.Seat,SkillProgramTriggerWindow.CharacterTurnedFaceUp);
        RecordCharacterTurnedOver(p.Seat,faceDown);
    }
}
