using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class TokenAuthTests
{
    [Fact]
    public void Accepts_Matching_Token()
    {
        Assert.True(TokenAuth.IsAuthorized("abc123", "abc123"));
    }

    [Fact]
    public void Accepts_Token_With_Copy_Paste_Whitespace()
    {
        Assert.True(TokenAuth.IsAuthorized("abc123", " abc123\r\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData(null)]
    public void Rejects_Missing_Or_Invalid_Token(string? supplied)
    {
        Assert.False(TokenAuth.IsAuthorized("abc123", supplied));
    }
}
