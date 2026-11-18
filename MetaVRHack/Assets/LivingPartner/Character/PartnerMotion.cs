using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;

namespace LivingPartner.Character
{
    public enum Emote { Idle, Think, Work, Wave, Happy, Shiver, Cower, Sit, Balance, Startle, Thankful, Point, Listen, Talk }

    /// <summary>Cross-fades between the shared procedural poses. Deterministic given (emote history, time).</summary>
    public sealed class PoseMixer
    {
        readonly Pose _cur = new Pose(), _prev = new Pose(), _tmp = new Pose();
        Emote _emote = Emote.Idle, _prevEmote = Emote.Idle;
        float _fade = 1f;
        public Emote Current => _emote;
        public float Wobble = 1f;
        /// <summary>How loud the partner's voice is right now (0..1), for the talking pose.</summary>
        public float Voice;

        public void Set(Emote e) { if (e == _emote) return; _prevEmote = _emote; _emote = e; _fade = 0f; }

        static void Eval(Emote e, float t, Pose p, float wobble, float voice)
        {
            switch (e)
            {
                case Emote.Think: PoseLib.Think(p, t); break;
                case Emote.Work: PoseLib.Work(p, t); break;
                case Emote.Wave: PoseLib.Wave(p, t); break;
                case Emote.Happy: PoseLib.Happy(p, t); break;
                case Emote.Shiver: PoseLib.Shiver(p, t); break;
                case Emote.Cower: PoseLib.Cower(p, t); break;
                case Emote.Sit: PoseLib.Sit(p, t); break;
                case Emote.Balance: PoseLib.Balance(p, t, wobble); break;
                case Emote.Startle: PoseLib.Startle(p, t); break;
                case Emote.Thankful: PoseLib.Thankful(p, t); break;
                case Emote.Point: PoseLib.Point(p, t); break;
                case Emote.Listen: PoseLib.Listen(p, t); break;
                case Emote.Talk: PoseLib.Talk(p, t, voice); break;
                default: PoseLib.Idle(p, t); break;
            }
        }

        public void Evaluate(float time, float dt, Pose outPose)
        {
            _fade = Mathf.Min(1f, _fade + dt / 0.28f);
            Eval(_emote, time, _cur, Wobble, Voice);
            if (_fade < 1f) { Eval(_prevEmote, time, _prev, Wobble, Voice); Pose.Lerp(_prev, _cur, M.Smooth(_fade), outPose); }
            else outPose.CopyFrom(_cur);
        }
    }

    /// <summary>
    /// Ground locomotion on a plane (local 2D coordinates: x = right, y = forward). Gait phase is driven by the distance
    /// actually travelled (distance / stride length), so the stance foot does not slide, and the root never teleports:
    /// it only moves by speed * dt.
    /// </summary>
    public sealed class PartnerMotor
    {
        public Vector2 Pos;
        public float Yaw;                 // degrees, 0 = +forward
        public bool Run;
        public float Speed { get; private set; }
        public bool HasTarget { get; private set; }
        public bool Arrived => !HasTarget && Speed < 1e-4f && !_turning;

        Vector2 _target; float _phase; bool _hasFinalYaw; float _finalYaw; bool _turning;
        float _prevYaw, _turnPhase, _turnLift; bool _yawSeen;
        readonly Pose _gait = new Pose(), _run = new Pose();
        public float LastStepDistance { get; private set; }

        public void Teleport(Vector2 pos, float yaw) { Pos = pos; Yaw = yaw; HasTarget = false; Speed = 0; _turning = false; _prevYaw = yaw; }

        public void MoveTo(Vector2 target, bool run, float? finalYaw = null)
        {
            _target = target; Run = run; HasTarget = true;
            _hasFinalYaw = finalYaw.HasValue; _finalYaw = finalYaw ?? 0f;
        }

        public void Face(float yaw) { _hasFinalYaw = true; _finalYaw = yaw; _turning = true; }
        public void Stop() { HasTarget = false; }

        // steps per second: the legs are very short, so the partner patters along quickly
        public static float WalkSpeed(float H) => PoseLib.StrideLength(PoseLib.WalkAmp) * H * 4.6f;
        public static float RunSpeed(float H) => PoseLib.StrideLength(PoseLib.RunAmp) * H * 6.0f;

