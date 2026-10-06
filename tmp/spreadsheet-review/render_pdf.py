from pathlib import Path

import pypdfium2 as pdfium
from PIL import Image, ImageDraw

source = Path(r"C:\Users\Admin\source\repos\OrderPaymentMicroservice\ATPM\Hướng dẫn tạo Spec file dạng excel.pdf")
output = Path(r"C:\Users\Admin\source\repos\OrderPaymentMicroservice\tmp\spreadsheet-review\pdf-preview")
output.mkdir(parents=True, exist_ok=True)
pdf = pdfium.PdfDocument(source)
thumbs = []
for i, page in enumerate(pdf):
    image = page.render(scale=0.55).to_pil().convert("RGB")
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 80, 24), fill="white")
    draw.text((6, 5), f"Page {i + 1}", fill="black")
    thumbs.append(image)

for group_start in range(0, len(thumbs), 7):
    group = thumbs[group_start:group_start + 7]
    width = max(im.width for im in group)
    height = sum(im.height for im in group)
    contact = Image.new("RGB", (width, height), "white")
    y = 0
    for im in group:
        contact.paste(im, (0, y))
        y += im.height
    path = output / f"pages-{group_start + 1:02d}-{group_start + len(group):02d}.png"
    contact.save(path)
    print(path)
