# The pictures of the GLASS GAMES and of the shows inside the PC screen (Shared/Glass.cs), drawn the same way as the
# rest of the interface art: a web page of hand-written SVG, rendered by a headless browser with a transparent
# background and cut into textures. Called by build_ui_art.py (which owns the output folder); it can also be run by
# itself:  python tools/ui-art/glass_art.py
#
# Names: glass-<art>.png for every GlassArt name a game asks for (enemy, flyer, brute, gem, shot, orb, ring, bolt,
# puff, heart, drop, blob, gold, splash, star, sparkle, frost, card; the hand signs rock, paper-sign, scissors),
# glass-ab-<ability id>.png for the nine powers
# of the survival game, glass-hand.png / glass-hand-soft.png (the shadow of a hand: WHITE, tinted by the app; the
# right hand seen from behind, fingers up, the middle of the palm in the middle of the picture - mirror it for the
# left hand), and the show's props: glass-magnifier, glass-shimmer, glass-stall, glass-tag, glass-listed.
# Creatures and shots face / fly to the RIGHT (mirror them for the left); drops fall straight down.
import os, subprocess, tempfile, shutil
from PIL import Image

INK, CREAM = '#4A2F27', '#FFF8EA'
SCALE = 2
CUT = f'stroke="{CREAM}" stroke-width="4.4" stroke-linejoin="round" stroke-linecap="round" fill="{CREAM}"'
O = f'stroke="{INK}" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round"'


def sticker(shape_d, fill, extra='', cut=True, view='-1.5 -1.5 27 27'):
    """One closed shape as a sticker: a cream die-cut edge, the shape in its colour with the ink line, then details."""
    edge = f'<path d="{shape_d}" {CUT}/>' if cut else ''
    return f'<svg class="pic" viewBox="{view}">{edge}<path d="{shape_d}" fill="{fill}" {O}/>{extra}</svg>'


def eyes(x1, x2, y, r=1.45):
    return (f'<circle cx="{x1}" cy="{y}" r="{r}" fill="{INK}"/><circle cx="{x2}" cy="{y}" r="{r}" fill="{INK}"/>'
            f'<circle cx="{x1 + .5}" cy="{y - .5}" r="{r * .34}" fill="{CREAM}"/><circle cx="{x2 + .5}" cy="{y - .5}" r="{r * .34}" fill="{CREAM}"/>')


def hand_sign(parts, fill):
    """A hand that shows a sign, as a sticker. parts: rounded rectangles (x, y, w, h, r, rotation or ''), the palm among
    them; the cream die-cut edge runs round all of them together, then each is drawn in the colour with the ink line."""
    def rects(extra):
        return ''.join(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}"' + (f' transform="rotate({rot})"' if rot else '') + f' {extra}/>' for x, y, w, h, r, rot in parts)
    return f'<svg class="pic" viewBox="-1.5 -1.5 27 27">{rects(CUT)}' + rects(f'fill="{fill}" {O} stroke-width="1.4"') + '</svg>'


MONSTER_BODY = 'M5 9.6L3.6 4.2l4.6 2.6a8.2 8.2 0 0 1 7.6 0l4.6-2.6L19 9.6a8 8 0 1 1-14 0z'
STAR = 'M12 2.2l2.7 6 6.5.7-4.9 4.4 1.4 6.4-5.7-3.3-5.7 3.3 1.4-6.4-4.9-4.4 6.5-.7z'
HEART = 'M12 20.6C5.4 15.6 2.6 12.3 2.6 8.6a4.9 4.9 0 0 1 9.4-1.9 4.9 4.9 0 0 1 9.4 1.9c0 3.7-2.8 7-9.4 12z'
GEM = 'M7 3.6h10l4.4 5.6L12 21 2.6 9.2z'
DROP = 'M12 2.4C9 7.2 6 10.6 6 14.7a6 6 0 0 0 12 0c0-4.1-3-7.5-6-12.3z'
BLOB = 'M12 2.6c2 2.7 3.2 3.6 5.7 5 3 1.7 4.2 4.4 3.5 7.3-.8 3.5-4.3 5.7-9.2 5.7s-8.4-2.2-9.2-5.7c-.7-2.9.5-5.6 3.5-7.3 2.5-1.4 3.7-2.3 5.7-5z'
BOLT = 'M13.5 1.8L5 13.4h5.4L8.8 22.2 19 9.8h-5.6z'
MOON = 'M15.2 2.8a9.4 9.4 0 1 0 6 13.6A7.6 7.6 0 0 1 15.2 2.8z'
SPARK = 'M12 2.6l2 7.4 7.4 2-7.4 2-2 7.4-2-7.4-7.4-2 7.4-2z'
CLOUD = 'M7.2 17a4.6 4.6 0 0 1-.5-9.2 6 6 0 0 1 11.5 1.3 4 4 0 0 1-.6 7.9z'


