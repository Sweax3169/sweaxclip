# Generates the README / site artwork for SweaxClip as animated SVGs (dark + light, en + tr).
# usage: python tools/art.py docs/art
import os, sys


FONT = "'Segoe UI Variable Display','Segoe UI',-apple-system,BlinkMacSystemFont,'Helvetica Neue',Arial,sans-serif"
MONO = "'Cascadia Mono',Consolas,'SF Mono',Menlo,monospace"

T = {
    'dark': dict(bg1='#13120F', bg2='#0C0B0A', panel1='#1F1E1C', panel2='#171615', edge='rgba(255,255,255,.10)', glint='rgba(255,255,255,.07)',
                 ink='#F2EFE9', ink2='rgba(242,239,233,.70)', ink3='rgba(242,239,233,.42)', rail='rgba(255,255,255,.12)', field='rgba(255,255,255,.05)',
                 accent='#FF6A3D', text='#D8D3CB', link='#7DA8FF', code='#5CD6B4', image='#F48FB1', shot='#A78BFA', files='#D7B46A',
                 slate='#24272F', paper='#2A2826', sel='rgba(255,255,255,.06)', shadow='0.55', glowA='.22', glowB='.14', bar='rgba(242,239,233,.16)', bar2='rgba(242,239,233,.30)'),
    'light': dict(bg1='#F7F5F1', bg2='#ECE9E3', panel1='#FFFFFF', panel2='#F8F6F2', edge='rgba(35,32,29,.12)', glint='#FFFFFF',
                  ink='#23201D', ink2='rgba(35,32,29,.70)', ink3='rgba(35,32,29,.45)', rail='rgba(35,32,29,.13)', field='rgba(35,32,29,.05)',
                  accent='#E4572E', text='#6E675F', link='#2F6BEA', code='#0E8F70', image='#C2416D', shot='#6D4FD8', files='#96701E',
                  slate='#1D2027', paper='#FFFDF8', sel='rgba(35,32,29,.05)', shadow='0.18', glowA='.16', glowB='.10', bar='rgba(35,32,29,.12)', bar2='rgba(35,32,29,.26)'),
}

L = {
    'en': dict(kicker='Clipboard history for Windows 10 and 11', tag='Everything you copy, kept.',
               sub1='Press Win + V: every text, link, image, screenshot', sub2='and file you ever copied, right where you type.',
               facts='Unlimited history  ·  Searches inside screenshots  ·  Offline  ·  ~10 MB idle',
               search='Search 3,214 clips', all='All', now='now', t1='Meeting moved to Thursday 14:30 —', t2='room 4B. Bring the Q4 numbers.',
               paste='paste', yest='yest.', times=['now', '4m', '9m', '16m', '22m']),
    'tr': dict(kicker='Windows 10 ve 11 için pano geçmişi', tag='Kopyaladığın her şey, saklı.',
               sub1='Win + V’ye bas: kopyaladığın her metin, bağlantı, görsel,', sub2='ekran görüntüsü ve dosya yazdığın yerde.',
               facts='Sınırsız geçmiş  ·  Ekran görüntüsünde arama  ·  Çevrimdışı  ·  Boşta ~10 MB',
               search='3.214 kayıtta ara', all='Tümü', now='şimdi', t1='Toplantı perşembe 14:30’a alındı —', t2='4B salonu. Q4 rakamlarını getir.',
               paste='yapıştır', yest='dün', times=['şimdi', '4 dk', '9 dk', '16 dk', '22 dk']),
}


