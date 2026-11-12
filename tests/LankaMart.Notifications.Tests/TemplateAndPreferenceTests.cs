using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;

namespace LankaMart.Notifications.Tests;

public sealed class TemplateRendererTests
{
    [Fact]
    public void Replaces_placeholders()
    {
        var s = TemplateRenderer.Render("Order {orderNumber} paid: {total}", new Dictionary<string, string> { ["orderNumber"] = "LM-2026-880001", ["total"] = "LKR 1,000.00" }, html: false);
        Assert.Equal("Order LM-2026-880001 paid: LKR 1,000.00", s);
    }

    [Fact]
    public void Html_mode_encodes_values_so_a_hostile_name_cannot_inject_markup()
    {
        var s = TemplateRenderer.Render("<p>Hi {name}</p>", new Dictionary<string, string> { ["name"] = "<script>alert(1)</script>" }, html: true);
        Assert.Equal("<p>Hi &lt;script&gt;alert(1)&lt;/script&gt;</p>", s);
    }

    [Fact]
    public void Text_mode_leaves_values_as_typed()
    {
        var s = TemplateRenderer.Render("Hi {name}", new Dictionary<string, string> { ["name"] = "A & B" }, html: false);
        Assert.Equal("Hi A & B", s);
    }

    [Fact]
    public void Unknown_placeholder_renders_empty()
    {
        Assert.Equal("Code:  end", TemplateRenderer.Render("Code: {missing} end", new Dictionary<string, string>(), html: false));
    }
}

public sealed class PreferenceTests
{
    [Fact]
    public void Promo_email_is_off_by_default_marketing_is_opt_in()
    {
        var p = PreferenceDefaults.For(Guid.NewGuid(), "promos");
        Assert.False(p.Allows(Channel.Email));
        Assert.False(p.Allows(Channel.Sms));
        Assert.True(p.Allows(Channel.InApp));
    }

    [Fact]
    public void Order_sms_is_off_by_default_because_it_costs_money()
    {
        var p = PreferenceDefaults.For(Guid.NewGuid(), "orders");
        Assert.True(p.Allows(Channel.Email));
        Assert.False(p.Allows(Channel.Sms));
        Assert.True(p.Allows(Channel.Push));
    }

    [Fact]
    public void Security_category_allows_every_channel_and_cannot_be_edited()
    {
        var p = PreferenceDefaults.For(Guid.NewGuid(), "security");
        Assert.All(Enum.GetValues<Channel>(), c => Assert.True(p.Allows(c)));
        Assert.DoesNotContain("security", PreferenceDefaults.UserEditable);
        Assert.False(new PreferenceValidator().Validate(new PreferenceDto("security", false, false, false, false)).IsValid);
        Assert.True(new PreferenceValidator().Validate(new PreferenceDto("orders", false, false, false, true)).IsValid);
    }
}
