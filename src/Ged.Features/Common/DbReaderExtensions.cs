using System.Data.Common;

namespace Ged.Features.Common;

/// <summary>Typed, explicit column access for read handlers.</summary>
/// <remarks>
/// Every read handler names the columns it consumes. A renamed column then fails at the line that
/// reads it, with the column name in the message — rather than silently producing a default that
/// travels to a screen and is noticed weeks later.
/// </remarks>
public static class DbReaderExtensions
{
    /// <summary>Reads a required string.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value.</returns>
    public static string GetString(this DbDataReader reader, string column) =>
        reader!.GetString(reader.GetOrdinal(column));

    /// <summary>Reads an optional string.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or null.</returns>
    public static string? GetNullableString(this DbDataReader reader, string column)
    {
        var ordinal = reader!.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>Reads a required identifier.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value.</returns>
    public static Guid GetGuid(this DbDataReader reader, string column) =>
        reader!.GetGuid(reader.GetOrdinal(column));

    /// <summary>Reads an optional identifier.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or null.</returns>
    public static Guid? GetNullableGuid(this DbDataReader reader, string column)
    {
        var ordinal = reader!.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    /// <summary>Reads a required 32-bit integer.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value.</returns>
    public static int GetInt32(this DbDataReader reader, string column) =>
        reader!.GetInt32(reader.GetOrdinal(column));

    /// <summary>Reads a required 64-bit integer.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value.</returns>
    public static long GetInt64(this DbDataReader reader, string column) =>
        reader!.GetInt64(reader.GetOrdinal(column));

    /// <summary>Reads a required instant.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value.</returns>
    public static DateTimeOffset GetInstant(this DbDataReader reader, string column) =>
        reader!.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(column));

    /// <summary>Reads an optional instant.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or null.</returns>
    public static DateTimeOffset? GetNullableInstant(this DbDataReader reader, string column)
    {
        var ordinal = reader!.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    /// <summary>Adds a parameter to a command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name, without its prefix.</param>
    /// <param name="value">The value. Null becomes <see cref="DBNull"/>.</param>
    public static void AddParameter(this DbCommand command, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