def defs(c, extra=''):
    return f'''<defs>
  <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{c['bg1']}"/><stop offset="1" stop-color="{c['bg2']}"/></linearGradient>
  <linearGradient id="pf" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{c['panel1']}"/><stop offset="1" stop-color="{c['panel2']}"/></linearGradient>
  <radialGradient id="g1"><stop offset="0" stop-color="{c['accent']}" stop-opacity="{c['glowA']}"/><stop offset="1" stop-color="{c['accent']}" stop-opacity="0"/></radialGradient>
  <radialGradient id="g2"><stop offset="0" stop-color="{c['shot']}" stop-opacity="{c['glowB']}"/><stop offset="1" stop-color="{c['shot']}" stop-opacity="0"/></radialGradient>
  <radialGradient id="g3"><stop offset="0" stop-color="{c['code']}" stop-opacity="{c['glowB']}"/><stop offset="1" stop-color="{c['code']}" stop-opacity="0"/></radialGradient>
  <filter id="sh" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur in="SourceAlpha" stdDeviation="16"/><feOffset dy="16"/><feComponentTransfer><feFuncA type="linear" slope="{c['shadow']}"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>
  <filter id="sh2" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur in="SourceAlpha" stdDeviation="8"/><feOffset dy="7"/><feComponentTransfer><feFuncA type="linear" slope="{c['shadow']}"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>
  {extra}
</defs>'''


def mark(x, y, s):
    """The app icon (the deck) at scale s, 256-unit artwork."""
    return f'''<g transform="translate({x} {y}) scale({s}) translate(128 128) scale(1.1) translate(-128 -132)">
  <rect x="60" y="40" width="136" height="86" rx="18" fill="#EAD7A8"/>
  <rect x="76" y="54" width="52" height="9" rx="4.5" fill="#4A3B1F" fill-opacity=".38"/>
  <rect x="44" y="70" width="168" height="90" rx="20" fill="#1D2027" stroke="#FFFFFF" stroke-opacity=".22" stroke-width="2"/>
  <rect x="62" y="84" width="60" height="9" rx="4.5" fill="#FFFFFF" fill-opacity=".32"/>
  <rect x="24" y="104" width="208" height="120" rx="26" fill="#FFFDF8" stroke="#3A2A1A" stroke-opacity=".16" stroke-width="2"/>
  <circle cx="52" cy="132" r="9" fill="#E4572E"/>
  <rect x="68" y="127" width="62" height="10" rx="5" fill="#23201D" fill-opacity=".3"/>
  <rect x="44" y="156" width="164" height="14" rx="7" fill="#23201D" fill-opacity=".78"/>
  <rect x="44" y="182" width="112" height="14" rx="7" fill="#23201D" fill-opacity=".78"/>
</g>'''


def keycap(x, y, label, c, w=None):
    w = w or (16 + 9 * len(label))
    return f'''<g transform="translate({x} {y})"><rect width="{w}" height="30" rx="7" fill="{c['field']}" stroke="{c['edge']}"/><rect y="27" width="{w}" height="3" rx="1.5" fill="{c['edge']}"/>
<text x="{w/2}" y="20" text-anchor="middle" font-family="{FONT}" font-size="14" font-weight="600" fill="{c['ink']}">{label}</text></g>''', w