def pictures():
    p = {}
    # ---- survival: what closes in (all three look to the right)
    p['enemy'] = sticker(MONSTER_BODY, '#C7B8F5',
        f'<ellipse cx="17.4" cy="14.9" rx="1.4" ry=".95" fill="#F8B4C8" opacity=".85"/>' + eyes(11.4, 16.2, 12.3) +
        f'<path d="M11.4 16.5c1.6 1.1 3.4 1 4.9-.2" fill="none" {O} stroke-width="1.4"/>'
        f'<path d="M12.5 17l.7 1.4.7-1.2M14.3 17.1l.7 1.1.6-1.5" fill="{CREAM}" stroke="{INK}" stroke-width=".9" stroke-linejoin="round"/>')
    wing = 'M8 11.6C5.6 7.6 3 6.6 1.3 7.4c1.3.9 1.7 2.1 1.5 3.6 1.3-.6 2.3-.4 3.1.6.9-.8 1.6-.7 2.1 0z'
    p['flyer'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><g transform="translate(0 1)">'
        f'<path d="{wing}" {CUT}/><path d="{wing}" transform="translate(24.6 0) scale(-1 1)" {CUT}/><circle cx="12.3" cy="13" r="6.4" {CUT}/>'
        f'<path d="{wing}" fill="#E58FB0" {O}/><path d="{wing}" transform="translate(24.6 0) scale(-1 1)" fill="#E58FB0" {O}/>'
        f'<path d="M8.2 8.4L7.4 4.6l3.2 2zM16.4 8.4l.8-3.8-3.2 2z" fill="#F8B4C8" {O} stroke-width="1.4"/>'
        f'<circle cx="12.3" cy="13" r="6.1" fill="#F8B4C8" {O}/>' + eyes(12.2, 16, 12.2, 1.3) +
        f'<path d="M12.6 15.6c1 .8 2.2.8 3.2 0" fill="none" {O} stroke-width="1.3"/><path d="M14 16.1l.6 1.5.7-1.4z" fill="{CREAM}" stroke="{INK}" stroke-width=".8" stroke-linejoin="round"/>'
        f'</g></svg>')
    brute = 'M6.5 6.2h11A4.5 4.5 0 0 1 22 10.7v6.1a4.5 4.5 0 0 1-4.5 4.5h-11A4.5 4.5 0 0 1 2 16.8v-6.1a4.5 4.5 0 0 1 4.5-4.5z'
    p['brute'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><path d="{brute}" {CUT}/><path d="M5.2 7L3.6 2.2l4.4 3.2zM18.8 7l1.6-4.8L16 5.4z" {CUT}/>'
        f'<path d="M5.2 7L3.6 2.2l4.4 3.2zM18.8 7l1.6-4.8L16 5.4z" fill="{CREAM}" {O} stroke-width="1.4"/>'
        f'<path d="{brute}" fill="#9FD4C0" {O}/><path d="M2.6 15.5h18.8" stroke="{INK}" stroke-width="1" opacity=".25" fill="none"/>' + eyes(12.4, 17.4, 12.2, 1.35) +
        f'<path d="M10.2 9.6l3.6 1.3M19.6 9.6L16 10.9" fill="none" {O} stroke-width="1.5"/>'
        f'<path d="M11 17h8" fill="none" {O} stroke-width="1.5"/><path d="M12.4 17l.8-1.9.9 1.9M16 17l.8-1.9.9 1.9" fill="{CREAM}" stroke="{INK}" stroke-width=".9" stroke-linejoin="round"/>'
        f'</svg>')
    p['gem'] = sticker(GEM, '#8FD3F4', f'<path d="M7 3.6l2.4 5.6L12 21l2.6-11.8L17 3.6" fill="#BFE8FB" stroke="{INK}" stroke-width="1.1" stroke-linejoin="round"/><path d="M2.6 9.2h18.8" stroke="{INK}" stroke-width="1.1" fill="none"/>')
    star_small = 'M15.4 5.4l1.9 4.2 4.6.5-3.4 3.1 1 4.5-4.1-2.3-4 2.3.9-4.5-3.4-3.1 4.6-.5z'
    p['shot'] = sticker(star_small, '#FFDD66', f'<g fill="none" stroke="{CREAM}" stroke-width="3.2" stroke-linecap="round"><path d="M2.4 8.6h5.2M1 12.4h6.4M2.4 16.2h5.2"/></g>'
        f'<g fill="none" stroke="#F3B56E" stroke-width="1.5" stroke-linecap="round"><path d="M2.4 8.6h5.2M1 12.4h6.4M2.4 16.2h5.2"/></g>')
    p['orb'] = sticker(MOON, '#FFE9A6', f'<path d="M7.4 9.4c.4-1.6 1.3-2.9 2.6-3.8" stroke="{CREAM}" stroke-width="1.3" stroke-linecap="round" fill="none"/>'
        f'<circle cx="9.2" cy="15.6" r="1" fill="#F3B56E" opacity=".7"/><circle cx="12.6" cy="18.2" r=".7" fill="#F3B56E" opacity=".7"/>')
    # the shock wave: one line, as wide as the picture (its size is the ring's diameter)
    p['ring'] = (f'<svg class="plain" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11" fill="none" stroke="{CREAM}" stroke-width="1.5"/>'
        f'<circle cx="12" cy="12" r="11" fill="none" stroke="#B59DEB" stroke-width=".7"/><circle cx="12" cy="12" r="9.8" fill="none" stroke="{CREAM}" stroke-width=".5" opacity=".7"/></svg>')
    zig = 'M14 2L5 35h6.5L6.5 62h6L8 94 19.5 54H13l5.5-27h-6L18 2z'
    p['bolt'] = f'<svg class="pic" viewBox="-1 -1 26 98" preserveAspectRatio="none"><path d="{zig}" {CUT} stroke-width="4"/><path d="{zig}" fill="#FFE27A" {O}/></svg>'
    p['puff'] = sticker(CLOUD, CREAM, f'<path d="M8.4 10.6c.6-1.5 1.7-2.5 3.2-2.9" stroke="#E9D9BC" stroke-width="1.3" stroke-linecap="round" fill="none"/>', cut=False)
    p['heart'] = sticker(HEART, '#FF9B85', f'<path d="M6.2 8.2c.2-1.2 1-2 2.2-2.2" stroke="{CREAM}" stroke-width="1.4" stroke-linecap="round" fill="none"/>')
    # ---- umbrella: the ink that rains, the golden drop, what is left where a drop ends
    p['drop'] = sticker(DROP, '#6C5FA8', f'<path d="M8.6 14.4c0-1.6.7-3 1.8-4.4" stroke="#B9AEE6" stroke-width="1.4" stroke-linecap="round" fill="none"/>')
    p['blob'] = sticker(BLOB, '#4E4486', f'<path d="M5.4 13.4c.2-1.5 1.1-2.7 2.6-3.5" stroke="#9F93D6" stroke-width="1.5" stroke-linecap="round" fill="none"/>'
        f'<circle cx="9.6" cy="14.4" r="1.25" fill="{CREAM}"/><circle cx="14.6" cy="14.4" r="1.25" fill="{CREAM}"/><path d="M8 12.2l2.6 1M16.2 12.2l-2.6 1" stroke="{CREAM}" stroke-width="1.1" stroke-linecap="round" fill="none"/>')
    p['gold'] = sticker(DROP, '#FFD05A', f'<path d="M8.6 14.4c0-1.6.7-3 1.8-4.4" stroke="{CREAM}" stroke-width="1.5" stroke-linecap="round" fill="none"/>'
        f'<path d="M19.6 2.2l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z" fill="{CREAM}" stroke="{INK}" stroke-width="1" stroke-linejoin="round"/>')
    sp = '#6C5FA8'
    p['splash'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><g fill="{sp}" {O} stroke-width="1.3">'
        f'<ellipse cx="12" cy="19" rx="7.2" ry="2.2"/><path d="M12 16.6c-1.1-2.9-1.1-5.6 0-8.4 1.1 2.8 1.1 5.5 0 8.4z"/>'
        f'<path d="M8.2 17c-2-1.7-3.2-3.7-3.6-6.2 2.1 1.4 3.4 3.4 3.6 6.2zM15.8 17c2-1.7 3.2-3.7 3.6-6.2-2.1 1.4-3.4 3.4-3.6 6.2z"/>'
        f'<circle cx="3.6" cy="6.8" r="1.2"/><circle cx="20.4" cy="6.8" r="1.2"/><circle cx="12" cy="4.4" r="1.3"/></g></svg>')
    p['star'] = sticker(STAR, '#FFDD66', f'<path d="M9.2 8.2c.6-1.3 1.3-2.3 2-3" stroke="{CREAM}" stroke-width="1.4" stroke-linecap="round" fill="none"/>')
    p['sparkle'] = f'<svg class="plain" viewBox="0 0 24 24"><path d="{SPARK}" fill="#FFF6C9" stroke="{INK}" stroke-width="1" stroke-linejoin="round"/><path d="M12 7.4l1 3.6 3.6 1-3.6 1-1 3.6-1-3.6-3.6-1 3.6-1z" fill="#FFFFFF"/></svg>'
    flake = 'M12 3.4v17.2M4.6 7.7l14.8 8.6M4.6 16.3l14.8-8.6M12 6.6l-1.8-1.8M12 6.6l1.8-1.8M12 17.4l-1.8 1.8M12 17.4l1.8 1.8'
    p['frost'] = (f'<svg class="plain" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11" fill="#BFE8FB" opacity=".62"/><circle cx="12" cy="12" r="11" fill="none" stroke="#FFFFFF" stroke-width="1" opacity=".9"/>'
        f'<path d="{flake}" fill="none" stroke="#FFFFFF" stroke-width="1.5" stroke-linecap="round" opacity=".95"/></svg>')
    # ---- the nine powers of the survival game, for its cards (GlassSurvival.AbilityIds)
    p['ab-star'] = sticker(STAR, '#FFDD66', f'<path d="M9.2 8.2c.6-1.3 1.3-2.3 2-3" stroke="{CREAM}" stroke-width="1.4" stroke-linecap="round" fill="none"/>', cut=False)
    p['ab-moons'] = sticker(MOON, '#FFE9A6', f'<path d="M19.4 2.4l.7 1.9 1.9.7-1.9.7-.7 1.9-.7-1.9-1.9-.7 1.9-.7z" fill="{CREAM}" stroke="{INK}" stroke-width="1" stroke-linejoin="round"/>', cut=False)
    p['ab-wave'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><circle cx="12" cy="12" r="10.4" fill="#EDE6FB" {O}/><circle cx="12" cy="12" r="6.6" fill="#DCCDF8" {O} stroke-width="1.4"/>'
        f'<circle cx="12" cy="12" r="2.8" fill="#B59DEB" {O} stroke-width="1.3"/></svg>')
    p['ab-frost'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><path d="{flake}" fill="none" stroke="{INK}" stroke-width="4.2" stroke-linecap="round"/>'
        f'<path d="{flake}" fill="none" stroke="#8FD3F4" stroke-width="2" stroke-linecap="round"/></svg>')
    p['ab-magnet'] = sticker('M4.4 3.6h5.4v8.6a2.2 2.2 0 0 0 4.4 0V3.6h5.4v8.6a7.6 7.6 0 0 1-15.2 0z', '#FF9B85',
        f'<path d="M4.4 3.6h5.4v3.6H4.4zM14.2 3.6h5.4v3.6h-5.4z" fill="{CREAM}" {O} stroke-width="1.4"/>', cut=False)
    p['ab-swift'] = sticker('M7 5.4h6v7.2l6.6 2.2a3.2 3.2 0 0 1 2.2 3v2.6H7z', '#86C2EE',
        f'<path d="M7 17.4h14.8" stroke="{INK}" stroke-width="1.3" fill="none"/><path d="M13 9.6h-2.4M13 12h-2.4" stroke="{CREAM}" stroke-width="1.3" stroke-linecap="round" fill="none"/>'
        f'<g fill="none" {O} stroke-width="1.6"><path d="M1 8.6h3.4M.4 13h4M1 17.4h3.4"/></g>', cut=False)
    F = f'fill="{CREAM}" stroke="{INK}" stroke-width="1.3" stroke-linejoin="round"'
    p['ab-giant'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><g {F}><rect x="2.6" y="12" width="2.9" height="6.6" rx="1.45" transform="rotate(-34 4 15.3)"/>'
        f'<rect x="6" y="4.6" width="2.9" height="9.6" rx="1.45" transform="rotate(-8 7.45 13)"/><rect x="9.2" y="2.6" width="2.9" height="11.4" rx="1.45"/>'
        f'<rect x="12.4" y="3" width="2.9" height="11" rx="1.45" transform="rotate(5 13.85 13)"/><rect x="15.5" y="5" width="2.8" height="9.4" rx="1.4" transform="rotate(12 16.9 13)"/>'
        f'<rect x="5.8" y="11" width="12.6" height="10.6" rx="5"/></g></svg>')
    p['ab-bolt'] = sticker(BOLT, '#FFE27A', '', cut=False)
    p['ab-heart'] = sticker(HEART, '#FF9B85', f'<path d="M6.2 8.2c.2-1.2 1-2 2.2-2.2" stroke="{CREAM}" stroke-width="1.4" stroke-linecap="round" fill="none"/>'
        f'<path d="M19 13.4v5.2M16.4 16h5.2" stroke="{CREAM}" stroke-width="3.4" stroke-linecap="round" fill="none"/><path d="M19 13.4v5.2M16.4 16h5.2" stroke="{INK}" stroke-width="1.5" stroke-linecap="round" fill="none"/>', cut=False)
    # ---- the hand signs (GlassArt.Rock / PaperSign / Scissors): the back of a right hand, the thumb on the left; mirrored for the other player
    p['rock'] = hand_sign([(5.4, 8.6, 13.2, 11.4, 5, ''), (5.8, 6.2, 3.5, 6, 1.75, ''), (9, 5.4, 3.5, 6.4, 1.75, ''), (12.2, 5.6, 3.5, 6.2, 1.75, ''), (15.3, 6.6, 3.3, 5.6, 1.65, ''),
                           (3.4, 11.4, 6.6, 3.6, 1.8, '24 6.7 13.2')], '#FFD9A0')
    p['paper-sign'] = hand_sign([(2.6, 12, 2.9, 6.6, 1.45, '-34 4 15.3'), (6, 4.6, 2.9, 9.6, 1.45, '-8 7.45 13'), (9.2, 2.6, 2.9, 11.4, 1.45, ''), (12.4, 3, 2.9, 11, 1.45, '5 13.85 13'),
                                 (15.5, 5, 2.8, 9.4, 1.4, '12 16.9 13'), (5.8, 11, 12.6, 10.6, 5, '')], '#C9EBB4')
    p['scissors'] = hand_sign([(4.9, 12.6, 3.2, 6.2, 1.6, '-28 6.5 15.7'), (8, 2.6, 3.3, 11.4, 1.65, '-13 9.65 12.5'), (12.7, 2.6, 3.3, 11.4, 1.65, '13 14.35 12.5'),
                               (7.2, 11.6, 9.6, 9.2, 4.4, '')], '#DCCDF8')
    # ---- the look: a magnifying glass for the corner of the frame, a band of light that travels down inside it
    p['magnifier'] = (f'<svg class="pic" viewBox="-1.5 -1.5 27 27"><circle cx="10.2" cy="10" r="7.2" {CUT}/><path d="M15.2 15l6 6.2" {CUT} stroke-width="7"/>'
        f'<path d="M15.2 15l6 6.2" fill="none" stroke="{INK}" stroke-width="4.6" stroke-linecap="round"/><path d="M15.8 15.6l5 5.2" fill="none" stroke="#F3B56E" stroke-width="2.2" stroke-linecap="round"/>'
        f'<circle cx="10.2" cy="10" r="7.2" fill="#DDF1FB" stroke="{INK}" stroke-width="2"/><path d="M6.2 9c.4-1.9 1.6-3.2 3.4-3.7" stroke="#FFFFFF" stroke-width="1.6" stroke-linecap="round" fill="none"/></svg>')
    return p


