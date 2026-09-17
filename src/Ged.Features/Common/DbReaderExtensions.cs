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
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> has no declaration that both engines read the same way. Failing
    /// here is the point: the alternative is a parameter that reaches the provider undeclared,
    /// which SQL Server accepts and PostgreSQL rejects, so the defect ships and surfaces on one
    /// engine only.
    /// </exception>
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
        parameter.DbType = DbTypeOf(typeof(T));

        command.Parameters.Add(parameter);
    }

    /// <summary>Maps a CLR type to the database type the provider must be told about.</summary>
    /// <param name="type">The CLR type, nullable wrapper already allowed for.</param>
    /// <returns>The database type.</returns>
    /// <remarks>
    /// <para>
    /// The map is deliberately total: there is no "the provider will work it out" case, because
    /// the position that carries no type information is exactly the position this helper exists
    /// for. An unmapped type is a mistake to report, not a value to send and hope for.
    /// </para>
    /// <para>
    /// Two omissions are decisions rather than gaps. <see cref="DateTime"/> has no correct
    /// declaration here: <see cref="DbType.DateTime2"/> reaches PostgreSQL as
    /// <c>timestamp without time zone</c>, and every instant column in this schema is
    /// <c>timestamptz</c> — an instant is a <see cref="DateTimeOffset"/>, which is also what
    /// <see cref="IClock"/> hands out. <see cref="TimeSpan"/> is a duration here — a retention
    /// window — and the only declaration that fits it on both engines is
    /// <see cref="DbType.Time"/>, a time of day, which silently truncates anything past
    /// twenty-four hours. A duration is turned into an instant before it reaches a parameter.
    /// </para>
    /// </remarks>
    private static DbType DbTypeOf(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.String => DbType.String,
            TypeCode.Boolean => DbType.Boolean,
            TypeCode.Int16 => DbType.Int16,
            TypeCode.Int32 => DbType.Int32,
            TypeCode.Int64 => DbType.Int64,
            TypeCode.Single => DbType.Single,
            TypeCode.Double => DbType.Double,
            TypeCode.Decimal => DbType.Decimal,
            _ when underlying == typeof(Guid) => DbType.Guid,
            _ when underlying == typeof(DateTimeOffset) => DbType.DateTimeOffset,
            _ when underlying == typeof(DateOnly) => DbType.Date,
            _ when underlying == typeof(TimeOnly) => DbType.Time,
            _ when underlying == typeof(byte[]) => DbType.Binary,
            _ => throw new NotSupportedException(
                $"No database type is declared for '{underlying}', so the parameter would reach "
                + "the provider untyped. Pass a type that has a declaration — an instant is a "
                + "DateTimeOffset, an entity identifier is its underlying Guid — or add the "
                + "mapping here, once it reads the same way on PostgreSQL and SQL Server."),
        };
    }
}
