"""
Films scenes of the game for trailers and TikToks, with no one at the keyboard.

Each scene starts a background copy of the game as the host and cameraman (Dev/Showreel.cs: the `cam` and `record`
console commands) and up to three more background copies as the lifeguards acting it out (the same console
commands the automated tests use: tp, goto, use, drive, carry, cannon, cpr, throw...). They all wait for each other
("waitplayers") and then run their timed scripts. The cameraman's frames are turned into clips with ffmpeg.

    python Tools/showreel.py                      every scene
    python Tools/showreel.py banana cannon        just these
    python Tools/showreel.py --list               what there is
    python Tools/showreel.py --sheets             also a contact sheet per clip (to check them at a glance)

Needs a player build (GameSceneBuilder.BuildPlayerBatch; --exe for another copy) and ffmpeg on the PATH.
Clips go to Builds/Showreel/clips/<recording>.mp4 (1080x1920, 30 fps, no sound: a background copy has none).
Uses port 7790.
"""
import argparse
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_EXE = os.path.join(ROOT, "Builds", "Win64", "PleaseDontDrown.exe")
OUT = os.path.join(ROOT, "Builds", "Showreel")
PARK = "noclip; tp 0 80 0"  # the cameraman's own body: up out of every shot
REC = "1080 1920 30"

# Each scene: what the cameraman (host) does and what each actor (p1, p2, p3 in join order) does, from the moment
# everyone is in. Times line up only roughly (each copy keeps its own clock), so recordings start early and end late.
SCENES = {
    "basketball": {
        # Take a ball off the rack, dribble in and pull up for the jump shot (the shot assist arcs it in), twice.
        "host": f"{PARK}; cam fixed -3.2 1.6 18.8 -5.5 2.1 26 45; wait 2.7; record ball {REC}; wait 12; record stop",
        "actors": [
            "costar 1; tp -6 1.5 27.6; lookat -5.3 0.3 28.4; wait 2.5; grab Basketball; wait 0.4; inventory; tp -5.6 1.5 23.6; "
            "lookat -5.9 1 29.6; wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.75; wait 3.2; "
            "tp -6 1.5 27.6; lookat -6 0.3 28.4; wait 0.3; grab Basketball; wait 0.4; tp -5 1.5 22.6; lookat -5.8 1 29.6; "
            "wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.8; wait 5",
        ],
    },
    "banana": {
        # Two on the banana, one on the jet ski towing it; a hard turn at speed throws them off. From behind.
        "host": f"{PARK}; cam fixed 7 1.6 -7.5 1.5 0.4 -14 52; wait 1; record banana_board {REC}; wait 6; record stop; "
                f"cam chase Banana 9 3.2 1.5 48; record banana_ride {REC}; wait 13; record stop",
        "actors": [
            "costar 1; wait 1; goto Lifeguard; wait 0.5; use; wait 5; drive 8 0 1; wait 8; drive 4 1 1; wait 6",
            "costar 2; wait 2.5; goto Banana; wait 0.5; use; wait 20",
            "costar 3; wait 3.5; goto Banana; wait 0.5; use; wait 20",
        ],
    },
    "banana2": {
        # The same ride from ahead of the banana: the riders' faces.
        "host": f"{PARK}; wait 7; cam chase Banana -5 2 4 50; record banana_front {REC}; wait 13; record stop",
        "actors": "banana",
    },
    "kiss": {
        # Chest compressions and the kiss of life on a woman, side on (she lies head-away from the lifeguard).
        "host": f"{PARK}; wait 3.5; cam rel victimf.chest p1 1.7 0.85 0.15 36; record cpr_f_across {REC}; wait 14; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious f; wait 3; cpr 24 0.5; wait 16",
        ],
    },
    "kiss2": {
        # Down at her face: the kiss in profile.
        "host": f"{PARK}; wait 3.5; cam rel victimf.mouth p1 1.5 0.35 0.1 36; record cpr_f_head {REC}; wait 14; record stop",
        "actors": "kiss",
    },
    "kiss3": {
        # From above his shoulder, looking down at the compressions.
        "host": f"{PARK}; wait 3.5; cam rel victimf.chest p1 1.1 1.5 0.35 34; record cpr_f_feet {REC}; wait 14; record stop",
        "actors": "kiss",
    },
    "slap": {
        # A man won't wake to a kiss: five compressions, then slap him awake.
        "host": f"{PARK}; wait 3.5; cam rel victimm.chest p1 1.8 0.9 0.45 38; record cpr_m_slap {REC}; wait 13; record stop",
        "actors": [
            "costar 2; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious m; wait 3; cpr 18 0.45; wait 14",
        ],
    },
    "cannon": {
        # Pick a friend up, stuff them in the cannon; they aim it round on its wheels and BOOM, out over the sea.
        "host": f"{PARK}; cam follow p2 6.7 1.5 4.4 55; record cannon {REC}; wait 17; record stop",
        "actors": [
            "costar 1; tp 24.5 1.5 10; lookat 25.5 1 11.2; wait 2; carry; wait 1.5; lookat 28 1 7; walk 0.9; wait 1.5; cannon; wait 10",
            "costar 2; tp 25.4 1.5 11.0; lookat 24 1.5 10; wait 6.5; lookat 26 16 -40; wait 0.6; lookat 22 18 -40; wait 0.6; "
            "lookat 18 20 -40; wait 10",
        ],
    },
}


