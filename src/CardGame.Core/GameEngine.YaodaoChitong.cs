namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string YaodaoChitongSkill = "shanhe:yaodao-chitong";

    [ProgramCapability]
    private sealed class YaodaoChitongCapability(GameEngine engine) : IYaodaoChitongHost
    {
        public SkillProgramStepOutcome BeginYaodaoChitong(ProgramSkillFrame f) => engine.BeginYaodaoChitong(f);
        public SkillProgramStepOutcome ResolveYaodaoChitong(ProgramSkillFrame f, SkillProgramEffect e) => engine.ResolveYaodaoChitong(f, e);
    }

    private bool ResolveYaodaoChitongEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.YaodaoChitong) return false;
        var grantId = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } oldSeat })
            _players[oldSeat].SkillGrants.RemoveGrant(grantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } newSeat } && _contentRegistry.Skills.ContainsKey(YaodaoChitongSkill))
            _players[newSeat].SkillGrants.Grant(new SkillGrant(grantId, YaodaoChitongSkill, grantId, grantId));
        return true;
    }

    private Card? ActiveYaodaoChitong(int seat, string skillId, string instance) =>
        skillId == YaodaoChitongSkill && _players[seat].IsAlive && HasWeaponAbility(_players[seat], CardKind.YaodaoChitong)
            ? GetEquipment(_players[seat]).SingleOrDefault(c => c.Kind == CardKind.YaodaoChitong &&
                IsActiveProgramSourceEquipmentCard(seat, skillId, instance, c)) : null;

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 5790)]
    private int YaodaoChitongBlackSlashBonus(IDamageAttempt attack)
    {
        if (attack is not CardAttackHandle || attack.IsSourceLess || attack.IsChainPropagation ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) || attack.SourceSeat != attack.CardUserSeat ||
            !_players[attack.SourceSeat].IsAlive || !HasWeaponAbility(_players[attack.SourceSeat], CardKind.YaodaoChitong)) return 0;
        // The accepted native action preserves a same-color, mixed-suit
        // material group; its absent common suit must not erase black color.
        if (LifecycleCardUse(attack.ResolutionId)?.Action is { } action)
            return ActualTargetPolicyColor(action) == false ? 1 : 0;
        return attack.Card is { } card && EffectiveSuit(_players[attack.CardUserSeat], card) is Suit.Spade or Suit.Club ? 1 : 0;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5791, SkillProgramEffectOp.BeginYaodaoChitong)]
    private bool CanOfferYaodaoChitong(ProgramTriggerLegalityContext gate) =>
        ActiveYaodaoChitong(gate.OwnerSeat, gate.SkillId, gate.Candidate.SkillInstanceId) is not null &&
        gate.WindowContext is { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } &&
        ProgramCardAttack is { } attack && SameAttackOwner(attack, ActiveCardAttack) &&
        attack.SourceSeat == gate.OwnerSeat && context.ParentCardUseFrameId == attack.ResolutionId &&
        attack.EffectiveCardKind is { } kind && IsSlashCard(kind);

    private SkillProgramStepOutcome BeginYaodaoChitong(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.YaodaoChitong is not null || f.InstructionIndex != 1 ||
            ActiveYaodaoChitong(f.OwnerSeat, f.SkillId, f.SkillInstanceId) is not { } weapon ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } window ||
            ProgramCardAttack is not { } attack || !SameAttackOwner(attack, ActiveCardAttack) || attack.SourceSeat != f.OwnerSeat ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { } action } use ||
            use.SourceSeat != f.OwnerSeat || context.ParentCardUseFrameId != use.Id || context.CardActionId != action.ActionId ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != attack.TargetSeat)
            throw new InvalidOperationException("Yaodao Chitong requires the exact equipped weapon and original source-owned Slash target window.");
        var r = new YaodaoChitongReceipt(new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, weapon.Id, use.Id, action.ActionId, window.ParentFrameId, use.TargetIndex, attack.TargetSeat);
        ReplaceRuntimeTop(f with { YaodaoChitong = r });
        AdvanceEventRulesAndQueueFact(new YaodaoChitongStartedEvent(f.Id, r));
        return SkillProgramStepOutcome.Continue;
    }

    private bool YaodaoChitongTarget(CardUseFrame use, CardAttackHandle attack, int target)
    {
        if (!IsValidPlayerSeat(target) || target == attack.TargetSeat || target == use.SourceSeat ||
            use.TargetSeats.Contains(target) || !_players[target].IsAlive) return false;
        // This is a replacement of an already paid Use, so preserve its exact
        // material/conversion, quota exemption and distance policy invoice.
        var card = attack.Card ?? new Card(0, use.CardKind, use.Action?.EffectiveSuit ?? Suit.None,
            use.Action?.EffectiveRank ?? 0);
        return CanUseSlashTarget(_players[use.SourceSeat], _players[target], card,
            attack.ConversionSource, use.CardKind, existingUseFrameId: use.Id,
            physicalCardIds: use.Action!.PhysicalCards.Select(c => c.CardId).ToArray());
    }

    private SkillProgramStepOutcome ResolveYaodaoChitong(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(input.Id); AssertYaodaoChitongFrame(f);
        if (f.YaodaoChitong is not { Resolved: false } r || f.InstructionIndex != 3 ||
            f.JudgmentOutcomeBindings?.Where(o => o.Name == effect.SourceBind).ToArray() is not [var outcome] ||
            outcome.TargetSeat != f.OwnerSeat || outcome.Reason != "shanhe.yaodao-chitong" ||
            !IsValidProgramJudgmentOutcome(f, outcome) || ProgramCardAttack is not { } attack ||
            attack.ResolutionId != r.CardUseId || attack.TargetSeat != r.OriginalTargetSeat ||
            LifecycleCardUse(r.CardUseId) is not { } use || use.TargetIndex != r.TargetIndex)
            throw new InvalidOperationException("Yaodao Chitong lost the exact completed native judgment and its unchanged original Slash target.");
        var candidates = outcome.Suit is Suit.Spade or Suit.Club && _players[use.SourceSeat].IsAlive &&
            _players[r.OriginalTargetSeat].IsAlive && _winner == Winner.None
            ? _players.Where(p => YaodaoChitongTarget(use, attack, p.Seat)).Select(p => p.Seat).Order().ToArray() : [];
        var before = _random.State;
        int? selected = candidates.Length == 0 ? null : candidates[_random.Next(candidates.Length)];
        var after = _random.State;
        // The one RNG draw is paid before any later target or cleanup children.
        ReplaceRuntimeTop(f with { YaodaoChitong = r with { Resolved = true },
            YaodaoChitongRandom = new(before, after, candidates.Length, selected) });
        if (selected is { } next)
        {
            attack.SetDamageParticipants(attack.SourceSeat, next);
            attack.SetIgnoresArmor(HasArmorBypass(_players[attack.SourceSeat]) ||
                HasCardArmorBypass(_players[attack.SourceSeat], _players[next], use.CardKind));
            RedirectCardUseTarget(use.Id, r.OriginalTargetSeat, next);
        }
        AdvanceEventRulesAndQueueFact(new YaodaoChitongResolvedEvent(f.Id, use.Id, r.ActionId, r.TargetIndex,
            r.OriginalTargetSeat, selected, outcome.JudgmentFrameId, outcome.Suit, candidates.Length));
        return SkillProgramStepOutcome.Continue;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 5791)]
    private void AssertYaodaoChitongFrame(ProgramSkillFrame f)
    {
        if (f.YaodaoChitong is not { } r) return;
        if (f.GameplayHash != r.GameplayHash || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            f.SkillId != YaodaoChitongSkill || r.TargetIndex < 0 || !IsValidPlayerSeat(r.OriginalTargetSeat) ||
            ProgramEventHistory<YaodaoChitongStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started.Receipt != (r with { Resolved = false }) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } window ||
            window.ParentFrameId != r.CardUseId || window.Action.ActionId != r.ActionId ||
            window.Continuation != ProgramCardContinuation.SlashTargetRedirecting || window.CandidateIndex < 0 ||
            window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            LifecycleCardUse(r.CardUseId) is not { Action: { } action } use || action.ActionId != r.ActionId ||
            use.SourceSeat != f.OwnerSeat || r.TargetIndex >= use.TargetSeats.Count ||
            r.WeaponCardId <= 0 || GetAdvancedCard(r.WeaponCardId).Kind != CardKind.YaodaoChitong)
            throw new InvalidOperationException("Yaodao Chitong lost its exact weapon, original source, native target window or once-issued judgment receipt.");
        var resolved = ProgramEventHistory<YaodaoChitongResolvedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Resolved ? resolved is not [var paid] || paid.CardUseId != r.CardUseId || paid.ActionId != r.ActionId ||
                paid.TargetIndex != r.TargetIndex || paid.OriginalTargetSeat != r.OriginalTargetSeat ||
                f.YaodaoChitongRandom is not { } random || random.CandidateCount != paid.CandidateCount ||
                random.SelectedTargetSeat != paid.NewTargetSeat ||
                (random.CandidateCount == 0 ? random.Before != random.After || random.SelectedTargetSeat is not null :
                    random.Before == random.After || random.SelectedTargetSeat is null) ||
                use.TargetSeats[r.TargetIndex] != (paid.NewTargetSeat ?? r.OriginalTargetSeat) ||
                paid.NewTargetSeat is { } newTarget && (newTarget == f.OwnerSeat || newTarget == r.OriginalTargetSeat || paid.CandidateCount < 1)
            : resolved.Length != 0 || f.YaodaoChitongRandom is not null || use.TargetSeats[r.TargetIndex] != r.OriginalTargetSeat)
            throw new InvalidOperationException("Yaodao Chitong changed its actual target without its single paid random-result fact.");
    }

    private ProgramSkillFrame? ExactYaodaoChitongJudgmentRoot(JudgmentFrame judgment)
    {
        var root = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == judgment.ParentFrameId);
        if (root is not { YaodaoChitong: { Resolved: false } receipt, InstructionIndex: 2 } ||
            judgment.ParentAttackId != receipt.CardUseId || judgment.TargetSeat != root.OwnerSeat ||
            judgment.Reason != "shanhe.yaodao-chitong" || JudgmentOutcomeInstruction(root, root.InstructionIndex) is null ||
            !IsValidProgramJudgmentContinuation(judgment, root)) return null;
        AssertYaodaoChitongFrame(root);
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index < 1 || index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] != judgment ||
            _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame nativeWindow || nativeWindow.Id != receipt.WindowId ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } action } context ||
            context.ParentFrameId != receipt.WindowId || context.SourceSeat != root.OwnerSeat ||
            action.ParentCardUseFrameId != receipt.CardUseId || action.CardActionId != receipt.ActionId) return null;
        // Reuse the existing exact native judgment/retrial/finalized-claim
        // edges and paid movement/health observer edges, with this root's
        // separate weapon/card-use/window receipt as its sole admission proof.
        for (var child = index + 1; child < _resolutionStack.Count; child++)
            if (!JudgmentOutcomeDamageEdge(child, root)) return null;
        return root;
    }

    private bool IsExactYaodaoChitongJudgmentFinalizedSubtree(ProgramJudgmentTriggerWindowFrame window,
        JudgmentFrame? judgment, int index) => judgment is not null && index >= 1 && index + 1 < _resolutionStack.Count &&
        window.ParentFrameId == judgment.Id && _resolutionStack[index - 1] == judgment &&
        _resolutionStack[index + 1] is ProgramSkillFrame && ExactYaodaoChitongJudgmentRoot(judgment) is not null;

    private bool HasExactYaodaoChitongJudgmentSubtree(long judgmentId) =>
        _resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j => j.Id == judgmentId) is { } judgment &&
        ExactYaodaoChitongJudgmentRoot(judgment) is not null;

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 5791)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 5791)]
    private bool ContinueIssuedYaodaoChitong(ProgramSkillFrame f)
    {
        if (f.YaodaoChitong is null) return false;
        AssertYaodaoChitongFrame(f); return true;
    }
}
