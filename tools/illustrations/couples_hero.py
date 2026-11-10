"""Generates src/Couples.Web/wwwroot/images/couples-hero.svg, the landing page hero.

Original artwork drawn for BridgeTogether; see docs/design/illustrations.md.
Usage: python3 tools/illustrations/couples_hero.py src/Couples.Web/wwwroot/images/couples-hero.svg
"""
import sys
BASE=392
out=[]
def e(s): out.append(s)

def person(x,h,w,skin,top,bottom,hair,haircol,inner_dir,hold=None,dress=False,plus=False,shoe="#2F2925",extra=None,outer_hold=None):
    r=h*0.085
    top_y=BASE-h
    hy=top_y+r
    sh=top_y+2*r+8
    hip=BASE-h*0.46
    lw=w*0.27
    g=[]
    # back hair
    if hair in("long","longwave"):
        g.append(f'<path d="M{x-r*1.15},{hy} Q{x-r*1.3},{sh+r*1.6} {x-r*0.6},{sh+r*1.5} L{x+r*0.6},{sh+r*1.5} Q{x+r*1.3},{sh+r*1.6} {x+r*1.15},{hy} Z" fill="{haircol}"/>')
    if hair=="afro":
        g.append(f'<circle cx="{x}" cy="{hy-r*0.25}" r="{r*1.55}" fill="{haircol}"/>')
    if hair=="bun":
        g.append(f'<circle cx="{x}" cy="{top_y-r*0.35}" r="{r*0.55}" fill="{haircol}"/>')
    if hair=="hijab":
        g.append(f'<path d="M{x-r*1.3},{hy} A{r*1.3},{r*1.3} 0 0 1 {x+r*1.3},{hy} L{x+r*1.25},{sh+r*0.9} Q{x},{sh+r*1.5} {x-r*1.25},{sh+r*0.9} Z" fill="{haircol}"/>')
    # legs
    legtop=hip-4
    lx1,lx2=x-w*0.2,x+w*0.2
    legcol = skin if dress else bottom
    for lx in (lx1,lx2):
        g.append(f'<line x1="{lx}" y1="{legtop}" x2="{lx}" y2="{BASE-6}" stroke="{legcol}" stroke-width="{lw}" stroke-linecap="round"/>')
        g.append(f'<ellipse cx="{lx+ (4 if lx>x else -4)}" cy="{BASE-3}" rx="{lw*0.75}" ry="{lw*0.38}" fill="{shoe}"/>')
    # torso
    hw=w/2; bw=hw*(1.12 if plus else 0.92)
    if dress:
        hem=BASE-h*0.2
        g.append(f'<path d="M{x-hw+6},{sh} L{x+hw-6},{sh} Q{x+hw+2},{sh} {x+hw},{sh+12} L{x+hw*0.85},{hip-10} L{x+hw*1.35},{hem} L{x-hw*1.35},{hem} L{x-hw*0.85},{hip-10} L{x-hw},{sh+12} Q{x-hw-2},{sh} {x-hw+6},{sh} Z" fill="{top}"/>')
    else:
        g.append(f'<rect x="{x-bw}" y="{hip-14}" width="{bw*2}" height="{22}" rx="9" fill="{bottom}"/>')
        g.append(f'<path d="M{x-hw+8},{sh} L{x+hw-8},{sh} Q{x+hw+1},{sh} {x+hw},{sh+12} C{x+bw+ (8 if plus else 0)},{hip-28} {x+bw},{hip-8} {x+bw-6},{hip} L{x-bw+6},{hip} C{x-bw},{hip-8} {x-bw-(8 if plus else 0)},{hip-28} {x-hw},{sh+12} Q{x-hw-1},{sh} {x-hw+8},{sh} Z" fill="{top}"/>')
    # neck & head
    g.append(f'<rect x="{x-r*0.38}" y="{hy+r*0.6}" width="{r*0.76}" height="{sh-hy-r*0.4}" rx="3" fill="{skin}"/>')
    if hair=="hijab":
        g.append(f'<circle cx="{x}" cy="{hy+1}" r="{r*0.86}" fill="{skin}"/>')
    else:
        g.append(f'<circle cx="{x}" cy="{hy}" r="{r}" fill="{skin}"/>')
    # front hair
    if hair in("short","long","longwave","bun","gray","fade"):
        g.append(f'<path d="M{x-r*1.04},{hy+r*0.15} A{r*1.04},{r*1.08} 0 0 1 {x+r*1.04},{hy+r*0.15} Q{x+r*0.4},{hy-r*0.55} {x-r*0.2},{hy-r*0.4} Q{x-r*0.8},{hy-r*0.2} {x-r*1.04},{hy+r*0.15} Z" fill="{haircol}"/>')
    if hair=="bald":
        g.append(f'<path d="M{x-r*1.02},{hy+r*0.25} Q{x-r*1.1},{hy-r*0.2} {x-r*0.75},{hy-r*0.35} L{x-r*0.85},{hy+r*0.3} Z" fill="{haircol}"/><path d="M{x+r*1.02},{hy+r*0.25} Q{x+r*1.1},{hy-r*0.2} {x+r*0.75},{hy-r*0.35} L{x+r*0.85},{hy+r*0.3} Z" fill="{haircol}"/>')
    if hair=="afro":
        g.append(f'<path d="M{x-r*1.0},{hy} A{r},{r*0.9} 0 0 1 {x+r*1.0},{hy} Q{x},{hy-r*0.5} {x-r},{hy} Z" fill="{haircol}"/>')
    if hair=="hijab":
        g.append(f'<path d="M{x-r*0.95},{hy+r*0.1} A{r*0.95},{r*0.95} 0 0 1 {x+r*0.95},{hy+r*0.1} Q{x},{hy-r*0.6} {x-r*0.95},{hy+r*0.1} Z" fill="{haircol}"/>')
    if extra=="beard":
        g.append(f'<path d="M{x-r*0.85},{hy+r*0.2} Q{x-r*0.8},{hy+r*1.12} {x},{hy+r*1.12} Q{x+r*0.8},{hy+r*1.12} {x+r*0.85},{hy+r*0.2} Q{x+r*0.6},{hy+r*0.75} {x},{hy+r*0.62} Q{x-r*0.6},{hy+r*0.75} {x-r*0.85},{hy+r*0.2} Z" fill="{haircol}"/>')
    if extra=="glasses":
        g.append(f'<g fill="none" stroke="#2F2925" stroke-width="1.8"><circle cx="{x-r*0.38}" cy="{hy+r*0.12}" r="{r*0.27}"/><circle cx="{x+r*0.38}" cy="{hy+r*0.12}" r="{r*0.27}"/><line x1="{x-r*0.11}" y1="{hy+r*0.12}" x2="{x+r*0.11}" y2="{hy+r*0.12}"/></g>')
    # arms
    aw=w*0.22
    for d in (-1,1):
        sx=x+d*(hw-aw*0.35); sy=sh+aw*0.55
        if d==inner_dir and hold:
            ex,ey=hold
        elif d==-inner_dir and outer_hold:
            ex,ey=outer_hold
        else:
            ex,ey=x+d*(hw+aw*0.25+(6 if plus else 0)),hip+h*0.06
        mx,my=sx+(ex-sx)*0.45,sy+(ey-sy)*0.45
        g.append(f'<line x1="{sx}" y1="{sy}" x2="{ex}" y2="{ey}" stroke="{skin}" stroke-width="{aw*0.8}" stroke-linecap="round"/>')
        g.append(f'<line x1="{sx}" y1="{sy}" x2="{mx}" y2="{my}" stroke="{top}" stroke-width="{aw}" stroke-linecap="round"/>')
        g.append(f'<circle cx="{ex}" cy="{ey}" r="{aw*0.52}" fill="{skin}"/>')
    e('<g>'+''.join(g)+'</g>')
    return hip

