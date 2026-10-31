using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class WeatherTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the rain follows the original's cycle of clouds, rain and clouds, and each cycle draws its own rain and wind", ScheduleAsync);
        yield return ("dungeons, rooms, the ice fields and Bellatra never take rain", FieldsAsync);
        yield return ("a game master's /weather sets every open field's weather, a dungeon stays clear, and auto gives it back", GameMasterAsync);
        yield return ("the weather reaches the client once when it changes and its packet refuses an unknown kind", WireAsync);
    }

    private static long At(long cycle, int minute) => (cycle * WeatherSchedule.DefaultCycleMinutes + minute) * WorldClock.MinuteMs;

    private static Task ScheduleAsync()
    {
        var start = WeatherSchedule.CloudsMinute;
        var end = start + WeatherSchedule.CloudyMinutes;
        var ramp = WeatherSchedule.RampMinutes;
        Assert.True(WeatherSchedule.At(At(3, start)) == WeatherState.Clear && WeatherSchedule.At(At(3, end)) == WeatherState.Clear,
            "the sky was not clear at the cloudy spell's edges");
        Assert.True(WeatherSchedule.At(At(3, start + 1)).Kind == WeatherKind.Overcast && WeatherSchedule.At(At(3, start + ramp)).Kind == WeatherKind.Overcast
            && WeatherSchedule.At(At(3, end - ramp)).Kind == WeatherKind.Overcast, "the clouds did not come an hour before and after the rain");
        var raining = WeatherSchedule.At(At(3, start + ramp + 1));
        Assert.True(raining.Raining && raining.Intensity > 0 && WeatherSchedule.At(At(3, end - ramp - 1)) == raining
            && WeatherSchedule.At(At(3, start + 1)).Intensity == 0, $"the rain in the cloudy spell was {raining}");

        var kinds = new HashSet<(WeatherKind, byte)>();
        for (var cycle = 0; cycle < 200; cycle++)
        {
            var rain = WeatherSchedule.At(At(cycle, start + 200));
            Assert.True(rain.Raining && rain == WeatherSchedule.At(At(cycle, start + 200)), $"cycle {cycle} did not rain the same at its middle");
            kinds.Add((rain.Kind, rain.Intensity));
        }

        Assert.True(kinds.SetEquals([(WeatherKind.Rain, WeatherSchedule.LightRain), (WeatherKind.Rain, WeatherSchedule.SteadyRain),
            (WeatherKind.Rain, WeatherSchedule.HeavyRain), (WeatherKind.Storm, WeatherSchedule.StormRain)]), "the cycles did not draw every rain");
        var shortCycle = WeatherSchedule.MinimumCycleMinutes;
        Assert.True(WeatherSchedule.At((shortCycle + start + ramp + 1) * (long)WorldClock.MinuteMs, shortCycle).Raining,
            "a shorter cycle did not rain in its second cycle");
        return Task.CompletedTask;
    }

    private static Task FieldsAsync()
    {
        FieldDef Field(FieldState state, int code = 1) => new("f", "F", state, new FieldBackdrop("", "", ""), "", []) { FieldCode = code };
        Assert.True(WeatherSchedule.Rains(Field(FieldState.Forest)) && WeatherSchedule.Rains(Field(FieldState.Village))
            && WeatherSchedule.Rains(Field(FieldState.Desert)), "an open field did not take rain");
        Assert.True(!WeatherSchedule.Rains(Field(FieldState.Dungeon)) && !WeatherSchedule.Rains(Field(FieldState.Room))
            && !WeatherSchedule.Rains(Field(FieldState.Ice)) && !WeatherSchedule.Rains(Field(FieldState.Forest, FieldCodes.Bellatra)),
            "a covered field, an ice field or Bellatra took rain");
        return Task.CompletedTask;
    }

    private static async Task GameMasterAsync()
    {
        var open = new ChatFixture();
        var master = await open.JoinAsync("Master", 0, 0, gmLevel: 2);
        await open.SayAsync(master, "/weather storm 200");
        open.Tick(1);
        Assert.True(ChatFixture.Latest(master)?.Weather == new WeatherState(WeatherKind.Storm, 200, 32, 192),
            $"the field's weather was {ChatFixture.Latest(master)?.Weather} after /weather storm 200");
        Assert.True(master.Link.Last.StartsWith("> Weather Storm 200"), $"the command answered {master.Link.Last}");
        await open.SayAsync(master, "/weather clear");
        open.Tick(1);
        Assert.True(ChatFixture.Latest(master)?.Weather == WeatherState.Clear, "/weather clear left the rain");
        await open.SayAsync(master, "/weather auto");
        Assert.True(master.Link.Last.EndsWith("(schedule)"), $"/weather auto answered {master.Link.Last}");

        var dungeon = new ChatFixture(state: FieldState.Dungeon);
        var below = await dungeon.JoinAsync("Below", 0, 0, gmLevel: 2);
        await dungeon.SayAsync(below, "/weather rain");
        dungeon.Tick(1);
        Assert.True(ChatFixture.Latest(below)?.Weather == WeatherState.Clear, "it rained in a dungeon");
    }

    private static Task WireAsync()
    {
        var storm = new WeatherState(WeatherKind.Storm, 255, 200, 180);
        var buffer = new byte[WeatherPacket.Size];
        Assert.True(WeatherPacket.Read(buffer.AsSpan(0, new WeatherPacket(storm).Write(buffer))).Weather == storm, "the weather did not read back");
        buffer[WeatherPacket.Size - 4] = (byte)WeatherKind.Storm + 1;
        Assert.Throws<InvalidDataException>(() => WeatherPacket.Read(buffer), "an unknown weather kind was read");

        var wire = new WireRoundTrip(1);
        var self = new EntityView(1, EntityKind.Player, true, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100, default);
        WeatherState? Received(WeatherState weather) =>
            wire.Push(new WorldSnapshot(1, [self], fieldId: 3) { Weather = weather }).LastOrDefault(static update => update.Weather is not null)?.Weather;
        Assert.True(Received(WeatherState.Clear) == WeatherState.Clear, "the first snapshot did not send the weather");
        Assert.True(Received(WeatherState.Clear) is null, "an unchanged weather was sent again");
        Assert.True(Received(storm) == storm, "the storm did not reach the client");
        return Task.CompletedTask;
    }
}
