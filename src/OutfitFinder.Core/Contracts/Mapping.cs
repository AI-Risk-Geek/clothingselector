using OutfitFinder.Core.Entities;

namespace OutfitFinder.Core.Contracts;

public static class Mapping
{
    public static SearchDto ToDto(this OutfitSearch s, string? imageUrl) => new(
        s.Id, s.Status, s.Stage, s.Error, imageUrl, s.CreatedUtc, s.CompletedUtc,
        s.Items.OrderBy(i => i.Number).Select(ToDto).ToList());

    public static ItemDto ToDto(this OutfitItem i) => new(
        i.Id, i.Number, i.Category, i.Name, i.Description, i.BrandHints,
        new BoxDto(i.BoxX, i.BoxY, i.BoxW, i.BoxH), i.IsUserAdjusted, i.Makeable,
        i.Matches.OrderBy(m => m.Rank).Select(ToDto).ToList());

    public static MatchDto ToDto(this ItemMatch m) => new(
        m.Id, m.Store, m.Title, m.Price, m.Currency, m.Url, m.ImageUrl, m.MatchType, m.Rank, m.Feedback);

    public static MaterialDto ToDto(this MaterialLine l) => new(l.Id, l.Kind, l.Description, l.Quantity, l.Unit, l.Url, l.Price);
}