def hand_svg():
    """The shadow of an open right hand seen from behind (the thumb on the left), white; the middle of the palm is the middle of the picture."""
    parts = ('<rect x="31" y="40" width="38" height="36" rx="15"/>'                                  # the palm
             '<rect x="32.2" y="13" width="8.6" height="40" rx="4.3" transform="rotate(-7 36.5 52)"/>'     # index
             '<rect x="42.4" y="7" width="8.8" height="44" rx="4.4" transform="rotate(-1 46.8 52)"/>'      # middle
             '<rect x="52.6" y="11" width="8.6" height="42" rx="4.3" transform="rotate(5 56.9 52)"/>'      # ring
             '<rect x="61.4" y="22" width="7.8" height="32" rx="3.9" transform="rotate(12 65.3 54)"/>'     # little
             '<rect x="30.5" y="32" width="11" height="32" rx="5.5" transform="rotate(-44 36 62)"/>'       # thumb (it leaves the palm low on the left)
             '<rect x="39" y="66" width="23" height="30" rx="8"/>')                                        # the wrist
    return ('<svg class="plain" viewBox="0 0 100 100"><defs><linearGradient id="wr" x1="0" y1="0" x2="0" y2="1">'
            '<stop offset="0" stop-color="#fff"/><stop offset=".80" stop-color="#fff"/><stop offset=".97" stop-color="#000"/></linearGradient>'
            '<mask id="wm"><rect x="0" y="0" width="100" height="100" fill="url(#wr)"/></mask></defs>'
            f'<g fill="#FFFFFF" mask="url(#wm)">{parts}</g></svg>')


