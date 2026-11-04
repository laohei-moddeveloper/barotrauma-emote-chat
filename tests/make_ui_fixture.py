"""Original synthetic art and text atlas for the production painter's visual fixture."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
out = Path('.test-fixtures')
out.mkdir(exist_ok=True)
font_paths = [Path('C:/Windows/Fonts/msyh.ttc'), Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')]
font_path = next(p for p in font_paths if p.exists())
labels = ['表情包  /  Emotes', '把想说的，变成一张图。', '全部', '最近', '搜索你的表情…', '预览', '开心一下', '选择一张表情发送', '动画开关', '刷新', '发送', '接收表情', '×']
atlas = Image.new('RGBA', (720, len(labels)*48))
draw = ImageDraw.Draw(atlas)
for i, text in enumerate(labels):
    draw.text((0, i*48+5), text, font=ImageFont.truetype(str(font_path), 29 if i == 0 else 18), fill=(241,244,250,255) if i != 1 else (151,160,178,255))
atlas.save(out/'ui-labels.png')
art = Image.new('RGBA', (128*8,128))
draw = ImageDraw.Draw(art)
colors = ['#ffd878','#ffb8c8','#b9acff','#8ee4d0','#96c9ff','#ffad83','#e0bdff','#d5e889']
for i, color in enumerate(colors):
    x=i*128
    draw.rounded_rectangle((x+12,12,x+116,116),28,fill=color)
    if i%3==0:
        draw.arc((x+30,42,x+48,60),190,350,fill='#252b37',width=4)
        draw.arc((x+78,42,x+96,60),190,350,fill='#252b37',width=4)
    else:
        draw.ellipse((x+35,43,x+43,54),fill='#252b37')
        draw.ellipse((x+85,43,x+93,54),fill='#252b37')
    draw.arc((x+43,61,x+85,86),0,180,fill='#252b37',width=4)
    draw.ellipse((x+21,63,x+38,73),fill='#ec8f9f')
    draw.ellipse((x+90,63,x+107,73),fill='#ec8f9f')
art.save(out/'ui-emotes.png')
