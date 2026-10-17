using CardGame.Content.Standard;

namespace CardGame.Wpf.ViewModels;

public static partial class GeneralArt
{
    // Activity clones retain the exact edition's attributed local artwork and skins.
    // Activity-only rules may reuse an existing portrait of the same character.
    private static string? GuanduPortraitSource(string id)
    {
        const string prefix = "guandu:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var family = id[prefix.Length..];
        if (StandardGuanduActivityPackage.OrdinaryVersions.TryGetValue(family, out var edition)) return edition;
        return family switch
        {
            "xu-you" => "classic:xu-you",
            "gao-lan" => "ol:gao-lan",
            "shen-pei" => "ol:shen-pei",
            "zhang-he" => "classic:zhang-he",
            "xun-chen" => "ol:xun-chen",
            _ => null
        };
    }
}