def stall_svg():
    aw = 'M16 24h88l12 24a8 8 0 0 1-16 0a8 8 0 0 1-16 0a8 8 0 0 1-16 0a8 8 0 0 1-16 0a8 8 0 0 1-16 0a8 8 0 0 1-16 0a8 8 0 0 1-16 0z'
    # seven scallops, every other one cream: a stripe runs from the top edge (88 wide) out to its scallop (112 wide)
    stripes = ''.join(f'<path d="M{16 + 88 / 7 * k:.2f} 20L{16 + 88 / 7 * (k + 1):.2f} 20L{20 + 16 * k} 60L{4 + 16 * k} 60z" fill="{CREAM}"/>' for k in (1, 3, 5))
    wood, wood2 = '#E8B77D', '#D9A066'
    return (f'<svg class="plain" viewBox="0 0 120 120"><defs><clipPath id="awc"><path d="{aw}"/></clipPath></defs>'
            f'<ellipse cx="60" cy="112" rx="52" ry="5" fill="rgba(74,47,39,.22)"/>'
            f'<rect x="21" y="40" width="6" height="70" rx="2" fill="{wood2}" stroke="{INK}" stroke-width="2.4"/><rect x="93" y="40" width="6" height="70" rx="2" fill="{wood2}" stroke="{INK}" stroke-width="2.4"/>'
            f'<rect x="14" y="80" width="92" height="31" rx="4" fill="{wood}" stroke="{INK}" stroke-width="2.6"/>'
            f'<path d="M14 91h92M14 101h92M44 80v11M76 91v10M36 101v10M84 101v10" stroke="{INK}" stroke-width="1.2" opacity=".35" fill="none"/>'
            f'<rect x="9" y="72" width="102" height="10" rx="3.5" fill="#F6D7A8" stroke="{INK}" stroke-width="2.6"/>'
            f'<path d="{aw}" fill="{CREAM}" stroke="{CREAM}" stroke-width="7" stroke-linejoin="round"/>'
            f'<path d="{aw}" fill="#FF9B85"/><g clip-path="url(#awc)">{stripes}</g>'
            f'<path d="{aw}" fill="none" stroke="{INK}" stroke-width="2.6" stroke-linejoin="round"/><path d="M11 40h98" stroke="{INK}" stroke-width="1.4" opacity=".35" fill="none"/>'
            f'<path d="M52 18v7M68 18v7" stroke="{INK}" stroke-width="2" fill="none"/>'
            f'<rect x="34" y="2" width="52" height="19" rx="5" fill="{CREAM}" stroke="{INK}" stroke-width="2.4"/>'
            f'<text x="60" y="16.2" text-anchor="middle" font-family="Jua" font-size="12.5" fill="{INK}">Market</text></svg>')


