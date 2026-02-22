using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class AuthService
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 120_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    public sealed class AuthResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public UserAccount? User { get; init; }
    }

    public sealed class UserRow
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastLoginAt { get; init; }
    }

    public static void EnsureDefaultAdminUser(AppDbContext db)
    {
        if (db.UserAccounts.AsNoTracking().Any())
            return;

        var (salt, hash) = CreatePasswordHash("admin123");
        db.UserAccounts.Add(new UserAccount
        {
            Username = "admin",
            UsernameNorm = NormalizeUserName("admin"),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    public AuthResult Login(string? username, string? password)
    {
        var normalized = NormalizeUserName(username);
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(password))
            return new AuthResult { Ok = false, Message = "Username and password are required." };

        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(u => u.UsernameNorm == normalized);
        if (user is null || !user.IsActive)
            return new AuthResult { Ok = false, Message = "Invalid username or password." };

        if (!VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
            return new AuthResult { Ok = false, Message = "Invalid username or password." };

        user.LastLoginAt = DateTime.UtcNow;
        db.SaveChanges();

        return new AuthResult
        {
            Ok = true,
            Message = $"Welcome {user.Username}.",
            User = user
        };
    }

    public IReadOnlyList<UserRow> GetUsers()
    {
        using var db = new AppDbContext();
        return db.UserAccounts
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserRow
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            })
            .ToList();
    }

    public (bool ok, string message) CreateUser(string? username, string? password, string? role, bool isActive)
    {
        var normalized = NormalizeUserName(username);
        if (string.IsNullOrWhiteSpace(normalized))
            return (false, "Username is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return (false, "Password must be at least 4 characters.");

        using var db = new AppDbContext();
        if (db.UserAccounts.AsNoTracking().Any(u => u.UsernameNorm == normalized))
            return (false, "That username already exists.");

        var (salt, hash) = CreatePasswordHash(password);
        db.UserAccounts.Add(new UserAccount
        {
            Username = username!.Trim(),
            UsernameNorm = normalized,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = NormalizeRole(role),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return (true, "User created.");
    }

    public (bool ok, string message) ResetPassword(int userId, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            return (false, "New password must be at least 4 characters.");

        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return (false, "User not found.");

        var (salt, hash) = CreatePasswordHash(newPassword);
        user.PasswordSalt = salt;
        user.PasswordHash = hash;
        db.SaveChanges();
        return (true, $"Password reset for {user.Username}.");
    }

    public (bool ok, string message) UpdateRole(int userId, string? role)
    {
        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return (false, "User not found.");

        user.Role = NormalizeRole(role);
        db.SaveChanges();
        return (true, $"{user.Username} role updated to {user.Role}.");
    }

    public (bool ok, string message) SetActive(int userId, bool isActive)
    {
        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return (false, "User not found.");

        user.IsActive = isActive;
        db.SaveChanges();
        return (true, $"{user.Username} {(isActive ? "activated" : "deactivated")}.");
    }

    public (bool ok, string message) DeleteUser(int userId)
    {
        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return (false, "User not found.");

        var adminCount = db.UserAccounts.Count(u => u.Role == "Admin" && u.IsActive);
        if (user.Role == "Admin" && user.IsActive && adminCount <= 1)
            return (false, "Cannot delete the last active Admin user.");

        db.UserAccounts.Remove(user);
        db.SaveChanges();
        return (true, $"{user.Username} deleted.");
    }

    public bool HasAnyUser()
    {
        using var db = new AppDbContext();
        return db.UserAccounts.AsNoTracking().Any();
    }

    public static string NormalizeUserName(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        return new string(username
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "Tech";

        var value = role.Trim();
        return value switch
        {
            "Admin" => "Admin",
            "Ops" => "Ops",
            "Tech" => "Tech",
            "Technician" => "Tech",
            "ReadOnly" => "ReadOnly",
            _ => "Tech"
        };
    }

    public static bool CanAccessTechnicianApp(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return false;

        return role.Trim().Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Trim().Equals("Tech", StringComparison.OrdinalIgnoreCase)
            || role.Trim().Equals("Technician", StringComparison.OrdinalIgnoreCase);
    }

    private static (string salt, string hash) CreatePasswordHash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithm,
            KeyBytes);

        return (Convert.ToBase64String(saltBytes), Convert.ToBase64String(hashBytes));
    }

    private static bool VerifyPassword(string password, string saltBase64, string hashBase64)
    {
        byte[] saltBytes;
        byte[] expectedHash;
        try
        {
            saltBytes = Convert.FromBase64String(saltBase64);
            expectedHash = Convert.FromBase64String(hashBase64);
        }
        catch
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithm,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
