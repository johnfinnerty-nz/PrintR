using System.Net;
using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class NetworkGuardsTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.1")]
    public void Allows_Local_And_Private_Addresses(string address)
    {
        Assert.True(NetworkGuards.IsPrivateOrLoopback(IPAddress.Parse(address)));
    }

    [Fact]
    public void Rejects_Public_Addresses()
    {
        Assert.False(NetworkGuards.IsPrivateOrLoopback(IPAddress.Parse("8.8.8.8")));
    }
}
