using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Moq;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;

internal static class AkeneoAttributeCatalogTestData
{
    public static IAkeneoApiClient Create(params string[] codes)
    {
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(code => new AkeneoAttributeDefinition { Code = code }).ToList());
        return api.Object;
    }
}