def tag_svg():
    tag = 'M22 5h66a5 5 0 0 1 5 5v28a5 5 0 0 1-5 5H22L5 24z'
    star = 'M33 12.6l3.3 7.3 7.9.9-5.9 5.3 1.7 7.8-7-4-7 4 1.7-7.8-5.9-5.3 7.9-.9z'
    return (f'<svg class="pic" viewBox="0 0 96 48"><path d="{tag}" fill="{CREAM}" stroke="{INK}" stroke-width="2.6" stroke-linejoin="round"/>'
            f'<circle cx="14" cy="24" r="2.6" fill="#E9D9BC" stroke="{INK}" stroke-width="1.6"/>'
            f'<path d="{star}" transform="translate(1 -.5)" fill="#FFDD66" stroke="{INK}" stroke-width="2" stroke-linejoin="round"/></svg>')


# ---- PLAIN SHAPES for scripted games (GlassArt.Rect / Disc / RingShape): WHITE, tinted by the renderers with the
# sprite's colour, and filling their canvas - glass-rect (a square: stretched to w x size), glass-disc (a filled circle,
# its diameter the canvas), glass-hoop (a circle's outline: the line is 1/14 of the diameter, its outer edge the canvas).
# ---- A little wooden board and its stones (GlassArt.Board / StoneA / StoneB / Cell): built-in pictures like the others,
# for games that want them. glass-board FILLS its canvas: 9 x 9 points, the point (col, row) at ((col + .5) / 9,
# (row + .5) / 9) of the picture (draw it with w = size). A stone is a disc 0.75 of its canvas across, in the middle.
WOOD_LIGHT, WOOD, WOOD_DEEP, WOOD_RIM = '#F8E2B4', '#F1CF98', '#E2B578', '#D9A066'


