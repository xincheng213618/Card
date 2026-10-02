namespace CardGame.Core;

internal interface ITurnRedSlashProgramHost { void GrantTurnRedSlashBenefits(ProgramSkillFrame frame); }
internal sealed class GrantTurnRedSlashBenefitsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnRedSlashBenefits;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnRedSlashBenefitsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpLost)];
}
public sealed class GrantTurnRedSlashBenefitsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnRedSlashBenefits;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((ITurnRedSlashProgramHost)host).GrantTurnRedSlashBenefits(frame); return SkillProgramStepOutcome.Continue; }
}

public sealed record TurnRedSlashPolicy(long GrantSequence, int TurnNumber, int TurnSeat, long ParentFrameId, int EffectIndex, CardUseEffectSource Source);
public sealed record TurnRedSlashPolicyGrantedEvent(TurnRedSlashPolicy Policy) : IGameEvent;
internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnRedSlashPolicy> _redSlashPolicies = [];
    internal TurnRedSlashPolicy GrantRedSlashPolicy(int turn, int seat, long parent, int effect, CardUseEffectSource source)
    {
        var existing = _redSlashPolicies.SingleOrDefault(p=>p.ParentFrameId==parent && p.EffectIndex==effect);
        if(existing is not null)
        { if(existing.TurnNumber!=turn||existing.TurnSeat!=seat||existing.Source!=source) throw new InvalidOperationException("A red Slash grant changed its meaning."); return existing; }
        var policy = new TurnRedSlashPolicy(++_grantSequence,turn,seat,parent,effect,source); _redSlashPolicies.Add(policy); return policy;
    }
    internal bool HasRedSlashPolicy(int turn,int seat,int actor)=>_redSlashPolicies.Any(p=>p.TurnNumber==turn&&p.TurnSeat==seat&&p.Source.OwnerSeat==actor);
    private IEnumerable<long> ExpiringRedSlashPolicies(int turn,int seat)=>_redSlashPolicies.Where(p=>p.TurnNumber==turn&&p.TurnSeat==seat).Select(p=>p.GrantSequence);
    private void ExpireRedSlashPolicies(HashSet<long> expired)=>_redSlashPolicies.RemoveAll(p=>expired.Contains(p.GrantSequence));
}
