using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.UI.Components;
using OutfitFinder.UI.Services;

namespace OutfitFinder.Tests.Components;

public class ComponentTests : BunitContext
{
    private sealed class FixedVisitor : IVisitorIdProvider
    {
        public ValueTask<Guid?> GetAsync() => ValueTask.FromResult<Guid?>(Guid.Empty);
    }

    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/OutfitFinder.UI/js/outfit.js").Mode = JSRuntimeMode.Loose;
        Services.AddScoped<OutfitJs>();
        Services.AddSingleton(new OutfitApiClient(new HttpClient { BaseAddress = new Uri("https://api.invalid/") }, new FixedVisitor()));
    }

    [Fact]
    public void Hamburger_toggles_the_menu_and_closes_on_navigation()
    {
        var cut = Render<AppHeader>();
        var burger = cut.Find("button.burger");
        Assert.Equal("false", burger.GetAttribute("aria-expanded"));
        Assert.Equal("of-nav", burger.GetAttribute("aria-controls"));

        burger.Click();
        Assert.Equal("true", cut.Find("button.burger").GetAttribute("aria-expanded"));
        Assert.Contains("open", cut.Find("#of-nav").ClassList);

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/looks");
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("button.burger").GetAttribute("aria-expanded")));
    }

    [Fact]
    public void Header_shows_brand_and_account_state()
    {
        var anon = Render<AppHeader>(p => p.Add(x => x.IsSignedIn, false));
        Assert.Contains(OutfitFinder.Core.Brand.Name, anon.Find(".brand").TextContent);
        Assert.Contains("Sign in", anon.Markup);
        var admin = Render<AppHeader>(p => p.Add(x => x.IsSignedIn, true).Add(x => x.IsAdmin, true));
        Assert.Contains("My account", admin.Markup);
        Assert.Contains("admin/ai", admin.Markup);
    }

    private static SearchDto Sample() => new(Guid.NewGuid(), SearchStatus.Completed, SearchStage.Done, null, "https://img.test/a.jpg", DateTime.UtcNow, DateTime.UtcNow,
    [
        new ItemDto(Guid.NewGuid(), 1, "Top", "Linen camp shirt", "Navy", null, new BoxDto(0.3, 0.3, 0.4, 0.3), false, true,
            [new MatchDto(Guid.NewGuid(), "Store A", "Shirt", 68m, "USD", "https://shop.test/1", null, MatchKind.Exact, 0, null),
             new MatchDto(Guid.NewGuid(), "Store B", "Shirt 2", 45m, "USD", "https://shop.test/2", null, MatchKind.Similar, 1, null)]),
        new ItemDto(Guid.NewGuid(), 2, "Shoes", "Leather loafers", "Brown", null, new BoxDto(0.3, 0.9, 0.4, 0.07), false, false,
            [new MatchDto(Guid.NewGuid(), "Store C", "Loafer", 120m, "USD", "https://shop.test/3", null, MatchKind.Similar, 0, null)]),
    ]);

    [Fact]
    public void Results_show_markers_chips_matches_and_whole_look_range()
    {
        var cut = Render<ResultsView>(p => p.Add(x => x.Search, Sample()));
        Assert.Equal(2, cut.FindAll("button.dot").Count);
        Assert.Contains("We found 2 pieces", cut.Markup);
        Assert.Contains("Whole look $165 – $188", cut.Markup);
        Assert.Contains("Exact match", cut.Markup);
        var link = cut.Find("a.b.sm");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));

        // The sewable shirt offers Buy / Make and is marked with scissors in the chips.
        Assert.Equal(2, cut.FindAll("[role=tab]").Count);
        Assert.Single(cut.FindAll("[role=group] [aria-label='pattern available']"));

        // Picking item 2 (loafers, not sewable) marks it pressed and offers no "Make it" dead end.
        cut.FindAll("button.dot")[1].Click();
        Assert.Equal("true", cut.FindAll("button.dot")[1].GetAttribute("aria-pressed"));
        Assert.Empty(cut.FindAll("[role=tab]"));
        Assert.Contains("Loafer", cut.Markup);
    }

    [Fact]
    public void Model_text_is_rendered_as_text_not_html()
    {
        var s = Sample();
        var evil = s with { Items = [s.Items[0] with { Name = "<img src=x onerror=alert(1)>" }] };
        var cut = Render<ResultsView>(p => p.Add(x => x.Search, evil));
        // The only <img> is the user's photo; the model's text never becomes an element.
        Assert.Single(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("img[src=x]"));
        Assert.Contains("&lt;img src=x", cut.Find("h2").InnerHtml);
    }

    [Fact]
    public void Progress_steps_mark_done_and_active_rows()
    {
        var cut = Render<ProgressSteps>(p => p.Add(x => x.Stage, SearchStage.SearchingStores));
        var rows = cut.FindAll("li.st");
        Assert.Equal(4, rows.Count);
        Assert.Contains("done", rows[0].ClassList);
        Assert.Contains("done", rows[1].ClassList);
        Assert.Contains("act", rows[2].ClassList);
        Assert.Equal("50", cut.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
    }
}