def board_svg(kind, top=False):
    """kind: omok (9 x 9 points) | ttt (3 x 3 cells) | c4 (7 x 6 wells). (top: the bare face, without rim and corners.)"""
    w, h = (70, 60) if kind == 'c4' else (90, 90)
    rim = 0 if top else 1.15 if kind == 'c4' else 1.7       # how wide the rim is drawn (the wells of c4 come close to the edge)
    uid = kind + ('t' if top else 'b')
    grain = ''.join(
        f'<path d="M-2 {y:.1f}C{w * .22:.1f} {y - a:.1f} {w * .46:.1f} {y + a:.1f} {w * .7:.1f} {y - a * .3:.1f}S{w * .93:.1f} {y + a:.1f} {w + 2} {y - a * .5:.1f}" '
        f'fill="none" stroke="{WOOD_RIM}" stroke-width="{sw}" opacity="{op}"/>'
        for y, a, sw, op in ((h * .07, 1.2, .5, .30), (h * .19, -1.6, .7, .22), (h * .30, 1.0, .4, .30), (h * .43, 1.9, .8, .18), (h * .56, -1.2, .5, .28),
                             (h * .68, 1.5, .6, .22), (h * .80, -1.8, .8, .18), (h * .91, 1.1, .45, .30)))
    face = ''
    if kind == 'omok':
        lines = ''.join(f'M{5 + 10 * i} 5V85M5 {5 + 10 * i}H85' for i in range(9))
        face = (f'<path d="{lines}" fill="none" stroke="{INK}" stroke-width=".62" stroke-linecap="round" opacity=".9"/>' +
                ''.join(f'<circle cx="{x}" cy="{y}" r="1.25" fill="{INK}"/>' for x, y in ((25, 25), (65, 25), (45, 45), (25, 65), (65, 65))))
    elif kind == 'ttt':
        face = (f'<path d="M30 8V82M60 8V82M8 30H82M8 60H82" fill="none" stroke="{WOOD_LIGHT}" stroke-width="4.6" stroke-linecap="round" opacity=".7"/>'
                f'<path d="M30 8V82M60 8V82M8 30H82M8 60H82" fill="none" stroke="{INK}" stroke-width="2.3" stroke-linecap="round"/>')
    else:
        for r in range(6):
            for c in range(7):
                x, y = 5 + 10 * c, 5 + 10 * r
                face += (f'<circle cx="{x}" cy="{y}" r="3.6" fill="{WOOD_DEEP}" stroke="{INK}" stroke-width=".5" opacity=".95"/>'
                         f'<path d="M{x - 2.8} {y - .8}a3 3 0 0 1 5.6 0" fill="none" stroke="#C08A52" stroke-width=".75" stroke-linecap="round" opacity=".7"/>'
                         f'<path d="M{x - 2.3} {y + 1.8}a3 3 0 0 0 4.6 0" fill="none" stroke="{WOOD_LIGHT}" stroke-width=".55" stroke-linecap="round" opacity=".8"/>')
    defs = (f'<defs><linearGradient id="wd{uid}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{WOOD_LIGHT}"/><stop offset=".55" stop-color="{WOOD}"/><stop offset="1" stop-color="#EBC389"/></linearGradient>'
            f'<clipPath id="cp{uid}"><rect x="{rim}" y="{rim}" width="{w - 2 * rim}" height="{h - 2 * rim}" rx="{0 if top else 2.2}"/></clipPath></defs>')
    if top:
        body = f'<rect x="-1" y="-1" width="{w + 2}" height="{h + 2}" fill="url(#wd{uid})"/><g clip-path="url(#cp{uid})">{grain}</g>{face}'
    else:
        body = (f'<rect x=".55" y=".55" width="{w - 1.1}" height="{h - 1.1}" rx="3.6" fill="{WOOD_RIM}" stroke="{INK}" stroke-width="1.1"/>'
                f'<rect x="{rim}" y="{rim}" width="{w - 2 * rim}" height="{h - 2 * rim}" rx="2.2" fill="url(#wd{uid})"/><g clip-path="url(#cp{uid})">{grain}</g>'
                f'<rect x="{rim}" y="{rim}" width="{w - 2 * rim}" height="{h - 2 * rim}" rx="2.2" fill="none" stroke="{INK}" stroke-width=".45" opacity=".45"/>{face}')
    return f'<svg class="plain" viewBox="0 0 {w} {h}" preserveAspectRatio="none">{defs}{body}</svg>'


