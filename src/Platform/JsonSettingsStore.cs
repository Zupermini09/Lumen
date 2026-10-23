using System.Text.Json;
using Lumen.Core;
using Serilog;

namespace Lumen.Platform;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger _log;
    private readonly Lock _lock = new();

    public JsonSettingsStore(string dataDirectory, ILogger log)
    {
        _path = Path.Combine(dataDirectory, "settings.json");
        _log = log.ForContext<JsonSettingsStore>();
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Settings, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Could not save settings");
            }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A corrupt settings file must not stop the app from starting; start fresh and keep a copy.
            _log.Error(ex, "Settings file is unreadable; starting with defaults");
            try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch (IOException) { }
        }

        var settings = new AppSettings();
        Settings = settings;
        Save();
        return settings;
    }
}
