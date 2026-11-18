namespace LivingPartner.Shared
{
    /// <summary>
    /// What a scripted game can call, as TEXT: Doc is pasted, exactly as it is, into the prompt of the language model
    /// that writes scripts (the host's writer adds the examples of GlassScriptExamples and, when a script failed the
    /// check, GlassScriptCheck's report). A test keeps this text and the runtime (GlassScript) in step: every function
    /// named here exists, and every function that exists is named here.
    /// </summary>
    public static class GlassScriptApi
    {
        /// <summary>The functions GlassScript itself defines (besides LuaSandbox.CommonNames and LuaMotion.Functions).</summary>
        public static readonly string[] Functions =
        {
            "rect", "disc", "hoop", "line", "pic", "text", "card",
            "hands", "all_hands", "taps", "held", "reach",
            "pet_goto", "pet_at", "pet_pose", "pet_play", "pet_say", "pet_hurt", "pet_face",
            "score", "add", "lives", "level", "banner", "finish", "time", "sound", "flat", "grid",
            "panel", "button", "display", "toggle",
            "inside", "shoot_out", "out_events", "aim", "shots", "hit_at",
        };
        /// <summary>The methods of what grid() returns.</summary>
        public static readonly string[] GridMethods = { "cell", "center", "draw" };

        public const string Game =
@"You write ONE small Lua program: a game, a toy or a little tool that is played with the HANDS on a field, together
with Nilo, a small character. It runs inside the app in a sandbox. Write only Lua, about 40 - 160 lines.

THE FIELD   x runs from 0 (left) to W (about 1.78), y from 0 to 1. A length of 0.1 is a tenth of the field's height.
  upright (the default): y is UP and the floor is y = 0. Things fall towards y = 0; gravity is yours to write.
  flat (write flat(true) at the top): the field lies on the desk like a board, y points away from the player.
  Use flat ONLY for board and card games and for Nilo running about on the desk. Everything else is upright:
  a tool, anything with words to read, anything that falls, flies, jumps or is shot.
        y=1 +-----------------------------+
            |                             |
            |         (W/2, 0.5)          |
        y=0 +-----------------------------+
           x=0                           x=W

THE SCRIPT
  -- top: constants, the state of the game in local variables, flat(true), motion(...) definitions
  function start() ... end       -- optional; runs once
  function update(dt) ... end    -- runs every frame; dt = seconds since the last frame (about 0.01 - 0.03)

RULES
  1. Draw EVERYTHING again in every update: nothing stays on the screen by itself. What is drawn later is on top.
  2. update must return quickly. Never loop until something happens, never wait: keep the state in variables and
     move it on by dt. A call that runs too long stops the whole game.
  3. At most 160 things a frame, 32 of them words. Words: at most 60 letters, size 0.04 or more.
  4. taps() for placing and choosing; hands() for what follows or touches a hand; shots() for shooting; button
     for keys. There is no mouse and no keyboard. Something must happen when the player plays.
  5. Do not draw the score, the lives or Nilo: the frame around the game shows them. Set them with the functions.
  6. When the game can end, call finish(text) - and keep drawing after it, so that the last picture stays.
  7. Words on the screen are English. Colours are a storybook's: cream paper, brown ink, pastels - never a black
     background, never neon.
  8. A TOOL (calculator, timer, notes, counter, converter ...) is UPRIGHT - never flat -, fills most of the field
     and is built from panel / button / display. Keys are at least 0.14 x 0.12. Send Nilo to one side
     (pet_goto(0.16)) so that it does not stand in front of the keys.
  9. Asked for a game across the glass (Nilo kicks, throws or shoots at the player; the player blocks or shoots
     back)? Then things must really LEAVE the field: shoot_out and out_events, aim and shots. A flat picture of
     it is not what was asked for.

DRAWING   x, y is always the MIDDLE of the thing. alpha is 0..1 (default 1). rot is degrees, counter-clockwise.
  rect(x, y, w, h, color [, alpha [, rot]])
  disc(x, y, r, color [, alpha])                     a filled circle
  hoop(x, y, r, color [, alpha])                     the outline of a circle
  line(x1, y1, x2, y2, thick, color [, alpha])       thick is about 0.006
  text(str, x, y, size [, color [, alpha]])          centred on x, y; size is the height of a letter (0.05 normal, 0.08 big)
  pic(name, x, y, size [, rot [, alpha [, flip]]])   a sticker; size is its height; flip = true mirrors it. The names:
        star gem heart gold drop orb sparkle puff splash bolt shot ring blob frost     small things
        enemy flyer brute         a grumpy little monster, one that flies, a big one
        stone-a stone-b           the player's game piece and Nilo's
        rock paper scissors       a hand showing each, 0.3 is big ('paper' is the picture paper-sign): pic(h.shape, ...)
        or a picture that was made for this module, by its name
  card(title, note, x, y, size [, fill])             a card to choose (size is its height, about 0.34); fill 0..1 closes a ring on it
COLOURS   a name - cream ink brown pink honey mint sky lilac white black red orange yellow green blue grey - or a number 0xRRGGBB.

THE HANDS
  hands() -> a list of the hands over the field, each {x=, y=, down=, id=, r=}. id 1 is the right hand, 2 the left.
             r is how far the hand reaches (about 0.085): a thing is touched when dist(h.x, h.y, x, y) < h.r + its own radius.
             shape is what the hand shows: 'rock' (a fist), 'paper' (an open hand), 'scissors' (two fingers), 'point'
             (one finger) or '' (unclear). speed is how fast it moves, in metres a second: under 0.05 it is held still,
             a slow reach is 0.2, a wave 1.
  all_hands() -> every hand that is SEEN, also one held up far from the field: each {x=, y=, id=, shape=, speed=, near=}.
             near = it is over the field and in hands() too. For games that read a sign or a movement (rock paper
             scissors, freezing): a hand anywhere in view counts.
  taps()  -> a list of {x=, y=}: where the player tapped in THIS frame. On the desk a tap is a fingertip coming down.
             Through the glass of a screen nothing can be touched: there a hand that rests on one spot for half a
             second taps it. Both arrive here in the same way - a game written with taps() works everywhere.
  held(x, y, r) -> for how many seconds a hand has been resting within r of (x, y); 0 when none is. For hold-to-pick:
             card(name, note, x, y, 0.34, held(x, y, 0.12) / 0.8)
  reach([r]) -> how far an open hand reaches; with r it is set (a power can make the hand bigger).

TOOLS AND MENUS   ready-made parts in the app's own look: cream paper, ink outlines, round corners. The call draws them.
  panel(x, y, w, h [, title [, color]])   a card of paper to put the parts on, with a title strip
  button(label, x, y, w, h [, color]) -> true in the frame it is pressed (a tap as in taps(); through a screen also
        pointing at it and pinching). It lights up under a hand, dips and clicks by itself. Call it in every update:
            if button('7', x, y, 0.2, 0.12) then ... end
        At most 40 a frame. Give special keys a pastel colour.
  display(text, x, y, w, h [, align])     a sunk-in field for a number or a line of words, fitted to its size. A number
        stands on the right, words in the middle; align 'left' / 'center' / 'right' says otherwise.
        Write a number with string.format('%.12g', v).
  toggle(label, x, y, on) -> on           a switch, its label to the right. Tapped, it gives back the other state:
            music = toggle('Music', 0.5, 0.3, music)

THROUGH THE GLASS   the field is a window between Nilo's side and the player's. Things can leave it and fly through
the ROOM at the player: out of the screen when the game is played inside a monitor, up from the desk otherwise.
  inside() -> true when the game is played inside a monitor (the player sits in front of its glass), false on the desk
  shoot_out(x, y [, pic_or_color [, speed [, size]]]) -> id    something leaves the field at (x, y) and flies at the
        player: a sticker or a colour (default 'orb'). speed is metres a second (0.7 is fair, 1.2 fast - the player
        is about 0.6 m away), size metres across (0.06). At most 6 on their way at once and 40 in a minute (more
        are not sent: id 0). Show that it is coming: Nilo winds up, the thing grows.
  out_events() -> a list of {id=, what=}: what became of them since the last frame. what is 'blocked' (the player's
        open hand was in the way), 'hit' (it reached the player) or 'missed' (it flew past). Usually blocked
        scores and hit takes a life. Every id gets exactly one event, within a few seconds.
  aim() -> x, y: where the player POINTS on the field (nil, nil when they do not). Draw a crosshair there.
  shots() -> a list of {x=, y=}: the player FIRED at these spots in this frame. Pointing and pinching is the
        trigger; where nobody can point, a tap is a shot. Take each as 'the player fired at this spot':
            for _, s in ipairs(shots()) do if hit_at(s.x, s.y, foe.x, foe.y, 0.08) then ... end end
  hit_at(px, py, x, y, r) -> true when the point (px, py) is within r of (x, y)

BAMBI   the partner: 0.2 tall, drawn for you. pet_at gives its FEET; its middle is 0.1 higher.
  pet_goto(x [, y [, speed]])   it walks there by itself (upright: along the floor; flat: anywhere). Speed 0.6 walks, 1.2 runs.
                                In start() it is simply put there. It begins in the middle of the field.
  pet_at() -> x, y
  pet_pose(name)                what it does when it stands: idle cheer hurt pick wipe cower down - or a motion you defined
  pet_play(name)                plays a motion you defined once (or cheer, hurt ... for a moment), then it goes back to its pose
  pet_say(text [, seconds])     a short line it says
  pet_hurt([seconds])           it blinks for a moment: it was hit
  pet_face(x)                   it turns towards that x

THE GAME
  score([n]) -> the score; with n it is set.   add([n]) adds n (1) to it.   lives([n])   level([n])
  banner(text [, seconds])      one line across the top
  finish(text)                  the game is over; text is the result (""You win!"")
  time() -> seconds played
  sound(name)                   tap pop open close page hammer sparkle ta-da coin error portal-in portal-out kick glove spell whoosh land squeak step hit bounce win lose drop tick
  flat(true)                    lay the field on the desk (only at the top of the script)
  W, H                          the size of the field (H is 1)
  grid(cols, rows, x, y, w, h) -> g: a board of cells in the rectangle w x h around (x, y). Column 1 is the left
        one, row 1 the bottom one (flat: the one nearest the player).
        g:cell(px, py) -> col, row (nil outside)     g:center(col, row) -> x, y     g:draw(color [, thick]) draws its lines
        g.cw, g.ch is the size of one cell.

HELPERS
";

        /// <summary>The whole text for the writer's prompt: the game API, the helpers every script has, and the motions.</summary>
        public const string Doc = Game + LuaSandbox.CommonDoc + "\n\n" + LuaMotion.Doc;
    }
}
