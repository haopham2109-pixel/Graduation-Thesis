# ============================================================
# FILE 1/3
# COPY VAO FILE:
# datn_object_detector_yolo_3_separate.py
#
# DATN - OBJECT DETECTOR YOLO 3 VAT - V39 REAL MASS
# Tam thoi dung 3 model YOLO rieng:
#   1) kimquat_sauce = muoi ot kim quat = 227g
#   2) red_chili     = ot bot = 104g
#   3) orange        = trai cam = 150g tam thoi, sua lai neu can dung so can that
#
# Ghi chu quan trong:
# - Model OT_BOT co the hien class name ben trong la kimquat_sauce do data.yaml cu.
# - Code nay KHONG lay ten class tu model.names.
# - Code ep nhan model OT_BOT la label "red_chili".
# - Sau nay train 1 YOLO chung 3 class thi thay class nay bang detector multi-class.
# ============================================================

import cv2
import time
from pathlib import Path
from dataclasses import dataclass
from collections import deque, Counter

try:
    import torch
except Exception:
    torch = None

try:
    from ultralytics import YOLO
except Exception:
    YOLO = None


VALID_OBJECT_LABELS = ("kimquat_sauce", "red_chili", "orange")


@dataclass
class ObjectResult:
    label: str = "none"          # raw label
    stable_label: str = "none"   # label sau khi vote nhieu frame
    score: float = 0.0
    bbox: tuple = None           # x, y, w, h
    mass_g: float = 0.0
    mu: float = 0.55
    safety_factor: float = 2.0
    min_force_n: float = 0.0
    target_force_n: float = 0.0
    max_force_n: float = 0.0
    hard_limit_n: float = 0.0
    reason: str = ""

#=============== 5. YOLO nhận dạng vật thể ======================
# nhận dạng vật thể bằng YOLO. Detector chỉ xử lý mỗi vài frame để giảm tải
# giới hạn vùng ROI và dùng history để ổn định kết quả nhận dạng.
class Yolo3SeparateObjectDetector:
    def __init__(
        self,
        roi=(40, 120, 620, 460),
        process_every_n=2,
        stable_window=7,
        stable_min_count=4,
        show_roi=True,
        require_center_in_roi=True,
        model_configs=None,
    ):
        self.roi = tuple(int(v) for v in roi)
        self.process_every_n = max(1, int(process_every_n))
        self.stable_window = max(3, int(stable_window))
        self.stable_min_count = max(2, int(stable_min_count))
        self.show_roi = bool(show_roi)
        self.require_center_in_roi = bool(require_center_in_roi)

        self.frame_id = 0
        self.history = deque(maxlen=self.stable_window)
        self.last_result = ObjectResult()
        self.last_process_time = 0.0
        self.last_candidates = {}

        self.device = 0 if (torch is not None and torch.cuda.is_available()) else "cpu"
#=============== END 5. YOLO nhận dạng vật thể ======================

#================ 6. Bảng vật, khối lượng, hệ số ma sát =================
        self.object_cfg = {
            "kimquat_sauce": {
                "mass_g": 227.0,
                "mu": 0.5,
                "safety_factor": 2.0,
                "safe_min_ratio": 0.85,
                "safe_max_ratio": 1.18,
                "hard_limit_n": 12.0,
                "display": "MUOI OT KIM QUAT",
            },
            "red_chili": {
                "mass_g": 104.0,
                "mu": 0.227,
                "safety_factor": 2.0,
                "safe_min_ratio": 0.82,
                "safe_max_ratio": 1.20,
                "hard_limit_n": 6.0,
                "display": "OT DO / OT BOT",
            },
            "orange": {
                "mass_g": 200.0,
                "mu": 0.6,
                "safety_factor": 2.0,
                "safe_min_ratio": 0.85,
                "safe_max_ratio": 1.15,
                "hard_limit_n": 9.0,
                "display": "TRAI CAM",
            },
        }
