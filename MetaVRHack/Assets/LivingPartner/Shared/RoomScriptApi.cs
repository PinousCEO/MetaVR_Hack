using System;
using System.Collections.Generic;
using System.Globalization;
using MoonSharp.Interpreter;

namespace LivingPartner.Shared
{
    /// <summary>
    /// What a room script can call: the binding of RoomScript to Lua, the built-in poses by name, and Doc - the one text
    /// the host's writer puts into its prompt. (A test holds Doc and the functions that really exist together.)
    /// </summary>
    public static class RoomScriptApi
    {
        // ------------------------------------------------------------------ built-in poses
        /// <summary>The built-in poses pet_pose knows.</summary>
        public static readonly string[] Poses =
        {
            "idle", "walk", "run", "cheer", "wave", "think", "dance", "sit", "sleep", "study", "read", "work", "pick", "kick",
            "point", "bow", "listen", "talk", "startle", "hurt", "cower", "shiver", "down", "peek", "hide", "offer", "crouch", "ready", "wipe",
        };

        static string PoseName(string name)
        {
            switch (name)
            {
                case "happy": case "jump": case "joy": case "celebrate": return "cheer";
                case "sad": case "cry": return "down";
                case "nap": return "sleep";
                case "thanks": case "thank": case "thankful": return "bow";
                case "surprised": case "surprise": return "startle";
                case "scared": case "afraid": return "cower";
                case "cold": return "shiver";
                case "stand": case "none": return "idle";
                case "hello": case "hi": return "wave";
                default: return name;
            }
        }

        public static bool IsPose(string name) => Array.IndexOf(Poses, PoseName((name ?? "").ToLowerInvariant())) >= 0;

        /// <summary>
        /// A built-in pose by name. t = the clock the pose lives on (seconds), since = seconds since the pose was asked
        /// for (the ones that have a beginning - pick, kick - play from there).
        /// </summary>
        public static void Pose(string name, Pose p, float t, float since)
        {
            switch (PoseName((name ?? "").ToLowerInvariant()))
            {
                case "walk": PoseLib.Walk(p, since * 1.7f); break;
                case "run": PoseLib.Run(p, since * 2.4f); break;
                case "cheer": PoseLib.Happy(p, t); break;
                case "wave": PoseLib.Wave(p, t); break;
                case "think": PoseLib.Think(p, t); break;
                case "dance": PoseLib.Dance(p, t, since * 2f); break;
                case "sit": PoseLib.Sit(p, t); break;
                case "sleep": PoseLib.Sit(p, t); p[Bone.Head].X = 20f + 2f * M.Sin(t * 1.3f); p[Bone.Spine].X = 10f; p.Blink = 1f; p.Mouth = 0.15f; break;
                case "study": PoseLib.Study(p, t); p.HipsOffset.Y = -(Skeleton.HipsHeight - 0.065f); p[Bone.LegLL].X = 70f; p[Bone.LegLR].X = 70f; break;
                case "read": PoseLib.ReadBook(p, t, false); break;
                case "work": PoseLib.Work(p, t); break;
                case "pick": GlassPoses.Pick(p, t, M.Sin((float)Math.PI * M.Clamp01(since / 1.2f))); break;
                case "kick": if (since < 0.6f) PoseLib.Kick(p, since / 0.6f, t); else PoseLib.Idle(p, t); break;
                case "point": PoseLib.Point(p, t); break;
                case "bow": PoseLib.Thankful(p, t); break;
                case "listen": PoseLib.Listen(p, t); break;
                case "talk": PoseLib.Talk(p, t, 0.55f + 0.45f * M.Sin(t * 9f)); break;
                case "startle": PoseLib.Startle(p, t); break;
                case "hurt": GlassPoses.Hurt(p, t); break;
                case "cower": PoseLib.Cower(p, t); break;
                case "shiver": PoseLib.Shiver(p, t); break;
                case "down": GlassPoses.Down(p, t); break;
                case "peek": PoseLib.Peek(p, t); break;
                case "hide": PoseLib.CoverEyes(p, t); break;
                case "offer": GlassPoses.Offer(p, t, 0.5f); break;
                case "crouch": PoseLib.Crouch(p, 0.7f); break;
                case "ready": PoseLib.TennisReady(p, t); break;
                case "wipe": GlassPoses.Wipe(p, t); break;
                default: PoseLib.Idle(p, t); break;
            }
        }

