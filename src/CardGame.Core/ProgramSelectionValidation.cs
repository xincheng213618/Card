using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CardGame.Core;

internal enum ProgramSelectionValidationStage { BeforeDistinctSuits, AfterCardFilters, BeforePublicPrerequisites }

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ProgramSelectionValidatorAttribute(
    ProgramSelectionValidationStage stage, int order, params SkillProgramEffectOp[] operations) : Attribute
{
    internal ProgramSelectionValidationStage Stage { get; } = stage;
    internal int Order { get; } = order;
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

internal readonly record struct ProgramSelectionValidationRequest(
    ProgramSelectionContext Context, LegalAction Action, IReadOnlyList<int> Cards, IReadOnlyList<int> Targets);

internal interface IProgramSelectionValidator
{
    CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries);
}

internal sealed class ProgramSelectionValidationCatalog
{
    internal sealed record Registration(ProgramSelectionValidationStage Stage, int Order,
        IReadOnlyList<SkillProgramEffectOp> Operations, IProgramSelectionValidator Validator);

    private readonly IReadOnlyDictionary<ProgramSelectionValidationStage, IReadOnlyList<Registration>> _stages;
    internal IReadOnlyList<Registration> Registrations { get; }

    internal ProgramSelectionValidationCatalog(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var registrations = new List<Registration>();
        foreach (var type in types.Select(type => type ?? throw new InvalidOperationException("A selection validator type cannot be null."))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type.GetCustomAttribute<ProgramSelectionValidatorAttribute>() is not { } declaration) continue;
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.IsGenericType || type.ContainsGenericParameters ||
                !typeof(IProgramSelectionValidator).IsAssignableFrom(type) || !Enum.IsDefined(declaration.Stage) ||
                declaration.Order < 0 || declaration.Operations.Any(operation => !Enum.IsDefined(operation)) ||
                declaration.Operations.Distinct().Count() != declaration.Operations.Count ||
                constructors is not [var constructor] || constructor.GetParameters().Length != 0 ||
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 0)
                throw new InvalidOperationException($"Selection validator '{type.FullName}' has an invalid registration.");
            registrations.Add(new(declaration.Stage, declaration.Order,
                Array.AsReadOnly(declaration.Operations.ToArray()), Construct(constructor)));
        }
        if (registrations.Count == 0 || registrations.GroupBy(item => (item.Stage, item.Order)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Selection validators require nonempty, uniquely ordered stage registrations.");
        Registrations = Array.AsReadOnly(registrations.OrderBy(item => item.Stage).ThenBy(item => item.Order).ToArray());
        _stages = new ReadOnlyDictionary<ProgramSelectionValidationStage, IReadOnlyList<Registration>>(
            Registrations.GroupBy(item => item.Stage).ToDictionary(group => group.Key,
                group => (IReadOnlyList<Registration>)Array.AsReadOnly(group.ToArray())));
    }

    internal IReadOnlyList<Registration> ForStage(ProgramSelectionValidationStage stage) => _stages.GetValueOrDefault(stage) ?? [];

    private static IProgramSelectionValidator Construct(ConstructorInfo constructor)
    {
        try { return (IProgramSelectionValidator)constructor.Invoke(null); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
}

internal sealed class ProgramSelectionValidationRegistry(
    ProgramSelectionValidationCatalog catalog, IProgramSelectionQueries queries)
{
    internal CommandError? Validate(ProgramSelectionValidationStage stage, ProgramSelectionContext context,
        LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var request = new ProgramSelectionValidationRequest(context, action, cards, targets);
        foreach (var registration in catalog.ForStage(stage))
            if ((registration.Operations.Count == 0 || registration.Operations.Any(context.Features.HasOperation)) &&
                registration.Validator.Validate(request, queries) is { } error)
                return error;
        return null;
    }
}

internal interface IMatchingRecastSelectionQuery : IProgramSelectionQuery { bool IsValid(int ownerSeat, IReadOnlyList<int> cards); }
internal interface IOwnedRankSumSelectionQuery : IProgramSelectionQuery { bool CanStart(int ownerSeat, ProgramExecutionPlan plan); }
internal interface IExactOwnedCountSelectionQuery : IProgramSelectionQuery { bool CanStart(int ownerSeat, ProgramExecutionPlan plan); }
internal interface IEquipmentDonationSelectionQuery : IProgramSelectionQuery { bool CanPay(int ownerSeat, string skillId, string stateId); }
internal interface IPairObtainSelectionQuery : IProgramSelectionQuery { bool CanSelect(int ownerSeat, string skillId, SkillProgramActivation activation, IReadOnlyList<int> targets); }
internal interface IDeferredHandPairSelectionQuery : IProgramSelectionQuery { bool CanSelect(int ownerSeat, int first, int second); }
internal interface IForeignSlashSelectionQuery : IProgramSelectionQuery { bool CanSelect(int ownerSeat, int targetSeat); }
internal interface ISelectedActorDuelSelectionQuery : IProgramSelectionQuery { bool CanSelect(int ownerSeat, int targetSeat); }
internal interface IEquipmentPlacementSelectionQuery : IProgramSelectionQuery { bool CanPlace(int ownerSeat, int targetSeat, int cardId); }
internal interface ICapturedEquipmentPlacementSelectionQuery : IProgramSelectionQuery { bool CanPlace(int ownerSeat, int targetSeat, int cardId, string skillId); }
internal interface IBlackGiftSelectionQuery : IProgramSelectionQuery { bool WasIssued(int ownerSeat, string skillId); }
internal interface IDynamicDiscardDamageSelectionQuery : IProgramSelectionQuery
{
    bool CanStart(int ownerSeat, string skillId);
    bool CanTarget(int ownerSeat, string skillId, int targetSeat);
}
internal interface IGameHandHpSelectionQuery : IProgramSelectionQuery { bool CanTarget(int ownerSeat, string skillId, string stateId, int targetSeat); }
internal interface ICategoryRefundSelectionQuery : IProgramSelectionQuery { bool CanTarget(int ownerSeat, string skillId, string stateId, int targetSeat); }
internal interface INextActualUseSelectionQuery : IProgramSelectionQuery
{
    bool HasHpLossTargets(int ownerSeat);
    SkillProgramEffect? HpLossProducer(ProgramExecutionPlan plan);
    int HpLossMaximum(int ownerSeat, CardKind kind);
    SkillProgramEffect? SelectedProducer(ProgramExecutionPlan plan);
    string SkillInstance(int ownerSeat, string skillId);
    ProgramMultiCardViewAsSelection? FindSelection(int ownerSeat, IReadOnlyList<int> cards, CardKind kind, CardConversionSource source);
    bool HasAdjustment(int ownerSeat);
    bool IsValidSeat(int seat);
    bool CanUseSlash(int ownerSeat, int targetSeat, ProgramMultiCardViewAsSelection selection);
    IReadOnlyList<int> Targets(int ownerSeat, ProgramExecutionPlan plan, ProgramMultiCardViewAsSelection selection);
}
internal interface IProvenanceSelectionQuery : IProgramSelectionQuery
{
    bool TracksProvenance { get; }
    bool IsFaceDown(int ownerSeat);
    bool HasClaimedEntityPolicy(int ownerSeat);
    string SkillInstance(int ownerSeat, string skillId);
    ProgramMultiCardViewAsSelection? FindSelection(int ownerSeat, IReadOnlyList<int> cards, CardKind kind, CardConversionSource source);
    bool IsValidSeat(int seat);
    bool CanUseSlash(int ownerSeat, int targetSeat, ProgramMultiCardViewAsSelection selection);
}
internal interface IWholeHandSuitSelectionQuery : IProgramSelectionQuery
{
    string SkillInstance(int ownerSeat, string skillId);
    bool IsValid(int ownerSeat, IReadOnlyList<int> cards, CardConversionSource source);
}
internal interface IDistinctFactionSelectionQuery : IProgramSelectionQuery { bool CanPay(int ownerSeat, string skillId); }

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10, SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)]
internal sealed class MatchingRecastSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IMatchingRecastSelectionQuery>().IsValid(request.Context.OwnerSeat, request.Cards) ? null :
            new(CommandErrorCode.InvalidCard, "A matching recast requires at least two same-name cards or an all-equipment HE group.");
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 20)]
internal sealed class OwnedRankSumSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IOwnedRankSumSelectionQuery>().CanStart(request.Context.OwnerSeat, request.Context.Plan) ? null :
            new(CommandErrorCode.IllegalAction, "自己的手牌中不存在满足精确点数和的选牌方案。");
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 30)]
internal sealed class ExactOwnedCountSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IExactOwnedCountSelectionQuery>().CanStart(request.Context.OwnerSeat, request.Context.Plan) ? null :
            new(CommandErrorCode.IllegalAction, "没有足额的合法区域牌支付精确费用。");
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 40, SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits)]
internal sealed class EquipmentDonationSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var context = request.Context;
        var donation = context.Features.First(SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits)!;
        return queries.Get<IEquipmentDonationSelectionQuery>().CanPay(context.OwnerSeat, context.SkillId, donation.StateId!) ? null :
            new(CommandErrorCode.IllegalAction, "The whole equipment payment or limited opportunity is unavailable.");
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 50, SkillProgramEffectOp.ObtainOneFromEachSelectedTarget)]
internal sealed class PairObtainSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IPairObtainSelectionQuery>().CanSelect(request.Context.OwnerSeat, request.Context.SkillId,
            request.Context.Activation, request.Targets) ? null :
            new(CommandErrorCode.InvalidTarget, "The original pair must still each have one legal obtainable card.");
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 60, SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt)]
internal sealed class DeferredHandPairSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 0 || request.Targets is not [var first, var second] ||
        !queries.Get<IDeferredHandPairSelectionQuery>().CanSelect(request.Context.OwnerSeat, first, second)
            ? new(CommandErrorCode.InvalidTarget, "The selected hand pair exceeds the actual owner HE count.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 70, SkillProgramEffectOp.UseSelectedForeignCardAsSlash)]
internal sealed class ForeignSlashSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 0 || request.Targets.Count != 1 ||
        !queries.Get<IForeignSlashSelectionQuery>().CanSelect(request.Context.OwnerSeat, request.Targets[0])
            ? new(CommandErrorCode.InvalidTarget, "The selected actor has no legal region card for the instructed Slash.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 80, SkillProgramEffectOp.UseSelectedActorDuel)]
internal sealed class SelectedActorDuelSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 0 || request.Targets.Count != 1 ||
        !queries.Get<ISelectedActorDuelSelectionQuery>().CanSelect(request.Context.OwnerSeat, request.Targets[0])
            ? new(CommandErrorCode.InvalidTarget, "The selected actor cannot actually use Duel on the owner.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 90, SkillProgramEffectOp.PlaceSelectedEquipment)]
internal sealed class EquipmentPlacementSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 1 || request.Targets.Count != 1 ||
        !queries.Get<IEquipmentPlacementSelectionQuery>().CanPlace(request.Context.OwnerSeat, request.Targets[0], request.Cards[0])
            ? new(CommandErrorCode.InvalidCard, "The selected equipment cannot be placed on this target.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 100, SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw)]
internal sealed class CapturedEquipmentPlacementSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 1 || request.Targets.Count != 1 ||
        !queries.Get<ICapturedEquipmentPlacementSelectionQuery>().CanPlace(request.Context.OwnerSeat, request.Targets[0], request.Cards[0], request.Context.SkillId)
            ? new(CommandErrorCode.InvalidCard, "The captured equipment cannot actually be placed on this other target.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 10, SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest)]
internal sealed class BlackGiftSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IBlackGiftSelectionQuery>().WasIssued(request.Context.OwnerSeat, request.Context.SkillId)
            ? new(CommandErrorCode.IllegalAction, "This limited black-hand contest was already issued in this game.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 20, SkillProgramEffectOp.DiscardTargetHpCardsAndDamage)]
internal sealed class DynamicDiscardDamageSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Targets is not [var target] ||
        !queries.Get<IDynamicDiscardDamageSelectionQuery>().CanStart(request.Context.OwnerSeat, request.Context.SkillId) ||
        !queries.Get<IDynamicDiscardDamageSelectionQuery>().CanTarget(request.Context.OwnerSeat, request.Context.SkillId, target)
            ? new(CommandErrorCode.InvalidTarget, "The exact dynamic target HP/range cost is unavailable.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 30, SkillProgramEffectOp.ResolveGameTargetHandHpChoice)]
internal sealed class GameHandHpSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 0 || request.Targets is not [var target] ||
        !queries.Get<IGameHandHpSelectionQuery>().CanTarget(request.Context.OwnerSeat, request.Context.SkillId,
            request.Context.Features.First(SkillProgramEffectOp.ResolveGameTargetHandHpChoice)!.StateId!, target)
            ? new(CommandErrorCode.InvalidTarget, "This game target is spent or its current hand/HP are equal.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 40, SkillProgramEffectOp.DrawThenDiscardDistinctCategories)]
internal sealed class CategoryRefundSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Cards.Count != 0 || request.Targets is not [var target] ||
        !queries.Get<ICategoryRefundSelectionQuery>().CanTarget(request.Context.OwnerSeat, request.Context.SkillId,
            request.Context.Features.First(SkillProgramEffectOp.DrawThenDiscardDistinctCategories)!.StateId!, target)
            ? new(CommandErrorCode.InvalidTarget, "This target is unavailable for the actual turn's category refund method.") : null;
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 50)]
internal sealed class NextActualUseSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var context = request.Context;
        var targets = request.Targets;
        if (targets.Count <= context.Activation.MaxTargets) return null;
        var query = queries.Get<INextActualUseSelectionQuery>();
        if (query.HasHpLossTargets(context.OwnerSeat) && query.HpLossProducer(context.Plan) is not null)
            return ValidateHpLoss(request, query);
        var effect = query.SelectedProducer(context.Plan);
        var source = effect is null ? null : new CardConversionSource(request.Action.ProgramSkillId!, effect.SourceBind!,
            context.OwnerSeat, query.SkillInstance(context.OwnerSeat, request.Action.ProgramSkillId!));
        var selection = source is null ? null : query.FindSelection(context.OwnerSeat, request.Cards, effect!.OutputKind!.Value, source);
        return effect is null || !query.HasAdjustment(context.OwnerSeat) || targets.Count != context.Activation.MaxTargets + 1 ||
            selection is null || (effect.OutputKind is CardKind.Slash or CardKind.FireSlash
                ? targets.Any(target => !query.CanUseSlash(context.OwnerSeat, target, selection))
                : !query.Targets(context.OwnerSeat, context.Plan, selection).Contains(targets[0]))
            ? new(CommandErrorCode.InvalidTarget, "The next actual-use target adjustment lost its exact materials, source or legal targets.") : null;
    }

    private static CommandError? ValidateHpLoss(ProgramSelectionValidationRequest request, INextActualUseSelectionQuery query)
    {
        var context = request.Context;
        if (request.Targets.Count <= context.Activation.MaxTargets || !query.HasHpLossTargets(context.OwnerSeat)) return null;
        var effect = query.HpLossProducer(context.Plan);
        var source = effect is null ? null : new CardConversionSource(request.Action.ProgramSkillId!, effect.SourceBind!, context.OwnerSeat,
            query.SkillInstance(context.OwnerSeat, request.Action.ProgramSkillId!));
        var selection = source is null ? null : query.FindSelection(context.OwnerSeat, request.Cards, effect!.OutputKind!.Value, source);
        return selection is not null && selection.Cards.All(card => !card.IsGeneralWeapon) &&
            request.Targets.Count <= query.HpLossMaximum(context.OwnerSeat, selection.OutputKind) &&
            request.Targets.Distinct().Count() == request.Targets.Count &&
            request.Targets.All(target => query.IsValidSeat(target) && query.CanUseSlash(context.OwnerSeat, target, selection))
            ? null : new(CommandErrorCode.InvalidTarget, "The HP-based Slash targets lost their real materials or current legal target count.");
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 60)]
internal sealed class ProvenanceSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var context = request.Context;
        var query = queries.Get<IProvenanceSelectionQuery>();
        var effect = context.Features.First(SkillProgramEffectOp.UseSelectedCardsAs);
        if (!query.TracksProvenance || !query.IsFaceDown(context.OwnerSeat) || request.Action.ProgramSkillId is not { } skill ||
            effect is not { OutputKind: CardKind.Slash or CardKind.FireSlash, SourceBind: { } binding } ||
            !query.HasClaimedEntityPolicy(context.OwnerSeat)) return null;
        var source = new CardConversionSource(skill, binding, context.OwnerSeat, query.SkillInstance(context.OwnerSeat, skill));
        var selection = query.FindSelection(context.OwnerSeat, request.Cards, effect.OutputKind.Value, source);
        if (selection is null)
            return new(CommandErrorCode.InvalidCard, "The selected physical cards no longer satisfy this view-as rule.");
        if (request.Targets.Count != 1 || !query.IsValidSeat(request.Targets[0]) ||
            !query.CanUseSlash(context.OwnerSeat, request.Targets[0], selection))
            return new(CommandErrorCode.InvalidTarget, "The complete selected material set cannot use Slash on this target.");
        return null;
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 70, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class WholeHandSuitSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var context = request.Context;
        if (context.Features.First(SkillProgramEffectOp.UseSelectedCardsAs) is not { SourceBind: { } binding } ||
            context.Program.ViewAs.SingleOrDefault(rule => rule.Id == binding) is not { AllHandCardsOfSelectedSuit: true }) return null;
        var query = queries.Get<IWholeHandSuitSelectionQuery>();
        return query.IsValid(context.OwnerSeat, request.Cards,
            new(context.SkillId, binding, context.OwnerSeat, query.SkillInstance(context.OwnerSeat, context.SkillId))) ? null :
            new(CommandErrorCode.InvalidCard, "Select every hand card of one suit for this weapon.");
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforePublicPrerequisites, 10, SkillProgramEffectOp.DiscardDistinctFactionParticipants)]
internal sealed class DistinctFactionSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        queries.Get<IDistinctFactionSelectionQuery>().CanPay(request.Context.OwnerSeat, request.Context.SkillId) ? null :
            new(CommandErrorCode.IllegalAction, "The exact skill instance has no payable owner HE cost.");
}
