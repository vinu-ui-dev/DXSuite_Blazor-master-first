using DXSuite.Director.Entities;
using DXSuite.Director.Models.CoreAPI;
using DXSuite.Director.Services.Messaging;

namespace DXSuite.Director.Tests;

/// <summary>
/// MSG-03 on the Messages page: each Message Type tab works in the type's own languages, in the type's order, rather
/// than a fixed list or CoreAPI's retired <c>GET Messages/Languages</c> (CAPI-09).
/// </summary>
public class MessagesPageLanguagesTests
{
    private static readonly MessageLanguage[] Catalogue = [new("eng", "English"), new("fra", "French"), new("spa", "Spanish")];

    [Fact]
    public void Each_type_tab_gets_its_own_languages_in_order()
    {
        var paging = Type(1, "PAGING", 2, "fra", "eng");
        var irrops = Type(2, "IRROPS", 1, "eng", "spa");

        var tabs = Pages.Messages.Messages.TabsFor([paging, irrops], Catalogue, gtaaMessaging: false);

        Assert.Equal(["ActiveMessages", "IRROPS", "PAGING"], tabs.Select(t => t.Key));
        Assert.Equal(["French", "English"], Languages(tabs.Single(t => t.Key == "PAGING")).Select(l => l.LanguageName));
        Assert.Equal(["eng", "spa"], Languages(tabs.Single(t => t.Key == "IRROPS")).Select(l => l.LanguageCode));
        // Active Messages names each Message rather than showing its texts, so it takes no languages.
        Assert.Empty(tabs[0].Parameters);
    }

    [Fact]
    public void A_code_missing_from_the_catalogue_shows_as_the_code()
    {
        var tabs = Pages.Messages.Messages.TabsFor([Type(1, "PAGING", 1, "pan")], Catalogue, gtaaMessaging: false);

        Assert.Equal("pan", Assert.Single(Languages(tabs[1])).LanguageName);
    }

    [Fact]
    public void The_Top_priority_type_tab_comes_first()
    {
        var top = Type(1, "EMERGENCY", 5, "eng");
        top.IsTopPriority = true;

        var tabs = Pages.Messages.Messages.TabsFor([Type(2, "PAGING", 1, "eng"), top], Catalogue, gtaaMessaging: false);

        Assert.Equal(["ActiveMessages", "EMERGENCY", "PAGING"], tabs.Select(t => t.Key));
    }

    // MSG-20: under D-02 the built-in Counter type sits outside the Display Zone ranking, so it gets no send tab.
    [Fact]
    public void Under_D02_the_built_in_Counter_type_gets_no_tab_and_without_it_Counter_is_an_ordinary_tab()
    {
        TblMessageType[] types = [Type(1, "EMERGENCY", 1, "eng"), Type(4, MessageTypeConfiguration.CounterTypeCode, 2, "eng", "fra")];

        var gtaa = Pages.Messages.Messages.TabsFor(types, Catalogue, gtaaMessaging: true);
        var product = Pages.Messages.Messages.TabsFor(types, Catalogue, gtaaMessaging: false);

        Assert.Equal(["ActiveMessages", "EMERGENCY"], gtaa.Select(t => t.Key));
        Assert.Equal(["ActiveMessages", "EMERGENCY", "COUNTER"], product.Select(t => t.Key));
    }

    private static List<TblLanguage> Languages(Models.Messages.MessageTab tab) => (List<TblLanguage>)tab.Parameters["Languages"];

    private static TblMessageType Type(int id, string code, int priority, params string[] languages) => new()
    {
        MessageTypeId = id,
        Code = code,
        DisplayName = code,
        Priority = priority,
        Languages = languages.Select((l, i) => new TblMessageTypeLanguage { MessageTypeId = id, LanguageCode = l, SortOrder = i + 1 }).ToList()
    };
}