#================ END 6. Bảng vật, khối lượng, hệ số ma sát =================
        if model_configs is None:
            model_configs = self.default_model_configs()

        self.models = []
        self.load_models(model_configs)

    def default_model_configs(self):
        # ===================== 3 MODEL DA CHOT =====================
        kimquat_path = r"C:\Users\admin\Downloads\muoi_ot_kim_quat\runs\detect\train_kimquat_sauce_yolov8l_1814_img800_ram\weights\best.pt"

        # OT_BOT model: ben trong co the ghi class name sai la kimquat_sauce,
        # nhung code se ep label output la red_chili.
        red_chili_path = r"C:\Users\admin\Downloads\OT_BOT\runs\detect\train_kimquat_sauce_yolov8m_otbot\weights\best.pt"

        orange_path = r"C:\Users\admin\Downloads\trai_cam\runs\detect\train_orange_yolov8m_v1_ram\weights\best.pt"

        return [
            {
                "label": "kimquat_sauce",
                "model_path": kimquat_path,
                "conf": 0.60,
                "imgsz": 800,
                "color": (0, 255, 255),
                "priority": 3,
            },
            {
                "label": "red_chili",
                "model_path": red_chili_path,
                # Test rieng thay ~0.86, nen 0.65 an toan hon 0.90.
                # Neu nham vat khac, tang len 0.75/0.80.
                "conf": 0.65,
                "imgsz": 640,
                "color": (0, 0, 255),
                "priority": 2,
            },
            {
                "label": "orange",
                "model_path": orange_path,
                "conf": 0.60,
                "imgsz": 640,
                "color": (0, 160, 255),
                "priority": 2,
            },
        ]

    def load_models(self, model_configs):
        if YOLO is None:
            print("[YOLO 3OBJ] ERROR: Chua cai ultralytics. Chay: pip install ultralytics")
            return

        for cfg in model_configs:
            label = str(cfg.get("label", "unknown"))
            model_path = Path(str(cfg.get("model_path", "")))

            if not model_path.exists():
                print(f"[YOLO 3OBJ] SKIP {label}: khong thay model: {model_path}")
                continue

            try:
                model = YOLO(str(model_path))
                info = {
                    "label": label,
                    "model": model,
                    "path": model_path,
                    "conf": float(cfg.get("conf", 0.60)),
                    "imgsz": int(cfg.get("imgsz", 640)),
                    "color": cfg.get("color", (0, 255, 0)),
                    "priority": int(cfg.get("priority", 1)),
                }
                self.models.append(info)
                print(f"[YOLO 3OBJ] Loaded {label}: {model_path} | conf={info['conf']} imgsz={info['imgsz']}")
            except Exception as e:
                print(f"[YOLO 3OBJ] Load FAIL {label}: {e}")

        if not self.models:
            print("[YOLO 3OBJ] WARNING: Chua load duoc model nao.")

    def get_roi(self, frame_shape):
        H, W = frame_shape[:2]
        x1, y1, x2, y2 = self.roi
        x1 = max(0, min(W - 1, int(x1)))
        y1 = max(0, min(H - 1, int(y1)))
        x2 = max(x1 + 1, min(W, int(x2)))
        y2 = max(y1 + 1, min(H, int(y2)))
        return x1, y1, x2, y2

    def bbox_center_inside_roi(self, bbox, frame_shape):
        if bbox is None:
            return False
        x, y, w, h = bbox
        cx = x + w * 0.5
        cy = y + h * 0.5
        x1, y1, x2, y2 = self.get_roi(frame_shape)
        return x1 <= cx <= x2 and y1 <= cy <= y2

    @staticmethod
    def xywh_to_xyxy(box):
        if box is None:
            return None
        x, y, w, h = box
        return x, y, x + w, y + h

    @staticmethod
    def calc_iou_xywh(box_a, box_b):
        if box_a is None or box_b is None:
            return 0.0

        ax1, ay1, ax2, ay2 = Yolo3SeparateObjectDetector.xywh_to_xyxy(box_a)
        bx1, by1, bx2, by2 = Yolo3SeparateObjectDetector.xywh_to_xyxy(box_b)

        ix1 = max(ax1, bx1)
        iy1 = max(ay1, by1)
        ix2 = min(ax2, bx2)
        iy2 = min(ay2, by2)

        iw = max(0, ix2 - ix1)
        ih = max(0, iy2 - iy1)
        inter = iw * ih

        area_a = max(0, ax2 - ax1) * max(0, ay2 - ay1)
        area_b = max(0, bx2 - bx1) * max(0, by2 - by1)
        union = area_a + area_b - inter

        if union <= 0:
            return 0.0

        return inter / union
