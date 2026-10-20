namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouChenGongActualTurnOrdinal => ProgramEventHistory<TurnStartedEvent>().Count();
    private MouChenGongZhichiProtection? CurrentMouChenGongProtection(int owner)
    {
        if (!IsValidPlayerSeat(owner) || !_players[owner].IsAlive ||
            CompleteProgramEventHistory().LastOrDefault(e => e is TurnStartedEvent or TurnEndedEvent) is not TurnStartedEvent turn ||
            turn.TurnNumber != _turnNumber || turn.ActorSeat != _currentSeat) return null;
        var ordinal = MouChenGongActualTurnOrdinal;
        return ProgramEventHistory<MouChenGongZhichiIssuedEvent>().Select(e => e.Protection).SingleOrDefault(p => p.Source.OwnerSeat == owner &&
            p.ActualTurnNumber == turn.TurnNumber && p.ActualTurnOwnerSeat == turn.ActorSeat && p.ActualTurnOrdinal == ordinal);
    }
    private bool ValidMouChenGongProtection(MouChenGongZhichiProtection p) => p.Source.SkillId == MouChenGongComposition.Zhichi &&
        p.Source.BindingId == "arm-protection" && p.DamageTargetSeat == p.Source.OwnerSeat && IsValidPlayerSeat(p.Source.OwnerSeat) &&
        p.WindowId > 0 && p.DamageFrameId > 0 && p.AttackResolutionId > 0 && p.ActualTurnOrdinal > 0 && OriginalMouChenGongProtectionTurn(p) &&
        _contentRegistry.GetSkill(p.Source.SkillId).Program is { } program && program.GameplayHash == p.GameplayHash && MouChenGongComposition.IsZhichi(program) &&
        ProgramEventHistory<MouChenGongZhichiIssuedEvent>().Count(e => e.Protection == p) == 1 &&
        ProgramEventHistory<DamageTriggerWindowOpenedEvent>().Count(e => e.ResolutionId == p.WindowId && e.DamageFrameId == p.DamageFrameId &&
            e.TargetSeat == p.Source.OwnerSeat && e.Candidates.Any(c => c.ToProgramCandidate() is { } candidate && candidate.OwnerSeat == p.Source.OwnerSeat &&
                candidate.SkillId == p.Source.SkillId && candidate.SkillInstanceId == p.Source.SkillInstanceId && candidate.BindingId == p.Source.BindingId && candidate.GameplayHash == p.GameplayHash)) == 1;
    private bool OriginalMouChenGongProtectionTurn(MouChenGongZhichiProtection p)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var index = Array.FindIndex(history, e => e is DamageTriggerWindowOpenedEvent window && window.ResolutionId == p.WindowId);
        if (index < 0) return false;
        var prefix = history.Take(index).ToArray();
        return prefix.LastOrDefault(e => e is TurnStartedEvent or TurnEndedEvent) is TurnStartedEvent turn &&
            turn.TurnNumber == p.ActualTurnNumber && turn.ActorSeat == p.ActualTurnOwnerSeat && prefix.OfType<TurnStartedEvent>().Count() == p.ActualTurnOrdinal &&
            prefix.OfType<DamageRequestedEvent>().Count(e => e.ResolutionId == p.DamageFrameId && e.TargetSeat == p.DamageTargetSeat &&
                e.Amount == p.DamageAmount && e.Nature == p.Nature && e.SourceLess == p.SourceLess) == 1;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 29601)]
    private void ObserveMouChenGongZhichi(IGameEvent fact)
    {
        if (fact is DamageTriggerWindowOpenedEvent opened && ActiveDamageTrigger is { TriggerWindow: SkillProgramTriggerWindow.AfterDamageApplied } window &&
            window.Id == opened.ResolutionId && window.ParentFrameId == opened.DamageFrameId && _players[window.TargetSeat].IsAlive &&
            CurrentMouChenGongProtection(window.TargetSeat) is null)
        {
            var candidates = window.Candidates.Select(c => c.ToProgramCandidate()).Where(c => c is not null && c.OwnerSeat == window.TargetSeat &&
                c.SkillId == MouChenGongComposition.Zhichi && c.BindingId == "arm-protection").Cast<ProgramTriggerCandidate>().ToArray();
            if (candidates.Length == 0) return;
            var candidate = candidates.OrderBy(c => c.SkillInstanceId, StringComparer.Ordinal).First();
            var attack = GetDamageTriggerAttack(window);
            if (_contentRegistry.GetSkill(candidate.SkillId).Program is not { } program || program.GameplayHash != candidate.GameplayHash ||
                !MouChenGongComposition.IsZhichi(program) || attack.TargetSeat != window.TargetSeat ||
                _resolutionStack.SingleOrDefault(f => f.Id == window.ParentFrameId) is not DamageFrame damage || damage.TargetSeat != window.TargetSeat ||
                ProgramEventHistory<DamageRequestedEvent>().Count(e => e.ResolutionId == damage.Id && e.SourceSeat == damage.SourceSeat &&
                    e.TargetSeat == damage.TargetSeat && e.Amount == damage.Amount && e.Nature == damage.Nature) != 1)
                throw new InvalidOperationException("Zhichi can arm only from its actual native after-damage window and frozen original skill candidate.");
            var protection = new MouChenGongZhichiProtection(new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId),
                candidate.GameplayHash, window.Id, window.ParentFrameId, attack.ResolutionId, _turnNumber, _currentSeat, MouChenGongActualTurnOrdinal,
                window.TargetSeat, damage.Amount, damage.Nature, attack.IsSourceLess);
            AdvanceEventRulesAndQueueFact(new MouChenGongZhichiIssuedEvent(protection));
            AddLog("SkillTriggered", $"{_players[window.TargetSeat].Name} 的【智迟】生效，本回合后续受到的伤害均被防止。", window.TargetSeat);
            return;
        }
        if (fact is not ProgramDamagePreventedEvent prevented || prevented.SkillId != MouChenGongComposition.Zhichi ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WindowContext.MouChenGongProtection: { } protectionReceipt } f) return;
        int? nativeSource = prevented.SourceSeat;
        if (!ExactMouChenGongBeforeFrame(f, allowPrevented: true) ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == f.WindowContext.ParentFrameId) is not BeforeDamageProgramWindowFrame parent ||
            prevented.FrameId != parent.Id ||
            prevented.BindingId != "prevent-later-damage" || prevented.OwnerSeat != f.OwnerSeat || prevented.TargetSeat != f.OwnerSeat ||
            BeforeDamageHasNoSource(parent) != (f.WindowContext.SourceSeat is null) ||
            (BeforeDamageHasNoSource(parent) ? nativeSource is not null && nativeSource != parent.SourceSeat : nativeSource != parent.SourceSeat) ||
            prevented.Amount != f.WindowContext.Amount)
            throw new InvalidOperationException("Zhichi prevention must return through its exact native before-damage parent and already issued protection.");
        AdvanceEventRulesAndQueueFact(new MouChenGongZhichiPreventedEvent(prevented.FrameId, protectionReceipt, f.WindowContext.SourceSeat, prevented.TargetSeat, prevented.Amount));
    }
    private bool IsMouChenGongZhichiBeforeCandidate(ProgramTriggerCandidate candidate) => candidate.SkillId == MouChenGongComposition.Zhichi && candidate.BindingId == "prevent-later-damage";
    private IEnumerable<BeforeDamageProgramCandidate> CollectIssuedMouChenGongZhichiCandidates(int source, int target, bool rangeChainOnly)
    {
        if (CurrentMouChenGongProtection(target) is not { } protection || !ValidMouChenGongProtection(protection)) yield break;
        var trigger = _contentRegistry.GetSkill(protection.Source.SkillId).Program!.Triggers.Single(t => t.Id == "prevent-later-damage");
        yield return new(new(target, protection.Source.SkillId, trigger.Id, protection.Source.SkillInstanceId, protection.GameplayHash, trigger.Priority),
            CaptureProgramTriggerFacts(_players[target]) with { EventTargetHp = _players[target].Hp });
    }
    private ProgramSkillWindowContext AttachMouChenGongProtection(BeforeDamageProgramCandidate item, ProgramSkillWindowContext context) =>
        IsMouChenGongZhichiBeforeCandidate(item.Candidate) && CurrentMouChenGongProtection(item.Candidate.OwnerSeat) is { } p &&
        p.Source.SkillInstanceId == item.Candidate.SkillInstanceId && p.GameplayHash == item.Candidate.GameplayHash
            ? context with { MouChenGongProtection = p } : context;
    private bool IsIssuedMouChenGongZhichiCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != MouChenGongComposition.Zhichi || context.OwnerSeat != candidate.OwnerSeat ||
            CurrentMouChenGongProtection(candidate.OwnerSeat) is not { } p || !ValidMouChenGongProtection(p) ||
            p.Source.SkillInstanceId != candidate.SkillInstanceId || p.GameplayHash != candidate.GameplayHash) return false;
        if (candidate.BindingId == "arm-protection")
            return context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.ParentFrameId == p.WindowId &&
                _resolutionStack.SingleOrDefault(f => f.Id == p.WindowId) is DamageTriggerWindowFrame w && w.TriggerWindow == context.Window &&
                w.ParentFrameId == p.DamageFrameId && w.TargetSeat == candidate.OwnerSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
                w.Candidates[w.CandidateIndex].ToProgramCandidate() == candidate &&
                (CreateAfterDamageProgramContext(w, candidate) with { Facts = null }) == (context with { Facts = null });
        if (candidate.BindingId != "prevent-later-damage" || context.Window != SkillProgramTriggerWindow.BeforeDamageApplied || context.MouChenGongProtection != p ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not BeforeDamageProgramWindowFrame before || before.Prevented || before.RedirectedTargetSeat is not null ||
            before.TargetSeat != candidate.OwnerSeat || before.Amount <= 0 || before.CandidateIndex < 0 || before.CandidateIndex >= before.Candidates.Count) return false;
        var current = before.Candidates[before.CandidateIndex];
        return current.Candidate == candidate && CreateBeforeDamageProgramContext(before, current) == context &&
            CurrentDamageAttempt is { } attack && attack.TargetSeat == before.TargetSeat && attack.SourceSeat == before.SourceSeat;
    }
    private bool ExactMouChenGongBeforeFrame(ProgramSkillFrame f, bool allowPrevented)
    {
        if (f.TriggerId != "prevent-later-damage" || f.ActivationId != f.TriggerId || f.SkillId != MouChenGongComposition.Zhichi ||
            f.InstructionIndex is < 0 or > 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, MouChenGongProtection: { } p } context ||
            CurrentMouChenGongProtection(f.OwnerSeat) != p || !ValidMouChenGongProtection(p) || p.Source.OwnerSeat != f.OwnerSeat ||
            p.Source.SkillInstanceId != f.SkillInstanceId || p.GameplayHash != f.GameplayHash ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not BeforeDamageProgramWindowFrame w ||
            w.TargetSeat != f.OwnerSeat || w.Amount <= 0 || w.RedirectedTargetSeat is not null || w.Prevented && !allowPrevented ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].Candidate) ||
            CreateBeforeDamageProgramContext(w, w.Candidates[w.CandidateIndex]) != context) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == w.Id;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29602)]
    private bool GateMouChenGongZhichi(ProgramTriggerLegalityContext gate) => gate.SkillId != MouChenGongComposition.Zhichi ||
        gate.Candidate.BindingId != "prevent-later-damage" || CurrentMouChenGongProtection(gate.OwnerSeat) is not null;
    private bool PermitMouChenGongZhichiAfterLoss(ProgramSkillFrame f)
    {
        if (f.SkillId != MouChenGongComposition.Zhichi || f.WindowContext is not { } context) return false;
        if (f.TriggerId == "prevent-later-damage") return ExactMouChenGongBeforeFrame(f, allowPrevented: true);
        return f.TriggerId == "arm-protection" && f.InstructionIndex is 0 or 1 && IsIssuedMouChenGongZhichiCandidate(
            new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), context);
    }
}
