using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MoTianenBranch { Same, Different }
public enum MoTianenStage { DiscardChildren, QuanyuChild, ObtainChildren, Completing }
public sealed record MoTianenMaterial(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record MoTianenPaidMovement(int CardId, CardKind PrintedKind, CardLocation From,
    CardLocation To, string Reason, long Sequence);
public sealed record MoTianenReceipt(CardConversionSource Source, string GameplayHash, long UseId,
    long ActionId, long WindowId, int TargetSeat, int RoundNumber, int ActualTurn, int TurnOwner,
    MoQuanyuEffect OwnerChoice, MoQuanyuEffect TargetChoice, MoTianenBranch Branch)
{
    public MoTianenStage Stage { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int PoolCount { get; init; }
    public int RandomIndex { get; init; } = -1;
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    public MoTianenMaterial? Material { get; init; }
    private readonly IReadOnlyList<MoTianenMaterial> _grain = Array.AsReadOnly(Array.Empty<MoTianenMaterial>());
    public IReadOnlyList<MoTianenMaterial> GrainMaterials
    { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<MoTianenPaidMovement> _movements = Array.AsReadOnly(Array.Empty<MoTianenPaidMovement>());
    public IReadOnlyList<MoTianenPaidMovement> Movements
    { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
    public bool PaymentSealed { get; init; }
    public MoQuanyuTargetRequest? QuanyuRequest { get; init; }
    public long? QuanyuChildFrameId { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoTianenReceipt? MoTianen { get; init; }
}
public sealed record MoTianenBranchIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, long WindowId, int TargetSeat, int RoundNumber, int ActualTurn, int TurnOwner,
    MoQuanyuEffect OwnerChoice, MoQuanyuEffect TargetChoice, MoTianenBranch Branch) : IGameEvent;
public sealed record MoTianenRandomDiscardIssuedEvent(long FrameId, int PoolCount, int RandomIndex,
    uint RandomBefore, uint RandomAfter, int? CardId, CardKind? PrintedKind, CardLocation? From, long Before) : IGameEvent;
public sealed record MoTianenSpillMaterialFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From) : IGameEvent;
public sealed record MoTianenPaymentSealedEvent(long FrameId, MoTianenBranch Branch, long Before,
    long After, int MovementCount, int? PrimaryCardId) : IGameEvent;
public sealed record MoTianenMaterialPaidEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, string Reason, long Sequence) : IGameEvent;
public sealed record MoTianenSlashObtainIssuedEvent(long FrameId, int? CardId, CardKind? PrintedKind,
    CardLocation? From, long Before) : IGameEvent;
public sealed record MoTianenSlashExemptionIssuedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record MoTianenQuanyuRequestedEvent(long FrameId, MoQuanyuTargetRequest Request) : IGameEvent;
public sealed record MoTianenQuanyuReturnedEvent(long FrameId, long ChildFrameId, MoQuanyuTargetRequest Request,
    MoQuanyuEffect ChosenEffect, int DrawCount) : IGameEvent;
public sealed record MoTianenCompletedEvent(long FrameId, MoTianenBranch Branch) : IGameEvent;

internal interface IMoSunQuanTianenHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveMoSunQuanTianenUniqueUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MoSunQuanTianenHandler();
}
internal sealed class MoSunQuanTianenHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMoSunQuanTianenHost>().Resolve(frame);
}
internal static class MoSunQuanTianenComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "ol:mo-tianen" && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse)) return;
        if (p.Id != "ol:mo-tianen" || p.Activations.Count != 0 || p.Triggers is not [var t] || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            t.Id != "unique-target-branches" || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.Optional ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.SingleActionInstance ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Tianen requires both actual-turn branches for every canonical sole-target true Use, with real random discard/target-only Quanyu or original Slash obtain.");
    }
}