def panel(x, y, c, l, w=450):
    """The Win+V timeline, drawn: search, categories, five rows, and a selection that walks down them."""
    rows_y = y + 104
    rh = 52
    hues = [c['accent'], c['text'], c['link'], c['image'], c['shot'], c['files'], c['code'], 'url(#rainbow)']
    tabs = ''
    tx = x + 18
    for i, h in enumerate(hues):
        tabs += f'<rect x="{tx}" y="{y+64}" width="16" height="16" rx="4.5" fill="{h}" opacity="{1 if i == 0 else .55}"/>'
        if i == 0:
            tabs += f'<text x="{tx+24}" y="{y+77}" font-family="{FONT}" font-size="13" font-weight="600" fill="{c["ink"]}">{l["all"]}</text>'
            tabs += f'<rect x="{tx}" y="{y+88}" width="16" height="2.5" rx="1.25" fill="{c["accent"]}"/>'
            tx += 14 + 8 * len(l['all'])
        tx += 30
    cx = x + 66
    times = l['times']
    dots = [c['text'], c['link'], c['shot'], c['code'], c['accent']]
    rail = f'<line x1="{x+50}" y1="{rows_y}" x2="{x+50}" y2="{rows_y+rh*5}" stroke="{c["rail"]}"/>'
    rows = ''
    for i in range(5):
        ry = rows_y + i * rh
        rows += f'<g class="r" style="animation-delay:{.15+i*.07}s">'
        rows += f'<text x="{x+38}" y="{ry+22}" text-anchor="end" font-family="{FONT}" font-size="11.5" fill="{c["ink3"]}">{times[i]}</text>'
        rows += f'<circle cx="{x+50}" cy="{ry+18}" r="3.5" fill="{dots[i]}"/>'
        if i == 0:
            rows += f'<text x="{cx+8}" y="{ry+22}" font-family="{FONT}" font-size="13.5" fill="{c["ink"]}">{l["t1"]}</text>'
            rows += f'<text x="{cx+8}" y="{ry+40}" font-family="{FONT}" font-size="13.5" fill="{c["ink"]}">{l["t2"]}</text>'
        elif i == 1:
            rows += f'<rect x="{cx+8}" y="{ry+9}" width="20" height="20" rx="5.5" fill="{c["link"]}"/><text x="{cx+18}" y="{ry+23.5}" text-anchor="middle" font-family="{FONT}" font-size="11" font-weight="700" fill="#fff">G</text>'
            rows += f'<text x="{cx+38}" y="{ry+24}" font-family="{FONT}" font-size="14" font-weight="600" fill="{c["ink"]}">github.com</text>'
            rows += f'<text x="{cx+122}" y="{ry+24}" font-family="{MONO}" font-size="12" fill="{c["ink3"]}">/Sweax3169/sweaxclip</text>'
        elif i == 2:
            rows += f'<rect x="{cx+8}" y="{ry+6}" width="{w-cx+x-30}" height="40" rx="8" fill="#14161B"/>'
            for b in range(16):
                bh = [14, 22, 10, 26, 18, 30, 12, 24, 20, 28, 16, 22, 30, 14, 26, 18][b]
                col = '#3B8BEB' if b % 4 == 3 else '#3A3F4B'
                rows += f'<rect x="{cx+60+b*18}" y="{ry+42-bh}" width="10" height="{bh}" rx="2" fill="{col}"/>'
            for (px, py, d) in [(cx+12, ry+10, 'M0 8V0H8'), (w + x - 30 - 12, ry+10, 'M-8 0H0V8'), (cx+12, ry+42, 'M0 -8V0H8'), (w + x - 30 - 12, ry+42, 'M-8 0H0V-8')]:
                rows += f'<path transform="translate({px} {py})" d="{d}" fill="none" stroke="{c["accent"]}" stroke-width="2"/>'
        elif i == 3:
            rows += f'<rect x="{cx+8}" y="{ry+8}" width="{w-cx+x-30}" height="36" rx="8" fill="{c["slate"]}"/>'
            rows += f'<text x="{cx+20}" y="{ry+31}" font-family="{MONO}" font-size="12.5" xml:space="preserve"><tspan fill="#FF8A65">public async</tspan><tspan fill="#80CBC4"> Task</tspan><tspan fill="#E6E8EE">&lt;</tspan><tspan fill="#80CBC4">Clip</tspan><tspan fill="#E6E8EE">?&gt; </tspan><tspan fill="#80CBC4">FindAsync</tspan><tspan fill="#E6E8EE">(</tspan><tspan fill="#FF8A65">string</tspan><tspan fill="#E6E8EE"> q)</tspan></text>'
        else:
            rows += f'<rect x="{cx+8}" y="{ry+8}" width="22" height="22" rx="6" fill="#0F6CBD"/><text x="{cx+42}" y="{ry+24}" font-family="{MONO}" font-size="13.5" font-weight="600" fill="{c["ink"]}">#0F6CBD</text>'
        rows += '</g>'
    sel = f'''<g class="sel"><rect x="{cx-2}" y="{rows_y+1}" width="{w-cx+x-12}" height="{rh-2}" rx="11" fill="{c['sel']}" stroke="{c['accent']}" stroke-opacity=".35"/>
<circle cx="{x+50}" cy="{rows_y+18}" r="8.5" fill="{c['accent']}" opacity=".22"/><circle cx="{x+50}" cy="{rows_y+18}" r="5" fill="{c['accent']}"/></g>'''
    h = 104 + rh * 5 + 48
    foot = ''
    fx = x + 18
    for k, lab in [('↑↓', ''), ('↵', l['paste'])]:
        foot += f'<rect x="{fx}" y="{y+h-34}" width="{12+8*len(k)}" height="20" rx="5" fill="{c["field"]}" stroke="{c["edge"]}"/><text x="{fx+6+4*len(k)}" y="{y+h-20}" text-anchor="middle" font-family="{FONT}" font-size="11" fill="{c["ink2"]}">{k}</text>'
        fx += 22 + 8 * len(k)
        if lab:
            foot += f'<text x="{fx-4}" y="{y+h-20}" font-family="{FONT}" font-size="11.5" fill="{c["ink3"]}">{lab}</text>'
            fx += 8 * len(lab) + 8
    return f'''<g filter="url(#sh)">
  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="18" fill="url(#pf)" stroke="{c['edge']}"/>
</g>
<rect x="{x+1}" y="{y+1}" width="{w-2}" height="{h-2}" rx="17" fill="none" stroke="{c['glint']}" stroke-opacity=".6" stroke-dasharray="{w-30} 4000" stroke-dashoffset="-15"/>
<circle cx="{x+30}" cy="{y+30}" r="7" fill="none" stroke="{c['ink3']}" stroke-width="1.8"/><line x1="{x+35}" y1="{y+35}" x2="{x+40}" y2="{y+40}" stroke="{c['ink3']}" stroke-width="1.8" stroke-linecap="round"/>
<text x="{x+58}" y="{y+36}" font-family="{FONT}" font-size="15.5" fill="{c['ink3']}">{l['search']}</text>
<rect class="caret" x="{x+53}" y="{y+22}" width="1.6" height="18" fill="{c['accent']}"/>
{tabs}
<line x1="{x}" y1="{y+96}" x2="{x+w}" y2="{y+96}" stroke="{c['edge']}"/>
{rail}
{sel}
{rows}
<line x1="{x}" y1="{y+h-48}" x2="{x+w}" y2="{y+h-48}" stroke="{c['edge']}"/>
{foot}''', h


