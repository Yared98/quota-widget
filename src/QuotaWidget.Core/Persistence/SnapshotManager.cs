using System.IO;
using System.Text.Json;
using QuotaWidget.Model;

namespace QuotaWidget.Persistence;

public class SnapshotManager
{
    private readonly string _filePath;

    public SnapshotManager(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaWidget",
            "snapshot.json");
    }

    public Dictionary<string, QuotaSnapshot> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<QuotaSnapshot>>(json);
                if (list != null)
                {
                    return list.ToDictionary(a => a.ProviderId, a => a, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Ignore load errors, return empty
        }

        return new Dictionary<string, QuotaSnapshot>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IEnumerable<QuotaSnapshot> snapshots)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(snapshots.ToList(), options);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Do not crash if save fails
        }
    }
}
