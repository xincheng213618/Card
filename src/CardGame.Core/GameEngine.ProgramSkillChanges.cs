namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly record struct ProgramSkillChangeContext(ProgramSkillFrame Frame, CharacterState Owner);

    private enum ProgramSkillSourceRemoval
    {
        Enabled,
        All,
        NonModePreserveProjections
    }

    // The executor owns definition, lifetime and paid-continuation checks. A
    // mutation binds to its exact active instruction; it has no round prerequisite.
    private ProgramSkillChangeContext PrepareProgramSkillChange(ProgramSkillFrame supplied)
    {
        var active = GetActiveProgramFrame(supplied.Id);
        if (active.OwnerSeat != supplied.OwnerSeat || active.SkillId != supplied.SkillId ||
            active.SkillInstanceId != supplied.SkillInstanceId || active.GameplayHash != supplied.GameplayHash ||
            active.ActivationId != supplied.ActivationId || active.TriggerId != supplied.TriggerId ||
            active.InstructionIndex != supplied.InstructionIndex || active.InstructionIndex < 1 ||
            active.WindowContext?.ParentFrameId != supplied.WindowContext?.ParentFrameId)
            throw new InvalidOperationException("A skill change lost its exact active owner, binding, instance or instruction.");
        return new(active, _players[active.OwnerSeat]);
    }

    private void UpgradeConfiguredConversionTier(ProgramSkillFrame frame, string state) =>
        TryUpgradeConfiguredConversionTier(frame, state);

    private bool TryUpgradeConfiguredConversionTier(ProgramSkillFrame frame, string state, int? expectedTier = null)
    {
        var context = PrepareProgramSkillChange(frame);
        var key = (context.Owner.Seat, state);
        var tier = _configuredConversionTiers.GetValueOrDefault(key);
        if (tier is < 0 or > 2) throw new InvalidOperationException("A conversion modification count is outside its defined versions.");
        if (tier == 2 || expectedTier is { } expected && tier != expected) return false;
        CommitConfiguredConversionTier(context.Owner, state, tier + 1);
        AddLog("SkillTriggered", $"{context.Owner.Name} 的转换能力修改至第 {tier + 1} 层。", context.Owner.Seat);
        return true;
    }

    // Native declaration observers can initialize a tier without a program
    // instruction. Their source qualification stays at the observer entry.
    private bool CommitConfiguredConversionTier(CharacterState owner, string state, int tier)
    {
        if (tier is < 0 or > 2) throw new InvalidOperationException("A conversion modification count is outside its defined versions.");
        var key = (owner.Seat, state);
        if (_configuredConversionTiers.GetValueOrDefault(key) == tier) return false;
        _configuredConversionTiers[key] = tier;
        AdvanceEventRulesAndQueueFact(new ConfiguredConversionTierChangedEvent(owner.Seat, state, tier));
        return true;
    }

    private void SetProgramBooleanState(ProgramSkillFrame frame, string stateId, bool value) =>
        SetProgramBooleanState(frame, frame.SkillId, frame.SkillInstanceId, stateId, value);

    private void SetProgramBooleanState(ProgramSkillFrame source, string skillId, string instanceId, string stateId, bool value)
    {
        var context = PrepareProgramSkillChange(source);
        SetProgramBooleanState(context.Owner.Seat, skillId, instanceId, stateId, value);
    }

    private void GrantProgramSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
        AcquireProgramSkillChange(PrepareProgramSkillChange(frame), skillIds, announceAwakening: true);

    // An issued acquisition owns one removable source even while another source
    // supplies the same capability. Sharing its active instance avoids duplicate
    // triggers; revocation removes only the specifically issued grant IDs.
    private SkillGrant GrantOwnedProgramSkillSource(ProgramSkillFrame frame, string sourceId, string skillId)
    {
        var context = PrepareProgramSkillChange(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!sourceId.StartsWith("acquired:", StringComparison.Ordinal) || !context.Owner.IsAlive ||
            context.Frame.PendingMovementContinuation is not null ||
            context.Frame.SelectedCardPayment is not null && context.Frame.SelectedCardPaymentResult is null)
            throw new InvalidOperationException("An issued skill source requires its settled original instruction and living owner.");
        _ = _contentRegistry.GetSkill(skillId);
        var grantId = $"{sourceId}:{skillId}";
        if (context.Owner.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == grantId) is { } prior)
            return prior.SourceId == sourceId && prior.SkillId == skillId ? prior :
                throw new InvalidOperationException("An issued skill source conflicts with its original acquisition.");
        var instanceId = GetSkillBindingShard(context.Owner).ActiveGrants.Where(g => g.SkillId == skillId)
            .OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId ?? grantId;
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var acquired = !EnabledContentSkillIds(context.Owner).Contains(skillId, StringComparer.Ordinal);
        var grant = new SkillGrant(grantId, skillId, instanceId, sourceId);
        context.Owner.SkillGrants.Grant(grant);
        if (acquired)
        {
            RegisterTaggedConversionSkill(context.Owner, skillId);
            AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(context.Owner.Seat, context.Frame.SkillId,
                Array.AsReadOnly(new[] { skillId })));
        }
        return grant;
    }

    private IReadOnlyList<SkillGrant> RevokeOwnedProgramSkillSources(ProgramSkillFrame frame, IReadOnlyList<string> grantIds)
    {
        var context = PrepareProgramSkillChange(frame);
        var ids = grantIds.ToHashSet(StringComparer.Ordinal);
        var removed = context.Owner.SkillGrants.Grants.Where(g => ids.Contains(g.GrantId)).ToArray();
        if (removed.Any(g => g.IsModeRule || !g.SourceId.StartsWith("acquired:", StringComparison.Ordinal)))
            throw new InvalidOperationException("Source revocation may remove only explicitly issued acquisition grants.");
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        foreach (var grant in removed) context.Owner.SkillGrants.RemoveGrant(grant.GrantId);
        return Array.AsReadOnly(removed);
    }

    // Native paid rules qualify the recipient and lifetime before entering this
    // mutation layer. Losing the initiating source does not revoke an issued grant.
    private void GrantIssuedProgramTurnSkills(ProgramSkillFrame frame, int recipientSeat,
        IReadOnlyList<string> skillIds, SkillGrantTurnExpiry expiry)
    {
        var sourceId = $"turn:{expiry.TurnNumber}:{expiry.TurnOwnerSeat}:native:{frame.OwnerSeat}:{frame.SkillId}:" +
            $"{frame.SkillInstanceId}:{GetProgramBindingId(frame)}:frame:{frame.Id}:effect:{frame.InstructionIndex - 1}:recipient:{recipientSeat}";
        CommitProgramParticipantSkillGrants(frame, recipientSeat, skillIds, sourceId, expiry, null);
    }

    private void CommitProgramParticipantSkillGrants(ProgramSkillFrame frame, int recipientSeat,
        IReadOnlyList<string> skillIds, string sourceId, SkillGrantTurnExpiry? turnExpiry,
        SkillGrantNextActualTurnStartExpiry? nextStartExpiry)
    {
        var active = PrepareProgramSkillChange(frame).Frame;
        if (!IsValidPlayerSeat(recipientSeat) || active.PendingMovementContinuation is not null ||
            active.SelectedCardPayment is not null && active.SelectedCardPaymentResult is null)
            throw new InvalidOperationException("A participant skill grant requires its exact settled instruction and recipient.");
        var recipient = _players[recipientSeat];
        if (!recipient.IsAlive) return;
        var granted = skillIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var skillId in granted) _ = _contentRegistry.GetSkill(skillId);
        foreach (var skillId in granted)
        {
            var existing = GetSkillBindingShard(recipient).ActiveGrants.Where(grant => grant.SkillId == skillId)
                .OrderBy(grant => grant.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            var instanceId = existing?.SkillInstanceId ?? $"turn-shared:{recipient.Seat}:{skillId}";
            recipient.SkillGrants.Grant(new SkillGrant($"{sourceId}:{skillId}", skillId, instanceId, sourceId,
                TurnExpiry: turnExpiry) { NextActualTurnStartExpiry = nextStartExpiry });
        }
        AdvanceEventRulesAndQueueFact(new ProgramTurnSkillsGrantedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, Array.AsReadOnly(granted))
        { RecipientSeat = recipient.Seat, GrantSourceId = sourceId, TurnExpiry = turnExpiry,
            NextActualTurnStartExpiry = nextStartExpiry });
    }

    private IReadOnlyList<string> AcquireProgramSkillChange(ProgramSkillChangeContext context,
        IReadOnlyList<string> skillIds, bool announceAwakening)
    {
        var frame = context.Frame;
        var acquired = IsDyingOwnerReplacementBinding(frame)
            ? AcquireDyingOwnerReplacementSkills(frame, skillIds)
            : AcquireRuntimeSkills(context.Owner, frame.SkillId, skillIds);
        if (announceAwakening && (_contentRegistry.GetSkill(frame.SkillId).Tags & SkillTag.Awakening) != 0)
            AdvanceEventRulesAndQueueFact(new SkillAwakenedEvent(context.Owner.Seat, frame.SkillId,
                context.Owner.MaxHp, acquired));
        return acquired;
    }

    // Acquisition order and projected-source ownership are part of the existing
    // operation contract. All paths share the same instruction-bound mutation.
    private void ReplaceProgramSkills(ProgramSkillFrame frame, IReadOnlyList<string> lost,
        IReadOnlyList<string> acquired, ProgramSkillSourceRemoval removal, bool acquireFirst,
        bool announceAwakening = false)
    {
        var context = PrepareProgramSkillChange(frame);
        if (acquireFirst) AcquireProgramSkillChange(context, acquired, announceAwakening);
        var grants = removal == ProgramSkillSourceRemoval.NonModePreserveProjections
            ? lost.SelectMany(id => context.Owner.SkillGrants.Grants.Where(g => g.SkillId == id)).ToArray()
            : context.Owner.SkillGrants.Grants.Where(g => lost.Contains(g.SkillId)).ToArray();
        foreach (var grant in grants)
        {
            if (removal == ProgramSkillSourceRemoval.Enabled && !grant.IsEnabled ||
                removal == ProgramSkillSourceRemoval.NonModePreserveProjections && grant.IsModeRule) continue;
            if (removal == ProgramSkillSourceRemoval.NonModePreserveProjections &&
                (grant.LordProjection is not null || grant.GeneralLibraryProjection is not null))
                context.Owner.SkillGrants.SetEnabled(grant.GrantId, false);
            else context.Owner.SkillGrants.RemoveGrant(grant.GrantId);
        }
        if (!acquireFirst) AcquireProgramSkillChange(context, acquired, announceAwakening);
    }

    private SkillProgramStepOutcome ReplaceProgramOwnerSkills(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = PrepareProgramSkillChange(frame).Frame;
        var paidContinuation = frame.WindowContext?.Window == SkillProgramTriggerWindow.DyingEntering
            ? CaptureDyingOwnerReplacementContinuation(frame, effect) : null;
        ReplaceProgramSkills(frame, effect.SkillIds, [effect.SourceBind!],
            ProgramSkillSourceRemoval.Enabled, acquireFirst: true);
        CommitProgramSkillChangeFact(frame, new ProgramOwnerSkillsReplacedEvent(frame.Id, frame.SkillId,
            frame.OwnerSeat, effect.SkillIds.ToArray(), effect.SourceBind!));
        if (paidContinuation is not null) IssueReplacedPaidHpContinuation(paidContinuation);
        // The initiating source can disappear here. Its paid parent return is
        // retained before completing this terminal replacement instruction.
        FinishProgramSkill(frame, completed: true);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void CommitProgramSkillChangeFact(ProgramSkillFrame frame, IGameEvent fact)
    {
        PrepareProgramSkillChange(frame);
        AdvanceEventRulesAndQueueFact(fact);
    }
}
