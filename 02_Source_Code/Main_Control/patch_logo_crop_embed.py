# -*- coding: utf-8 -*-
"""
Patch DATN Web Dashboard logo:
- Cắt bỏ viền trắng thừa trong file logo gốc.
- Tạo logo vuông đã crop, không dùng transform scale gây cắt mép.
- Nhúng trực tiếp logo đã crop vào HTML bằng base64 data URI.
- Giữ khung logo màu trắng.

Cách chạy:
  python patch_logo_crop_embed.py
hoặc:
  python patch_logo_crop_embed.py "D:\\DATN\\datn_web_dashboard_v7_real_camera.py"
"""

from __future__ import annotations

import base64
import os
import re
import shutil
import sys
import time
from pathlib import Path

TARGET_DEFAULT = "datn_web_dashboard_v7_real_camera.py"
SCHOOL_LOGO = r"D:\DATN\logotruong.png"
FACULTY_LOGO = r"D:\DATN\logokhoa.png"
OUT_SCHOOL_LOGO = r"D:\DATN\logotruong_crop_fill.png"
OUT_FACULTY_LOGO = r"D:\DATN\logokhoa_crop_fill.png"

CSS_OVERRIDE = r"""
    /* ===================== DATN LOGO CROP EMBED FIX ===================== */
    .thesis-header{
      grid-template-columns:124px minmax(0,1fr) 124px!important;
      min-height:142px!important;
      padding:8px 14px!important;
    }

    .thesis-logo-box{
      width:116px!important;
      height:116px!important;
      min-height:116px!important;
      padding:3px!important;
      border-radius:16px!important;
      background:#ffffff!important;
      overflow:hidden!important;
      display:flex!important;
      align-items:center!important;
      justify-content:center!important;
      box-shadow:0 6px 22px rgba(0,0,0,.22)!important;
    }

    .thesis-logo-box img{
      width:100%!important;
      height:100%!important;
      max-width:100%!important;
      max-height:100%!important;
      object-fit:contain!important;
      display:block!important;
      transform:none!important;
      transform-origin:center center!important;
    }

    .thesis-logo-box .logo-fallback{
      display:none!important;
    }

    .school-name{
      font-size:17px!important;
      line-height:1.08!important;
      letter-spacing:.05em!important;
      white-space:nowrap!important;
    }

    .faculty-name{
      font-size:14px!important;
      margin-top:5px!important;
      letter-spacing:.12em!important;
    }

    .topic-title{
      font-size:23px!important;
      line-height:1.05!important;
      margin-top:10px!important;
    }

    .thesis-info{
      font-size:13.2px!important;
      line-height:1.18!important;
      margin-top:7px!important;
    }
"""


def find_target() -> Path:
    if len(sys.argv) >= 2:
        return Path(sys.argv[1]).expanduser().resolve()
    here = Path.cwd()
    p = here / TARGET_DEFAULT
    if p.exists():
        return p.resolve()
    matches = list(here.glob("datn_web_dashboard*.py"))
    if matches:
        return matches[0].resolve()
    return p.resolve()