        /// <summary>Advances the motor and blends the gait over the supplied base pose (already evaluated by the mixer).</summary>
        public void Update(float dt, float H, Pose pose)
        {
            LastStepDistance = 0;
            float vmax = Run ? RunSpeed(H) : WalkSpeed(H);
            float targetSpeed = 0f;
            if (HasTarget)
            {
                Vector2 d = _target - Pos; float dist = d.magnitude;
                if (dist < 0.0004f) { Pos = _target; HasTarget = false; _turning = _hasFinalYaw; }   // arrive exactly: later motion starts from this point
                else
                {
                    float want = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    float dy = Mathf.DeltaAngle(Yaw, want);
                    Yaw = Mathf.MoveTowardsAngle(Yaw, want, 540f * dt);
                    float align = Mathf.Clamp01(1f - Mathf.Abs(dy) / 70f);           // do not walk sideways: turn first
                    float brake = Mathf.Clamp01(dist / (H * 0.9f)) * 0.85f + 0.15f;   // ease into the stop
                    targetSpeed = vmax * align * brake;
                }
            }
            else if (_turning)
            {
                // a turn on the spot eases in and out and is slow enough for the feet to step it
                float left = Mathf.Abs(Mathf.DeltaAngle(Yaw, _finalYaw));
                Yaw = Mathf.MoveTowardsAngle(Yaw, _finalYaw, Mathf.Lerp(110f, 330f, Mathf.Clamp01(left / 70f)) * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(Yaw, _finalYaw)) < 0.5f) { Yaw = _finalYaw; _turning = false; }
            }
            Speed = Mathf.MoveTowards(Speed, targetSpeed, vmax * 4.5f * dt);
            // turning without walking: small steps on the spot, one for every 55 degrees
            if (!_yawSeen) { _yawSeen = true; _prevYaw = Yaw; }
            float turned = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, Yaw)); _prevYaw = Yaw;
            bool stepping = turned > 0.02f && Speed < vmax * 0.25f;
            _turnLift = Mathf.MoveTowards(_turnLift, stepping ? 1f : 0f, dt * (stepping ? 9f : 5f));
            if (turned < 60f) _turnPhase += turned / 110f;
            if (_turnLift > 0.01f) PoseLib.StepInPlace(pose, _turnPhase, _turnLift * Mathf.Clamp01(1f - Speed / (vmax * 0.25f)));
            if (Speed > 1e-5f)
            {
                float step = Speed * dt;
                if (HasTarget) step = Mathf.Min(step, (_target - Pos).magnitude);
                var fwd = new Vector2(Mathf.Sin(Yaw * Mathf.Deg2Rad), Mathf.Cos(Yaw * Mathf.Deg2Rad));
                Pos += fwd * step;
                LastStepDistance = step;
                float amp = Run ? PoseLib.RunAmp : PoseLib.WalkAmp;
                _phase += step / (PoseLib.StrideLength(amp) * H);
                if (Run) PoseLib.Run(_gait, _phase); else PoseLib.Walk(_gait, _phase);
                float w = Mathf.Clamp01(Speed / (WalkSpeed(H) * 0.35f));
                Pose.Lerp(pose, _gait, w, pose);
            }
        }
    }

    /// <summary>How the conversation state (CharacterInfo.talk) shows on the partner, the same on both sides.</summary>
    public static class TalkLook
    {
        public static bool Active(string talk) => !string.IsNullOrEmpty(talk);
        public static Emote Emote(string talk) => talk == "listening" ? Character.Emote.Listen : talk == "speaking" ? Character.Emote.Talk : Character.Emote.Think;
        public static Color Color(string talk) => talk == "listening" ? new Color(1f, 0.45f, 0.55f) : talk == "speaking" ? new Color(1f, 0.78f, 0.30f) : new Color(0.72f, 0.58f, 1f);
        public static string Label(string talk) => talk == "listening" ? "Listening" : talk == "thinking" ? "Thinking" : "";
    }

    /// <summary>Common colours for the partner's status light (the clasp at its neck + the workspace status icon).</summary>
    public static class StatusColors
    {
        /// <summary>1 while the activity is one that is shown (the status light is lit), 0 when idle.</summary>
        public static float Lit(string activity) => Label(activity).Length > 0 ? 1f : 0f;

        public static Color For(string activity)
        {
            switch (activity)
            {
                case "analysis": return new Color(0.35f, 0.75f, 1f);
                case "modify": return new Color(1f, 0.72f, 0.25f);
                case "test": return new Color(0.75f, 0.55f, 1f);
                case "done": return new Color(0.4f, 1f, 0.55f);
                case "error": return new Color(1f, 0.35f, 0.35f);
                case "approval": return new Color(1f, 0.92f, 0.35f);
                default: return new Color(0.3f, 0.9f, 1f);
            }
        }
        public static string Label(string activity)
        {
            switch (activity)
            {
                case "analysis": return "Analyzing";
                case "modify": return "Editing";
                case "test": return "Testing";
                case "done": return "Done";
                case "error": return "Error";
                case "approval": return "Needs approval";
                default: return "";
            }
        }
    }
}
