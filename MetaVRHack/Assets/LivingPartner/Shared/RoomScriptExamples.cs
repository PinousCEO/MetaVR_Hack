using System;
using System.Collections.Generic;
using System.Text;

namespace LivingPartner.Shared
{
    // (This file is generated from the .lua files the scripts were written and tried as - artifacts/tmp/s3/gen.py. The scripts
    // are the few-shot material for the host's writer, the seeds of the things and skills that come with the partner, and
    // the test cases of the runtime: every one of them passes RoomScriptCheck without a warning.)

    /// <summary>
    /// Worked room scripts. Made: what the user asked for and the script that would have been written for it (the
    /// examples for the writer's prompt). Shipped: the behaviour of the built-in kinds of thing (ItemKinds) as scripts -
    /// what "use" does with a chair, a bed, a book ... Skills: the skills that come with the partner (Dance).
    /// </summary>
    public static class RoomScriptExamples
    {
        public sealed class Example
        {
            public string id = "";              // Made: a short name; Shipped: the ItemKinds id; Skills: the skill id
            public string request = "";         // what the user said (Korean)
            public string requestEn = "";       // the same in English
            public string name = "";            // the thing's label
            public string kind = "box";         // ItemKinds id
            public string[] uses = new string[0];
            public float sizeM = 0.08f;
            public bool skill;                  // a skill, not a thing
            public string script = "";
        }

        public const string Bat =
@"-- Baseball bat. Bambi holds it in its right hand; ""Swing!"" pitches a ball at it and Bambi hits it away.
wearable(""handR"")
offer(""swing"", ""Swing!"")

-- the swing: wound up behind the right shoulder, through the ball (u = 0.5), round to the left
motion(""swing"", 0.7, function(u, p)
  local back = keys(u, {0, 0, 0.30, 1, 0.40, 1, 0.55, 0})   -- wound up
  local thru = keys(u, {0.38, 0, 0.55, 1, 0.80, 1, 1, 0})   -- through the ball and across
  local twist = 38 * back - 44 * thru                       -- + turns the body to its own right
  p.spine = {x = 10, y = twist * 0.4}
  p.chest = {y = twist * 0.6}
  p.head  = {y = -twist * 0.8}                              -- the eyes stay on the ball
  p.armR  = {x = 40 * back - 90 * thru, z = 10 + 40 * back - 20 * thru}
  p.foreR = {x = -12 - 10 * thru}
  p.armL  = {x = -30 + 15 * back - 50 * thru, z = -15 * back - 20 * thru}
  p.foreL = {x = -30}
  p.legL  = {x = -14 * thru}                                -- a small step into it
  p.squash = 0.10 * back - 0.18 * thru
end)

local ball = nil
local swinging = false

function on_wear()
  pet_face_user()
  pet_say(""Batter up!"")
  pet_pose(""cheer"", 1.2)
  sound(""ta-da"")
end

function on_unwear()
  pet_say(""Good game!"")
end

function on_act(name)
  if not worn() then pet_hold(""handR"") end
  local px, pz = pet_at()
  pet_face_user()
  if ball then remove(ball) end
  ball = make(""ball"", 0.02, ""white"")
  put(ball, px + 0.02, 0.07, pz - 0.45)               -- the pitch comes from the user's side
  throw_to(ball, px + 0.02, 0.045, pz - 0.05, 0.9)    -- to just in front of Bambi, in 0.9 s
  sound(""whoosh"")
  swinging = false
  after(0.55, function() pet_play(""swing""); swinging = true end)   -- so that the bat is there when the ball is
  after(3.2, function()                               -- hit or missed: tidy up
    if ball then remove(ball); ball = nil end
    done()
  end)
end

function update(dt)
  if not (ball and swinging) then return end
  local px, pz = pet_at()
  if hit(ball, px + 0.02, 0.045, pz - 0.05, 0.02) then
    swinging = false
    throw(ball, rnd(-0.25, 0.25), 0.55, -0.9)         -- away, high over the user's side of the desk
    sound(""hit"")
    sparks(px + 0.02, 0.045, pz - 0.05, 12, ""yellow"")
    pet_say(""Home run!"")
    after(0.45, function() pet_pose(""cheer"", 1.5) end)    -- when the swing is over
  end
end
";

