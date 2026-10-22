namespace EMA.Api.Services.Sandbox;

public sealed class SandboxAlreadyExistsException :
    InvalidOperationException
{
    public SandboxAlreadyExistsException()
        : base(
            "An active sandbox already exists for this location.")
    {
    }
}
