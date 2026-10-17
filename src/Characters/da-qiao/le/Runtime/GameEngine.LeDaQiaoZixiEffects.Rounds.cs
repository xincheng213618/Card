namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueZixiExtraEffect(ProgramSkillFrame f, CardUseFrame use, ProgramCardTriggerWindowFrame window)
    {
        var r = f.ZixiEffects!; var old = use.ZixiEffectRounds;
        if (r.JudgmentCount != 1 || window.Continuation == ProgramCardContinuation.Slash && !IsSlashCard(use.CardKind) ||
            window.Continuation == ProgramCardContinuation.FinalizedTrick && window.TrickContinuation is null ||
            window.Continuation == ProgramCardContinuation.FinalizedSimpleCard && window.SimpleContinuation is null ||
            old is not null && (old.ActionId != r.ActionId || old.Started > 0 || old.Grants.Any(g => g.Source.OwnerSeat == f.OwnerSeat)))
            throw new InvalidOperationException("Zixi cannot issue another effect without the original accepted native continuation.");
        var simple = window.SimpleContinuation is { } s ? s with
        { RecoveryPolicySources = s.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null } : null;
        var native = old?.Native ?? new ExtraCardEffectState { Source = r.Source, ProgramFrameId = f.Id, ActionId = r.ActionId,
            PaidCardId = 0, Trick = window.TrickContinuation, Simple = simple };
        UpdateLifecycleCardUse(use.Id, current => current with { ZixiEffectRounds = new(r.ActionId, native)
        { Grants = (old?.Grants ?? []).Append(new(f.Id, r.Source, r.GameplayHash, r.WindowId, r.TargetSeat, 1)).ToArray() } });
        AdvanceEventRulesAndQueueFact(new ZixiExtraEffectIssuedEvent(f.Id, r.Source, r.GameplayHash, r.UseId, r.ActionId, r.TargetSeat, 1));
    }
    private bool HasUnstartedZixiExtraEffect(long id) => LifecycleCardUse(id) is { ZixiEffectRounds: { } r, Action: { } a } use &&
        a.ActionId == r.ActionId && r.Started < r.Grants.Count && !IsNamedAmbushUseIneffective(use) && _winner == Winner.None;
    private bool IsZixiExtraEffectRound(long id) => LifecycleCardUse(id)?.ZixiEffectRounds is { Started: > 0 };
    private bool IsTargetScopedZixiEffectRound(long id) => LifecycleCardUse(id) is
        { ZixiEffectRounds: { Active: true, Started: > 0 } r, Action: { } a } && r.ActionId == a.ActionId;
    private void CompleteZixiExtraEffectRound(long id)
    {
        if (LifecycleCardUse(id) is not { ZixiEffectRounds: { Active: true, Started: > 0 } r, Action: { } a } use) return;
        if (_resolutionStack.LastOrDefault()?.Id != id || use.NativeDraw is not null || use.NativeCardEffectDraw is not null ||
            use.WumiaoFlood is not null || use.DragonBoatCardEffect is not null || use.GuanduCardEffect is not null)
            throw new InvalidOperationException("Zixi cannot retire its target-local effect before the actual native children finish.");
        UpdateLifecycleCardUse(id, current => current with { ZixiEffectRounds = r with { Active = false },
            TargetIndex = current.TargetSeats.Count, SequentialTrick = null, AdjustedSimpleContinuation = null });
        AdvanceEventRulesAndQueueFact(new ZixiExtraEffectRoundCompletedEvent(id, a.ActionId,
            r.Grants[r.Started - 1].FrameId, r.Started, r.ActiveTargetSeat));
    }
    private void RecordZixiExtraEffectRedirect(ProgramSkillFrame f, long useId, int previous, int target)
    {
        if (LifecycleCardUse(useId) is not { ZixiEffectRounds: { } rounds, Action: { } a } use) return;
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } id } context ||
            id.ParentCardUseFrameId != use.Id || id.CardActionId != a.ActionId || f.OwnerSeat != previous ||
            context.TargetSeat != previous || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != target)
            throw new InvalidOperationException("Zixi's retained target may follow only this actual native Slash redirect.");
        var affected = rounds.Grants.Where(g => g.CurrentTargetSeat == previous).ToArray();
        if (affected.Length == 0) return;
        UpdateLifecycleCardUse(useId, current => current with { ZixiEffectRounds = rounds with
        { Grants = rounds.Grants.Select(g => g.CurrentTargetSeat == previous ? g with { CurrentTargetSeat = target } : g).ToArray() } });
        foreach (var grant in affected) AdvanceEventRulesAndQueueFact(new ZixiExtraEffectRedirectedEvent(useId, a.ActionId,
            grant.FrameId, f.Id, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            use.TargetIndex, previous, target));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 28720)]
    private void FollowZixiSoleTargetSubstitution(IGameEvent payload)
    {
        if (payload is not SoleTargetReplacedEvent e || LifecycleCardUse(e.CardUseFrameId) is not
            { ZixiEffectRounds: { Started: 0 } r, Action: { } a } use || e.ActionId != a.ActionId ||
            !IsSoleTargetReplacementFact(use, e, e.BeforeTargets)) return;
        UpdateLifecycleCardUse(use.Id, current => current with { ZixiEffectRounds = r with { Grants = r.Grants.Select(g =>
            g.CurrentTargetSeat == e.PreviousTargetSeat ? g with { CurrentTargetSeat = e.NewTargetSeat } : g).ToArray() } });
    }
    private bool IsZixiExtraEffectTargetSubset(CardUseFrame use, IReadOnlyList<int> targets) =>
        use is { ZixiEffectRounds: { Active: true, Started: > 0 } r, Action: { } a } && r.ActionId == a.ActionId &&
        r.ActiveTargetIndex >= 0 && r.ActiveTargetIndex < use.TargetSeats.Count && use.TargetSeats[r.ActiveTargetIndex] == r.ActiveTargetSeat &&
        targets.SequenceEqual(use.CardKind == CardKind.BorrowedSword ? use.TargetSeats.Skip(r.ActiveTargetIndex).Take(2) : [r.ActiveTargetSeat]) &&
        ProgramEventHistory<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId &&
            e.FrameId == r.Grants[r.Started - 1].FrameId && e.Round == r.Started && e.TargetSeat == r.ActiveTargetSeat) == 1;
    private void CaptureZixiExtraEffectAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { ZixiEffectRounds: { Started: 0 } rounds, CardAttack: { } original } use ||
            !IsSlashCard(use.CardKind) || rounds.Native.OriginalAttacks.Any(s => s.TargetIndex == use.TargetIndex)) return;
        UpdateLifecycleCardUse(use.Id, current => current with { ZixiEffectRounds = rounds with { Native = rounds.Native with
        { OriginalAttacks = rounds.Native.OriginalAttacks.Append(new(use.TargetIndex, original)).ToArray(),
            Fangtian = current.Continuations.FangtianHalberd ?? rounds.Native.Fangtian } } });
    }
    private bool TryBeginZixiExtraEffectRound(long id, bool afterAttack)
    {
        if (!HasUnstartedZixiExtraEffect(id) || LifecycleCardUse(id) is not { ZixiEffectRounds: { } r, Action: { } a } use ||
            use.ExtraCardEffect is { WholeGroupRepeated: false } || HasUnstartedFeiYiExtraEffect(id) ||
            r.Native.Trick is not null && HasRemainingSequentialTrickTargets(id) ||
            r.Native.Simple is not null && HasRemainingAdjustedSimpleTargets(id)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("Zixi cannot repeat an effect before the whole native child returns.");
        var previous = NativeExtraEffectState(use); var grant = r.Grants[r.Started];
        var targetIndex = Array.IndexOf(use.TargetSeats.ToArray(), grant.CurrentTargetSeat);
        if (use.CardKind == CardKind.BorrowedSword && targetIndex % 2 != 0) targetIndex = -1;
        var native = r.Native with { WholeGroupRepeated = true,
            MaterialClaims = previous?.MaterialClaims ?? r.Native.MaterialClaims,
            FrozenGroupDamageSourceSeat = previous?.FrozenGroupDamageSourceSeat ?? r.Native.FrozenGroupDamageSourceSeat };
        ReplaceRuntimeFrame(id, use with { ZixiEffectRounds = r with { Started = r.Started + 1, Native = native,
                Active = true, ActiveTargetIndex = targetIndex, ActiveTargetSeat = grant.CurrentTargetSeat },
            TargetIndex = targetIndex < 0 ? use.TargetSeats.Count : targetIndex, Step = ResolutionFrameStep.ResolvingEffect,
            SequentialTrick = null, AdjustedSimpleContinuation = null, FireAttackSelection = null,
            ColorFireAttack = null, UnexpectedAssaultReveal = null, VirtualBasicEffectApplied = false });
        AdvanceEventRulesAndQueueFact(new ZixiExtraEffectRoundStartedEvent(id, a.ActionId, grant.FrameId, r.Started + 1, grant.CurrentTargetSeat));
        if (targetIndex < 0 || !_players[grant.CurrentTargetSeat].IsAlive)
        { CompleteZixiExtraEffectRound(id); return false; }
        AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id, a.ActionId, targetIndex, grant.CurrentTargetSeat, use.CardKind));
        if (afterAttack) { ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision(); }
        if (IsSlashCard(use.CardKind))
        {
            var first = native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == targetIndex &&
                s.Attack.TargetSeat == grant.CurrentTargetSeat && !IsCardEffectIneffective(id, s.Attack.TargetSeat));
            // A redirected native target stays redirected; the original issuer,
            // judgment count and physical payment remain this same one Use.
            if (first is null) { CompleteZixiExtraEffectRound(id); return false; }
            var prepared = use.PreparedTargetAttacks?.Select((state, index) =>
                native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == index)?.Attack ?? state).ToArray();
            UpdateLifecycleCardUse(id, current => current with { TargetIndex = first.TargetIndex,
                PreparedTargetAttacks = prepared is null ? null : Array.AsReadOnly(prepared) });
            // This one target's extra effect must not restart a Fangtian/full
            // target walker belonging to the card's ordinary resolution.
            ActiveFangtianHalberd = null;
            UpdateCardAttackState(id, _ => ResetExtraCardEffectAttack(first.Attack));
            var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack; ContinueSlashDefense(attack); return true;
        }
        if (native.Trick is { } trick)
        {
            ActiveGroupCard = null; ActiveBorrowedSword = null;
            var card = GetTrickRepresentation(id, use.CardId, requireProcessing: true);
            IReadOnlyList<int> targets = use.CardKind == CardKind.BorrowedSword
                ? use.TargetSeats.Skip(targetIndex).Take(2).ToArray() : [grant.CurrentTargetSeat];
            BeginNullificationWindow(id, card, use.SourceSeat, targets, trick.ActionKind, null, trick.RequiredCardKind, use.CardKind); return true;
        }
        if (native.Simple is { } simple)
        {
            ContinueSimpleCardUse(id, simple); return true;
        }
        throw new InvalidOperationException("Zixi lost the original native card effect continuation.");
    }
    private bool ValidZixiGrantTarget(CardUseFrame use, ZixiExtraEffectGrant grant)
    {
        var current = grant.TargetSeat; var issued = false;
        foreach (var fact in CompleteProgramEventHistory())
        {
            if (fact is ZixiExtraEffectIssuedEvent started && started.FrameId == grant.FrameId) { issued = true; continue; }
            if (!issued) continue;
            if (fact is ZixiExtraEffectRedirectedEvent redirected && redirected.FrameId == grant.FrameId)
            {
                if (redirected.UseId != use.Id || redirected.ActionId != use.Action!.ActionId || redirected.PreviousSeat != current ||
                    redirected.Source.OwnerSeat != current || !IsValidPlayerSeat(redirected.TargetSeat) ||
                    _contentRegistry.GetSkill(redirected.Source.SkillId).Program is not { } p || p.GameplayHash != redirected.GameplayHash ||
                    !p.Triggers.Any(t => t.Id == redirected.Source.BindingId && t.Window == SkillProgramTriggerWindow.SlashTargetRedirecting &&
                        t.Effects.Any(e => e.Op == SkillProgramEffectOp.RedirectCurrentAttack)) ||
                    ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == redirected.RedirectFrameId &&
                        e.SkillId == redirected.Source.SkillId && e.SkillInstanceId == redirected.Source.SkillInstanceId &&
                        e.BindingId == redirected.Source.BindingId && e.OwnerSeat == current && e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) != 1) return false;
                current = redirected.TargetSeat;
            }
            else if (fact is SoleTargetReplacedEvent replaced && replaced.CardUseFrameId == use.Id && replaced.PreviousTargetSeat == current)
            { if (!IsSoleTargetReplacementFact(use, replaced, replaced.BeforeTargets)) return false; current = replaced.NewTargetSeat; }
        }
        return issued && current == grant.CurrentTargetSeat;
    }
    private void AssertZixiExtraEffectRounds()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.ZixiEffectRounds is not null))
        {
            var r = use.ZixiEffectRounds!;
            var starts = ProgramEventHistory<ZixiExtraEffectRoundStartedEvent>().Where(e => e.UseId == use.Id && e.ActionId == r.ActionId).ToArray();
            var completions = ProgramEventHistory<ZixiExtraEffectRoundCompletedEvent>().Where(e => e.UseId == use.Id && e.ActionId == r.ActionId).ToArray();
            if (use.Action is not { Type: CardActionType.Use } a || a.ActionId != r.ActionId || !ZixiEffectsKind(use.CardKind) ||
                r.Started < 0 || r.Started > r.Grants.Count || r.Grants.Count == 0 ||
                r.Grants.Select(g => g.FrameId).Distinct().Count() != r.Grants.Count ||
                r.Grants.Select(g => g.Source.OwnerSeat).Distinct().Count() != r.Grants.Count ||
                r.Native.ActionId != r.ActionId || r.Native.WholeGroupRepeated != (r.Started > 0) ||
                r.Started == 0 && (r.Active || r.ActiveTargetIndex != -1 || r.ActiveTargetSeat != -1) ||
                r.Started > 0 && (starts.Length != r.Started || r.ActiveTargetSeat != starts[^1].TargetSeat ||
                    r.ActiveTargetIndex != Array.IndexOf(use.TargetSeats.ToArray(), r.ActiveTargetSeat) ||
                    r.ActiveTargetSeat != r.Grants[r.Started - 1].CurrentTargetSeat) ||
                r.Active && (r.Started == 0 || r.ActiveTargetIndex < 0 || r.ActiveTargetIndex >= use.TargetSeats.Count ||
                    use.TargetSeats[r.ActiveTargetIndex] != r.ActiveTargetSeat || !IsZixiExtraEffectTargetSubset(use,
                        use.CardKind == CardKind.BorrowedSword ? use.TargetSeats.Skip(r.ActiveTargetIndex).Take(2).ToArray() : [r.ActiveTargetSeat])) ||
                r.Native.OriginalAttacks.Select(s => s.TargetIndex).Distinct().Count() != r.Native.OriginalAttacks.Count ||
                r.Native.OriginalAttacks.Any(s => s.TargetIndex < 0 || s.TargetIndex >= use.TargetSeats.Count) ||
                r.Grants.Any(g => g.JudgmentCount != 1 || !ValidZixiGrantTarget(use, g) || g.Source.SkillId != "le:zixi" || g.Source.BindingId != "unique-target-judgment-effects" ||
                    _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash != g.GameplayHash ||
                    ProgramEventHistory<ZixiEffectsStartedEvent>().Count(e => e.FrameId == g.FrameId && e.Source == g.Source && e.GameplayHash == g.GameplayHash &&
                        e.UseId == use.Id && e.ActionId == a.ActionId && e.WindowId == g.WindowId && e.TargetSeat == g.TargetSeat && e.JudgmentCount == 1 && e.CardKind == use.CardKind) != 1 ||
                    ProgramEventHistory<ZixiExtraEffectIssuedEvent>().Count(e => e.FrameId == g.FrameId && e.Source == g.Source && e.GameplayHash == g.GameplayHash &&
                        e.UseId == use.Id && e.ActionId == a.ActionId && e.TargetSeat == g.TargetSeat && e.JudgmentCount == 1) != 1) ||
                !Enumerable.Range(1, r.Started).SequenceEqual(starts.Select(e => e.Round)) ||
                starts.Any(e => e.Round < 1 || e.Round > r.Grants.Count || e.FrameId != r.Grants[e.Round - 1].FrameId || !IsValidPlayerSeat(e.TargetSeat)) ||
                !Enumerable.Range(1, r.Started - (r.Active ? 1 : 0)).SequenceEqual(completions.Select(e => e.Round)) ||
                completions.Any(e => !starts.Any(s => e == new ZixiExtraEffectRoundCompletedEvent(s.UseId, s.ActionId, s.FrameId, s.Round, s.TargetSeat))))
                throw new InvalidOperationException("Zixi lost its accepted original Use, once-issued judgment-count grant or ordered native repeated effect.");
        }
    }
}
