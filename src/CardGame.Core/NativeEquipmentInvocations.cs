using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum NativeEquipmentInvocationKind { Bagua, FactionBagua, IceSword, StoneAxe, QilinBow, Cixiong, Qinglong }
public sealed record NativeEquipmentInvocationReceipt(long ParentFrameId, long AttackId,
    NativeEquipmentInvocationKind Kind, int ActorSeat, int TargetSeat, string SkillKey, string OccurrenceKey,
    int EquipmentCardId = 0, int SelectedCardId = 0, CardKind? EffectiveSlashKind = null,
    int PhysicalOwnerSeat = -1, long? FactionRequestId = null, CardConversionSource? ConversionSource = null,
    Card? SlashCard = null, int ProviderRewardCount = 0, string CommitmentNonce = "")
{
    private IReadOnlyList<int> _materials = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(Array.Empty<CardLocation>());
    public IReadOnlyList<int> PhysicalCardIds { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> PhysicalFrom { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
}
public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeEquipmentInvocationReceipt? NativeEquipmentInvocation { get; init; }
}
// The event stores only scalar proof. The receipt's nested materials are frozen
// on the original owning frame; the fingerprint binds those IDs to issuance.
public sealed record NativeEquipmentInvocationIssuedEvent(long ParentFrameId, long AttackId,
    NativeEquipmentInvocationKind Kind, int ActorSeat, int TargetSeat, string SkillKey,
    string OccurrenceKey, string Fingerprint) : IGameEvent;
public sealed record NativeEquipmentInvocationReturnedEvent(long InvocationId, long ParentFrameId,
    long AttackId, NativeEquipmentInvocationKind Kind, string OccurrenceKey, bool Canceled) : IGameEvent;

public sealed partial class GameEngine
{
    private static CardKind NativeInvocationEquipment(NativeEquipmentInvocationKind kind) => kind switch
    {
        NativeEquipmentInvocationKind.Bagua or NativeEquipmentInvocationKind.FactionBagua => CardKind.BaguaFormation,
        NativeEquipmentInvocationKind.IceSword => CardKind.IceSword,
        NativeEquipmentInvocationKind.StoneAxe => CardKind.StoneAxe,
        NativeEquipmentInvocationKind.QilinBow => CardKind.QilinBow,
        NativeEquipmentInvocationKind.Cixiong => CardKind.CixiongDoubleSwords,
        NativeEquipmentInvocationKind.Qinglong => CardKind.QinglongCrescentBlade,
        _ => throw new InvalidOperationException("Unknown native equipment invocation.")
    };
    private static string NativeEquipmentFingerprint(NativeEquipmentInvocationReceipt r) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(r))));
    private NativeEquipmentInvocationReceipt CaptureNativeEquipmentInvocation(CardAttackHandle attack,
        NativeEquipmentInvocationKind kind, int actorSeat, int targetSeat)
    {
        var parent = _resolutionStack.LastOrDefault() ?? throw new InvalidOperationException("Equipment invocation requires a live native owner.");
        var equipment = NativeInvocationEquipment(kind);
        var cardId = GetEquipment(_players[actorSeat]).FirstOrDefault(c => c.Kind == equipment)?.Id ?? 0;
        var ordinal = ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().Count(e => e.ParentFrameId == parent.Id && e.Kind == kind && e.ActorSeat == actorSeat);
        var nonce = $"{_random.Next(int.MaxValue):X8}{_random.Next(int.MaxValue):X8}";
        return new(parent.Id, attack.ResolutionId, kind, actorSeat, targetSeat, CardCatalog.Get(equipment).DisplayName,
            $"native-equipment:{attack.ResolutionId}:{kind}:{actorSeat}:{ordinal}", cardId, CommitmentNonce: nonce);
    }
    private bool TryBeginNativeEquipmentInvocation(NativeEquipmentInvocationReceipt receipt)
    {
        if (!HasSkillInvocationObservers) return false;
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not { } parent || parent.Id != receipt.ParentFrameId ||
            parent.NativeEquipmentInvocation is not null || !IsValidPlayerSeat(receipt.ActorSeat) || !IsValidPlayerSeat(receipt.TargetSeat) ||
            !_players[receipt.ActorSeat].IsAlive || receipt.PhysicalCardIds is not System.Collections.IList { IsReadOnly: true })
            throw new InvalidOperationException("A native equipment activation requires its original clear owner and immutable accepted materials.");
        ReplaceRuntimeTop(parent with { NativeEquipmentInvocation = receipt });
        AdvanceEventRulesAndQueueFact(new NativeEquipmentInvocationIssuedEvent(receipt.ParentFrameId, receipt.AttackId, receipt.Kind,
            receipt.ActorSeat, receipt.TargetSeat, receipt.SkillKey, receipt.OccurrenceKey, NativeEquipmentFingerprint(receipt)));
        if (!TryBeginSkillInvocation(parent.Id, receipt.ActorSeat, receipt.SkillKey, receipt.OccurrenceKey,
                returnKind: SkillInvocationReturnKind.NativeEquipment))
            throw new InvalidOperationException("A newly issued equipment occurrence did not acquire its exact invocation child.");
        return true;
    }
    private bool TryBeginBaguaEquipmentInvocation(CardAttackHandle attack, CharacterState defender, bool factionDefense = false)
    {
        if (!HasSkillInvocationObservers) return false;
        return TryBeginNativeEquipmentInvocation(CaptureNativeEquipmentInvocation(attack,
            factionDefense ? NativeEquipmentInvocationKind.FactionBagua : NativeEquipmentInvocationKind.Bagua, defender.Seat, attack.TargetSeat));
    }
    private bool TryBeginIceSwordEquipmentInvocation(IceSwordHandle pending) => !HasSkillInvocationObservers ? false :
        TryBeginNativeEquipmentInvocation(CaptureNativeEquipmentInvocation(pending.Attack, NativeEquipmentInvocationKind.IceSword, pending.Attack.SourceSeat, pending.Attack.TargetSeat));
    private bool TryBeginStoneAxeEquipmentInvocation(CardAttackHandle attack) => !HasSkillInvocationObservers ? false :
        TryBeginNativeEquipmentInvocation(CaptureNativeEquipmentInvocation(attack, NativeEquipmentInvocationKind.StoneAxe, attack.SourceSeat, attack.TargetSeat));
    private bool TryBeginQilinBowEquipmentInvocation(CardAttackHandle attack, Card discardedMount) => !HasSkillInvocationObservers ? false :
        TryBeginNativeEquipmentInvocation(CaptureNativeEquipmentInvocation(attack, NativeEquipmentInvocationKind.QilinBow, attack.SourceSeat, attack.TargetSeat) with { SelectedCardId = discardedMount.Id });
    private bool TryBeginCixiongEquipmentInvocation(CixiongDoubleSwordsHandle pending) => !HasSkillInvocationObservers ? false :
        TryBeginNativeEquipmentInvocation(CaptureNativeEquipmentInvocation(pending.Attack, NativeEquipmentInvocationKind.Cixiong, pending.Attack.SourceSeat, pending.Attack.TargetSeat));
    private bool TryBeginQinglongEquipmentInvocation(QinglongCrescentBladeHandle pending, Card slash,
        CardKind effectiveSlashKind, int physicalOwnerSeat, IReadOnlyList<Card> physicalCards,
        FactionCardRequestHandle? jijiang = null, CardConversionSource? conversionSource = null)
    {
        if (!HasSkillInvocationObservers) return false;
        var selected = conversionSource ?? _selectedUseConversion ?? _selectedResponseConversion;
        var r = CaptureNativeEquipmentInvocation(pending.Attack, NativeEquipmentInvocationKind.Qinglong, pending.Attack.SourceSeat, pending.Attack.TargetSeat) with
        { SlashCard = slash, SelectedCardId = slash.Id, EffectiveSlashKind = effectiveSlashKind, PhysicalOwnerSeat = physicalOwnerSeat,
            PhysicalCardIds = physicalCards.Select(c => c.Id).ToArray(), PhysicalFrom = physicalCards.Select(c => _cardZones.GetLocation(c.Id)).ToArray(), FactionRequestId = jijiang?.OwnerFrameId,
            ProviderRewardCount = jijiang is null ? 0 : CaptureFactionProviderReward(jijiang).DrawCount, ConversionSource = selected };
        ClearPendingDecision();
        return TryBeginNativeEquipmentInvocation(r);
    }
    private sealed record NativeEquipmentSuspensionProjection(ResolutionFrame Parent, CardUseFrame AttackRoot,
        SkillInvocationFrame Invocation, NativeEquipmentInvocationReceipt Receipt, int ParentIndex);

    // This is a structural projection for the suspended native host only. The
    // real child decision and any child dying/judgment frame keep their identity.
    private NativeEquipmentSuspensionProjection? FindNativeEquipmentInvocation(long attackId)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            var parent = _resolutionStack[index];
            if (parent.NativeEquipmentInvocation is not { } r || r.AttackId != attackId || r.ParentFrameId != parent.Id ||
                !ValidNativeEquipmentReceipt(r) || _resolutionStack[index + 1] is not SkillInvocationFrame invocation ||
                invocation.ReturnKind != SkillInvocationReturnKind.NativeEquipment || invocation.ParentFrameId != parent.Id ||
                invocation.ActorSeat != r.ActorSeat || invocation.SkillKey != r.SkillKey || invocation.OccurrenceKey != r.OccurrenceKey ||
                !SkillInvocationStructuralEdge(parent, invocation) ||
                ProgramEventHistory<NativeEquipmentInvocationReturnedEvent>().Any(e => e.InvocationId == invocation.Id) ||
                ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().Count(e => e == new NativeEquipmentInvocationIssuedEvent(parent.Id, r.AttackId,
                    r.Kind, r.ActorSeat, r.TargetSeat, r.SkillKey, r.OccurrenceKey, NativeEquipmentFingerprint(r))) != 1) continue;
            var root = parent as CardUseFrame;
            if (r.Kind == NativeEquipmentInvocationKind.FactionBagua && index > 0 && parent is ResponseWindowFrame response &&
                response.ParentFrameId == attackId && _resolutionStack[index - 1] is CardUseFrame responseRoot)
                root = responseRoot;
            if (root is null || root.Id != attackId || !NativeEquipmentHostMatches(parent, root, r) ||
                !HasObserverSuffix(index, static (engine, child) => engine.NativeEquipmentObserverEdge(child),
                    static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying))) continue;
            return new(parent, root, invocation, r, index);
        }
        return null;
    }
    private static bool ValidNativeEquipmentReceipt(NativeEquipmentInvocationReceipt r) =>
        Enum.IsDefined(r.Kind) && r.PhysicalCardIds is System.Collections.IList { IsReadOnly: true } &&
        r.PhysicalFrom is System.Collections.IList { IsReadOnly: true } && r.PhysicalFrom.Count == r.PhysicalCardIds.Count &&
        r.PhysicalCardIds.Distinct().Count() == r.PhysicalCardIds.Count && r.CommitmentNonce.Length == 16 &&
        r.CommitmentNonce.All(Uri.IsHexDigit) && r.SkillKey == CardCatalog.Get(NativeInvocationEquipment(r.Kind)).DisplayName &&
        !string.IsNullOrWhiteSpace(r.OccurrenceKey);
    private bool NativeEquipmentHostMatches(ResolutionFrame parent, CardUseFrame root, NativeEquipmentInvocationReceipt r)
    {
        if (root.CardAttack is not { } attack || !IsValidPlayerSeat(r.ActorSeat) || !IsValidPlayerSeat(r.TargetSeat) ||
            attack.TargetSeat != r.TargetSeat || attack.EffectiveCardKind != root.CardKind ||
            !IsValidPlayerSeat(attack.SourceSeat) ||
            (r.Kind is NativeEquipmentInvocationKind.Bagua or NativeEquipmentInvocationKind.FactionBagua
                ? !IsSlashCard(root.CardKind) && root.CardKind != CardKind.ArrowBarrage
                : !IsSlashCard(root.CardKind))) return false;
        var c = root.Continuations;
        if (r.Kind == NativeEquipmentInvocationKind.FactionBagua)
            return parent is ResponseWindowFrame { Step: ResolutionFrameStep.AwaitingResponse } response &&
                response.ParentFrameId == root.Id && response.SourceSeat == attack.SourceSeat && response.IncomingCard == root.CardKind &&
                response.ResponderSeat == attack.TargetSeat && root.Step == ResolutionFrameStep.AwaitingResponse && !attack.IgnoresArmor &&
                attack.FactionDefenseAttempted && c.FactionDefense is { Active: true } defense && defense.OwnerSeat == attack.TargetSeat &&
                defense.CandidateIndex >= 0 && defense.CandidateIndex < defense.CandidateSeats.Count &&
                defense.CandidateSeats[defense.CandidateIndex] == r.ActorSeat;
        if (parent.Id != root.Id || parent is not CardUseFrame) return false;
        if (r.Kind == NativeEquipmentInvocationKind.Bagua)
            return r.ActorSeat == attack.TargetSeat && !attack.IgnoresArmor && root.Step == ResolutionFrameStep.ResolvingEffect;
        if (r.ActorSeat != attack.SourceSeat) return false;
        return r.Kind switch
        {
            NativeEquipmentInvocationKind.IceSword => root.Step == ResolutionFrameStep.AwaitingResponse && attack.IceSwordAttempted &&
                c.IceSword is { Active: true, Activated: true, DiscardedCardIds.Count: 0 },
            NativeEquipmentInvocationKind.StoneAxe => root.Step == ResolutionFrameStep.ResolvingEffect && c.StoneAxe is not { Active: true } &&
                ProgramEventHistory<StoneAxeResolvedEvent>().LastOrDefault(e => e.ResolutionId == root.Id) is
                    { Used: true, DiscardedCardIds.Count: 2 } paid && paid.SourceSeat == attack.SourceSeat && paid.TargetSeat == attack.TargetSeat &&
                paid.DiscardedCardIds.Distinct().Count() == 2,
            NativeEquipmentInvocationKind.QilinBow => root.Step == ResolutionFrameStep.ResolvingEffect && attack.QilinBowAttempted &&
                c.QilinBow is not { Active: true } && r.SelectedCardId > 0,
            NativeEquipmentInvocationKind.Cixiong => root.Step == ResolutionFrameStep.Declared &&
                c.CixiongDoubleSwords is { Active: true, Stage: CixiongDoubleSwordsStage.TargetChoice },
            NativeEquipmentInvocationKind.Qinglong => root.Step == ResolutionFrameStep.AwaitingResponse &&
                c.QinglongCrescentBlade is { Active: true } && r.SlashCard is not null && r.SelectedCardId == r.SlashCard.Id &&
                r.EffectiveSlashKind is { } effective && IsSlashCard(effective) && IsValidPlayerSeat(r.PhysicalOwnerSeat),
            _ => false
        };
    }
    private bool NativeEquipmentObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        // Invocation-owned costs/returns are stricter than the generic movement
        // edges; an unrecognized child of that native frame must be rejected.
        if (parent is SkillInvocationFrame || child is SkillInvocationFrame)
            return SkillInvocationStructuralEdge(parent, child);
        return NativeDrawAttemptRidesOn(child, parent) || IsMovementObserverEdge(index);
    }
    private bool HasNativeEquipmentInvocation(long attackId) => FindNativeEquipmentInvocation(attackId) is { AttackRoot.CardAttack.Active: true } &&
        ActiveCardAttack?.ResolutionId == attackId;
    private ResolutionFrame? NativeEquipmentInvariantTop(ResolutionFrame? actualTop)
    {
        if (actualTop?.Id != _resolutionStack.LastOrDefault()?.Id || actualTop is DyingFrame or JudgmentFrame or CardUseFrame) return actualTop;
        return ActiveCardAttack is { } attack && FindNativeEquipmentInvocation(attack.ResolutionId) is { } projection ? projection.Parent : actualTop;
    }
    private bool HasFactionDefenseEquipmentInvocation(FactionDefenseHandle defense) =>
        FindNativeEquipmentInvocation(defense.OwnerFrameId) is { Receipt.Kind: NativeEquipmentInvocationKind.FactionBagua } projection &&
        projection.Parent is ResponseWindowFrame response && response.ParentFrameId == defense.OwnerFrameId &&
        response.ResponderSeat == defense.OwnerSeat && projection.Receipt.ActorSeat == defense.CurrentCandidateSeat &&
        projection.Receipt.TargetSeat == defense.OwnerSeat && HasNativeEquipmentInvocation(defense.OwnerFrameId);
    private bool HasQinglongFactionEquipmentInvocation(FactionCardRequestHandle request) =>
        request.IsQinglongCrescentBladeUse && !request.AwaitingProviders && !request.AwaitingZhuqueFanChoice && request.ActiveAttack is null &&
        request.QinglongCrescentBlade is { } qinglong && FindNativeEquipmentInvocation(qinglong.OwnerFrameId) is
            { Receipt.Kind: NativeEquipmentInvocationKind.Qinglong } projection &&
        projection.Receipt.FactionRequestId == request.OwnerFrameId && projection.Receipt.ActorSeat == request.OwnerSeat &&
        projection.Receipt.PhysicalOwnerSeat == request.CurrentCandidateSeat && projection.Receipt.TargetSeat == request.TargetSeat &&
        HasNativeEquipmentInvocation(qinglong.OwnerFrameId);
    private void AssertNativeEquipmentInvocations()
    {
        foreach (var parent in _resolutionStack.Where(f => f.NativeEquipmentInvocation is not null))
        {
            var r = parent.NativeEquipmentInvocation!;
            if (r.ParentFrameId != parent.Id || !Enum.IsDefined(r.Kind) || !IsValidPlayerSeat(r.ActorSeat) || !IsValidPlayerSeat(r.TargetSeat) ||
                r.PhysicalCardIds is not System.Collections.IList { IsReadOnly: true } || r.PhysicalFrom is not System.Collections.IList { IsReadOnly: true } ||
                r.PhysicalFrom.Count != r.PhysicalCardIds.Count || r.PhysicalCardIds.Distinct().Count() != r.PhysicalCardIds.Count || r.CommitmentNonce.Length != 16 ||
                ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().Count(e => e == new NativeEquipmentInvocationIssuedEvent(parent.Id, r.AttackId,
                    r.Kind, r.ActorSeat, r.TargetSeat, r.SkillKey, r.OccurrenceKey, NativeEquipmentFingerprint(r))) != 1 ||
                FindNativeEquipmentInvocation(r.AttackId) is not { } projection || projection.Parent.Id != parent.Id)
                throw new InvalidOperationException("Native equipment lost its original owned activation and exact typed invocation child.");
        }
    }
    private void ContinueNativeEquipmentInvocation(SkillInvocationFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not { NativeEquipmentInvocation: { } r } parent || parent.Id != frame.ParentFrameId ||
            frame.ReturnKind != SkillInvocationReturnKind.NativeEquipment || frame.ActorSeat != r.ActorSeat || frame.SkillKey != r.SkillKey || frame.OccurrenceKey != r.OccurrenceKey ||
            ProgramEventHistory<NativeEquipmentInvocationIssuedEvent>().Count(e => e == new NativeEquipmentInvocationIssuedEvent(parent.Id, r.AttackId,
                r.Kind, r.ActorSeat, r.TargetSeat, r.SkillKey, r.OccurrenceKey, NativeEquipmentFingerprint(r))) != 1 ||
            ProgramEventHistory<NativeEquipmentInvocationReturnedEvent>().Any(e => e.InvocationId == frame.Id) ||
            !ProgramEventHistory<SkillInvocationCompletedEvent>().Any(e => e.FrameId == frame.Id && e.ParentFrameId == parent.Id && e.OccurrenceKey == r.OccurrenceKey))
            throw new InvalidOperationException("A native equipment activation lost its original exact invocation return.");
        var attack = new CardAttackHandle(this, r.AttackId);
        if (!SameAttackOwner(ActiveCardAttack, attack) || r.TargetSeat != attack.TargetSeat)
            throw new InvalidOperationException("Native equipment resumed a different attack or target.");
        var canceled = !_players[r.ActorSeat].IsAlive || !_players[r.TargetSeat].IsAlive || _winner != Winner.None;
        ReplaceRuntimeTop(parent with { NativeEquipmentInvocation = null });
        AdvanceEventRulesAndQueueFact(new NativeEquipmentInvocationReturnedEvent(frame.Id, parent.Id, r.AttackId, r.Kind, r.OccurrenceKey, canceled));
        if (canceled) { CancelNativeEquipmentContinuation(r, attack); return; }
        switch (r.Kind)
        {
            case NativeEquipmentInvocationKind.Bagua:
            case NativeEquipmentInvocationKind.FactionBagua:
            {
                var faction = r.Kind == NativeEquipmentInvocationKind.FactionBagua;
                if (faction && (ActiveFactionDefense is not { } defense || defense.CurrentCandidateSeat != r.ActorSeat || !SameAttackOwner(defense.Attack, attack)))
                    throw new InvalidOperationException("Faction Bagua lost its original provider cursor.");
                var result = BeginJudgment(attack, r.ActorSeat, JudgmentReasons.BaguaDefense, attack.ResolutionId, attack.EffectiveCardKind,
                    faction ? JudgmentContinuationKind.FactionDefenseBagua : JudgmentContinuationKind.Bagua);
                if (result is { } succeeded) { if (faction) CompleteFactionDefenseBaguaResponse(attack, succeeded); else CompleteBaguaResponse(attack, succeeded); }
                return;
            }
            case NativeEquipmentInvocationKind.IceSword:
                if (ActiveIceSword is not { Activated: true } ice || !SameAttackOwner(ice.Attack, attack)) throw new InvalidOperationException("Ice Sword lost its accepted activation.");
                if (GetIceSwordTargetCardCount(_players[r.TargetSeat], r.ActorSeat) == 0) FinishIceSwordPrevention(ice); else PublishIceSwordChoice(ice);
                return;
            case NativeEquipmentInvocationKind.StoneAxe:
                if (!ProgramEventHistory<StoneAxeResolvedEvent>().Any(e => e.ResolutionId == r.AttackId && e.Used && e.DiscardedCardIds.Count == 2)) throw new InvalidOperationException("Stone Axe lost its real paid two-card cost.");
                if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
                return;
            case NativeEquipmentInvocationKind.QilinBow:
                ContinueInvokedQilinBow(attack, GetQilinBowTargetMounts(_players[r.TargetSeat]).SingleOrDefault(c => c.Id == r.SelectedCardId));
                return;
            case NativeEquipmentInvocationKind.Cixiong:
                if (ActiveCixiongDoubleSwords is not { Stage: CixiongDoubleSwordsStage.TargetChoice } cixiong || !SameAttackOwner(cixiong.Attack, attack)) throw new InvalidOperationException("Cixiong lost its accepted target-choice stage.");
                PublishCixiongAcceptedTargetChoice(cixiong);
                return;
            case NativeEquipmentInvocationKind.Qinglong:
                ContinueInvokedQinglong(r, attack);
                return;
            default: throw new InvalidOperationException("No continuation for the accepted native equipment.");
        }
    }
    private void CancelNativeEquipmentContinuation(NativeEquipmentInvocationReceipt r, CardAttackHandle attack)
    {
        if (r.Kind == NativeEquipmentInvocationKind.FactionBagua && _players[attack.TargetSeat].IsAlive && _winner == Winner.None && ActiveFactionDefense is { } defense)
        { defense.CandidateIndex++; AdvanceFactionDefenseCandidate(); return; }
        if (ActiveIceSword is { } ice && SameAttackOwner(ice.Attack, attack)) ActiveIceSword = null;
        if (ActiveCixiongDoubleSwords is { } cixiong && SameAttackOwner(cixiong.Attack, attack)) ActiveCixiongDoubleSwords = null;
        if (ActiveQinglongCrescentBlade is { } qinglong && SameAttackOwner(qinglong.Attack, attack)) ActiveQinglongCrescentBlade = null;
        if (r.Kind == NativeEquipmentInvocationKind.Qinglong) RestoreFactionRequestAfterQinglongChoice(attack.ResolutionId);
        if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response && response.ParentFrameId == attack.ResolutionId) PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); CompleteAttack(attack);
    }
    private void PublishCixiongAcceptedTargetChoice(CixiongDoubleSwordsHandle pending)
    {
        var attack = pending.Attack; var source = _players[attack.SourceSeat]; var target = _players[attack.TargetSeat];
        if (!SameContinuationOwner(ActiveCixiongDoubleSwords, pending) || pending.Stage != CixiongDoubleSwordsStage.TargetChoice || _pendingDecision is not null)
            throw new InvalidOperationException("Cixiong target choice requires the exact already accepted activation.");
        var hand = GetHand(target).OrderBy(c => c.Id).ToArray();
        var choices = hand.Select(card => new PromptChoice(new($"cixiong.discard.card-{card.Id}"), $"弃置【{card.DisplayName}】（{card.RankText}）", [card.Id], [], new Dictionary<string,string> { ["action"] = "cixiong-discard" }))
            .Append(new(new("cixiong.allow-draw"), $"令 {source.Name} 摸一张牌。", [], [], new Dictionary<string,string> { ["action"] = "cixiong-draw" })).ToArray();
        _pendingDecision = new(DecisionKind.CixiongDoubleSwords, target.Seat, $"{source.Name} 发动【雌雄双股剑】，请选择弃置一张手牌，或令其摸一张牌。",
            hand.Select(c => c.Id).ToArray(), [], source.Seat, attack.EffectiveCardKind)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target.Seat, Choices = Array.AsReadOnly(choices) };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ContinueInvokedQilinBow(CardAttackHandle attack, Card? mount)
    {
        var source = _players[attack.SourceSeat]; var target = _players[attack.TargetSeat];
        if (mount is not null && !IsForeignEquipmentDiscardPrevented(source.Seat, mount, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard))
            MoveLogicalDiscard(source.Seat, source.Seat, mount, CardLocation.Equipment(target.Seat), CardMoveReasons.QilinBowDiscard, parentFrameId: attack.ResolutionId);
        else mount = null;
        AdvanceEventRulesAndQueueFact(new QilinBowResolvedEvent(attack.ResolutionId, source.Seat, target.Seat, mount is not null, mount?.Id));
        AddLog(mount is null ? "EquipmentSkipped" : "EquipmentEffect", mount is null ? $"{source.Name} 未发动【麒麟弓】，继续对 {target.Name} 结算伤害。" : $"{source.Name} 发动【麒麟弓】，弃置 {target.Name} 的【{mount.DisplayName}】。", source.Seat, target.Seat);
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }
    private void ContinueInvokedQinglong(NativeEquipmentInvocationReceipt r, CardAttackHandle attack)
    {
        var pending = ActiveQinglongCrescentBlade;
        if (pending is null || !SameAttackOwner(pending.Attack, attack) || r.SlashCard is null || r.EffectiveSlashKind is null || !IsValidPlayerSeat(r.PhysicalOwnerSeat))
            throw new InvalidOperationException("Qinglong lost its exact accepted Slash materials.");
        var materials = r.PhysicalCardIds.Select(GetAttackCard).ToArray();
        var provider = _players[r.PhysicalOwnerSeat];
        var legal = provider.IsAlive && materials.Select((c, i) => _cardZones.GetLocation(c.Id).OwnerSeat == r.PhysicalOwnerSeat && _cardZones.GetLocation(c.Id) == r.PhysicalFrom[i]).All(x => x) &&
            !IsCardUseForbidden(r.ActorSeat, r.EffectiveSlashKind.Value, CardActionType.Use) && !IsDirectedCardTargetProhibited(r.ActorSeat, r.TargetSeat, r.EffectiveSlashKind.Value);
        if (!legal) { CancelNativeEquipmentContinuation(r, attack); return; }
        FactionCardRequestHandle? jijiang = r.FactionRequestId is { } requestId ? new(this, requestId) : null;
        // Only this verified typed return may bypass the original invocation
        // gate. Actual card payment and Slash settlement remain native.
        BeginQinglongCrescentBladeFollowup(pending, r.SlashCard, r.EffectiveSlashKind.Value, r.PhysicalOwnerSeat, materials, jijiang, r.ConversionSource, skipInvocation: true);
        if (r.FactionRequestId is { } rewardedRequest && r.ProviderRewardCount > 0)
            RewardFactionRequestProvider(new(rewardedRequest, r.ProviderRewardCount), r.PhysicalOwnerSeat);
    }
}
