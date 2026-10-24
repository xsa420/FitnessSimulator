using System;
using System.IO;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using UnityEngine;

namespace FitnessSimulator.Game.Bootstrap
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private Font chineseFont;
        [SerializeField] private TextAsset fontLicense;
        private IGameFrontend frontend;
        public Font ChineseFont { get => chineseFont; set => chineseFont = value; }
        public TextAsset FontLicense { get => fontLicense; set => fontLicense = value; }
        private void Start()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            UnityEngine.Application.targetFrameRate = 60;
            if (!chineseFont) throw new InvalidOperationException("GameBootstrap requires the licensed Chinese font asset.");
            if (!fontLicense) throw new InvalidOperationException("GameBootstrap requires bundled font license text.");
            // Separate from the historical ResourceLab DevelopmentProbe.
            var saveDirectory = Path.Combine(UnityEngine.Application.persistentDataPath, "PlayerM1");
            frontend = new GameSession(saveDirectory);
            gameObject.AddComponent<GameFrontendView>().Initialize(frontend, chineseFont);
        }

        private void OnDestroy()
        {
            (frontend as IDisposable)?.Dispose();
            frontend = null;
        }
    }
}
