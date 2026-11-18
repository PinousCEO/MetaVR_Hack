using System;

namespace LivingPartner.Shared
{
    /// <summary>
    /// Pure portal-transition state machine (authoritative copy lives in the Companion Host).
    /// Idle -> TargetSelected -> Approaching -> ReceiverReady -> Crossing -> Settling -> Complete,
    /// with Hold / Cancel / Recover failure paths. Every event is idempotent: duplicates and events for an
    /// old transitionId never change state, so repeated complete/cancel messages cannot corrupt it.
    /// </summary>
    public sealed class TransitionMachine
    {
        public TransitionInfo Info { get; private set; } = new TransitionInfo();

        public TState State => Parse(Info.state);
        public static TState Parse(string s) => Enum.TryParse<TState>(s, out var v) ? v : TState.Idle;
        public bool IsActive { get { var s = State; return s != TState.Idle && s != TState.Complete && s != TState.Cancelled; } }

        /// <summary>The environment(s) allowed to draw the character right now.</summary>
        public static void Visibility(TransitionInfo t, out bool mrDraws, out bool pcDraws, out bool clipped)
        {
            var s = Parse(t.state);
            bool crossing = s == TState.Crossing || s == TState.Recovering || (s == TState.Hold && Parse(t.heldState) == TState.Crossing);
            // During the crossing both sides draw complementary parts; Settling keeps the clip so the
            // destination shows the whole body and the source shows nothing even before the owner flips.
            if (crossing || s == TState.Settling) { mrDraws = true; pcDraws = true; clipped = true; return; }
            clipped = false;
            mrDraws = t.owner == Env.Mr; pcDraws = t.owner == Env.Pc;
        }

        public void Restore(TransitionInfo info) { Info = info ?? new TransitionInfo(); }

        /// <summary>Starts a new transition. Fails (returns false) while another one is active.</summary>
        public bool Begin(string transitionId, string characterId, int direction, string currentOwner, TargetInfo target, double now)
        {
            if (IsActive) return false;
            string src = direction == 0 ? Env.Mr : Env.Pc, dst = direction == 0 ? Env.Pc : Env.Mr;
            if (currentOwner != src) return false; // the character is not on the side it is asked to leave
            Info = new TransitionInfo
            {
                transitionId = transitionId, characterId = characterId, direction = direction,
                state = nameof(TState.TargetSelected), owner = currentOwner, source = src, destination = dst,
                target = target, startedAt = now, updatedAt = now, revision = 1,
            };
            return true;
        }

        /// <summary>Applies an event. Returns true only when the state actually changed.</summary>
        public bool Apply(TEvent ev, string transitionId, double now, string reason = "")
        {
            var t = Info;
            if (string.IsNullOrEmpty(transitionId) || transitionId != t.transitionId) return false;
            var s = State;
            bool changed = false;
            switch (ev)
            {
                case TEvent.BeginApproach:
                    if (s == TState.TargetSelected) { Set(TState.Approaching); changed = true; }
                    break;
                case TEvent.ApproachDone:
                    if ((s == TState.Approaching || s == TState.TargetSelected) && !t.approachDone) { t.approachDone = true; changed = true; Promote(); }
                    break;
                case TEvent.ReceiverReady:
                    if ((s == TState.Approaching || s == TState.TargetSelected) && !t.receiverReady) { t.receiverReady = true; changed = true; Promote(); }
                    break;
                case TEvent.StartCrossing:
                    if (s == TState.ReceiverReady) { Set(TState.Crossing); changed = true; }
                    break;
                case TEvent.CrossingFinished:
                    if (s == TState.Crossing) { Set(TState.Settling); t.owner = t.destination; changed = true; }
                    break;
                case TEvent.Settled:
                    if (s == TState.Settling) { Set(TState.Complete); changed = true; }
                    break;
                case TEvent.Hold:
                    if (s == TState.Approaching || s == TState.ReceiverReady || s == TState.Crossing || s == TState.TargetSelected)
                    { t.heldState = t.state; Set(TState.Hold); changed = true; }
                    break;
                case TEvent.Resume:
                    if (s == TState.Hold) { var back = Parse(t.heldState); t.heldState = ""; Set(back); changed = true; }
                    break;
                case TEvent.Cancel:
                    if (s == TState.TargetSelected || s == TState.Approaching || s == TState.ReceiverReady)
                    { Set(TState.Cancelled); t.owner = t.source; changed = true; }
                    else if (s == TState.Hold)
                    {
                        if (Parse(t.heldState) == TState.Crossing) { t.heldState = ""; Set(TState.Recovering); }
                        else { t.heldState = ""; Set(TState.Cancelled); t.owner = t.source; }
                        changed = true;
                    }
                    else if (s == TState.Crossing) { Set(TState.Recovering); changed = true; }
                    // Settling / Complete / Cancelled / Recovering: too late or already done -> ignored (idempotent)
                    break;
                case TEvent.RecoverDone:
                    if (s == TState.Recovering) { Set(TState.Cancelled); t.owner = t.source; changed = true; }
                    break;
            }
            if (changed) { t.updatedAt = now; t.revision++; if (!string.IsNullOrEmpty(reason)) t.reason = reason; }
            return changed;
        }

        void Promote()
        {
            if (Info.approachDone && Info.receiverReady) Set(TState.ReceiverReady);
            else if (State == TState.TargetSelected) Set(TState.Approaching);
        }

        void Set(TState s) { Info.state = s.ToString(); }
    }
}