        public const string Wand =
@"-- Magic wand. Bambi holds it; ""Wave it"" swishes it: stars come out of its tip and one big star floats up and pops.
wearable(""handR"")
offer(""wave"", ""Wave it"")

motion(""swish"", 1.3, function(u, p)
  local up = keys(u, {0, 0, 0.2, 1, 0.8, 1, 1, 0})      -- the arm goes up and out, and comes back
  local s = wave(u, 3) * up                             -- three swishes
  p.armR  = {x = -45 * up, z = 20 + 30 * up + 16 * s}
  p.foreR = {x = -20 * up, z = 22 * s}
  p.head  = {x = -8 * up, z = 6 * s}
  p.spine = {z = -4 * s}
  p.mouth = 0.6 * up
end)

local star, stars, y = nil, nil, 0

function on_wear()
  local px, pz = pet_at()
  pet_face_user()
  pet_say(""Abracadabra!"")
  sound(""sparkle"")
  sparks(px, 0.10, pz, 10, ""lilac"")
end

function on_act(name)
  if not worn() then pet_hold(""handR"") end
  local px, pz = pet_at()
  pet_face_user()
  pet_play(""swish"")
  sound(""sparkle"")
  if star then remove(star) end
  if stars then cancel(stars) end
  star = make(""star"", 0.03, ""yellow"")
  y = 0.08
  put(star, px + 0.05, y, pz - 0.03)
  stars = every(0.25, function()                        -- little stars from the tip of the wand while it swishes
    sparks(px + 0.05 + rnd(-0.02, 0.02), 0.08 + rnd(0, 0.03), pz - 0.02, 6, rnd() < 0.5 and ""yellow"" or ""pink"")
  end)
  after(1.3, function() cancel(stars); stars = nil end)
  after(2.4, function()                                 -- the big star pops
    local sx, sy, sz = where(star)
    sparks(sx, sy, sz, 16, ""yellow"")
    sound(""pop"")
    remove(star); star = nil
    done()
  end)
end

function update(dt)                                     -- the big star floats up, swaying
  if not star then return end
  local px, pz = pet_at()
  y = y + 0.04 * dt
  put(star, px + 0.05 + 0.015 * math.sin(time() * 4), y, pz - 0.03)
end
";

        public const string Drum =
@"-- Drum. Press it: Bambi walks over and plays a little rhythm on it.
motion(""drumming"", 0.6, function(u, p)       -- the hands in turn: the right one is down at u = 0, the left one at u = 0.5
  local r, l = pulse(u), pulse(u + 0.5)      -- 1 = that hand is up
  p.armR  = {x = -55 - 40 * r, z = 5}
  p.foreR = {x = -30 - 25 * r}
  p.armL  = {x = -55 - 40 * l, z = 5}
  p.foreL = {x = -30 - 25 * l}
  p.spine = {x = 14, z = 3 * wave(u)}
  p.head  = {x = 6 + 4 * pulse(u, 2)}
end, true)

local beat = nil

function on_use()
  if beat then return end                    -- it is playing already
  local x, y, z = me()
  pet_goto(x - 0.07, z, function()           -- beside the drum
    pet_face(x, z)
    pet_play(""drumming"")
    local n = 0
    beat = every(0.3, function()             -- one blow every 0.3 s, as the motion has them
      n = n + 1
      sound(n % 4 == 0 and ""land"" or ""hammer"")
      me_bounce()
      if n % 4 == 0 then sparks(x, y + 0.06, z, 5, ""orange"") end
      if n >= 16 then
        cancel(beat); beat = nil
        pet_face_user()
        pet_pose(""cheer"", 1.5)
        sound(""ta-da"")
        done()
      end
    end)
  end)
end

function on_stop()
  beat = nil
end
";

