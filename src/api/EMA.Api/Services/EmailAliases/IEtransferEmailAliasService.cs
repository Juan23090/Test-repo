namespace EMA_Api.Services.EmailAliases;

public interface IEtransferEmailAliasService
{
    Task<bool> AliasExistsAsync(
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task AddAliasAsync(
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task EnsureAliasAsync(
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task RemoveAliasIfExistsAsync(
        string emailAddress,
        CancellationToken cancellationToken = default);
}

public sealed class EtransferAliasInUseException : Exception
{
    public EtransferAliasInUseException(
        string emailAddress)
        : base(
            $"{emailAddress} is already in use. Please choose another email prefix.")
    {
        EmailAddress = emailAddress;
    }

    public string EmailAddress { get; }
}

public sealed class EtransferAliasBusyException : Exception
{
    public EtransferAliasBusyException(
        string emailAddress)
        : base(
            $"{emailAddress} is currently being changed by another request. Please check the address again.")
    {
        EmailAddress = emailAddress;
    }

    public string EmailAddress { get; }
}