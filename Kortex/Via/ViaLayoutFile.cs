using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Kortex.Via
{
    /// <summary>
    /// Обмен раскладкой в формате файлов VIA: слои перечислены кодами QMK по именам,
    /// поэтому файл читается и самой VIA, и Kortex.
    /// </summary>
    public static class ViaLayoutFile
    {
        public sealed class Layout
        {
            public string Name { get; set; }
            public long VendorProductId { get; set; }
            public List<List<string>> Layers { get; set; } = new List<List<string>>();
        }

        public static void Save(string path, string name, ushort vendorId, ushort productId, ushort[][,] layers,
                                int rows, int cols)
        {
            var text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"name\": ").Append(JsonSerializer.Serialize(name)).Append(",\n");
            text.Append("  \"vendorProductId\": ").Append(((long)vendorId << 16) | productId).Append(",\n");
            text.Append("  \"layers\": [\n");

            for (int layer = 0; layer < layers.Length; layer++)
            {
                text.Append("    [");
                bool first = true;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (!first) text.Append(", ");
                        first = false;
                        text.Append(JsonSerializer.Serialize(QmkKeycodes.Name(layers[layer][r, c])));
                    }
                text.Append(layer == layers.Length - 1 ? "]\n" : "],\n");
            }

            text.Append("  ]\n}\n");
            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
        }

        /// <summary>Читает файл VIA и возвращает слои в виде кодов.</summary>
        public static ushort[][,] Load(string path, int rows, int cols, out int layerCount)
        {
            layerCount = 0;
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement layersNode;
                if (!document.RootElement.TryGetProperty("layers", out layersNode) ||
                    layersNode.ValueKind != JsonValueKind.Array)
                    return null;

                var layers = new List<ushort[,]>();
                foreach (var layerNode in layersNode.EnumerateArray())
                {
                    var map = new ushort[rows, cols];
                    int index = 0;
                    foreach (var cell in layerNode.EnumerateArray())
                    {
                        if (index >= rows * cols) break;
                        map[index / cols, index % cols] = QmkKeycodes.Parse(cell.GetString());
                        index++;
                    }
                    layers.Add(map);
                }

                layerCount = layers.Count;
                return layers.ToArray();
            }
        }
    }
}
