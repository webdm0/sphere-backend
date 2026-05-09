using System.ComponentModel.DataAnnotations;
using SphereBackend.Features.Cards;

namespace UnitTests;

public class CardValidationRulesTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    [InlineData("Low")]
    [InlineData("HIGH")]
    public void IsAllowedPriority_WithSupportedValue_ReturnsTrue(string value)
    {
        var result = CardValidationRules.IsAllowedPriority(value);

        Assert.True(result);
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("none")]
    [InlineData("")]
    public void IsAllowedPriority_WithUnsupportedValue_ReturnsFalse(string value)
    {
        var result = CardValidationRules.IsAllowedPriority(value);

        Assert.False(result);
    }

    [Fact]
    public void CreateCardDto_WithTooLongContent_FailsValidation()
    {
        var dto = new CreateCardDto
        {
            Title = "Card",
            ColumnId = "abc123",
            Content = new string('x', CardValidationRules.MaxContentLength + 1)
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result => result.ErrorMessage == CardValidationRules.ContentTooLongMessage);
    }

    [Fact]
    public void CreateCardDto_WithBoundaryContent_PassesValidation()
    {
        var dto = new CreateCardDto
        {
            Title = "Card",
            ColumnId = "abc123",
            Content = new string('x', CardValidationRules.MaxContentLength)
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }
}
