using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>
/// How the video reaches the player. By default the original file is sent (direct play: VLC plays almost anything). A
/// quality limit, a file the server cannot send as it is, or a direct play that fails right after opening makes the
/// server convert the video to HLS instead (transcoding). Quality, audio and burned-in subtitle changes of a
/// converted video start a new stream at the current position; the server's conversion is stopped when it is no
/// longer needed.
/// </summary>
public sealed partial class PlayerViewModel
{
    private readonly List<int> _subtitleFiles = [];
    private PlaybackRequest _request = new();
    private int? _maxBitrate;
    private bool _fallbackTried;
    private bool _switching;
    private bool _transcodeEnded;
    private bool _tracksChosen;
    private int? _audioIndex;
    private int? _subtitleIndex;
    private DateTime _openedAt;

    /// <summary>What the server answered for a request, with the request that produced it.</summary>
    private sealed record Negotiated(PlaybackRequest Request, PlaybackDetails Details);

    private bool IsVideo => _item.MediaType == "Video";
    private bool IsTranscoding => _details.Method == PlayMethod.Transcode;

    public override bool CanChooseQuality => IsVideo;

    // ---- Starting ----------------------------------------------------------------------------------------------

    /// <summary>Asks the server how to play (with the quality limit from the settings) and opens the stream.</summary>
    private async Task StartStreamAsync()
    {
        _maxBitrate = IsVideo ? _settings.Settings.MaxStreamingBitrate : null;
        var wish = new PlaybackRequest { MaxBitrate = _maxBitrate };
        var negotiated = await NegotiateAsync(wish);

        if (negotiated is null)
        {
            // Without PlaybackInfo the file still plays; only server track names and defaults are missing.
            negotiated = new Negotiated(wish, PlaybackDetails.Fallback(_item));
        }
        else if (StreamingPolicy.Evaluate(negotiated.Details) is var outcome && outcome != StreamOutcome.Play)
        {
            // The saved quality cannot be had (the user may not transcode): play the original instead.
            ShowNotice($"{StreamingPolicy.NoStreamMessage(outcome)}. Playing the original.");
            _maxBitrate = null;
            negotiated = await NegotiateAsync(new PlaybackRequest()) is { } original && StreamingPolicy.Evaluate(original.Details) == StreamOutcome.Play
                ? original
                : new Negotiated(new PlaybackRequest(), PlaybackDetails.Fallback(_item) with { Chapters = negotiated.Details.Chapters });
        }
        else if (negotiated.Details.Method == PlayMethod.Transcode && _maxBitrate is null)
        {
            // Nobody asked for a conversion: say why the server insists on one.
            ShowNotice(StreamingPolicy.TranscodeNotice(negotiated.Details.TranscodeReasons));
        }

        await BeginAsync(negotiated.Request, negotiated.Details, StartPosition);
    }

    private async Task BeginAsync(PlaybackRequest request, PlaybackDetails details, TimeSpan startAt)
    {
        _request = request;
        _details = details;
        OnPropertyChanged(nameof(Chapters));
        _playSessionId = details.PlaySessionId ?? Guid.NewGuid().ToString("N");
        _streamStart = startAt;
        _lastPosition = startAt;
        _reportedStart = false;
        _stopped = false;
        _tracksPrepared = false;
        _transcodeEnded = false;
        _pendingSubtitlePosition = null;
        _subtitleFiles.Clear();
        UpdateMode();
        ErrorMessage = null;

        _log.Information("Streaming by {Method}{Limit}", details.Method,
            request.MaxBitrate is { } limit ? $" up to {QualityPresets.FormatBitrate(limit)}" : "");
        try
        {
            _openedAt = DateTime.UtcNow;
            await Player.PlayAsync(StreamUri(details), startAt);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not start playback");
            ErrorMessage = "Playback could not be started.";
        }
    }

    private Uri StreamUri(PlaybackDetails details) =>
        details.Method == PlayMethod.Transcode && details.TranscodeUri is { } uri ? uri : _client.GetStreamUri(_item, details.MediaSourceId);

    private void UpdateMode()
    {
        ModeText = IsVideo ? StreamingPolicy.Describe(_details.Method, _request.MaxBitrate) : null;
        QualityText = QualityPresets.ShortLabel(_maxBitrate);
    }

    // ---- Asking the server -------------------------------------------------------------------------------------

