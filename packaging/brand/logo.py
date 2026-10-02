"""Logo V-Max / Maxine : squircle ruby dégradé + monogramme M + étincelle. Rendu Pillow, export .ico + .png."""
from PIL import Image, ImageDraw, ImageFilter
import math, os

S = 1024              # canevas haute résolution
pad = 96
A = (255, 92, 122)    # ruby clair (haut)
B = (193, 33, 69)     # ruby profond (bas)
INK = (24, 20, 28)    # ombre interne
WHITE = (253, 244, 246)

def lerp(a, b, t): return tuple(round(a[i] + (b[i]-a[i])*t) for i in range(3))

def squircle_mask(size, radius):
    """Masque alpha d'un carré à coins très arrondis (superéllipse approximée par rounded-rect lissé)."""
    m = Image.new("L", (size*4, size*4), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, size*4-1, size*4-1], radius=radius*4, fill=255)
    m = m.resize((size, size), Image.LANCZOS)
    return m

# --- fond dégradé vertical
grad = Image.new("RGB", (S, S))
gp = grad.load()
for y in range(S):
    c = lerp(A, B, y/(S-1))
    for x in range(S):
        gp[x, y] = c

# squircle
inner = S - pad*2
mask = squircle_mask(inner, int(inner*0.30))
full_mask = Image.new("L", (S, S), 0)
full_mask.paste(mask, (pad, pad))

logo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
logo.paste(grad, (0, 0), full_mask)

# halo lumineux en haut (effet verre)
gloss = Image.new("L", (S, S), 0)
gd = ImageDraw.Draw(gloss)
gd.ellipse([pad-40, pad-inner*0.35, S-pad+40, pad+inner*0.55], fill=70)
gloss = gloss.filter(ImageFilter.GaussianBlur(40))
gloss_layer = Image.new("RGBA", (S, S), (255, 255, 255, 0))
gloss_layer.putalpha(Image.composite(gloss, Image.new("L", (S, S), 0), full_mask))
logo = Image.alpha_composite(logo, gloss_layer)

# --- monogramme M (dessiné en supersampling)
def draw_M(scale, color, dx=0, dy=0, w_mul=1.0):
    big = Image.new("RGBA", (S*scale, S*scale), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    cx = S*scale/2 + dx*scale
    top = (pad+inner*0.30)*scale + dy*scale
    bot = (pad+inner*0.74)*scale + dy*scale
    half = inner*0.26*scale
    valley = top + (bot-top)*0.62
    w = int(inner*0.115*scale*w_mul)
    xL, xR = cx-half, cx+half
    inL, inR = cx-half*0.52, cx+half*0.52
    def seg(p, q):
        d.line([p, q], fill=color, width=w)
        r = w//2
        for pt in (p, q):
            d.ellipse([pt[0]-r, pt[1]-r, pt[0]+r, pt[1]+r], fill=color)
    seg((xL, bot), (xL, top))                 # jambe gauche
    seg((xR, bot), (xR, top))                 # jambe droite
    seg((xL, top), ((xL+inL)/2+ (cx-inL)*0.0, top))  # petit plat haut gauche (évite angle sec)
    seg((xL, top), (cx, valley))              # diagonale gauche
    seg((xR, top), (cx, valley))              # diagonale droite
    return big.resize((S, S), Image.LANCZOS)

# ombre portée douce du M
shadow = draw_M(4, (0, 0, 0, 90), dy=10, w_mul=1.05).filter(ImageFilter.GaussianBlur(6))
logo = Image.alpha_composite(logo, Image.composite(shadow, Image.new("RGBA", (S, S), (0,0,0,0)), full_mask))
logo = Image.alpha_composite(logo, draw_M(4, WHITE + (255,)))

# --- étincelle (4 branches) en haut à droite
def spark(cx, cy, r, color):
    big = Image.new("RGBA", (S*4, S*4), (0,0,0,0))
    d = ImageDraw.Draw(big)
    cx4, cy4, r4 = cx*4, cy*4, r*4
    k = r4*0.28
    pts = [(cx4, cy4-r4), (cx4+k, cy4-k), (cx4+r4, cy4), (cx4+k, cy4+k),
           (cx4, cy4+r4), (cx4-k, cy4+k), (cx4-r4, cy4), (cx4-k, cy4-k)]
    d.polygon(pts, fill=color)
    return big.resize((S, S), Image.LANCZOS)

sp_cx, sp_cy = pad+inner*0.80, pad+inner*0.26
glow = spark(sp_cx, sp_cy, inner*0.14, WHITE+(120,)).filter(ImageFilter.GaussianBlur(10))
logo = Image.alpha_composite(logo, glow)
logo = Image.alpha_composite(logo, spark(sp_cx, sp_cy, inner*0.085, WHITE+(255,)))

out = r"D:\VMAX\VPet-Simulator.Windows"
os.makedirs(os.path.join(out, "Res"), exist_ok=True)
logo.save(os.path.join(out, "Res", "maxine-logo.png"))
logo.resize((512, 512), Image.LANCZOS).save(os.path.join(out, "Res", "maxine-logo-512.png"))
# .ico multi-tailles
sizes = [16, 24, 32, 48, 64, 128, 256]
logo.save(os.path.join(out, "maxine.ico"), sizes=[(s, s) for s in sizes])
# aperçu pour QA
logo.resize((256, 256), Image.LANCZOS).save(r"C:\Users\PC\AppData\Local\Temp\claude\D--VMAX\5e51d53c-44f4-4e0c-ad62-932bcfdc84b7\scratchpad\logo-preview.png")
print("logo généré : maxine.ico + Res/maxine-logo.png")
