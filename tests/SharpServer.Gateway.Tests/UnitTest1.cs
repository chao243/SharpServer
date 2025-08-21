using Microsoft.AspNetCore.Mvc.Testing;

namespace SharpServer.Gateway.Tests;

public class HelloEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public HelloEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HelloEndpoint_ReturnsHello()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/hello");
        
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", content);
    }
}