#================= 7. Tính lực kẹp mục tiêu từ khối lượng vật ==========
    def compute_force_info(self, label):
        if label not in self.object_cfg:
            return {
                "mass_g": 0.0,
                "mu": 0.55,
                "safety_factor": 2.0,
                "target_force_n": 0.0,
                "min_force_n": 0.0,
                "max_force_n": 0.0,
                "hard_limit_n": 0.0,
            }

        cfg = self.object_cfg[label]
        mass_kg = cfg["mass_g"] / 1000.0
        weight_n = mass_kg * 9.81
        target = weight_n * cfg["safety_factor"] / max(cfg["mu"], 1e-6)

        return {
            "mass_g": cfg["mass_g"],
            "mu": cfg["mu"],
            "safety_factor": cfg["safety_factor"],
            "target_force_n": target,
            "min_force_n": target * cfg["safe_min_ratio"],
            "max_force_n": target * cfg["safe_max_ratio"],
            "hard_limit_n": cfg["hard_limit_n"],
        }
#================= END 7. Tính lực kẹp mục tiêu từ khối lượng vật ==========
    def stabilize_label(self, raw_label):
        self.history.append(raw_label)
        label, count = Counter(self.history).most_common(1)[0]

        if label != "none" and count >= self.stable_min_count:
            return label

        return "none"

    def detect_one_model(self, info, frame):
        model = info["model"]
        fixed_label = info["label"]
        conf = info["conf"]
        imgsz = info["imgsz"]

        try:
            results = model.predict(
                frame,
                imgsz=imgsz,
                conf=conf,
                iou=0.45,
                verbose=False,
                device=self.device,
            )
        except Exception as e:
            return ObjectResult(label="none", reason=f"{fixed_label}: predict error {e}")

        boxes = results[0].boxes
        best = ObjectResult(label="none", score=0.0, bbox=None, reason=f"{fixed_label}: no box")

        if boxes is None or len(boxes) == 0:
            return best

        H, W = frame.shape[:2]

        for b in boxes:
            score = float(b.conf[0])
            x1, y1, x2, y2 = map(int, b.xyxy[0].cpu().numpy())

            x1 = max(0, min(W - 1, x1))
            y1 = max(0, min(H - 1, y1))
            x2 = max(0, min(W - 1, x2))
            y2 = max(0, min(H - 1, y2))

            w = max(0, x2 - x1)
            h = max(0, y2 - y1)

            if w <= 8 or h <= 8:
                continue

            bbox = (x1, y1, w, h)

            if self.require_center_in_roi and not self.bbox_center_inside_roi(bbox, frame.shape):
                continue

            if score > best.score:
                best = ObjectResult(
                    label=fixed_label,
                    score=score,
                    bbox=bbox,
                    reason=f"{fixed_label} conf={score:.2f}",
                )

        return best

    def choose_candidate(self, candidates):
        valid = [r for r in candidates.values() if r.label != "none" and r.bbox is not None]

        if not valid:
            return ObjectResult(label="none", stable_label="none", reason="no object")

        kim = candidates.get("kimquat_sauce", ObjectResult())
        red = candidates.get("red_chili", ObjectResult())
        org = candidates.get("orange", ObjectResult())

        # Neu model kimquat va red_chili cung bat cung vung, uu tien kimquat.
        # Ly do: model red_chili/OT_BOT tung co class name sai va co nguy co nham chai.
        if red.label == "red_chili" and kim.label == "kimquat_sauce":
            iou = self.calc_iou_xywh(red.bbox, kim.bbox)
            if iou >= 0.12 and kim.score >= 0.45:
                kim.reason = f"choose kimquat_sauce by veto red_chili | iou={iou:.2f} kim={kim.score:.2f} red={red.score:.2f}"
                return kim

        # Neu red_chili va orange cung bat vung do, uu tien orange.
        if red.label == "red_chili" and org.label == "orange":
            iou = self.calc_iou_xywh(red.bbox, org.bbox)
            if iou >= 0.12 and org.score >= 0.45:
                org.reason = f"choose orange by veto red_chili | iou={iou:.2f} orange={org.score:.2f} red={red.score:.2f}"
                return org

        # Neu 2 model cung bat nhung khong overlap, lay score cao nhat.
        # Score boost nhe theo priority de kimquat/chai chinh it bi model khac tranh.
        score_map = {m["label"]: m.get("priority", 1) for m in self.models}

        def rank_score(r):
            priority = score_map.get(r.label, 1)
            return float(r.score) + 0.015 * priority

        valid.sort(key=rank_score, reverse=True)
        return valid[0]
