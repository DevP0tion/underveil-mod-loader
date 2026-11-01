using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;

namespace UnderveilModLoader
{
    public interface IMod
    {
        void OnLoad(ModContext context);
    }

    public sealed class ModContext
    {
        public ModEntry Info { get; internal set; }
        public ManualLogSource Logger { get; internal set; }
        public Harmony Harmony { get; internal set; }
        public string Directory => Info.Directory;
    }

    public enum ModStatus
    {
        Pending,
        Loaded,
        Disabled,
        Failed
    }

    public sealed class ModEntry
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("version")] public string Version;
        [JsonProperty("author")] public string Author;
        [JsonProperty("description")] public string Description;
        [JsonProperty("dll")] public string Dll;
        [JsonProperty("dependencies")] public List<string> Dependencies = new List<string>();

        [JsonIgnore] public string Directory;
        [JsonIgnore] public ModStatus Status;
        [JsonIgnore] public string Error;

        internal bool CanToggle;
    }

    internal sealed class ModSettings
    {
        [JsonProperty("disabled")] public List<string> Disabled = new List<string>();
    }

    internal static class ModLoader
    {
        internal static readonly List<ModEntry> Entries = new List<ModEntry>();
        internal static string SaveError { get; private set; }

        private static ManualLogSource log;
        private static string settingsPath;
        private static ModSettings settings = new ModSettings();
        private static readonly HashSet<string> visiting = new HashSet<string>();

        internal static bool IsEnabled(ModEntry entry) => !settings.Disabled.Contains(entry.Id);

        // Mods cannot be unloaded, so a change is only saved here and applies on the next start.
        internal static void SetEnabled(ModEntry entry, bool enabled)
        {
            ModSettings changed = new ModSettings { Disabled = settings.Disabled.Where(id => id != entry.Id).ToList() };
            if (!enabled)
            {
                changed.Disabled.Add(entry.Id);
            }
            try
            {
                File.WriteAllText(settingsPath, JsonConvert.SerializeObject(changed, Formatting.Indented));
                settings = changed;
                SaveError = null;
                log.LogInfo($"[{entry.Id}] {(enabled ? "Enabled" : "Disabled")} from next start");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SaveError = $"Cannot save settings.json: {e.Message}";
                log.LogError(SaveError);
            }
        }

        internal static void LoadAll(ManualLogSource logger)
        {
            log = logger;
            string modsDir = Path.Combine(Paths.GameRootPath, "Mods");
            Directory.CreateDirectory(modsDir);

            // Mods loaded through LoadFrom cannot otherwise see this assembly, which BepInEx loads by path.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLoaderAssembly;

            foreach (string dir in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                Entries.Add(ReadEntry(dir));
            }
            MarkDuplicates();
            Entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            settingsPath = Path.Combine(modsDir, "settings.json");
            if (!ApplySettings())
            {
                return;
            }
            foreach (ModEntry entry in Entries)
            {
                Load(entry);
            }
            log.LogInfo($"Mods: {Entries.Count(e => e.Status == ModStatus.Loaded)} loaded, " +
                $"{Entries.Count(e => e.Status == ModStatus.Disabled)} disabled, " +
                $"{Entries.Count(e => e.Status == ModStatus.Failed)} failed");
        }

        private static Assembly ResolveLoaderAssembly(object sender, ResolveEventArgs args)
        {
            Assembly self = typeof(ModLoader).Assembly;
            return new AssemblyName(args.Name).Name == self.GetName().Name ? self : null;
        }

        private static ModEntry ReadEntry(string dir)
        {
            string folder = Path.GetFileName(dir);
            string manifestPath = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifestPath))
            {
                return Invalid(dir, folder, "mod.json not found");
            }

            ModEntry entry;
            try
            {
                entry = JsonConvert.DeserializeObject<ModEntry>(File.ReadAllText(manifestPath));
            }
            catch (Exception e) when (e is JsonException || e is IOException)
            {
                return Invalid(dir, folder, $"Cannot read mod.json: {e.Message}");
            }
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Dll))
            {
                return Invalid(dir, folder, "mod.json must have id and dll");
            }

            entry.Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name;
            entry.Dependencies = entry.Dependencies ?? new List<string>();
            entry.Directory = dir;
            return entry;
        }

        private static ModEntry Invalid(string dir, string folder, string error)
        {
            ModEntry entry = new ModEntry { Id = folder, Name = folder, Directory = dir };
            Fail(entry, error);
            return entry;
        }

        private static void MarkDuplicates()
        {
            foreach (IGrouping<string, ModEntry> group in Entries.Where(e => e.Status != ModStatus.Failed).GroupBy(e => e.Id))
            {
                foreach (ModEntry duplicate in group.Skip(1))
                {
                    Fail(duplicate, $"Duplicate id, already used by {Path.GetFileName(group.First().Directory)}");
                }
            }
        }

        // A broken list must not silently load mods the player turned off, so nothing loads until it is fixed.
        private static bool ApplySettings()
        {
            if (File.Exists(settingsPath))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(settingsPath)) ?? settings;
                }
                catch (Exception e) when (e is JsonException || e is IOException)
                {
                    foreach (ModEntry entry in Entries.Where(e2 => e2.Status != ModStatus.Failed))
                    {
                        Fail(entry, $"Cannot read settings.json: {e.Message}");
                    }
                    return false;
                }
            }
            // Reached only with a readable list, so saving a toggle cannot overwrite a broken file the player may want to fix.
            foreach (ModEntry entry in Entries.Where(e => e.Status != ModStatus.Failed))
            {
                entry.CanToggle = true;
                if (!IsEnabled(entry))
                {
                    entry.Status = ModStatus.Disabled;
                    log.LogInfo($"[{entry.Id}] Disabled");
                }
            }
            return true;
        }

        private static void Load(ModEntry entry)
        {
            if (entry.Status != ModStatus.Pending)
            {
                return;
            }

            visiting.Add(entry.Id);
            foreach (string dependencyId in entry.Dependencies)
            {
                ModEntry dependency = Entries.FirstOrDefault(e => e.Id == dependencyId && e.Status != ModStatus.Failed);
                if (dependency == null)
                {
                    Fail(entry, $"Missing dependency: {dependencyId}");
                }
                else if (visiting.Contains(dependencyId))
                {
                    Fail(entry, $"Circular dependency: {dependencyId}");
                }
                else
                {
                    Load(dependency);
                    if (dependency.Status != ModStatus.Loaded)
                    {
                        Fail(entry, $"Dependency not loaded: {dependencyId}");
                    }
                }
                if (entry.Status == ModStatus.Failed)
                {
                    visiting.Remove(entry.Id);
                    return;
                }
            }
            visiting.Remove(entry.Id);
            Initialize(entry);
        }

        private static void Initialize(ModEntry entry)
        {
            string dllPath = Path.Combine(entry.Directory, entry.Dll);
            if (!File.Exists(dllPath))
            {
                Fail(entry, $"DLL not found: {entry.Dll}");
                return;
            }

            try
            {
                ModText.Load(entry);
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                Fail(entry, $"Cannot read language files: {e.Message}");
                return;
            }

            Harmony harmony = new Harmony($"underveil.mod.{entry.Id}");
            // Mods are third-party code, so any exception they raise stays contained to that mod.
            try
            {
                Type[] modTypes = Assembly.LoadFrom(dllPath).GetTypes()
                    .Where(t => typeof(IMod).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract).ToArray();
                if (modTypes.Length != 1)
                {
                    ModText.Unload(entry);
                    Fail(entry, $"Expected one IMod class, found {modTypes.Length}");
                    return;
                }
                IMod mod = (IMod)Activator.CreateInstance(modTypes[0]);
                mod.OnLoad(new ModContext { Info = entry, Logger = Logger.CreateLogSource(entry.Name), Harmony = harmony });
                entry.Status = ModStatus.Loaded;
                log.LogInfo($"[{entry.Id}] Loaded {entry.Version}");
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                ModText.Unload(entry);
                Fail(entry, $"{e.GetType().Name}: {e.Message}");
            }
        }

        private static void Fail(ModEntry entry, string error)
        {
            entry.Status = ModStatus.Failed;
            entry.Error = error;
            log.LogError($"[{entry.Id}] {error}");
        }
    }
}
