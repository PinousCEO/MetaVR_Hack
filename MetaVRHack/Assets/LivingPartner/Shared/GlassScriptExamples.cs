using System.Collections.Generic;

namespace LivingPartner.Shared
{
    /// <summary>One example script: what was asked for, and the program that answers it.</summary>
    public sealed class GlassScriptExample
    {
        public string id = "";
        public string title = "";           // the module's name
        public string requestKo = "";       // what the user would have said
        public string requestEn = "";
        public string about = "";           // one line: what it is
        public string howTo = "";           // the line shown when it starts
        public string lua = "";
        public bool ship;                   // meant for the shelf (the others are teaching material and test cases only)
        public bool flat;                   // it lies on the desk (the script says flat(true))
    }

    /// <summary>
    /// Scripts written the way the language model should write them: short, state in local variables, everything
    /// drawn again in every update, a few words of comment. They are (1) the few-shot examples of the host's writer,
    /// (2) the proof that the API is enough - each passes GlassScriptCheck without a warning and is played by a
    /// purposeful bot in the tests, and (3) the modules that ship as scripts (ship = true).
    /// OMOK IS NOT FOR THE SHELF: the user asked for exactly that game and the partner has to MAKE it when asked.
    /// It is here as the test case of the checker and as an example for the writer.
    /// </summary>
    public static class GlassScriptExamples
    {
        // ------------------------------------------------------------------ 1. omok
        public const string Omok = @"-- Omok: five in a row on a 9 x 9 board. The player has stone-a and moves first, Nilo has stone-b.
flat(true)
local N = 9
local g = grid(N, N, W / 2, 0.5, 0.9, 0.9)
local board = {}                    -- board[(row - 1) * N + col]: 0 empty, 1 the player, 2 Nilo
for i = 1, N * N do board[i] = 0 end
local function col(i) return (i - 1) % N + 1 end
local function row(i) return math.floor((i - 1) / N) + 1 end

local windows = {}                  -- every five cells in a row, in all four directions: {i1, i2, i3, i4, i5}
for c = 1, N do for r = 1, N do
  for _, d in ipairs({{1, 0}, {0, 1}, {1, 1}, {1, -1}}) do
    local ec, er = c + d[1] * 4, r + d[2] * 4
    if ec >= 1 and ec <= N and er >= 1 and er <= N then
      local w = {}
      for k = 0, 4 do w[k + 1] = (r + d[2] * k - 1) * N + c + d[1] * k end
      windows[#windows + 1] = w
    end
  end
end end

local turn = 1                      -- whose move: 1 the player, 2 Nilo
local think = 0                     -- seconds until Nilo's stone lands
local move = nil                    -- the cell Nilo has chosen
local win = nil                     -- the five cells of the winning line
local stones, clock = 0, 0

-- the window of five that cell i has just completed, or nil
local function line_through(i)
  local who = board[i]
  for _, w in ipairs(windows) do
    if board[w[1]] == who and board[w[2]] == who and board[w[3]] == who and board[w[4]] == who and board[w[5]] == who then
      for k = 1, 5 do if w[k] == i then return w end end
    end
  end
  return nil
end

-- Nilo's move. A window that holds stones of ONE colour only is worth something to each of its cells:
-- most when Nilo can win there, nearly as much when the player could (block!), less for lines that are still short.
local MINE = {1, 6, 40, 500, 1000000}       -- by Nilo's stones already in the window: 0, 1, 2, 3, 4
local YOURS = {1, 5, 35, 2500, 60000}       -- by the player's stones in it
local function choose()
  local value = {}
  for i = 1, N * N do value[i] = 0 end
  for _, w in ipairs(windows) do
    local mine, yours = 0, 0
    for k = 1, 5 do
      local v = board[w[k]]
      if v == 2 then mine = mine + 1 elseif v == 1 then yours = yours + 1 end
    end
    local worth = 0
    if yours == 0 then worth = MINE[mine + 1] elseif mine == 0 then worth = YOURS[yours + 1] end
    if worth > 0 then
      for k = 1, 5 do value[w[k]] = value[w[k]] + worth end
    end
  end
  local best, cell = -1, 0
  for i = 1, N * N do
    if board[i] == 0 then            -- a little chance, and a little love for the middle
      local v = value[i] + rnd() * 0.5 - (math.abs(col(i) - 5) + math.abs(row(i) - 5)) * 0.1
      if v > best then best, cell = v, i end
    end
  end
  return cell
end

local function place(i, who)
  board[i] = who
  stones = stones + 1
  sound(who == 1 and 'tap' or 'pop')
  win = line_through(i)
  if win then
    if who == 1 then add(1)  sound('win')  pet_pose('down')  finish('You win - five in a row!')
    else sound('lose')  pet_pose('cheer')  finish('Nilo wins this time.') end
  elseif stones == N * N then
    finish('A draw - the board is full.')
  else
    turn = 3 - who
  end
end

function start()
  pet_goto(W / 2 + 0.6, 0.5)
end

function update(dt)
  clock = clock + dt
  if turn == 1 and not win then
    for _, t in ipairs(taps()) do
      local c, r = g:cell(t.x, t.y)
      if c and turn == 1 and board[(r - 1) * N + c] == 0 then
        place((r - 1) * N + c, 1)
        if turn == 2 then                       -- Nilo thinks, and walks to the side of the board nearest its move
          move = choose()
          think = 0.9
          local x, y = g:center(col(move), row(move))
          pet_goto(x < W / 2 and W / 2 - 0.6 or W / 2 + 0.6, y)
          banner('Nilo is thinking...', 1)
        end
      end
    end
  elseif turn == 2 and not win then
    think = think - dt
    if think <= 0 then
      place(move, 2)
      if turn == 1 then banner('Your turn', 1.5) end
    end
  end

  rect(W / 2, 0.5, 0.96, 0.96, 'honey')        -- the board, its lines, the stones
  g:draw('brown', 0.004)
  for i = 1, N * N do
    if board[i] ~= 0 then
      local x, y = g:center(col(i), row(i))
      pic(board[i] == 1 and 'stone-a' or 'stone-b', x, y, g.ch * 0.86)
    end
  end
  if turn == 1 and not win then                -- the free square a hand is over
    for _, h in ipairs(hands()) do
      local c, r = g:cell(h.x, h.y)
      if c and board[(r - 1) * N + c] == 0 then
        local x, y = g:center(c, r)
        hoop(x, y, g.cw * 0.4, 'ink', 0.5)
      end
    end
  end
  if win then
    for _, i in ipairs(win) do
      local x, y = g:center(col(i), row(i))
      hoop(x, y, g.cw * 0.5, 'red')
      pic('sparkle', x, y, g.ch * 0.5, clock * 90)
    end
  end
end
";

        // ------------------------------------------------------------------ 2. catching stars
        public const string Catch = @"-- Catch the falling stars. The basket follows your hand and Nilo runs along under it.
-- Five misses and it is over; it starts slowly and every 8 stars they fall faster.
local stars = {}              -- each {x, y, vy, turn}
local pops = {}               -- a sparkle where one was caught, a puff where one was missed: {x, y, age, miss}
local basket = W / 2          -- the middle of the basket
local next_star = 2.5         -- seconds until the next star (a moment to find the basket first)
local fall = 0.18             -- how fast a new star falls
local caught, clock = 0, 0
local over = false
local FLOOR, BASKET_Y, HALF = 0.05, 0.27, 0.12

function start()
  lives(5)
  pet_say('Catch them all!', 2)
end

function update(dt)
  clock = clock + dt
  local hs = hands()
  if #hs > 0 and not over then
    basket = lerp(basket, clamp(hs[1].x, HALF, W - HALF), math.min(1, dt * 14))
  end
  pet_goto(basket, 0, 1.4)

  if not over then
    next_star = next_star - dt
    if next_star <= 0 then
      stars[#stars + 1] = {x = rnd(0.15, W - 0.15), y = 1.06, vy = fall * rnd(0.9, 1.2), turn = rnd(-200, 200)}
      next_star = math.max(0.5, 2.4 - 0.2 * level())
    end
    for i = #stars, 1, -1 do
      local s = stars[i]
      s.vy = s.vy + 0.08 * dt                    -- gravity
      s.y = s.y - s.vy * dt
      if s.y < BASKET_Y + 0.04 and s.y > BASKET_Y - 0.06 and math.abs(s.x - basket) < HALF then
        table.remove(stars, i)
        add(1)  caught = caught + 1  sound('coin')
        pops[#pops + 1] = {x = s.x, y = BASKET_Y + 0.03, age = 0}
        if caught % 8 == 0 then
          level(level() + 1)  fall = fall + 0.07
          banner('Level ' .. level() .. ' - faster!', 1.5)  sound('sparkle')  pet_play('cheer')
        end
      elseif s.y < FLOOR then
        table.remove(stars, i)
        lives(lives() - 1)  sound('error')  pet_hurt(0.6)
        pops[#pops + 1] = {x = s.x, y = FLOOR + 0.02, age = 0, miss = true}
        if lives() <= 0 then
          over = true  pet_pose('down')  sound('lose')
          finish('You caught ' .. score() .. ' stars!')
        end
      end
    end
  end

  rect(W / 2, FLOOR / 2, W, FLOOR, 'mint')                       -- the ground
  for _, s in ipairs(stars) do pic('star', s.x, s.y, 0.09, s.turn * clock) end
  rect(basket, BASKET_Y, HALF * 2, 0.05, 'brown')                -- the basket and its rim
  rect(basket, BASKET_Y + 0.025, HALF * 2 + 0.03, 0.015, 'honey')
  for i = #pops, 1, -1 do
    local p = pops[i]
    p.age = p.age + dt
    if p.age > 0.5 then table.remove(pops, i)
    else pic(p.miss and 'puff' or 'sparkle', p.x, p.y + p.age * 0.1, 0.07 + p.age * 0.1, 0, 1 - p.age * 2) end
  end
end
";

        // ------------------------------------------------------------------ 3. memory
        public const string Memory = @"-- Memory: twelve cards, six pairs. Tap a card to turn it over; two of a kind stay open.
flat(true)
local COLS, ROWS = 4, 3
local g = grid(COLS, ROWS, W / 2 + 0.1, 0.5, 1.2, 0.86)
local FACES = {'star', 'heart', 'gem', 'gold', 'drop', 'orb'}
local cards = {}              -- cards[i] = {face, up, done, turn}; turn 0..1 is how far it has turned (for the flip)
local first, second = nil, nil    -- the two cards that are open right now
local look = 0                -- seconds left to look at two that do not match
local left = #FACES           -- pairs still to find
local turns = 0

function start()
  local deck = {}
  for _, f in ipairs(FACES) do deck[#deck + 1] = f  deck[#deck + 1] = f end
  for i = #deck, 2, -1 do local j = rndi(1, i)  deck[i], deck[j] = deck[j], deck[i] end     -- shuffle
  for i, f in ipairs(deck) do cards[i] = {face = f, up = false, done = false, turn = 0} end
  pet_goto(0.16, 0.5)
  pet_say('Find the pairs!', 2)
end

local function tap_card(i)
  local card = cards[i]
  if card.up or card.done or second then return end
  card.up = true
  sound('page')
  if not first then first = i  return end
  second = i
  turns = turns + 1
  if cards[first].face == card.face then
    cards[first].done = true  card.done = true
    first, second = nil, nil
    left = left - 1
    add(10)  sound('coin')  pet_play('cheer')
    if left == 0 then sound('win')  pet_pose('cheer')  finish('All pairs found in ' .. turns .. ' turns!') end
  else
    look = 0.9
    sound('error')
  end
end

function update(dt)
  if look > 0 then
    look = look - dt
    if look <= 0 then cards[first].up = false  cards[second].up = false  first, second = nil, nil end
  else
    for _, t in ipairs(taps()) do
      local c, r = g:cell(t.x, t.y)
      if c then tap_card((r - 1) * COLS + c) end
    end
  end

  for i, card in ipairs(cards) do
    local x, y = g:center((i - 1) % COLS + 1, math.floor((i - 1) / COLS) + 1)
    card.turn = clamp(card.turn + (card.up and dt or -dt) * 6, 0, 1)
    local wide = math.abs(card.turn * 2 - 1)          -- a card gets narrow while it turns
    local alpha = card.done and 0.55 or 1
    if card.turn > 0.5 then
      rect(x, y, g.cw * 0.86 * wide, g.ch * 0.86, 'white', alpha)
      if wide > 0.4 then pic(card.face, x, y, g.ch * 0.5, 0, alpha) end
    else
      rect(x, y, g.cw * 0.86 * wide, g.ch * 0.86, 'pink')
      if wide > 0.4 then text('?', x, y, 0.1, 'cream') end
    end
  end
  text(turns .. ' turns', 0.16, 0.9, 0.05, 'brown')
end
";

        // ------------------------------------------------------------------ 4. a focus timer
        public const string Pomodoro = @"-- A focus timer: 25 minutes of work, then 5 of rest, round after round.
-- Tap the ring to start or to pause. Nilo sits and studies along, and cheers when a round is done.
local FOCUS, REST = 25 * 60, 5 * 60       -- seconds
local left = FOCUS
local running, resting = false, false
local CX, CY, R = W / 2 + 0.3, 0.52, 0.3
local DOTS = 60

motion('study', 4, function(u, p)         -- sitting and writing; it looks up for a moment near the end
  local up = keys(u, {0.7, 0, 0.8, 1, 0.92, 1, 1, 0})
  p.base = 'sit'
  p.spine = {x = 12 - 6 * up}
  p.head  = {x = 14 - 22 * up, y = 5 * wave(u)}
  p.armR  = {x = -62 + 4 * wave(u, 8), z = -8 + 6 * wave(u, 3)}      -- the writing hand
  p.foreR = {x = -48}
  p.armL  = {x = -52, z = -12}
  p.foreL = {x = -50}
end, true)

motion('hooray', 1.2, function(u, p)      -- a hop with the arms flung out
  local air = keys(u, {0.15, 0, 0.5, 1, 0.85, 0})
  p.lift = 0.3 * air
  p.squash = 0.3 * pulse(clamp(u * 6, 0, 1)) - 0.2 * air
  p.armL = {z = 10 + 55 * air}
  p.armR = {z = 10 + 55 * air}
  p.mouth = air
end)

local function clock_text(s)
  s = math.max(0, math.ceil(s))
  return string.format('%02d:%02d', math.floor(s / 60), s % 60)
end

function start()
  pet_goto(0.42)
  pet_say('Tap the ring when you are ready.', 3)
end

function update(dt)
  for _, t in ipairs(taps()) do
    if dist(t.x, t.y, CX, CY) < R + 0.06 then
      running = not running
      sound(running and 'open' or 'close')
      if running then pet_say(resting and 'A little rest.' or 'Let us focus!', 2) end
    end
  end
  if running then
    left = left - dt
    if left <= 0 then                           -- a round of work or a rest is over
      resting = not resting
      left = resting and REST or FOCUS
      if resting then
        add(1)  sound('ta-da')  pet_play('hooray')
        banner('Well done! Take a break.', 4)
      else
        sound('sparkle')
        banner('Back to work!', 3)
      end
    end
  end
  pet_pose((running and not resting) and 'study' or (resting and 'cheer' or 'idle'))

  local done = 1 - left / (resting and REST or FOCUS)      -- the ring: dots that fill up clockwise from the top
  disc(CX, CY, R + 0.05, 'cream')
  hoop(CX, CY, R + 0.05, 'brown')
  for i = 0, DOTS - 1 do
    local a = math.pi / 2 - i / DOTS * 2 * math.pi
    local on = i / DOTS < done
    disc(CX + math.cos(a) * R, CY + math.sin(a) * R, on and 0.016 or 0.007, on and (resting and 'mint' or 'red') or 'grey')
  end
  text(clock_text(left), CX, CY + 0.03, 0.12)
  text(running and (resting and 'break' or 'focus') or 'tap to start', CX, CY - 0.1, 0.045, 'brown')
end
";

        // ------------------------------------------------------------------ 5. tag
        public const string Tag = @"-- Tag: Nilo runs about on the desk - touch it with a finger to catch it. Thirty seconds.
flat(true)
local ROUND = 30
local goal_x, goal_y = W / 2, 0.5     -- where Nilo is heading
local pause = 0                       -- it stands still for this long (caught, out of breath)
local safe = 0                        -- and cannot be caught again for this long
local puffs = {}                      -- where it was caught: {x, y, age}
local MARGIN = 0.14

local function somewhere()
  return rnd(MARGIN, W - MARGIN), rnd(MARGIN, 1 - MARGIN)
end

function start()
  pet_say('You cannot catch me!', 2)
end

function update(dt)
  local px, py = pet_at()
  local left = ROUND - time()
  safe = safe - dt

  local near, hx, hy = 9, 0, 0              -- the nearest hand
  for _, h in ipairs(hands()) do
    local d = dist(h.x, h.y, px, py)
    if d < near then near, hx, hy = d, h.x, h.y end
  end

  if left <= 0 then
    pet_pose('cheer')
    finish('You caught Nilo ' .. score() .. ' times!')
  elseif pause > 0 then
    pause = pause - dt
    if pause <= 0 then goal_x, goal_y = somewhere()  pet_goto(goal_x, goal_y, 1.0) end
  elseif near < 0.09 and safe <= 0 then     -- caught!
    add(1)  sound('squeak')  pet_play('hurt')  pet_say('Eek!', 0.8)
    puffs[#puffs + 1] = {x = px, y = py, age = 0}
    pause, safe = 0.7, 1.5
    pet_goto(px, py)
  elseif near < 0.45 then                   -- a hand is close: run away from it
    local ax, ay = px - hx, py - hy
    local n = math.max(0.001, dist(0, 0, ax, ay))
    goal_x = clamp(px + ax / n * 0.4, MARGIN, W - MARGIN)
    goal_y = clamp(py + ay / n * 0.4, MARGIN, 1 - MARGIN)
    if dist(goal_x, goal_y, px, py) < 0.1 then goal_x, goal_y = somewhere() end     -- in a corner: break out
    pet_goto(goal_x, goal_y, 0.9 + 0.02 * score())
  elseif dist(px, py, goal_x, goal_y) < 0.03 then     -- nothing near: stroll
    goal_x, goal_y = somewhere()
    pet_goto(goal_x, goal_y, 0.4)
  end

  rect(W / 2, 0.5, W - 0.06, 0.94, 'mint', 0.45)                  -- the meadow
  rect(W / 2, 0.95, (W - 0.2) * clamp(left / ROUND, 0, 1), 0.02, 'honey')    -- the time that is left
  disc(px, py, 0.07, 'green', 0.35)                               -- Nilo's shadow
  for i = #puffs, 1, -1 do
    local p = puffs[i]
    p.age = p.age + dt
    if p.age > 0.6 then table.remove(puffs, i)
    else pic('puff', p.x, p.y, 0.1 + p.age * 0.2, 0, 1 - p.age / 0.6) end
  end
  for _, h in ipairs(hands()) do hoop(h.x, h.y, 0.05, 'ink', 0.4) end
end
";

        // ------------------------------------------------------------------ 6. Shadow Umbrella (the game that used to be GlassUmbrella.cs)
        public const string Umbrella = @"-- Shadow Umbrella: Nilo gathers stars from the floor while ink rains down. Your hand is the umbrella:
-- hold it over Nilo. Golden drops are good - lift the hand and let them through.
local stars = {}              -- the x of the three stars on the floor
local drops = {}              -- each {x, y, vx, vy, kind}; kind: 'drop', 'blob' (big: meet it with the middle of the hand), 'gold'
local fx = {}                 -- short effects: {pic, x, y, age, text}
local SIZE = {drop = 0.055, blob = 0.10, gold = 0.07}
local next_drop = 1.5         -- seconds until the next drop
local wind, wind_turn = 0, 6  -- from level 3 the wind carries the drops sideways and turns every six seconds
local picking = 0             -- seconds Nilo still bends down for a star
local safe = 0                -- it cannot be hit again for this long
local hush = 0                -- while a golden drop comes down no ink is aimed at Nilo: the umbrella can be lifted
local picked, clock = 0, 0
local over = false
local FLOOR, HEAD = 0.05, 0.22

local function effect(name, x, y, words) fx[#fx + 1] = {pic = name, x = x, y = y, age = 0, text = words} end

function start()
  for i = 1, 3 do stars[i] = rnd(0.15, W - 0.15) end
  lives(3)
end

local function new_drop()
  local px = pet_at()
  local kind = 'drop'
  if rndi(1, 8) == 1 then kind = 'gold' elseif level() >= 2 and rndi(1, 4) == 1 then kind = 'blob' end
  local fall = (0.42 + 0.035 * level()) * (kind == 'gold' and 0.55 or (kind == 'blob' and 0.7 or 1))
  local x = rnd(0.08, W - 0.08)
  if kind == 'gold' then
    x = clamp(px + rnd(-0.1, 0.1), 0.08, W - 0.08)
    hush = 0.9 / fall
  elseif hush > 0 then
    x = px < W / 2 and px + rnd(0.3, 0.8) or px - rnd(0.3, 0.8)
  elseif rnd() < 0.6 then
    x = clamp(px + rnd(-0.1, 0.1), 0.08, W - 0.08)              -- most are aimed at Nilo
  end
  local drift = kind == 'gold' and 0 or wind                 -- (the wind does not move the golden ones)
  drops[#drops + 1] = {x = x - drift * 0.9, y = 1.06, vx = drift, vy = fall, kind = kind}
end

function update(dt)
  clock = clock + dt
  safe, hush = safe - dt, hush - dt
  local px = pet_at()
  local hs = hands()

  if not over then
    -- the rain
    next_drop = next_drop - dt
    if next_drop <= 0 then new_drop()  next_drop = math.max(0.28, 1.0 - 0.09 * level()) * rnd(0.7, 1.3) end
    if level() >= 3 then
      wind_turn = wind_turn - dt
      if wind_turn <= 0 then wind_turn = 6  wind = (wind > 0 and -1 or 1) * rnd(0.08, 0.16)  sound('whoosh') end
    end
    local threat = false                      -- ink right above Nilo, and no hand between
    for i = #drops, 1, -1 do
      local d = drops[i]
      local r = SIZE[d.kind] / 2
      d.x = d.x + d.vx * dt
      d.y = d.y - d.vy * dt
      if d.kind == 'gold' then d.x = d.x + clamp(px - d.x, -0.22 * dt, 0.22 * dt) end      -- a golden one leans towards Nilo
      local caught = false
      for _, h in ipairs(hs) do
        local reach = d.kind == 'blob' and h.r * 0.8 or h.r + r
        if dist(h.x, h.y, d.x, d.y) < reach then caught = true end
      end
      if caught then
        table.remove(drops, i)
        if d.kind == 'gold' then effect('puff', d.x, d.y)  sound('squeak')       -- a golden one wasted
        else effect('splash', d.x, d.y)  sound('drop') end
      elseif d.y < HEAD and math.abs(d.x - px) < 0.045 + r then                   -- it reaches Nilo
        table.remove(drops, i)
        if d.kind == 'gold' then
          add(3)  sound('coin')  effect('sparkle', px, HEAD + 0.05, '+3')
        elseif safe <= 0 then
          lives(lives() - 1)  safe = 1.2  sound('error')  pet_hurt(1.2)  effect('heart', px, HEAD + 0.06)
          if lives() <= 0 then over = true  sound('lose')  pet_pose('down')  finish('Nilo gathered ' .. score() .. ' stars.') end
        end
      elseif d.y < FLOOR then
        table.remove(drops, i)
        effect('splash', d.x, FLOOR)
      elseif d.kind ~= 'gold' and d.y < 0.6 and math.abs(d.x - px) < 0.1 then
        threat = true
      end
    end

    -- Nilo: cowers under ink that nobody stops, otherwise walks to the nearest star and bends down for it
    if over then
    elseif threat then
      pet_pose('cower')  pet_goto(px)  picking = 0
    elseif picking > 0 then
      picking = picking - dt
      if picking <= 0 then
        for i = 1, 3 do if math.abs(stars[i] - px) < 0.03 then stars[i] = rnd(0.15, W - 0.15) end end
        add(1)  picked = picked + 1  sound('sparkle')  effect('sparkle', px, 0.12)
        if picked % 5 == 0 then level(level() + 1)  sound('ta-da')  banner('Level ' .. level(), 1.5)  pet_play('cheer') end
      end
    else
      local goal = stars[1]
      for i = 2, 3 do if math.abs(stars[i] - px) < math.abs(goal - px) then goal = stars[i] end end
      if math.abs(goal - px) < 0.02 then picking = 0.5  pet_pose('pick')
      else pet_pose('idle')  pet_goto(goal, 0, 0.3) end
    end
  end

  -- draw: the floor, the stars, the rain, the effects
  rect(W / 2, FLOOR / 2, W, FLOOR, 'lilac', 0.6)
  for i = 1, 3 do pic('star', stars[i], FLOOR + 0.035, 0.07, 10 * math.sin(clock * 2 + i)) end
  for _, d in ipairs(drops) do pic(d.kind, d.x, d.y, SIZE[d.kind], d.vx * 120) end
  for i = #fx, 1, -1 do
    local f = fx[i]
    f.age = f.age + dt
    if f.age > 0.6 then table.remove(fx, i)
    else
      pic(f.pic, f.x, f.y + f.age * 0.12, 0.08 + f.age * 0.08, 0, 1 - f.age / 0.6)
      if f.text then text(f.text, f.x, f.y + 0.08 + f.age * 0.12, 0.06, 'honey', 1 - f.age / 0.6) end
    end
  end
end
";

        // ------------------------------------------------------------------ 7. Vampire Night (the game that used to be GlassSurvival.cs)
        public const string Survival = @"-- Vampire Night: they keep coming. Nilo fights by itself and follows your hand; your hand bops what it touches.
flat(true)
local pulse_time, sigils, mx, my = 6, {}, {}, {}
local foes, shots, gems, fx = {}, {}, {}, {}
local KIND = {
  enemy = {size = 0.11, speed = 0.16, hp = 1},
  flyer = {size = 0.09, speed = 0.19, hp = 1},
  brute = {size = 0.20, speed = 0.085, hp = 4},
}
local POWERS = {
  {id = 'star',  name = 'Star Shot',   note = 'more stars, faster'},
  {id = 'moons', name = 'Moons',       note = 'one more moon circles'},
  {id = 'frost', name = 'Frost',       note = 'they come slower'},
  {id = 'swift', name = 'Swift Feet',  note = 'Nilo is faster'},
  {id = 'giant', name = 'Big Hand',    note = 'your hand is bigger'},
  {id = 'bolt',  name = 'Lightning',   note = 'strikes the nearest'},
  {id = 'heart', name = 'Extra Heart', note = 'one more heart'},
}
local has = {star = 1, moons = 0, frost = 0, swift = 0, giant = 0, bolt = 0, heart = 0}     -- the level of each power, up to 3
local offer, offer_age = nil, 0      -- the three powers to choose from; the world stands still meanwhile
local xp, need, wave, wave_left = 0, 7, 1, 18
local spawn, shoot, strike = 1, 0.5, 3      -- seconds until the next enemy / star / lightning
local safe, clock, over, FLOOR = 0, 0, false, 0.05
local function effect(name, x, y, size) fx[#fx + 1] = {pic = name, x = x, y = y, size = size or 0.09, age = 0} end
local function hurt(f, n)            -- an enemy takes n hits; when it falls it leaves a gem
  f.hp = f.hp - n
  if f.hp > 0 then sound('hit') return end
  f.dead = true; add(1); sound('pop'); effect('puff', f.x, f.y)
  gems[#gems + 1] = {x = clamp(f.x, 0.05, W - 0.05), y = math.max(FLOOR + 0.03, f.y - 0.03), age = 0}
end
local function nearest(x, y, range)
  local best = nil
  for _, f in ipairs(foes) do
    local d = dist(x, y, f.x, f.y)
    if d < range and not f.dead then best, range = f, d end
  end
  return best
end
local function level_up()
  level(level()+1); xp,need = xp-need, 4+3*level()
  local pool = {}
  for _, p in ipairs(POWERS) do if has[p.id] < 3 then pool[#pool + 1] = p end end
  if #pool == 0 then lives(math.min(6, lives() + 1)) return end
  offer, offer_age = {}, 0
  for i = 1, math.min(3, #pool) do offer[i] = table.remove(pool, rndi(1, #pool)) end
  sound('sparkle')  banner('Level ' .. level() .. ' - pick a power', 0)
end
local function take(p)
  has[p.id] = has[p.id] + 1
  if p.id == 'heart' then lives(math.min(6, lives() + 1)) end
  if p.id == 'giant' then reach(0.085 * (1 + 0.4 * has.giant)) end
  offer, safe = nil, 1
  sound('ta-da')  banner(p.name .. '!', 1.5)  pet_play('cheer')
end
local function graveyard()
  local quiet=#foes<13 and level()<4
  rect(W/2,0.5,W,1,'#122334')
  rect(W/2,0.49,W-0.09,0.87,'#1C3940')
  -- Flagstones cross a ring of weathered standing stones.
  rect(W/2,0.49,0.36,0.86,'#29454B')
  rect(W/2,0.49,W-0.09,0.16,'#29454B')
  if quiet then for i=0,6 do line(W/2-0.17,0.10+i*0.125,W/2+0.17,0.10+i*0.125,0.004,'#18333B') end end
  local cx,cy=W/2,0.49
  disc(cx,cy,0.246,'#17343C')
  hoop(cx,cy,0.235,'#408276',0.70)
  if quiet then hoop(cx,cy,0.195,'#37706E',0.70) end
  for i=0,7,quiet and 1 or 4 do
    local a=i*math.pi/4; local x,y=cx+0.216*math.cos(a),cy+0.216*math.sin(a)
    rect(x,y,0.012,0.012,'#7FC5AA',0.83,45)
  end
  for i=1,6 do
    local x=(i<4 and 0.075 or 0.925)*W; local y=0.19+((i-1)%3)*0.30
    if quiet then rect(x,y,0.092,0.095,'#102A31') end
    rect(x-0.007,y+0.017,0.068,0.088,'#71838B')
    rect(x-0.007,y+0.028,0.035,0.009,'#354956')
    if quiet then rect(x-0.007,y+0.022,0.008,0.027,'#354956') end
  end
  for i=0,3 do
    local x=(0.25+(i%2)*0.5)*W; local y=i<2 and 0.18 or 0.79
    disc(x,y,0.052,'#5FC3A7',0.23)
    rect(x,y,0.030,0.030,'#A8F2CD',0.92,45)
  end
  -- Split stone and fireflies mark the paths between the gravestones.
  for i=1,quiet and 6 or 0 do
    local x=(0.19+0.60*((i*7)%11)/10)*W; local y=0.13+0.68*((i*3)%7)/6
    line(x,y,x+0.034,y+0.015,0.004,'#122E36')
    line(x+0.034,y+0.015,x+0.055,y+0.006,0.003,'#122E36')
    disc(x+0.035*math.sin(clock*0.55+i),y+0.045+0.013*math.sin(clock+i),0.0035,'#E3DC90',0.8)
  end
end
function start()
  lives(3) pet_goto(W / 2, 0.45, 2)
  for i=0,7 do
    local a=(i+0.35)*math.pi/4
    local kind=i%3==0 and 'flyer' or 'enemy'
    foes[#foes+1]={x=W*(0.5+0.41*math.cos(a)),y=0.48+0.39*math.sin(a),kind=kind,hp=1,wait=0}
  end
end
function update(dt)
  if not offer and not over then clock=clock+dt end
  local px, py = pet_at()
  local MID = py + 0.045
  for m = 1, has.moons do
    local a = clock * 2.6 + m * 2 * math.pi / has.moons
    mx[m], my[m] = px + math.cos(a)*0.17, MID + math.sin(a)*0.17
  end
  if offer then                                 -- choosing: hold a hand on a card (the first one after 15 seconds)
    offer_age = offer_age + dt
    for i, p in ipairs(offer) do
      local on = offer_age > 0.3 and held(W / 2 + (i - 2) * 0.42, 0.56, 0.15) or 0
      if on >= 0.8 or (i == 1 and offer_age > 15) then take(p) break end
    end
  elseif not over then
    safe = safe - dt
    local hs = hands(); local lead = hs[1]
    local ax, ay = aim()
    if ax or lead then pet_goto(ax or lead.x, clamp(ay or lead.y, 0.07, 0.87), 0.52 + 0.11 * has.swift)
    elseif wave <= 2 then
      local near = nearest(px,MID,0.34)
      if near then pet_goto(clamp(px+2*(px-near.x),0.12,W-0.12),clamp(py+2*(MID-near.y),0.12,0.76),0.34) end
    end
    wave_left = wave_left - dt                  -- more of them with every wave
    if wave_left <= 0 then wave, wave_left = wave + 1, 18  sound('whoosh')  banner('Wave ' .. wave, 1.5) end
    spawn = spawn - dt
    if spawn <= 0 and #foes < 26 then
      spawn = math.max(0.80, 2.45 - 0.18 * wave)
      local edge, center = rndi(1,4), rnd(0.18,0.82)
      for j = 1, math.min(26 - #foes, 2 + math.min(3,wave)) do
        local u = clamp(center + (j - 2) * 0.09, 0.03, 0.97)
        local x = edge == 1 and -0.05 or edge == 2 and W + 0.05 or W * u
        local y = edge == 3 and 0.99 or edge == 4 and 0.02 or u
        local kind = wave >= 3 and j == 1 and 'brute' or (wave >= 2 and j == 2 and 'flyer' or 'enemy')
        foes[#foes+1] = {x=x, y=y, kind=kind, hp=KIND[kind].hp, wait=0}
      end
    end
    pulse_time = pulse_time - dt
    if pulse_time <= 0 then
      pulse_time = 6
      local f = nearest(px, MID, W+1)
      local id = f and shoot_out(f.x, f.y, 'gem', 0.62, 0.065) or 0
      if id > 0 then sigils[id] = {x=f.x,y=f.y} end
    end
    for _,e in ipairs(out_events()) do
      local src = sigils[e.id]; sigils[e.id] = nil
      if src and e.what == 'blocked' then
        effect('ring',src.x,src.y,0.5)
        for _,f in ipairs(foes) do if not f.dead and dist(f.x,f.y,src.x,src.y)<0.48 then hurt(f,3) end end
        sound('sparkle')
      end
    end
    shoot = shoot - dt; local target = shoot <= 0 and nearest(px, MID, 0.75)
    if target then
      shoot = 1.1 - 0.2 * has.star
      for n = 1, has.star do
        local a = math.atan2(target.y - MID, target.x - px) + (n - (has.star + 1) / 2) * 0.25
        shots[#shots + 1] = {x = px, y = MID, vx = math.cos(a) * 1.5, vy = math.sin(a) * 1.5, life = 0.6}
      end
      pet_face(target.x)
    end
    strike = strike - dt
    if has.bolt > 0 and strike <= 0 then
      strike = 3.6 - 0.6 * has.bolt
      local f = nearest(px, MID, 0.9)
      if f then effect('bolt', f.x, (f.y + 1) / 2, 1 - f.y)  hurt(f, 2) end
    end
    local slow = (1 - 0.13 * has.frost) * math.min(1, 0.34 + wave * 0.16)           -- the enemies: towards Nilo; hands and moons hurt them; touching Nilo costs a heart
    for _, f in ipairs(foes) do
      local k = KIND[f.kind]
      local d = math.max(0.001, dist(f.x, f.y, px, MID))
      f.x = f.x + (px - f.x) / d * k.speed * slow * dt
      f.y = f.y + (MID - f.y) / d * k.speed * slow * dt
      f.wait = f.wait - dt
      if f.wait <= 0 then
        for _, h in ipairs(hs) do
          if not f.dead and f.wait <= 0 and dist(h.x, h.y, f.x, f.y) < h.r + k.size / 2 then
            f.wait = 0.4
            f.x = f.x + (f.x < px and -0.12 or 0.12)          -- knocked back
            hurt(f, has.giant >= 3 and 2 or 1)
          end
        end
        if d < 0.32 and not f.dead and f.wait <= 0 then
          for m = 1, has.moons do
            if dist(mx[m], my[m], f.x, f.y) < 0.035 + k.size / 2 then
              f.wait = 0.4; hurt(f, 1); break
            end
          end
        end
      end
      if not f.dead and safe <= 0 and d < 0.05 + k.size / 2 then
        safe, f.x = 1.2, f.x + (f.x < px and -0.2 or 0.2)
        lives(lives() - 1)  sound('error')  pet_hurt(1.2)  effect('heart', px, MID)
        if lives() <= 0 then
          over = true  sound('lose')  pet_pose('down')
          finish('Nilo beat ' .. score() .. ' of them in ' .. math.floor(time()) .. ' seconds.')
        end
      end
    end
    for i = #shots, 1, -1 do                    -- the stars fly, and hit
      local s = shots[i]
      s.x, s.y, s.life = s.x + s.vx * dt, s.y + s.vy * dt, s.life - dt
      local f = nearest(s.x, s.y, 0.07)
      if f then hurt(f, 1)  s.life = 0 end
      if s.life <= 0 then table.remove(shots, i) end
    end
    for i = #foes, 1, -1 do if foes[i].dead then table.remove(foes, i) end end
    for i = #gems, 1, -1 do                     -- gems: a hand or Nilo gathers them
      local g = gems[i]
      g.age = g.age + dt
      local gap = dist(g.x, g.y, px, MID)
      if gap < 0.30 and g.age > 0.18 then
        local k = math.min(1, dt * 1.15 / math.max(0.01,gap))
        g.x, g.y = lerp(g.x,px,k), lerp(g.y,MID,k)
      end
      local taken = g.age > 0.25 and gap < 0.065
      for _, h in ipairs(hs) do if g.age > 0.25 and dist(h.x, h.y, g.x, g.y) < h.r + 0.03 then taken = true end end
      if taken then table.remove(gems, i)  xp = xp + 1  sound('coin')  effect('sparkle', g.x, g.y, 0.06)
      elseif g.age > 12 then table.remove(gems, i) end
    end
    if xp >= need and not over then level_up() end
  end
  graveyard()
  hoop(px,MID,0.095,'#7BFFD2',0.72)
  text('Wave ' .. wave,0.16,0.86,0.048,'#E4F6CC')
  rect(W/2,0.885,W*0.45,0.014,'#0B1827')
  local fill=W*0.45*math.min(1,xp/need)
  rect(W*0.275+fill/2,0.885,fill,0.012,'#6FF4C6')
  for _, g in ipairs(gems) do pic('gem', g.x, g.y, 0.05) end
  local drift=math.sin(5*clock)
  for i, f in ipairs(foes) do
    local size=KIND[f.kind].size
    local bob=i%2==0 and drift or -drift
    if f.kind=='brute' then rect(f.x,f.y-size*0.42,size*0.50,0.010,'#0A212B',0.72) end
    pic(f.kind,f.x,f.y+0.005*bob,size,4*bob,1,f.x>px)
  end
  for m = 1, #mx do pic('orb', mx[m], my[m], 0.07) end
  for _, s in ipairs(shots) do pic('shot', s.x, s.y, 0.05, math.atan2(s.vy, s.vx) * 180 / math.pi) end
  for i = #fx, 1, -1 do
    local f = fx[i]
    f.age = f.age + dt
    if f.age > 0.5 then table.remove(fx, i)
    else pic(f.pic, f.x, f.y, f.size * (1 + f.age), 0, 1 - f.age / 0.5) end
  end
  if offer then
    for i, p in ipairs(offer) do
      local x = W / 2 + (i - 2) * 0.42
      card(p.name, p.note, x, 0.56, 0.34, offer_age > 0.3 and held(x, 0.56, 0.15) / 0.8 or 0)
    end
  end
end
";

        // ------------------------------------------------------------------ 8. Glass Keeper (through the glass: the user's football idea)
        public const string Keeper = @"-- Glass Keeper: Nilo kicks balls OUT of the field at you - out of the screen when it plays inside the PC, up from
-- the desk otherwise. Stop them with an open hand: a save is a point. Five goals against you and it is over.
local GOALS, FLOOR = 5, 0.06
local wait = 3.5              -- seconds until the next kick (first a moment to get ready)
local swing = -1              -- >= 0 while Nilo winds up: seconds since it began
local spot = W / 2            -- where the ball lies
local ball = nil              -- the ball on its way to the glass: {x1, y1, t}
local flying = {}             -- balls in the room: flying[id] = {x, y, age}, where each left the field
local marks = {}              -- what became of them: {x, y, age, what}
local saves, speed = 0, 0.6   -- speed: metres a second through the room
local over = false

motion('kick', 0.7, function(u, p)        -- lean back, swing the right leg through, follow on
  local back = keys(u, {0, 0, 0.4, 1, 0.6, -0.6, 1, 0})
  p.base = 'ready'
  p.spine = {x = -12 * back}
  p.legR = {x = 40 * back}
  p.shinR = {x = 50 * math.max(0, back)}
  p.armL = {x = -30 * back, z = 20}
  p.armR = {x = 30 * back, z = 20}
  p.lift = 0.06 * pulse(clamp((u - 0.4) * 3, 0, 1))
end)

function start()
  lives(GOALS)
  pet_goto(spot - 0.12)
  pet_say(inside() and 'I kick it out of the screen!' or 'I kick it up at you!', 2.5)
end

function update(dt)
  local px = pet_at()
  if not over then
    -- 1. the wind-up: the player sees the kick coming
    if swing < 0 and not ball and next(flying) == nil and #marks == 0 then
      wait = wait - dt
      if wait <= 0 and math.abs(px - (spot - 0.12)) < 0.02 then
        swing = 0  pet_face(spot)  pet_play('kick')  sound('step')
      end
    elseif swing >= 0 then
      swing = swing + dt
      if swing >= 0.4 then                 -- the foot meets the ball: it flies to a place on the glass
        ball = {x1 = rnd(0.25, W - 0.25), y1 = rnd(0.35, 0.85), t = 0}
        swing = -1  sound('hit')
      end
    end
    -- 2. the ball crosses the field and LEAVES it
    if ball then
      ball.t = ball.t + dt / 0.35
      if ball.t >= 1 then
        local id = shoot_out(ball.x1, ball.y1, 'football', speed, 0.09)
        if id > 0 then flying[id] = {x = ball.x1, y = ball.y1, age = 0} end
        ball = nil  sound('whoosh')
        wait = math.max(1.6, 3.4 - 0.25 * level())
        spot = rnd(0.3, W - 0.3)
        pet_goto(spot - 0.12, 0, 0.9)
      end
    end
    -- 3. what became of it in the room: blocked by the hand, a goal, or wide
    for _, e in ipairs(out_events()) do
      local f = flying[e.id]
      if f then
        flying[e.id] = nil
        marks[#marks + 1] = {x = f.x, y = f.y, age = 0, what = e.what}
        if e.what == 'blocked' then
          add(1)  saves = saves + 1  sound('bounce')  pet_play('hurt', 0.6)
          if saves % 4 == 0 then
            level(level() + 1)  speed = math.min(1.3, speed + 0.1)
            banner('Level ' .. level() .. ' - harder shots!', 1.5)
          end
        elseif e.what == 'hit' then
          lives(lives() - 1)  sound('error')  pet_play('cheer', 1)  banner('Goal for Nilo!', 1.2)
          if lives() <= 0 then
            over = true  sound('lose')  pet_pose('cheer')
            finish('You saved ' .. score() .. ' shots!')
          end
        else
          sound('drop')
        end
      end
    end
  end

  -- A floodlit pitch seen from the near goal. The box and turf stripes widen towards your feet.
  local cx=W/2
  rect(cx,0.5,W,1,'#132844')
  rect(cx,0.79,W,0.21,'#244760')
  for i=0,3 do rect(cx,0.70+i*0.048,W,0.024,i%2==0 and '#345873' or '#40627A') end
  for i=0,27 do
    local x=(i+0.5)*W/28; local y=0.71+0.07*((i*7)%5)/4
    rect(x,y,0.028,0.013,i%3==0 and '#F5AE71' or '#AAC8D1',0.88)
  end
  for i=0,1 do
    local x=(0.17+i*0.66)*W
    line(x,0.80,x,0.935,0.009,'#7090A1')
    rect(x,0.935,0.16,0.035,'#4D6E7E')
    for j=-2,2 do disc(x+j*0.028,0.935,0.009,'#FFF6BF') end
  end
  rect(cx,0.335,W,0.67,'#247158')
  local edge=0.66
  for i=1,7 do
    local nextEdge=0.66*(1-(i/7)^1.5)
    if i%2==0 then rect(cx,(edge+nextEdge)/2,W,edge-nextEdge,'#33866A') end
    edge=nextEdge
  end
  line(0,0.675,W,0.675,0.014,'#F6DB91')
  line(W*0.24,0.65,W*0.015,0.02,0.006,'#D1EADA')
  line(W*0.76,0.65,W*0.985,0.02,0.006,'#D1EADA')
  line(W*0.22,0.035,W*0.32,0.32,0.007,'#E0F2E2')
  line(W*0.32,0.32,W*0.68,0.32,0.007,'#E0F2E2')
  line(W*0.68,0.32,W*0.78,0.035,0.007,'#E0F2E2')
  for i=0,11 do
    local a,b=math.pi*i/12,math.pi*(i+1)/12
    line(cx+0.16*math.cos(a),0.32+0.05*math.sin(a),cx+0.16*math.cos(b),0.32+0.05*math.sin(b),0.005,'#E0F2E2')
  end
  disc(cx,0.22,0.009,'#E0F2E2')
  -- The near net stays around the edges, away from a hand or a ball at the centre.
  for i=0,1 do
    local side=i==0 and 1 or -1; local x=i==0 and 0.035 or W-0.035
    for j=1,6 do line(x,j*0.135,x+side*0.080,0.095+j*0.106,0.003,'#B9D8D5',0.67) end
    line(x+side*0.080,0.095,x+side*0.080,0.825,0.006,'#A0B5B7')
    line(x,0.025,x,0.905,0.012,'#EDF6EB')
  end
  line(0.115,0.825,W-0.115,0.825,0.006,'#B9CECB')
  for j=1,13 do line(W*j/14,0.905,0.095+(W-0.19)*j/14,0.825,0.003,'#D1E5DA',0.65) end
  line(0.03,0.905,W-0.03,0.905,0.014,'#F6FFF2')
  if ball then
    local k = ease(ball.t)                 -- it grows: it comes towards the glass
    local x,y=lerp(spot,ball.x1,k),lerp(FLOOR+0.04,ball.y1,k)
    line(lerp(spot,x,0.55),lerp(FLOOR+0.04,y,0.55),x,y,0.012,'#D0EABD',0.57)
    pic('football',x,y,0.08+0.14*k,k*160)
  elseif not over and next(flying) == nil and #marks == 0 then
    pic('football', spot, FLOOR + 0.04, 0.08)
  end
  for id, f in pairs(flying) do            -- a ring spreads where it went through
    f.age = f.age + dt
    hoop(f.x, f.y, 0.08 + 0.12 * f.age, 'ink', clamp(0.6 - 0.4 * f.age, 0.1, 0.6))
  end
  for i = #marks, 1, -1 do
    local m = marks[i]
    m.age = m.age + dt
    if m.age > 0.9 then table.remove(marks, i)
    elseif m.what == 'blocked' then
      local k = clamp(m.age / 0.70, 0, 1)
      if k < 1 then pic('football', lerp(m.x,spot,k), lerp(m.y,FLOOR+0.04,k)+0.13*math.sin(k*math.pi), lerp(0.22,0.08,k), k*300) end
      pic('sparkle', m.x, m.y, 0.12 + 0.1 * m.age, 0, 1 - m.age / 0.9)
      text('Saved!', m.x, m.y + 0.1 + 0.05 * m.age, 0.07, 'green', 1 - m.age / 0.9)
    elseif m.what == 'hit' then
      pic('splash', m.x, m.y, 0.14 + 0.1 * m.age, 0, 1 - m.age / 0.9)
    else
      text('Wide!', m.x, m.y + 0.05 * m.age, 0.06, 'brown', 1 - m.age / 0.9)
    end
  end
end
";

        // ------------------------------------------------------------------ 9. Clone Shooter (aiming, firing and being shot at: the user's shooting idea)
        public const string Shooter = @"-- Clone Shooter: little clones of Nilo pop up around the field. Point at one and pinch to pop it (a tap works too).
-- The real Nilo shoots back, out of the field at you: hold an open hand in the way. Three hits and it is over.
local clones = {}             -- each {x, y, age, stay, tint}
local pops = {}               -- short effects: {pic, x, y, age}
local streak, last_hit, trails = 0, -10, {}
local bolts = {}              -- Nilo's shots in the room: bolts[id] = {x, y, age}
local next_clone = 1.0        -- seconds until the next clone
local next_bolt = 6.5         -- seconds until Nilo shoots (not at once: first the player learns to aim)
local charge = -1             -- >= 0 while Nilo winds up for a shot
local popped, clock = 0, 0
local over = false
local FLOOR, R = 0.05, 0.065  -- the floor; how big a clone is
local TINTS = {'#FD72A0','#64E8F2','#BDA1FF','#FFD175'}

motion('fling', 0.8, function(u, p)       -- the arm goes back, then whips forward
  local back = keys(u, {0, 0, 0.6, 1, 0.75, -1, 1, 0})
  p.armR = {x = 40 * back, z = 30}
  p.foreR = {x = -40 * math.max(0, back)}
  p.spine = {y = 15 * back}
  p.squash = 0.15 * math.max(0, back)
end)

local function effect(name, x, y) pops[#pops + 1] = {pic = name, x = x, y = y, age = 0} end

local function pop_clone(c)
  streak = clock-last_hit < 1.7 and math.min(5,streak+1) or 1
  last_hit = clock; popped = popped + 1
  add(streak); sound('coin'); effect('sparkle', c.x, c.y)
  if popped % 8 == 0 then level(level() + 1); banner('Level ' .. level() .. ' - faster!', 1.5); sound('sparkle') end
end

local function clone(c, k)                -- a little Nilo: a round head with two ears and two eyes; k = how far it is out
  local r = R * k
  local flame=(0.48+0.11*math.sin(clock*8+c.stay))*r
  rect(c.x,c.y-r*0.86,0.53*r,flame*2,'#FFAE78',0.90,45)
  hoop(c.x,c.y,r*1.33,c.tint,0.48)
  disc(c.x-r*0.72,c.y+r*0.76,r*0.41,c.tint)
  disc(c.x+r*0.72,c.y+r*0.76,r*0.41,c.tint)
  disc(c.x,c.y,r,c.tint)
  rect(c.x,c.y-r*0.03,r*1.58,r*0.57,'#25264C')
  for side=-1,1,2 do rect(c.x+side*r*0.38,c.y+r*0.01,r*0.24,r*0.115,'#F4FFFF') end
end

function start()
  lives(3)
  for i=1,3 do
    clones[i]={x=W*(0.17+0.24*(i-1)),y=0.49+(i%2)*0.19,vx=(i%2==0 and -0.15 or 0.15),age=0.28,stay=3+i*0.5,tint=TINTS[i]}
  end
  pet_say('Catch my clones if you can!', 2.5)
end

local function night_city()
  rect(W/2,0.5,W,1,'#151834')
  local colors={'#241F48','#302650','#432B59','#5F3763','#80486D'}
  for i=1,5 do rect(W/2,0.81-i*0.095,W,0.098,colors[i]) end
  local sunX,sunY=W*0.74,0.59
  disc(sunX,sunY,0.162,'#F77E83',0.27)
  disc(sunX,sunY,0.143,'#FFC388')
  for i=1,5 do
    local y=-0.011-i*0.020
    rect(sunX,sunY+y,2*math.sqrt(0.143^2-y^2),0.004+i*0.001,'#9F516D')
  end
  for i=1,8 do
    local x=((i*0.117+clock*0.0015)%1)*W; local y=0.66+(i*0.143)%0.26
    disc(x,y,0.0025+(i%3)*0.0012,'#CAF4FF',0.86)
  end
  local street=0.29
  for i=1,10 do
    local x=W*(i-0.5)/10; local h=0.08+((i*7)%8)*0.022
    rect(x,street+h*0.5,W*0.094,h,'#393753')
  end
  for i=1,7 do
    local x=W*(i-0.60)/7; local h=0.10+((i*3)%7)*0.032
    rect(x,street+h/2,W*0.085,h,'#222641')
    rect(x,street+h,W*0.085,0.005,i%2==0 and '#61CAD5' or '#DA82A0')
    for j=1,2 do rect(x+W*0.022,street+h*(0.3+j*0.19),W*0.019,0.007,'#E9BC91',0.88) end
  end
  for i=0,1 do
    local x=W*(0.20+0.57*i); local roof=street+(i==0 and 0.292 or 0.228)
    line(x,roof,x,roof+0.080,0.005,'#8393AA')
    disc(x,roof+0.080,0.009,'#FF8B9D')
  end
  local shuttle=W*((0.18+clock*0.046)%1)
  line(shuttle-0.085,0.76,shuttle,0.76,0.004,'#62BFCF',0.70)
  rect(shuttle,0.765,0.034,0.010,'#CFEDF1')
  rect(W/2,street/2,W,street,'#20213E')
  line(0,street,W,street,0.006,'#E891B8')
  for i=-4,4 do line(W/2+i*0.10,street,W/2+i*0.41,0.012,0.003,'#68528C') end
  for i=1,4 do local y=street*(1-(i/4)^1.6); line(0,y,W,y,0.003,'#805C96') end
end

function update(dt)
  clock = clock + dt
  local px = pet_at()
  night_city()
  if not over then
    -- the clones come, and go again when nobody pops them
    next_clone = next_clone - dt
    if next_clone <= 0 and #clones < 5 then
      clones[#clones + 1] = {x = rnd(0.15, W - 0.15), y = rnd(0.35, 0.82), vx = rnd() < 0.5 and -0.18 or 0.18, age = 0,
                             stay = math.max(1.6, 3.6 - 0.25 * level()), tint = TINTS[rndi(1, #TINTS)]}
      next_clone = math.max(0.45,1.20-0.1*level())*rnd(0.7,1.2)
      sound('pop')
    end
    for i = #clones, 1, -1 do
      local c = clones[i]
      c.age = c.age + dt
      c.x = c.x + c.vx * (1 + 0.08 * level()) * dt
      if c.x < 0.10 or c.x > W-0.10 then c.vx = -c.vx c.x = clamp(c.x,0.10,W-0.10) end
      c.y = c.y + dt * 0.11 * math.sin(clock*4+c.stay)
      if c.age > c.stay then table.remove(clones, i)  effect('puff', c.x, c.y) end
    end
    if #clones < 2 then next_clone=math.min(next_clone,0.19) end
    -- the player's shots: each pops the clone it lands on
    for _, s in ipairs(shots()) do
      effect('splash', s.x, s.y)
      trails[#trails+1] = {x=s.x,y=s.y,ox=W/2,oy=0,age=0,color='mint'}
      for i = #clones, 1, -1 do
        local c = clones[i]
        if hit_at(s.x, s.y, c.x, c.y, R + 0.035) then
          table.remove(clones, i)
          pop_clone(c)
          break
        end
      end
    end
    if not inside() and clock-last_hit > 0.20 then
      for _,h in ipairs(hands()) do
        for i=#clones,1,-1 do
          local c=clones[i]
          if c.age>0.14 and hit_at(h.x,h.y,c.x,c.y,R+0.025) then table.remove(clones,i); pop_clone(c); break end
        end
      end
    end
    -- Nilo shoots back: it winds up (the shot grows in its hand), then the shot LEAVES the field
    if charge < 0 then
      next_bolt = next_bolt - dt
      if next_bolt <= 0 then charge = 0  pet_goto(px)  pet_play('fling')  pet_say('Take this!', 0.8) end
    else
      charge = charge + dt
      if charge >= 0.6 then
        local id = shoot_out(px, 0.2, 'shot', 0.7, 0.07)
        if id > 0 then bolts[id] = {x = px, y = 0.2, age = 0} end
        charge = -1  sound('whoosh')
        next_bolt = math.max(2.2, 4.4 - 0.3 * level()) * rnd(0.85, 1.15)
        pet_goto(rnd(0.2, W - 0.2), 0, 0.7)
      end
    end
    for _, e in ipairs(out_events()) do     -- what became of its shots in the room
      local b = bolts[e.id]
      if b then
        bolts[e.id] = nil
        if e.what == 'blocked' then
          sound('bounce')  effect('sparkle', b.x, b.y)
          local best, gap = nil, W+2
          for i,c in ipairs(clones) do
            local d=dist(b.x,b.y,c.x,c.y)
            if d<gap then best,gap=i,d end
          end
          local target = best and table.remove(clones,best)
          local x, y = target and target.x or px, target and target.y or 0.15
          trails[#trails+1] = {ox=b.x,oy=b.y,x=x,y=y,age=0,color='honey'}
          effect('sparkle',x,y) add(2) banner('Parry +2',0.8)
        elseif e.what == 'hit' then
          lives(lives() - 1)  sound('error')  banner('Ouch! Block with an open hand.', 1.5)
          if lives() <= 0 then
            over = true  sound('lose')  pet_pose('cheer')
            finish('You popped ' .. popped .. ' clones!')
          end
        end
      end
    end
  end

  -- draw: the floor, the clones, the shot that is coming, the rings where shots went out, the effects, the crosshair
  line(0,FLOOR,W,FLOOR,0.006,'#50CEDE',0.82)
  for _, c in ipairs(clones) do
    clone(c, math.min(1, c.age * 6) * (c.stay - c.age < 0.4 and 0.8 + 0.2 * wave(clock * 4) or 1))
  end
  if clock-last_hit < 1.5 and streak>1 then text('x'..streak,W/2,0.83,0.058,'#FFE29B') end
  for i=#trails,1,-1 do
    local t=trails[i]; t.age=t.age+dt
    if t.age>0.24 then table.remove(trails,i)
    else
      local a=1-t.age/0.24
      line(t.ox,t.oy,t.x,t.y,0.022*a+0.003,t.color,0.68*a)
      line(t.ox,t.oy,t.x,t.y,0.005,'white',a)
      hoop(t.x,t.y,0.03+0.22*t.age,t.color,a)
    end
  end
  if charge >= 0 then hoop(px,0.2,0.14-0.12*charge,'honey') end
  if charge >= 0 then pic('shot', px, 0.2, 0.05 + 0.15 * charge, clock * 300) end
  for id, b in pairs(bolts) do
    b.age = b.age + dt
    hoop(b.x, b.y, 0.06 + 0.12 * b.age, 'red', clamp(0.6 - 0.4 * b.age, 0.1, 0.6))
  end
  for i = #pops, 1, -1 do
    local p = pops[i]
    p.age = p.age + dt
    if p.age > 0.4 then table.remove(pops, i)
    else pic(p.pic, p.x, p.y, 0.07 + 0.15 * p.age, 0, 1 - p.age / 0.4) end
  end
  local ax, ay = aim()
  if ax then
    hoop(ax,ay,0.037,'#81FFFF',0.96)
    for side=-1,1,2 do
      line(ax+side*0.047,ay,ax+side*0.063,ay,0.004,'#FFF1A8')
      line(ax,ay+side*0.047,ax,ay+side*0.063,0.004,'#FFF1A8')
    end
  end
end
";

        // ------------------------------------------------------------------ 10. a calculator (a TOOL: upright, panel + display + buttons)
        public const string Calculator = @"-- Calculator: a real one, in the app's own look. Tap the keys (through a screen: rest a hand on a key, or point at
-- it and pinch). It chains from left to right like a pocket calculator, and Nilo reads out the result.
local KEYS = {                -- from the top row down
  {'C', '<', '%', '/'},
  {'7', '8', '9', 'x'},
  {'4', '5', '6', '-'},
  {'1', '2', '3', '+'},
  {'0', '.', '+/-', '='},
}
local TINT = {C = 'pink', ['<'] = 'pink', ['%'] = 'sky', ['/'] = 'sky', x = 'sky', ['-'] = 'sky', ['+'] = 'sky', ['='] = 'honey'}
local CX = W / 2 + 0.22       -- the middle of the calculator (Nilo stands on its left)
local g = grid(4, 5, CX, 0.355, 1.04, 0.67)
local shown = '0'             -- what the display shows
local acc, op = nil, nil      -- the result so far, and the operation that waits for its second number
local fresh = true            -- the next digit begins a new number
local broken = false          -- it shows an error: the next key clears it

local function write(v)       -- put a number on the display
  if v ~= v or v >= 1e15 or v <= -1e15 then
    shown, acc, op, fresh, broken = 'Error', nil, nil, true, true
  else
    shown = string.format('%.12g', v)
  end
end

local function work()         -- do the operation that waits: acc (op) the number shown
  local b = tonumber(shown) or 0
  if not op or acc == nil then acc = b
  elseif op == '+' then acc = acc + b
  elseif op == '-' then acc = acc - b
  elseif op == 'x' then acc = acc * b
  elseif b == 0 then acc = 0 / 0            -- nothing can be divided by zero: an error
  else acc = acc / b end
  write(acc)
end

local function press(k)
  if broken or k == 'C' then
    shown, acc, op, fresh, broken = '0', nil, nil, true, false
    if k == 'C' then return end
  end
  if #k == 1 and k >= '0' and k <= '9' then  -- a digit
    if fresh or shown == '0' then shown = k elseif #shown < 12 then shown = shown .. k end
    fresh = false
  elseif k == '.' then
    if fresh then shown = '0.' elseif not string.find(shown, '.') then shown = shown .. '.' end
    fresh = false
  elseif k == '<' then                       -- rub out the last digit
    if not fresh then shown = string.sub(shown, 1, #shown - 1) end
    if shown == '' or shown == '-' then shown = '0' end
  elseif k == '+/-' then
    if tonumber(shown) ~= 0 then write(-(tonumber(shown) or 0)) end
  elseif k == '%' then
    write((tonumber(shown) or 0) / 100)
  elseif k == '=' then
    work()
    if broken then pet_say('That does not work!', 2)  sound('error')
    else pet_say('It is ' .. shown .. '!', 3)  pet_play('cheer')  sound('ta-da')  add(1) end
    acc, op, fresh = nil, nil, true
  else                                       -- + - x /
    if not fresh or acc == nil then work() end      -- 2 + 3 + : the first sum is done now
    if not broken then op, fresh = k, true end
  end
end

function start()
  pet_goto(0.2)
  pet_say('What shall we work out?', 2.5)
end

function update(dt)
  panel(CX, 0.5, 1.12, 0.98, 'Calculator')
  display(shown, CX, 0.775, 1.02, 0.13)
  for r = 1, 5 do
    for c = 1, 4 do
      local k = KEYS[r][c]
      local x, y = g:center(c, 6 - r)        -- the grid counts its rows from the bottom
      if button(k, x, y, g.cw - 0.02, g.ch - 0.014, TINT[k]) then press(k) end
    end
  end
end
";

        // ------------------------------------------------------------------ 11. Rock Paper Scissors (what the hand SHOWS: all_hands, shape)
        public const string Rps = @"-- Rock Paper Scissors: Nilo counts - rock, paper, scissors, shoot! - and on 'shoot' both show a hand at once.
-- Hold a hand up where it can be seen, anywhere: a fist, an open hand or two fingers. First to three wins.
local SIGNS = {'rock', 'paper', 'scissors'}
local BEAT = 0.6                  -- seconds from one word of the count to the next
local state = 'count'             -- count -> read -> show -> count ... and at the end: done
local t = -2                      -- seconds in this state (the very first count waits a moment longer)
local mine = 1                    -- Nilo's sign in this round: 1 rock, 2 paper, 3 scissors
local yours = 0                   -- the player's, as far as it has been read (0 = no hand seen yet)
local votes = {0, 0, 0}           -- how often the hand showed each sign while it was read
local bambi = 0                   -- rounds Nilo has won (the player's are the score)
local word, beat_was = 'First to three!', -9
local UX, BX, Y = W * 0.3, W * 0.72, 0.46

motion('count', BEAT, function(u, p)        -- its fist comes down with every word
  local d = pulse(u)
  p.armR = {x = -70 + 40 * d, z = 8}
  p.foreR = {x = -50 + 20 * d}
  p.spine = {x = 5 * d}
end, true)

motion('show', 0.4, function(u, p)          -- the hand held out
  p.armR = {x = -82, z = 10}
  p.foreR = {x = -8}
  p.head = {x = -6}
end)

local function new_round()
  -- NO CHEATING: Nilo chooses now, before the count - long before it gets to see the player's hand.
  mine = rndi(1, 3)
  yours = 0  votes = {0, 0, 0}
  state = 'count'  beat_was = -9
  pet_pose('count')
end

function start()
  lives(3)                        -- the hearts: the rounds the player can still lose
  pet_goto(BX)
  new_round()
end

-- 1 when the sign a beats b (paper wraps rock, scissors cut paper, rock blunts scissors), 2 when b beats a, 0 for a draw
local function winner(a, b)
  if a == b then return 0 end
  return (a - b) % 3 == 1 and 1 or 2
end

function update(dt)
  t = t + dt
  if state == 'count' then
    local beat = math.floor(t / BEAT)
    if beat ~= beat_was and beat >= 1 then sound('tick') end
    beat_was = beat
    if beat >= 1 then word = beat == 1 and 'Rock ...' or (beat == 2 and 'Paper ...' or 'Scissors ...') end
    if beat >= 4 then
      state = 'read'  t = 0  word = 'Shoot!'
      sound('pop')  pet_pose('show')
    end
  elseif state == 'read' then
    -- the hand is read for a moment - the HIGHEST hand that is seen (the one held up; the other may lie on the desk) -
    -- and what it shows most often counts
    if t > 0.1 then
      local top = nil
      for _, h in ipairs(all_hands()) do
        if not top or h.y > top.y then top = h end
      end
      if top then
        local s = top.shape == 'point' and 'scissors' or top.shape     -- one finger out: the second one was missed
        for i = 1, 3 do
          if SIGNS[i] == s then votes[i] = votes[i] + 1 end
        end
      end
    end
    for i = 1, 3 do
      if votes[i] > 0 and (yours == 0 or votes[i] > votes[yours]) then yours = i end
    end
    if t >= 0.8 then
      state = 'show'  t = 0
      local w = yours > 0 and winner(yours, mine) or -1
      if w < 0 then word = 'Show me your hand!'  sound('error')
      elseif w == 1 then add(1)  word = 'You win this one!'  sound('coin')
      elseif w == 2 then bambi = bambi + 1  lives(3 - bambi)  word = 'Nilo wins this one!'  sound('drop')  pet_pose('cheer')
      else word = 'A draw!'  sound('tap') end
    end
  elseif state == 'show' and t >= 2 then
    if score() >= 3 or bambi >= 3 then
      state = 'done'  pet_pose('cheer')  sound(score() >= 3 and 'win' or 'lose')
      word = score() >= 3 and 'You win the match!' or 'Nilo wins the match!'
      finish(word .. ' ' .. score() .. ' : ' .. bambi)
    else
      t = 0  word = 'Ready ...'
      new_round()                 -- a round nobody could read is simply played again, with a new choice
    end
  end

  -- draw: the word of the moment, the score, and the two hands side by side
  text(word, W / 2, 0.82, 0.09)                -- (the top tenth of the field is left free: the frame's sign may lie over it)
  text('You  ' .. score() .. ' : ' .. bambi .. '  Nilo', W / 2, 0.71, 0.055, 'brown')
  if state == 'count' then
    local drop = -0.05 * math.abs(math.sin(math.max(0, t) / BEAT * math.pi))   -- both fists come down with the count
    pic('rock', UX, Y + drop, 0.26)
    pic('rock', BX, Y + drop, 0.26, 0, 1, true)
  else
    local w = (state ~= 'read' and yours > 0) and winner(yours, mine) or 0   -- the winning hand grows, the other fades
    local grow = 1 + 0.2 * clamp(t / 0.3, 0, 1)
    pic(SIGNS[mine], BX, Y, 0.3 * (w == 2 and grow or 1), 0, w == 1 and 0.5 or 1, true)
    if yours > 0 then pic(SIGNS[yours], UX, Y, 0.3 * (w == 1 and grow or 1), 0, w == 2 and 0.5 or 1)
    else text('?', UX, Y, 0.2, 'brown', 0.6) end
  end
  text('You', UX, 0.23, 0.05, 'brown')
end
";

        // ------------------------------------------------------------------ 12. Star Pop (a score chase: what a hand touches)
        public const string Pop = @"-- Star Pop: stars rise slowly from the floor and drift. Touch one with a hand and it pops: a point - three for a
-- golden one. Forty seconds: a gentle start, then they come faster. A star that gets away costs nothing.
local ROUND = 40              -- seconds
local stars = {}              -- each {x, y, vy, sway, gold, age}
local pops = {}               -- where one was popped: {x, y, age, gold}
local next_star = 1.0         -- seconds until the next one
local made = 0                -- stars so far: every sixth is golden
local over = false

function start()
  pet_say('Pop the stars!', 2)
end

function update(dt)
  local left = math.max(0, ROUND - time())
  local pace = 1 - left / ROUND                 -- 0 at the start, 1 at the end
  if not over then
    -- 1. a new star now and then: few and slow at first, more and faster towards the end
    next_star = next_star - dt
    if next_star <= 0 then
      made = made + 1
      local gold = made % 6 == 0
      stars[#stars + 1] = {x = rnd(0.14, W - 0.14), y = -0.05, sway = rnd(0, 6.28), gold = gold, age = 0,
                           vy = lerp(0.14, 0.26, pace) * rnd(0.9, 1.1) * (gold and 1.15 or 1)}
      next_star = lerp(1.1, 0.42, pace)
      pet_goto(stars[#stars].x, 0, 0.9)         -- Nilo runs to where it comes up
    end
    -- 2. they rise and drift; a hand that touches one pops it
    local hs = hands()
    for i = #stars, 1, -1 do
      local s = stars[i]
      s.age = s.age + dt
      s.y = s.y + s.vy * dt
      s.x = clamp(s.x + math.sin(s.age * 1.7 + s.sway) * 0.07 * dt, 0.08, W - 0.08)
      local popped = false
      for _, h in ipairs(hs) do
        if dist(h.x, h.y, s.x, s.y) < h.r + (s.gold and 0.07 or 0.055) then popped = true end
      end
      if popped then
        table.remove(stars, i)
        add(s.gold and 3 or 1)
        sound(s.gold and 'sparkle' or 'pop')
        pops[#pops + 1] = {x = s.x, y = s.y, age = 0, gold = s.gold}
        if s.gold then pet_play('cheer', 0.8) end
      elseif s.y > 1.08 then
        table.remove(stars, i)                  -- it got away: that costs nothing
      end
    end
    -- 3. time is up
    if left <= 0 then
      over = true
      for _, s in ipairs(stars) do pops[#pops + 1] = {x = s.x, y = s.y, age = 0} end
      stars = {}
      sound('win')  pet_pose('cheer')
      finish('Time! You popped ' .. score() .. ' points.')
    end
  end

  -- draw: the floor, the time that is left as a bar, the stars, the pops, a ring on each hand
  rect(W / 2, 0.02, W, 0.04, 'mint')
  local full = W - 0.5
  rect(W / 2, 0.87, full + 0.012, 0.034, 'brown')
  rect(W / 2, 0.87, full, 0.022, 'cream')
  if left > 0.2 then
    rect(W / 2 - full / 2 + full * left / ROUND / 2, 0.87, full * left / ROUND, 0.022, left < 8 and 'pink' or 'honey')
  end
  for _, s in ipairs(stars) do
    local turn = math.sin(s.age * 3 + s.sway) * 14
    if s.gold then
      disc(s.x, s.y, 0.09, 'honey', 0.4)        -- a golden one: bigger, with a glow and a glint
      pic('star', s.x, s.y, 0.15, turn)
      pic('sparkle', s.x + 0.05, s.y + 0.05, 0.05, s.age * 90)
    else
      pic('star', s.x, s.y, 0.11, turn)
    end
  end
  for i = #pops, 1, -1 do
    local p = pops[i]
    p.age = p.age + dt
    if p.age > 0.5 then table.remove(pops, i)
    else
      pic('sparkle', p.x, p.y, 0.1 + 0.3 * p.age, p.age * 200, 1 - p.age * 2)
      text(p.gold and '+3' or '+1', p.x, p.y + 0.07 + 0.12 * p.age, p.gold and 0.08 or 0.06, 'brown', 1 - p.age * 2)
    end
  end
  for _, h in ipairs(hands()) do hoop(h.x, h.y, h.r, 'ink', 0.3) end
end
";

        // ------------------------------------------------------------------ 13. Red Light (how STILL a hand can be: all_hands, speed)
        public const string RedLight = @"-- Red Light, Green Light: Nilo turns its back and counts. While it counts, lead the star along the track with a
-- hand - it only creeps. When Nilo spins round: FREEZE. A hand that still moves is caught and the star goes
-- back to the start. Bring the star to Nilo and you win; three catches and Nilo wins.
local START, TRACK, CREEP = 0.3, 0.14, 0.42
local GOAL = W - 0.48             -- the star has arrived here: right beside Nilo
local FAST, GRACE, TOO_LONG = 0.10, 0.4, 0.2   -- metres a second that count as moving; seconds to stop; seconds of moving that are caught
local CHANT = 'Green light ... one ... two ... three ...'
local star = START                -- how far the star has got
local state = 'ready'             -- ready -> green <-> red -> caught -> ready ... and at the end: over
local t, len = 0, 0               -- seconds in this state, and how long it lasts
local greens = 0                  -- green lights so far (they get shorter)
local moving, hold = 0, 0         -- seconds a hand has been moving on red / waiting on the star

motion('count', 1.2, function(u, p)       -- its back to the player, its hands over its eyes, nodding along
  p.hips = {y = 180}
  p.armL = {x = -72, z = -6}
  p.armR = {x = -72, z = -6}
  p.foreL = {x = -90}
  p.foreR = {x = -90}
  p.head = {x = 8 + 6 * wave(u)}
end, true)

motion('stare', 0.25, function(u, p)      -- it spins round and stares, arms out
  p.hips = {y = 180 * (1 - ease(u))}
  p.armL = {z = 35}
  p.armR = {z = 35}
  p.head = {x = -6}
  p.blink = -0.5
end)

function start()
  lives(3)
  pet_goto(W - 0.3)
end

local function go(to, seconds)
  state = to  t = 0  len = seconds or 0  moving = 0
end

local function green()
  greens = greens + 1
  -- (never the same length twice, and shorter as the game goes on: nobody can tell when it will turn)
  go('green', rnd(1.3, 2.2) * math.max(0.6, 1 - 0.06 * greens))
  pet_pose('count')  sound('tick')  banner('Green light - go!', len)
end

function update(dt)
  t = t + dt
  local hs = all_hands()                  -- a hand anywhere in view counts - but only ONE: the higher one (the other may rest on the desk)
  local lead = nil                        -- the hand that plays
  for _, h in ipairs(hs) do
    if not lead or h.y > lead.y then lead = h end
  end
  local fast = lead ~= nil and lead.speed > FAST      -- does it move?
  local on_star = lead ~= nil and math.abs(lead.x - star) < 0.22

  if state == 'ready' then                -- every try begins with a hand on the star, at the start line
    hold = on_star and hold + dt or 0
    if hold > 0.6 and t > 3.2 then green() end
  elseif state == 'green' then            -- its back is turned: the star follows the hand, creeping
    if on_star and lead.x > star then star = math.min(lead.x, star + CREEP * dt, GOAL) end
    if star >= GOAL then
      go('over')  sound('win')  pet_pose('hurt')
      finish('You got me!')
    elseif t >= len then
      go('red', rnd(1.2, 2.0))
      pet_pose('stare')  sound('whoosh')  banner('Red light! Freeze!', len)
    end
  elseif state == 'red' then              -- it looks: a moment to stop, then a hand that keeps moving is caught
    moving = (t > GRACE and fast) and moving + dt or 0
    if moving > TOO_LONG then
      lives(lives() - 1)
      go('caught', 1.6)
      sound('error')  pet_pose('cheer')  banner('You moved!', 1.5)
    elseif t >= len then green() end
  elseif state == 'caught' then           -- the star rolls back to the start
    star = lerp(star, START, math.min(1, dt * 5))
    if t >= len then
      if lives() <= 0 then
        go('over')  sound('lose')
        finish('Caught three times - Nilo wins!')
      else
        star = START  hold = 0
        go('ready')  pet_pose('idle')
      end
    end
  end

  -- draw: the track and its start line, the lamp, the star, a ring on every hand (red while it moves), a line of help
  line(START, TRACK - 0.07, GOAL, TRACK - 0.07, 0.014, 'brown', 0.5)
  line(START, TRACK - 0.1, START, TRACK + 0.1, 0.014, 'brown')
  local lamp = state == 'green' and 'green' or ((state == 'red' or state == 'caught') and 'red' or 'grey')
  disc(W / 2, 0.82, 0.055, lamp)
  hoop(W / 2, 0.82, 0.055, 'ink')
  pic('star', star, TRACK, 0.13, state == 'green' and math.sin(t * 7) * 12 or 0)
  for _, h in ipairs(hs) do hoop(h.x, h.y, 0.05, h.speed > FAST and 'red' or 'ink', 0.5) end
  local help = ''
  if state == 'ready' then
    help = on_star and 'Wait for the green light ...' or (lead and 'Put your hand on the star' or 'Hold a hand up where I can see it')
  elseif state == 'green' then
    help = string.sub(CHANT, 1, math.ceil(#CHANT * clamp(t / len, 0.3, 1)))
  elseif state == 'red' then
    help = 'Do not move!'
  elseif state == 'caught' then
    help = 'Back to the start!'
  else
    help = lives() > 0 and 'The star is home!' or 'Caught!'
  end
  if #help > 0 then text(help, W / 2, 0.48, 0.06, 'brown') end      -- (under the banner, which lies across the field higher up)
end
";

        public static readonly GlassScriptExample[] All =
        {
            new GlassScriptExample
            {
                id = "omok", title = "Omok", ship = false, flat = true, lua = Omok,
                requestKo = "오목 게임 만들어줘", requestEn = "Make me an omok game (five in a row) to play against you.",
                about = "Five in a row on a 9 x 9 board that lies on the desk; Nilo answers every stone with a real heuristic.",
                howTo = "Five in a row wins. Tap a square to put your stone there.",
            },
            new GlassScriptExample
            {
                id = "catch", title = "Star Catcher", ship = true, lua = Catch,
                requestKo = "떨어지는 별 받기 게임 만들어줘", requestEn = "Make a game where I catch falling stars.",
                about = "Stars fall, the basket follows the hand, three misses end it; it speeds up.",
                howTo = "Move your hand under the stars to catch them.",
            },
            new GlassScriptExample
            {
                id = "memory", title = "Pairs", ship = true, flat = true, lua = Memory,
                requestKo = "카드 짝 맞추기 게임 만들어줘", requestEn = "Make a memory game where I match pairs of cards.",
                about = "Twelve cards on the desk; tap two, pairs stay open.",
                howTo = "Tap two cards. Find all the pairs.",
            },
            new GlassScriptExample
            {
                id = "pomodoro", title = "Focus Timer", ship = true, lua = Pomodoro,
                requestKo = "뽀모도로 타이머 만들어줘", requestEn = "Make me a pomodoro timer.",
                about = "Not a game: 25 minutes of focus and 5 of rest with a ring that fills; Nilo studies along (a motion of its own).",
                howTo = "Tap the ring to start or pause.",
            },
            new GlassScriptExample
            {
                id = "tag", title = "Tag with Nilo", ship = true, flat = true, lua = Tag,
                requestKo = "밤비랑 술래잡기 하고 싶어", requestEn = "I want to play tag with Nilo.",
                about = "Nilo runs about on the desk and flees from the hand; touch it to catch it, thirty seconds.",
                howTo = "Touch Nilo to catch it!",
            },
            new GlassScriptExample
            {
                id = "umbrella", title = "Shadow Umbrella", ship = false, lua = Umbrella,
                requestKo = "잉크 비를 손으로 막아주는 게임 만들어줘", requestEn = "Make a game where my hand is an umbrella that keeps ink rain off you.",
                about = "Nilo gathers stars while ink rains; the hand shelters it, golden drops must be let through; wind from level 3.",
                howTo = "Your hand is the umbrella. Let the golden drops through!",
            },
            new GlassScriptExample
            {
                id = "survival", title = "Vampire Night", ship = true, flat = true, lua = Survival,
                requestKo = "몰려오는 적을 물리치는 뱀파이어 서바이벌 게임 만들어줘", requestEn = "Make a survival game where enemies keep coming and you fight them off.",
                about = "Waves of enemies; Nilo shoots by itself and follows the hand, the hand bops; gems give levels and a choice of seven powers on cards.",
                howTo = "Dodge the horde. Gather gems, choose powers and parry crystals with your palm!",
            },
            // The three below are NOT for the shelf either: they are what the user asked the partner for, and it has to make them.
            new GlassScriptExample
            {
                id = "keeper", title = "Glass Keeper", ship = false, lua = Keeper,
                requestKo = "축구 게임 만들어줘. 네가 화면 안에서 차고 내가 막을게", requestEn = "Make a football game: you kick from inside the screen and I block.",
                about = "THROUGH THE GLASS: Nilo winds up and kicks balls out of the field at the player (shoot_out); out_events says saved, goal or wide. Five goals end it.",
                howTo = "Nilo kicks at YOU. Block the ball with an open hand!",
            },
            new GlassScriptExample
            {
                id = "shooter", title = "Clone Shooter", ship = false, lua = Shooter,
                requestKo = "슈팅 게임 만들어줘. 밤비가 나한테 쏘면 손으로 막고, 나는 밤비 분신들을 쏠게", requestEn = "Make a shooting game: Nilo shoots at me, I block with my hand and shoot its little clones.",
                about = "AIMING AND FIRING: the player points and pinches (aim, shots, hit_at) to pop clones; Nilo shoots back out of the field (shoot_out, out_events). Three hits end it.",
                howTo = "Point and pinch to pop the clones. Block Nilo's shots with an open hand!",
            },
            new GlassScriptExample
            {
                id = "calculator", title = "Calculator", ship = false, lua = Calculator,
                requestKo = "계산기 만들어줘", requestEn = "Make me a calculator.",
                about = "A TOOL in the app's look: upright, a panel, a display and a 4 x 5 grid of buttons; real chained arithmetic; Nilo says the result.",
                howTo = "Tap the keys. Nilo tells you the result.",
            },
            // The three games that used to be C# in the headset (MrPlay.cs), as the scripts they are now. They read what a
            // hand SHOWS and how fast it MOVES (all_hands: shape, speed) - a hand anywhere in view plays.
            new GlassScriptExample
            {
                id = "rps", title = "Rock Paper Scissors", ship = true, lua = Rps,
                requestKo = "가위바위보 하자. 진짜 손 모양으로", requestEn = "Let's play rock paper scissors with my real hand.",
                about = "WHAT THE HAND SHOWS: Nilo counts and chooses before it looks; all_hands().shape is read for a moment on 'Shoot!'; first to three.",
                howTo = "Show rock, paper or scissors with your hand on 'Shoot!'",
            },
            new GlassScriptExample
            {
                id = "pop", title = "Star Pop", ship = true, lua = Pop,
                requestKo = "떠오르는 별을 손으로 터뜨리는 게임 만들어줘", requestEn = "Make a game where I pop rising stars with my hands.",
                about = "A score chase without lives: stars rise and drift, a hand that touches one pops it, golden ones count three; forty seconds, faster and faster.",
                howTo = "Touch the stars to pop them. Golden ones count three!",
            },
            new GlassScriptExample
            {
                id = "redlight", title = "Red Light", ship = true, lua = RedLight,
                requestKo = "무궁화 꽃이 피었습니다 하자", requestEn = "Let's play red light, green light.",
                about = "HOW STILL A HAND IS: Nilo turns its back (a motion) and counts while the hand leads a star along; when it turns round, all_hands().speed over 0.15 for 0.2 s is caught.",
                howTo = "Lead the star to Nilo while it counts. Freeze when it turns round!",
            },
        };

        public static GlassScriptExample Find(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        /// <summary>A pack that plays the example.</summary>
        public static GamePackDef Pack(GlassScriptExample e)
        {
            var p = new GamePackDef();
            p.id = "script-" + e.id; p.template = GlassScript.TemplateName; p.title = e.title; p.howTo = e.howTo; p.script = e.lua;
            return p;
        }
    }
}
