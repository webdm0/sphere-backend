using SphereBackend.Features.Columns;
using SphereBackend.Models;

namespace UnitTests;

public class ColumnFeatureHelpersTests
{
    [Fact]
    public void HasDuplicateIds_WhenIdsRepeat_ReturnsTrue()
    {
        var result = ColumnFeatureHelpers.HasDuplicateIds(new[] { 1, 2, 1 });

        Assert.True(result);
    }

    [Fact]
    public void HasDuplicateIds_WhenIdsAreUnique_ReturnsFalse()
    {
        var result = ColumnFeatureHelpers.HasDuplicateIds(new[] { 1, 2, 3 });

        Assert.False(result);
    }

    [Fact]
    public void AssignSequentialOrders_RewritesOrdersFromZero()
    {
        var columns = new List<Column>
        {
            new() { Order = 7 },
            new() { Order = 14 },
            new() { Order = 21 }
        };

        ColumnFeatureHelpers.AssignSequentialOrders(columns);

        Assert.Equal(0, columns[0].Order);
        Assert.Equal(1, columns[1].Order);
        Assert.Equal(2, columns[2].Order);
    }
}
