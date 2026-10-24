using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryLiuFengOpen = "boundary-liu-feng-open";
    private IEnumerable<CardConversionSource> BoundaryLiuFengSources(int actor)
    {
        if (!_players[actor].IsAlive || _winner != Winner.None || !_setupComplete || _turnNumber <= 0 || BoundaryLiuFengPaidThisTurn(actor)) yield break;
        foreach (var i in GetSkillBindingShard(_players[actor]).ProgramInstances.Where(i => i.SkillId == BoundaryLiuFengComposition.Qinling))
            yield return new(i.SkillId, BoundaryLiuFengComposition.Activation, actor, i.SkillInstanceId);
    }
    private bool BoundaryLiuFengHasCost(int actor) => GetHand(_players[actor]).Concat(GetEquipment(_players[actor])).Any(c => !IsForeignEquipmentDiscardPrevented(actor, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard));
    private BoundaryLiuFengNeed? CaptureBoundaryLiuFengNeed(int actor, DecisionKind kind, ResolutionFrame? top)
    {
        if (top is null || !IsValidPlayerSeat(actor) || top is ProgramSkillFrame { BoundaryLiuFeng: not null }) return null;
        var owner = top is ResponseWindowFrame response ? response.ParentFrameId : top.Id;
        BoundaryLiuFengNeed N(BoundaryLiuFengIntent intent, int[] targets, SkillProgramEffectOp? op = null, int instruction = 0) => new(intent, owner, top.Id, LifecycleCardUse(owner)?.Action?.ActionId, actor, instruction, op, targets);
        // A provider supplies a card for somebody else's action; Qinling instead
        // makes this skill owner the real user. Pure Played requests are excluded.
        if (kind == DecisionKind.RespondSlash && ActiveFactionCardRequest is null && ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } b && b.ResolutionId == owner && b.WeaponOwnerSeat == actor) return N(BoundaryLiuFengIntent.BorrowedSword, [b.SlashTargetSeat]);
        if (kind == DecisionKind.QinglongCrescentBlade && ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == owner && q.Attack.SourceSeat == actor && SameAttackOwner(ActiveCardAttack, q.Attack)) return N(BoundaryLiuFengIntent.Qinglong, [q.Attack.TargetSeat]);
        if (kind != DecisionKind.ProgramTrigger || top is not ProgramSkillFrame p || GetPausedPrivateOfferEffect(p) is not { } effect) return null;
        int[]? targets = null; var intent = BoundaryLiuFengIntent.AssistedSlash;
        if (IsSayingSlashRequest(p) && TryGetSayingSlashRequest(p, out var sayingActor, out var sayingTargets) && sayingActor == actor) targets = sayingTargets.ToArray();
        else if (IsChanshuangSlashRequest(p) && TryGetChanshuangSlashRequest(p, out var chanshuangActor, out var chanshuangTargets) && chanshuangActor == actor) targets = chanshuangTargets.ToArray();
        else if (IsQigongRequest(p) && p.LvKuangLvXiang!.Actor == actor) targets = [p.LvKuangLvXiang.Target];
        else if (IsWuJingHejiRequest(p) && p.OwnerSeat == actor) targets = [p.WuJing!.TargetSeat];
        else if (IsCaoChunInitialRequest(p) && p.OwnerSeat == actor) targets = _players.Where(t => t.IsAlive && t.Seat != actor).Select(t => t.Seat).ToArray();
        else if (p.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicChoice, ActorSeat: var keActor } && keActor == actor && ValidKeBiNeng(p)) { targets = _players.Where(t => t.IsAlive && t.Seat != actor).Select(t => t.Seat).ToArray(); intent = BoundaryLiuFengIntent.GenericBasic; }
        else if (RoundOnceCardlessProgramActor(p, effect.Op, actor))
        {
            targets = RoundOnceCardlessProgramTargets(p, actor, effect.Op);
            intent = effect.Op switch { SkillProgramEffectOp.RequestSlashByTarget => BoundaryLiuFengIntent.ProgramSlash, SkillProgramEffectOp.RequestSlashByNearest => BoundaryLiuFengIntent.ProgramNearestSlash, SkillProgramEffectOp.RequestLegalSlashByNearest => BoundaryLiuFengIntent.NearestLegalSlash, SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner or SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash => BoundaryLiuFengIntent.FixedSlash, _ => BoundaryLiuFengIntent.AssistedSlash };
        }
        return targets is { Length: > 0 } ? N(intent, targets, effect.Op, p.InstructionIndex) : null;
    }
    private bool BoundaryLiuFengNeedCurrent(BoundaryLiuFengReceipt r) => r.Need is { } need && r.OriginalDecision is { } p && _resolutionStack.SingleOrDefault(f => f.Id == need.RequestFrameId) is { } parent && CaptureBoundaryLiuFengNeed(need.ActorSeat, p.Kind, parent) == need;
    private bool BoundaryLiuFengRequestTarget(BoundaryLiuFengNeed n, int target)
    {
        if (!_players[n.ActorSeat].IsAlive || !IsValidPlayerSeat(target) || !_players[target].IsAlive || target == n.ActorSeat || !n.Targets.Contains(target) || !TargetRules.AllowsNativeUseMaterial(new(n.ActorSeat, CardKind.Slash, [])) || IsCardUseForbidden(n.ActorSeat, CardKind.Slash, CardActionType.Use) || HasBeneficiarySuitShield(n.ActorSeat, target, Suit.None) || IsDirectedCardTargetProhibited(n.ActorSeat, target, CardKind.Slash) || IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None)) return false;
        var actor = _players[n.ActorSeat];
        if (n.Intent == BoundaryLiuFengIntent.BorrowedSword) return ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } b && b.ResolutionId == n.OwnerFrameId && GetWeapon(actor) is not null && IsLegalBorrowedSwordSlashTarget(actor, _players[target], CardKind.Slash, Suit.None, false, null, 0, []);
        if (n.Intent == BoundaryLiuFengIntent.Qinglong) return ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == n.OwnerFrameId && CanUseQinglongCrescentBladeTarget(actor, _players[target], CardKind.Slash, null, false, Suit.None, 0, []);
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.OwnerFrameId);
        var ignore = n.Intent is BoundaryLiuFengIntent.ProgramSlash or BoundaryLiuFengIntent.ProgramNearestSlash || parent is not null && (IsQigongRequest(parent) || IsWuJingHejiRequest(parent) || n.Intent == BoundaryLiuFengIntent.FixedSlash && parent.FixedTargetSlash?.IgnoreDistance == true);
        return CanUseSlashTarget(actor, _players[target], new(0, CardKind.Slash, Suit.None, 0), effectiveKind: CardKind.Slash, ignoreDistance: ignore, noEffectiveRank: true, physicalCardIds: []);
    }
    private PendingDecision? AppendBoundaryLiuFengQinlingChoices(PendingDecision? p)
    {
        if (p is null || p.Choices.Any(c => c.Parameters.ContainsKey(BoundaryLiuFengOpen))) return p;
        var sources = BoundaryLiuFengSources(p.PlayerSeat).ToArray();
        if (sources.Length == 0 || !BoundaryLiuFengHasCost(p.PlayerSeat) || CaptureBoundaryLiuFengNeed(p.PlayerSeat, p.Kind, _resolutionStack.LastOrDefault()) is not { } n || !n.Targets.Any(t => BoundaryLiuFengRequestTarget(n, t))) return p;
        var choices = p.Choices.ToList();
        foreach (var source in sources)
        { var args = new Dictionary<string,string> { [BoundaryLiuFengOpen] = "open" }; AddConversionParameters(args, source); choices.Add(new(new($"boundary-liu-feng.open.{n.RequestFrameId}.{source.SkillInstanceId}"), "发动侵凌：弃置一张牌，视为使用杀", [], [], args)); }
        return p with { Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()) };
    }
    private static PendingDecision BoundaryLiuFengNativeDecision(PendingDecision p) => !p.Choices.Any(c => c.Parameters.ContainsKey(BoundaryLiuFengOpen)) ? p : p with { Choices = Array.AsReadOnly(p.Choices.Where(c => !c.Parameters.ContainsKey(BoundaryLiuFengOpen)).ToArray()) };
    private static string BoundaryLiuFengDecisionHash(PendingDecision p) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(RequestedDeckBasicFrame.FreezeDecision(p)))));
    private bool IsBoundaryLiuFengQinlingAnswer(ChoiceId id) => _pendingDecision?.Choices.Any(c => c.Id == id && c.Parameters.ContainsKey(BoundaryLiuFengOpen)) == true;
    private CommandResult SubmitBoundaryLiuFengQinlingAnswer(int actor, PromptId prompt, ChoiceId id)
    {
        if (!_started) return Reject(CommandErrorCode.NotStarted, "游戏尚未开始。");
        if (_pendingDecision is not { } p || p.PlayerSeat != actor || p.PromptId != prompt) return Reject(CommandErrorCode.InvalidPrompt, "侵凌必须回答当前真实使用请求。");
        var selected = p.Choices.SingleOrDefault(c => c.Id == id && c.Parameters.ContainsKey(BoundaryLiuFengOpen));
        if (selected is null) return Reject(CommandErrorCode.InvalidChoice, "侵凌选择未公布。");
        return Accept(() => { OpenBoundaryLiuFengRequest(selected); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); });
    }
    private void OpenBoundaryLiuFengRequest(PromptChoice selected)
    {
        var p = _pendingDecision ?? throw new InvalidOperationException("Qinling lost its actual native request.");
        var source = RequireConversionSource(selected); var n = CaptureBoundaryLiuFengNeed(p.PlayerSeat, p.Kind, _resolutionStack.LastOrDefault()) ?? throw new InvalidOperationException("Qinling cannot replace a pure Played request.");
        if (!p.Choices.Any(c => AssistedChoicesEqual([c], [selected])) || !BoundaryLiuFengSources(p.PlayerSeat).Contains(source) || !BoundaryLiuFengHasCost(p.PlayerSeat) || !n.Targets.Any(t => BoundaryLiuFengRequestTarget(n, t))) throw new InvalidOperationException("Qinling lost its original live actor, source, allowance or target.");
        var hash = _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash;
        var f = new ProgramSkillFrame(++_resolutionSequence, p.PlayerSeat, source.SkillId, source.BindingId, hash, 0, [], []) { SkillInstanceId = source.SkillInstanceId, BoundaryLiuFeng = new(source, hash, SkillProgramEffectOp.ResolveBoundaryLiuFengQinling, n.OwnerFrameId, _turnNumber, _turnProgression.OwnerSeat, BoundaryLiuFengStage.Targets) { Need = n, OriginalDecision = p } };
        ClearPendingDecision(); PushRuntimeFrame(f);
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengStartedEvent(f.Id, source, hash, SkillProgramEffectOp.ResolveBoundaryLiuFengQinling, n.OwnerFrameId, _turnNumber, _turnProgression.OwnerSeat));
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengRequestStartedEvent(f.Id, n.OwnerFrameId, n.RequestFrameId, n.ActorSeat, n.InstructionIndex, n.RequestOp, n.Intent, p.PromptId, p.Revision, BoundaryLiuFengDecisionHash(p)));
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id, f.OwnerSeat, f.SkillId, f.ActivationId)); var host = new ProgramSkillHost(this); new SkillProgramExecutor().Run(f.Id, host, host);
    }
    private ProgramSkillFrame? BoundaryLiuFengRequestDraft() => _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.BoundaryLiuFeng is { Need: not null, UseId: 0, Stage: not BoundaryLiuFengStage.Completed });
    private PendingDecision? BoundaryLiuFengInvariantDecision(PendingDecision? ordinary) => BoundaryLiuFengRequestDraft()?.BoundaryLiuFeng?.OriginalDecision is { } p ? BoundaryLiuFengNativeDecision(p) : ordinary is null ? null : BoundaryLiuFengNativeDecision(ordinary);
    private ResolutionFrame? BoundaryLiuFengInvariantTop(ResolutionFrame? ordinary) => BoundaryLiuFengRequestDraft()?.BoundaryLiuFeng?.Need is { } n ? _resolutionStack.Single(f => f.Id == n.RequestFrameId) : ordinary;
    private bool HasBoundaryLiuFengNativeRequestContinuation(long ownerFrameId) => BoundaryLiuFengRequestDraft() is { BoundaryLiuFeng: { Need: { } n } r } f && n.OwnerFrameId == ownerFrameId && ValidBoundaryLiuFeng(f) && BoundaryLiuFengNeedCurrent(r);
    private void AssertBoundaryLiuFengRequests() { foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.BoundaryLiuFeng is not null)) AssertBoundaryLiuFeng(f); }
    private bool HasBoundaryLiuFengProgramSlash(CharacterState actor, IEnumerable<int> targets) => BoundaryLiuFengSources(actor.Seat).Any() && BoundaryLiuFengHasCost(actor.Seat) && CaptureBoundaryLiuFengNeed(actor.Seat, DecisionKind.ProgramTrigger, _resolutionStack.LastOrDefault()) is { } n && n.Targets.Any(t => targets.Contains(t) && BoundaryLiuFengRequestTarget(n, t));
    private bool HasBoundaryLiuFengGenericBasic(ProgramSkillFrame f) => f.KeBiNengPingduan is { } r && HasBoundaryLiuFengProgramSlash(_players[r.ActorSeat], _players.Where(p => p.IsAlive && p.Seat != r.ActorSeat).Select(p => p.Seat));
    private bool HasBoundaryLiuFengForcedSlash(CharacterState actor, CharacterState target, BorrowedSwordHandle pending) => SameContinuationOwner(ActiveBorrowedSword, pending) && _resolutionStack.LastOrDefault()?.Id == pending.ResolutionId && pending.ActiveAttack is null && pending.WeaponOwnerSeat == actor.Seat && pending.SlashTargetSeat == target.Seat && BoundaryLiuFengSources(actor.Seat).Any() && BoundaryLiuFengHasCost(actor.Seat) && actor.IsAlive && target.IsAlive && GetWeapon(actor) is not null && !IsCardUseForbidden(actor.Seat, CardKind.Slash, CardActionType.Use) && IsLegalBorrowedSwordSlashTarget(actor, target, CardKind.Slash, Suit.None, false, null, 0, []);
    private bool HasBoundaryLiuFengForcedSlash(CharacterState actor, CharacterState target, QinglongCrescentBladeHandle pending) => SameAttackOwner(ActiveCardAttack, pending.Attack) && _resolutionStack.LastOrDefault()?.Id == pending.Attack.ResolutionId && pending.Attack.SourceSeat == actor.Seat && pending.Attack.TargetSeat == target.Seat && BoundaryLiuFengSources(actor.Seat).Any() && BoundaryLiuFengHasCost(actor.Seat) && actor.IsAlive && target.IsAlive && !IsCardUseForbidden(actor.Seat, CardKind.Slash, CardActionType.Use) && CanUseQinglongCrescentBladeTarget(actor, target, CardKind.Slash, null, false, Suit.None, 0, []);
    private bool TryAdvanceBoundaryLiuFengAi()
    { if (_pendingDecision is not { } p || _players[p.PlayerSeat].IsHuman) return false; if (p.Choices.FirstOrDefault(c => c.Parameters.ContainsKey(BoundaryLiuFengOpen)) is not { } selected) return false; OpenBoundaryLiuFengRequest(selected); return true; }
}