def couple(x1,p1,x2,p2,holdy):
    x2=x1+(p1["w"]+p2["w"])/2+30
    mid=(x1+x2)/2
    d1=1 if x2>x1 else -1
    person(x1,inner_dir=d1,hold=(mid-3,holdy),**p1)
    person(x2,inner_dir=-d1,hold=(mid+3,holdy),**p2)

def wheelchair(x,skin,top,bottom,hair,haircol,hold,facing=1,extra=None):
    g=[]
    seat=BASE-92
    wx=x-facing*6
    # big wheel
    g.append(f'<circle cx="{wx}" cy="{BASE-46}" r="46" fill="none" stroke="#3B4A5A" stroke-width="7"/>')
    g.append(f'<circle cx="{wx}" cy="{BASE-46}" r="36" fill="none" stroke="#3B4A5A" stroke-width="2" opacity=".6"/>')
    g.append(f'<circle cx="{wx}" cy="{BASE-46}" r="5" fill="#3B4A5A"/>')
    g.append(f'<circle cx="{x+facing*58}" cy="{BASE-9}" r="9" fill="none" stroke="#3B4A5A" stroke-width="5"/>')
    g.append(f'<path d="M{x-facing*30},{seat-70} L{x-facing*24},{seat} L{x+facing*44},{seat} L{x+facing*58},{BASE-18}" fill="none" stroke="#3B4A5A" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>')
    e('<g>'+''.join(g)+'</g>')
    # person seated
    w=58; r=21; hw=w/2
    hip=seat-6
    sh=hip-82
    hy=sh-8-r
    g=[]
    # thighs and shins
    knee=x+facing*52
    g.append(f'<line x1="{x}" y1="{hip}" x2="{knee}" y2="{hip}" stroke="{bottom}" stroke-width="17" stroke-linecap="round"/>')
    g.append(f'<line x1="{knee}" y1="{hip}" x2="{knee+facing*4}" y2="{BASE-20}" stroke="{bottom}" stroke-width="16" stroke-linecap="round"/>')
    g.append(f'<ellipse cx="{knee+facing*12}" cy="{BASE-16}" rx="13" ry="6" fill="#2F2925"/>')
    g.append(f'<path d="M{x-hw+8},{sh} L{x+hw-8},{sh} Q{x+hw+1},{sh} {x+hw},{sh+12} L{x+hw-2},{hip+6} L{x-hw+2},{hip+6} L{x-hw},{sh+12} Q{x-hw-1},{sh} {x-hw+8},{sh} Z" fill="{top}"/>')
    g.append(f'<rect x="{x-r*0.38}" y="{hy+r*0.6}" width="{r*0.76}" height="{sh-hy-r*0.4}" rx="3" fill="{skin}"/>')
    g.append(f'<circle cx="{x}" cy="{hy}" r="{r}" fill="{skin}"/>')
    if hair=="short":
        g.append(f'<path d="M{x-r*1.04},{hy+r*0.15} A{r*1.04},{r*1.08} 0 0 1 {x+r*1.04},{hy+r*0.15} Q{x+r*0.4},{hy-r*0.55} {x-r*0.2},{hy-r*0.4} Q{x-r*0.8},{hy-r*0.2} {x-r*1.04},{hy+r*0.15} Z" fill="{haircol}"/>')
    if extra=="beard":
        g.append(f'<path d="M{x-r*0.85},{hy+r*0.2} Q{x-r*0.8},{hy+r*1.12} {x},{hy+r*1.12} Q{x+r*0.8},{hy+r*1.12} {x+r*0.85},{hy+r*0.2} Q{x+r*0.6},{hy+r*0.75} {x},{hy+r*0.62} Q{x-r*0.6},{hy+r*0.75} {x-r*0.85},{hy+r*0.2} Z" fill="{haircol}"/>')
    aw=13
    for d in (-1,1):
        sx=x+d*(hw-4); sy=sh+7
        ex,ey = hold if d==facing else (x+d*(hw+2), hip-6)
        mx,my=sx+(ex-sx)*0.45,sy+(ey-sy)*0.45
        g.append(f'<line x1="{sx}" y1="{sy}" x2="{ex}" y2="{ey}" stroke="{skin}" stroke-width="{aw*0.8}" stroke-linecap="round"/>')
        g.append(f'<line x1="{sx}" y1="{sy}" x2="{mx}" y2="{my}" stroke="{top}" stroke-width="{aw}" stroke-linecap="round"/>')
        g.append(f'<circle cx="{ex}" cy="{ey}" r="7" fill="{skin}"/>')
    e('<g>'+''.join(g)+'</g>')

