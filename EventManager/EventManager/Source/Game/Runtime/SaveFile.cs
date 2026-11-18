using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game
{
    /// <summary>
    /// The on-disk save format (Task 30 Part A). One save is one pretty-printed JSON file under
    /// <c>&lt;ProjectFolder&gt;/Saves/</c>, named after its slot. The format is deliberately plain:
    /// a marker string, a schema version and then the sections later parts fill in
    /// (scene, globals, per-actor sheets, spawned records and the RNG seed).
    ///
    /// The words are the contract, not this layout. A file whose <c>format</c> marker or
    /// <c>version</c> is missing is treated as damaged, a newer schema is refused without
    /// touching the running game, and an older one loads with a warning so a future migration
    /// has a place to live.
    /// </summary>
    public static class SaveFile
    {
        /// <summary>What every save says it is, so an unrelated JSON file is never half-loaded.</summary>
        public const string FormatName = "event-manager-save";

        /// <summary>The only schema this build writes. Bump only when the layout really changes.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The slot used by <c>save game</c> and when a blank slot name is given.</summary>
        public const string DefaultSlot = "default";

        /// <summary>The folder under the project root, not under Content (saves are not assets).</summary>
        public const string FolderName = "Saves";

        /// <summary>The extension every slot file gets.</summary>
        public const string Extension = ".json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>The save folder for a project: <c>&lt;projectFolder&gt;/Saves</c>.</summary>
        public static string SaveFolder(string projectFolder)
        {
            if (string.IsNullOrWhiteSpace(projectFolder))
                throw new ArgumentException("A project folder is needed to find the save folder.", nameof(projectFolder));
            return Path.Combine(projectFolder, FolderName);
        }

        /// <summary>
        /// The file one slot writes to. A blank slot name means <see cref="DefaultSlot"/>; every
        /// character that could leave the folder (separators, dots, colons, wildcards) is
        /// replaced by <c>_</c>, so <c>"../slot1"</c> still lands inside <c>Saves/</c>.
        /// </summary>
        public static string PathFor(string projectFolder, string slot)
        {
            return Path.Combine(SaveFolder(projectFolder), FileNameFor(slot));
        }

        /// <summary>The file name for a slot, with the extension and safe characters only.</summary>
        public static string FileNameFor(string slot)
        {
            return NormalizeSlot(slot) + Extension;
        }

        /// <summary>
        /// The slot name as it appears in a file name: trimmed, with only letters, digits,
        /// spaces, <c>-</c> and <c>_</c> kept. Anything else becomes <c>_</c>; an empty result
        /// falls back to <see cref="DefaultSlot"/>.
        /// </summary>
        public static string NormalizeSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return DefaultSlot;
            string trimmed = slot.Trim();
            var safe = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ')
                    safe.Append(c);
                else
                    safe.Append('_');
            }
            string name = safe.ToString().Trim();
            return name.Length == 0 ? DefaultSlot : name;
        }

        /// <summary>True when that slot has a file on disk.</summary>
        public static bool Exists(string projectFolder, string slot)
        {
            return File.Exists(PathFor(projectFolder, slot));
        }

        /// <summary>
        /// Removes a slot. Returns false when there was no file, so <c>delete save</c> can tell
        /// "already gone" from "deleted".
        /// </summary>
        public static bool Delete(string projectFolder, string slot)
        {
            string path = PathFor(projectFolder, slot);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// Serializes a save, stamping the format marker and schema version so a caller cannot
        /// accidentally write a file this build will not read back.
        /// </summary>
        public static string Serialize(SaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.Format = FormatName;
            data.Version = SchemaVersion;
            return JsonSerializer.Serialize(data, JsonOptions);
        }

        /// <summary>
        /// Writes a slot. The slot name and save time are stamped here; the write goes through a
        /// temporary file and is replaced in one move, so a crash mid-save cannot leave a
        /// half-written slot that later reads as corrupt.
        /// </summary>
        public static void Write(string projectFolder, string slot, SaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.Slot = NormalizeSlot(slot);
            data.SavedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            WritePath(PathFor(projectFolder, slot), data);
        }

        /// <summary>Writes a save to an explicit path (tests and tools use this form).</summary>
        public static void WritePath(string path, SaveData data)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A save path is needed.", nameof(path));
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            string json = Serialize(data);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }

        /// <summary>Reads a slot. The caller decides what to do with the status and message.</summary>
        public static SaveFileResult TryRead(string projectFolder, string slot)
        {
            return TryReadPath(PathFor(projectFolder, slot));
        }

        /// <summary>Reads an explicit path, returning a friendly status instead of throwing.</summary>
        public static SaveFileResult TryReadPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new SaveFileResult
                {
                    Status = SaveFileStatus.Missing,
                    Path = path ?? "",
                    Message = string.IsNullOrWhiteSpace(path)
                        ? "There is no save file path to read."
                        : $"There is no save file at \"{path}\".",
                };

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception error)
            {
                return Corrupt(path, $"I couldn't open the save file \"{path}\" ({error.Message}). The current game was left alone.");
            }

            return Parse(path, json);
        }

        private static SaveFileResult Parse(string path, string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, DocumentOptions);
            }
            catch (JsonException)
            {
                return Corrupt(path, $"The save file \"{FileNameOf(path)}\" is damaged — it isn't valid save JSON. The current game was left alone.");
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return Corrupt(path, $"The save file \"{FileNameOf(path)}\" isn't an Event Manager save. The current game was left alone.");

                if (!TryGetProperty(root, "format", out JsonElement formatElement) ||
                    formatElement.ValueKind != JsonValueKind.String ||
                    !string.Equals(formatElement.GetString(), FormatName, StringComparison.Ordinal))
                {
                    return Corrupt(path, $"The save file \"{FileNameOf(path)}\" isn't an Event Manager save (the format marker is missing). The current game was left alone.");
                }

                if (!TryGetProperty(root, "version", out JsonElement versionElement) ||
                    !versionElement.TryGetInt32(out int version))
                {
                    return Corrupt(path, $"The save file \"{FileNameOf(path)}\" has no schema version, so this build can't tell how to read it. The current game was left alone.");
                }

                if (version > SchemaVersion)
                {
                    return new SaveFileResult
                    {
                        Status = SaveFileStatus.NewerVersion,
                        Path = path,
                        Message = $"The save file \"{FileNameOf(path)}\" was written by a newer version of Event Manager (schema {version}; this build reads up to {SchemaVersion}). It was not loaded, so the current game keeps running.",
                    };
                }

                SaveData data;
                try
                {
                    data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
                }
                catch (JsonException)
                {
                    return Corrupt(path, $"The save file \"{FileNameOf(path)}\" is damaged — its contents don't match the save format. The current game was left alone.");
                }

                if (data == null)
                    return Corrupt(path, $"The save file \"{FileNameOf(path)}\" is empty. The current game was left alone.");

                data.EnsureSections();

                if (version < SchemaVersion)
                {
                    return new SaveFileResult
                    {
                        Status = SaveFileStatus.OlderVersion,
                        Data = data,
                        Path = path,
                        Message = $"The save file \"{FileNameOf(path)}\" uses schema {version}, which is older than this build's {SchemaVersion}. Loading what it has; a future migration can fill in the rest.",
                    };
                }

                return new SaveFileResult
                {
                    Status = SaveFileStatus.Ok,
                    Data = data,
                    Path = path,
                };
            }
        }

        private static SaveFileResult Corrupt(string path, string message)
        {
            return new SaveFileResult
            {
                Status = SaveFileStatus.Corrupt,
                Path = path,
                Message = message,
            };
        }

        private static string FileNameOf(string path)
        {
            try
            {
                return Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return path;
            }
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value))
                return true;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }

    public enum SaveFileStatus
    {
        /// <summary>The file parsed and its schema version is this build's.</summary>
        Ok,
        /// <summary>No file at that slot. Not an error.</summary>
        Missing,
        /// <summary>The file is not valid save JSON, is not an Event Manager save, or has no version.</summary>
        Corrupt,
        /// <summary>The file is from a newer schema; it was not loaded.</summary>
        NewerVersion,
        /// <summary>The file is from an older schema; it was loaded with a warning.</summary>
        OlderVersion,
    }

    /// <summary>One read attempt: the status, the data when it could be read, and a friendly message.</summary>
    public class SaveFileResult
    {
        public SaveFileStatus Status;
        public SaveData Data;
        public string Path = "";
        public string Message = "";

        /// <summary>True when <see cref="Data"/> is usable (an older schema still loads).</summary>
        public bool Loaded => Status == SaveFileStatus.Ok || Status == SaveFileStatus.OlderVersion;

        /// <summary>True when the save loaded but the caller should warn about it.</summary>
        public bool HasWarning => Status == SaveFileStatus.OlderVersion;
    }

    /// <summary>
    /// The save file's root. The sections are the save's whole vocabulary: the scene it came
    /// from, every global, one entry per sheet/actor, records for spawned prefabs and clones,
    /// and the RNG seed the run continues from. Parts B–D of Task 30 fill and restore them;
    /// Part A only fixes their shape and the file around them.
    /// </summary>
    public class SaveData
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = "";

        /// <summary>Null until <see cref="SaveFile.Serialize"/> stamps it, so a reader can tell
        /// "no version at all" from "version 0".</summary>
        [JsonPropertyName("version")]
        public int? Version { get; set; }

        [JsonPropertyName("slot")]
        public string Slot { get; set; } = "";

        [JsonPropertyName("savedAt")]
        public string SavedAt { get; set; } = "";

        [JsonPropertyName("scene")]
        public SaveScene Scene { get; set; } = new SaveScene();

        [JsonPropertyName("globals")]
        public List<SaveVariable> Globals { get; set; } = new List<SaveVariable>();

        [JsonPropertyName("actors")]
        public List<SaveActor> Actors { get; set; } = new List<SaveActor>();

        [JsonPropertyName("spawned")]
        public List<SaveSpawned> Spawned { get; set; } = new List<SaveSpawned>();

        [JsonPropertyName("rng")]
        public SaveRng Rng { get; set; } = new SaveRng();

        /// <summary>Repairs missing sections after a hand-edited or partial file, so a reader
        /// never has to null-check every list.</summary>
        public void EnsureSections()
        {
            Scene ??= new SaveScene();
            Scene.Additive ??= new List<string>();
            Globals ??= new List<SaveVariable>();
            Actors ??= new List<SaveActor>();
            Spawned ??= new List<SaveSpawned>();
            Rng ??= new SaveRng();
            foreach (SaveActor actor in Actors)
                actor?.EnsureSections();
            foreach (SaveSpawned spawned in Spawned)
                spawned?.EnsureSections();
        }
    }

    /// <summary>The level and its additive overlays (HUDs, menus) at save time.</summary>
    public class SaveScene
    {
        /// <summary>The scene/level that was loaded when the game was saved.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Scenes added on top of it, in load order.</summary>
        [JsonPropertyName("additive")]
        public List<string> Additive { get; set; } = new List<string>();

        /// <summary>The run's <c>LevelName</c> global, kept beside the scene so a save still
        /// knows its place after the scene reloads.</summary>
        [JsonPropertyName("levelName")]
        public string LevelName { get; set; } = "";

        /// <summary>The run's 1-based <c>LevelNumber</c>; 0 when it wasn't in the level order.</summary>
        [JsonPropertyName("levelNumber")]
        public int LevelNumber { get; set; }
    }

    /// <summary>One global or per-object variable. <see cref="Kind"/> mirrors
    /// <see cref="ValueKind"/> so a number stays a number and a text stays text.</summary>
    public class SaveVariable
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary><c>number</c>, <c>text</c>, <c>boolean</c>, or <c>none</c> (never set).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "number";

        [JsonPropertyName("number")]
        public double Number { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("boolean")]
        public bool Boolean { get; set; }

        public static SaveVariable FromBox(string name, ValueBox value)
        {
            var saved = new SaveVariable { Name = name ?? "" };
            if (value == null)
            {
                saved.Kind = "none";
                return saved;
            }
            switch (value.Kind)
            {
                case ValueKind.Number:
                    saved.Kind = "number";
                    saved.Number = value.Number;
                    break;
                case ValueKind.Text:
                    saved.Kind = "text";
                    saved.Text = value.Text ?? "";
                    break;
                case ValueKind.Boolean:
                    saved.Kind = "boolean";
                    saved.Boolean = value.Boolean;
                    break;
                default:
                    saved.Kind = "none";
                    break;
            }
            return saved;
        }

        public ValueBox ToBox()
        {
            switch ((Kind ?? "").Trim().ToLowerInvariant())
            {
                case "number":
                    return ValueBox.OfNumber(Number);
                case "text":
                    return ValueBox.OfText(Text ?? "");
                case "boolean":
                    return ValueBox.OfBoolean(Boolean);
                default:
                    return new ValueBox();
            }
        }

        public void EnsureSections()
        {
            Text ??= "";
            Kind ??= "none";
        }
    }

    /// <summary>One sheet/actor in the save: which script drove it, where it stood, what it
    /// carried and which state it was in.</summary>
    public class SaveActor
    {
        /// <summary>The <c>.events</c> source name, so a scene reload can rebuild the sheet.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        /// <summary>The actor's real name (the runtime's identity, never <c>ToString()</c>).</summary>
        [JsonPropertyName("actor")]
        public string Actor { get; set; } = "";

        /// <summary>Local player 1 or 2, or 0 for a non-player sheet.</summary>
        [JsonPropertyName("player")]
        public int Player { get; set; }

        /// <summary>The sheet's opt-in physics movement flag.</summary>
        [JsonPropertyName("physics")]
        public bool Physics { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        [JsonPropertyName("transform")]
        public SaveTransform Transform { get; set; } = new SaveTransform();

        /// <summary>Every per-object variable, including health, quest flags and counters.</summary>
        [JsonPropertyName("variables")]
        public List<SaveVariable> Variables { get; set; } = new List<SaveVariable>();

        [JsonPropertyName("bag")]
        public List<SaveBagEntry> Bag { get; set; } = new List<SaveBagEntry>();

        /// <summary>The item names currently equipped, in no particular order.</summary>
        [JsonPropertyName("equipped")]
        public List<string> Equipped { get; set; } = new List<string>();

        /// <summary>The sheet's <c>CurrentState</c> (default <c>default</c>).</summary>
        [JsonPropertyName("state")]
        public string State { get; set; } = "default";

        /// <summary>The prefab/scene/template this actor came from (<c>my spawnedFrom</c>), or empty.</summary>
        [JsonPropertyName("spawnedFrom")]
        public string SpawnedFrom { get; set; } = "";

        public void EnsureSections()
        {
            Source ??= "";
            Actor ??= "";
            Transform ??= new SaveTransform();
            Transform.EnsureSections();
            Variables ??= new List<SaveVariable>();
            Bag ??= new List<SaveBagEntry>();
            Equipped ??= new List<string>();
            State ??= "default";
            SpawnedFrom ??= "";
            foreach (SaveVariable variable in Variables)
                variable?.EnsureSections();
            foreach (SaveBagEntry entry in Bag)
                entry?.EnsureSections();
        }
    }

    /// <summary>One item stack in a bag: the name the bag counts by and how many are held.</summary>
    public class SaveBagEntry
    {
        [JsonPropertyName("thing")]
        public string Thing { get; set; } = "";

        [JsonPropertyName("count")]
        public int Count { get; set; }

        public void EnsureSections()
        {
            Thing ??= "";
        }
    }

    /// <summary>A prefab, scene or clone that has to be recreated before its sheet state can be
    /// applied (Task 30 Part D).</summary>
    public class SaveSpawned
    {
        /// <summary><c>prefab</c>, <c>scene</c> or <c>clone</c>.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "prefab";

        /// <summary>The source asset or template name, as the spawn word wrote it.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        /// <summary>The spawned root's actor name, including the existing spawn suffix.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>The parent actor's name, or empty when it sits at the scene root.</summary>
        [JsonPropertyName("parent")]
        public string Parent { get; set; } = "";

        [JsonPropertyName("transform")]
        public SaveTransform Transform { get; set; } = new SaveTransform();

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        public void EnsureSections()
        {
            Kind ??= "prefab";
            Source ??= "";
            Name ??= "";
            Parent ??= "";
            Transform ??= new SaveTransform();
            Transform.EnsureSections();
        }
    }

    /// <summary>Where an actor stood and how it was turned, in Flax's world units.</summary>
    public class SaveTransform
    {
        [JsonPropertyName("position")]
        public SaveVector Position { get; set; } = new SaveVector();

        [JsonPropertyName("orientation")]
        public SaveQuaternion Orientation { get; set; } = new SaveQuaternion();

        [JsonPropertyName("scale")]
        public SaveVector Scale { get; set; } = SaveVector.One;

        public void EnsureSections()
        {
            Position ??= new SaveVector();
            Orientation ??= new SaveQuaternion();
            Scale ??= SaveVector.One;
        }
    }

    /// <summary>A position or scale. Doubles keep the JSON simple and the tests exact.</summary>
    public class SaveVector
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }

        public static SaveVector One => new SaveVector { X = 1, Y = 1, Z = 1 };
    }

    /// <summary>A rotation. The identity is (0, 0, 0, 1), matching Flax's Quaternion.Identity.</summary>
    public class SaveQuaternion
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }

        [JsonPropertyName("w")]
        public double W { get; set; } = 1;
    }

    /// <summary>The RNG seed to continue from (Task 30's deterministic continuation).</summary>
    public class SaveRng
    {
        [JsonPropertyName("seed")]
        public int Seed { get; set; }
    }
}