def stone_svg(fill, shade, light):
    return (f'<svg class="plain" viewBox="0 0 24 24"><ellipse cx="12" cy="13.5" rx="9.3" ry="9" fill="rgba(74,47,39,.26)"/>'
            f'<circle cx="12" cy="12" r="9" fill="{fill}" stroke="{INK}" stroke-width="1.35"/>'
            f'<path d="M5.2 14.6a7.4 7.4 0 0 0 13.6 0 8.4 8.4 0 0 1-13.6 0z" fill="{shade}" opacity=".9"/>'
            f'<path d="M6.6 9.6c.9-2 2.5-3.3 4.6-3.8" fill="none" stroke="{light}" stroke-width="1.5" stroke-linecap="round"/></svg>')


def cell_svg():
    """The free point a hand is over: a pale stone that is not there yet, with a dashed ink line."""
    return (f'<svg class="plain" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.9" fill="{CREAM}" opacity=".55"/>'
            f'<circle cx="12" cy="12" r="8.9" fill="none" stroke="{INK}" stroke-width="1.15" stroke-dasharray="2.9 2.69" stroke-linecap="round" opacity=".85"/></svg>')


# name, cell (CSS px) w, h, output w, h, body
def entries():
    pics = pictures()
    e = []
    e.append(('glass-board', 512, 512, 1024, 1024, board_svg('omok')))
    e.append(('glass-stone-a', 144, 144, 256, 256, stone_svg(CREAM, '#EBDCC0', '#FFFFFF')))
    e.append(('glass-stone-b', 144, 144, 256, 256, stone_svg('#7C5544', '#63402F', '#B08A72')))
    e.append(('glass-cell', 144, 144, 256, 256, cell_svg()))
    e.append(('glass-rect', 16, 16, 16, 16, '<svg class="plain" viewBox="0 0 8 8"><rect x="-1" y="-1" width="10" height="10" fill="#FFFFFF"/></svg>'))
    e.append(('glass-disc', 144, 144, 256, 256, '<svg class="plain" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11.95" fill="#FFFFFF"/></svg>'))
    e.append(('glass-hoop', 144, 144, 256, 256, '<svg class="plain" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11.1" fill="none" stroke="#FFFFFF" stroke-width="1.7"/></svg>'))
    for n in ('enemy', 'flyer', 'brute', 'gem', 'shot', 'orb', 'puff', 'heart', 'drop', 'blob', 'gold', 'splash', 'star', 'sparkle', 'frost', 'magnifier', 'rock', 'paper-sign', 'scissors'):
        e.append(('glass-' + n, 144, 144, 256, 256, pics[n]))
    for n in ('star', 'moons', 'wave', 'frost', 'magnet', 'swift', 'giant', 'bolt', 'heart'):
        e.append(('glass-ab-' + n, 144, 144, 192, 192, pics['ab-' + n]))
    e.append(('glass-ring', 256, 256, 512, 512, pics['ring']))
    e.append(('glass-bolt', 72, 288, 128, 512, pics['bolt']))
    e.append(('glass-card', 216, 300, 368, 512, '<div class="card"><div class="band"></div><div class="well"></div></div>'))
    e.append(('glass-hand', 256, 256, 512, 512, hand_svg()))
    e.append(('glass-hand-soft', 256, 256, 512, 512, '<div class="soft">' + hand_svg() + '</div>'))
    e.append(('glass-shimmer', 128, 64, 256, 128, '<div class="shimmer"></div>'))
    e.append(('glass-stall', 320, 320, 512, 512, stall_svg()))
    e.append(('glass-tag', 216, 108, 384, 192, tag_svg()))
    e.append(('glass-listed', 460, 150, 512, 167, '<div class="ribbon"><span>Listed!</span></div>'))
    return e


NAMES = [x[0] for x in entries()]


