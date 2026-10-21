using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MoCaoCaoFuzaiRuntime MoCaoCaoEquipment => GetRuntimeModule<MoCaoCaoFuzaiRuntime>();
    private MoCaoCaoBachaoRuntime MoCaoCaoBachao => GetRuntimeModule<MoCaoCaoBachaoRuntime>();
    [ProgramPipelineHook(ProgramPipelinePhase.VirtualEquipment,33800)]
    private CardKind? MoCaoCaoVirtualEquipment(int seat, EquipmentSlot slot) => MoCaoCaoEquipment.VirtualKind(seat,slot);

    [ProgramCapability]
    private sealed class MoCaoCaoFacts(GameEngine engine) : IMoCaoCaoFacts
    {
        public bool Enabled => engine._contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeMoCaoCaoFuzai);
        public IReadOnlyList<int> Seats => engine.CurrentTableOrder();
        public MoCaoCaoParticipant Participant(int seat)
        {
            var p = engine._players[seat];
            return new(seat,p.IsAlive,p.IsHuman,p.Hp,engine.GetEquipment(p).Count,
                p.EquipmentSlotCapacity(EquipmentSlot.Weapon)>0,p.EquipmentSlotCapacity(EquipmentSlot.Armor)>0);
        }
        public MoCaoCaoBinding? FuzaiBinding(int seat)
        {
            if (!Enabled || !engine.IsValidPlayerSeat(seat) || !engine._players[seat].IsAlive) return null;
            var c = engine.CollectProgramTriggerCandidates(engine._players[seat],SkillProgramTriggerWindow.GameStarting)
                .FirstOrDefault(c => c.SkillId == MoCaoCaoComposition.Fuzai);
            return c is null ? null : new(new(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId),c.GameplayHash);
        }
        public SkillProgram? Program(string id) => engine._contentRegistry.Skills.TryGetValue(id,out var skill) ? skill.Program : null;
        public ActualDiscardRecoveryPhaseKey? Phase => engine.CurrentActualDiscardRecoveryPhase();
        public IReadOnlyList<int> Hand(int seat) => Array.AsReadOnly(engine.GetHand(engine._players[seat]).Select(c=>c.Id).ToArray());
        public CardLocation Location(int id) => engine._cardZones.GetLocation(id);
        public CardKind Kind(int id) => engine.GetAdvancedCard(id).Kind;
        public IEnumerable<T> History<T>() where T:IGameEvent => engine.ProgramEventHistory<T>();
        public bool Held(ProgramSkillFrame f) => engine.HasRuntimeSkillInstance(engine._players[f.OwnerSeat],f.SkillId,f.SkillInstanceId);
        public bool Finished => engine._winner != Winner.None;
        public long MovementSequence => engine._movementSequence;
        public bool IsExactStart(ProgramSkillFrame f, SkillProgramEffectOp op)
        {
            if (f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } w ||
                engine.GetPausedPrivateOfferEffect(f)?.Op != op || Program(f.SkillId)?.GameplayHash != f.GameplayHash) return false;
            var index = engine._resolutionStack.FindIndex(p=>p.Id==f.Id);
            if (index < 1 || engine._resolutionStack[index-1].Id != w.ParentFrameId) return false;
            if (op == SkillProgramEffectOp.ResolveMoCaoCaoBaibi)
                return engine._resolutionStack[index-1] is DamageTriggerWindowFrame damage && damage.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied &&
                    damage.CandidateIndex >= 0 && damage.CandidateIndex < damage.Candidates.Count &&
                    MountObserverCandidateMatches(f,damage.Candidates[damage.CandidateIndex].ToProgramCandidate());
            if (op == SkillProgramEffectOp.ResolveMoCaoCaoBachao)
                return w.Window==SkillProgramTriggerWindow.PlayPhaseStarting && w.OwnerSeat==f.OwnerSeat && w.SourceSeat==f.OwnerSeat && w.TargetSeat==f.OwnerSeat &&
                    engine._resolutionStack[index-1] is PlayPhaseStartingBoundaryFrame starting && starting.OwnerSeat==f.OwnerSeat &&
                    starting.ItemIndex>=0 && starting.ItemIndex<starting.Items.Count && starting.Items[starting.ItemIndex] is {Kind:TurnEndingBoundaryItemKind.Program,Candidate:{ } candidate} &&
                    MountObserverCandidateMatches(f,candidate);
            return op==SkillProgramEffectOp.InitializeMoCaoCaoFuzai && engine._resolutionStack[index-1] is ProgramLifecycleTriggerWindowFrame parent &&
                parent.Window==SkillProgramTriggerWindow.GameStarting && w.Window==parent.Window && parent.CandidateIndex>=0 && parent.CandidateIndex<parent.Candidates.Count &&
                MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]);
        }
        public int ResolveHandCard(ProgramSkillFrame f,int chooser,PromptChoice c)
        {
            if (c.Cards is [var id]) return id;
            var subject = f.MoCaoCaoBaibi?.Claim.TargetSeat ?? chooser;
            var zone = Enum.Parse<CardZoneKind>(c.Parameters["source-zone"],true);
            var slot = int.Parse(c.Parameters["slot-index"],CultureInfo.InvariantCulture);
            return engine._cardZones.CardsAt(new(zone,subject))[slot].Id;
        }
        public IReadOnlyList<PromptChoice> PaymentChoices(ProgramSkillFrame f,int chooser,int subject,bool handOnly) =>
            engine.BuildOwnedCardPaymentChoices(f.Id,chooser,subject,handOnly ? [CardZoneKind.Hand] : [CardZoneKind.Hand,CardZoneKind.Equipment],
                handOnly ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Transfer,
                destination:SkillProgramCardDestination.OwnerHand,destinationSeat:f.OwnerSeat);
        public MoCaoCaoDamageOrigin? DamageOrigin
        {
            get
            {
                if (engine.CurrentDamageAttempt is not { } a || a.EffectiveCardKind is not { } kind ||
                    engine.LifecycleCardUse(a.ResolutionId)?.Action is not { } action ||
                    engine._resolutionStack.OfType<DamageFrame>().LastOrDefault(d=>d.ParentFrameId==a.ResolutionId && d.SourceSeat==a.SourceSeat && d.TargetSeat==a.TargetSeat) is not { } damage) return null;
                return new(damage.Id,a.ResolutionId,action.ActionId,a.SourceSeat,a.TargetSeat,kind,a.DamageAmount,a.IsSourceLess);
            }
        }
        public MoCaoCaoWeaponAuthorization? WeaponAuthorization(int seat,CardKind kind)
        {
            if (!engine.HasWeaponAbility(engine._players[seat],kind)) return null;
            var physical=engine.GetEquipment(engine._players[seat]).FirstOrDefault(c=>!c.IsGeneralWeapon && engine.GetWeaponAbilityKinds(c).Contains(kind));
            if (physical is not null) return new(physical.Id,0,$"equipment:{physical.Id}:{kind}");
            if (engine.MoCaoCaoEquipment.VirtualKind(seat,EquipmentSlot.Weapon)==kind && engine.MoCaoCaoEquipment.LastIssue(seat) is { } issue)
                return new(0,issue.IssueId,$"fuzai:{issue.IssueId}:{kind}");
            return (engine._inheritedWeaponAbilities.GetValueOrDefault(seat)??[]).Contains(kind) ? new(0,0,$"inherited:{seat}:{kind}") : null;
        }
        public bool ExactIssuedCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
        {
            if (engine._resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not { } window ||
                !window.Candidates.Any(c=>c.ToProgramCandidate()==candidate)) return false;
            return (engine.CreateAfterDamageProgramContext(window,candidate) with { Facts=null }) == (context with { Facts=null });
        }
        public bool NativeChild(ProgramSkillFrame f,ResolutionFrame child) => engine.MoCaoCaoFirstNativeChild(f,child);
        public bool Friendly(int chooser,int target) => engine._aiBrains[chooser].ScoreProgramTarget(engine.CreateSnapshot(chooser),target,new SkillProgramAiHint(0,0,0,1,0,0,false,false))>0;
        public int KeepValue(int chooser,int card) => GetKeepValue(engine.GetAdvancedCard(card),engine._players[chooser]);
    }
    [ProgramCapability]
    private sealed class MoCaoCaoOperations(GameEngine engine) : IMoCaoCaoOperations
    {
        public int NextRandom(int count) => engine._random.Next(count);
        public void PublishState() => engine.AdvanceRulesAndPublishState();
        public void LogEquipment(int seat,CardKind weapon,CardKind armor) => engine.AddLog("Fuzai",$"{engine._players[seat].Name} 的覆载装备为【{EquipmentCatalog.Get(weapon).DisplayName}】、【{EquipmentCatalog.Get(armor).DisplayName}】。",seat);
        public void Finish(ProgramSkillFrame f) => engine.FinishProgramSkill(f,true);
        public bool Drain(ProgramSkillFrame f) => engine.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
            engine.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) || engine.TryBeginAdvancedSkillsChanged(f.Id) ||
            engine.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) || engine.TryBeginCardsMovedProgramWindow(f.Id);
        public MoCaoCaoMovement Transfer(ProgramSkillFrame f,int payer,int id,string reason)
        {
            var before=engine._movementSequence; var from=engine._cardZones.GetLocation(id);
            if (from.OwnerSeat!=payer || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) throw new InvalidOperationException("Bachao/Baibi lost the actual HE payment.");
            engine.MoveCard(engine.GetAdvancedCard(id),from,CardLocation.Hand(f.OwnerSeat),new CardMoveReason(reason),m=>
            {
                if(engine._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p=>p.Id==f.Id) is not { } parent ||
                    !engine.MoCaoCaoBachao.Valid(parent) || m.CardId!=id || m.From!=from || m.Reason.Value!=reason || m.Sequence<=before)
                    throw new InvalidOperationException("Magic Cao Cao payment lost its owning ancestor or actual movement.");
                engine.ReplaceRuntimeFrame(parent.Id,parent.MoCaoCaoBachao is { } r ? parent with { MoCaoCaoBachao=r with { After=m.Sequence } } :
                    parent with { MoCaoCaoBaibi=parent.MoCaoCaoBaibi! with { After=m.Sequence } });
                if (parent.MoCaoCaoBachao is not null) engine.RecordNativeGivenCards(f.Id,payer,f.OwnerSeat,[id],before);
            },disclosure:from.Zone==CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
            var after=engine._movementSequence;
            return new(before,after,engine._cardMovements.Any(m=>m.CardId==id && m.From==from && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==reason && m.Sequence>before && m.Sequence<=after));
        }
        public void Damage(ProgramSkillFrame f,int target) => engine.BeginProgramSkillDamage(f,target,1);
        public void EnterRumo(ProgramSkillFrame f) => engine.EnterOlMoRumo(f);
        public bool AdvanceObserverChildren() => engine.TryAdvanceObserverSubtree(true);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate,33800)]
    private IReadOnlyList<ProgramTriggerCandidate> IncludeMoCaoCaoBaibi(CharacterState owner,SkillProgramTriggerWindow window,IReadOnlyList<ProgramTriggerCandidate> candidates) =>
        MoCaoCaoBachao.IncludeBaibi(owner.Seat,window,candidates);
}