RAINBOW = lambda c: f'<linearGradient id="rainbow" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{c["accent"]}"/><stop offset=".4" stop-color="#F2B84B"/><stop offset=".7" stop-color="{c["code"]}"/><stop offset="1" stop-color="{c["link"]}"/></linearGradient>'

STYLE = '''<style>
  .r{animation:rin .7s cubic-bezier(.2,1.25,.4,1) both}
  @keyframes rin{from{opacity:0;transform:translateY(10px)}to{opacity:1;transform:none}}
  .sel{animation:walk 7.2s cubic-bezier(.3,1.35,.45,1) infinite}
  @keyframes walk{0%,18%{transform:translateY(0)}25%,43%{transform:translateY(52px)}50%,68%{transform:translateY(104px)}75%,93%{transform:translateY(156px)}100%{transform:translateY(0)}}
  .caret{animation:blink 1.1s steps(1) infinite}
  @keyframes blink{50%{opacity:0}}
  .float{animation:float 6s ease-in-out infinite}
  @keyframes float{50%{transform:translateY(-6px)}}
  @media (prefers-reduced-motion: reduce){.r,.sel,.caret,.float{animation:none}}
</style>'''


def banner(theme, lang):
    c, l = T[theme], L[lang]
    W, H = 1280, 520
    p, ph = panel(760, 58, c, l)
    k1, w1 = keycap(92, 338, '⊞ Win', c, 74)
    k2, w2 = keycap(92 + 74 + 30, 338, 'V', c, 34)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" role="img" aria-label="SweaxClip — {l['tag']}">
{STYLE}
{defs(c, RAINBOW(c))}
<rect width="{W}" height="{H}" rx="24" fill="url(#bg)"/>
<ellipse cx="1040" cy="150" rx="460" ry="300" fill="url(#g1)"/>
<ellipse cx="860" cy="520" rx="420" ry="240" fill="url(#g2)"/>
<ellipse cx="1260" cy="420" rx="300" ry="220" fill="url(#g3)"/>
<rect x="0.5" y="0.5" width="{W-1}" height="{H-1}" rx="23.5" fill="none" stroke="{c['edge']}"/>
{mark(92, 96, 0.3)}
<text x="92" y="88" font-family="{FONT}" font-size="15" fill="{c['ink3']}">{l['kicker']}</text>
<text x="180" y="150" font-family="{FONT}" font-size="58" font-weight="700" letter-spacing="-1.2" fill="{c['ink']}">SweaxClip</text>
<text x="92" y="232" font-family="{FONT}" font-size="34" font-weight="600" letter-spacing="-.5" fill="{c['ink']}">{l['tag']}</text>
<text x="92" y="276" font-family="{FONT}" font-size="18" fill="{c['ink2']}">{l['sub1']}</text>
<text x="92" y="302" font-family="{FONT}" font-size="18" fill="{c['ink2']}">{l['sub2']}</text>
{k1}
<text x="{92+74+15}" y="359" text-anchor="middle" font-family="{FONT}" font-size="16" fill="{c['ink3']}">+</text>
{k2}
<text x="92" y="438" font-family="{FONT}" font-size="14.5" fill="{c['ink3']}">{l['facts']}</text>
<g class="float">{p}</g>
</svg>'''


# ── feature vignettes: language-neutral pictures for the feature table ──────────

def frame(c, inner, w=420, h=236, extra=''):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">
{STYLE}
<style>
  .drop{{animation:drop 4.2s cubic-bezier(.25,1.3,.45,1) infinite}}
  @keyframes drop{{0%,12%{{opacity:0;transform:translateY(-10px) scale(.96)}}24%,82%{{opacity:1;transform:none}}92%,100%{{opacity:0;transform:none}}}}
  .scroll{{animation:scroll 18s linear infinite}}
  @keyframes scroll{{to{{transform:translateY(-400px)}}}}
  .pulse{{animation:pulse 2.4s ease-in-out infinite}}
  @keyframes pulse{{50%{{opacity:.35}}}}
  .slide{{animation:slide 8s cubic-bezier(.3,1.35,.45,1) infinite}}
  @keyframes slide{{0%,14%{{transform:translateX(0)}}20%,34%{{transform:translateX(48px)}}40%,54%{{transform:translateX(96px)}}60%,74%{{transform:translateX(144px)}}80%,94%{{transform:translateX(192px)}}100%{{transform:translateX(0)}}}}
  .lift{{animation:lift 5s cubic-bezier(.3,1.3,.5,1) infinite}}
  @keyframes lift{{0%,20%{{transform:none}}35%,80%{{transform:translate(-18px,-16px) scale(1.12)}}95%,100%{{transform:none}}}}
  .flow{{animation:flow 1.6s linear infinite}}
  @keyframes flow{{to{{stroke-dashoffset:-20}}}}
</style>
{defs(c, RAINBOW(c) + extra)}
<rect width="{w}" height="{h}" rx="18" fill="url(#bg)"/>
<rect x=".5" y=".5" width="{w-1}" height="{h-1}" rx="17.5" fill="none" stroke="{c['edge']}"/>
{inner}
</svg>'''


