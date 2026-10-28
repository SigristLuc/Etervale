using PristonTale.Application.Chat;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Contacts;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.Contacts;

// A name on a list as its owner sees it: a friend's presence (playing now); always false on the ignored list.
public readonly record struct ContactEntry(string Name, bool Online);

public sealed record ContactLists(IReadOnlyList<ContactEntry> Friends, IReadOnlyList<ContactEntry> Ignored);

// The friends page on the server: a character's lists are loaded when it joins the chat (ChatMember.Contacts), changed and
// stored one name at a time, and answered with each friend's presence. A refused change answers its line in the chat.
// One connection's requests come one at a time, so a check and the change it allows never interleave.
public sealed class ContactAppService(ChatDirectory chat, IContactRepository contacts)
{
    public async Task<ContactBook> LoadAsync(long characterId, CancellationToken ct = default) =>
        new((await contacts.ListAsync(characterId, ct)).Select(static record => (record.List, record.Name)));

    public async Task<ContactLists?> ChangeAsync(ulong entityId, ContactList list, bool add, string name, CancellationToken ct = default)
    {
        if (!chat.TryGet(entityId, out var member) || member.Session.Loaded is not { } loaded)
        {
            return null;
        }

        var book = member.Contacts;
        name = name.Trim();
        var listed = add ? null : book.Find(list, name);
        var status = add ? book.Check(list, name, member.Name)
            : !book.Available ? ContactStatus.Unavailable
            : listed is null ? ContactStatus.NotListed
            : ContactStatus.Ok;
        if (status != ContactStatus.Ok)
        {
            member.Link.Deliver(ChatLine.Reply(ContactMessages.Of(status, name)));
        }
        else if (add)
        {
            await contacts.AddAsync(loaded.Id, list, name, ct);
            book.Add(list, name);
        }
        else
        {
            await contacts.RemoveAsync(loaded.Id, list, listed!, ct);
            book.Remove(list, listed!);
        }

        return Lists(member);
    }

    public ContactLists? Lists(ulong entityId) => chat.TryGet(entityId, out var member) ? Lists(member) : null;

    private ContactLists Lists(ChatMember member)
    {
        var book = member.Contacts;
        return new ContactLists(book.Friends.Select(name => new ContactEntry(name, chat.FindByName(name) is not null)).ToArray(),
            book.Ignored.Select(static name => new ContactEntry(name, false)).ToArray());
    }
}
