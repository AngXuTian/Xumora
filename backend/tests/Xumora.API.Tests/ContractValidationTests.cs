using System.ComponentModel.DataAnnotations;
using Xumora.Contracts.Common;
using Xumora.Contracts.Photography.Requests;

namespace Xumora.API.Tests;

public sealed class ContractValidationTests
{
    [Fact]
    public void Pagination_defaults_are_valid()
    {
        var request = new PaginationRequest();

        Assert.True(Validate(request));
        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Pagination_rejects_out_of_range_values(int page, int pageSize)
    {
        var request = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        Assert.False(Validate(request));
    }

    [Fact]
    public void Photography_request_rejects_missing_purpose()
    {
        var request = new TestPhotographyRequest
        {
            Purpose = string.Empty,
            NumberOfOptions = 3
        };

        Assert.False(Validate(request));
    }

    private static bool Validate(object instance)
    {
        return Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            [],
            validateAllProperties: true);
    }
}
