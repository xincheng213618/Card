namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasLeZouShiCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.InitializeYunzhengInitialEntities) || _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse);
    private static CardConversionSource YunzhengSource(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zou_shi_le_LeZouShiYunzheng.YunzhengSource(frame);

    // The physical mark outlives its original holder, zone and granting instance.
    private bool HasYunzhengEntityMark(int cardId) => HasLeZouShiCapability &&
        (ProgramEventHistory<YunzhengInitialEntityMarkedEvent>().Any(e => e.CardId == cardId) ||
         ProgramEventHistory<HuoxinEntityMarkedEvent>().Any(e => e.CardId == cardId));
    private bool HasYunzhengHandEntityMark(int ownerSeat, int cardId) =>
        IsValidPlayerSeat(ownerSeat) && GetHand(_players[ownerSeat]).Any(c => c.Id == cardId) &&
        HasYunzhengEntityMark(cardId);
    private bool IsYunzhengHandLimitExempt(CharacterState owner, int cardId) =>
        HasLeZouShiCapability && HasLiveYunzhengSource(owner) &&
        HasYunzhengHandEntityMark(owner.Seat, cardId);
    // Locked source lookup bottoms out before asking for another source.
    private bool YunzhengNonLockedGrantQualified(CharacterState holder, SkillGrant grant) =>
        !HasLeZouShiCapability || _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
        !GetHand(holder).Any(c => HasYunzhengEntityMark(c.Id)) ||
        !_players.Any(p => p.IsAlive && p.Seat != holder.Seat && HasLiveYunzhengSource(p));
    private bool YunzhengBaseGrantQualified(CharacterState owner, SkillGrant grant) => LibraryBaseGrantLive(owner, grant) &&
        (grant.IsModeRule || !owner.IdentityConcealed && IsCurrentTurnSkillGrantQualified(owner, grant) &&
         GuanduGeneralGrantQualified(owner, grant) && IsProjectedGrantQualified(owner, grant) &&
         IsGeneralLibraryGrantQualified(owner, grant) && IsHpConditionalGrantQualified(owner, grant) &&
         IsEquipmentSuitGrantQualified(owner, grant) && IsDaQiaoXiaoQiaoLuoyanGrantQualified(owner, grant));
    private bool HasLiveYunzhengSource(CharacterState owner)
    {
        if (!owner.IsAlive) return false;
        var candidates = owner.SkillGrants.Grants.Where(g => g.SkillId == "le:yunzheng" && YunzhengBaseGrantQualified(owner, g)).ToArray();
        if (candidates.Length == 0) return false;
        var suppressors = HasPrivateGeneralLibraryCapability ? PrivateGeneralLibrarySuppressionInputs(owner) :
            owner.SkillGrants.Grants.Where(g => _contentRegistry.GetSkill(g.SkillId).SuppressionRule is { } policy &&
                owner.Hp == policy.OwnerHpEquals && YunzhengBaseGrantQualified(owner, g))
                .Select(g => g.SkillId).ToHashSet(StringComparer.Ordinal);
        return candidates.Any(g => suppressors.Count == 0 || g.IsModeRule || suppressors.Contains(g.SkillId) || g.SourceId.StartsWith("equipment:", StringComparison.Ordinal));
    }
    private readonly record struct YunzhengQualificationSeat(long Revision, int Hp, bool Alive, bool Concealed,
        bool PrimarySelected, bool PrimaryRevealed, bool SecondarySelected, bool SecondaryRevealed, Role Role);
    private YunzhengQualificationSeat[] _yunzhengQualificationSeats = [];
    private (long Inner, long Movement, int Initial, int Shown) _yunzhengQualificationInputs;
    private long _yunzhengQualificationStamp;
    private long CaptureYunzhengQualificationStamp(long inner)
    {
        if (!HasLeZouShiCapability) return inner;
        var initial = ProgramEventHistory<YunzhengInitialEntityMarkedEvent>().Count();
        var shown = ProgramEventHistory<HuoxinEntityMarkedEvent>().Count();
        if (initial == 0 && shown == 0) return inner;
        // Pure cache evidence; a runtime skill lookup here would recurse into shards.
        var inputs = (inner, (long)_movementSequence, initial, shown);
        var changed = inputs != _yunzhengQualificationInputs || _yunzhengQualificationSeats.Length != _players.Count;
        if (_yunzhengQualificationSeats.Length != _players.Count) _yunzhengQualificationSeats = new YunzhengQualificationSeat[_players.Count];
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i]; var next = new YunzhengQualificationSeat(p.SkillGrants.Revision, p.Hp, p.IsAlive, p.IdentityConcealed,
                p.GeneralSelected, p.GeneralRevealed, p.SecondaryGeneralSelected, p.SecondaryGeneralRevealed, p.Role);
            if (_yunzhengQualificationSeats[i] == next) continue;
            _yunzhengQualificationSeats[i] = next; changed = true;
        }
        if (changed) { _yunzhengQualificationInputs = inputs; _yunzhengQualificationStamp++; }
        return long.MinValue / 8 + _yunzhengQualificationStamp;
    }
    private ProgramLifecycleTriggerWindowFrame CaptureYunzhengInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeZouShiCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.YunzhengInitialEntities is not null)
            throw new InvalidOperationException("Yunzheng's final opening Hand can only be frozen once.");
        var owners = window.Candidates.Where(candidate => GetProgramTrigger(candidate).Effects is
            [{ Op: SkillProgramEffectOp.InitializeYunzhengInitialEntities }])
            .Select(candidate => candidate.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Hand(owner));
            if (entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("A Yunzheng initial entity requires its actual opening-Hand entry.");
            return new YunzhengInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { YunzhengInitialEntities = entities };
    }

    private bool IsIssuedYunzhengCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        HasLeZouShiCapability && candidate.SkillId == "le:yunzheng" &&
        candidate.BindingId == "mark-initial-entities" && IsValidPlayerSeat(candidate.OwnerSeat) &&
        _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash == candidate.GameplayHash &&
        GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.InitializeYunzhengInitialEntities }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == candidate.OwnerSeat &&
        _phase == TurnPhase.NotStarted &&
        _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.GameStarting, Continuation: ProgramLifecycleContinuation.CompleteGameStarting,
              YunzhengInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        parent.Candidates[parent.CandidateIndex] == candidate &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<YunzhengInitializedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28950,
        SkillProgramEffectOp.InitializeYunzhengInitialEntities)]
    private bool CanOfferYunzheng(ProgramTriggerLegalityContext gate) =>
        IsIssuedYunzhengCandidate(gate.Candidate, gate.Context);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28950)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28950)]
    private bool PermitIssuedYunzheng(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "le:yunzheng" || frame.TriggerId != "mark-initial-entities" ||
            frame.WindowContext is not { } context || frame.InstructionIndex is < 0 or > 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash ||
            !ExactOriginalHandLifecycleParent(frame, SkillProgramTriggerWindow.GameStarting)) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId,
            frame.SkillInstanceId, frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        if (IsIssuedYunzhengCandidate(candidate, context)) return true;
        // After the only instruction, preserve its completed right while the executor closes it.
        return frame.InstructionIndex == 1 &&
            ProgramEventHistory<YunzhengInitializedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is [var initialized] &&
            initialized.Source == YunzhengSource(frame) && initialized.GameplayHash == frame.GameplayHash;
    }

    [ProgramCapability]
    private sealed class LeZouShiCapability(GameEngine engine) : ILeZouShiHost
    {
        public SkillProgramStepOutcome InitializeYunzheng(ProgramSkillFrame frame) => engine.InitializeYunzheng(frame);
        public SkillProgramStepOutcome ResolveHuoxin(ProgramSkillFrame frame) => engine.BeginHuoxin(frame);
    }

    private SkillProgramStepOutcome InitializeYunzheng(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || !PermitIssuedYunzheng(frame) ||
            ProgramEventHistory<YunzhengInitializedEvent>().Any(e => e.Source.OwnerSeat == frame.OwnerSeat))
            throw new InvalidOperationException("Yunzheng requires its exact frozen opening mother and initializes once.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(f => f.Id == frame.WindowContext!.ParentFrameId);
        var entities = parent.YunzhengInitialEntities!.Where(e => e.OwnerSeat == frame.OwnerSeat).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e =>
            !_cardMovements.Any(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId &&
                m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OwnerSeat))))
            throw new InvalidOperationException("Yunzheng cannot mark an entity without its exact opening-Hand provenance.");
        AdvanceEventRulesAndQueueFact(new YunzhengInitializedEvent(frame.Id, YunzhengSource(frame), frame.GameplayHash, entities.Length));
        foreach (var entity in entities)
            AdvanceEventRulesAndQueueFact(new YunzhengInitialEntityMarkedEvent(frame.Id, entity.OwnerSeat,
                entity.CardId, entity.PrintedKind, entity.EntrySequence));
        return SkillProgramStepOutcome.Continue;
    }
}