def actors_of(scene):
    actors = SCENES[scene]["actors"]
    return SCENES[actors]["actors"] if isinstance(actors, str) else actors


def launch(exe, log, script, host, players, seconds):
    args = [exe, "-batchmode", "-pdd-nosteam", "-pdd-port", "7790", "-pdd-nosave", "-pdd-nostory",
            "-pdd-quit-after", str(seconds), "-logFile", log]
    args += ["-pdd-host-offline"] if host else ["-nographics", "-pdd-join", "localhost"]
    args += ["-pdd-exec", f"waitplayers {players}; wait 0.5; {script}"]
    return subprocess.Popen(args)


def film(scene, exe, logs):
    actors = actors_of(scene)
    players = 1 + len(actors)
    print(f"== {scene}: {len(actors)} lifeguard(s) acting")
    procs = [launch(exe, os.path.join(logs, f"{scene}-host.log"), SCENES[scene]["host"], True, players, 75)]
    time.sleep(4)  # the host has to be up first
    for i, script in enumerate(actors):
        procs.append(launch(exe, os.path.join(logs, f"{scene}-p{i + 1}.log"), script, False, players, 70))
        time.sleep(2.5)  # one at a time, so they get p1, p2, p3 in order
    for p in procs:
        p.wait()
    with open(os.path.join(logs, f"{scene}-host.log"), encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("[Showreel]") or "failed:" in line or "Exception" in line:
                print("  " + line.rstrip())


def encode(recording, frames_dir, sheets):
    """Frames (taken whenever the game got round to it) -> a steady 30 fps clip."""
    index = os.path.join(frames_dir, "frames.txt")
    rows = [line.split() for line in open(index) if line.strip()]
    if len(rows) < 2:
        print(f"  {recording}: no frames")
        return
    listing = os.path.join(frames_dir, "concat.txt")
    with open(listing, "w") as f:
        for i, (n, t) in enumerate(rows):
            path = os.path.join(frames_dir, f"f{int(n):05d}.jpg").replace("\\", "/")
            if not os.path.exists(path):
                continue
            nxt = float(rows[i + 1][1]) if i + 1 < len(rows) else float(t) + 1 / 30
            f.write(f"file '{path}'\nduration {max(0.001, nxt - float(t)):.4f}\n")
    clips = os.path.join(OUT, "clips")
    os.makedirs(clips, exist_ok=True)
    out = os.path.join(clips, recording + ".mp4")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing,
                    "-vf", "fps=30,format=yuv420p", "-c:v", "libx264", "-crf", "18", "-preset", "medium", out], check=True)
    print(f"  {recording}: {len(rows)} frames -> {out}")
    if sheets:
        sheet = os.path.join(OUT, "sheets", recording + ".jpg")
        os.makedirs(os.path.dirname(sheet), exist_ok=True)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", out, "-vf",
                        "fps=2,scale=216:384,drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='%{pts\\:hms}':x=4:y=4:fontsize=18:fontcolor=white:box=1:boxcolor=black@0.5,tile=8x4",
                        "-frames:v", "1", sheet], check=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scenes", nargs="*")
    ap.add_argument("--exe", default=DEFAULT_EXE)
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--sheets", action="store_true")
    a = ap.parse_args()
    if a.list:
        for name in SCENES:
            print(name)
        return
    if not os.path.exists(a.exe):
        sys.exit(f"No player build at {a.exe}")
    if shutil.which("ffmpeg") is None:
        sys.exit("ffmpeg isn't on the PATH")
    logs = os.path.join(ROOT, "Logs", "showreel")
    os.makedirs(logs, exist_ok=True)
    recordings = os.path.join(os.path.dirname(a.exe), "Recordings")
    for scene in a.scenes or list(SCENES):
        if scene not in SCENES:
            sys.exit(f"no scene '{scene}' (--list)")
        film(scene, a.exe, logs)
        for name in [w.split()[1] for w in SCENES[scene]["host"].split(";") if w.strip().startswith("record ") and "stop" not in w]:
            folder = os.path.join(recordings, name)
            if os.path.isdir(folder):
                encode(name, folder, a.sheets)
            else:
                print(f"  {name}: nothing recorded")


if __name__ == "__main__":
    main()