def v_where(c):
    bars = ''.join(f'<rect x="48" y="{40+i*20}" width="{w}" height="8" rx="4" fill="{c["bar"]}"/>' for i, w in enumerate([150, 230, 200]))
    caret = f'<rect x="48" y="104" width="90" height="8" rx="4" fill="{c["bar2"]}"/><rect class="caret" x="144" y="99" width="2" height="18" fill="{c["accent"]}"/>'
    mini = f'''<g class="drop" style="transform-origin:150px 122px"><g filter="url(#sh2)"><rect x="128" y="124" width="250" height="96" rx="12" fill="url(#pf)" stroke="{c['edge']}"/></g>
<rect x="140" y="136" width="120" height="8" rx="4" fill="{c['bar']}"/>
<line x1="160" y1="154" x2="160" y2="214" stroke="{c['rail']}"/>
<rect x="170" y="152" width="196" height="26" rx="7" fill="{c['sel']}" stroke="{c['accent']}" stroke-opacity=".35"/><circle cx="160" cy="165" r="4" fill="{c['accent']}"/>
<rect x="180" y="161" width="150" height="7" rx="3.5" fill="{c['bar2']}"/>
<circle cx="160" cy="195" r="3" fill="{c['link']}"/><rect x="180" y="191" width="110" height="7" rx="3.5" fill="{c['bar']}"/></g>'''
    return frame(c, bars + caret + mini)


