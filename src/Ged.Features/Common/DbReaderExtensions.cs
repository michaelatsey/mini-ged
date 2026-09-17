using System.Data;
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

    /// <summary>Adds a typed parameter to a command.</summary>
    /// <typeparam name="T">The value type. Its <see cref="DbType"/> is declared to the provider.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name, without its prefix.</param>
    /// <param name="value">The value. Null becomes <see cref="DBNull"/>.</param>
    /// <remarks>
    /// <para>
    /// The type is always declared, including for a null value — and especially then. A provider
    /// that receives an untyped null has to infer the type from where the parameter appears in the
    /// statement, and some positions carry no information at all:
    /// </para>
    /// <code>
    /// WHERE (@parentId IS NULL AND parent_id IS NULL)   -- nothing here says what @parentId is
    ///    OR (parent_id = @parentId)
    /// </code>
    /// <para>
    /// PostgreSQL answers that with <c>42P08: could not determine data type of parameter $1</c>,
    /// pointing at the first occurrence. Declaring the type removes the guesswork, and does so
    /// without a provider-specific cast in the SQL — <c>@parentId::uuid</c> would fix PostgreSQL and
    /// break SQL Server, in a slice that is meant to run on both.
    /// </para>
    /// </remarks>
    public static void AddParameter<T>(this DbCommand command, string name, T? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = (object?)value ?? DBNull.Value;

        if (DbTypeOf(typeof(T)) is { } dbType)
        {
            parameter.DbType = dbType;
        }

        command.Parameters.Add(parameter);
    }

    /// <summary>Maps a CLR type to the database type the provider should be told about.</summary>
    /// <param name="type">The CLR type, nullable wrapper already allowed for.</param>
    /// <returns>The database type, or null when the provider's own inference is adequate.</returns>
    private static DbType? DbTypeOf(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.String => DbType.String,
            TypeCode.Int32 => DbType.Int32,
            TypeCode.Int64 => DbType.Int64,
            TypeCode.Boolean => DbType.Boolean,
            TypeCode.Decimal => DbType.Decimal,
            TypeCode.Double => DbType.Double,
            TypeCode.DateTime => DbType.DateTime2,
            _ when underlying == typeof(Guid) => DbType.Guid,
            _ when underlying == typeof(DateTimeOffset) => DbType.DateTimeOffset,
            _ when underlying == typeof(byte[]) => DbType.Binary,
            _ => null,
        };
    }
}
