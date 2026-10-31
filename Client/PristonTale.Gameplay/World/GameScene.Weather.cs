using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World;

// The weather of the local player's field as the server last sent it (clear until it does).
public sealed partial class GameScene
{
    public WeatherState ServerWeather { get; private set; }

    private void ObserveWeather(PresenceUpdate update)
    {
        if (update.Weather is { } weather)
        {
            ServerWeather = weather;
        }
    }
}
