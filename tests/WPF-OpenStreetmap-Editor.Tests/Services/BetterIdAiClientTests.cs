using System.Net;
using System.Text;
using System.Text.Json;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class BetterIdAiClientTests {
    [Fact]
    public async Task SummarizeChangesAsync_PostsToDefaultBetterIdEndpoint() {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = JsonContent("""{"summary":"更新道路表面"}""")
        });
        var client = new BetterIdAiClient(new HttpClient(handler));

        var result = await client.SummarizeChangesAsync(new OsmAiChangesetSummary { Total = 1 });

        Assert.Equal("更新道路表面", result);
        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal("https://map.osm.asia/api/osm-ai/summarize", handler.Request?.RequestUri?.AbsoluteUri);
        Assert.Contains(@"""provider_order"":[""deepseek"",""openai"",""mimo""]", handler.Body);
    }

    [Fact]
    public async Task GetTagSuggestionsAsync_PostsBetterIdPayload() {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = JsonContent("""{"summary":"","suggestions":[],"sources":[]}""")
        });
        var client = new BetterIdAiClient(new HttpClient(handler));
        var request = BetterIdAiClient.CreateTagSuggestionRequest(
            "  新开的面包店  ",
            new Dictionary<string, string> {
                ["shop"] = "old",
                ["empty"] = "",
                [new string('k', 256)] = "skip"
            },
            "point",
            new BetterIdAiLocation(23, 113));

        await client.GetTagSuggestionsAsync(request);

        Assert.Equal("https://map.osm.asia/api/osm-ai/tag-suggestions", handler.Request?.RequestUri?.AbsoluteUri);
        using var document = JsonDocument.Parse(handler.Body);
        var root = document.RootElement;
        Assert.Equal("新开的面包店", root.GetProperty("description").GetString());
        Assert.True(root.GetProperty("web_search").GetBoolean());
        Assert.Equal("openai", root.GetProperty("provider_order")[0].GetString());
        Assert.Equal("kimi", root.GetProperty("provider_order")[1].GetString());
        Assert.Equal("deepseek", root.GetProperty("text_provider_order")[0].GetString());
        Assert.Equal("old", root.GetProperty("tags").GetProperty("shop").GetString());
        Assert.False(root.GetProperty("tags").TryGetProperty("empty", out _));
    }

    [Fact]
    public void NormalizeTagSuggestions_DropsBlockedUnchangedAndInvalidEntries() {
        var response = new BetterIdAiRawTagSuggestionResponse {
            Summary = "找到餐饮相关信息",
            Sources = [
                new BetterIdAiSuggestionSource { Title = "OSM Wiki", Url = "https://wiki.openstreetmap.org/wiki/Tag:amenity%3Drestaurant" },
                new BetterIdAiSuggestionSource { Title = "Bad", Url = "file:///tmp/source" }
            ],
            Suggestions = [
                Suggestion("source", "survey", 0.9),
                Suggestion("amenity", "cafe", 0.9),
                Suggestion("amenity", "restaurant", "0.7", "描述指向餐厅", ["https://wiki.openstreetmap.org/wiki/Tag:amenity%3Drestaurant"]),
                Suggestion("created_by", "BetterID", "high")
            ],
            Warnings = ["请人工核验"]
        };

        var normalized = BetterIdAiTagSuggestionNormalizer.Normalize(
            response,
            new Dictionary<string, string> { ["amenity"] = "cafe" });

        var suggestion = Assert.Single(normalized.Suggestions);
        Assert.Equal("amenity", suggestion.Key);
        Assert.Equal("restaurant", suggestion.Value);
        Assert.True(suggestion.Selected);
        Assert.Equal("medium", suggestion.ConfidenceLabel);
        Assert.Single(normalized.Sources);
        Assert.Equal(["请人工核验"], normalized.Warnings);
    }

    [Fact]
    public void ChangesetSummaryBuilder_ReportsActualTagChanges() {
        var document = new MapDocument();
        var feature = new MapFeature {
            Id = "osm-node-1",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(113, 23)]],
            Attributes = new Dictionary<string, string> {
                ["amenity"] = "cafe",
                ["name"] = "Old"
            },
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Node, Id = 1, Version = 1 }
        };
        document.Features.Add(feature);
        document.MarkClean();
        feature.Attributes["amenity"] = "restaurant";
        feature.Attributes["name"] = "New";
        document.Features.Add(new MapFeature {
            Id = "new-building",
            GeometryType = MapGeometryType.Polygon,
            Parts = [[
                new GeoPoint(113, 23),
                new GeoPoint(113.001, 23),
                new GeoPoint(113.001, 23.001),
                new GeoPoint(113, 23)
            ]],
            Attributes = new Dictionary<string, string> { ["building"] = "yes" }
        });

        var preview = OsmChangeSerializer.Build(document, 99);
        var summary = OsmAiChangesetSummaryBuilder.Build(document, preview);

        Assert.Equal(preview.TotalCount, summary.Total);
        Assert.Contains(summary.ActualChanges, change =>
            change.Action == "modified" &&
            change.TagChanges.Changed.Any(tag => tag.Key == "amenity" && tag.Before == "cafe" && tag.After == "restaurant"));
        Assert.Contains(summary.ActualChanges, change => change.Action == "created" && change.FeatureAfter == "building=yes");
        Assert.Contains("amenity", summary.TagKeys);
    }

    private static BetterIdAiRawTagSuggestion Suggestion(
        string key,
        string value,
        object confidence,
        string reason = "reason",
        IReadOnlyList<string>? sources = null) {
        return new BetterIdAiRawTagSuggestion {
            Key = key,
            Value = value,
            Confidence = JsonSerializer.SerializeToElement(confidence),
            Reason = reason,
            Sources = sources
        };
    }

    private static StringContent JsonContent(string json) {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            Request = request;
            Body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
