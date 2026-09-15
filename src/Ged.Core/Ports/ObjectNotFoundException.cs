using Ged.Domain.Blobs.ValueObjects;

namespace Ged.Core.Ports;

/// <summary>Thrown when a storage backend holds nothing at the requested address.</summary>
/// <remarks>
/// A distinct type so the location resolver can tell "this copy is gone" from "this backend is
/// unreachable". The first is repairable by falling back to another location; the second is not,
/// and retrying it against every location would turn one outage into a slow cascade.
/// </remarks>
public sealed class ObjectNotFoundException : Exception
{
    /// <summary>Initializes the exception for a missing object.</summary>
    /// <param name="provider">The backend queried.</param>
    /// <param name="key">The address queried.</param>
    public ObjectNotFoundException(StorageProvider provider, ObjectKey key)
        : base($"No object at '{key}' on provider '{provider}'.")
    {
        Provider = provider;
        Key = key;
    }

    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public ObjectNotFoundException(string message) : base(message) { }

    /// <summary>Initializes the exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectNotFoundException(string message, Exception? innerException)
        : base(message, innerException) { }

    /// <summary>Gets the backend that was queried, when known.</summary>
    public StorageProvider? Provider { get; }

    /// <summary>Gets the address that was queried, when known.</summary>
    public ObjectKey? Key { get; }
}