def crop_with_pillow(src: str, dst: str, out_size: int = 700, pad_ratio: float = 0.035) -> bool:
    try:
        from PIL import Image, ImageChops, ImageStat
    except Exception:
        return False

    src_path = Path(src)
    if not src_path.exists():
        return False

    im = Image.open(src_path).convert("RGBA")
    w, h = im.size
    if w <= 1 or h <= 1:
        return False

    # Lấy màu nền từ 4 góc. Logo của bạn nền trắng nên cách này ổn định.
    corners = [im.getpixel((0, 0)), im.getpixel((w - 1, 0)), im.getpixel((0, h - 1)), im.getpixel((w - 1, h - 1))]
    bg = tuple(int(sum(c[i] for c in corners) / len(corners)) for i in range(4))

    # Tạo ảnh nền cùng màu rồi lấy diff.
    bg_img = Image.new("RGBA", im.size, bg)
    diff = ImageChops.difference(im, bg_img).convert("L")

    # Tăng tương phản mask để bắt cả chữ/logo nhạt.
    mask = diff.point(lambda p: 255 if p > 18 else 0)
    bbox = mask.getbbox()
    if not bbox:
        # fallback copy/resize square.
        bbox = (0, 0, w, h)

    x1, y1, x2, y2 = bbox
    bw, bh = x2 - x1, y2 - y1
    pad = int(max(bw, bh) * pad_ratio)
    x1 = max(0, x1 - pad)
    y1 = max(0, y1 - pad)
    x2 = min(w, x2 + pad)
    y2 = min(h, y2 + pad)

    # Crop theo hình vuông quanh vùng logo để khi fit vào khung không méo và không bị cắt.
    cx = (x1 + x2) / 2.0
    cy = (y1 + y2) / 2.0
    side = max(x2 - x1, y2 - y1)
    half = side / 2.0
    sx1 = int(round(cx - half))
    sy1 = int(round(cy - half))
    sx2 = int(round(cx + half))
    sy2 = int(round(cy + half))

    # Nếu vuông vượt biên, dịch vào lại.
    if sx1 < 0:
        sx2 -= sx1
        sx1 = 0
    if sy1 < 0:
        sy2 -= sy1
        sy1 = 0
    if sx2 > w:
        shift = sx2 - w
        sx1 -= shift
        sx2 = w
    if sy2 > h:
        shift = sy2 - h
        sy1 -= shift
        sy2 = h
    sx1 = max(0, sx1)
    sy1 = max(0, sy1)
    sx2 = min(w, sx2)
    sy2 = min(h, sy2)

    cropped = im.crop((sx1, sy1, sx2, sy2))

    # Đưa về nền trắng vuông cố định.
    canvas = Image.new("RGBA", (out_size, out_size), (255, 255, 255, 255))
    cropped.thumbnail((out_size, out_size), Image.LANCZOS)
    px = (out_size - cropped.size[0]) // 2
    py = (out_size - cropped.size[1]) // 2
    canvas.alpha_composite(cropped, (px, py))
    canvas = canvas.convert("RGB")

    Path(dst).parent.mkdir(parents=True, exist_ok=True)
    canvas.save(dst, "PNG", optimize=True)
    return True


def crop_with_cv2(src: str, dst: str, out_size: int = 700, pad_ratio: float = 0.035) -> bool:
    try:
        import cv2
        import numpy as np
    except Exception:
        return False

    if not os.path.isfile(src):
        return False

    img = cv2.imdecode(np.fromfile(src, dtype=np.uint8), cv2.IMREAD_UNCHANGED)
    if img is None:
        return False
    if img.ndim == 2:
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGRA)
    elif img.shape[2] == 3:
        img = cv2.cvtColor(img, cv2.COLOR_BGR2BGRA)

    h, w = img.shape[:2]
    corners = np.array([img[0,0], img[0,w-1], img[h-1,0], img[h-1,w-1]], dtype=np.float32)
    bg = np.mean(corners, axis=0)
    diff = np.linalg.norm(img.astype(np.float32) - bg.reshape(1,1,4), axis=2)
    alpha = img[:, :, 3]
    mask = (diff > 28) & (alpha > 10)
    ys, xs = np.where(mask)
    if len(xs) == 0 or len(ys) == 0:
        x1, y1, x2, y2 = 0, 0, w, h
    else:
        x1, x2 = int(xs.min()), int(xs.max()) + 1
        y1, y2 = int(ys.min()), int(ys.max()) + 1

    bw, bh = x2 - x1, y2 - y1
    pad = int(max(bw, bh) * pad_ratio)
    x1, y1 = max(0, x1 - pad), max(0, y1 - pad)
    x2, y2 = min(w, x2 + pad), min(h, y2 + pad)

    cx, cy = (x1 + x2) / 2.0, (y1 + y2) / 2.0
    side = max(x2 - x1, y2 - y1)
    sx1, sy1 = int(round(cx - side / 2)), int(round(cy - side / 2))
    sx2, sy2 = sx1 + side, sy1 + side
    if sx1 < 0:
        sx2 -= sx1; sx1 = 0
    if sy1 < 0:
        sy2 -= sy1; sy1 = 0
    if sx2 > w:
        sx1 -= (sx2 - w); sx2 = w
    if sy2 > h:
        sy1 -= (sy2 - h); sy2 = h
    sx1, sy1 = max(0, int(sx1)), max(0, int(sy1))
    sx2, sy2 = min(w, int(sx2)), min(h, int(sy2))

    crop = img[sy1:sy2, sx1:sx2]
    crop = cv2.resize(crop, (out_size, out_size), interpolation=cv2.INTER_AREA)

    # Flatten lên nền trắng.
    bgr = crop[:, :, :3].astype(np.float32)
    a = crop[:, :, 3:4].astype(np.float32) / 255.0
    white = np.full_like(bgr, 255.0)
    out = (bgr * a + white * (1 - a)).astype(np.uint8)

    Path(dst).parent.mkdir(parents=True, exist_ok=True)
    ok, enc = cv2.imencode(".png", out)
    if not ok:
        return False
    enc.tofile(dst)
    return True


