namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouLuSuAnniversaryRuntime MouLuSuAnniversary => GetRuntimeModule<MouLuSuAnniversaryRuntime>();
    private bool HasMouLuSuAnniversary => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuSuMengmou);
    [ProgramCapability] private sealed class MouLuSuAnniversaryFacts(GameEngine e) : IMouLuSuAnniversaryFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int seat) => e.IsValidPlayerSeat(seat) && e._players[seat].IsAlive;
        public bool Human(int seat) => e._players[seat].IsHuman; public string Name(int seat) => e._players[seat].Name;
        public int MaxHp(int seat) => e._players[seat].MaxHp; public int HandCount(int seat) => e.GetHand(e._players[seat]).Count;
        public bool Friendly(int owner,int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),other,new(0,0,0,1,0,0,false,false)) > 0;
        public IReadOnlyList<int> Living() => Array.AsReadOnly(e._players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray());
        public IReadOnlyList<CardSnapshot> Hand(int seat) => Array.AsReadOnly(e.GetHand(e._players[seat]).Select(e.ToSnapshot).ToArray());
        public bool InHand(int seat,int id) => e._cardZones.GetLocation(id) == CardLocation.Hand(seat);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool Yin(int owner,string instance) => e.GetProgramBooleanState(owner,MouLuSuAnniversaryRuntime.Mengmou,instance,"yin");
        public ActualDiscardRecoveryPhaseKey? Phase() => e.CurrentActualDiscardRecoveryPhase();
        public bool Parent(ProgramSkillFrame f)
        {
            var i = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (i < 1 || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat || e._resolutionStack[i - 1].Id != w.ParentFrameId) return false;
            return e._resolutionStack[i - 1] switch {
                CardsMovedTriggerWindowFrame p => p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]),
                DamageTriggerWindowFrame p => e.DamageProgramMatchesWindow(f,p),
                ProgramLifecycleTriggerWindowFrame p => p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]), _ => false };
        }
        public int? Peer(ProgramTriggerCandidate c,ProgramSkillWindowContext w) => e.MouLuSuGainPeer(c,w);
        public (int Actor,int Amount,long UseId)? Damage(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
        {
            if (w.Window != SkillProgramTriggerWindow.AfterDamageApplied || w.Amount <= 0 || e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not DamageTriggerWindowFrame p ||
                p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || p.Candidates[p.CandidateIndex].ToProgramCandidate() != c || e._resolutionStack.SingleOrDefault(x => x.Id == p.ParentFrameId) is not DamageFrame damage || w.DamageFrameId != damage.Id || w.Amount != damage.Amount || w.SourceSeat != damage.SourceSeat || w.TargetSeat != damage.TargetSeat || e.LifecycleCardUse(damage.ParentFrameId) is not { MouLuSuAnniversary: { } o } use || o.Source.OwnerSeat != c.OwnerSeat || o.Source.SkillInstanceId != c.SkillInstanceId || o.Source.SkillId != c.SkillId || o.GameplayHash != c.GameplayHash || damage.SourceSeat != o.Actor || !e.MouLuSuNativeMatches(use)) return null;
            return (o.Actor,w.Amount,use.Id);
        }
        public IReadOnlyList<LegalAction> Uses(ProgramSkillFrame f) => e.MouLuSuUseActions(f);
        public IReadOnlyList<MouLuSuAnniversaryPlayedPlan> Plays(ProgramSkillFrame f) => e.MouLuSuPlayedPlans(f);
    }
    [ProgramCapability] private sealed class MouLuSuAnniversaryNative(GameEngine e) : IMouLuSuAnniversaryNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f,true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Yin(ProgramSkillFrame f,bool yin) { if (!e.MouLuSuAnniversary.Valid(f) || f.SkillId != MouLuSuAnniversaryRuntime.Mengmou) throw new InvalidOperationException("Mengmou state requires its accepted native branch."); e.SetProgramBooleanState(f.OwnerSeat,f.SkillId,f.SkillInstanceId,"yin",yin); }
        public void Reveal(ProgramSkillFrame f)
        {
            if (!e.MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.Recipient } r || r.Cards.Any(c => e._cardZones.GetLocation(c) != CardLocation.Hand(f.OwnerSeat))) throw new InvalidOperationException("Mingshi requires its actual selected hand entities to reveal.");
            e.AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,Array.AsReadOnly(r.Cards.Select(id => e.ToSnapshot(e.GetAdvancedCard(id))).ToArray())));
            foreach (var id in r.Cards) e.AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryShownEvent(f.Id,id));
            e.TryBeginPublicHandRevealWindow(f.Id,"mou-lu-su-mingshi-three");
        }
        public void Gift(ProgramSkillFrame f,int card)
        {
            if (!e.MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.GiftChildren } r || !r.Cards.Contains(card) || !e._players[r.Other].IsAlive || e._cardZones.GetLocation(card) != CardLocation.Hand(f.OwnerSeat)) throw new InvalidOperationException("Mingshi requires the recipient's exact publicly shown hand entity.");
            e.MoveProgramCardsFromMultipleSources([card],CardLocation.Hand(r.Other),new("skill-program.mou:mingshi.give"),(_,moves) => {
                var parent = e.GetActiveProgramFrame(f.Id); if (moves is not [var m] || m.CardId != card || m.From != CardLocation.Hand(f.OwnerSeat) || m.To != CardLocation.Hand(r.Other)) throw new InvalidOperationException("Mingshi gift changed its physical owner or recipient.");
                e.ReplaceRuntimeFrame(f.Id,parent with { MouLuSuAnniversary = parent.MouLuSuAnniversary! with { After = m.Sequence } }); e.AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryGiftEvent(f.Id,r.Other,card,m.Sequence)); },disclosure:CardMovementDisclosure.FaceUp);
        }
        public void Use(ProgramSkillFrame f) => e.IssueMouLuSuUse(f);
        public void Play(ProgramSkillFrame f) => e.IssueMouLuSuPlayed(f);
        public void CompletePlayed(ProgramSkillFrame f) => e.BeginMouLuSuPlayedCompleted(f);
        public void LoseHp(ProgramSkillFrame f,int target) => new ProgramSkillHost(e).LoseHp(f.Id,f.SkillId,target,1);
        public void Recover(ProgramSkillFrame f,int target) => new ProgramSkillHost(e).Recover(f.Id,f.OwnerSeat,target,1,null,null);
    }
    [PublicHandRevealReturn("mou-lu-su-mingshi-three")] private void ReturnMouLuSuPublicReveal(ProgramSkillFrame f) { if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary?.Stage != MouLuSuAnniversaryStage.Recipient) throw new InvalidOperationException("Mingshi lost its actual publicly shown roster."); MouLuSuAnniversary.Continue(f.Id); }
    private IEnumerable<int> MouLuSuPublicShownIds() => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.MouLuSuAnniversary is { Operation: SkillProgramEffectOp.ResolveMouLuSuMingshi, Stage: MouLuSuAnniversaryStage.Recipient or MouLuSuAnniversaryStage.Gift or MouLuSuAnniversaryStage.GiftChildren } && MouLuSuAnniversary.Valid(f)).SelectMany(f => f.MouLuSuAnniversary!.Cards.Where(c => _cardZones.GetLocation(c) == CardLocation.Hand(f.OwnerSeat)));
}
