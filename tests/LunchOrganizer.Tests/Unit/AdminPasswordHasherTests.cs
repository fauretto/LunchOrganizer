using FluentAssertions;
using LunchOrganizer.Domain.Security;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises <see cref="AdminPasswordHasher"/>: hashing, verification, format detection, and
/// rejection of unsupported or malformed stored password values.
/// </summary>
public sealed class AdminPasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerifyWithCorrectPassword_ReturnsTrue()
    {
        var token = AdminPasswordHasher.Hash("correct-password");

        AdminPasswordHasher.Verify("correct-password", token).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var token = AdminPasswordHasher.Hash("correct-password");

        AdminPasswordHasher.Verify("wrong-password", token).Should().BeFalse();
    }

    [Fact]
    public void Hash_CalledTwiceWithSamePassword_ProducesDifferentTokensThatBothVerify()
    {
        var token1 = AdminPasswordHasher.Hash("same-password");
        var token2 = AdminPasswordHasher.Hash("same-password");

        token1.Should().NotBe(token2);
        AdminPasswordHasher.Verify("same-password", token1).Should().BeTrue();
        AdminPasswordHasher.Verify("same-password", token2).Should().BeTrue();
    }

    [Fact]
    public void Hash_WithEmptyPassword_ThrowsArgumentException()
    {
        var act = () => AdminPasswordHasher.Hash("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hash_WithNullPassword_ThrowsArgumentException()
    {
        var act = () => AdminPasswordHasher.Hash(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_WithNullStoredPassword_ReturnsFalse()
    {
        AdminPasswordHasher.Verify("some-password", null).Should().BeFalse();
    }

    [Fact]
    public void Verify_WithEmptyStoredPassword_ReturnsFalse()
    {
        AdminPasswordHasher.Verify("some-password", "").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithBarePlaintextStoredPassword_ReturnsFalse()
    {
        AdminPasswordHasher.Verify("plaintextpassword", "plaintextpassword").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithOldUnsaltedSha256Format_ReturnsFalse()
    {
        AdminPasswordHasher.Verify("some-password", "sha256:ABCDEF").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithTokenProducedByHash_ReturnsTrue()
    {
        var token = AdminPasswordHasher.Hash("some-password");

        AdminPasswordHasher.IsSupportedFormat(token).Should().BeTrue();
    }

    [Fact]
    public void IsSupportedFormat_WithNull_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat(null).Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithEmpty_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithBarePlaintext_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("plaintextpassword").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithOldUnsaltedSha256Format_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("sha256:ABCDEF").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithWrongNumberOfParts_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("pbkdf2-sha256:210000:c2FsdA==").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithWrongNumberOfParts_ReturnsFalseWithoutThrowing()
    {
        AdminPasswordHasher.Verify("some-password", "pbkdf2-sha256:210000:c2FsdA==").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithNonNumericIterationCount_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("pbkdf2-sha256:not-a-number:c2FsdA==:aGFzaA==").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithNonNumericIterationCount_ReturnsFalseWithoutThrowing()
    {
        AdminPasswordHasher.Verify("some-password", "pbkdf2-sha256:not-a-number:c2FsdA==:aGFzaA==").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedFormat_WithInvalidBase64_ReturnsFalse()
    {
        AdminPasswordHasher.IsSupportedFormat("pbkdf2-sha256:210000:not-valid-base64!!!:aGFzaA==").Should().BeFalse();
    }

    [Fact]
    public void Verify_WithInvalidBase64_ReturnsFalseWithoutThrowing()
    {
        AdminPasswordHasher.Verify("some-password", "pbkdf2-sha256:210000:not-valid-base64!!!:aGFzaA==").Should().BeFalse();
    }
}
