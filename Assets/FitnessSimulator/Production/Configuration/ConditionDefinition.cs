using System;
using System.Globalization;

namespace FitnessSimulator.Game.Configuration
{
    [Serializable] public sealed class ConditionDefinition
    {
        public string Op;
        public string Key;
        public string Value;
        public ConditionDefinition[] Children;
    }
    public static class ConditionEvaluator
    {
        public static void Validate(ConditionDefinition condition, Func<string, bool> isAllowedKey, string path = "condition")
        { if (isAllowedKey == null) throw new ArgumentNullException(nameof(isAllowedKey)); int count = 0; ValidateNode(condition, isAllowedKey, path, 0, ref count); }
        private static void ValidateNode(ConditionDefinition node, Func<string, bool> allowed, string path, int depth, ref int count)
        {
            if (node == null || depth > 32 || ++count > 2048) throw new ContentException("conditions", path, "Missing or oversized condition tree.");
            if (node.Children == null) throw new ContentException("conditions", path + ".Children", "Explicit child array required.");
            bool group = node.Op == "All" || node.Op == "Any" || node.Op == "Not";
            bool constant = node.Op == "True" || node.Op == "False";
            bool comparison = node.Op == "Eq" || node.Op == "Ne" || node.Op == "Gt" || node.Op == "Gte" || node.Op == "Lt" || node.Op == "Lte";
            if (!group && !constant && !comparison) throw new ContentException("conditions", path + ".Op", "Unknown operator.");
            if (group || constant) {
                if (!string.IsNullOrEmpty(node.Key) || !string.IsNullOrEmpty(node.Value)) throw new ContentException("conditions", path, "Group/constant cannot carry leaf parameters.");
                if (group && (node.Children.Length == 0 || node.Op == "Not" && node.Children.Length != 1) || constant && node.Children.Length != 0)
                    throw new ContentException("conditions", path + ".Children", "Invalid child count.");
            } else {
                if (node.Children.Length != 0 || string.IsNullOrWhiteSpace(node.Key) || !allowed(node.Key) || node.Value == null)
                    throw new ContentException("conditions", path, "Leaf requires an allowed fact, explicit value and no children.");
                if (node.Op != "Eq" && node.Op != "Ne") Number(node.Value, path + ".Value");
            }
            for (int i = 0; i < node.Children.Length; i++) ValidateNode(node.Children[i], allowed, path + ".Children[" + i + "]", depth + 1, ref count);
        }
        public static bool Evaluate(ConditionDefinition condition, Func<string, string> readFact, Func<string, bool> isAllowedKey)
        {
            if (readFact == null) throw new ArgumentNullException(nameof(readFact));
            Validate(condition, isAllowedKey); return EvaluateNode(condition, readFact);
        }
        private static bool EvaluateNode(ConditionDefinition node, Func<string, string> read)
        {
            switch (node.Op) {
                case "True": return true; case "False": return false;
                case "Not": return !EvaluateNode(node.Children[0], read);
                case "All": foreach (var child in node.Children) if (!EvaluateNode(child, read)) return false; return true;
                case "Any": foreach (var child in node.Children) if (EvaluateNode(child, read)) return true; return false;
            }
            string value = read(node.Key);
            if (value == null) throw new ContentException("conditions", node.Key, "Allowed fact has no value.");
            if (node.Op == "Eq") return string.Equals(value, node.Value, StringComparison.Ordinal);
            if (node.Op == "Ne") return !string.Equals(value, node.Value, StringComparison.Ordinal);
            int compare = Number(value, node.Key).CompareTo(Number(node.Value, node.Key));
            switch (node.Op) { case "Gt": return compare > 0; case "Gte": return compare >= 0; case "Lt": return compare < 0; case "Lte": return compare <= 0; default: throw new InvalidOperationException("Unvalidated operator."); }
        }
        private static decimal Number(string value, string path)
        {
            if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                throw new ContentException("conditions", path, "Expected invariant decimal value.");
            return number;
        }
    }
}
