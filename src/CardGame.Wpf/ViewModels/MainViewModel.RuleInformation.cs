using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record DeclaredCardViewModel(
    long DeclarationId,
    string Title,
    string Targets,
    string RevealStatus,
    IReadOnlyList<CardViewModel> PublicFaces,
    IReadOnlyList<CardViewModel> OwnerCostFaces,
    string OwnerCostTitle)
{
    public bool HasOwnerCost => OwnerCostFaces.Count > 0;
}

public sealed record PrivateGeneralAvatarViewModel(string GeneralId, string Name, GeneralPortraitViewModel Portrait);

public sealed record GeneralLibraryViewModel(
    int OwnerSeat,
    string Title,
    string PublicDeclaration,
    string Attributes,
    string PrivateTitle,
    IReadOnlyList<PrivateGeneralAvatarViewModel> PrivateAvatars)
{
    public bool HasPrivateAvatars => PrivateAvatars.Count > 0;
}

public sealed partial class MainViewModel
{
    public ObservableCollection<DeclaredCardViewModel> DeclaredCards { get; } = [];
    public bool HasDeclaredCards => DeclaredCards.Count > 0;
    public ObservableCollection<GeneralLibraryViewModel> GeneralLibraries { get; } = [];
    public bool HasGeneralLibraries => GeneralLibraries.Count > 0;

    private void RebuildDeclaredCardViews()
    {
        DeclaredCards.Clear();
        foreach (var declaration in _snapshot.CardDeclarations ?? [])
        {
            string PlayerName(int seat) => _snapshot.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? $"座位 {seat + 1}";
            var name = CardCatalog.Get(declaration.DeclaredKind).DisplayName;
            var title = declaration.OwnerSeat == declaration.ActorSeat
                ? $"{PlayerName(declaration.OwnerSeat)} · 声明【{name}】"
                : $"{PlayerName(declaration.OwnerSeat)} · 为{PlayerName(declaration.ActorSeat)}声明【{name}】";
            var publicFaces = declaration is { IsRevealed: true, RevealedCard: { } revealed }
                ? new[] { CreateViewedCard(revealed, privateView: false) }
                : Array.Empty<CardViewModel>();
            var ownerFaces = !declaration.IsRevealed && declaration.OwnerCost is { } cost &&
                             (declaration.OwnerSeat == _snapshot.HumanSeat || IsDeveloperView)
                ? new[] { CreateViewedCard(cost, privateView: true) }
                : Array.Empty<CardViewModel>();
            DeclaredCards.Add(new(declaration.DeclarationId, title,
                declaration.TargetSeats.Count == 0 ? "无需指定目标" : $"目标：{string.Join("、", declaration.TargetSeats.Select(PlayerName))}",
                declaration.IsRevealed ? "原牌已公开" : "原牌扣置中",
                Array.AsReadOnly(publicFaces), Array.AsReadOnly(ownerFaces),
                declaration.OwnerSeat == _snapshot.HumanSeat ? "扣置的原牌 · 仅你可见" : "扣置的原牌 · 开发视图"));
        }
        RaisePropertyChanged(nameof(HasDeclaredCards));
    }

    private void RebuildGeneralLibraryViews()
    {
        GeneralLibraries.Clear();
        string GeneralName(string id) => _game.ContentRegistry?.Generals.GetValueOrDefault(id)?.Name ?? id;
        string SkillName(string id) => _game.ContentRegistry?.Skills.GetValueOrDefault(id)?.Name ?? id;
        foreach (var player in _snapshot.Players)
        {
            var libraries = player.PrivateGeneralLibraries ?? [];
            for (var index = 0; index < libraries.Count; index++)
            {
                var library = libraries[index];
                var sourceName = SkillName(library.Source.CapabilitySkillId);
                var suffix = libraries.Count > 1 ? $"库 {index + 1}" : "库";
                var title = $"{player.GeneralName} · {sourceName}{suffix} · {library.Count}张";
                var declaration = library.RevealedGeneralId is { } revealed
                    ? $"展示：{GeneralName(revealed)}" + (library.DeclaredSkillId is { } skill ? $" · 技能：{SkillName(skill)}" : string.Empty)
                    : "尚未展示";
                var attributes = library.Gender is { } gender && library.FactionId is { } faction
                    ? $"当前属性：{FactionName(faction)} · {(gender == GeneralGender.Female ? "女" : "男")}" : "当前展示未生效";
                var canReadPrivate = player.Seat == _snapshot.HumanSeat && library.Source.OwnerSeat == _snapshot.HumanSeat || IsDeveloperView;
                var privateAvatars = canReadPrivate && library.GeneralIds is { } ids
                    ? ids.Select(id => new PrivateGeneralAvatarViewModel(id, GeneralName(id), GetGeneralPortrait(id))).ToArray()
                    : Array.Empty<PrivateGeneralAvatarViewModel>();
                GeneralLibraries.Add(new(player.Seat, title, declaration, attributes,
                    player.Seat == _snapshot.HumanSeat ? "库内武将 · 仅你可见" : "库内武将 · 开发视图",
                    Array.AsReadOnly(privateAvatars)));
            }
        }
        RaisePropertyChanged(nameof(HasGeneralLibraries));
    }
}