def make_cropped_logo(src: str, dst: str) -> str:
    if os.path.isfile(dst):
        # Tạo lại mỗi lần để đúng theo logo mới nếu user thay ảnh.
        try:
            os.remove(dst)
        except Exception:
            pass

    ok = crop_with_pillow(src, dst) or crop_with_cv2(src, dst)
    if ok and os.path.isfile(dst):
        return dst

    # Fallback: nếu crop fail thì dùng ảnh gốc để web không vỡ.
    if os.path.isfile(src):
        try:
            Path(dst).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
            return dst
        except Exception:
            return src
    raise FileNotFoundError(f"Không tìm thấy logo: {src}")


def data_uri_png(path: str) -> str:
    data = Path(path).read_bytes()
    return "data:image/png;base64," + base64.b64encode(data).decode("ascii")


def remove_css_blocks(text: str) -> str:
    markers = [
        "/* ===================== DATN LOGO FILL FRAME FIX ===================== */",
        "/* ===================== DATN LOGO CROP EMBED FIX ===================== */",
        "/* ===================== DATN HEADER LOGO BIG FIX ===================== */",
    ]
    for marker in markers:
        while True:
            idx = text.find(marker)
            if idx < 0:
                break
            end = text.find("</style>", idx)
            if end < 0:
                text = text[:idx]
                break
            text = text[:idx] + text[end:]
    return text


def replace_logo_img(html: str, school_uri: str, faculty_uri: str) -> str:
    # Trường hợp đã có header mới với api/logo.
    html = re.sub(
        r'<img\s+src="[^"]*api/logo/school[^"]*"\s+alt="[^"]*"\s*>',
        f'<img src="{school_uri}" alt="Logo trường">',
        html,
        count=1,
        flags=re.IGNORECASE,
    )
    html = re.sub(
        r'<img\s+src="[^"]*api/logo/faculty[^"]*"\s+alt="[^"]*"\s*>',
        f'<img src="{faculty_uri}" alt="Logo khoa">',
        html,
        count=1,
        flags=re.IGNORECASE,
    )

    # Trường hợp đã embed data uri cũ.
    html = re.sub(
        r'<img\s+src="data:image/[^\"]+"\s+alt="Logo trường"\s*>',
        f'<img src="{school_uri}" alt="Logo trường">',
        html,
        count=1,
        flags=re.IGNORECASE,
    )
    html = re.sub(
        r'<img\s+src="data:image/[^\"]+"\s+alt="Logo khoa"\s*>',
        f'<img src="{faculty_uri}" alt="Logo khoa">',
        html,
        count=1,
        flags=re.IGNORECASE,
    )

    return html


def main() -> None:
    target = find_target()
    print(f"[PATCH] File dashboard: {target}")
    if not target.exists():
        raise FileNotFoundError(f"Không tìm thấy file: {target}")

    school_crop = make_cropped_logo(SCHOOL_LOGO, OUT_SCHOOL_LOGO)
    faculty_crop = make_cropped_logo(FACULTY_LOGO, OUT_FACULTY_LOGO)
    print(f"[PATCH] Logo trường crop: {school_crop}")
    print(f"[PATCH] Logo khoa crop:   {faculty_crop}")

    school_uri = data_uri_png(school_crop)
    faculty_uri = data_uri_png(faculty_crop)

    raw = target.read_text(encoding="utf-8")
    text = remove_css_blocks(raw)
    text = replace_logo_img(text, school_uri, faculty_uri)

    if "</style>" not in text:
        raise RuntimeError("Không tìm thấy </style> trong HTML_PAGE.")
    patched = text.replace("</style>", CSS_OVERRIDE + "\n</style>", 1)

    backup = target.with_suffix(target.suffix + time.strftime(".bak_logo_crop_%Y%m%d_%H%M%S"))
    backup.write_text(raw, encoding="utf-8")
    target.write_text(patched, encoding="utf-8")

    print(f"[PATCH] Đã tạo backup: {backup}")
    print("[DONE] Đã crop logo để phóng đều trong 4 góc, không dùng transform scale cắt mép.")
    print("[DONE] Chạy lại Python chính và nhấn Ctrl + F5 trên trình duyệt.")


if __name__ == "__main__":
    main()
