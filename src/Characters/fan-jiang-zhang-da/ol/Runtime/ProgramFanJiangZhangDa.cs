using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum JueshengStage { NativeUse, GrantChildren }
public sealed record JueshengDuelOrigin(long ProgramFrameId, long UseId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActorSeat, int InitialTargetSeat);
public sealed record JueshengTargetDamage(int TargetSeat, int SlashCount, int Amount);
public sealed record JueshengReceipt(JueshengDuelOrigin Origin, int InstructionIndex, JueshengStage Stage,
    IReadOnlyList<JueshengTargetDamage> Targets, IReadOnlyList<int> FinalTargets, bool NativeCompleted = false, int GrantCursor = 0)
{
    private IReadOnlyList<JueshengTargetDamage> _targets = Array.AsReadOnly(Targets.ToArray());
    public IReadOnlyList<JueshengTargetDamage> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _finalTargets = Array.AsReadOnly(FinalTargets.ToArray());
    public IReadOnlyList<int> FinalTargets { get => _finalTargets; init => _finalTargets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JueshengReceipt? Juesheng { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JueshengDuelOrigin? JueshengDuel { get; init; } }
public sealed record JueshengSpentEvent(long FrameId, int OwnerSeat, string SkillId) : IGameEvent;
public sealed record JueshengDuelIssuedEvent(JueshengDuelOrigin Origin, int InstructionIndex) : IGameEvent;
public sealed record JueshengTargetDamageFrozenEvent(long FrameId, long UseId, int Index, int TargetSeat, int SlashCount, int Amount) : IGameEvent;
public sealed record JueshengFinalTargetEvent(long FrameId, long UseId, int Index, int TargetSeat) : IGameEvent;
public sealed record JueshengDuelReturnedEvent(long FrameId, long UseId, int FinalTargetCount) : IGameEvent;
public sealed record JueshengSkillGrantedEvent(long FrameId, long UseId, int Index, int RecipientSeat, string GrantId, string SkillInstanceId, string SourceId, int HistoryCutoff) : IGameEvent;
public sealed record JueshengGrantSkippedEvent(long FrameId, long UseId, int Index, int RecipientSeat) : IGameEvent;
public sealed record JueshengSkillExpiredEvent(int RecipientSeat, string GrantId, string SkillInstanceId, string SourceId, int ActualTurn) : IGameEvent;
public sealed record JueshengCompletedEvent(long FrameId, long UseId) : IGameEvent;

internal interface IFanJiangZhangDaHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal interface IJueshengTargetQuery : IProgramSelectionQuery { bool Allows(int actor, int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 27350, SkillProgramEffectOp.ResolveJueshengLimitedDuel)]
internal sealed class JueshengTargetFilter : IProgramSelectionTargetFilter
{ public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) => queries.Get<IJueshengTargetQuery>().Allows(context.OwnerSeat, targetSeat); }
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 27350, SkillProgramEffectOp.ResolveJueshengLimitedDuel)]
internal sealed class JueshengTargetValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => request.Targets is not [var target] || !queries.Get<IJueshengTargetQuery>().Allows(request.Context.OwnerSeat, target)
        ? new(CommandErrorCode.InvalidTarget, "Juesheng requires one native legal ordinary Duel target.") : null;
}
internal sealed class ResolveJueshengLimitedDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveJueshengLimitedDuel;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new JueshengHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
}
internal sealed class JueshengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveJueshengLimitedDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IFanJiangZhangDaHost>().Resolve(f);
}
internal static class FanJiangZhangDaComposition
{
    [ProgramCatalogValidation(530)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var operation = p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveJueshengLimitedDuel)) || p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveJueshengLimitedDuel));
        var policy = p.CardPolicies.Any(c => c.Kind == SkillProgramCardPolicyKind.BlackSlashIgnoresArmorBothDirections);
        if (!operation && !policy) return;
        if (p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection)
            throw new InvalidOperationException(path + ": Fan Jiang Zhang Da requires his exact native Duel or black-Slash armor metadata.");
        if (policy)
        {
            if (p.Id != "ol:yuanchou" || p.Activations.Count != 0 || p.CardPolicies is not [var c] || c.Kind != SkillProgramCardPolicyKind.BlackSlashIgnoresArmorBothDirections ||
                c.Value != 0 || c.CardKinds.Count != 0 || c.RequiredCardKinds.Count != 0 || c.Condition.Kind != SkillProgramConditionKind.Always || c.InputSuit is not null || c.OutputSuit is not null ||
                c.FactionId is not null || c.OwnerRole is not null || c.DiscardCost != 0 || c.ProviderDrawCount != 0)
                throw new InvalidOperationException(path + ": Yuanchou requires only its zero-value two-way effective-black Slash armor policy.");
            return;
        }
        if (p.Id != "ol:juesheng" || p.CardPolicies.Count != 0 || p.Activations is not [var a] || a.Id != "limited-duel" ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveJueshengLimitedDuel, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerPhase is not null || a.UsesPerTurn is not null || a.UsesPerGame != 1 ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath || a.MarkerCost is not null || a.CardCountExpression is not null || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || a.EquipmentSlots.Count != 0 || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }))
            throw new InvalidOperationException(path + ": Juesheng requires one permanently limited zero-material ordinary native Duel and one other living target.");
    }
}
