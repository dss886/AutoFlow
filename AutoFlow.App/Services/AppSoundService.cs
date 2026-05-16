using System.IO;
using System.Media;

namespace AutoFlow.App.Services;

public sealed class AppSoundService
{
    private static readonly string SoundsDirectory =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");

    private readonly Dictionary<string, SoundPlayer> _players = new(StringComparer.OrdinalIgnoreCase);

    public void PlayScriptStarted()
    {
        Play("start.wav");
    }

    public void PlayScriptStopped()
    {
        Play("stop.wav");
    }

    public void PlayRecordingStarted()
    {
        Play("start.wav");
    }

    public void PlayRecordingCompleted()
    {
        Play("stop.wav");
    }

    private void Play(string fileName)
    {
        try
        {
            if (!_players.TryGetValue(fileName, out var player))
            {
                var soundPath = Path.Combine(SoundsDirectory, fileName);
                if (!File.Exists(soundPath))
                {
                    return;
                }

                player = new SoundPlayer(soundPath);
                player.Load();
                _players[fileName] = player;
            }

            player.Play();
        }
        catch
        {
            // 音效播放失败不影响主流程。
        }
    }
}
