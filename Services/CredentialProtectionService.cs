using System;
using System.Security.Cryptography;
using System.Text;

namespace StingListManager.Services;

public static class CredentialProtectionService
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("CapitalAir.StingListManager.Login");

    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return null;

        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(plainBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return null;
        }
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }
}