def build_html(font):
    es = entries()
    x = y = row = 0
    width = 1600
    parts, rects = [], {}
    for name, w, h, ow, oh, body in es:
        if x + w > width: x, y, row = 0, y + row, 0
        parts.append(f'<div class="cell" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px">{body}</div>')
        rects[name] = (x, y, w, h, ow, oh)
        x += w; row = max(row, h)
    height = y + row
    css = '''
@font-face{font-family:"Jua";src:url("%s");}
html,body{margin:0;background:transparent;}
body{width:%dpx;height:%dpx;position:relative;overflow:hidden;font-family:"Jua","Segoe UI",sans-serif;color:%s;}
.cell{position:absolute;overflow:hidden;}
.pic{position:absolute;inset:10px;width:calc(100%% - 20px);height:calc(100%% - 20px);filter:drop-shadow(0 3px 2px rgba(74,47,39,.28));}
.plain{position:absolute;inset:0;width:100%%;height:100%%;}
.soft{position:absolute;inset:0;filter:blur(9px);}
.card{position:absolute;left:14px;top:10px;right:14px;bottom:18px;box-sizing:border-box;border-radius:20px;border:5px solid %s;
  background:linear-gradient(180deg,#FFFCF2 0%%,%s 55%%,#FBEBCB 100%%);box-shadow:0 0 0 4px %s,0 9px 10px 3px rgba(74,47,39,.26);overflow:hidden;}
.card .band{position:absolute;left:0;right:0;top:0;height:42%%;background:linear-gradient(180deg,#FFE9A6,#FFF3CF);border-bottom:3px solid rgba(74,47,39,.35);}
.card .well{position:absolute;left:50%%;top:21%%;width:96px;height:96px;margin:-48px 0 0 -48px;border-radius:50%%;background:%s;border:4px solid %s;box-sizing:border-box;}
.shimmer{position:absolute;inset:0;background:linear-gradient(180deg,rgba(255,255,255,0) 0%%,rgba(255,255,255,.55) 62%%,rgba(255,255,255,.9) 80%%,rgba(255,255,255,0) 100%%);}
.ribbon{position:absolute;left:50px;top:22px;width:360px;height:74px;background:#C9EBB4;border:5px solid %s;box-sizing:border-box;border-radius:14px;
  box-shadow:0 8px 10px 2px rgba(74,47,39,.24);display:flex;align-items:center;justify-content:center;}
.ribbon::before,.ribbon::after{content:"";position:absolute;top:14px;width:44px;height:60px;background:#8FCF8B;border:5px solid %s;box-sizing:border-box;z-index:-1;}
.ribbon::before{left:-30px;clip-path:polygon(0 0,100%% 0,100%% 100%%,0 100%%,34%% 50%%);border-radius:6px;}
.ribbon::after{right:-30px;clip-path:polygon(0 0,100%% 0,66%% 50%%,100%% 100%%,0 100%%);border-radius:6px;}
.ribbon span{font-size:46px;letter-spacing:.5px;}
''' % ('file:///' + font.replace('\\', '/'), width, height, INK, INK, CREAM, CREAM, CREAM, INK, INK, INK)
    html = f'<!doctype html><html><head><meta charset="utf-8"><style>{css}</style></head><body>{"".join(parts)}</body></html>'
    return html, rects, width, height


def build(browser, font, out_dir, here):
    """Renders the sheet and writes the pictures into out_dir. Returns how many were written."""
    html, rects, width, height = build_html(font)
    page = os.path.join(here, 'glass-sheet.html')
    open(page, 'w', encoding='utf-8').write(html)
    shot = os.path.join(here, 'glass-sheet.png')
    if os.path.exists(shot): os.remove(shot)
    profile = tempfile.mkdtemp(prefix='lp-glass-art-')
    try:
        subprocess.run([browser, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--allow-file-access-from-files', f'--user-data-dir={profile}',
                        f'--force-device-scale-factor={SCALE}', f'--window-size={width},{height}', '--default-background-color=00000000', '--virtual-time-budget=3000',
                        f'--screenshot={shot}', 'file:///' + page.replace('\\', '/')], check=False, timeout=120, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    finally:
        shutil.rmtree(profile, ignore_errors=True)
    if not os.path.exists(shot): raise SystemExit('the browser did not write the glass sheet')
    sheet = Image.open(shot).convert('RGBA')
    print('glass sheet', sheet.size, 'expected', (width * SCALE, height * SCALE))
    os.makedirs(out_dir, exist_ok=True)
    for name, (x, y, w, h, ow, oh) in rects.items():
        im = sheet.crop((x * SCALE, y * SCALE, (x + w) * SCALE, (y + h) * SCALE)).resize((ow, oh), Image.LANCZOS)
        im.save(os.path.join(out_dir, name + '.png'))
    return len(rects)


if __name__ == '__main__':
    HERE = os.path.dirname(os.path.abspath(__file__))
    ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
    for b in (r'C:\Program Files\Google\Chrome\Application\chrome.exe', r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', r'C:\Program Files\Microsoft\Edge\Application\msedge.exe'):
        if os.path.exists(b):
            n = build(b, os.path.join(ROOT, 'MetaVRHack', 'Assets', 'LivingPartner', 'Art', 'Fonts', 'Jua-Regular.ttf'), os.path.join(ROOT, 'MetaVRHack', 'Assets', 'LivingPartner', 'Art', 'Ui'), HERE)
            print('wrote', n, 'glass pictures')
            break
    else:
        raise SystemExit('no Chrome / Edge found')
