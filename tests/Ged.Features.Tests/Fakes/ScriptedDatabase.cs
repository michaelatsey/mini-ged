using System.Collections;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Data.Common;

namespace Ged.Features.Tests.Fakes;

/// <summary>
/// The collector's read side, scripted. The purge job writes its own SQL rather than going through
/// a repository, so a test has to answer the three questions it asks a database: which candidates
/// are due, whether one is still referenced, and what its row says now.
/// </summary>
/// <remarks>
/// Each statement is recognised by a fragment of its own text. A renamed column would go unnoticed
/// here — that is what the container smoke test is for; this fake exists to pin down the
/// <em>order</em> in which the job commits and deletes, which no database can demonstrate.
/// </remarks>
internal sealed class ScriptedDatabase : IDbConnectionFactory
{
    /// <summary>Gets the digests the candidate query returns.</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>Gets how many live versions reference a digest.</summary>
    public Func<string, long> ReferenceCount { get; init; } = _ => 0;

    /// <summary>Gets what the blob row says at the moment the job reads it back.</summary>
    public Func<string, string> Status { get; init; } = _ => BlobStatus.Purged.Code;

    public ValueTask<DbConnection> OpenAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<DbConnection>(new ScriptedConnection(this));
}

internal sealed class ScriptedConnection(ScriptedDatabase database) : DbConnection
{
    [AllowNull]
    public override string ConnectionString { get; set; } = "scripted";

    public override string Database => "scripted";

    public override string DataSource => "scripted";

    public override string ServerVersion => "0";

    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close() { }

    public override void Open() { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new ScriptedCommand(this, database);
}

internal sealed class ScriptedCommand(DbConnection connection, ScriptedDatabase database) : DbCommand
{
    private readonly ScriptedParameters _parameters = [];

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; } = connection;

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }

    public override void Prepare() { }

    public override int ExecuteNonQuery() => 0;

    public override object? ExecuteScalar() =>
        CommandText.Contains("COUNT(*)", StringComparison.Ordinal)
            ? database.ReferenceCount(Digest())
            : CommandText.Contains("SELECT status", StringComparison.Ordinal)
                ? database.Status(Digest())
                : throw new NotSupportedException(CommandText);

    protected override DbParameter CreateDbParameter() => new ScriptedParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        CommandText.Contains("FETCH FIRST", StringComparison.Ordinal)
            ? new ScriptedReader("id", database.Candidates)
            : throw new NotSupportedException(CommandText);

    private string Digest() => (string)_parameters["blobId"].Value!;
}

internal sealed class ScriptedParameters : DbParameterCollection, IEnumerable<DbParameter>
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;

    public override object SyncRoot => _parameters;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);

        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value);
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) =>
        ((ICollection)_parameters).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

    public override int IndexOf(string parameterName) =>
        _parameters.FindIndex(p =>
            string.Equals(p.ParameterName, parameterName, StringComparison.Ordinal));

    public override void Insert(int index, object value) =>
        _parameters.Insert(index, (DbParameter)value);

    public override void Remove(object value) => _parameters.Remove((DbParameter)value);

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) =>
        _parameters[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value) =>
        _parameters[IndexOf(parameterName)] = value;

    IEnumerator<DbParameter> IEnumerable<DbParameter>.GetEnumerator() => _parameters.GetEnumerator();
}

internal sealed class ScriptedParameter : DbParameter
{
    public override DbType DbType { get; set; }

    public override ParameterDirection Direction { get; set; }

    public override bool IsNullable { get; set; }

    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    public override int Size { get; set; }

    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    public override bool SourceColumnNullMapping { get; set; }

    public override object? Value { get; set; }

    public override void ResetDbType() => DbType = DbType.Object;
}

internal sealed class ScriptedReader(string column, IReadOnlyList<string> rows) : DbDataReader
{
    private int _row = -1;

    public override int FieldCount => 1;

    public override bool HasRows => rows.Count > 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => 0;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read() => ++_row < rows.Count;

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => column;

    public override int GetOrdinal(string name) =>
        string.Equals(name, column, StringComparison.Ordinal)
            ? 0
            : throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown column.");

    public override string GetString(int ordinal) => rows[_row];

    public override object GetValue(int ordinal) => rows[_row];

    public override Type GetFieldType(int ordinal) => typeof(string);

    public override string GetDataTypeName(int ordinal) => "text";

    public override bool IsDBNull(int ordinal) => false;

    public override int GetValues(object[] values)
    {
        values[0] = rows[_row];

        return 1;
    }

    public override IEnumerator GetEnumerator() => rows.GetEnumerator();

    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

    public override byte GetByte(int ordinal) => throw new NotSupportedException();

    public override long GetBytes(
        int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(
        int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

    public override double GetDouble(int ordinal) => throw new NotSupportedException();

    public override float GetFloat(int ordinal) => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => throw new NotSupportedException();

    public override int GetInt32(int ordinal) => throw new NotSupportedException();

    public override long GetInt64(int ordinal) => throw new NotSupportedException();
}
