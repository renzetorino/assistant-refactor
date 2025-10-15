using System.Text;

namespace dataAccess.Api.Services
{
    public sealed class ReportSpec
    {
        public string Phase1System { get; init; } = "";
        public string Phase2System { get; init; } = "";
    }

    public static class ReportSpecLoader
    {
        // Robust loader: tries strict YAML first; if that fails, falls back to plain-text scan.
        public static async Task<ReportSpec> LoadAsync(string name, CancellationToken ct = default)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts", name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Prompt file not found: {path}");

            var raw = await File.ReadAllTextAsync(path, ct);

            // Normalize line endings + strip BOM if present
            raw = StripBom(raw).Replace("\r\n", "\n");

            // --- 1) Try strict YAML (if YamlDotNet is present) ---
            try
            {
                // Use local, minimal parse to avoid hard-typing entire schema
                using var reader = new StringReader(raw);
                var yaml = new YamlDotNet.RepresentationModel.YamlStream();
                yaml.Load(reader);

                var root = (YamlDotNet.RepresentationModel.YamlMappingNode)yaml.Documents[0].RootNode;

                static string ReadSystem(YamlDotNet.RepresentationModel.YamlMappingNode map)
                {
                    if (map.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode("system"), out var sysNode))
                        return (sysNode as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value ?? "";
                    return "";
                }

                string p1 = "", p2 = "";
                if (root.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode("phase1"), out var n1)
                    && n1 is YamlDotNet.RepresentationModel.YamlMappingNode m1)
                    p1 = ReadSystem(m1);

                if (root.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode("phase2"), out var n2)
                    && n2 is YamlDotNet.RepresentationModel.YamlMappingNode m2)
                    p2 = ReadSystem(m2);

                if (!string.IsNullOrWhiteSpace(p1) || !string.IsNullOrWhiteSpace(p2))
                    return new ReportSpec { Phase1System = p1, Phase2System = p2 };
            }
            catch
            {
                // swallow and try fallback
            }

            // --- 2) Fallback: plain-text scan (handles BOM, “---”, comments, etc.) ---
            var (phase1, phase2) = ExtractSystemsByText(raw);
            if (string.IsNullOrWhiteSpace(phase1) && string.IsNullOrWhiteSpace(phase2))
                throw new InvalidDataException("Could not find phase1.system or phase2.system blocks in YAML.");

            return new ReportSpec { Phase1System = phase1, Phase2System = phase2 };
        }

        private static string StripBom(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // U+FEFF
            return s.Length > 0 && s[0] == '\uFEFF' ? s.Substring(1) : s;
        }

        private static (string phase1, string phase2) ExtractSystemsByText(string text)
        {
            // Very tolerant indentation/state machine scanner
            // Finds:
            // phase1:
            //   system: |
            //     <indented block...>
            // phase2:
            //   system: |
            //     <indented block...>

            var lines = text.Split('\n');
            string? currentPhase = null;
            bool inSystemHeader = false;
            int? blockIndent = null;
            var sbP1 = new StringBuilder();
            var sbP2 = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i];
                var line = rawLine.TrimEnd('\r'); // already \n-split
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;

                // Phase headers
                if (trimmed.StartsWith("phase1:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPhase = "phase1"; inSystemHeader = false; blockIndent = null;
                    continue;
                }
                if (trimmed.StartsWith("phase2:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPhase = "phase2"; inSystemHeader = false; blockIndent = null;
                    continue;
                }

                // system: header (accept "system:" or "system: |")
                if (currentPhase != null &&
                    (trimmed.Equals("system:", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("system: |", StringComparison.OrdinalIgnoreCase)))
                {
                    inSystemHeader = true;
                    blockIndent = null; // we’ll set it on the first content line
                    continue;
                }

                // Collect block if we’re inside system
                if (currentPhase != null && inSystemHeader)
                {
                    // first non-empty line defines block indent
                    if (blockIndent == null)
                    {
                        if (trimmed.Length == 0) { continue; } // skip blank lines right after header
                        blockIndent = indent;
                    }

                    // If indentation is less than the block indent, the block ended
                    if (indent < blockIndent.Value)
                    {
                        inSystemHeader = false; blockIndent = null;
                        // this line might belong to something else; continue scanning
                        // but don’t consume this line; just proceed.
                        // (we’ll treat it as a header or ignore next loop)
                        continue;
                    }

                    // Append de-indented content
                    var content = line.Length >= blockIndent.Value
                        ? line.Substring(blockIndent.Value)
                        : trimmed;

                    if (currentPhase == "phase1") sbP1.AppendLine(content);
                    else if (currentPhase == "phase2") sbP2.AppendLine(content);

                    continue;
                }
            }

            return (sbP1.ToString().Trim(), sbP2.ToString().Trim());
        }
    }
}