        // ------------------------------------------------------------------ the tunes and the surroundings that come with the partner
        /// <summary>The id of a tune for the short name a script may give (club | lofi | lullaby); anything else is passed on as it is.</summary>
        public static string SongId(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "club": return "song-club";
                case "lofi": case "lo-fi": return "song-lofi";
                case "lullaby": return "song-lullaby";
                default: return (name ?? "").Trim();
            }
        }

        /// <summary>The id of the surroundings for the short name a script may give (club | rain | night ...); anything else is passed on as it is.</summary>
        public static string AmbienceId(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            switch (n)
            {
                case "club": case "rain": case "night": case "aurora": case "petals": case "snow": case "peak": case "coast": case "sunset": return "amb-" + n;
                case "stars": return "amb-night";
                case "spring": return "amb-petals";
                case "mountain": return "amb-peak";
                case "sea": case "beach": case "seaside": return "amb-coast";
                default: return (name ?? "").Trim();
            }
        }

        // ------------------------------------------------------------------ colours
        /// <summary>"#RRGGBB" for what a script gave as a colour (a name of the storybook palette, "#RRGGBB", 0xRRGGBB, {r, g, b}); "" when it is none.</summary>
        public static string Color(DynValue v)
        {
            int rgb = 0xFFFFFF;
            if (v == null || !LuaSandbox.TryColor(v, ref rgb)) return "";
            return "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ the functions
        /// <summary>Every function of the room a script can call (besides the common helpers of LuaSandbox and the motion functions of LuaMotion).</summary>
        public static readonly string[] Functions =
        {
            "offer", "wearable",
            "pet_goto", "pet_face", "pet_face_user", "pet_pose", "pet_play", "pet_sit", "pet_lie", "pet_free", "pet_say", "pet_hold", "pet_drop", "worn", "pet_at", "pet_busy",
            "me", "me_move", "me_turn", "me_spin", "me_bounce", "me_glow", "me_scale",
            "make", "put", "throw", "throw_to", "roll_to", "where", "hit", "remove",
            "hands", "user", "things", "near",
            "sparks", "sound", "label", "music", "music_stop", "ambience", "ambience_clear",
            "after", "every", "cancel", "time", "done",
        };

        static DynValue Num(double v) => DynValue.NewNumber(v);
        static readonly DynValue True = DynValue.True, False = DynValue.False;

        /// <summary>The first function among the arguments from i on (a callback may come after optional numbers), or null.</summary>
        static DynValue FnFrom(LuaArgs a, int i) { for (; i < a.Count; i++) { var f = a.Fn(i); if (f != null) return f; } return null; }

        internal static void Register(RoomScript rs, LuaSandbox lua)
        {
            // what the thing offers
            lua.Def("offer", a => { rs.Offer(a.Str(0), a.IsString(1) ? a.Str(1) : "", FnFrom(a, 1)); return null; });
            lua.Def("wearable", a => { rs.SetWearable(a.Str(0, "handR")); return null; });

            // the partner
            lua.Def("pet_goto", a => { rs.PetGoto(a.Num(0), a.Num(1), a.IsNumber(2) ? a.Num(2) : 0f, FnFrom(a, 2)); return null; });
            lua.Def("pet_face", a => { if (!a.Has(0)) rs.PetFace(true, 0f, 0f); else rs.PetFace(false, a.Num(0), a.Num(1)); return null; });
            lua.Def("pet_face_user", a => { rs.PetFace(true, 0f, 0f); return null; });
            lua.Def("pet_pose", a => { rs.PetPose(a.Str(0), a.Num(1)); return null; });
            lua.Def("pet_play", a => { rs.PetPlay(a.Str(0), FnFrom(a, 1)); return null; });
            lua.Def("pet_sit", a => { rs.PetPose(a.IsString(0) ? a.Str(0) : "sit", a.IsString(0) ? a.Num(1) : a.Num(0), "sit", ThingId(a)); return null; });
            lua.Def("pet_lie", a => { rs.PetPose(a.IsString(0) ? a.Str(0) : "sleep", a.IsString(0) ? a.Num(1) : a.Num(0), "lie", ThingId(a)); return null; });
            lua.Def("pet_free", a => { rs.PetFree(); return null; });
            lua.Def("pet_say", a => { rs.PetSay(a.Str(0, "", 80), a.Num(1)); return null; });
            lua.Def("pet_hold", a => { rs.PetHold(a.Str(0)); return null; });
            lua.Def("pet_drop", a => { rs.PetDrop(); return null; });
            lua.Def("worn", a => { return rs.WornOn.Length > 0 ? DynValue.NewString(rs.WornOn) : DynValue.Nil; });
            lua.Def("pet_at", a => { rs.PetAt(out float x, out float z); return DynValue.NewTuple(Num(x), Num(z)); });
            lua.Def("pet_busy", a => { return rs.PetIsBusy() ? True : False; });

            // the thing itself
            lua.Def("me", a => { rs.MePlace(out float x, out float y, out float z); return DynValue.NewTuple(Num(x), Num(y), Num(z), Num(rs.Me.yaw)); });
            lua.Def("me_move", a => { rs.MeMove(a.Num(0), a.Num(1), a.Num(2), a.IsNumber(2)); return null; });
            lua.Def("me_turn", a => { rs.MeTurn(a.Num(0)); return null; });
            lua.Def("me_spin", a => { rs.MeSpin(a.Num(0)); return null; });
            lua.Def("me_bounce", a => { rs.MeBounce(); return null; });
            lua.Def("me_glow", a =>
            {
                var v = a.Raw(0);
                bool off = v.Type == DataType.Nil || (v.Type == DataType.Boolean && !v.Boolean) || (v.Type == DataType.String && (v.String == "off" || v.String == "none" || v.String.Length == 0));
                rs.MeGlow(off ? "" : rs.ColorOf(v.Type == DataType.Boolean ? DynValue.NewString("yellow") : v, "me_glow"), a.Num(1));
                return null;
            });
            lua.Def("me_scale", a => { rs.MeScale(a.Num(0, 1f)); return null; });

            // its little objects (0 = the thing itself)
            lua.Def("make", a =>
            {
                // (forgiving: make("ball", "red") - the size left out)
                bool noSize = !a.IsNumber(1) && a.Has(1) && !a.Has(2);
                int id = rs.MakeObject(a.Str(0, "ball"), noSize ? 0f : a.Num(1), rs.ColorOf(a.Raw(noSize ? 1 : 2), "make"));
                return id > 0 ? Num(id) : DynValue.Nil;
            });
            lua.Def("put", a => { rs.Put(Id(a), a.Num(1), a.Num(2), a.Num(3)); return null; });
            lua.Def("throw", a => { rs.Throw(Id(a), a.Num(1), a.Num(2), a.Num(3)); return null; });
            lua.Def("throw_to", a => { rs.ThrowTo(Id(a), a.Num(1), a.Num(2), a.Num(3), a.Num(4)); return null; });
            lua.Def("roll_to", a => { rs.RollTo(Id(a), a.Num(1), a.Num(2)); return null; });
            lua.Def("where", a => { return rs.Where(Id(a), out float x, out float y, out float z) ? DynValue.NewTuple(Num(x), Num(y), Num(z)) : DynValue.Nil; });
            lua.Def("hit", a => { return rs.Hit(Id(a), a.Num(1), a.Num(2), a.Num(3), a.Num(4, 0.02f)) ? True : False; });
            lua.Def("remove", a => { if (a.Has(0)) rs.Remove(Id(a)); return null; });

            // the user
            lua.Def("hands", a =>
            {
                var list = lua.NewTable();
                int n = rs.Hands(out RoomHand[] hands);
                for (int i = 0; i < n; i++)
                {
                    var h = lua.NewTable();
                    h.Set("x", Num(hands[i].x)); h.Set("y", Num(hands[i].y)); h.Set("z", Num(hands[i].z)); h.Set("pinch", hands[i].pinch ? True : False);
                    list.Set(i + 1, DynValue.NewTable(h));
                }
                return DynValue.NewTable(list);
            });
            lua.Def("user", a => { rs.User(out float x, out float y, out float z); return DynValue.NewTuple(Num(x), Num(y), Num(z)); });

            lua.Def("things", a =>
            {
                var list = lua.NewTable(); int n = 0;
                foreach (var t in rs.Things()) list.Set(++n, Thing(lua, t));
                return DynValue.NewTable(list);
            });
            lua.Def("near", a =>
            {
                // near("table", 0.2) - or near(0.2): any thing
                var t = a.IsString(0) ? rs.Near(a.Str(0), a.Num(1)) : rs.Near("", a.Num(0));
                return t != null ? Thing(lua, t) : DynValue.Nil;
            });

            // effects
            lua.Def("music", a => { string w = a.Str(0); rs.Music(w == "stop" || w == "off" ? "" : w.Length > 0 ? w : "lofi"); return null; });
            lua.Def("music_stop", a => { rs.Music(""); return null; });
            lua.Def("ambience", a => { string w = a.Str(0); rs.Ambience(w == "clear" || w == "off" || w == "none" ? "" : w); return null; });
            lua.Def("ambience_clear", a => { rs.Ambience(""); return null; });
            lua.Def("sparks", a => { rs.Sparks(a.Num(0), a.Num(1), a.Num(2), a.Int(3), a.Has(4) ? rs.ColorOf(a.Raw(4), "sparks") : ""); return null; });
            lua.Def("sound", a => { rs.Sound(a.Str(0)); return null; });
            lua.Def("label", a => { rs.Label(a.Str(0, "", 40), a.Num(1), a.Num(2), a.Num(3), a.Num(4)); return null; });

            // time
            lua.Def("after", a => { int id = rs.After(a.Num(0), FnFrom(a, 0), false); return id > 0 ? Num(id) : DynValue.Nil; });
            lua.Def("every", a => { int id = rs.After(a.Num(0, 1f), FnFrom(a, 0), true); return id > 0 ? Num(id) : DynValue.Nil; });
            lua.Def("cancel", a => { if (a.Has(0)) rs.Cancel(a.Int(0)); return null; });
            lua.Def("time", a => { return Num(rs.Time); });
            lua.Def("done", a => { rs.Done(); return null; });
        }

        /// <summary>The id of the thing given among the arguments (one of things() / near(): a table with an id), "" when there is none.</summary>
        static string ThingId(LuaArgs a)
        {
            for (int i = 0; i < a.Count; i++)
            {
                var t = a.Tab(i);
                if (t == null) continue;
                var id = t.Get("id");
                if (id != null && id.Type == DataType.String) return id.String;
            }
            return "";
        }

        static DynValue Thing(LuaSandbox lua, RoomThingInfo t)
        {
            var o = lua.NewTable();
            o.Set("id", DynValue.NewString(t.id ?? "")); o.Set("name", DynValue.NewString(t.name ?? "")); o.Set("kind", DynValue.NewString(t.kind ?? ""));
            o.Set("x", Num(t.x)); o.Set("y", Num(t.y)); o.Set("z", Num(t.z)); o.Set("size", Num(t.size)); o.Set("seat", Num(t.seat)); o.Set("top", Num(t.top));
            var uses = lua.NewTable();
            if (t.uses != null) for (int i = 0; i < t.uses.Length; i++) uses.Set(i + 1, DynValue.NewString(t.uses[i] ?? ""));
            o.Set("uses", DynValue.NewTable(uses));
            return DynValue.NewTable(o);
        }

        /// <summary>An object id as a script gives it: a number; nil (a variable that was never set, or was cleared) means "none", not the thing itself.</summary>
        static int Id(LuaArgs a) => a.IsNumber(0) ? a.Int(0, -1) : -1;

        // ------------------------------------------------------------------ the text for the writer
        /// <summary>
        /// Everything a language model needs to write the script of a thing: the room, the hooks, the functions, the
        /// rules. (RoomScriptExamples has worked scripts to go with it.)
        /// </summary>
        public static readonly string Doc = DocRoom.Replace("\r", "") + "\n" + DocMotions() + "\n" + DocRules.Replace("\r", "");

        const string DocRoom =
@"ROOM SCRIPT - what a thing on the desk (or a skill of Nilo's) DOES, as a small Lua program. It lives as long as the
thing is in the room. Write the hooks you need and nothing else: 5 to 60 lines is normal.

THE ROOM
 Metres and seconds. x = to the user's right, z = AWAY from the user, y = up from the desk top (y = 0 is ON the desk).
 (0, 0, 0) is Nilo's home spot. The user sits at about z = -0.5; their screens stand at about z = +0.3.
 Nilo (the partner) is 0.10 m tall: its hands are at y = 0.04, the top of its head at y = 0.10. For scale: a bat is
 about 0.08 m long, a ball 0.02 m across, a drum 0.06 m. Nilo walks slowly: 0.12 m in a second.
 Nilo can be sent anywhere in x -0.40 .. 0.40, z -0.22 .. 0.25 (its part of the desk). Everything else has to stay
 within 1.2 m of the home spot. What is outside is moved to the nearest allowed place.
 Angles are degrees: yaw 0 looks away from the user (+z), 90 to the user's right, 180 at the user.
 Colours: ""#RRGGBB"" or cream ink brown pink honey mint sky lilac white black red orange yellow green blue grey.
 Things fall slowly (gravity 1.5 m/s2), bounce on the desk, roll out and stop.

HOOKS - global functions that the room calls. All are optional.
 on_make()        once, when the thing appears in the room
 on_use()         the user pressed the thing, or asked Nilo to use it (a skill: the user asked for it)
 on_stop()        the user said stop, or Nilo is needed elsewhere: end what is going on (the room then ends all timers
                  and frees Nilo by itself). Define it for anything that goes on until it is stopped.
 on_wear()        Nilo has just put the thing on / taken it in its hand (a thing that is wearable)
 on_unwear()      Nilo has just taken it off
 on_act(name)     the user asked for one of the acts the thing offers
 update(dt)       every frame WHILE the thing is doing something: from any hook above until you call done().
                  (An update that does nothing for 10 s is stopped as well.) A thing that only reacts needs no update.

FUNCTIONS
 What the thing offers - call these at the top of the script, outside any function:
  offer(name, label)          an act the user can ask for: a button with that label (English, 18 letters at most) appears
                              when the thing is pressed, and the user can ask for it by voice. on_act(name) is called. 4 at most.
  wearable(slot)              Nilo can wear / hold the thing. slot = ""handR"" | ""handL"" | ""head"" | ""back"". A ""Wear"" button appears.
 Nilo:
  pet_goto(x, z [, fn])       walks there; fn (optional) is called when it has arrived
  pet_face(x, z)              turns towards that point
  pet_face_user()             turns towards the user
  pet_pose(name [, seconds])  a built-in pose for that long (2 s when not given; 600 for ""until it is stopped""):
                              idle walk run cheer wave think dance sit sleep study read work pick kick point bow listen talk
                              startle hurt cower shiver down peek hide offer crouch ready wipe
  pet_play(name [, fn])       plays a motion you defined with motion(...) (see MOTIONS); fn (optional) is called when it is over
  pet_sit([pose] [, seconds] [, thing])   Nilo sits ON the thing, at its seat (pose: sit | study | read ..., ""sit"" when not
                              given; 20 s when not given). thing: another thing to sit on, as things() / near() gave it.
  pet_lie([seconds] [, thing])   Nilo lies down on top of the thing and sleeps (a bed)
                              (pet_pose / pet_play / pet_sit / pet_lie: what was asked LAST is what Nilo does.)
  pet_free()                  Nilo stops what the script asked of it, at once, and is its own again
  pet_say(text [, seconds])   a short line in a speech bubble (English)
  pet_hold([slot])            Nilo takes the thing: in its hand, on its head, on its back (where wearable() said, when not given)
  pet_drop()                  Nilo puts it down beside itself
  worn()                      -> the slot Nilo wears the thing on, or nil
  pet_at()                    -> x, z   where Nilo stands
  pet_busy()                  -> true while Nilo is still doing what the script asked (walking there, a pose, a motion)
 The thing itself:
  me()                        -> x, y, z, yaw
  me_move(x, z [, y])         it stands there now
  me_turn(yaw)
  me_spin(degPerSec)          it keeps turning (0 = stop)
  me_bounce()                 a little hop
  me_glow(color [, seconds])  it lights up in that colour (for good when no seconds are given); me_glow(""off"") puts it out
  me_scale(s)                 0.5 .. 2 times its own size
 Little objects of the thing's own (24 at most - remove the ones that are no longer needed):
  make(shape, size, color)    -> id.  shape = ""ball"" | ""box"" | ""disc"" | ""star"" | ""ring"", size in metres (0.005 .. 0.2)
  put(id, x, y, z)            puts it there, at rest: it stays there, also in the air
  throw(id, vx, vy, vz)       lets it fly at that speed (m/s): it falls, bounces, rolls out, stops. With vy = 0 on the desk it rolls.
  throw_to(id, x, y, z [, seconds])   throws it so that it arrives at that point after that many seconds
  roll_to(id, x, z)           rolls it along the desk so that it stops there
  where(id)                   -> x, y, z   (nil when there is no such object)
  hit(id, x, y, z, r)         -> true when the object is within r metres of that point right now
  remove(id)
  The id 0 is the thing itself: put(0, ...), throw(0, ...), throw_to(0, ...), roll_to(0, ...), where(0), hit(0, ...) work on it.
 The user and the room:
  hands()                     -> the fingertips that are seen (0, 1 or 2 of them): { {x=, y=, z=, pinch=}, ... }
  user()                      -> x, y, z of the user's head
  things()                    -> the OTHER things in the room: { {id=, name=, kind=, uses={...}, x=, y=, z=, size=, seat=, top=}, ... }
                              uses: sit table sleep read play music light decor eat; seat / top: how high its seat / top is (m)
  near(use [, r])             -> the nearest other thing that is for that use within r metres of this thing (0.3 when not given), or nil
 Effects:
  sparks(x, y, z [, n [, color]])     a burst of n sparks (8 when not given, 24 at most)
  sound(name)                 tap pop open close page hammer sparkle ta-da coin error portal-in portal-out kick glove spell whoosh land squeak step hit bounce win lose drop tick
  label(text, x, y, z [, seconds])    a small paper label floating there (2 s when not given; 4 at a time)
  music(name)                 puts a record on: club | lofi | lullaby, or the id / name of a tune the user owns
  music_stop()
  ambience(name)              changes the surroundings round the desk: club | rain | night | aurora | petals | snow | peak | coast | sunset,
                              or the id / name of a place the user owns
  ambience_clear()            the room as it is
 Time:
  after(seconds, fn)          calls fn once, later.  -> id
  every(seconds, fn)          calls fn again and again.  -> id   (8 timers at a time)
  cancel(id)                  stops a timer
  time()                      -> seconds since the script began
  done()                      the thing has finished what it was doing: update() is not called any more, every() timers end,
                              and when what is still running (after() timers, a pose, a motion, flying objects) is over, Nilo is free again.
 Helpers:";

        const string DocRules =
@"RULES
 - on_use / on_act / on_wear must do something that is SEEN or HEARD at once (Nilo moves or speaks, a sound, sparks ...).
 - Nothing may go on for ever by itself: call done() when an act is over and cancel() what you started with every().
   What is MEANT to go on until the user stops it (sitting, dancing, a tune) needs on_stop().
 - Remove the objects you made when they are no longer needed.
 - Only the functions listed here exist. An error switches the script off, and the thing is then an ordinary thing.
 - Keep variables at the top of the script (local ball = nil); the script lives as long as the thing is in the room.
 - hit() and hands() are only worth asking in update(): a thing that waits for a hand starts in a hook and says done()
   when it stops waiting.";

        static string DocMotions()
        {
            // (the helpers every script has, and the motion API - S2's texts, word for word)
            return "  " + LuaSandbox.CommonDoc.Replace("\r", "").Replace("\n", "\n  ") + "\n\n" + LuaMotion.Doc.Replace("\r", "");
        }
    }
}