    /// <summary>One PlaybackInfo request; null when the server cannot be reached or answers nonsense.</summary>
    private async Task<PlaybackDetails?> RequestAsync(PlaybackRequest request)
    {
        try
        {
            return await _client.GetPlaybackDetailsAsync(_item, request);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _log.Information("Playback info unavailable: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Asks the server how to play <paramref name="wish"/>. A converted video carries one audio track and the
    /// subtitles burned into the picture, chosen by the request, so when the server decided those itself the request
    /// is repeated with the tracks we want (and a sensible bitrate if none was set).
    /// </summary>
    private async Task<Negotiated?> NegotiateAsync(PlaybackRequest wish)
    {
        var details = await RequestAsync(wish);
        if (details is null)
            return null;

        if (details.Method == PlayMethod.Transcode && wish.AudioStreamIndex is null && details.Streams.Count > 0)
        {
            ChooseTracks(details);
            wish = SpecifyTracks(wish, details) with { MaxBitrate = wish.MaxBitrate ?? StreamingPolicy.FallbackBitrate };
            if (await RequestAsync(wish) is { } specific)
                details = specific with { Chapters = details.Chapters };
        }

        return new Negotiated(wish, details);
    }

    /// <summary>The audio and the subtitle (burned in, or -1 for none) a converted stream should have.</summary>
    private PlaybackRequest SpecifyTracks(PlaybackRequest request, PlaybackDetails details)
    {
        var burn = _subtitleIndex is { } index && details.SubtitleStreams.FirstOrDefault(s => s.Index == index) is { } stream
                   && StreamingPolicy.IsImageSubtitle(stream.Codec);
        return request with
        {
            MediaSourceId = details.MediaSourceId,
            AudioStreamIndex = _audioIndex ?? details.DefaultAudioStreamIndex ?? details.AudioStreams.OrderBy(s => s.Index).FirstOrDefault()?.Index,
            SubtitleStreamIndex = burn ? _subtitleIndex : -1,
            IncludeChapters = false,
        };
    }

    /// <summary>The starting audio and subtitle from the preferences and the server's defaults, once the streams are known.</summary>
    private void ChooseTracks(PlaybackDetails details)
    {
        if (_tracksChosen || details.Streams.Count == 0)
            return;
        var choice = TrackSelector.Select(details.Streams, details.DefaultAudioStreamIndex, details.DefaultSubtitleStreamIndex,
            _settings.Settings.GetTrackPreferences());
        _audioIndex = choice.AudioStreamIndex;
        _subtitleIndex = choice.SubtitleStreamIndex;
        _tracksChosen = true;
    }

    // ---- Changing the stream -----------------------------------------------------------------------------------

    private TimeSpan CurrentPosition() => Player.Position > TimeSpan.Zero ? Player.Position : _lastPosition;

    /// <summary>
    /// Moves playback to a new stream at the current position. The server is asked first, so a refusal (the user may
    /// not transcode) leaves the current playback untouched. Returns false when nothing changed.
    /// </summary>
    private async Task<bool> SwitchAsync(PlaybackRequest wish, bool restartAlways, bool announce)
    {
        if (_switching)
            return false;
        _switching = true;
        try
        {
            var position = CurrentPosition();
            if (await NegotiateAsync(wish) is not { } negotiated)
            {
                ShowNotice("Could not reach the server to change the stream");
                return false;
            }

            var (request, details) = negotiated;
            if (StreamingPolicy.Evaluate(details) is var outcome && outcome != StreamOutcome.Play)
            {
                ShowNotice(StreamingPolicy.NoStreamMessage(outcome));
                return false;
            }

            if (!restartAlways && details.Method != PlayMethod.Transcode && !IsTranscoding)
            {
                // Still the original file (the limit is above its bitrate): nothing to restart.
                _request = request;
                return true;
            }

            await EndStreamAsync();
            await BeginAsync(request, details with { Chapters = _details.Chapters }, position);
            if (announce && details.Method == PlayMethod.Transcode)
                ShowNotice(StreamingPolicy.TranscodeNotice(details.TranscodeReasons));
            return true;
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>Reports the old stream finished, stops the player and the server's conversion of it.</summary>
    private async Task EndStreamAsync()
    {
        ReportStopped();
        var stopping = Player.StopAsync();
        EndTranscode(stopping);
        await stopping;
    }

    /// <summary>
    /// Tells the server to stop converting the video of this play session (as jellyfin-web does) once the player has
    /// let go of the stream, so its ffmpeg job does not run on for nothing.
    /// </summary>
    private void EndTranscode(Task playerStopped)
    {
        if (!IsTranscoding || _transcodeEnded)
            return;
        _transcodeEnded = true;
        var sessionId = _playSessionId;
        _ = Task.Run(async () =>
        {
            try
            {
                await playerStopped.ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _client.StopTranscodingAsync(sessionId, timeout.Token);
                _log.Information("Stopped the server's conversion of this stream");
            }
            catch (Exception ex)
            {
                _log.Information("Could not stop the server's conversion: {Error}", ex.Message);
            }
        });
    }

    // ---- Quality -----------------------------------------------------------------------------------------------

    public override IReadOnlyList<MenuOption> QualityOptions() =>
        QualityPresets.With(_maxBitrate)
            .Select(p => new MenuOption(p.Label, p.MaxBitrate == _maxBitrate, () => _ = ChangeQualityAsync(p.MaxBitrate)))
            .ToList();

    private async Task ChangeQualityAsync(int? bitrate)
    {
        if (bitrate == _maxBitrate || _switching)
            return;
        var wish = SpecifyTracks(new PlaybackRequest { MaxBitrate = bitrate }, _details);
        if (!await SwitchAsync(wish, restartAlways: false, announce: false))
            return;

        // Remembered for the next video, like the web client's quality setting.
        _maxBitrate = bitrate;
        _fallbackTried = false;
        _settings.Settings.MaxStreamingBitrate = bitrate;
        _settings.Save();
        UpdateMode();
    }

    // ---- Direct play failed ------------------------------------------------------------------------------------

    /// <summary>The player gave up on the stream: if direct play fails right after opening, try a conversion once.</summary>
    private void OnStreamFailed()
    {
        if (_switching || !StreamingPolicy.ShouldFallBack(_details.Method, _fallbackTried, IsVideo, DateTime.UtcNow - _openedAt))
            return;
        _fallbackTried = true;
        _log.Information("Direct play failed, asking the server to convert the video");
        var wish = SpecifyTracks(StreamingPolicy.FallbackRequest(new PlaybackRequest { MaxBitrate = _maxBitrate }), _details);
        _ = SwitchAsync(wish, restartAlways: true, announce: true);
    }

    // ---- Tracks of a converted video ---------------------------------------------------------------------------

    private IReadOnlyList<MenuOption> TranscodeAudioOptions()
    {
        var current = _details.TranscodeAudioStreamIndex ?? _audioIndex;
        return ServerAudio
            .Select(s => new MenuOption(s.DisplayTitle, s.Index == current, () => _ = ChangeTracksAsync(audio: s.Index)))
            .ToList();
    }

    private IReadOnlyList<MenuOption> TranscodeSubtitleOptions() =>
        _details.SubtitleStreams.OrderBy(s => s.Index)
            .Select(s => new MenuOption(
                StreamingPolicy.IsImageSubtitle(s.Codec) ? $"{SubtitleLabel(s)} (burned in)" : SubtitleLabel(s),
                s.Index == _subtitleIndex,
                () => SelectTranscodeSubtitle(s)))
            .Prepend(new MenuOption("Off", _subtitleIndex is null, () => SelectTranscodeSubtitle(null)))
            .ToList();

    private void SelectTranscodeSubtitle(MediaStreamInfo? stream)
    {
        var burned = _details.BurnedSubtitleStreamIndex;
        if ((stream is not null && StreamingPolicy.IsImageSubtitle(stream.Codec)) || burned is not null)
        {
            // Burned-in subtitles are part of the picture: changing them needs a new stream.
            if (burned != stream?.Index)
                _ = ChangeTracksAsync(subtitle: stream?.Index, changeSubtitle: true);
            return;
        }

        _subtitleIndex = stream?.Index;
        ShowSubtitleFile(stream);
    }

    /// <summary>Text subtitles of a converted video come as files from the server, added when first wanted.</summary>
    private void ShowSubtitleFile(MediaStreamInfo? stream)
    {
        if (stream is null)
        {
            Player.SelectSubtitleTrack(-1);
            return;
        }

        var position = _subtitleFiles.IndexOf(stream.Index);
        var tracks = Player.SubtitleTracks;
        if (position < 0)
        {
            _subtitleFiles.Add(stream.Index);
            Player.AddExternalSubtitle(_client.GetSubtitleUri(_item, _details.MediaSourceId, stream), select: true);
        }
        else if (tracks.Count == _subtitleFiles.Count)
        {
            Player.SelectSubtitleTrack(tracks[position].Id);
        }
    }

    /// <summary>
    /// Subtitle files use the video's own timeline, but the player shows the HLS stream 10 s behind its timestamps
    /// (see <see cref="StreamingPolicy.HlsTimestampOffset"/>), so for a converted video the subtitles are delayed by
    /// that much; the user's subtitle delay is counted from there.
    /// </summary>
    protected override TimeSpan SubtitleBaseDelay => IsTranscoding ? StreamingPolicy.HlsTimestampOffset : TimeSpan.Zero;

    private void PrepareTranscodeSubtitles()
    {
        Player.SubtitleDelay = SubtitleBaseDelay;
        var wanted = _subtitleIndex is { } index && _details.BurnedSubtitleStreamIndex != index
            ? _details.SubtitleStreams.FirstOrDefault(s => s.Index == index && !StreamingPolicy.IsImageSubtitle(s.Codec))
            : null;
        if (wanted is null)
            Player.SelectSubtitleTrack(-1);
        else
            ShowSubtitleFile(wanted);
    }

    private async Task ChangeTracksAsync(int? audio = null, int? subtitle = null, bool changeSubtitle = false)
    {
        var (oldAudio, oldSubtitle) = (_audioIndex, _subtitleIndex);
        _audioIndex = audio ?? _audioIndex;
        if (changeSubtitle)
            _subtitleIndex = subtitle;
        if (!await SwitchAsync(SpecifyTracks(_request, _details), restartAlways: true, announce: false))
            (_audioIndex, _subtitleIndex) = (oldAudio, oldSubtitle);
    }
}
