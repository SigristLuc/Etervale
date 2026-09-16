using Grpc.Core;
using PristonTale.Application.Services;
using PristonTale.Protocol;
using PristonTale.Protocol.Auth;

namespace PristonTale.Server.Net;

public sealed class AuthGrpcService : AuthService.AuthServiceBase
{
    private readonly AuthAppService _auth;

    public AuthGrpcService(AuthAppService auth)
    {
        _auth = auth;
    }

    public override async Task<LoginReply> Login(LoginRequest request, ServerCallContext context)
    {
        var outcome = await _auth.LoginAsync(request.Account, request.Password, context.CancellationToken);
        return new LoginReply
        {
            Ok = outcome.Ok,
            SessionToken = outcome.SessionToken,
            Message = outcome.Message,
        };
    }
}