def v_history(c):
    rows = ''
    hues = [c['text'], c['link'], c['shot'], c['code'], c['image'], c['files'], c['text'], c['link'], c['code'], c['shot'], c['text'], c['image'], c['link'], c['files'], c['text'], c['code'], c['shot'], c['link'], c['text'], c['image']]
    for i, h in enumerate(hues):
        y = 30 + i * 20
        rows += f'<circle cx="92" cy="{y}" r="3.2" fill="{h}"/><rect x="108" y="{y-4}" width="{[220,160,250,190,140,230][i%6]}" height="8" rx="4" fill="{c["bar"] if i%3 else c["bar2"]}"/><rect x="40" y="{y-3}" width="28" height="6" rx="3" fill="{c["bar"]}"/>'
    clip = f'<clipPath id="cl"><rect x="20" y="16" width="380" height="204" rx="10"/></clipPath>'
    fade = f'<linearGradient id="fd" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{c["bg1"]}" stop-opacity="1"/><stop offset=".22" stop-color="{c["bg1"]}" stop-opacity="0"/><stop offset=".78" stop-color="{c["bg2"]}" stop-opacity="0"/><stop offset="1" stop-color="{c["bg2"]}" stop-opacity="1"/></linearGradient>'
    inner = f'''<g clip-path="url(#cl)"><line x1="92" y1="0" x2="92" y2="236" stroke="{c['rail']}"/><g class="scroll">{rows}<g transform="translate(0 400)">{rows}</g></g></g>
<rect x="1" y="1" width="418" height="234" rx="17" fill="url(#fd)"/>'''
    return frame(c, inner, extra=clip + fade)


def v_ocr(c):
    doc = f'''<g filter="url(#sh2)"><rect x="70" y="58" width="280" height="160" rx="10" fill="{c['slate']}"/></g>
<rect x="84" y="70" width="252" height="136" rx="5" fill="#FBFBFA"/>
<rect x="100" y="86" width="96" height="14" rx="3" fill="#23201D"/>
<rect x="268" y="88" width="52" height="7" rx="3" fill="#23201D" opacity=".55"/>
{''.join(f'<rect x="100" y="{120+i*16}" width="{w}" height="6" rx="3" fill="#23201D" opacity=".3"/><rect x="284" y="{120+i*16}" width="36" height="6" rx="3" fill="#23201D" opacity=".45"/>' for i, w in enumerate([120, 96, 110]))}
<rect x="236" y="176" width="84" height="18" rx="2" fill="#3B8BEB"/>
<rect class="pulse" x="94" y="80" width="108" height="26" rx="5" fill="{c['accent']}" fill-opacity=".14" stroke="{c['accent']}" stroke-width="2"/>
<path d="M76 66 V58 H84 M344 58 H336 M344 58 V66" fill="none" stroke="{c['accent']}" stroke-width="2"/>'''
    field = f'''<g filter="url(#sh2)"><rect x="130" y="16" width="230" height="34" rx="10" fill="url(#pf)" stroke="{c['edge']}"/></g>
<circle cx="150" cy="32" r="6" fill="none" stroke="{c['accent']}" stroke-width="1.8"/><line x1="154.5" y1="36.5" x2="159" y2="41" stroke="{c['accent']}" stroke-width="1.8" stroke-linecap="round"/>
<rect x="170" y="28" width="70" height="8" rx="4" fill="{c['accent']}" opacity=".85"/><rect class="caret" x="246" y="24" width="1.6" height="16" fill="{c['accent']}"/>'''
    return frame(c, doc + field)


