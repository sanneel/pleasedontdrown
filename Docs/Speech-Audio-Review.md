# Speech and rescue audio review

The NPC dialogue path now puts each DialogueService line in a rounded, dark-text bubble over its speaker. It follows the NPC's head for the full reading time. Pressing E on story characters reaches this path through the story's talk event. Beach chatter and shouts use the same bubble, while reward, damage and other floating notifications keep their plain popup presentation. The subtitle remains for accessibility and for player or offscreen lines.

Rescue calls now use short intelligible local TTS recordings for “Help!”, “Over here!”, “I can't swim!” and the island-one victim lines. The recordings were generated from the installed Microsoft Zira Desktop voice. Lower pitched files supply a second register, but they are the same synthetic voice, not a separate actor or a male performance. The on-screen text matches the selected audio line. For unrecorded custom shouts the game still uses a wordless procedural cry. The previous doubled cry layer was removed. The WAVs can be regenerated with Tools/GenerateRescueBarks.ps1 on a Windows machine with that voice and FFmpeg installed.

## Manual checks

1. Run **Tools → PDD → Capture speech bubbles** and inspect Screenshots/Review/SpeechBubbles.png for short and long line fit and contrast.
2. In play, press E at Sandy and Milo. Confirm the subtitle and head-following bubble show the same line as the NPC moves; confirm another player also sees it.
3. Trigger a rescue with several tourists. Listen from nearby and from farther down the beach for intelligibility and distance falloff. Check that calls do not form doubled voices, and that their visible captions match the words heard.
4. Trigger a reward and damage popup to confirm those still float without a bubble.

The first Unity capture exposed oversized text; this was corrected using measured glyph bounds, smaller lettering and centred alignment. The second capture was visually checked: short and long lines fit inside the rounded panel. Bubbles sit above the head and replace a previous line from the same speaker. Crowd overlap and the aesthetic quality of the synthetic voice still need a human listening/play pass; these recordings are an intelligible baseline, not acted panic performances.
