using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// The hands working the gun (holder's machine): reloading (the left hand pulls the magazine out, fetches a new one
    /// from below, pushes it in with a slap and racks the slide / charging handle; the shotgun takes its shells one by
    /// one and pumps), and the sniper's bolt worked by the right hand after each shot. How to Fish plays animations
    /// for these; ours are made here from waypoints on the gun, so they fit any gun. The hands follow through
    /// <see cref="Player.PlayerHands.GetGrip(Items.Item, out HandGrip, out HandGrip)"/>, so the first-person hands
    /// and the body both do it.
    /// </summary>
    public partial class Weapon
    {
        private enum Action { Magazine, Shells, Bolt }

        private Transform _magazinePart;   // where the magazine sits (the greybox part: its renderer may be off)
        private Transform _gunLeftGrip;
        private GameObject _prop;          // the magazine / shell in the hand
        private bool _propIsShell;
        private bool _racked;              // this reload's rack / pump / bolt has moved the part

        private Action ReloadAction => _sound == GunSound.Shotgun ? Action.Shells : Action.Magazine;

        /// <summary>Where the left and right hands are while the gun is being worked; false when they're just holding it.</summary>
        public bool HandOverride(bool right, out HandGrip grip)
        {
            grip = default;
            if (Local != this) return false;
            Transform t = transform;
            if (_magazinePart == null) _magazinePart = FindChild("Magazine");
            if (_gunLeftGrip == null) _gunLeftGrip = FindChild("GripLeft");

            // The sniper's bolt after a shot: the right hand leaves the grip, works the bolt and comes back.
            if (right && _cycleBlocks && _slide != null && Time.time < _cycleUntil)
            {
                float length = _interval * 0.85f;
                float u = Mathf.Clamp01(1f - (_cycleUntil - Time.time) / length);
                grip = Bolt(u, t);
                return true;
            }
            if (!_reloading)
            {
                ShowProp(false, null);
                if (_magazinePart != null) SetPartShown(_magazinePart, true);
                return false;
            }

            float p = ReloadProgress;
            if (right)
            {
                // The sniper racks its bolt with the right hand at the end of a reload.
                if (_cycleBlocks && _slide != null && p > 0.72f && p < 0.92f)
                {
                    grip = Bolt((p - 0.72f) / 0.2f, t);
                    return true;
                }
                return false;
            }
            grip = ReloadAction == Action.Shells ? LeftShells(p, t) : LeftMagazine(p, t);
            return true;
        }

        // ------------------------------------------------------------------ magazines

        private HandGrip LeftMagazine(float p, Transform t)
        {
            Vector3 up = t.up, fwd = t.forward, side = t.right;
            Vector3 mag = _magazinePart != null ? _magazinePart.position : t.position - up * 0.09f;
            HandGrip Hold(Vector3 at) => new(at - side * 0.035f, (fwd - up * 0.3f).normalized, side, HandPose.Cup);
            HandGrip atMag = Hold(mag);
            HandGrip pulled = Hold(mag - up * 0.14f);
            HandGrip away = Hold(mag - up * 0.38f - fwd * 0.08f - side * 0.06f);
            HandGrip home = LeftHome(t);
            bool pistol = _sound == GunSound.Pistol;
            // Rack: the pistol's slide from the top (palm down over it), a rifle's / SMG's charging handle.
            Vector3 rackAt = _slide != null ? _slide.position : t.position + up * 0.05f;
            rackAt += pistol ? -fwd * 0.07f + up * 0.03f : up * 0.02f;
            var rack = new HandGrip(rackAt - side * 0.01f, side, -up, HandPose.LooseFist);
            var rackPulled = new HandGrip(rackAt - fwd * 0.06f - side * 0.01f, side, -up, HandPose.LooseFist);
            bool rackWithLeft = !_cycleBlocks; // the sniper uses the bolt (right hand)

            // The magazine: out with the hand, gone below, a new one comes up and goes in.
            bool carrying = (p > 0.18f && p < 0.33f) || (p > 0.45f && p < 0.68f);
            if (_magazinePart != null) SetPartShown(_magazinePart, p < 0.18f || p >= 0.68f);
            if (p < 0.18f) return Blend(home, atMag, p / 0.18f, false);
            if (p < 0.33f) return Carry(Blend(atMag, pulled, (p - 0.18f) / 0.15f, false), carrying);
            if (p < 0.45f) return Carry(Blend(pulled, away, (p - 0.33f) / 0.12f, false), false);
            if (p < 0.6f) return Carry(Blend(away, pulled, (p - 0.45f) / 0.15f, false), carrying);
            if (p < 0.68f) return Carry(Blend(pulled, atMag, (p - 0.6f) / 0.08f, false), carrying);
            if (p < 0.74f)
            {
                // The slap.
                float s = Mathf.Sin((p - 0.68f) / 0.06f * Mathf.PI);
                return Hold(mag - up * (0.03f * (1f - s)));
            }
            if (!rackWithLeft) return Blend(atMag, home, (p - 0.74f) / 0.26f, false);
            if (p < 0.84f) return Blend(atMag, rack, (p - 0.74f) / 0.1f, false);
            if (p < 0.9f)
            {
                if (!_racked && p > 0.86f)
                {
                    _racked = true;
                    _slideKick = 1f;
                    PlayLocal(ProceduralAudio.Rack, 0.6f);
                }
                return Blend(rack, rackPulled, (p - 0.84f) / 0.06f, true);
            }
            return Blend(rackPulled, home, (p - 0.9f) / 0.1f, false);
        }

        // ------------------------------------------------------------------ shells (pump shotgun)

        private HandGrip LeftShells(float p, Transform t)
        {
            Vector3 up = t.up, fwd = t.forward, side = t.right;
            Vector3 port = t.position + fwd * 0.08f - up * 0.04f;
            var pocket = new HandGrip(port - up * 0.35f - fwd * 0.12f - side * 0.08f, fwd, up, HandPose.Cup);
            var atPort = new HandGrip(port - up * 0.03f, fwd, up, HandPose.Cup);
            var pushed = new HandGrip(port + up * 0.01f + fwd * 0.02f, fwd, up, HandPose.Cup);
            HandGrip home = LeftHome(t);
            var pumpBack = new HandGrip(home.Point - fwd * 0.08f, home.Fingers, home.Palm, home.Pose);

            // Two shells, one at a time (the tube holds two).
            if (p < 0.12f) return Blend(home, pocket, p / 0.12f, false);
            if (p < 0.26f) return Carry(Blend(pocket, atPort, (p - 0.12f) / 0.14f, false), true, shell: true);
            if (p < 0.32f) return Carry(Blend(atPort, pushed, (p - 0.26f) / 0.06f, false), p < 0.3f, shell: true);
            if (p < 0.44f) return Carry(Blend(pushed, pocket, (p - 0.32f) / 0.12f, false), false, shell: true);
            if (p < 0.58f) return Carry(Blend(pocket, atPort, (p - 0.44f) / 0.14f, false), true, shell: true);
            if (p < 0.64f) return Carry(Blend(atPort, pushed, (p - 0.58f) / 0.06f, false), p < 0.62f, shell: true);
            if (p < 0.76f) return Blend(pushed, home, (p - 0.64f) / 0.12f, false);
            // Pump: back and forward again.
            if (p < 0.92f)
            {
                if (!_racked && p > 0.78f)
                {
                    _racked = true;
                    _slideKick = 1f;
                    PlayLocal(ProceduralAudio.Rack, 0.6f);
                }
                float s = Mathf.Sin((p - 0.76f) / 0.16f * Mathf.PI);
                return Blend(home, pumpBack, s, true);
            }
            return home;
        }

        // ------------------------------------------------------------------ bolt (sniper, right hand)

        private HandGrip Bolt(float u, Transform t)
        {
            Vector3 up = t.up, fwd = t.forward, side = t.right;
            Transform g = FindChild("GripRight");
            var home = g != null ? new HandGrip(g.position, g.forward, -g.up, _item.GripPose) : new HandGrip(t.position, fwd, -side, HandPose.Fist);
            Vector3 knob = _slide.position + side * 0.045f;
            var onKnob = new HandGrip(knob + side * 0.015f, fwd, -side, HandPose.LooseFist);
            var lifted = new HandGrip(knob + up * 0.03f + side * 0.02f, fwd, -side, HandPose.LooseFist);
            var back = new HandGrip(knob + up * 0.03f - fwd * 0.09f + side * 0.02f, fwd, -side, HandPose.LooseFist);
            if (u < 0.2f) return Blend(home, onKnob, u / 0.2f, false);
            if (u < 0.32f) return Blend(onKnob, lifted, (u - 0.2f) / 0.12f, true);
            if (u < 0.5f)
            {
                if (u > 0.45f && _slideKick < 0.5f) _slideKick = 1f;
                return Blend(lifted, back, (u - 0.32f) / 0.18f, true);
            }
            if (u < 0.68f) return Blend(back, lifted, (u - 0.5f) / 0.18f, true);
            if (u < 0.78f) return Blend(lifted, onKnob, (u - 0.68f) / 0.1f, true);
            return Blend(onKnob, home, (u - 0.78f) / 0.22f, false);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The left hand's own place: on the fore-end, or (one-handed pistol) down out of view.</summary>
        private HandGrip LeftHome(Transform t)
        {
            if (_gunLeftGrip != null) return new HandGrip(_gunLeftGrip.position, _gunLeftGrip.forward, -_gunLeftGrip.up, _item.GripPose);
            return new HandGrip(t.position - t.up * 0.4f - t.right * 0.14f - t.forward * 0.1f, t.forward, t.right, HandPose.Relaxed);
        }

        private static HandGrip Blend(HandGrip a, HandGrip b, float u, bool sharp)
        {
            float k = sharp ? Mathf.Clamp01(u) : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
            return new HandGrip(Vector3.Lerp(a.Point, b.Point, k), Vector3.Slerp(a.Fingers, b.Fingers, k).normalized,
                Vector3.Slerp(a.Palm, b.Palm, k).normalized, HandPose.Lerp(a.Pose, b.Pose, k));
        }

        /// <summary>The magazine / shell rides in the hand (shown) or not.</summary>
        private HandGrip Carry(HandGrip hand, bool shown, bool shell = false)
        {
            ShowProp(shown, hand, shell);
            return hand;
        }

        private void ShowProp(bool shown, HandGrip? hand, bool shell = false)
        {
            if (!shown || hand is not { } h)
            {
                if (_prop != null) _prop.SetActive(false);
                if (!_reloading) _racked = false;
                return;
            }
            if (_prop == null || _propIsShell != shell)
            {
                if (_prop != null) Destroy(_prop);
                _propIsShell = shell;
                _prop = GameObject.CreatePrimitive(shell ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                Destroy(_prop.GetComponent<Collider>());
                _prop.name = shell ? "ShellInHand" : "MagazineInHand";
                Renderer source = _magazinePart != null ? _magazinePart.GetComponentInChildren<Renderer>(true) : null;
                Renderer r = _prop.GetComponent<Renderer>();
                if (shell) r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.75f, 0.12f, 0.08f) };
                else if (source != null) r.sharedMaterial = source.sharedMaterial;
                _prop.transform.localScale = shell ? new Vector3(0.022f, 0.035f, 0.022f)
                    : _magazinePart != null ? _magazinePart.lossyScale : new Vector3(0.03f, 0.12f, 0.05f);
            }
            _prop.SetActive(true);
            // In the palm: the magazine upright along the gun, the shell lying along the fingers.
            Vector3 palmOut = -h.Palm;
            Vector3 at = h.Point - palmOut * 0.035f + h.Fingers * 0.02f;
            _prop.transform.SetPositionAndRotation(at, shell ? Quaternion.LookRotation(Vector3.Cross(h.Fingers, h.Palm), h.Fingers)
                : _magazinePart != null ? _magazinePart.rotation : transform.rotation);
        }

        /// <summary>The gun's own magazine (built guns: a separate part) hides while it's out in the hand.</summary>
        private static void SetPartShown(Transform part, bool shown)
        {
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) r.forceRenderingOff = !shown;
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform c in GetComponentsInChildren<Transform>(true))
                if (c.name == childName) return c;
            return null;
        }
    }
}
