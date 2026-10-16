using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace Landsmith
{
    internal sealed class ToolDefinition
    {
        internal ToolKind Kind;
        internal string Name;
        internal string Description;
        internal string Template;
        internal string IconFile;
        internal GameObject Prefab;
        internal Piece Piece;
        // The stone requirement whose amount follows the planned stroke; null for free tools.
        internal Piece.Requirement Stone;
    }

    // Adds the Landsmith tools to the Hoe as cloned vanilla hoe pieces without their TerrainOp.
    // The clones are never networked: the pieces only select a tool, the edit itself is written by TerrainWriter.
    internal static class ToolRegistry
    {
        internal const string HoeItem = "Hoe";
        internal const string StoneItem = "Stone";
        private const string LevelTemplate = "mud_road_v2";
        private const string RaiseTemplate = "raise_v2";
        private const string PathTemplate = "path_v2";
        private const string PavedTemplate = "paved_road_v2";

        internal static readonly List<ToolDefinition> Definitions = new List<ToolDefinition>
        {
            Define(ToolKind.Level, "Landsmith: Level", "Flattens the brush area at the target height. Raised ground costs stone.", LevelTemplate, "level.png"),
            Define(ToolKind.Offset, "Landsmith: Raise or lower", "Moves the ground up or down by the offset and keeps its shape. Raised ground costs stone.", RaiseTemplate, "offset.png"),
            Define(ToolKind.Smooth, "Landsmith: Smooth", "Softens bumps and steps inside the brush. Ground added overall costs stone.", LevelTemplate, "smooth.png"),
            Define(ToolKind.Slope, "Landsmith: Slope", "Shapes a straight incline at the chosen angle. Strokes continue the same plane. Raised ground costs stone.", RaiseTemplate, "slope.png"),
            Define(ToolKind.Path, "Landsmith: Path", "Paints a dirt path without changing the height.", PathTemplate, "path.png"),
            Define(ToolKind.Paved, "Landsmith: Paved road", "Paints paving without changing the height. Costs stone per area.", PavedTemplate, "paved.png"),
            Define(ToolKind.Reset, "Landsmith: Reset", "Restores the original height and ground cover inside the brush. Refilled ground costs stone.", LevelTemplate, "reset.png"),
        };

        private static readonly Dictionary<Piece, ToolDefinition> byPiece = new Dictionary<Piece, ToolDefinition>();
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        private static GameObject container;
        private static PieceTable builtFor;
        private static ManualLogSource log;

        internal static bool Ready => builtFor && byPiece.Count > 0;

        internal static void Initialize(ManualLogSource logger) => log = logger;

        private static ToolDefinition Define(ToolKind kind, string name, string description, string template, string icon) =>
            new ToolDefinition { Kind = kind, Name = name, Description = description, Template = template, IconFile = icon };

        internal static bool TryGet(Piece piece, out ToolDefinition definition)
        {
            definition = null;
            return piece && byPiece.TryGetValue(piece, out definition);
        }

        internal static bool Contains(Piece piece) => piece && byPiece.ContainsKey(piece);

        // Called after every ObjectDB initialization. Idempotent: the prefabs are built once per Hoe piece table.
        internal static void Register(ObjectDB db)
        {
            if (Application.isBatchMode || !db) return;
            // The main menu first creates an empty ObjectDB and fills it through CopyOtherDB right after.
            if (db.m_items == null || db.m_items.Count == 0) return;
            GameObject hoe = db.GetItemPrefab(HoeItem);
            ItemDrop drop = hoe ? hoe.GetComponent<ItemDrop>() : null;
            PieceTable table = drop ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (!table)
            {
                log?.LogWarning($"The {HoeItem} piece table was not found; Landsmith tools are unavailable.");
                return;
            }
            if (builtFor != table || !PrefabsAlive()) Build(table, db);
            foreach (ToolDefinition definition in Definitions)
                if (definition.Prefab && !table.m_pieces.Contains(definition.Prefab))
                    table.m_pieces.Add(definition.Prefab);
        }

        internal static void Unregister()
        {
            RemoveFromTable();
            byPiece.Clear();
            builtFor = null;
            if (container) UnityEngine.Object.Destroy(container);
            container = null;
            foreach (Sprite sprite in icons.Values)
            {
                if (!sprite) continue;
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            icons.Clear();
        }

        private static void RemoveFromTable()
        {
            if (!builtFor) return;
            foreach (ToolDefinition definition in Definitions)
                if (definition.Prefab) builtFor.m_pieces.Remove(definition.Prefab);
            // Entries whose clone was already destroyed compare equal to null.
            builtFor.m_pieces.RemoveAll(piece => !piece);
        }

        private static bool PrefabsAlive()
        {
            if (byPiece.Count == 0) return false;
            foreach (ToolDefinition definition in byPiece.Values)
                if (!definition.Prefab || !definition.Piece) return false;
            return true;
        }

        private static void Build(PieceTable table, ObjectDB db)
        {
            RemoveFromTable();
            byPiece.Clear();
            if (container) UnityEngine.Object.Destroy(container);
            // Children of an inactive object never run Awake, so the cloned TerrainOp cannot fire.
            container = new GameObject("Landsmith_Prefabs");
            container.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(container);

            var templates = new Dictionary<string, GameObject>();
            foreach (GameObject piece in table.m_pieces)
                if (piece && !templates.ContainsKey(piece.name)) templates[piece.name] = piece;
            ItemDrop stone = FindStone(db, templates);
            if (!stone) log?.LogWarning($"Item {StoneItem} not found; Landsmith tools will not cost stone.");

            int built = 0;
            foreach (ToolDefinition definition in Definitions)
            {
                definition.Prefab = null;
                definition.Piece = null;
                definition.Stone = null;
                if (!templates.TryGetValue(definition.Template, out GameObject template))
                {
                    if (!templates.TryGetValue(LevelTemplate, out template))
                    {
                        log?.LogWarning($"Hoe piece {definition.Template} not found; {definition.Name} is unavailable.");
                        continue;
                    }
                    log?.LogWarning($"Hoe piece {definition.Template} not found; {definition.Name} uses {LevelTemplate} instead.");
                }
                GameObject clone = UnityEngine.Object.Instantiate(template, container.transform, false);
                clone.name = "Landsmith_" + definition.Kind;
                TerrainOp op = clone.GetComponent<TerrainOp>();
                if (op) UnityEngine.Object.DestroyImmediate(op);
                Piece piece = clone.GetComponent<Piece>();
                if (!piece)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    continue;
                }
                piece.m_name = definition.Name;
                piece.m_description = definition.Description;
                Sprite icon = LoadIcon(definition.IconFile);
                if (icon) piece.m_icon = icon;
                piece.m_canRotate = false;
                piece.m_allowAltGroundPlacement = false;
                piece.m_randomInitBuildRotation = false;
                bool charges = Tools.ChargesForRaising(definition.Kind) || Tools.ChargesForPaving(definition.Kind);
                if (charges && stone)
                {
                    definition.Stone = new Piece.Requirement { m_resItem = stone, m_amount = 0, m_amountPerLevel = 0, m_recover = false };
                    piece.m_resources = new[] { definition.Stone };
                }
                else
                {
                    piece.m_resources = Array.Empty<Piece.Requirement>();
                }
                definition.Prefab = clone;
                definition.Piece = piece;
                byPiece[piece] = definition;
                built++;
            }
            builtFor = table;
            log?.LogInfo($"Added {built} Landsmith tools to the {HoeItem}.");
        }

        private static ItemDrop FindStone(ObjectDB db, Dictionary<string, GameObject> templates)
        {
            GameObject prefab = db.GetItemPrefab(StoneItem);
            ItemDrop stone = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (stone) return stone;
            // Fallback: the vanilla Raise ground requirement is Stone.
            if (templates.TryGetValue(RaiseTemplate, out GameObject raise) && raise.GetComponent<Piece>() is Piece piece && piece)
                foreach (Piece.Requirement requirement in piece.m_resources)
                    if (requirement != null && requirement.m_resItem) return requirement.m_resItem;
            return null;
        }

        private static Sprite LoadIcon(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (icons.TryGetValue(file, out Sprite cached) && cached) return cached;
            string resource = typeof(ToolRegistry).Namespace + ".assets.icons." + file;
            using (Stream stream = typeof(ToolRegistry).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    log?.LogWarning($"Icon {resource} is missing.");
                    return null;
                }
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                byte[] rgba;
                int width, height;
                try
                {
                    rgba = PngDecoder.DecodeRgba(bytes, out width, out height);
                }
                catch (InvalidDataException error)
                {
                    log?.LogWarning($"Icon {resource} could not be read: {error.Message}");
                    return null;
                }
                // Unity textures start with the bottom row.
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = (height - 1 - y) * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int i = row + x * 4;
                        pixels[y * width + x] = new Color32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                    }
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Landsmith_" + file, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = texture.name;
                icons[file] = sprite;
                return sprite;
            }
        }

        internal static void ResetStone()
        {
            foreach (ToolDefinition definition in Definitions)
                if (definition.Stone != null) definition.Stone.m_amount = 0;
        }

        // The stone amount vanilla checks and consumes for the next click of this tool.
        internal static void SetStone(ToolDefinition definition, int amount)
        {
            if (definition?.Stone != null) definition.Stone.m_amount = Math.Max(0, amount);
        }
    }
}
