"""Project-original basic tones. No samples, recordings, soundfonts or referenced melodies.
Run from repository root: python Tools/Content/GenerateAudio.py
"""
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/FitnessSimulator/Production/Resources/GameAudio"
RATE = 22050
SEED = 20261023

def write(name, seconds, music=False, base=440, upward=True):
    count = round(seconds * RATE)
    rng = random.Random(SEED + sum(map(ord, name)))
    offsets = [rng.choice((0, 3, 5, 7)) for _ in range(8)]
    samples = []
    for i in range(count):
        t = i / RATE
        if music:
            slot = min(7, int(t / 2))
            local = t % 2
            freq = base * 2 ** (offsets[slot] / 12)
            envelope = min(1, local / .08) * min(1, (2 - local) / .25)
            sample = sum(math.sin(2 * math.pi * freq * ratio * local) / (n + 1) for n, ratio in enumerate((1, 1.25, 1.5))) * .12 * envelope
        else:
            envelope = min(1, t / .008) * max(0, 1 - t / seconds) ** 2
            slope = .6 if upward else -.45
            phase = 2 * math.pi * base * (t + slope * t * t / (2 * seconds))
            sample = .3 * envelope * (math.sin(phase) + .2 * math.sin(2 * phase))
        sample *= min(1, t / .008, (seconds - t) / .008)
        samples.append(round(sample * 32767))
    if max(map(abs, samples)) >= 32767:
        raise RuntimeError("Clipping")
    path = OUT / (name + ".wav")
    with wave.open(str(path), "wb") as stream:
        stream.setnchannels(1); stream.setsampwidth(2); stream.setframerate(RATE)
        stream.writeframes(struct.pack("<" + "h" * len(samples), *samples))
    return {"file": path.relative_to(ROOT).as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(), "seconds": seconds,
            "sampleRate": RATE, "channels": 1, "peak": max(map(abs, samples)), "firstSample": samples[0], "lastSample": samples[-1]}

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    specs = [("daily",16,True,174,True),("competition",16,True,220,True),("confirm",.16,False,660,True),
             ("invalid",.18,False,180,False),("training_valid",.22,False,520,True),("training_failed",.25,False,150,False),
             ("result",.7,False,390,True),("ending",1.1,False,330,True)]
    records = [write(*spec) for spec in specs]
    doc = {"Version":"audio.project-original.1", "DefaultMusicEnabled":True,"DefaultSfxEnabled":True,"DefaultMusicVolume":.25,"DefaultSfxVolume":.5,
           "Entries":[{"Id":spec[0],"AssetRef":"GameAudio/"+spec[0],"Kind":"music" if spec[2] else "sfx","Loop":spec[2],"Gain":1,
                       "Source":"project-original", "SourceRecord":"Docs/Content/AudioSources.md", "Sha256":record["sha256"]} for spec,record in zip(specs,records)],
           "Cues":[{"Cue":spec[0],"AudioId":spec[0]} for spec in specs]}
    target = ROOT / "Assets/FitnessSimulator/Production/Resources/GameContent/audio.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(doc,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    report = {"seed":SEED,"generator":"Tools/Content/GenerateAudio.py","records":records}
    report_text = json.dumps(report, ensure_ascii=False, indent=2)
    source = ROOT / "Docs/Content/AudioSources.md"
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_text("# [COMPUTED] 基础音频制作记录\n\n[COMPUTED｜HIGH] 2026-10-23，由表现实现任务使用本仓库生成脚本制作；仅基础正弦振荡器、包络与固定种子音程排列，无外部采样、音色库、录音或引用旋律。标记project-original是制作来源，不是第三方许可或法律权属结论。\n\n[COMPUTED｜HIGH] 单声道16-bit PCM；脚本检查峰值不削波，并记录首尾样本。能解码与数值接缝检查不等于已试听或引擎播放验收。\n\n```json\n"+report_text+"\n```\n\n[RULES I BROKE]: 无。\n",encoding="utf-8")
    print(report_text)

if __name__ == "__main__":
    main()
