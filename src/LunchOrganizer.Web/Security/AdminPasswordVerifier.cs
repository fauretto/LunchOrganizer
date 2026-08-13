namespace LunchOrganizer.Web.Security;

public static class AdminPasswordVerifier
{
    public static bool Verify(string suppliedPassword, string storedPassword)
    {
        if (storedPassword.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedHex = storedPassword["sha256:".Length..];
            var actualHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedPassword)));
            return actualHex.Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
        }
        // Plain-text comparison, but constant-time to reduce timing side-channels even though
        // this app is explicitly low-security-by-design (plan §6.5) — costs nothing to do properly.
        var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(suppliedPassword);
        var storedBytes = System.Text.Encoding.UTF8.GetBytes(storedPassword);
        return suppliedBytes.Length == storedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(suppliedBytes, storedBytes);
    }
}
