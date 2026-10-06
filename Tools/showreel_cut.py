"""
Cuts the clips Tools/showreel.py filmed into one vertical video with big captions (TikTok style).

    python Tools/showreel_cut.py                  the cut below -> Builds/Showreel/PleaseDontDrown-showreel.mp4
    python Tools/showreel_cut.py --out x.mp4

Edit CUT to change it: (clip in Builds/Showreel/clips, start s, end s, caption, playback speed). Captions sit in the
top third, white with a black outline; "|" breaks a line.
"""
import argparse
import os
import subprocess
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIPS = os.path.join(ROOT, "Builds", "Showreel", "clips")
FONT = "C\\:/Windows/Fonts/impact.ttf"

CUT = [
    # clip, from, to, caption, speed
    ("banana_front", 0.0, 4.5, "POV: your friends|are the lifeguards", 1.0),
    ("banana_ride", 7.8, 10.0, "", 1.0),
    ("ball", 2.0, 6.0, "\"on duty\"", 1.0),
    ("ball", 8.4, 11.6, "", 1.0),
    ("cpr_f_across", 0.3, 4.8, "finally someone|is drowning", 1.0),
    ("cpr_f_across", 5.2, 7.6, "mouth to mouth.|strictly professional", 1.0),
    ("cpr_m_slap", 4.8, 10.2, "him though?|SLAP HIM AWAKE", 1.0),
    ("cannon", 1.4, 5.2, "then this one|told the boss", 1.0),
    ("cannon", 8.4, 12.2, "so we fired him", 1.0),
]


def caption_filter(text, workdir, i):
    if not text:
        return "null"
    path = os.path.join(workdir, f"caption{i}.txt")
    with open(path, "w", encoding="utf-8") as f:
        f.write(text.replace("|", "\n"))
    p = path.replace("\\", "/").replace(":", "\\:")
    return (f"drawtext=fontfile='{FONT}':textfile='{p}':fontsize=92:fontcolor=white:borderw=7:bordercolor=black:"
            f"line_spacing=10:x=(w-text_w)/2:y=h*0.17")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, "Builds", "Showreel", "PleaseDontDrown-showreel.mp4"))
    a = ap.parse_args()
    with tempfile.TemporaryDirectory() as work:
        parts = []
        for i, (clip, start, end, text, speed) in enumerate(CUT):
            src = os.path.join(CLIPS, clip + ".mp4")
            part = os.path.join(work, f"part{i:02d}.mp4")
            vf = f"setpts=PTS/{speed},{caption_filter(text, work, i)},fps=30,format=yuv420p"
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", str(start), "-to", str(end), "-i", src,
                            "-vf", vf, "-an", "-c:v", "libx264", "-crf", "18", "-preset", "medium", part], check=True)
            parts.append(part)
        listing = os.path.join(work, "parts.txt")
        with open(listing, "w") as f:
            for p in parts:
                f.write(f"file '{p}'\n")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy",
                        "-movflags", "+faststart", a.out], check=True)
    print(a.out)


if __name__ == "__main__":
    main()
