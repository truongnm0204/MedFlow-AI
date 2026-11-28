using MedFlow.Application.Options;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Ekyc;

public sealed class VnptEkycConfigReaderTests
{
    private static IOptions<EkycOptions> BuildOptions(
        string backendUrl = "https://api.idg.vnpt.vn",
        double compareThreshold = 70.0,
        int sessionTtlMinutes = 15,
        int maxAttempts = 5,
        string? tokenId = "token-id",
        string? tokenKey = "token-key",
        string? accessToken = "access-token",
        string? publicKeyCa = "public-key-ca")
    {
        return Options.Create(new EkycOptions
        {
            BackendUrl = backendUrl,
            CompareThreshold = compareThreshold,
            SessionTtlMinutes = sessionTtlMinutes,
            MaxAttempts = maxAttempts,
            TokenId = tokenId,
            TokenKey = tokenKey,
            AccessToken = accessToken,
            PublicKeyCa = publicKeyCa,
        });
    }

    [Fact]
    public void Read_ReturnsResolvedConfig_WhenComplete()
    {
        var config = VnptEkycConfigReader.Read(BuildOptions());

        Assert.Equal("https://api.idg.vnpt.vn", config.BackendUrl);
        Assert.Equal(70.0, config.CompareThreshold);
        Assert.Equal(15, config.SessionTtlMinutes);
        Assert.Equal(5, config.MaxAttempts);
        Assert.Equal("token-id", config.TokenId);
        Assert.Equal("token-key", config.TokenKey);
        Assert.Equal("access-token", config.AccessToken);
        Assert.Equal("public-key-ca", config.PublicKeyCa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_ThrowsMissingCredential_WhenTokenIdBlank(string? tokenId)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(tokenId: tokenId)));

        Assert.Equal(VnptEkycConfigReader.MissingCredentialMessage, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Read_ThrowsMissingCredential_WhenTokenKeyBlank(string? tokenKey)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(tokenKey: tokenKey)));

        Assert.Equal(VnptEkycConfigReader.MissingCredentialMessage, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Read_ThrowsMissingCredential_WhenAccessTokenBlank(string? accessToken)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(accessToken: accessToken)));

        Assert.Equal(VnptEkycConfigReader.MissingCredentialMessage, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Read_ThrowsMissingCredential_WhenPublicKeyCaBlank(string? publicKeyCa)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(publicKeyCa: publicKeyCa)));

        Assert.Equal(VnptEkycConfigReader.MissingCredentialMessage, ex.Message);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(100.5d)]
    public void Read_ThrowsInvalidConfig_WhenThresholdOutOfRange(double threshold)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(compareThreshold: threshold)));

        Assert.Equal(VnptEkycConfigReader.InvalidConfigMessage, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Read_ThrowsInvalidConfig_WhenTtlNonPositive(int ttl)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(sessionTtlMinutes: ttl)));

        Assert.Equal(VnptEkycConfigReader.InvalidConfigMessage, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Read_ThrowsInvalidConfig_WhenMaxAttemptsNonPositive(int maxAttempts)
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(maxAttempts: maxAttempts)));

        Assert.Equal(VnptEkycConfigReader.InvalidConfigMessage, ex.Message);
    }

    [Fact]
    public void Read_ThrowsInvalidConfig_WhenBackendUrlBlank()
    {
        var ex = Assert.Throws<ConflictException>(
            () => VnptEkycConfigReader.Read(BuildOptions(backendUrl: " ")));

        Assert.Equal(VnptEkycConfigReader.InvalidConfigMessage, ex.Message);
    }

    [Fact]
    public void Read_ThrowsInvalidConfig_WhenOptionsNull()
    {
        var ex = Assert.Throws<ConflictException>(() => VnptEkycConfigReader.Read(null));

        Assert.Equal(VnptEkycConfigReader.InvalidConfigMessage, ex.Message);
    }

    [Fact]
    public void HasCredentials_TrueOnlyWhenAllPresent()
    {
        Assert.True(VnptEkycConfigReader.HasCredentials(BuildOptions()));
        Assert.False(VnptEkycConfigReader.HasCredentials(BuildOptions(tokenId: null)));
        Assert.False(VnptEkycConfigReader.HasCredentials(BuildOptions(accessToken: "")));
        Assert.False(VnptEkycConfigReader.HasCredentials(null));
    }

    [Fact]
    public void Read_DoesNotHardcodeThresholdLogic()
    {
        // Hard requirement 7: threshold must be config-driven. Two different configs must
        // yield two different resolved thresholds — proof the value flows from config.
        var low = VnptEkycConfigReader.Read(BuildOptions(compareThreshold: 55.0));
        var high = VnptEkycConfigReader.Read(BuildOptions(compareThreshold: 90.0));

        Assert.Equal(55.0, low.CompareThreshold);
        Assert.Equal(90.0, high.CompareThreshold);
    }
}
