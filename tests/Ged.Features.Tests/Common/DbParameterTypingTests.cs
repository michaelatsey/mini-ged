using System.Data;
using System.Data.Common;
using Ged.Domain.Folders;
using Ged.Features.Tests.Fakes;

namespace Ged.Features.Tests.Common;

/// <summary>
/// The one promise <see cref="DbReaderExtensions.AddParameter{T}"/> makes is that the provider is
/// never left to guess. These tests hold it to that promise for the types no slice happens to use
/// yet, because the day one does, the failure is a <c>42P08</c> from PostgreSQL in production and
/// nothing at all on SQL Server — the worst possible pair.
/// </summary>
public sealed class DbParameterTypingTests
{
    [Fact]
    public void A_null_date_declares_its_type()
    {
        // The reproduction from the issue: WHERE (@on IS NULL AND ...) carries no type information,
        // so an undeclared null here is the 42P08 the helper exists to prevent.
        Declare<DateOnly?>(null).DbType.ShouldBe(DbType.Date);
    }

    [Fact]
    public void A_null_time_declares_its_type() =>
        Declare<TimeOnly?>(null).DbType.ShouldBe(DbType.Time);

    [Fact]
    public void A_16_bit_integer_declares_its_type() => Declare<short>(7).DbType.ShouldBe(DbType.Int16);

    [Fact]
    public void A_single_precision_number_declares_its_type() =>
        Declare<float>(1.5f).DbType.ShouldBe(DbType.Single);

    [Fact]
    public void An_entity_identifier_is_refused_rather_than_sent_untyped()
    {
        // A FolderId is not a value any provider can send. Before, it went out as an untyped null
        // that PostgreSQL alone rejected, and at the statement rather than at the call.
        var act = () => Declare<FolderId?>(null);

        act.ShouldThrow<NotSupportedException>();
    }

    [Fact]
    public void A_date_time_is_refused_rather_than_sent_as_a_local_timestamp()
    {
        // DbType.DateTime2 reaches PostgreSQL as `timestamp without time zone`, which every instant
        // column in this schema is not. There is no declaration that is right on both engines.
        var act = () => Declare(Fixed.Now.UtcDateTime);

        act.ShouldThrow<NotSupportedException>();
    }

    [Fact]
    public void An_instant_declares_an_offset() =>
        Declare<DateTimeOffset?>(null).DbType.ShouldBe(DbType.DateTimeOffset);

    [Fact]
    public void A_null_identifier_declares_its_type() =>
        Declare<Guid?>(null).DbType.ShouldBe(DbType.Guid);

    private static DbParameter Declare<T>(T? value)
    {
        using var connection = new ScriptedConnection(new ScriptedDatabase());
        using var command = connection.CreateCommand();

        command.AddParameter<T>("p", value);

        return command.Parameters[0];
    }
}
