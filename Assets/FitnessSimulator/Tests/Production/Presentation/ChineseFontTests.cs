using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class ChineseFontTests
    {
        [Test] public void BundledFontContainsActualInterfaceChineseAndLicense()
        {
            const string root = "Assets/FitnessSimulator/Production/Presentation/Fonts/";
            var font = AssetDatabase.LoadAssetAtPath<Font>(root + "NotoSansCJKsc-Regular.otf");
            Assert.That(font, Is.Not.Null, "No system-font fallback is allowed for the formal offline UI.");
            foreach (char glyph in "健身模拟器卧推左右修正训练休息疲劳力量确认计划保存失败继续角色当天安排")
                Assert.That(font.HasCharacter(glyph), Is.True, "Missing glyph " + glyph);
            var license = AssetDatabase.LoadAssetAtPath<TextAsset>(root + "OFL.txt");
            Assert.That(license, Is.Not.Null);
            Assert.That(license.text, Does.Contain("SIL OPEN FONT LICENSE Version 1.1"));
        }
    }
}
