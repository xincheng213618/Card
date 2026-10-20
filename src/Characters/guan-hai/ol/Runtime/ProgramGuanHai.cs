using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record GuanHaiMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed partial record CardUseFrame
{
    private IReadOnlyList<XieweiNativePayment>? _xieweiPayments;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<XieweiNativePayment>? XieweiPayments
    { get => _xieweiPayments; init => _xieweiPayments = value is null ? null : Array.AsReadOnly(value.Select(p => p with { Materials = p.Materials, Movements = p.Movements }).ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YouqueSlashOrigin? YouqueSlash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? XieweiTargetMaximum { get; init; }
    private IReadOnlyList<int>? _xieweiOriginalTargets;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<int>? XieweiOriginalTargets { get => _xieweiOriginalTargets; init => _xieweiOriginalTargets = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public enum GuanHaiStage { XieweiChildren, ChooseBait, Pindian, PindianChildren, DiscardChildren, SlashIssuing, NativeSlash, NativeChildren, RewardDraw, RewardChildren }
public sealed record GuanHaiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, int TurnOwner, GuanHaiStage Stage)
{
    public bool Paid { get; init; }
    public long ParentId { get; init; }
    public long PaymentId { get; init; }
    public int OpponentSeat { get; init; } = -1;
    public int BaitCardId { get; init; }
    public int BaitRank { get; init; }
    public long PindianId { get; init; }
    public long NativeUseId { get; init; }
    public long ActionId { get; init; }
    public long DamageFrameId { get; init; }
    public int LoserSeat { get; init; } = -1;
    public int WinnerSeat { get; init; } = -1;
    public bool NativeReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualDrawCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicPersistentPileSource? Pile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PindianResult? Result { get; init; }
    private IReadOnlyList<GuanHaiMaterial> _materials = Array.AsReadOnly(Array.Empty<GuanHaiMaterial>());
    public IReadOnlyList<GuanHaiMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuanHaiReceipt? GuanHai { get; init; } }
public sealed record GuanHaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, int TurnOwner, long ParentId) : IGameEvent;
public sealed record GuanHaiCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface IGuanHaiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class GuanHaiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian ? [] : [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new GuanHaiHandler(op);
}
internal sealed class GuanHaiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IGuanHaiHost>().Resolve(f, op);
}
internal sealed class XieweiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:xiewei";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || !t.IncludeResponseUses || t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.SourceSkillId != SkillId || !t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional || t.Subject is not null || t.CardCategories.Count != 0 || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Xiewei requires one exact paid native basic-use commitment.");
    }
}
internal static class GuanHaiComposition
{
    [ProgramCatalogValidation(720)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.ViewAs.Any(v => v.XieweiBaitBasicUse is not null) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse or SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian or SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit)) return;
        if (p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Guan Hai requires his complete native public-pile skills.");
        if (p.Id == "ol:xiewei")
        {
            if (p.Activations.Count != 0 || p.Triggers.Count != 1 || p.ViewAs.Count != 2 || !p.ViewAs.Select(v => v.OutputKind).ToHashSet().SetEquals([CardKind.Slash, CardKind.Dodge])) throw new InvalidOperationException(path + ": Xiewei requires both native basic directions.");
            foreach (var r in p.ViewAs) XieweiBaitBasicUseSchema.Validate(r, path);
            new XieweiCommittedUseContract().ValidateTrigger(p.Triggers[0]);
        }
        else if (p.Id != "ol:youque" || p.ViewAs.Count != 0 || p.Activations is not [var a] || a.Id != "minimum-bait-pindian" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian, Target: SkillProgramEffectTarget.Owner }] || p.Triggers is not [var t] || t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Any || t.Optional || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Youque requires native minimum-bait Pindian and its exact issued Slash damage benefit.");
    }
}