        public const string Football =
@"-- Football. Press it: Bambi and you pass it to and fro - touch the ball with a fingertip to send it back.
local playing, to_user, kicks = false, false, 0
local home_x, home_z = 0, 0

local function pass()                          -- Bambi kicks it over to the user's hand
  local hx, hz = 0.10, -0.30
  local h = hands()[1]
  if h then hx, hz = clamp(h.x, -0.4, 0.4), clamp(h.z, -0.45, -0.15) end
  pet_pose(""kick"", 0.6)
  after(0.3, function() roll_to(0, hx, hz); sound(""hit""); to_user = true end)
end

function on_use()
  if playing then on_stop() return end
  playing, to_user, kicks = true, false, 0
  local x, y, z = me()
  home_x, home_z = x, z
  pet_say(""Pass it back!"")
  pet_goto(x, z + 0.06, function() pet_face_user(); pass() end)   -- behind the ball, facing the user
end

function update(dt)
  if not playing then return end
  local bx, by, bz = where(0)
  local px, pz = pet_at()
  if to_user then
    for _, h in ipairs(hands()) do
      if hit(0, h.x, h.y, h.z, 0.02) then      -- a fingertip touched it: back to Bambi
        to_user = false
        kicks = kicks + 1
        roll_to(0, px, pz - 0.06)
        sound(""bounce"")
        sparks(bx, 0.03, bz, 6)
        return
      end
    end
  elseif dist(bx, bz, px, pz - 0.06) < 0.03 and not pet_busy() then   -- it has come back
    if kicks >= 5 then pet_say(""Good game!""); on_stop() else pass() end
  end
end

function on_stop()
  playing, to_user = false, false
  roll_to(0, home_x, home_z)
  pet_pose(""cheer"", 1.2)
  done()
end
";

