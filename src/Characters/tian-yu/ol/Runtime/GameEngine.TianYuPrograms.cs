namespace CardGame.Core;

// 扫狄 evidence: one scalar record per added target. The owning use and action
// are already public (their acceptance is committed before the finalized-target
// window), so the record only names the seat this skill appended.
public sealed record ProgramSaodiTargetsAddedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, long CardUseFrameId, long CardActionId, int AddedSeat) : IGameEvent;

// Shared directed distance reduction: the grant/revoke pair records one owner
// holding a distance reduction against one target. Any content skill can grant
// through the 追讨-shaped ops; EvaluateDistance applies active grants as additive
// outgoing-distance contributions, so the state is derived from committed
// history on cold recovery and no runtime map survives a checkpoint.
public sealed record ProgramDirectedDistanceGrantedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int TargetSeat, int Amount) : IGameEvent;

public sealed record ProgramDirectedDistanceRevokedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int TargetSeat) : IGameEvent;

public sealed partial class GameEngine
{
    // 扫狄 card kinds: 【杀】(all three faces) plus the ordinary tricks that can
    // designate a character target. Pure-benefit or no-character tricks
    // (无中生有、五谷丰登、无懈可击) and the all-others AOE tricks can never
    // satisfy "仅指定一名其他角色为目标" with a usable in-between set, so the
    // trigger does not list them.
    internal static readonly CardKind[] SaodiCardKinds =
    [
        CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
        CardKind.Duel, CardKind.Dismantlement, CardKind.Snatch,
        CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword
    ];

    // Shared ledger: replays the committed grant/revoke history for one ordered
    // pair. A revoke erases every grant emitted before it; a later re-grant
    // starts a fresh entry, so "直到你对其造成伤害后" survives cold recovery.
    private IReadOnlyList<ProgramDirectedDistanceGrantedEvent> ActiveDirectedDistanceGrants(int ownerSeat, int targetSeat)
    {
        var active = new List<ProgramDirectedDistanceGrantedEvent>();
        foreach (var fact in CompleteProgramEventHistory())
        {
            switch (fact)
            {
                case ProgramDirectedDistanceGrantedEvent grant when grant.OwnerSeat == ownerSeat && grant.TargetSeat == targetSeat:
                    active.Add(grant);
                    break;
                case ProgramDirectedDistanceRevokedEvent revoke when revoke.OwnerSeat == ownerSeat && revoke.TargetSeat == targetSeat:
                    active.Clear();
                    break;
            }
        }
        return active;
    }

    // Shared consumption: every active directed grant against the queried pair
    // joins the outgoing distance as an additive contribution, bounded to the
    // game minimum of one, beneath the Set-style distance overlays.
    [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 10)]
    private RuleQueryEvaluation ApplyProgramDirectedDistance(CharacterState source, CharacterState target,
        RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ZhuitaoMarkTarget))
            return original;
        var grants = ActiveDirectedDistanceGrants(source.Seat, target.Seat);
        if (grants.Count == 0)
            return original;
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-program-directed", ConvertRuleValue(original))],
            grants.Select((grant, index) => (RuleQueryContribution)new FiniteRuleQueryContribution(
                $"program-directed:{grant.FrameId}:{index}", SkillRuleOperation.Add, grant.Amount)).ToArray());
    }

    [ProgramCapability]
    private sealed class TianYuProgramCapability(GameEngine engine) : ITianYuProgramHost
    {
        public SkillProgramStepOutcome SaodiExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.SaodiProgramExpandTargets(frame, effect);
        public SkillProgramStepOutcome ZhuitaoMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuitaoProgramMarkTarget(frame, effect);
        public SkillProgramStepOutcome ZhuitaoRevoke(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuitaoProgramRevoke(frame, effect);
    }
}
