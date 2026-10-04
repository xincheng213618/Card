namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This interface joins the existing card attack to the damage pipeline. A
    // program handle stores only an owner ID; all of its mutable data lives in
    // ProgramSkillFrame.AttackAttempt.
    private interface IDamageAttempt
    {
        long ResolutionId { get; }
        int SourceSeat { get; }
        int CardUserSeat { get; }
        int TargetSeat { get; }
        Card? Card { get; }
        IReadOnlyList<Card> PhysicalCards { get; }
        CardKind? EffectiveCardKind { get; }
        CardConversionSource? ConversionSource { get; }
        int DamageAmount { get; }
        bool DamageAmountFinalized { get; }
        bool IgnoresArmor { get; }
        bool IsDelayedJudgmentDamage { get; }
        bool IsProgramJudgmentDamage { get; }
        bool IsSourceLess { get; }
        DamageNature? DamageNatureOverride { get; }
        bool IsChainPropagation { get; }
        bool DamageWasApplied { get; }
        bool BeforeDamageProgramsResolved { get; }
        bool DamageRedirected { get; }
        bool PendingRedBladeDamageBonus { get; set; }
        int SourceToTargetDistanceAtDamage { get; }
        void SetIgnoresArmor(bool value);
        void FinalizeDamageAmount(int bonus, int? maximum = null);
        void ReduceFinalizedDamageAmount(int amount);
        void IncreaseFinalizedDamageAmount(int amount);
        void MarkBeforeDamageProgramsResolved();
        void MarkDamageApplied();
        void CaptureDamageDistance(int distance);
        void MarkDyingResolvedForDamage(long damageFrameId);
        bool HasResolvedDyingForDamage(long damageFrameId);
        void RedirectFinalizedDamageTarget(int targetSeat, ProgramDamageTransferFollowup followup);
        bool TryConsumeProgramDamageTransferFollowup(out ProgramDamageTransferFollowup followup);
        void SetChainedTargets(IReadOnlyList<int> targetSeats);
        bool TryAdvanceChainedTarget(Func<int, bool> isAlive, bool resetDamageForTargetModifiers,
            out int fromSeat);
    }

    private sealed class ProgramAttackHandle(GameEngine engine, long ownerFrameId) : IDamageAttempt
    {
        private AttackAttemptState State => engine.GetProgramAttackState(ownerFrameId);
        private void Update(Func<AttackAttemptState, AttackAttemptState> update) =>
            engine.UpdateProgramAttackState(ownerFrameId, update);

        public long ResolutionId => ownerFrameId;
        public int SourceSeat => State.SourceSeat;
        public int CardUserSeat => State.SourceSeat;
        public int TargetSeat => State.TargetSeat;
        public Card? Card => null;
        public IReadOnlyList<Card> PhysicalCards => [];
        public CardKind? EffectiveCardKind => null;
        public CardConversionSource? ConversionSource => null;
        public int DamageAmount => State.DamageAmount;
        public bool DamageAmountFinalized => State.DamageAmountFinalized;
        public bool IgnoresArmor => State.IgnoresArmor;
        public bool IsDelayedJudgmentDamage => false;
        public bool IsProgramJudgmentDamage => State.ProgramJudgmentWindowId is not null;
        public bool IsSourceLess => State.SourceLess;
        public DamageNature? DamageNatureOverride => State.Nature;
        public bool IsChainPropagation => State.IsChainPropagation;
        public bool DamageWasApplied => State.DamageWasApplied;
        public bool BeforeDamageProgramsResolved => State.BeforeDamageProgramsResolved;
        public bool DamageRedirected => State.DamageRedirected;
        public int SourceToTargetDistanceAtDamage => State.SourceToTargetDistanceAtDamage;
        public bool PendingRedBladeDamageBonus
        {
            get => false;
            set
            {
                if (value) throw new InvalidOperationException("Cardless damage cannot use Red Blood Blade.");
            }
        }

        public void SetIgnoresArmor(bool value) => Update(state => state with { IgnoresArmor = value });
        public void FinalizeDamageAmount(int bonus, int? maximum = null) => Update(state =>
        {
            if (state.DamageAmountFinalized)
                throw new InvalidOperationException("The attack damage amount is already final.");
            var amount = checked(state.DamageAmount + bonus);
            return state with
            {
                DamageAmount = maximum is { } cap ? Math.Min(amount, cap) : amount,
                DamageAmountFinalized = true
            };
        });
        public void ReduceFinalizedDamageAmount(int amount) => Update(state =>
        {
            if (!state.DamageAmountFinalized || amount <= 0 || amount > state.DamageAmount)
                throw new InvalidOperationException("Damage reduction requires a positive part of the frozen amount.");
            return state with { DamageAmount = state.DamageAmount - amount };
        });
        public void IncreaseFinalizedDamageAmount(int amount) => Update(state =>
        {
            if (!state.DamageAmountFinalized || state.DamageWasApplied || amount <= 0)
                throw new InvalidOperationException("Damage increase requires a positive amount before frozen damage is applied.");
            return state with { DamageAmount = checked(state.DamageAmount + amount) };
        });
        public void MarkBeforeDamageProgramsResolved() => Update(state =>
            state with { BeforeDamageProgramsResolved = true });
        public void MarkDamageApplied() => Update(state => state with { DamageWasApplied = true });
        public void CaptureDamageDistance(int distance) => Update(state =>
            state with { SourceToTargetDistanceAtDamage = distance });
        public void MarkDyingResolvedForDamage(long damageFrameId) => Update(state =>
            state with { ResolvedDyingDamageFrameId = damageFrameId });
        public bool HasResolvedDyingForDamage(long damageFrameId) =>
            State.ResolvedDyingDamageFrameId == damageFrameId;
        public void RedirectFinalizedDamageTarget(int targetSeat, ProgramDamageTransferFollowup followup) =>
            Update(state =>
            {
                if (!state.DamageAmountFinalized || state.DamageRedirected || state.TargetSeat == targetSeat)
                    throw new InvalidOperationException("Damage redirection requires a new recipient and a finalized amount.");
                return state with
                {
                    TargetSeat = targetSeat,
                    DamageRedirected = true,
                    TransferSkillId = followup.SkillId,
                    TransferOwnerSeat = followup.OwnerSeat,
                    TransferTargetSeat = followup.TargetSeat,
                    TransferDrawLostHp = followup.DrawLostHp
                };
            });
        public bool TryConsumeProgramDamageTransferFollowup(out ProgramDamageTransferFollowup followup)
        {
            var state = State;
            if (state.TransferSkillId is null || state.TransferOwnerSeat is null || state.TransferTargetSeat is null)
            {
                followup = null!;
                return false;
            }
            followup = new(state.TransferSkillId, state.TransferOwnerSeat.Value,
                state.TransferTargetSeat.Value, state.TransferDrawLostHp);
            Update(current => current with
            {
                TransferSkillId = null,
                TransferOwnerSeat = null,
                TransferTargetSeat = null,
                TransferDrawLostHp = false
            });
            return true;
        }
        public void SetChainedTargets(IReadOnlyList<int> targetSeats) => Update(state =>
        {
            if (state.ChainedTargetSeats is { Count: > 0 } || state.ChainedTargetIndex != 0)
                throw new InvalidOperationException("The elemental chain targets have already been captured.");
            return state with { ChainedTargetSeats = Array.AsReadOnly(targetSeats.ToArray()) };
        });
        public bool TryAdvanceChainedTarget(Func<int, bool> isAlive, bool resetDamageForTargetModifiers,
            out int fromSeat)
        {
            var state = State;
            fromSeat = state.TargetSeat;
            var targets = state.ChainedTargetSeats ?? [];
            var cursor = state.ChainedTargetIndex;
            while (cursor < targets.Count)
            {
                var next = targets[cursor++];
                if (!isAlive(next)) continue;
                Update(current => current with
                {
                    TargetSeat = next,
                    ChainedTargetIndex = cursor,
                    IsChainPropagation = true,
                    DamageAmountFinalized = resetDamageForTargetModifiers ? false : current.DamageAmountFinalized
                });
                _engine.RestoreRecipientScopedDamageBase(this, fromSeat);
                return true;
            }
            Update(current => current with { ChainedTargetIndex = cursor });
            return false;
        }
    }

    private AttackAttemptState GetProgramAttackState(long ownerFrameId) =>
        _resolutionStack.OfType<ProgramSkillFrame>()
            .SingleOrDefault(frame => frame.Id == ownerFrameId)?.AttackAttempt ??
        throw new InvalidOperationException($"Program damage owner {ownerFrameId} lost its attempt.");

    private void UpdateProgramAttackState(long ownerFrameId,
        Func<AttackAttemptState, AttackAttemptState> update)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame is ProgramSkillFrame program &&
            program.Id == ownerFrameId);
        if (index < 0 || _resolutionStack[index] is not ProgramSkillFrame program ||
            program.AttackAttempt is null)
            throw new InvalidOperationException($"Program damage owner {ownerFrameId} lost its attempt.");
        ReplaceRuntimeFrame(_resolutionStack[index].Id, program with { AttackAttempt = update(program.AttackAttempt) });
    }

    private IDamageAttempt? CurrentDamageAttempt
    {
        get
        {
            var programIndex = _resolutionStack.FindLastIndex(frame => frame is ProgramSkillFrame
                { AttackAttempt: not null });
            var cardIndex = ActiveCardAttack is { } card
                ? _resolutionStack.FindLastIndex(frame => frame.Id == card.ResolutionId)
                : -1;
            return programIndex > cardIndex && _resolutionStack[programIndex] is ProgramSkillFrame program
                ? new ProgramAttackHandle(this, program.Id)
                : ActiveCardAttack;
        }
    }

    private bool IsCardAttackSuspendedByProgramDamage =>
        ActiveCardAttack is not null && CurrentDamageAttempt is ProgramAttackHandle;
    private void CompleteProgramAttack(IDamageAttempt attack)
    {
        if (ActiveDying is not null) throw new InvalidOperationException("Program damage retained a dying child.");
        if (attack.TryConsumeProgramDamageTransferFollowup(out var transfer))
        {
            var target = _players[transfer.TargetSeat];
            var count = transfer.DrawLostHp && attack.DamageWasApplied && target.IsAlive
                ? Math.Max(0, target.MaxHp - target.Hp) : 0;
            if (count > 0) DrawCards(target, count, log: true,
                reason: new CardMoveReason("skill-program.damage-transfer.followup-draw"));
            AdvanceEventRulesAndQueueFact(new ProgramDamageTransferCardsDrawnEvent(
                attack.ResolutionId, transfer.SkillId, transfer.OwnerSeat, transfer.TargetSeat, count));
        }
        if (_winner == Winner.None && attack.TryAdvanceChainedTarget(seat => _players[seat].IsAlive,
                UsesFormalTengjia || UsesFormalSilverLion, out var fromSeat))
        {
            AdvanceEventRulesAndQueueFact(new ChainedDamagePropagatedEvent(attack.ResolutionId,
                attack.SourceSeat, fromSeat, attack.TargetSeat, attack.DamageAmount, GetDamageNature(attack)));
            AddLog("ChainDamage", $"连环伤害从 {_players[fromSeat].Name} 传导至 {_players[attack.TargetSeat].Name}。",
                attack.SourceSeat, attack.TargetSeat);
            if (!ApplyAttackDamage(attack)) CompleteProgramAttack(attack);
            return;
        }
        var id = attack.ResolutionId;
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == id);
        if (index != _resolutionStack.Count - 1 || _resolutionStack[index] is not ProgramSkillFrame program)
            throw new InvalidOperationException("Program damage lost its owner continuation.");
        var receipt = program.AttackReturn;
        CaptureSuitPreventionDamageCompleted(program, attack.DamageWasApplied);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, program with { AttackAttempt = null, AttackReturn = null });
        if (receipt?.ParentAttackOwnerFrameId is { } parentId && CurrentDamageAttempt?.ResolutionId != parentId)
            throw new InvalidOperationException("Program damage changed its suspended parent owner.");
        if (receipt?.ParentDamageWindowFrameId is { } windowId && ActiveDamageTrigger?.Id != windowId)
            throw new InvalidOperationException("Program damage lost its parent damage occurrence.");
        _pendingDecision = null;
        AdvanceRuntimeProgram(id);
    }

    private bool HasDamageTriggerForAttack(IDamageAttempt attack) =>
        _resolutionStack.OfType<DamageTriggerWindowFrame>().Any(window =>
            _resolutionStack.OfType<DamageFrame>().Any(damage =>
                damage.Id == window.ParentFrameId && damage.ParentFrameId == attack.ResolutionId &&
                damage.SourceSeat == attack.SourceSeat && damage.TargetSeat == attack.TargetSeat));

    private void AssertProgramAttackState()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (frame.AttackAttempt is not { } state)
            {
                if (frame.AttackReturn is not null)
                    throw new InvalidOperationException("A completed program damage retained its return receipt.");
                continue;
            }
            if (!IsValidPlayerSeat(state.SourceSeat) || !IsValidPlayerSeat(state.TargetSeat) ||
                state.DamageAmount < 0 || frame.AttackReturn is null ||
                state.ChainedTargetIndex < 0 || state.ChainedTargetIndex > (state.ChainedTargetSeats?.Count ?? 0) ||
                (state.ChainedTargetSeats ?? []).Any(seat => !IsValidPlayerSeat(seat)))
                throw new InvalidOperationException("A program damage attempt has invalid participants or chain cursor.");
            if (frame.AttackReturn.ParentAttackOwnerFrameId is { } parent &&
                !_resolutionStack.TakeWhile(item => item.Id != frame.Id).Any(item => item.Id == parent))
                throw new InvalidOperationException("A program damage attempt lost its suspended parent owner.");
            if (frame.AttackReturn.ParentDamageWindowFrameId is { } windowId &&
                !_resolutionStack.OfType<DamageTriggerWindowFrame>().Any(window => window.Id == windowId))
                throw new InvalidOperationException("A program damage attempt lost its suspended damage occurrence.");
        }
    }

}
