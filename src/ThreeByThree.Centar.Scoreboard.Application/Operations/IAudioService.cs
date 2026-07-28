using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Operations;

public interface IAudioService
{
    void ApplySettings(AppSettings settings);

    void Play(BuzzerKind buzzer);

    void Test(int volumePercent);
}
