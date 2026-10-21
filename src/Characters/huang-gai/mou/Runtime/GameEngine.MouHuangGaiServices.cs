namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class MouHuangGaiFacts(GameEngine e):IMouHuangGaiFacts
    {
        public int Turn=>e._turnNumber;public int TurnOwner=>e._turnProgression.OwnerSeat;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;public int LivingCount=>e._players.Count(p=>p.IsAlive);
        public bool Alive(int seat)=>e._players[seat].IsAlive;public bool Human(int seat)=>e._players[seat].IsHuman;public bool OwnPlay(int seat)=>seat==e._currentSeat&&e._phase==TurnPhase.Play;
        public bool LiejiParent(ProgramSkillFrame f)=>e.MouHgLiejiParent(f);public bool HasNativeUse(long id)=>e.LifecycleCardUse(id)is not null;
        public bool SameChoice(PromptChoice left,PromptChoice right)=>AssistedChoicesEqual([left],[right]);
        public string Name(int seat)=>e._players[seat].Name;public SkillProgram? Program(string id)=>e._contentRegistry.GetSkill(id).Program;
        public SkillPromptPresentation Presentation(string id){var s=e._contentRegistry.GetSkill(id);return new(s.Id,s.Name,s.Name,s.Description);}
        public double TargetScore(int owner,int target)=>e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),target,new(0,0,1,0,0,0,false,false));
        public IReadOnlyList<int> Seats()=>Array.AsReadOnly(e._players.Select(p=>p.Seat).ToArray());
        public IReadOnlyList<int> DamageHand(int owner)=>Array.AsReadOnly(e.GetHand(e._players[owner]).Where(c=>IsDamageCardKind(e.AdvancedEffectiveHandKind(e._players[owner],c))).Select(c=>c.Id).ToArray());
        public IReadOnlyList<int> Targets(ProgramSkillFrame f)=>e.MouHgTargets(f);
        public IReadOnlyList<int> ProcessingReveals(ProgramSkillFrame f)=>Array.AsReadOnly(f.MouHuangGai!.Reveals.Where(c=>e._cardZones.GetLocation(c.CardId)==CardLocation.Processing).Select(c=>c.CardId).ToArray());
        public IReadOnlyList<MouHgLiejiGrant> Grants(int owner)=>Array.AsReadOnly((e._players[owner].MouHgLiejiGrants??[]).ToArray());
        public MouHgUseFacts? Use(long id)=>e.LifecycleCardUse(id)is{Action:{Type:CardActionType.Use}a}u?
            new(u.Id,a.ActionId,Array.AsReadOnly(a.PhysicalCards.Select(c=>c.CardId).ToArray()),u.MouHgBonuses is{}b?Array.AsReadOnly(b.ToArray()):null):null;
        public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
    }
    [ProgramCapability]
    private sealed class MouHuangGaiNative(GameEngine e):IMouHuangGaiNative
    {
        public void SetGrants(int owner,IReadOnlyList<MouHgLiejiGrant>? grants)=>e._players[owner].MouHgLiejiGrants=grants is null?null:Array.AsReadOnly(grants.ToArray());
        public void SetUseBonuses(long useId,IReadOnlyList<MouHgUseBonus> bonuses)=>e.UpdateLifecycleCardUse(useId,u=>u with{MouHgBonuses=bonuses});
        public MouHgReveal? Reveal(ProgramSkillFrame f,string binding)
        {
            var before=f.MouHuangGai!.Before;e.RevealProgramTopCards(f.Id,f.OwnerSeat,1,null,binding,SkillProgramCardSetVisibility.Public);f=e.GetActiveProgramFrame(f.Id);
            var card=e.GetProgramCardSet(f,binding).CardIds.SingleOrDefault();if(card==0)return null;
            var actual=e._cardMovements.Last(m=>m.Sequence>before&&m.CardId==card&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing);return new(card,actual.CardKind,actual.Sequence);
        }
        public void Obtain(ProgramSkillFrame f,IReadOnlyList<int> ids)=>e.MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new(MouHuangGaiRuntime.ObtainReason),(_,moves)=>e.MouHuangGaiRules.MovesPaid(f.Id,moves),disclosure:CardMovementDisclosure.FaceUp);
        public void DiscardSlash(ProgramSkillFrame f)
        {
            var shown=f.MouHuangGai!.Reveals[^1];if(e._cardZones.GetLocation(shown.CardId)==CardLocation.Processing)
                e.MoveCard(e.GetAdvancedCard(shown.CardId),CardLocation.Processing,CardLocation.DiscardPile,new(MouHuangGaiRuntime.DiscardReason),m=>e.MouHuangGaiRules.MovesPaid(f.Id,[m]));
        }
        public void Slash(ProgramSkillFrame f,int target)=>e.IssueMouHgSlash(f,target);
        public bool Drain(long id)=>e.TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||e.TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||e.TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||e.TryBeginCardsMovedProgramWindow(id)||e.TryBeginAdvancedSkillsChanged(id);
        public void Publish()=>e.AdvanceRulesAndPublishState();public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
    }
}
