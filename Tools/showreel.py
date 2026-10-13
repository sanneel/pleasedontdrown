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
            "costar 1; wait 2.5; goto Basketball; wait 0.5; grab Basketball; wait 0.4; inventory; tp -5.6 1.5 23.6; "
            "lookat -5.9 1 29.6; wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.75; wait 3.2; "
            "goto Basketball; wait 0.5; grab Basketball; wait 0.4; tp -5 1.5 22.6; lookat -5.8 1 29.6; "
            "wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.8; wait 5",
        ],
    },
    "banana": {
        # Two on the banana, one on the jet ski towing it; a hard turn at speed throws them off. From behind.
        "host": f"{PARK}; cam fixed 7 1.6 -7.5 1.5 0.4 -14 52; wait 1; record banana_board {REC}; wait 6; record stop; "
                f"cam chase Banana 7 5 4 50; record banana_ride {REC}; wait 13; record stop",
        "actors": [
            "costar 1; wait 1; goto Lifeguard; wait 0.5; use; wait 5; drive 8 0 1; wait 8; drive 4 1 1; wait 6",
            "costar 2; wait 2.5; goto Banana; wait 0.5; use; wait 20",
            "costar 3; wait 3.5; goto Banana; wait 0.5; use; wait 20",
        ],
    },
    "banana2": {
        # The same ride from ahead of the banana: the riders' faces.
        "host": f"{PARK}; wait 7; cam chase Banana -6 3.2 3 50; record banana_front {REC}; wait 13; record stop",
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
    "fpkiss": {
        # The kiss of life through the lifeguard's own eyes (the host acts and films itself: cam eyes).
        "host": f"god; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious f; wait 2.5; cam eyes; "
                f"record cpr_fp_kiss {REC}; wait 0.3; cpr 24 0.5; wait 16; record stop",
        "actors": [],
    },
    "fpslap": {
        # A man won't wake to a kiss: compressions, then the slap, first person.
        "host": f"god; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious m; wait 2.5; cam eyes; "
                f"record cpr_fp_slap {REC}; wait 0.3; cpr 18 0.45; wait 14; record stop",
        "actors": [],
    },
    "fppirates": {
        # Skull Cove at night: a raiding party (blades, fists, pistols, muskets) closes in on you; first person, looking
        # from one to the next, one punch at the end (punching early knocks them out before they crowd you).
        "host": f"god; island3; wait 6; brawltest 7 mix; wait 2; aimat One-Eye head; cam eyes; record pirates_fp {REC}; "
                "wait 2.5; aimat Barnacle head; wait 2.2; aimat Saltbeard head; wait 2.2; aimat Rusty head; wait 2.2; "
                "aimat Gulliver head; wait 2; aimat Mack head; wait 1; punch; wait 1.5; record stop",
        "actors": [],
    },
    "pirates": {
        # The same raid from outside: circling the lifeguard while they come at him.
        "host": f"{PARK}; wait 8.5; cam orbit p1 3.6 1.9 14 48; record pirates_orbit {REC}; wait 14; record stop",
        "actors": [
            "costar 1; god; island3; wait 5; brawltest 7 mix; wait 2.5; aimat One-Eye; wait 2.5; aimat Barnacle; wait 2.5; "
            "aimat Saltbeard; wait 2.5; aimat Rusty; punch; wait 1; punch hook; wait 8",
        ],
    },
    "girls": {
        # Every tourist girl lying on the sand, one still each, to pick a clean one for the carry-and-throw.
        "host": "tp 3.4 1.5 0.0; lookat 8 1.5 0.0; wait 0.4; victim 1.8 unconscious f 16; wait 0.3; tp 3.4 1.5 1.4; lookat 8 1.5 1.4; wait 0.4; victim 1.8 unconscious f 17; wait 0.3; tp 3.4 1.5 2.8; lookat 8 1.5 2.8; wait 0.4; victim 1.8 unconscious f 18; wait 0.3; tp 3.4 1.5 4.2; lookat 8 1.5 4.2; wait 0.4; victim 1.8 unconscious f 19; wait 0.3; tp 3.4 1.5 5.6; lookat 8 1.5 5.6; wait 0.4; victim 1.8 unconscious f 20; wait 0.3; tp 3.4 1.5 7.0; lookat 8 1.5 7.0; wait 0.4; victim 1.8 unconscious f 23; wait 0.3; tp 3.4 1.5 8.4; lookat 8 1.5 8.4; wait 0.4; victim 1.8 unconscious f 24; wait 0.3; noclip; tp 0 80 0; wait 1.5; cam fixed 6.1 1.7 0.35 5.2 0.15 0.0 50; wait 0.4; record girl_16_lola 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 1.75 5.2 0.15 1.4 50; wait 0.4; record girl_17_red 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 3.15 5.2 0.15 2.8 50; wait 0.4; record girl_18_blonde 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 4.55 5.2 0.15 4.2 50; wait 0.4; record girl_19_redhead 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 5.95 5.2 0.15 5.6 50; wait 0.4; record girl_20_black 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 7.35 5.2 0.15 7.0 50; wait 0.4; record girl_23_pink 720 1280 30; wait 0.7; record stop; cam fixed 6.1 1.7 8.75 5.2 0.15 8.4 50; wait 0.4; record girl_24_lavender 720 1280 30; wait 0.7; record stop",
        "actors": [],
    },
    "hook": {
        # A drowning girl out along the cannon's line; a lifeguard stuffs his friend in the cannon, aims at her and fires.
        "host": f"tp 28 1.5 7; lookat 45 1.5 -40; wait 0.3; victim 46 drowning f 17; wait 0.3; {PARK}; cam fixed 30 1.4 12.5 36 4 -15 62; record hook_boom {REC}; wait 17; record stop",
        "actors": [
            "costar 1; tp 24.5 1.5 10; lookat 25.5 1 11.2; wait 2; carry; wait 1.5; lookat 28 1 7; walk 0.9; wait 1.5; cannon; "
            "wait 1.5; lookat 45 30 -40; wait 1.5; cannon fire; wait 10",
            "costar 2; tp 25.4 1.5 11.0; lookat 24 1.5 10; wait 22",
        ],
    },
    "hook2": {
        # Riding along with the flying friend.
        "host": f"tp 28 1.5 7; lookat 45 1.5 -40; wait 0.3; victim 46 drowning f 17; wait 0.3; {PARK}; wait 5; cam follow p2 -2.5 2 6 58; record hook_fly {REC}; wait 13; record stop",
        "actors": "hook",
    },
    "hook3": {
        # At the drowning girl, looking back up the line of fire: the friend comes down out of the sky beside her.
        "host": f"tp 28 1.5 7; lookat 45 1.5 -40; wait 0.3; victim 46 drowning f 17; wait 0.3; {PARK}; wait 2; cam look victim -2.4 2.6 6.6 58; record hook_land {REC}; wait 16; record stop",
        "actors": "hook",
    },
    "ff": {
        # Friendly fire: three quick punches knock a lifeguard flat (stars, googly eyes). Side on, open sand.
        "host": f"{PARK}; wait 1.5; cam rel p2 p1 2.8 1.0 -0.6 45; record ff_side {REC}; wait 9; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 4.6 1.5 4.5; wait 3; punch; wait 0.35; punch hook; wait 0.35; punch uppercut; wait 8",
            "costar 2; tp 4.6 1.5 4.5; lookat 3.4 1.5 4.5; wait 12",
        ],
    },
    "ff2": {
        # Down on the sand at the knocked-out lifeguard's face.
        "host": f"{PARK}; wait 4.4; cam rel p2.head p1 0.9 0.25 0.5 38; record ff_face {REC}; wait 4; record stop",
        "actors": "ff",
    },
    "shark": {
        # A swimmer in trouble, then the shark.
        "host": f"tp 5 1.5 0; lookat 5 0 -30; wait 0.5; victim 14 distressed f 20; wait 1.5; noclip; tp 0 80 0; "
                f"cam look victim 3 1.2 5 45; record shark {REC}; wait 1.5; shark; wait 11; record stop",
        "actors": [],
    },
    "klepto": {
        # The Kleptopus at the hotel pier grabbing a sunbather (its test run), circled.
        "host": f"god; kleptocheck; wait 1.5; cam orbit Kleptopus 15 6 9 55; record klepto {REC}; wait 15; record stop",
        "actors": [],
    },
    "klepto2": {
        # The same, from the pier through the lifeguard's eyes.
        "host": f"god; kleptocheck; wait 1.5; aimat Kleptopus; cam eyes; record klepto_fp {REC}; wait 4; aimat Kleptopus head; wait 10; record stop",
        "actors": [],
    },
    "tsunami": {
        # The tsunami coming in over the beach, two lifeguards standing on the sand for scale.
        "host": f"{PARK}; wait 2; tsunami start; wait 10; cam fixed 6 2.2 16 6 4 -40 62; record tsunami {REC}; wait 23; record stop",
        "actors": [
            "costar 1; tp 3.5 1.5 6; lookat 3.5 1.5 -40; wait 40",
            "costar 2; tp 8 1.5 5; lookat 8 1.5 -40; wait 40",
        ],
    },
    "tsunami2": {
        # Through the eyes of a lifeguard on the beach as it comes.
        "host": f"god; tp 3.4 1.5 4.5; lookat 3.4 5 -40; wait 1; tsunami start; wait 10; cam eyes; record tsunami_fp {REC}; wait 22; record stop",
        "actors": [],
    },
    "guns": {
        # Skull Cove at night: pirates with pistols and muskets keep their distance and shoot at you. First person.
        "host": f"god; island3; wait 6; brawltest 6 guns; wait 2.5; aimat One-Eye; cam eyes 26; record pirates_guns_fp {REC}; "
                "wait 3; aimat Barnacle; wait 3; aimat Saltbeard; wait 3; aimat Rusty; wait 3; record stop",
        "actors": [],
    },
    "guns2": {
        # Over the shoulder of the lifeguard they're shooting at.
        "host": f"{PARK}; wait 9; cam chase p1 3.6 1.9 1.4 52; record pirates_guns_ots {REC}; wait 13; record stop",
        "actors": [
            "costar 1; god; island3; wait 5; brawltest 6 guns; wait 3; aimat One-Eye; wait 3.5; aimat Barnacle; wait 3.5; "
            "aimat Saltbeard; wait 3.5; aimat Rusty; wait 8",
        ],
    },
    "carry": {
        # The redhead (body 19, the cleanest face) picked up unconscious and thrown up the beach; framed on her face.
        "host": f"{PARK}; wait 3.5; cam look victim.head 2.2 1.0 3.2 42; record carry_throw {REC}; wait 10; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1; victim 1.6 unconscious f 19; wait 2.5; grab; wait 1.5; "
            "lookat 8 1.5 4.5; walk 0.6; wait 1; lookat 9 3 4.5; wait 0.4; throw 1; wait 8",
        ],
    },
    "carry2": {
        # From in front of her: her face, the lifeguard behind.
        "host": f"{PARK}; wait 3.5; cam rel victimf.head p1 0.8 0.6 2.6 45; record carry_front {REC}; wait 10; record stop",
        "actors": "carry",
    },
    "probe_carry": {
        # Animation check: a lifeguard picks up an unconscious tourist and carries her (side and front).
        "host": f"{PARK}; wait 5; cam fixed 3.6 1.3 7.8 3.6 1.0 4.5 45; record pc_side 540 960 30; wait 1.5; record stop; "
                "cam fixed 6.6 1.3 4.6 3.4 1.0 4.5 45; record pc_front 540 960 30; wait 1.5; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 0.5; victim 1.6 unconscious f 19; wait 2; grab; wait 1; lookat 8 1.5 4.5; wait 10",
        ],
    },
    "probe_friend": {
        # Animation check: picking up a friend (the lift), walking to the cannon, stuffing them in, BOOM. From the side.
        "host": f"{PARK}; wait 1.2; cam fixed 22.5 1.6 13.5 26 1.0 9 50; record pf_carry 540 960 30; wait 8; record stop; "
                "cam fixed 37 2.2 10.5 29 2.8 4 62; record pf_load 540 960 30; wait 9; record stop",
        "actors": [
            "costar 1; tp 24.5 1.5 10; lookat 25.5 1 11.2; wait 2; carry; wait 2; lookat 28 1 7; walk 0.9; wait 2.5; walk 0.6; wait 2; "
            "cannon; wait 3; lookat 45 30 -40; wait 1.5; cannon fire; wait 8",
            "costar 2; tp 25.4 1.5 11.0; lookat 24 1.5 10; wait 25",
        ],
    },
    "probe_tsunami": {
        # Shader check: the tsunami face from the beach as it comes in, and close.
        "host": f"{PARK}; tsunami start; wait 14; cam fixed 6 1.8 3 6 3 -40 60; record pt_far 540 960 30; wait 1; record stop; "
                "wait 6; record pt_near 540 960 30; wait 1; record stop; wait 3; record pt_hit 540 960 30; wait 1; record stop",
        "actors": [],
    },
    "probe_klepto": {
        # Animation check: the Kleptopus idling (breathing, looking about, arm slaps).
        "host": "god; kleptocheck; wait 3; aimat Kleptopus; cam eyes 50; record pk 540 960 30; wait 10; record stop",
        "actors": [],
    },
    "probe_guns": {
        # Animation check: pirates with pistols and muskets shooting, in daylight on the beach (side view).
        "host": f"{PARK}; wait 7; cam look p1 0 7 12 62; record pg 540 960 30; wait 8; record stop",
        "actors": [
            "costar 1; god; tp 3.4 1.5 6; lookat 8 1.5 6; wait 1; brawltest 4 guns; wait 16",
        ],
    },
    "troll": {
        # The troll, start to finish: a friend waves, gets picked up, carried to the human cannon, stuffed in, BOOM. Wide.
        "host": f"{PARK}; cam fixed 19.1 1.8 1.9 26.2 1.2 9 58; record troll_wide {REC}; wait 22; record stop",
        "actors": [
            "costar 1; tp 24.5 1.5 10; lookat 25.5 1 11.2; wait 2.5; carry; wait 2; lookat 28 1 7; walk 0.9; wait 2.5; walk 0.6; wait 2; "
            "cannon; wait 2.5; lookat 45 30 -40; wait 1.5; cannon fire; wait 9",
            "costar 2; tp 25.4 1.5 11.0; lookat 24 1.5 10; wait 0.6; wave; wait 25",
        ],
    },
    "troll2": {
        # Riding behind the carrier: the friend in his arms, the cannon ahead.
        "host": f"{PARK}; wait 2; cam chase p1 3.2 1.7 1.6 50; record troll_carry {REC}; wait 14; record stop",
        "actors": "troll",
    },
    "troll3": {
        # With the friend as he's fired out over the sea.
        "host": f"{PARK}; wait 11; cam follow p2 -2.5 2 6 58; record troll_fly {REC}; wait 12; record stop",
        "actors": "troll",
    },
    "banana3": {
        # The banana from high at the side: the riders, the hard turn, everyone in the sea.
        "host": f"{PARK}; wait 6; cam chase Banana 2 4 6 55; record banana_side {REC}; wait 15; record stop",
        "actors": "banana",
    },
    "cprwide": {
        # CPR, the whole picture: the lifeguard pumping, her face, compressions only.
        "host": f"{PARK}; wait 3.5; cam rel victimf.chest p1 0.9 1.3 2.0 50; record cpr_wide {REC}; wait 9; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious f 19; wait 3; cpr 14 0.45; wait 10",
        ],
    },
    "beer": {
        # A lifeguard downs a beer on the beach, and a couple of seconds later the burp comes up.
        "host": f"{PARK}; wait 2.5; cam look p1.head 1.6 -0.15 0.35 40; record beer {REC}; wait 9; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 1.5 4.5; wait 1; spawn Beer; wait 0.6; grab Beer; wait 0.8; eat 3; wait 10",
        ],
    },
    "ball1v1": {
        # One on one: two lifeguards taking turns at the hoop, one from the three-point line.
        "host": f"{PARK}; cam fixed -4.5 1.8 16.5 -6 2.2 26 55; wait 2.5; record ball_1v1 {REC}; wait 15; record stop",
        "actors": [
            "costar 1; wait 2.5; goto Basketball; wait 0.5; grab Basketball; wait 0.4; tp -5.6 1.5 23.6; "
            "lookat -5.9 1 29.6; wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.75; wait 6.5; "
            "goto Basketball; wait 0.5; grab Basketball; wait 0.4; tp -5 1.5 22.6; lookat -5.8 1 29.6; "
            "wait 0.6; walk 0.45; wait 0.9; lookat -6 3.4 29.6; wait 0.5; throw 0.8; wait 5",
            "costar 2; wait 5.5; goto Basketball; wait 0.5; grab Basketball; wait 0.4; tp -8.6 1.5 23.6; "
            "lookat -6.2 1 29.6; wait 0.6; walk 0.4; wait 0.9; lookat -6.1 3.6 29.6; wait 0.5; throw 0.85; wait 8",
        ],
    },
    "klepto3": {
        # The Kleptopus from the pier, through the lifeguard's eyes (breathing, watching, slapping the water).
        "host": "god; kleptocheck; wait 3; aimat Kleptopus; cam eyes 55; record klepto_fp2 {REC}; wait 12; record stop".replace("{REC}", REC),
        "actors": [],
    },
    "klepto4": {
        # Circling the lifeguard on the pier with the Kleptopus behind.
        "host": "god; kleptocheck; wait 3; aimat Kleptopus; cam orbit p0 6 2.5 14 55; record klepto_orbit {REC}; wait 12; record stop".replace("{REC}", REC),
        "actors": [],
    },
    "gunsday": {
        # Pirates with pistols and muskets shooting at a lifeguard on the beach in daylight: over his shoulder.
        "host": f"{PARK}; wait 6.5; cam chase p1 3.0 1.8 1.2 50; record guns_ots {REC}; wait 12; record stop",
        "actors": [
            "costar 1; god; tp 3.4 1.5 6; lookat 8 1.5 6; wait 1; brawltest 5 guns; wait 4; aimat One-Eye; wait 3; aimat Barnacle; "
            "wait 3; aimat Saltbeard; wait 3; aimat Rusty; wait 6",
        ],
    },
    "gunsday2": {
        # The same, wide from the side.
        "host": f"{PARK}; wait 6.5; cam look p1 6 2.5 6 60; record guns_wide {REC}; wait 12; record stop",
        "actors": "gunsday",
    },
    "troll4": {
        # The troll, closer: the wave, the pick-up, the walk, the cannon.
        "host": f"{PARK}; cam fixed 22.5 1.6 13.8 26.3 1.1 9 52; record troll_mid {REC}; wait 21; record stop",
        "actors": "troll",
    },
    "cprlow": {
        # CPR low from across her: her face in profile in front, the lifeguard pumping behind.
        "host": f"{PARK}; wait 3.5; cam rel victimf.mouth p1 1.6 0.5 0.4 48; record cpr_low {REC}; wait 9; record stop",
        "actors": "cprwide",
    },
    "cprlow2": {
        # CPR from across the body, low.
        "host": f"{PARK}; wait 3.5; cam rel victimf.chest p1 0.3 0.6 1.9 55; record cpr_low2 {REC}; wait 9; record stop",
        "actors": "cprwide",
    },
    "beer2": {
        # The beer, from a step back: the swig, then the burp.
        "host": f"{PARK}; wait 2.5; cam look p1.head 2.6 -0.45 0.9 45; record beer_wide {REC}; wait 9; record stop",
        "actors": "beer",
    },
    "klepto5": {
        # Over the lifeguard's shoulder on the pier, the Kleptopus in front.
        "host": "god; kleptocheck; wait 3; aimat Kleptopus; cam chase p0 3 1.6 1 52; record klepto_ots {REC}; wait 12; record stop".replace("{REC}", REC),
        "actors": [],
    },
    "gunsfp": {
        # Pirates in daylight shooting at you: your own eyes, zoomed in on each gunman in turn.
        "host": "god; tp 3.4 1.5 6; lookat 8 1.5 6; wait 1; brawltest 4 guns; wait 3.5; aimat One-Eye; cam eyes 24; record guns_fp {REC}; "
                "wait 3; aimat Barnacle; wait 3; aimat Saltbeard; wait 3; aimat Rusty; wait 3; record stop".replace("{REC}", REC),
        "actors": [],
    },
    "troll5": {
        # The troll, tight: everybody set up and still before the camera rolls; a friend right behind the cannon gets
        # picked up and stuffed straight in, aimed and fired. Medium shot.
        "host": f"{PARK}; wait 3; cam fixed 22.6 1.6 13.8 27.0 1.2 9.4 54; record troll_tight {REC}; wait 10; record stop",
        "actors": [
            "costar 1; tp 26.6 1.5 10.4; lookat 27.6 1 10.4; wait 4; carry; wait 0.9; lookat 28 1 7; wait 0.4; cannon; "
            "wait 2; lookat 45 30 -40; wait 1.2; cannon fire; wait 8",
            "costar 2; tp 27.6 1.5 10.4; lookat 26.6 1.5 10.4; wait 3.2; wave; wait 20",
        ],
    },
    "troll6": {
        # The launch from behind the cannon along the line of fire: out of the muzzle and away over the sea.
        "host": f"{PARK}; wait 6.5; cam fixed 28.2 2.6 12.6 32 4.5 -4.3 60; record troll_launch {REC}; wait 6; record stop",
        "actors": "troll5",
    },
    "troll7": {
        # Riding with him from the moment he's out of the barrel to the splash.
        "host": f"{PARK}; wait 8.9; cam follow p2 -2.5 2 6 58; record troll_follow {REC}; wait 7; record stop",
        "actors": "troll5",
    },
    "ball2": {
        # One on one, close: both already holding a ball before the camera rolls, they shoot in turn; no teleports.
        "host": f"{PARK}; wait 3; cam fixed -6.1 1.7 18.8 -6.1 2.4 27 60; record ball_1v1b {REC}; wait 9; record stop",
        "actors": [
            "costar 1; tp -4.9 1.5 24.0; lookat -6 1 29.6; wait 0.5; spawn Basketball; wait 0.5; grab Basketball; wait 3; "
            "lookat -5.9 1 29.6; walk 0.35; wait 0.8; lookat -6 3.4 29.6; wait 0.5; throw 0.75; wait 8",
            "costar 2; tp -7.3 1.5 24.4; lookat -6 1 29.6; wait 1.6; spawn Basketball; wait 0.5; grab Basketball; wait 4.6; "
            "lookat -6.1 1 29.6; walk 0.35; wait 0.8; lookat -6.1 3.5 29.6; wait 0.5; throw 0.8; wait 6",
        ],
    },
    "cprfix": {
        # CPR after the pose fix (hands on the lower breastbone, kneeling further out), low from across her.
        "host": f"{PARK}; wait 3.5; cam rel victimf.mouth p1 1.6 0.5 0.4 48; record cpr_fix {REC}; wait 6; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious f 19; wait 3; cpr 12 0.45; wait 8",
        ],
    },
    "cprfix2": {
        "host": f"{PARK}; wait 3.5; cam rel victimf.chest p1 0.3 0.75 2.1 52; record cpr_fix2 {REC}; wait 6; record stop",
        "actors": "cprfix",
    },
    "pirface": {
        # A pirate aiming and firing at the lifeguard, the camera just in front of him (nothing in between).
        "host": f"{PARK}; wait 7; cam rel One-Eye p1 0.9 1.5 -2.6 45; record pirate_face {REC}; wait 8; record stop",
        "actors": [
            "costar 1; god; tp 3.4 1.5 6; lookat 8 1.5 6; wait 1; brawltest 3 guns; wait 18",
        ],
    },
    "pirback": {
        # Over the pirate's shoulder: his gun up, the lifeguard he's shooting at in front.
        "host": f"{PARK}; wait 7; cam rel Barnacle p1 0.7 1.75 1.9 48; record pirate_back {REC}; wait 8; record stop",
        "actors": "pirface",
    },
    "klepto6": {
        # The Kleptopus circled from the water (by name now).
        "host": "god; kleptocheck; wait 3; cam orbit Kleptopus 13 4.5 10 55; record klepto_orbit2 {REC}; wait 12; record stop".replace("{REC}", REC),
        "actors": [],
    },
    "cprfix3": {
        # CPR after the posture fix (more upright, head up): across her, timed on the compressions.
        "host": f"{PARK}; wait 4.2; cam rel victimf.chest p1 0.3 0.75 2.1 52; record cpr_fix3 {REC}; wait 5.5; record stop",
        "actors": "cprfix",
    },
    "cprfix4": {
        # The same from her side, low, her face and his straight arms.
        "host": f"{PARK}; wait 4.2; cam rel victimf.chest p1 -1.9 0.7 0.9 50; record cpr_fix4 {REC}; wait 5.5; record stop",
        "actors": "cprfix",
    },
    "cprchk": {
        # CPR posture check: side on at hand height (hands on the chest, the head well above them).
        "host": f"{PARK}; wait 4.6; cam rel victimf.chest p1 -1.7 0.55 0.6 45; record cpr_chk {REC}; wait 3.5; record stop",
        "actors": "cprfix",
    },
    "cprchk2": {
        "host": f"{PARK}; wait 4.6; cam rel victimm.chest p1 -1.8 0.6 0.6 45; record cpr_chk_m {REC}; wait 3.5; record stop",
        "actors": [
            "costar 1; tp 3.4 1.5 4.5; lookat 8 0 4.5; wait 1.5; victim 1.6 unconscious m; wait 3; cpr 12 0.45; wait 8",
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