#=================8. Ổn định và khóa vật YOLO ==========
# Kết quả YOLO từng frame có thể dao động, nên nhóm dùng voting nhiều frame.
#  Chỉ khi một label xuất hiện đủ số lần trong cửa sổ history, vật mới được xem là ổn định và được dùng để tính lực kẹp.
    def update(self, frame):
        self.frame_id += 1

        if frame is None:
            self.last_result = ObjectResult(label="none", stable_label="none")
            return self.last_result

        if self.frame_id % self.process_every_n != 0:
            return self.last_result

        t0 = time.time()
        candidates = {}

        for info in self.models:
            candidates[info["label"]] = self.detect_one_model(info, frame)

        self.last_candidates = candidates

        best = self.choose_candidate(candidates)
        stable_label = self.stabilize_label(best.label)

        # Chi tinh mass/force khi da stable, tranh nhay mass lung tung.
        force = self.compute_force_info(stable_label)

        result = ObjectResult(
            label=best.label,
            stable_label=stable_label,
            score=best.score,
            bbox=best.bbox,
            mass_g=force["mass_g"],
            mu=force["mu"],
            safety_factor=force["safety_factor"],
            min_force_n=force["min_force_n"],
            target_force_n=force["target_force_n"],
            max_force_n=force["max_force_n"],
            hard_limit_n=force["hard_limit_n"],
            reason=best.reason,
        )

        self.last_result = result
        self.last_process_time = time.time() - t0
        return result
#================= END 8. Ổn định và khóa vật YOLO ==========
    def draw_overlay(self, frame, result: ObjectResult):
        if frame is None:
            return frame

        out = frame

        if self.show_roi:
            x1, y1, x2, y2 = self.get_roi(out.shape)
            cv2.rectangle(out, (x1, y1), (x2, y2), (255, 120, 0), 2)
            cv2.putText(
                out,
                "YOLO 3 OBJECT ROI",
                (x1 + 8, max(25, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.60,
                (255, 120, 0),
                2,
            )

        if result.bbox is not None and result.label != "none":
            x, y, w, h = result.bbox

            if result.label == "kimquat_sauce":
                color = (0, 255, 255)
            elif result.label == "red_chili":
                color = (0, 0, 255)
            elif result.label == "orange":
                color = (0, 160, 255)
            else:
                color = (0, 255, 0)

            cv2.rectangle(out, (x, y), (x + w, y + h), color, 2)

            raw_text = f"{result.label.upper()} {result.score:.2f}"
            cv2.putText(
                out,
                raw_text,
                (x, max(25, y - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.65,
                color,
                2,
            )

        if result.stable_label != "none":
            cfg = self.object_cfg.get(result.stable_label, {})
            display_name = cfg.get("display", result.stable_label.upper())
            txt = f"OBJECT LOCKED: {display_name} | {result.mass_g:.0f}g | TGT {result.target_force_n:.2f}N"
            col = (0, 255, 0)
        else:
            txt = "OBJECT: SEARCHING YOLO 3 OBJECTS"
            col = (160, 160, 160)

        cv2.putText(out, txt, (20, 455), cv2.FONT_HERSHEY_SIMPLEX, 0.52, col, 2)

        return out
