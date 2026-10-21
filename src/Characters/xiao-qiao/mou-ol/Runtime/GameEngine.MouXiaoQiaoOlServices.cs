namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MouXiaoQiaoOlRuntime MouXiaoQiaoOl => GetRuntimeModule<MouXiaoQiaoOlRuntime>();
    private bool TryBeginPublicHandRevealWindow(long parentId, PublicHandRevealReturnKind returned) => MouXiaoQiaoOl.BeginPublicReveal(parentId, returned);
    private bool TryBeginPublicHandRevealWindow(long parentId, string key) => MouXiaoQiaoOl.BeginPublicReveal(parentId, PublicHandRevealReturnKind.Registered, key);
    private bool IsPublicHandRevealProducerSuspended(long parentId,string key)=>MouXiaoQiaoOl.RevealProducerSuspended(parentId,key);
    [ProgramCapability]
    private sealed class MouXiaoQiaoOlFacts(GameEngine e) : IMouXiaoQiaoOlFacts
    {
        public bool Enabled => e._contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal);
        public bool SetupComplete => e._setupComplete;
        public int Round => e._roundNumber;
        public int Turn => e._turnNumber;
        public int CurrentSeat => e._currentSeat;
        public TurnPhase Phase => e._phase;
        public Winner Winner => e._winner;
        public int PlayerCount => e._playerCount;
        public long MovementSequence => e._movementSequence;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman;
        public string Name(int seat) => e._players[seat].Name;
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public bool HasSource(CardConversionSource s) => e.HasRuntimeSkillInstance(e._players[s.OwnerSeat], s.SkillId, s.SkillInstanceId!);
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<Card> Hand(int seat) => e.GetHand(e._players[seat]);
        public IReadOnlyList<Card> Equipment(int seat) => e.GetEquipment(e._players[seat]);
        public Card Card(int id) => e.GetAdvancedCard(id);
        public CardLocation Location(int id) => e._cardZones.GetLocation(id);
        public CardKind EffectiveKind(int seat, int id) => e.AdvancedEffectiveHandKind(e._players[seat], e.GetAdvancedCard(id));
        public Suit EffectiveSuit(int seat, int id) => e.EffectiveSuit(e._players[seat], e.GetAdvancedCard(id));
        public bool? EffectiveRed(int seat, int id) => SuitColor(e.EffectiveSuit(e._players[seat], e.GetAdvancedCard(id)));
        public IReadOnlyList<CardMovementRecord> Movements(long before, long after) => Array.AsReadOnly(e._cardMovements.Where(m => m.Sequence > before && m.Sequence <= after).ToArray());
        public IReadOnlyList<MouXiaoQiaoOlUsePlan> NativePlans(int seat, CardKind kind, CardConversionSource source) => e.MouXqNativePlans(e._players[seat], kind, source);
        public IReadOnlyList<ProgramTriggerCandidate> Sources(int seat, string skillId) => Array.AsReadOnly(e.CollectProgramTriggerCandidates(e._players[seat], SkillProgramTriggerWindow.PublicHandRevealed).Where(c => c.SkillId == skillId).ToArray());
        public SkillPromptPresentation SkillPrompt(string id)
        { var s = e._contentRegistry.GetSkill(id); return new(id, s.Name, s.Name, s.Description); }
        public double KeepValue(int seat, int id) => GetKeepValue(e.GetAdvancedCard(id), e._players[seat]);
        public double TargetValue(int owner, int target, bool helpful) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), target,
            helpful ? new(0, 0, 0, 0, 1, 0, true, false) : new(0, 0, 1, 0, 0, 0, false, false));
        public (long FrameId, IReadOnlyList<int> CardIds) PublicReveal(IGameEvent p) => p switch
        {
            CardsRevealedEvent x => (x.ResolutionId, x.Cards.Select(c => c.Id).ToArray()),
            ProgramCardsRevealedEvent x => (x.FrameId, x.Cards.Select(c => c.Id).ToArray()),
            FireAttackCardRevealedEvent x => (x.ResolutionId, [x.CardId]),
            ProgramHandComparisonCardRevealedEvent x => (x.FrameId, [x.CardId]),
            ProgramHandComparisonSourceRevealedEvent x => (x.FrameId, [x.CardId]),
            ProgramHandDraftRevealedEvent x => (x.FrameId, e.MiuyanDraftHandIds(x).ToArray()),
            ProgramQingliangRevealEvent x => (x.FrameId, e.MiuyanQingliangHandIds(x).ToArray()),
            _ => (0, Array.Empty<int>())
        };
        public bool ActorLineage(long useId,long actionId,int originalActor,int actor,int provider,CardActionType type)=>e.MouGuquActorLineageMatches(useId,actionId,originalActor,actor,provider,type);
        public bool IsResponseRequest(ProgramSkillFrame f) => e.IsMouXqJueyanResponseRequest(f);
        public bool GiftRecoveryChild(ProgramSkillFrame f,ResolutionFrame child)
        {
            if(f.MouXiaoQiaoOl is not{Stage:MouXiaoQiaoOlStage.GiftChildren,Paid:true}r||
                e.ProgramEventHistory<MouXiaoQiaoMiluoGiftEvent>().LastOrDefault(x=>x.FrameId==f.Id&&x.Kind==CardKind.SilverLion&&x.From==CardLocation.Equipment(f.OwnerSeat))is not{}paid||
                !e._cardMovements.Any(m=>m.Sequence==paid.Sequence&&m.CardId==paid.CardId&&m.CardKind==CardKind.SilverLion&&m.From==paid.From&&m.To==paid.To&&m.Reason.Value==MouXiaoQiaoOlRuntime.GiveReason))return false;
            if(child is RecoveryReplacementFrame recovery)return e.RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.Program&&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==MouXiaoQiaoOlRuntime.GiveReason&&
                recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat;
            return child is HpChangedTriggerWindowFrame hp&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&
                hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount>0&&hp.Change.SourceSeat==f.OwnerSeat&&hp.Change.TargetSeat==f.OwnerSeat&&
                e.ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(x=>x.ResolutionId>f.Id&&x.ResolutionId<hp.Change.Id&&x.PlayerSeat==f.OwnerSeat&&x.Reason.Value==MouXiaoQiaoOlRuntime.GiveReason&&x.RecoveredAmount==hp.Change.Amount&&x.RemainingHp==hp.Change.HpAfter);
        }
    }
    [ProgramCapability]
    private sealed class MouXiaoQiaoOlOperations(GameEngine e) : IMouXiaoQiaoOlOperations
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public void Reveal(ProgramSkillFrame f, IReadOnlyList<int> ids) => e.AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId ?? f.ActivationId,
            f.OwnerSeat, "mou-xiao-qiao-public-hand", Array.AsReadOnly(ids.Select(id => e.ProjectHandCard(e._players[f.OwnerSeat], e.GetAdvancedCard(id))).ToArray())));
        public bool BeginRevealChildren(long id) => e.TryBeginPublicHandRevealWindow(id, PublicHandRevealReturnKind.Program);
        public void Gift(ProgramSkillFrame f, int id, int target) => e.MoveCard(e.GetAdvancedCard(id), e._cardZones.GetLocation(id),
            CardLocation.Hand(target), new(MouXiaoQiaoOlRuntime.GiveReason), disclosure: CardMovementDisclosure.FaceUp);
        public void Recover(ProgramSkillFrame f, int target) => new ProgramSkillHost(e).Recover(f.Id, f.OwnerSeat, target, 1, null, null);
        public void LoseHp(ProgramSkillFrame f, int target) => new ProgramSkillHost(e).LoseHp(f.Id, f.SkillId, target, 1);
        public bool BeginRecovery(long id) => e.TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program);
        public bool BeginCharacterState(long id) => e.TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program);
        public bool BeginHpChanged(long id) => e.TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program);
        public bool BeginCardsMoved(long id) => e.TryBeginCardsMovedProgramWindow(id);
        public bool BeginSkillsChanged(long id) => e.TryBeginAdvancedSkillsChanged(id);
        public void IssueNativeUse(ProgramSkillFrame f, MouXiaoQiaoOlUsePlan p) => e.IssueMouXqNativeUse(f, p);
        public void SignNativeBatches(ProgramSkillFrame f,CardUseFrame use)=>e.SignMouXqNativeBatches(f,use);
        public bool BeginResponse(PromptChoice choice) => e.BeginMouXqJueyanResponse(choice);
        public void ReturnResponse(ProgramSkillFrame f) => e.ReturnMouXqJueyanResponse(f);
        public void BeginRevealTrigger(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => e.BeginProgramBinding(c, context);
        public void ReturnRevealWindow(PublicHandRevealWindowFrame w)
        {
            if (w.ReturnKind == PublicHandRevealReturnKind.Program)
            { if (!ProgramContinuations.Value.TryResume(e, w.ParentFrameId)) e.AdvanceRuntimeProgram(w.ParentFrameId); }
            else if (w.ReturnKind == PublicHandRevealReturnKind.FireAttack)
            {
                var use = e.LifecycleCardUse(w.ParentFrameId) ?? throw new InvalidOperationException("Public reveal lost its actual FireAttack.");
                var id = use.FireAttackSelection?.RevealedCardId ?? throw new InvalidOperationException("FireAttack cannot replace its frozen revealed entity.");
                e.BeginFireAttackDiscard(use, e.GetAdvancedCard(id));
            }
            else if (w.ReturnKind == PublicHandRevealReturnKind.Registered)
                PublicHandRevealReturns.Default.Return(e, w.ReturnKey!, e._resolutionStack.Single(x => x.Id == w.ParentFrameId));
            else e.AdvanceRuntimeFrame(w.ParentFrameId);
        }
    }
}