        public const string Lamp =
@"-- Desk lamp. Press it: the light goes on; press again: off.
local lit = false

function on_use()
  local x, y, z = me()
  lit = not lit
  sound(""tap"")
  pet_face(x, z)
  if lit then
    me_glow(""#FFE08A"")
    sparks(x, y + 0.10, z, 6, ""yellow"")
    pet_pose(""cheer"", 1.2)
  else
    me_glow(""off"")
    pet_pose(""sleep"", 1.5)
  end
end
";

        /// <summary>The built-in kind "chair".</summary>
        public const string Chair =
@"-- Chair. Bambi sits on it - with a table beside it, it sits down to study. Press again and it gets up.
function on_use()
  if pet_busy() then on_stop() return end
  local x, y, z = me()
  pet_goto(x, z, function()
    local desk = near(""table"", 0.2)
    if desk then
      pet_face(desk.x, desk.z)
      pet_sit(""study"", 600)
    else
      pet_face_user()
      pet_sit(""sit"", 600)
    end
    sound(""land"")
  end)
end

function on_stop()
  pet_free()
  done()
end
";

        /// <summary>The built-in kind "desk".</summary>
        public const string Desk =
@"-- Desk. Bambi works at it: on the chair beside it when there is one, otherwise standing at it.
function on_use()
  if pet_busy() then on_stop() return end
  local x, y, z = me()
  local chair = near(""sit"", 0.2)
  if chair then
    pet_goto(chair.x, chair.z, function()
      pet_face(x, z)
      pet_sit(""study"", 600, chair)
    end)
  else
    pet_goto(x, z - 0.09, function()
      pet_face(x, z)
      pet_pose(""work"", 600)
    end)
  end
end

function on_stop()
  pet_free()
  done()
end
";

        /// <summary>The built-in kind "bed".</summary>
        public const string Bed =
@"-- Bed. Bambi lies down on it and sleeps. Press again to wake it.
function on_use()
  if pet_busy() then on_stop() return end
  local x, y, z = me()
  pet_goto(x, z, function()
    pet_lie(600)
    sound(""land"")
  end)
end

function on_stop()
  pet_free()
  pet_say(""Good morning!"")
  done()
end
";

        /// <summary>The built-in kind "sofa".</summary>
        public const string Sofa =
@"-- Sofa. Bambi sits down on it and looks at you. Press again and it gets up.
function on_use()
  if pet_busy() then on_stop() return end
  local x, y, z = me()
  pet_goto(x, z, function()
    pet_face_user()
    pet_sit(""sit"", 600)
    sound(""land"")
  end)
end

function on_stop()
  pet_free()
  done()
end
";

        /// <summary>The built-in kind "book".</summary>
        public const string Book =
@"-- Book. Bambi picks it up and reads - on a seat when one is near. Press again and it puts the book back.
local home_x, home_z = 0, 0
local reading = false

function on_use()
  if reading then on_stop() return end
  reading = true
  local x, y, z = me()
  home_x, home_z = x, z
  pet_goto(x - 0.05, z, function()
    pet_pose(""pick"", 1.0)
    after(0.6, function()
      pet_hold(""handL"")
      sound(""page"")
      local seat = near(""sit"", 0.3)
      if seat then
        pet_goto(seat.x, seat.z, function() pet_face_user(); pet_sit(""read"", 600, seat) end)
      else
        pet_face_user()
        pet_pose(""read"", 600)
      end
    end)
  end)
end

function on_stop()
  reading = false
  pet_free()
  if worn() then pet_drop(); me_move(home_x, home_z); sound(""page"") end
  done()
end
";

        /// <summary>The built-in kind "ball".</summary>
        public const string Ball =
@"-- Ball. Bambi kicks it along the desk and runs after it, four times.
local n = 0

local function kick()
  local x, y, z = where(0)
  local dir = (n % 2 == 0) and 1 or -1            -- to the right, then back to the left
  pet_goto(clamp(x - dir * 0.05, -0.4, 0.4), z, function()
    pet_face(x, z)
    pet_pose(""kick"", 0.6)
    after(0.3, function()
      n = n + 1
      sound(""hit"")
      roll_to(0, clamp(x + dir * 0.18, -0.38, 0.38), z)
      if n < 4 then
        after(1.6, kick)
      else
        after(1.4, function() pet_face_user(); pet_pose(""cheer"", 1.2); done() end)
      end
    end)
  end)
end

function on_use()
  if pet_busy() then return end
  n = 0
  kick()
end
";

        /// <summary>The built-in kind "lamp".</summary>
        public const string KindLamp =
@"-- Lamp. Press it: the light goes on; press again: off.
local lit = false

function on_use()
  local x, y, z = me()
  lit = not lit
  sound(""tap"")
  pet_face(x, z)
  if lit then
    me_glow(""#FFE08A"")
    sparks(x, y + 0.10, z, 6, ""yellow"")
    pet_pose(""cheer"", 1.2)
  else
    me_glow(""off"")
    pet_pose(""sleep"", 1.5)
  end
end
";

        /// <summary>The built-in kind "plant".</summary>
        public const string Plant =
@"-- Plant. Bambi waters it: it shakes its leaves and grows a little.
local size = 1

function on_use()
  local x, y, z = me()
  pet_goto(clamp(x - 0.07, -0.4, 0.4), z, function()
    pet_face(x, z)
    pet_pose(""offer"", 1.6)
    sound(""drop"")
    after(0.8, function()
      size = math.min(size + 0.05, 1.3)
      me_scale(size)
      me_bounce()
      sparks(x, y + 0.08, z, 8, ""sky"")
      sound(""sparkle"")
    end)
  end)
end
";

        /// <summary>The built-in kind "cup".</summary>
        public const string Cup =
@"-- Cup. Bambi takes a sip and puts it back.
function on_use()
  if pet_busy() then return end
  local x, y, z = me()
  pet_goto(clamp(x - 0.06, -0.4, 0.4), z, function()
    pet_face_user()
    pet_hold(""handR"")
    pet_pose(""talk"", 1.6)
    sound(""drop"")
    after(1.8, function()
      pet_drop()
      me_move(x, z)
      pet_say(""Ahh!"")
      pet_pose(""cheer"", 1.0)
    end)
  end)
end
";

        /// <summary>The built-in kind "record".</summary>
        public const string Record =
@"-- Record player. Press it: a record goes on (press again: off). The buttons choose the record.
offer(""club"", ""Club"")
offer(""lofi"", ""Lo-fi"")
offer(""lullaby"", ""Lullaby"")
offer(""stop"", ""Stop"")
local playing = false

function on_act(name)
  local x, y, z = me()
  if name == ""stop"" then
    playing = false
    music_stop()
    me_glow(""off"")
    sound(""close"")
  else
    playing = true
    music(name)
    me_glow(""honey"")
    me_bounce()
    sound(""open"")
    sparks(x, y + 0.06, z, 6, ""pink"")
    pet_face(x, z)
    pet_pose(""dance"", 4)
  end
end

function on_use()
  on_act(playing and ""stop"" or ""lofi"")
end
";

        /// <summary>The built-in kind "speaker".</summary>
        public const string Speaker =
@"-- Speaker. Press it: a quiet tune goes on; press again: off.
local playing = false

function on_use()
  local x, y, z = me()
  playing = not playing
  if playing then
    music(""lofi"")
    me_bounce()
    sound(""open"")
    sparks(x, y + 0.07, z, 6, ""sky"")
    pet_pose(""dance"", 4)
  else
    music_stop()
    sound(""close"")
    pet_pose(""wave"", 1.2)
  end
end
";

        /// <summary>The built-in kind "food".</summary>
        public const string Food =
@"-- Cake. Bambi eats a piece: the cake gets smaller - and when it is nearly gone, a new one appears.
local left = 1

function on_use()
  if pet_busy() then return end
  local x, y, z = me()
  pet_goto(clamp(x - 0.06, -0.4, 0.4), z, function()
    pet_face(x, z)
    pet_pose(""pick"", 1.2)
    after(1.2, function()
      left = left - 0.15
      if left < 0.5 then
        left = 1
        sparks(x, y + 0.04, z, 12, ""pink"")
        sound(""ta-da"")
      else
        sound(""pop"")
      end
      me_scale(left)
      pet_face_user()
      pet_say(""Yum!"")
      pet_pose(""cheer"", 1.2)
    end)
  end)
end
";

        /// <summary>The built-in kind "toy".</summary>
        public const string Toy =
@"-- Toy. Bambi winds it up: it spins and hops five times.
function on_use()
  if pet_busy() then return end
  local x, y, z, yaw = me()
  pet_goto(clamp(x - 0.06, -0.4, 0.4), z, function()
    pet_face(x, z)
    pet_pose(""work"", 1.0)
    sound(""tick"")
    after(1.0, function()
      local hops = 0
      local t
      me_spin(540)
      sound(""squeak"")
      t = every(0.5, function()
        hops = hops + 1
        me_bounce()
        sound(""bounce"")
        if hops >= 5 then
          cancel(t)
          me_spin(0)
          me_turn(yaw)
          pet_pose(""cheer"", 1.2)
          done()
        end
      end)
    end)
  end)
end
";

        /// <summary>The built-in kind "box".</summary>
        public const string Box =
@"-- Box. Bambi peeks inside - something in there squeaks.
function on_use()
  if pet_busy() then return end
  local x, y, z = me()
  pet_goto(clamp(x - 0.06, -0.4, 0.4), z, function()
    pet_face(x, z)
    pet_pose(""peek"", 1.6)
    after(0.9, function()
      me_bounce()
      sound(""squeak"")
      sparks(x, y + 0.06, z, 6, ""honey"")
      pet_pose(""startle"", 1.0)
    end)
  end)
end
";

        /// <summary>The skill "Dance" (WorldDefaults.Dance) as a script.</summary>
        public const string SkillDance =
@"-- Dance (a skill). Club lights round the desk, the club tune, and Bambi dances until it is told to stop.
function on_use()
  ambience(""club"")
  music(""club"")
  pet_goto(0, 0, function()
    pet_face_user()
    pet_pose(""dance"", 600)
  end)
end

function on_stop()
  ambience_clear()
  music_stop()
  pet_free()
  done()
end
";

        /// <summary>What the user asked for, and the script that was written for it.</summary>
        public static readonly Example[] Made =
        {
            new Example { id = "bat", request = "야구배트 - 착용하면 야구 스윙 모션이 추가돼", requestEn = "A baseball bat - when Bambi holds it, a baseball swing is added", name = "Baseball bat", kind = "toy", uses = new[] { "play" }, sizeM = 0.08f, script = Bat },
            new Example { id = "wand", request = "마법 지팡이 - 흔들면 별이 나와", requestEn = "A magic wand - stars come out when it is waved", name = "Magic wand", kind = "toy", uses = new[] { "play" }, sizeM = 0.07f, script = Wand },
            new Example { id = "drum", request = "북 - 누르면 밤비가 둥둥 쳐", requestEn = "A drum - press it and Bambi drums on it", name = "Drum", kind = "toy", uses = new[] { "play", "music" }, sizeM = 0.06f, script = Drum },
            new Example { id = "football", request = "축구공 - 밤비랑 패스", requestEn = "A football - pass it to and fro with Bambi", name = "Football", kind = "ball", uses = new[] { "play" }, sizeM = 0.05f, script = Football },
            new Example { id = "lamp", request = "스탠드 조명 - 누르면 켜지고 꺼져", requestEn = "A desk lamp - press it and it goes on and off", name = "Desk lamp", kind = "lamp", uses = new[] { "light", "decor" }, sizeM = 0.11f, script = Lamp },
        };

        /// <summary>The built-in kinds of thing (ItemKinds.All), each with what "use" does with it.</summary>
        public static readonly Example[] Shipped =
        {
            new Example { id = "chair", name = "Chair", kind = "chair", uses = ItemKinds.Find("chair").uses, sizeM = ItemKinds.Find("chair").sizeM, script = Chair },
            new Example { id = "desk", name = "Desk", kind = "desk", uses = ItemKinds.Find("desk").uses, sizeM = ItemKinds.Find("desk").sizeM, script = Desk },
            new Example { id = "bed", name = "Bed", kind = "bed", uses = ItemKinds.Find("bed").uses, sizeM = ItemKinds.Find("bed").sizeM, script = Bed },
            new Example { id = "sofa", name = "Sofa", kind = "sofa", uses = ItemKinds.Find("sofa").uses, sizeM = ItemKinds.Find("sofa").sizeM, script = Sofa },
            new Example { id = "book", name = "Book", kind = "book", uses = ItemKinds.Find("book").uses, sizeM = ItemKinds.Find("book").sizeM, script = Book },
            new Example { id = "ball", name = "Ball", kind = "ball", uses = ItemKinds.Find("ball").uses, sizeM = ItemKinds.Find("ball").sizeM, script = Ball },
            new Example { id = "lamp", name = "Lamp", kind = "lamp", uses = ItemKinds.Find("lamp").uses, sizeM = ItemKinds.Find("lamp").sizeM, script = KindLamp },
            new Example { id = "plant", name = "Plant", kind = "plant", uses = ItemKinds.Find("plant").uses, sizeM = ItemKinds.Find("plant").sizeM, script = Plant },
            new Example { id = "cup", name = "Cup", kind = "cup", uses = ItemKinds.Find("cup").uses, sizeM = ItemKinds.Find("cup").sizeM, script = Cup },
            new Example { id = "record", name = "Record player", kind = "record", uses = ItemKinds.Find("record").uses, sizeM = ItemKinds.Find("record").sizeM, script = Record },
            new Example { id = "speaker", name = "Speaker", kind = "speaker", uses = ItemKinds.Find("speaker").uses, sizeM = ItemKinds.Find("speaker").sizeM, script = Speaker },
            new Example { id = "food", name = "Cake", kind = "food", uses = ItemKinds.Find("food").uses, sizeM = ItemKinds.Find("food").sizeM, script = Food },
            new Example { id = "toy", name = "Toy", kind = "toy", uses = ItemKinds.Find("toy").uses, sizeM = ItemKinds.Find("toy").sizeM, script = Toy },
            new Example { id = "box", name = "Box", kind = "box", uses = ItemKinds.Find("box").uses, sizeM = ItemKinds.Find("box").sizeM, script = Box },
        };

        /// <summary>The skills that come with the partner.</summary>
        public static readonly Example[] Skills =
        {
            new Example { id = "skill-dance", name = "Dance", request = "춤 춰 줘", requestEn = "Dance!", skill = true, script = SkillDance },
        };

        /// <summary>The script of a built-in kind of thing ("" = it has none).</summary>
        public static string ForKind(string kind)
        {
            foreach (var e in Shipped) if (e.id == kind) return e.script;
            return "";
        }

        /// <summary>The script of a skill that comes with the partner ("" = it has none).</summary>
        public static string ForSkill(string skillId)
        {
            foreach (var e in Skills) if (e.id == skillId) return e.script;
            return "";
        }

        /// <summary>A thing as the example has it, standing a little to the right in front of the home spot (for the checker and for tests).</summary>
        public static ItemDef Item(Example e)
        {
            return new ItemDef { id = "ex-" + e.id, name = e.name, kind = e.kind, uses = e.uses, sizeM = e.sizeM, status = "ready", inRoom = true, x = 0.12f, z = -0.06f, script = e.script };
        }

        /// <summary>The made examples as text for the writer's prompt: each request, then its script.</summary>
        public static string FewShot(int max = 3)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Made.Length && i < max; i++)
                sb.Append("REQUEST: ").Append(Made[i].request).Append("  (").Append(Made[i].requestEn).Append(")\nSCRIPT:\n").Append(Made[i].script).Append('\n');
            return sb.ToString();
        }
    }
}
