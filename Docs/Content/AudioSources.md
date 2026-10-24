# [COMPUTED] 基础音频制作记录

[COMPUTED｜HIGH] 2026-10-23，由表现实现任务使用本仓库生成脚本制作；仅基础正弦振荡器、包络与固定种子音程排列，无外部采样、音色库、录音或引用旋律。标记project-original是制作来源，不是第三方许可或法律权属结论。

[COMPUTED｜HIGH] 单声道16-bit PCM；脚本检查峰值不削波，并记录首尾样本。能解码与数值接缝检查不等于已试听或引擎播放验收。

```json
{
  "seed": 20261023,
  "generator": "Tools/Content/GenerateAudio.py",
  "records": [
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/daily.wav",
      "sha256": "6204387F9440BB435EA02B9F5458FC1259D5D65D46A9B0377BB1D97756B81600",
      "seconds": 16,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 6971,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/competition.wav",
      "sha256": "0D9AEBC6F6C5464F2532EB6476D159AD33BEE5B304F8DA729DAF2CD23B02D5EA",
      "seconds": 16,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 6971,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/confirm.wav",
      "sha256": "8D0034691484367F48A53AD51EF588C123F37CA93EF2C34560DE3F05936DAC67",
      "seconds": 0.16,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 9382,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/invalid.wav",
      "sha256": "D4CFE29BBABDA690BB0442C48ED7D9608E27DB75A73DAD37D52A1C2CEE11EE50",
      "seconds": 0.18,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 9354,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/training_valid.wav",
      "sha256": "CFBC52CC626ABE576D21870773393126FC9E221CD7470F89C473CBA4E785E20B",
      "seconds": 0.22,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 9711,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/training_failed.wav",
      "sha256": "BCED9ED4F5A1070FC76C2ED9ED924B19B41ED69320A12174C7A74EB053DCFB2B",
      "seconds": 0.25,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 9839,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/result.wav",
      "sha256": "957A5EEB36164671B8CC03A53169C7E9DA5D9D7390674AD599D059CC3ECBCBA8",
      "seconds": 0.7,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 10258,
      "firstSample": 0,
      "lastSample": 0
    },
    {
      "file": "Assets/FitnessSimulator/Production/Resources/GameAudio/ending.wav",
      "sha256": "0E5607B1D0D3F0381F01ADE4DBD45B2CF2D315915641644F72369E67722FEBDF",
      "seconds": 1.1,
      "sampleRate": 22050,
      "channels": 1,
      "peak": 10341,
      "firstSample": 0,
      "lastSample": 0
    }
  ]
}
```

[RULES I BROKE]: 无。
