using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace ETS2LA.Settings
{
    public class SettingsHandler : IDisposable
    {
        public readonly string BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETS2LA");
        readonly JsonSerializerOptions JsonOption = new JsonSerializerOptions {
            WriteIndented = true,
            IncludeFields = true
        };

        // This will disable listener callbacks during save operations.
        // Otherwise saving a file would trigger the listener callback to read it
        // at the same time -> file access conflicts.
        readonly List<string> SavingInProgress = new();

        readonly Dictionary<string, List<ListenerEntry>> Listeners = new(StringComparer.OrdinalIgnoreCase);
        class ListenerEntry
        {
            public Type? DataType;
            public Delegate Callback;
            public ListenerEntry(Type? type, Delegate cb) { DataType = type; Callback = cb; }
        }

        readonly object Sync = new();
        readonly FileSystemWatcher Watcher;

        public SettingsHandler()
        {
            Directory.CreateDirectory(BaseDirectory);
            Watcher = new FileSystemWatcher(BaseDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            Watcher.Changed += OnFsEvent;
            Watcher.Created += OnFsEvent;
            Watcher.Renamed += OnFsRenamed;
            Watcher.Deleted += OnFsEvent;
        }

        private void VerifyJsonPath(string fileName)
        {
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Settings file name must end with .json");
        }

        public bool Save<T>(string fileName, T data)
        {
            VerifyJsonPath(fileName);
            string json = JsonSerializer.Serialize(data, JsonOption);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(BaseDirectory, fileName))!);
            string target = Path.Combine(BaseDirectory, fileName);
            string temp = target + ".tmp";

            // Write temp file, then replace the target to avoid lock issues.
            try
            {
                lock (Sync) SavingInProgress.Add(fileName);
                File.WriteAllText(temp, json, Encoding.UTF8);
                int retries = 3;
                while (retries-- > 0)
                {
                    try {
                        if (File.Exists(target)) 
                        { 
                            File.Replace(temp, target, null); 
                            break;
                        }
                        else
                        {
                            File.Move(temp, target);
                            break;
                        }
                    } catch (IOException) when (retries > 1) {
                        Thread.Sleep(50);
                    }
                }
                lock (Sync) SavingInProgress.Remove(fileName);
            } catch (Exception ex)
            {
                lock (Sync) SavingInProgress.Remove(fileName);
                Console.WriteLine($"Failed to save settings file {fileName}: {ex}");
                File.Delete(temp);
                return false;
            }

            // Manually trigger listeners since FS watcher is ignored during save.
            HandleFsChange(target, fileName);
            return true;
        }

        public T Load<T>(string fileName)
        {
            VerifyJsonPath(fileName);
            string path = Path.Combine(BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                T created = Activator.CreateInstance<T>();
                Save(fileName, created);
                return created;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            try
            {
                var data = JsonSerializer.Deserialize<T>(json, JsonOption);
                return data != null ? data : default!;
            }
            catch (JsonException)
            {
                Console.WriteLine($"Failed to deserialize settings file {fileName}, returning default instance.");
                return Activator.CreateInstance<T>();
            }
        }

        public void ApplyCloudUpdate()
        {
            string loadNextPath = Path.Combine(BaseDirectory, "cloud_update.json");
            if (!File.Exists(loadNextPath)) return;

            try
            {
                string json = File.ReadAllText(loadNextPath, Encoding.UTF8);
                // {
                //   "files": {
                //     "filename.json": {
                //       ... # contents
                //     } 
                //   }
                // }
                var jsonData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json, JsonOption);
                if (jsonData == null || !jsonData.TryGetValue("files", out var filesDict) || filesDict == null)
                {
                    Console.WriteLine($"cloud_update.json is missing 'files' section, skipping update.");
                    return;
                }

                foreach (var kvp in filesDict)
                {
                    string fileName = kvp.Key;
                    var jsonContent = JsonSerializer.Serialize(kvp.Value, JsonOption);
                    string targetPath = Path.Combine(BaseDirectory, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.WriteAllText(targetPath, jsonContent, Encoding.UTF8);
                    Console.WriteLine($"Updated settings file {fileName} from cloud_update.json");
                }
                File.Delete(loadNextPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update settings files from load_next.json: {ex}");
            }
        }

        public void RegisterListener<T>(string fileName, Action<T> callback)
        {
            VerifyJsonPath(fileName);
            lock (Sync)
            {
                if (!Listeners.TryGetValue(fileName, out var list))
                {
                    list = new List<ListenerEntry>();
                    Listeners[fileName] = list;
                }
                list.Add(new ListenerEntry(typeof(T), callback));
            }
        }

        public void UnregisterListener(string fileName, Action callback)
        {
            lock (Sync)
            {
                if (Listeners.TryGetValue(fileName, out var list))
                {
                    list.RemoveAll(e => e.DataType == null && e.Callback.Equals(callback));
                    if (list.Count == 0) Listeners.Remove(fileName);
                }
            }
        }

        public void UnregisterListener<T>(string fileName, Action<T> callback)
        {
            lock (Sync)
            {
                if (Listeners.TryGetValue(fileName, out var list))
                {
                    list.RemoveAll(e => e.DataType == typeof(T) && e.Callback.Equals(callback));
                    if (list.Count == 0) Listeners.Remove(fileName);
                }
            }
        }

        // filesystem watcher handlers (debounce by checking actual timestamp change)
        void OnFsEvent(object? sender, FileSystemEventArgs e) => HandleFsChange(e.FullPath, e.Name);
        void OnFsRenamed(object? sender, RenamedEventArgs e) => HandleFsChange(e.FullPath, e.Name);

        void HandleFsChange(string fullPath, string? name)
        {
            if (name == null) return;

            List<ListenerEntry>? entries;
            lock (Sync)
            {
                if (SavingInProgress.Contains(name)) return;
                entries = Listeners.TryGetValue(name, out var list) ? new List<ListenerEntry>(list) : null;
            }
            if (entries == null) return;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(fullPath);

                // read file like this instead of File.ReadAllText to avoid file lock issues
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                string? json = null;
                json = sr.ReadToEnd();
                foreach (var entry in entries)
                {
                    if (entry.DataType == null)
                    {
                        entry.Callback.DynamicInvoke();
                        continue;
                    }

                    try
                    {
                        var obj = JsonSerializer.Deserialize(json, entry.DataType, JsonOption);
                        entry.Callback.DynamicInvoke(obj);
                    }
                    catch
                    {
                        Console.WriteLine($"Failed to deserialize settings file {name} for listener of type {entry.DataType}, skipping callback.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing filesystem change for {name}: {ex}");
            }
        }

        public void Dispose()
        {
            Watcher?.Dispose();
        }
    }
}