S=["#F7D9C2","#EDBE98","#D29A6E","#A86E47","#7A4B2F","#583524"]
TC="#A94A33"; TCL="#C76B53"; SAGE="#4F6B5A"; SAGEL="#8AA593"; MUST="#D9A441"; NAVY="#3B4A5A"; CREAM="#EADBC8"; PLUM="#7A4A5E"; DARK="#2F2925"; DENIM="#5E7186"
W,H=1440,420
e(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" role="img" aria-labelledby="t d">')
e('<title id="t">Many kinds of couples</title><desc id="d">An illustration of six couples holding hands: an older Black couple, two women, a woman in a hijab with her partner, a man using a wheelchair with his partner, two men, and a plus-size couple.</desc>')
# background
e(f'<path d="M40,{BASE} C40,250 180,150 390,170 C560,186 620,90 820,100 C1020,110 1080,200 1220,190 C1340,182 1400,280 1395,{BASE} Z" fill="#F3E2D3"/>')
e(f'<ellipse cx="720" cy="{BASE+2}" rx="700" ry="12" fill="#EAD3C1"/>')
# leaves
def leaf(x,y,rot,s=1,col=SAGEL):
    e(f'<g transform="translate({x},{y}) rotate({rot}) scale({s})"><path d="M0,0 C14,-30 14,-62 0,-90 C-14,-62 -14,-30 0,0 Z" fill="{col}" opacity=".55"/><line x1="0" y1="0" x2="0" y2="-80" stroke="#FAF5EE" stroke-width="2" opacity=".7"/></g>')
leaf(52,BASE,-28,1.1); leaf(78,BASE,-6,1.4); leaf(104,BASE,20,1.0)
leaf(1360,BASE,-18,1.2); leaf(1388,BASE,14,1.45); leaf(1410,BASE,34,0.9)
def heart(x,y,s,c=TCL,o=.8):
    e(f'<path transform="translate({x},{y}) scale({s})" d="M0,6 C-10,-4 -12,-14 -5,-16 C-1,-17 0,-13 0,-11 C0,-13 1,-17 5,-16 C12,-14 10,-4 0,6 Z" fill="{c}" opacity="{o}"/>')
heart(400,70,1.4); heart(720,48,1.1,TC,.6); heart(1060,80,1.3); heart(200,140,.9,MUST,.7); heart(1240,130,1.0,MUST,.7)

# 1 older Black couple
couple(140,dict(h=250,w=60,skin=S[5],top=SAGE,bottom=NAVY,hair="bald",haircol="#C9C3BB",extra="glasses"),
       0,dict(h=232,w=58,skin=S[4],top=MUST,bottom=TC,hair="afro",haircol="#D8D2CA",dress=True),BASE-128)
# 2 two women
couple(360,dict(h=246,w=60,skin=S[3],top=TC,bottom=DENIM,hair="longwave",haircol="#2A1E18"),
       0,dict(h=240,w=76,skin=S[0],top=CREAM,bottom=SAGE,hair="bun",haircol="#B5652F",plus=True),BASE-126)
# 3 hijab + partner
couple(590,dict(h=238,w=58,skin=S[2],top=PLUM,bottom=PLUM,hair="hijab",haircol=SAGE,dress=True),
       0,dict(h=262,w=62,skin=S[2],top=NAVY,bottom="#C2A887",hair="short",haircol="#1E1714",extra="beard"),BASE-128)
# 4 wheelchair + partner
wheelchair(810,S[1],MUST,SAGE,"short","#5A3A22",hold=(888,BASE-132),facing=1,extra="beard")
person(928,h=244,w=60,skin=S[4],top=TCL,bottom=DARK,hair="long",haircol="#1B1412",inner_dir=-1,hold=(892,BASE-132))
# 5 two men
couple(1050,dict(h=256,w=62,skin=S[1],top=SAGEL,bottom=NAVY,hair="short",haircol="#141010"),
       0,dict(h=250,w=62,skin=S[2],top=TC,bottom="#C2A887",hair="gray",haircol="#9C958E",extra="glasses"),BASE-128)
# 6 plus-size couple
couple(1240,dict(h=258,w=82,skin=S[0],top=NAVY,bottom=DENIM,hair="short",haircol="#C9A35E",plus=True,extra="beard"),
       0,dict(h=236,w=72,skin=S[3],top=MUST,bottom=TC,hair="long",haircol="#2A1E18",plus=True,dress=True),BASE-124)
e('</svg>')
open(sys.argv[1],'w').write('\n'.join(out))
