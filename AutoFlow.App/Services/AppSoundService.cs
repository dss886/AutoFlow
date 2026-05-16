using System.IO;
using System.Media;

namespace AutoFlow.App.Services;

public sealed class AppSoundService
{
    private const string ResourceUriPrefix = "pack://application:,,,/Assets/Sounds/";

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
                var uri = new Uri(ResourceUriPrefix + fileName, UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo == null)
                {
                    return;
                }

                player = new SoundPlayer(streamInfo.Stream);
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
