using MySql.Data.MySqlClient;

namespace TatoToys.Api.Services;

/// <summary>
/// MySQL database service — provides methods to create and query users.
/// Registered as a scoped service in DI; each request gets its own connection.
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Tests the MySQL connection. Called once at startup.
    /// </summary>
    public async Task TestConnectionAsync()
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
    }

    /// <summary>
    /// Inserts a new user into the users table. Throws on duplicate email.
    /// </summary>
    public async Task CreateUserAsync(
        string userId, string email, string passwordHash,
        string firstName, string lastName, bool isAdult,
        DateTime termsAcceptedAt, string termsVersion,
        bool marketingOptIn, DateTime createdAt)
    {
        const string sql = @"
            INSERT INTO users (user_id, email, password_hash, first_name, last_name,
                               is_adult, terms_accepted_at, terms_version, marketing_opt_in, created_at)
            VALUES (@userId, @email, @passwordHash, @firstName, @lastName,
                    @isAdult, @termsAcceptedAt, @termsVersion, @marketingOptIn, @createdAt)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("@firstName", firstName);
        cmd.Parameters.AddWithValue("@lastName", lastName);
        cmd.Parameters.AddWithValue("@isAdult", isAdult);
        cmd.Parameters.AddWithValue("@termsAcceptedAt", termsAcceptedAt);
        cmd.Parameters.AddWithValue("@termsVersion", termsVersion);
        cmd.Parameters.AddWithValue("@marketingOptIn", marketingOptIn);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Looks up a user by email. Returns null if not found.
    /// </summary>
    public async Task<UserRow?> GetUserByEmailAsync(string email)
    {
        const string sql = "SELECT user_id, email, password_hash, is_active FROM TetoToys.users WHERE email = @email";



        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;

        Console.WriteLine("Executing: " + cmd.CommandText);
        Console.WriteLine("With parameters: " + string.Join(", ", cmd.Parameters.Cast<MySqlParameter>().Select(p => $"{p.ParameterName}={p.Value}")));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new UserRow(
            reader.GetGuid(reader.GetOrdinal("user_id")).ToString(),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetString(reader.GetOrdinal("password_hash")),
            reader.GetBoolean(reader.GetOrdinal("is_active"))
        );
    }

    /// <summary>
    /// Updates the last_login timestamp for a user.
    /// </summary>
    public async Task UpdateLastLoginAsync(string userId)
    {
        const string sql = "UPDATE users SET last_login = @now WHERE user_id = @userId";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Lightweight record for user data returned from the database.
/// </summary>
public record UserRow(string UserId, string Email, string PasswordHash, bool IsActive);
