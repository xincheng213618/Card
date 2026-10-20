namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouJwFactionBind = "mou-jiang-wei-tiaoxin-faction";
    private bool CanRequestMouJwFaction(ProgramSkillFrame f)
    {
        if (f.MouJiangWei is not { Stage: MouJiangWeiStage.Responding, Accepted: true, FactionTried: false } r || r.Cursor >= r.Targets.Count || ActiveFactionCardRequest is not null || !ValidMouJw(f)) return false;
        var actor = r.Targets[r.Cursor];
        var source = _players[actor]; var target = _players[f.OwnerSeat];
        return source.IsAlive && target.IsAlive && GetFactionResponsePolicy(source, CardKind.Slash) is not null && !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) && !IsSlashProhibited(target) &&
            SlashKinds.Any(kind => !IsDirectedCardTargetProhibited(actor, target.Seat, kind) && !IsCardUseForbidden(actor, kind, CardActionType.Use) && TargetRules.AllowsSlash(new(source, target, kind, null, null)) && CanSpendSlashUse(source, target, ignoresCount: true, kind));
    }
    private void BeginMouJwFaction(ProgramSkillFrame f)
    {
        if (!CanRequestMouJwFaction(f)) throw new InvalidOperationException("Tiaoxin requires the responder's current legal native faction-request ability.");
        var r = f.MouJiangWei!; var actor = r.Targets[r.Cursor]; var policy = GetFactionResponsePolicy(_players[actor], CardKind.Slash)!;
        ReplaceRuntimeTop(f = f with { MouJiangWei = r with { Stage = MouJiangWeiStage.FactionRequest, FactionPolicy = policy, FactionTried = true } });
        AdvanceEventRulesAndQueueFact(new MouJiangWeiFactionRequestedEvent(f.Id, r.Cursor, actor, f.OwnerSeat, policy));
        var candidates = GetFactionProviderSeats(actor, policy.FactionId);
        ActiveFactionCardRequest = new FactionCardRequestHandle(this, f.Id, FactionCardRequestPurpose.AssistedProgramUse, actor, candidates, policy.FactionId, targetSeat: f.OwnerSeat, programSkillFrameId: f.Id, policySource: policy, assistedResultBind: MouJwFactionBind);
        ClearPendingDecision(); _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id, actor, candidates, policy.SkillId, IsActiveUse: true, f.OwnerSeat)); AdvanceFactionSlashCandidate();
    }
    private bool ValidateMouJwFactionParent(FactionCardRequestHandle pending, ProgramSkillFrame f)
    {
        if (f.MouJiangWei is not { FactionPolicy: { } policy } r) return false;
        if (!ValidMouJw(f) || !r.Accepted || r.Cursor >= r.Targets.Count || r.Stage is not (MouJiangWeiStage.FactionRequest or MouJiangWeiStage.Issuing or MouJiangWeiStage.NativeUse or MouJiangWeiStage.NativeReturned) || !pending.IsAssistedProgramUse || pending.OwnerFrameId != f.Id || pending.ResolutionId != f.Id || pending.ProgramSkillFrameId != f.Id || pending.OwnerSeat != r.Targets[r.Cursor] || pending.TargetSeat != f.OwnerSeat || pending.PolicySource != policy || pending.ProviderFactionId != policy.FactionId || pending.AssistedResultBind != MouJwFactionBind ||
            ProgramEventHistory<MouJiangWeiFactionRequestedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Cursor && e.ActorSeat == pending.OwnerSeat && e.TargetSeat == f.OwnerSeat && e.Policy == policy) != 1) throw new InvalidOperationException("Tiaoxin faction use changed its actual requester, original policy or owning target occurrence.");
        return true;
    }
    private bool MouJwFactionSupplyLegal(FactionCardRequestHandle pending, CardKind kind)
    {
        if (pending.ProgramSkillFrameId is not { } id || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is not { MouJiangWei.FactionPolicy: not null } f) return true;
        return ValidateMouJwFactionParent(pending, f) && !IsCardUseForbidden(pending.OwnerSeat, kind, CardActionType.Use) && !IsDirectedCardTargetProhibited(pending.OwnerSeat, f.OwnerSeat, kind) && !HasTurnCardTargetRestriction(pending.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly);
    }
    private bool HasMouJwFactionDistance(int actor, int target) => ActiveFactionCardRequest is { IsAssistedProgramUse: true, ProgramSkillFrameId: { } id } p && p.OwnerSeat == actor && p.TargetSeat == target &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is { MouJiangWei.FactionPolicy: not null } f && f.OwnerSeat == target && ValidateMouJwFactionParent(p, f);
    private bool PrepareMouJwFactionNative(ProgramSkillFrame f, FactionCardRequestHandle pending, CharacterState provider, IReadOnlyList<Card> cards, CardKind kind, CardConversionSource? conversion)
    {
        if (!ValidateMouJwFactionParent(pending, f)) return false;
        var r = f.MouJiangWei!; var actor = r.Targets[r.Cursor];
        if (r.Stage != MouJiangWeiStage.FactionRequest || cards.Count == 0 || cards.Select(c => c.Id).Distinct().Count() != cards.Count || provider.Seat == actor || !pending.CandidateSeats.Contains(provider.Seat) || cards.Any(c => _cardZones.GetLocation(c.Id).OwnerSeat != provider.Seat) || !IsSlashCard(kind) || IsCardUseForbidden(actor, kind, CardActionType.Use) || IsDirectedCardTargetProhibited(actor, f.OwnerSeat, kind)) throw new InvalidOperationException("Tiaoxin's real faction provider lost its paid material or the actual actor's target legality.");
        var action = new LegalAction(LegalActionKind.Slash, cards[0].Id, f.OwnerSeat, "通过实际势力响应使用杀", kind) { ConversionSource = conversion };
        ReplaceRuntimeTop(f with { MouJiangWei = r with { Stage = MouJiangWeiStage.Issuing, NativeAction = action, Materials = cards.Select(c => c.Id).ToArray(), ProviderSeat = provider.Seat } });
        AdvanceEventRulesAndQueueFact(new MouJiangWeiFactionProvidedEvent(f.Id, r.Cursor, actor, provider.Seat, kind, cards.Count)); return true;
    }
    private bool FinishMouJwFactionDeclined(long id, FactionCardRequestHandle pending)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouJiangWei.FactionPolicy: not null } f || f.Id != id) return false;
        if (!ValidateMouJwFactionParent(pending, f) || f.MouJiangWei is not { Stage: MouJiangWeiStage.FactionRequest } r) throw new InvalidOperationException("Tiaoxin faction failure has no matching unfinished request.");
        AdvanceEventRulesAndQueueFact(new MouJiangWeiFactionDeclinedEvent(f.Id, r.Cursor, r.Targets[r.Cursor]));
        ReplaceRuntimeTop(f = f with { MouJiangWei = r with { Stage = MouJiangWeiStage.Responding, FactionPolicy = null } }); ContinueMouJw(f.Id); return true;
    }
}
