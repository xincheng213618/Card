using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryGaoXiangHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class ResolveAnniversaryGaoXiangChiyingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveAnniversaryGaoXiangChiyingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireActivationEntry()];
}
internal sealed class ResolveAnniversaryGaoXiangChiyingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryGaoXiangHost>().Begin(f);
}
public enum GaoXiangChiyingStage { HealthQueries, Actor, Victims, Card, DiscardChildren, GainChildren, Completed }
public sealed record GaoXiangChiyingMove(int Ordinal, int Victim, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, int Sequence, long BatchId);
public sealed record GaoXiangChiyingReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner,
    GaoXiangChiyingStage Stage = GaoXiangChiyingStage.HealthQueries, int Actor = -1, int OwnerHp = 0, int ActorHp = 0,
    bool Committed = false, int Index = 0, int Before = 0, int After = 0)
{
    private IReadOnlyList<int> _victims = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<GaoXiangChiyingMove> _discards = Array.AsReadOnly(Array.Empty<GaoXiangChiyingMove>());
    private IReadOnlyList<GaoXiangChiyingMove> _gains = Array.AsReadOnly(Array.Empty<GaoXiangChiyingMove>());
    public IReadOnlyList<int> Victims { get => _victims; init => _victims = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<GaoXiangChiyingMove> Discards { get => _discards; init => _discards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<GaoXiangChiyingMove> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GaoXiangChiyingReceipt? GaoXiangChiying { get; init; }
}
public sealed record GaoXiangChiyingStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner) : IGameEvent;
public sealed record GaoXiangChiyingActorEvent(long FrameId, int Actor, int OwnerHp, int ActorHp) : IGameEvent;
public sealed record GaoXiangChiyingVictimEvent(long FrameId, int Ordinal, int Actor, int Victim) : IGameEvent;
public sealed record GaoXiangChiyingCommittedEvent(long FrameId, int Actor, int Count) : IGameEvent;
public sealed record GaoXiangChiyingMovedEvent(long FrameId, bool Gain, int Ordinal, int Victim, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, int Sequence, long BatchId) : IGameEvent;
internal static class GaoXiangComposition
{
    internal const string Skill = "anniversary:chiying", Activation = "assist-range-discards";
    [ProgramCatalogValidation(44900)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying)) return;
        if (p.Id != Skill || p.Activations is not [{ Id: Activation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
            TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: 1, Effects: [
            { Op: SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }] ||
            p.Triggers.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Chiying requires its exact once-per-Play zero-input native participant-discard activation.");
    }
}
