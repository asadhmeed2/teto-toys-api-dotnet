using MySql.Data.MySqlClient;

namespace TatoToys.Infrastructure.Data;

/// <summary>
/// Runs CREATE TABLE IF NOT EXISTS for every application table on startup.
/// Safe to call on every boot — statements are idempotent.
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        foreach (var sql in Migrations)
        {
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static readonly string[] Migrations =
    [
        // ── favorites_products ────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS favorites_products (
            user_id    VARCHAR(36)  NOT NULL,
            product_id VARCHAR(36)  NOT NULL,
            created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (user_id, product_id),
            CONSTRAINT fk_favorites_product
                FOREIGN KEY (product_id) REFERENCES products (product_id)
                ON DELETE CASCADE
        )
        """,

        // ── contact_messages ──────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS contact_messages (
            id         INT          NOT NULL AUTO_INCREMENT,
            name       VARCHAR(120) NOT NULL,
            email      VARCHAR(255) NOT NULL,
            subject    VARCHAR(255) NULL,
            message    TEXT         NOT NULL,
            created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (id)
        )
        """,

        // ── store_hours ───────────────────────────────────────────────────────
        // 0 = Sunday .. 6 = Saturday. Times are in the store's configured timezone.
        """
        CREATE TABLE IF NOT EXISTS store_hours (
            day_of_week TINYINT    NOT NULL PRIMARY KEY,
            open_time   TIME       NOT NULL DEFAULT '09:00:00',
            close_time  TIME       NOT NULL DEFAULT '18:00:00',
            is_closed   TINYINT(1) NOT NULL DEFAULT 0,
            updated_at  TIMESTAMP  NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        )
        """,

        // Seed a full week so the admin edit form always has seven rows to render.
        """
        INSERT IGNORE INTO store_hours (day_of_week, open_time, close_time, is_closed) VALUES
        (0, '09:00:00', '18:00:00', 0),
        (1, '09:00:00', '18:00:00', 0),
        (2, '09:00:00', '18:00:00', 0),
        (3, '09:00:00', '18:00:00', 0),
        (4, '09:00:00', '18:00:00', 0),
        (5, '09:00:00', '14:00:00', 0),
        (6, '09:00:00', '18:00:00', 1)
        """,
    ];
}
