using SphereBackend.Features.Cards;
using SphereBackend.Models;
using UnitTests.TestDoubles;

namespace UnitTests;

public class CardFeatureHelpersTests
{
    [Fact]
    public void HasDuplicateIds_WhenIdsRepeat_ReturnsTrue()
    {
        var result = CardFeatureHelpers.HasDuplicateIds(new[] { 4, 8, 4 });

        Assert.True(result);
    }

    [Fact]
    public void HasDuplicateIds_WhenIdsAreUnique_ReturnsFalse()
    {
        var result = CardFeatureHelpers.HasDuplicateIds(new[] { 4, 8, 15 });

        Assert.False(result);
    }

    [Fact]
    public void AssignSequentialOrders_UpdatesOnlyCardsWithChangedOrder()
    {
        var unchangedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 3, 24, 12, 0, 0, DateTimeKind.Utc);
        var cards = new List<Card>
        {
            new() { Order = 0, UpdatedAt = unchangedTime, UpdatedById = 1 },
            new() { Order = 7, UpdatedAt = unchangedTime, UpdatedById = 1 },
            new() { Order = 9, UpdatedAt = unchangedTime, UpdatedById = 1 }
        };

        CardFeatureHelpers.AssignSequentialOrders(cards, updatedAt, updatedById: 42);

        Assert.Equal(0, cards[0].Order);
        Assert.Equal(unchangedTime, cards[0].UpdatedAt);
        Assert.Equal(1, cards[1].Order);
        Assert.Equal(updatedAt, cards[1].UpdatedAt);
        Assert.Equal(42, cards[1].UpdatedById);
        Assert.Equal(2, cards[2].Order);
        Assert.Equal(updatedAt, cards[2].UpdatedAt);
        Assert.Equal(42, cards[2].UpdatedById);
    }

    [Fact]
    public void MapTargetColumnDto_ReturnsColumnWithMappedCardsInProvidedOrder()
    {
        var idHasher = new StubIdHasher();
        var column = new Column
        {
            Id = 10,
            Title = "Todo",
            Order = 3
        };
        var cards = new List<Card>
        {
            new() { Id = 100, Title = "First", Order = 0, ColumnId = 10 },
            new() { Id = 200, Title = "Restored", Order = 1, ColumnId = 10, AssigneeId = 5 },
            new() { Id = 300, Title = "Last", Order = 2, ColumnId = 10 }
        };

        var result = CardFeatureHelpers.MapTargetColumnDto(idHasher, column, cards);

        Assert.Equal("10", result.Id);
        Assert.Equal("Todo", result.Title);
        Assert.Equal(3, result.Order);
        Assert.Equal(new[] { "100", "200", "300" }, result.Cards.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, result.Cards.Select(c => c.Order));
        Assert.Equal("10", result.Cards[1].ColumnId);
        Assert.Equal("5", result.Cards[1].AssigneeId);
    }
}
