namespace CardGame.Core;

public sealed partial class GameEngine
{
    private NativeDrawPileSlashProof? NativeDrawPileSlashReplacementProof(ProgramSkillFrame frame, NativeDrawAttemptContext context) =>
        GetRuntimeModule<MoZhangFeiRuntime>().GreenReplacementProof(frame, context);
    private bool IsIssuedNativeDrawPileSlashReplacement(NativeDrawAttemptContext context, NativeDrawReplacementResult replacement) =>
        GetRuntimeModule<MoZhangFeiRuntime>().IsIssuedGreenReplacement(context, replacement);

    // Native adapters contain only fact projection and single primitive calls.
    // Qualification, branching, payment and state progression live in the module.
    [ProgramCapability]
    private sealed class MoZhangFeiFacts(GameEngine engine) : IMoZhangFeiFacts
    {
        public bool Enabled => engine._contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMoZhangFeiZhuohun);
        public bool SetupComplete => engine._setupComplete;
        public Winner Winner => engine._winner;
        public int ActualTurn => engine._turnNumber;
        public int TurnOwner => engine._turnProgression.OwnerSeat;
        public int Round => engine._roundNumber;
        public int PlayerCount => engine._playerCount;
        public bool Alive(int seat) => engine.IsValidPlayerSeat(seat) && engine._players[seat].IsAlive;
        public int Hp(int seat) => engine._players[seat].Hp;
        public int MaximumHp(int seat) => engine._players[seat].MaxHp;
        public int HandCount(int seat) => engine.GetHand(engine._players[seat]).Count;
        public IReadOnlyList<string> ZhuohunGrantIds(int seat) => Array.AsReadOnly(engine._players[seat].SkillGrants.Grants
            .Where(g => g.SkillId == MoZhangFeiRuntime.Zhuohun && !g.IsModeRule).Select(g => g.GrantId).ToArray());
        public bool AnyDying => engine._resolutionStack.OfType<DyingFrame>().Any();
        public long MovementSequence => engine._movementSequence;
        public IReadOnlyList<ProgramTriggerCandidate> Candidates(int seat, SkillProgramTriggerWindow window) =>
            engine.CollectProgramTriggerCandidates(engine._players[seat], window);
        public SkillProgramTrigger Trigger(ProgramTriggerCandidate c) => engine.GetProgramTrigger(c);
        public SkillProgram Program(string id) => engine._contentRegistry.GetSkill(id).Program!;
        public IEnumerable<T> History<T>() where T : IGameEvent => engine.ProgramEventHistory<T>();
        public CardKind? DamageKind(long windowId) => engine.ActiveDamageTrigger?.Id == windowId
            ? engine.CurrentDamageAttempt?.EffectiveCardKind ?? engine.CurrentDamageAttempt?.Card?.Kind : null;
        public IReadOnlyList<Card> DrawPileSlashes(int maximum) => Array.AsReadOnly(engine._cardZones.CardsAt(CardLocation.DrawPile)
            .Where(c => c.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash).Take(maximum).ToArray());
    }
    [ProgramCapability]
    private sealed class MoZhangFeiOperations(GameEngine engine) : IMoZhangFeiOperations
    {
        public void Finish(ProgramSkillFrame f) => engine.FinishProgramSkill(f, true);
        public void Recover(ProgramSkillFrame f, int amount) => new ProgramSkillHost(engine).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, amount, null, null);
        public void LoseHp(ProgramSkillFrame f, int seat, int amount) => new ProgramSkillHost(engine).LoseHp(f.Id, f.SkillId, seat, amount);
        public void Suppress(ProgramSkillFrame f, int seat) => engine.IssueCurrentTurnNonLockedSkillSuppression(f, seat);
        public void ObserveSkillRevisions()
        {
            foreach (var player in engine._players)
                engine._observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        }
        public bool RemoveSkillGrant(int owner, string id) => engine._players[owner].SkillGrants.RemoveGrant(id);
        public void GrantModifiedZhuohun(ProgramSkillFrame f) => engine.GrantProgramSkills(f, [MoZhangFeiRuntime.ModifiedZhuohun]);
        public void EnterRumo(ProgramSkillFrame f) => engine.EnterOlMoRumo(f);
        public bool BeginRecovery(long id) => engine.TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program);
        public bool BeginCharacterState(long id) => engine.TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program);
        public bool BeginHpChanged(long id) => engine.TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program);
        public bool BeginCardsMoved(long id) => engine.TryBeginCardsMovedProgramWindow(id);
        public bool BeginSkillsChanged(long id) => engine.TryBeginAdvancedSkillsChanged(id);
        public void GainSlash(ProgramSkillFrame f, Card card, int recipientSeat) => engine.MoveCard(card,
            CardLocation.DrawPile, CardLocation.Hand(recipientSeat), new(MoZhangFeiRuntime.GreenReason), m =>
                engine.AdvanceEventRulesAndQueueFact(new MoZhangFeiGreenMaterialEvent(f.Id, f.MoZhangFei!.DrawAttemptId!.Value,
                    m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        public void CompleteDrawPileReplacement(ProgramSkillFrame f, IReadOnlyList<int> ids, CardMoveReason reason, long before) =>
            engine.NativeDrawAttempts.CompleteDrawPileSlashReplacement(f, ids, reason, before);
    }
}
