using System.IO;
using Newtonsoft.Json;

namespace RPChronicles.Data;

/// <summary>
/// Which plan writes summaries. Saved on this computer, next to the notebooks.
/// </summary>
internal sealed class SummarySettings
{
    public string Provider { get; set; } = "chatgpt";

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(this.FilePath))
            return;

        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        var temp = this.FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, this.FilePath, overwrite: true);
    }

    public static SummarySettings Load(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        var path = Path.Combine(configDirectory, "summary-settings.json");
        SummarySettings settings;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonConvert.DeserializeObject<SummarySettings>(File.ReadAllText(path)) ?? new SummarySettings();
            }
            catch
            {
                settings = new SummarySettings();
            }
        }
        else
        {
            settings = new SummarySettings();
        }

        if (settings.Provider is not ("chatgpt" or "grok"))
            settings.Provider = "chatgpt";

        settings.FilePath = path;
        return settings;
    }
}
