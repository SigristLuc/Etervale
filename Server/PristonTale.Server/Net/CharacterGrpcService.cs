using Grpc.Core;
using PristonTale.Application.Services;
using PristonTale.Protocol;
using PristonTale.Protocol.Character;
using AppCharacterSummary = PristonTale.Application.Services.CharacterSummary;
using ProtoCharacterSummary = PristonTale.Protocol.Character.CharacterSummary;

namespace PristonTale.Server.Net;

public sealed class CharacterGrpcService : CharacterService.CharacterServiceBase
{
    private readonly CharacterAppService _characters;

    public CharacterGrpcService(CharacterAppService characters)
    {
        _characters = characters;
    }

    public override async Task<ListCharactersReply> ListCharacters(ListCharactersRequest request, ServerCallContext context)
    {
        IReadOnlyList<AppCharacterSummary> summaries;
        try
        {
            summaries = await _characters.ListCharactersAsync(request.SessionToken, context.CancellationToken);
        }
        catch (InvalidSessionException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid session"));
        }

        var reply = new ListCharactersReply();
        foreach (var summary in summaries)
        {
            reply.Characters.Add(new ProtoCharacterSummary
            {
                Name = summary.Name,
                ClassId = summary.ClassId,
                Level = summary.Level,
                FaceIndex = summary.FaceIndex,
                Tier = summary.Tier,
                HairIndex = summary.HairIndex,
                ArmorKey = summary.ArmorKey,
                WeaponKey = summary.WeaponKey,
                OffHandKey = summary.OffHandKey,
                InVillage = summary.InVillage,
            });
        }
        return reply;
    }

    public override async Task<CreateCharacterReply> CreateCharacter(CreateCharacterRequest request, ServerCallContext context)
    {
        CreateCharacterOutcome outcome;
        try
        {
            outcome = await _characters.CreateCharacterAsync(
                request.SessionToken, request.Name, request.ClassId, request.FaceIndex, context.CancellationToken);
        }
        catch (InvalidSessionException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid session"));
        }

        var reply = new CreateCharacterReply { Ok = outcome.Ok, Message = outcome.Message };
        if (outcome.Character is { } character)
        {
            reply.Character = new ProtoCharacterSummary
            {
                Name = character.Name,
                ClassId = character.ClassId,
                Level = character.Level,
                FaceIndex = character.FaceIndex,
                Tier = character.Tier,
                HairIndex = character.HairIndex,
                ArmorKey = character.ArmorKey,
                WeaponKey = character.WeaponKey,
                OffHandKey = character.OffHandKey,
                InVillage = character.InVillage,
            };
        }
        return reply;
    }

    public override async Task<DeleteCharacterReply> DeleteCharacter(DeleteCharacterRequest request, ServerCallContext context)
    {
        DeleteCharacterOutcome outcome;
        try
        {
            outcome = await _characters.DeleteCharacterAsync(
                request.SessionToken, request.Name, context.CancellationToken);
        }
        catch (InvalidSessionException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid session"));
        }

        return new DeleteCharacterReply { Ok = outcome.Ok, Message = outcome.Message };
    }
}