def v_kinds(c):
    glyphs = [
        '<path d="M9 11h14M9 16h14M9 21h9" stroke="#fff" stroke-width="2.2" stroke-linecap="round"/>',
        '<path d="M12 20l8-8M14 12h6v6" stroke="#fff" stroke-width="2.2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>',
        '<path d="M8 23l6-7 4 4 3-3 4 6z" fill="#fff"/><circle cx="21" cy="11" r="2.5" fill="#fff"/>',
        '<path d="M8 12V8h4M20 8h4v4M24 20v4h-4M12 24H8v-4" stroke="#fff" stroke-width="2.2" fill="none"/>',
        '<path d="M7 11.5a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2V22a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2z" fill="#fff"/>',
        '<path d="M13 11l-4 5 4 5M19 11l4 5-4 5" stroke="#fff" stroke-width="2.2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>',
        '<circle cx="13" cy="13" r="5" fill="#fff"/><circle cx="19" cy="15" r="5" fill="#fff" opacity=".75"/><circle cx="14" cy="20" r="4.5" fill="#fff" opacity=".55"/>',
    ]
    hues = [c['text'], c['link'], c['image'], c['shot'], c['files'], c['code'], 'url(#rainbow)']
    tiles = ''
    for i, h in enumerate(hues):
        x = 54 + i * 48
        tiles += f'<g transform="translate({x} 64)"><rect width="32" height="32" rx="9" fill="{h}"/>{glyphs[i]}</g>'
    rows = ''
    for i, w in enumerate([200, 150, 220]):
        rows += f'<circle cx="76" cy="{150+i*28}" r="3.5" fill="{c["link"]}"/><rect x="92" y="{140+i*28}" width="20" height="20" rx="5.5" fill="{c["link"]}"/><rect x="120" y="{146+i*28}" width="{w}" height="8" rx="4" fill="{c["bar2"] if i == 0 else c["bar"]}"/>'
    ind = f'<g class="slide"><rect x="54" y="106" width="32" height="3" rx="1.5" fill="{c["accent"]}"/><rect x="50" y="60" width="40" height="40" rx="11" fill="none" stroke="{c["accent"]}" stroke-opacity=".55"/></g>'
    return frame(c, f'<line x1="76" y1="134" x2="76" y2="236" stroke="{c["rail"]}"/>' + tiles + ind + rows)


def v_private(c):
    pc = f'''<g filter="url(#sh2)"><rect x="70" y="40" width="220" height="136" rx="12" fill="{c['slate']}"/></g>
<rect x="82" y="52" width="196" height="112" rx="6" fill="url(#pf)"/>
<rect x="150" y="182" width="60" height="10" rx="3" fill="{c['bar2']}"/><rect x="130" y="192" width="100" height="6" rx="3" fill="{c['bar']}"/>
<rect x="158" y="98" width="44" height="38" rx="8" fill="{c['accent']}"/><path d="M166 98 V88 a14 14 0 0 1 28 0 V98" fill="none" stroke="{c['accent']}" stroke-width="5"/>
<circle cx="180" cy="115" r="4.5" fill="#fff"/><rect x="178" y="116" width="4" height="10" rx="2" fill="#fff"/>
<g class="pulse" transform="translate(316 86)"><path d="M6 30 a18 18 0 0 1 4-35 a22 22 0 0 1 40 6 a14 14 0 0 1 2 29 z" fill="none" stroke="{c['ink3']}" stroke-width="2.5" stroke-linejoin="round"/>
<line x1="-4" y1="-14" x2="60" y2="40" stroke="{c['accent']}" stroke-width="3" stroke-linecap="round"/></g>'''
    return frame(c, pc)


