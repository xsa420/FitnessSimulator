using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Narrative;

namespace FitnessSimulator.Game.Presentation
{
    [Serializable] public sealed class PresentationDocument
    {
        public string Version;
        public PresentationStyle Style;
        public PresentationText[] Texts;
        public PresentationBand[] StrengthBands, ContourBands;
    }
    [Serializable] public sealed class PresentationStyle
    {
        public int BodySize, SecondarySize, TitleSize;
        public float LineHeight, ButtonMinHeight, Gap, SafeMargin, CommittedFeedbackSeconds;
    }
    [Serializable] public sealed class PresentationText { public string Id, Text; public string[] Variables; }
    [Serializable] public sealed class PresentationBand { public double Minimum, Maximum; public bool IncludeMinimum, IncludeMaximum; public string TextRef; }
    public static class PresentationContent
    {
        public static PresentationDocument Read() => ContentRuntime.Current.Get<PresentationDocument>("presentation");
        public static string Text(string id, params NarrativeValue[] values)
        {
            return Text(Read(), id, values);
        }
        public static string Text(PresentationDocument document, string id, params NarrativeValue[] values)
        {
            var entry = document.Texts.SingleOrDefault(x => x.Id == id) ?? throw Error("Texts." + id, "Unknown text reference");
            values = values ?? Array.Empty<NarrativeValue>();
            if (values.Any(x => x == null || x.Value == null || !entry.Variables.Contains(x.Key)) || values.Select(x => x.Key).Distinct().Count() != values.Length || entry.Variables.Any(x => !values.Any(v => v.Key == x))) throw Error("Texts." + id, "Variable mismatch");
            return Regex.Replace(entry.Text, @"\{([^{}]+)\}", match => values.Single(x => x.Key == match.Groups[1].Value).Value);
        }
        public static string StrengthDescription(double strength) => Band(Read().StrengthBands, strength, "StrengthBands");
        public static string ContourDescription(double fat) => Band(Read().ContourBands, fat, "ContourBands");
        private static string Band(PresentationBand[] bands, double value, string path)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw Error(path, "Non-finite input");
            var matches = bands.Where(x => (value > x.Minimum || x.IncludeMinimum && value == x.Minimum) && (value < x.Maximum || x.IncludeMaximum && value == x.Maximum)).ToArray();
            if (matches.Length != 1) throw Error(path, "Value outside configured display range: " + value.ToString(CultureInfo.InvariantCulture));
            return Text(matches[0].TextRef);
        }
        public static void ValidateContent()
        {
            var content = Read();
            if (string.IsNullOrWhiteSpace(content.Version) || content.Texts == null || content.Texts.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || x.Text == null || x.Variables == null) || content.Texts.Select(x => x.Id).Distinct().Count() != content.Texts.Length) throw Error("Texts", "Missing/duplicate text");
            foreach (var text in content.Texts)
            {
                if (text.Variables.Any(x => !Regex.IsMatch(x ?? "", @"^[A-Za-z][A-Za-z0-9_]*$")) || text.Variables.Distinct().Count() != text.Variables.Length) throw Error("Texts." + text.Id, "Invalid variables");
                foreach (Match match in Regex.Matches(text.Text, @"\{([^{}]+)\}")) if (!text.Variables.Contains(match.Groups[1].Value)) throw Error("Texts." + text.Id, "Undeclared variable");
            }
            var style = content.Style;
            if (style == null || style.BodySize < 12 || style.BodySize > 36 || style.SecondarySize < 10 || style.SecondarySize > 30 || style.TitleSize < 16 || style.TitleSize > 48) throw Error("Style", "Invalid font sizes");
            foreach (var value in new[] { style.LineHeight, style.ButtonMinHeight, style.Gap, style.SafeMargin, style.CommittedFeedbackSeconds }) if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw Error("Style", "Invalid positive dimension/duration");
            if (style.LineHeight > 2 || style.ButtonMinHeight < 44 || style.CommittedFeedbackSeconds > 10) throw Error("Style", "Dimension/duration out of supported range");
            ValidateBands(content, content.StrengthBands, "StrengthBands"); ValidateBands(content, content.ContourBands, "ContourBands");
        }
        private static void ValidateBands(PresentationDocument content, PresentationBand[] bands, string path)
        {
            if (bands == null || bands.Length == 0) throw Error(path, "Missing bands");
            for (int i = 0; i < bands.Length; i++)
            {
                var band = bands[i];
                if (band == null || double.IsNaN(band.Minimum) || double.IsNaN(band.Maximum) || double.IsInfinity(band.Minimum) || double.IsInfinity(band.Maximum) || band.Minimum >= band.Maximum || !content.Texts.Any(x => x.Id == band.TextRef && x.Variables.Length == 0)) throw Error(path + "[" + i + "]", "Invalid band/text reference");
                if (i > 0 && (band.Minimum != bands[i - 1].Maximum || band.IncludeMinimum == bands[i - 1].IncludeMaximum)) throw Error(path, "Gap/overlap between bands");
            }
        }
        private static ContentException Error(string path, string message) => new ContentException("presentation", path, message);
    }
}
