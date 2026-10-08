"""Import the requested local How to Fish water recordings into the project's water bank.

Requires UnityPy (the project's Library/ReferenceTools copy is supported) and numpy.
The source installation is read only. Provenance and processing are recorded in Docs.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Library/ReferenceTools'))
import UnityPy
import numpy as np


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True, help='How to Fish sharedassets0.assets')
    args = parser.parse_args()
    target = ROOT / 'Assets/_Game/Resources/Audio/Water'
    target.mkdir(parents=True, exist_ok=True)
    wanted = {f'Footstep_Water_V{i}': .026 for i in range(1, 6)}
    for size, level in [('Light', .018), ('Medium', .028), ('Heavy', .045)]:
        wanted.update({f'ItemHitWater{size}_V{i}': level for i in range(1, 4)})
    for direction in ['Enter', 'Exit']:
        wanted.update({f'UnderwaterOn{direction}_{i:02}': .035 for i in range(1, 3)})
    wanted.update({'UnderwaterLoop': .05, 'SeaAmbient_Loop_Mono': .035})
    env = UnityPy.load(str(args.source))
    records = []
    for obj in env.objects:
        if obj.type.name != 'AudioClip':
            continue
        clip = obj.read()
        if clip.m_Name not in wanted:
            continue
        raw = next(iter(clip.samples.values()))
        with wave.open(io.BytesIO(raw)) as src:
            if src.getsampwidth() != 2:
                raise ValueError('Expected decoded PCM16: ' + clip.m_Name)
            rate = src.getframerate()
            data = np.frombuffer(src.readframes(src.getnframes()), '<i2')
            data = data.astype(np.float64).reshape(-1, src.getnchannels()).mean(axis=1) / 32768
        data -= data.mean()
        loop = 'Loop' in clip.m_Name
        if loop:
            # Overlap the wrap, never fade a looping ambience down to silence.
            n = round(rate * .06)
            ramp = np.linspace(0, 1, n)
            data = np.concatenate([data[n:-n], data[-n:] * (1 - ramp) + data[:n] * ramp])
        else:
            fade_in, fade_out = round(rate * .003), round(rate * .012)
            data[:fade_in] *= np.linspace(0, 1, fade_in)
            data[-fade_out:] *= np.linspace(1, 0, fade_out)
        rms = float(np.sqrt(np.mean(data * data)))
        peak = float(np.max(np.abs(data)))
        gain = min(wanted[clip.m_Name] / max(rms, 1e-8), .78 / max(peak, 1e-8), 12)
        data *= gain
        pcm = np.round(data * 32767).astype('<i2')
        output = target / (clip.m_Name + '.wav')
        with wave.open(str(output), 'wb') as dst:
            dst.setnchannels(1)
            dst.setsampwidth(2)
            dst.setframerate(rate)
            dst.writeframes(pcm.tobytes())
        records.append({'name': clip.m_Name, 'source_path_id': obj.path_id,
                        'source_decoded_sha256': hashlib.sha256(raw).hexdigest(),
                        'output_sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
                        'seconds': round(len(data) / rate, 4), 'sample_rate': rate,
                        'loop': loop, 'gain': round(gain, 5),
                        'rms': round(rms * gain, 5), 'peak': round(peak * gain, 5)})
    missing = set(wanted) - {r['name'] for r in records}
    if missing:
        raise ValueError('Missing source clips: ' + ', '.join(sorted(missing)))
    manifest = {'source_game': 'How to Fish', 'source_asset': str(args.source),
                'processing': 'Decoded PCM, mono downmix, DC removal, per-role level balance; 3/12 ms one-shot fades or 60 ms loop crossfade. No synthesized water layers.',
                'clips': sorted(records, key=lambda r: r['name'])}
    (ROOT / 'Docs/WaterAudioSources.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(f'Imported {len(records)} recorded water clips into {target}')


if __name__ == '__main__':
    main()
