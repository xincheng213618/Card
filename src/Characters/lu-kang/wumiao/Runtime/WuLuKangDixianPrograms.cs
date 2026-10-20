using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WuLuKangDixianStage { Choosing, Drawing, DrawChildren, Collecting, MovementChildren }
public sealed record WuLuKangDixianMaterial(int CardId, CardKind PrintedKind, int PrintedRank, CardLocation From);
public sealed record WuLuKangDixianReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, long SequenceBefore)
{
    public WuLuKangDixianStage Stage { get; init; }
    public int Rank { get; init; }
    public bool AllAtLeast { get; init; }
    public int StockCount { get; init; }
    public int MaterialIndex { get; init; }
    public int ActualGained { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public long? LastMovementSequence { get; init; }
    private readonly IReadOnlyList<WuLuKangDixianMaterial> _materials = Array.AsReadOnly(Array.Empty<WuLuKangDixianMaterial>());
    public IReadOnlyList<WuLuKangDixianMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WuLuKangDixianGrant(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int Rank, ActualDiscardRecoveryPhaseKey Phase);
public sealed record WuLuKangDixianUseReceipt(long UseId, long ActionId, int ActorSeat, int ProviderSeat,
    CardKind Kind, int? EffectiveRank, WuLuKangDixianGrant? Grant, bool Applies);
public sealed record WuLuKangDixianDeclarationReceipt(long DeclarationId, int ActorSeat, int ProviderSeat,
    int CardId, CardKind Kind, int EffectiveRank, WuLuKangDixianGrant? Grant);
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuLuKangDixianDeclarationReceipt? WuLuKangDixian { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuLuKangDixianReceipt? WuLuKangDixian { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuLuKangDixianUseReceipt? WuLuKangDixianUse { get; init; }
}
public sealed record WuLuKangDixianStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, long SequenceBefore) : IGameEvent;
public sealed record WuLuKangDixianRankChosenEvent(long FrameId, int Rank, bool AllAtLeast, int StockCount, int MaterialCount) : IGameEvent;
public sealed record WuLuKangDixianMaterialFrozenEvent(long FrameId, int Index, WuLuKangDixianMaterial Material) : IGameEvent;
public sealed record WuLuKangDixianEntityMovedEvent(long FrameId, int Index, int CardId, CardLocation From,
    CardLocation To, long Sequence) : IGameEvent;
public sealed record WuLuKangDixianEntitySkippedEvent(long FrameId, int Index, int CardId, string Reason) : IGameEvent;
public sealed record WuLuKangDixianDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount,
    int ReplacementCount, long SequenceBefore, long SequenceAfter, long? AttemptId) : IGameEvent;
public sealed record WuLuKangDixianGrantedEvent(WuLuKangDixianGrant Grant) : IGameEvent;
public sealed record WuLuKangDixianCompletedEvent(long FrameId, int Rank, bool AllAtLeast, int ActualGained, bool Granted) : IGameEvent;
public sealed record WuLuKangDixianUseAdmittedEvent(WuLuKangDixianUseReceipt Receipt) : IGameEvent;
public sealed record WuLuKangDixianDeclarationAdmittedEvent(WuLuKangDixianDeclarationReceipt Receipt) : IGameEvent;

internal interface IWuLuKangDixianHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveWuLuKangDixianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangDixian;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(8));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuKangDixianHandler();
}
internal sealed class WuLuKangDixianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangDixian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        h.GetCapability<IWuLuKangDixianHost>().Resolve(f);
}
internal static partial class WuLuKangComposition
{
    internal static bool IsDixian(SkillProgram p) => p.Id == Dixian && p.Triggers.Count == 0 && p.ViewAs.Count == 0 &&
        p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 &&
        p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 &&
        !p.LordSkillProjection && !p.CannotChallengeDeclarations && p.Activations is [var a] && a.Id == "declare-rank" &&
        a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.TargetKind == SkillProgramTargetKind.AnyLiving &&
        a.UsesPerGame == 1 && a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsageGroup == a.Id &&
        a.Condition.Kind == SkillProgramConditionKind.Always && a.MarkerCost is null && !a.ContinueAfterOwnerDeath &&
        !a.OtherCharactersOnly && !a.SelectedCardsSameSuit && !a.SelectedCardsDistinctSuits && a.CardCountExpression is null &&
        !a.TargetRequiresEmptyEquipmentSlot && a.EquipmentSlots.Count == 0 && a.CardKinds.Count == 0 && a.CardSuits.Count == 0 &&
        a.CardCategories.Count == 0 && a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.CategoryTargetLedgerId is null && a.TargetPhaseLedgerId is null &&
        a.Effects is [var e] && e.Op == SkillProgramEffectOp.ResolveWuLuKangDixian && e.Target == SkillProgramEffectTarget.Owner &&
        e.Amount == 0 && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.Zones.Count == 0;
}