def v_library(c):
    cards = ''
    k = [(c['paper'], c['bar2']), (c['slate'], '#5CD6B4'), ('#0F6CBD', '#ffffff'), (c['paper'], c['link']), ('#6B2D5C', '#F6B26B'), (c['slate'], '#A78BFA')]
    for i, (fill, acc) in enumerate(k):
        x = 40 + (i % 3) * 118
        y = 36 + (i // 3) * 88
        cls = ' class="lift" style="transform-origin:{0}px {1}px"'.format(x + 51, y + 34) if i == 4 else ''
        cards += f'<g{cls}><g filter="url(#sh2)"><rect x="{x}" y="{y}" width="102" height="70" rx="9" fill="{fill}" stroke="{c["edge"]}"/></g><rect x="{x+10}" y="{y+12}" width="44" height="6" rx="3" fill="{acc}" opacity=".8"/><rect x="{x+10}" y="{y+28}" width="{70 - (i%2)*20}" height="6" rx="3" fill="{acc}" opacity=".45"/></g>'
    return frame(c, cards)


def arch(theme):
    c = T[theme]
    W, H = 1100, 470
    def box(x, y, w, h, title, lines, hue):
        t = f'<g filter="url(#sh2)"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="14" fill="url(#pf)" stroke="{c["edge"]}"/></g><rect x="{x}" y="{y+16}" width="3" height="22" rx="1.5" fill="{hue}"/>'
        t += f'<text x="{x+20}" y="{y+33}" font-family="{FONT}" font-size="16" font-weight="600" fill="{c["ink"]}">{title}</text>'
        for i, ln in enumerate(lines):
            t += f'<text x="{x+20}" y="{y+58+i*20}" font-family="{FONT}" font-size="12.5" fill="{c["ink2"]}">{ln}</text>'
        return t
    def wire(d, hue):
        return f'<path d="{d}" fill="none" stroke="{c["rail"]}" stroke-width="2"/><path class="flow" d="{d}" fill="none" stroke="{hue}" stroke-width="2" stroke-dasharray="4 16" stroke-linecap="round"/>'
    b = ''
    b += box(40, 60, 230, 130, 'Windows clipboard', ['AddClipboardFormatListener', 'raw formats, HTML, RTF', 'privacy flags honoured'], c['text'])
    b += box(40, 280, 230, 130, 'Screenshots folder', ['FileSystemWatcher', 'Win + PrtScn, Snipping Tool', 'imported on first run'], c['shot'])
    b += box(400, 150, 300, 170, 'Archive', ['SQLite · WAL · synchronous', 'FTS5 trigram index', 'images stored once (SHA-256)', 'daily snapshot + .zip export'], c['accent'])
    b += box(830, 40, 230, 120, 'Win + V panel', ['low-level keyboard hook', 'opens at the text caret'], c['link'])
    b += box(830, 180, 230, 120, 'Library', ['every clip, by day', 'lightbox, pin, restore'], c['files'])
    b += box(830, 320, 230, 120, 'Windows OCR', ['Windows.Media.Ocr', 'offline, on your PC'], c['code'])
    w = ''
    w += wire('M270 125 C 335 125, 335 200, 400 200', c['text'])
    w += wire('M270 345 C 335 345, 335 270, 400 270', c['shot'])
    w += wire('M700 200 C 765 200, 765 100, 830 100', c['link'])
    w += wire('M700 235 C 765 235, 765 240, 830 240', c['files'])
    w += wire('M830 380 C 765 380, 765 290, 700 290', c['code'])
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" role="img" aria-label="SweaxClip architecture">
{STYLE}
<style>.flow{{animation:flow 1.4s linear infinite}}@keyframes flow{{to{{stroke-dashoffset:-20}}}}</style>
{defs(c)}
<rect width="{W}" height="{H}" rx="20" fill="url(#bg)"/>
<rect x=".5" y=".5" width="{W-1}" height="{H-1}" rx="19.5" fill="none" stroke="{c['edge']}"/>
{w}{b}
</svg>'''


if __name__ == '__main__':
    OUT = sys.argv[1]
    os.makedirs(OUT, exist_ok=True)
    for theme in ('dark', 'light'):
        for lang in ('en', 'tr'):
            open(os.path.join(OUT, f'banner-{lang}-{theme}.svg'), 'w', encoding='utf-8').write(banner(theme, lang))
        c = T[theme]
        for name, fn in (('where', v_where), ('history', v_history), ('ocr', v_ocr), ('kinds', v_kinds), ('private', v_private), ('library', v_library)):
            open(os.path.join(OUT, f'feature-{name}-{theme}.svg'), 'w', encoding='utf-8').write(fn(c))
        open(os.path.join(OUT, f'architecture-{theme}.svg'), 'w', encoding='utf-8').write(arch(theme))
    print('ok')
